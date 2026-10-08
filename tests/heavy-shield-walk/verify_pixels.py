"""Independent pixel regressions; these do not substitute for a gait review."""
import argparse
from collections import deque
import hashlib
import io
import json
from pathlib import Path
import subprocess

from PIL import Image

BASE = "2df52f38910f44fdbbd2670fc27e687100853222"
TARGETS = {start + frame for start in (4, 95, 186, 277) for frame in range(6)}
LAYOUTS = [("HeavyShieldSoldierAtlas.png", 64, 48, 33),
           ("HeavyShieldSoldierAtlasCoarse.png", 48, 32, 22)]


def floating_boots(cell, leg_y):
    """Find opaque 8-connected boot components detached from the upper body."""
    pix = cell.load()
    width, height = cell.size
    seen = set()
    islands = []
    for y in range(height):
        for x in range(width):
            if (x, y) in seen or not pix[x, y][3]:
                continue
            queue = deque([(x, y)])
            seen.add((x, y))
            points = []
            while queue:
                px, py = queue.popleft()
                points.append((px, py))
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = px + dx, py + dy
                        if ((dx or dy) and 0 <= nx < width and 0 <= ny < height
                                and (nx, ny) not in seen and pix[nx, ny][3]):
                            seen.add((nx, ny))
                            queue.append((nx, ny))
            brown = sum(1 for px, py in points if py >= leg_y
                        and pix[px, py][0] > 70
                        and pix[px, py][0] > pix[px, py][1] * 1.25
                        and pix[px, py][1] > pix[px, py][2] * 1.25)
            if brown >= 3 and min(py for px, py in points) >= leg_y:
                islands.append({"pixels": len(points), "brown": brown})
    return islands


def check(repo, assets):
    errors, results = [], {}
    for name, width, height, leg_y in LAYOUTS:
        source = subprocess.check_output([
            "git", "-C", str(repo), "show", f"{BASE}:il2cpp/Assets/{name}"])
        old = Image.open(io.BytesIO(source)).convert("RGBA")
        new = Image.open(assets / name)
        if new.mode != "RGBA" or new.size != old.size:
            errors.append(f"{name}: RGBA/layout mismatch")
            continue
        palette = set(old.getdata())
        changed, detached = [], []
        for slot in range(512):
            x, y = slot % 16 * width, slot // 16 * height
            box = (x, y, x + width, y + height)
            before, after = old.crop(box), new.crop(box)
            if before.tobytes() != after.tobytes():
                changed.append(slot)
            if slot not in TARGETS:
                continue
            if before.crop((0, 0, width, leg_y)).tobytes() != after.crop((0, 0, width, leg_y)).tobytes():
                errors.append(f"{name} slot{slot}: upper body changed")
            def bottom(frame):
                return max(py for py in range(height) for px in range(width)
                           if frame.getpixel((px, py))[3])
            if bottom(before) != bottom(after):
                errors.append(f"{name} slot{slot}: support row changed")
            islands = floating_boots(after, leg_y)
            if islands:
                detached.append({"slot": slot, "islands": islands})
            # Independently identified sixth-frame shield rim and outline that
            # the rejected fixed-row mask erased. Coordinates are cell-local.
            if width == 64 and slot in (9, 100, 191):
                points = ((21, 34), (20, 36))
                # Worn shield has one further dark outer-outline pixel; its
                # color also occurs on trousers, so do not freeze all of it.
                if slot == 100:
                    points += ((22, 34),)
                if slot == 191:
                    points += ((22, 35),)
                for point in points:
                    if before.getpixel(point) != after.getpixel(point):
                        errors.append(f"{name} slot{slot}: shield rim {point} changed")
        if set(changed) != TARGETS:
            errors.append(f"{name}: wrong edited cells {changed}")
        if detached:
            errors.append(f"{name}: detached boots {detached}")
        invalid = sum(1 for rgba in new.getdata() if rgba not in palette
                      or rgba[3] not in (0, 255)
                      or (rgba[3] == 0 and rgba[:3] != (0, 0, 0)))
        if invalid:
            errors.append(f"{name}: {invalid} invalid palette/alpha pixels")
        results[name] = {"changedCells": changed, "unchangedCells": 512 - len(changed),
                         "detachedBoots": detached,
                         "sha256": hashlib.sha256((assets / name).read_bytes()).hexdigest()}
    return {"passed": not errors, "errors": errors, "images": results,
            "boundary": "Offline pixel scope, palette, upper-body, known shield rim, support row and boot topology. Full shield/anatomy/9fps gait require independent visual review; no game acceptance."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path)
    parser.add_argument("--evidence", type=Path, required=True)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    report = check(repo, args.assets or repo / "il2cpp/Assets")
    args.evidence.mkdir(parents=True, exist_ok=True)
    (args.evidence / "pixels.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    raise SystemExit(not report["passed"])
