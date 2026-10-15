"""Independent outline scope, selected-B pixels, and adversarial regressions."""
import argparse
from collections import deque
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw

BASE = "100c181e91a6edf0dc673fb94645e40b4b20f2c0"
LAYOUTS = (
    ("HeavyShieldSoldierAtlas.png", 64, 48, 33,
     "7fece0bbfb56bcb6f4c8f6443a3ee655a0ccd5654b795317ee2c58a0dc51b94c",
     "28c5ee55a32dc71691f9cf69d5b8060dba30a78e67075490965e7d25fd76b7c3",
     "9c52b4eb99e0170e53bd535f600d0f7ada2834ff17a88f20f7781e3b2272fd12", 45683),
    ("HeavyShieldSoldierAtlasCoarse.png", 48, 32, 22,
     "923c2f72940168d075ac098c06bed19b14f49c62388ad41d91cae2584fc894a3",
     "03214270d2345fde0c0764272d10e7d052078d8a37b2d75c3a7a86a46735409a",
     "1def2baaae8a24ee123d734bf62ace505852f735700e7e530b99845d145a1b4f", 25035),
)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def baseline(repo, spec):
    name, width, height, _, file_sha, rgba_sha, *_ = spec
    blob = subprocess.check_output([
        "git", "-C", str(repo), "show", f"{BASE}:il2cpp/Assets/{name}"])
    if digest(blob) != file_sha:
        raise ValueError(f"Unrecognized Git baseline: {name}")
    image = Image.open(io.BytesIO(blob))
    if image.mode != "RGBA" or image.size != (width * 16, height * 32) or digest(image.tobytes()) != rgba_sha:
        raise ValueError(f"Wrong decoded baseline: {name}")
    return image


def exterior(cell):
    # Independent of the generator's per-cell queue: padded Pillow flood-fill.
    w, h = cell.size
    mask = Image.new("L", (w + 2, h + 2), 1)
    mask.paste(cell.getchannel("A").point(lambda a: int(a == 0)), (1, 1))
    ImageDraw.floodfill(mask, (0, 0), 2)
    return {(x, y) for y in range(h) for x in range(w)
            if any(mask.getpixel((x + 1 + dx, y + 1 + dy)) == 2
                   for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                   if (dx or dy) and 0 <= x + dx < w and 0 <= y + dy < h)}


def shoe_components(cell, leg_y):
    """Exact lower-leg/boot opacity component coordinates, including support row."""
    width, height = cell.size
    alpha = cell.getchannel("A")
    remaining = {(x, y) for y in range(leg_y, height) for x in range(width)
                 if alpha.getpixel((x, y))}
    components = []
    while remaining:
        start = min(remaining)
        remaining.remove(start)
        queue, component = deque([start]), [start]
        while queue:
            x, y = queue.popleft()
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    point = (x + dx, y + dy)
                    if point in remaining:
                        remaining.remove(point)
                        component.append(point)
                        queue.append(point)
        components.append(tuple(sorted(component)))
    return sorted(components)


def inspect(source, result, spec):
    name, width, height, leg_y, _, _, selected_sha, expected_changes = spec
    errors, changed, occupied = set(), 0, 0
    if result.mode != "RGBA" or result.size != source.size:
        return {"layout"}, {}
    if result.getchannel("A").tobytes() != source.getchannel("A").tobytes():
        errors.add("alpha")
    for slot in range(512):
        x, y = slot % 16 * width, slot // 16 * height
        box = (x, y, x + width, y + height)
        before, after = source.crop(box), result.crop(box)
        before_alpha, after_alpha = before.getchannel("A"), after.getchannel("A")
        occupied += bool(after_alpha.getbbox())
        if slot >= 509 and after.tobytes() != before.tobytes():
            errors.add("empty_tail")
        if before_alpha.getbbox() != after_alpha.getbbox():
            errors.add("bbox_support_row")
        if shoe_components(before, leg_y) != shoe_components(after, leg_y):
            errors.add("shoe_topology")
        outside = exterior(before)
        for py in range(height):
            for px in range(width):
                old, new = before.getpixel((px, py)), after.getpixel((px, py))
                if old == new:
                    continue
                changed += 1
                if old == (0, 0, 0, 255):
                    if (px, py) not in outside:
                        errors.add("interior_black")
                elif old[3] == 0:
                    errors.add("transparent_rgba")
                else:
                    errors.add("nonblack_or_nonopaque")
    if occupied != 509:
        errors.add("occupied_frame_count")
    if changed != expected_changes:
        errors.add("changed_pixel_count")
    rgba_sha = digest(result.tobytes())
    if rgba_sha != selected_sha:
        errors.add("selected_B_rgba")
    return errors, {"changedPixels": changed, "occupiedFrames": occupied,
                    "emptyTailCells": 3, "rgbaSha256": rgba_sha,
                    "alphaNoncandidateInteriorTransparentRgbaUnchanged": not errors.intersection(
                        {"alpha", "interior_black", "transparent_rgba", "nonblack_or_nonopaque"}),
                    "bboxSupportRowsAndShoeTopologyUnchanged": not errors.intersection(
                        {"alpha", "bbox_support_row", "shoe_topology", "empty_tail", "occupied_frame_count"})}


def mutation_checks(source, result, spec):
    width, height = spec[1:3]
    first = source.crop((0, 0, width, height))
    outside = exterior(first)
    points = {
        "alpha": next((x, y) for y in range(height) for x in range(width) if first.getpixel((x, y))[3]),
        "interior_black": next((x, y) for y in range(height) for x in range(width)
                               if first.getpixel((x, y)) == (0, 0, 0, 255) and (x, y) not in outside),
        "nonblack_or_nonopaque": next((x, y) for y in range(height) for x in range(width)
                                      if first.getpixel((x, y))[3] == 255
                                      and first.getpixel((x, y))[:3] != (0, 0, 0) and (x, y) in outside),
        "empty_tail": (15 * width, 31 * height),
    }
    checks = []
    for expected, point in points.items():
        mutant = result.copy()
        old = mutant.getpixel(point)
        value = (*old[:3], 254) if expected == "alpha" else (1, 2, 3, 255)
        mutant.putpixel(point, value)
        errors, _ = inspect(source, mutant, spec)
        if expected not in errors:
            raise AssertionError(f"Mutation escaped its specific scope check: {expected}")
        checks.append({"mutation": expected, "rejectedBySpecificCheck": True})
    return checks


def output_checks(repo, evidence, assets):
    """Exercise the actual generator CLI, including its exclusive output boundary."""
    generator = repo / "tools/heavy_shield_outline/build_atlases.py"
    with tempfile.TemporaryDirectory(prefix="generator-check-", dir=evidence) as temporary:
        root = Path(temporary)
        output = root / "new"
        command = [sys.executable, "-B", str(generator), "--output", str(output)]
        first = subprocess.run(command, capture_output=True, text=True)
        if first.returncode:
            raise AssertionError(f"Fresh generation failed: {first.stderr}")
        for filename in (LAYOUTS[0][0], LAYOUTS[1][0]):
            with Image.open(output / filename) as fresh, Image.open(assets / filename) as supplied:
                if (fresh.mode, fresh.size, fresh.tobytes()) != (supplied.mode, supplied.size, supplied.tobytes()):
                    raise AssertionError("Generator fresh output RGBA differs")
        before = {p.name: p.read_bytes() for p in output.iterdir()}
        repeat = root / "repeat"
        second = subprocess.run(command[:-1] + [str(repeat)], capture_output=True, text=True)
        if second.returncode or before != {p.name: p.read_bytes() for p in repeat.iterdir()}:
            raise AssertionError("Fresh outputs/manifest differ between independent runs")
        empty = root / "empty"
        empty.mkdir()
        for path in (output, root, empty):
            completed = subprocess.run(command[:-1] + [str(path)], capture_output=True, text=True)
            if completed.returncode == 0:
                raise AssertionError("Existing directory unexpectedly accepted")
        link = root / "link"
        link.symlink_to(output, target_is_directory=True)
        linked = subprocess.run(command[:-1] + [str(link)], capture_output=True, text=True)
        parent_linked = subprocess.run(command[:-1] + [str(link / "child")], capture_output=True, text=True)
        if linked.returncode == 0 or parent_linked.returncode == 0:
            raise AssertionError("Symbolic output/ancestor unexpectedly accepted")
        if before != {p.name: p.read_bytes() for p in output.iterdir()}:
            raise AssertionError("Refused invocation modified output")
    return {"freshOutputExact": True, "freshManifestAndImagesRepeatExact": True,
            "existingOutputRejectedWithoutChanges": True,
            "symbolicOutputAndParentRejected": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--self-test", action="store_true", help="Also reject four mutations per atlas and test generator output safety")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    report = {"passed": True, "sourceCommit": BASE, "images": {}, "errors": [],
              "boundary": "Offline selected-B pixel and shape proof; no gait, native material or gameplay acceptance."}
    args.evidence.mkdir(parents=True, exist_ok=True)
    for spec in LAYOUTS:
        source = baseline(repo, spec)
        with Image.open(args.assets / spec[0]) as result:
            errors, row = inspect(source, result, spec)
            row["sha256"] = digest((args.assets / spec[0]).read_bytes())
            if args.self_test and not errors:
                row["adversarialChecks"] = mutation_checks(source, result, spec)
        report["images"][spec[0]] = row
        report["errors"].extend(f"{spec[0]}: {e}" for e in sorted(errors))
    if args.self_test and not report["errors"]:
        report["generatorChecks"] = output_checks(repo, args.evidence, args.assets)
    report["passed"] = not report["errors"]
    (args.evidence / "pixels.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return int(not report["passed"])


if __name__ == "__main__":
    raise SystemExit(main())
