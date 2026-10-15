"""Build the selected B exterior colours from immutable Git input; offline only."""
import argparse
from collections import Counter, deque
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess

from PIL import Image, __version__ as pillow_version

SOURCE_COMMIT = "100c181e91a6edf0dc673fb94645e40b4b20f2c0"
LAYOUTS = (
    ("HeavyShieldSoldierAtlas.png", 64, 48,
     "7fece0bbfb56bcb6f4c8f6443a3ee655a0ccd5654b795317ee2c58a0dc51b94c",
     "28c5ee55a32dc71691f9cf69d5b8060dba30a78e67075490965e7d25fd76b7c3"),
    ("HeavyShieldSoldierAtlasCoarse.png", 48, 32,
     "923c2f72940168d075ac098c06bed19b14f49c62388ad41d91cae2584fc894a3",
     "03214270d2345fde0c0764272d10e7d052078d8a37b2d75c3a7a86a46735409a"),
)
N4 = ((-1, 0), (1, 0), (0, -1), (0, 1))
N8 = tuple((dx, dy) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dx or dy)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def luminance(rgb):
    return (2126 * rgb[0] + 7152 * rgb[1] + 722 * rgb[2]) / 10000


def group(rgb):
    r, g, b = rgb
    if max(rgb) - min(rgb) <= 35:
        return "metal"
    if b > r and b >= g:
        return "blue"
    if r > g and g > b:
        return "leather_or_skin"
    if min(g, b) > 1.3 * r:
        return "teal"
    return "other"


def candidates(cell):
    width, height = cell.size
    pixels = cell.load()
    exterior, queue = set(), deque()
    for y in range(height):
        for x in range(width):
            if (x in (0, width - 1) or y in (0, height - 1)) and pixels[x, y][3] == 0:
                exterior.add((x, y))
                queue.append((x, y))
    while queue:
        x, y = queue.popleft()
        for dx, dy in N4:
            nx, ny = x + dx, y + dy
            if (0 <= nx < width and 0 <= ny < height and (nx, ny) not in exterior
                    and pixels[nx, ny][3] == 0):
                exterior.add((nx, ny))
                queue.append((nx, ny))
    return {(x, y) for y in range(height) for x in range(width)
            if pixels[x, y] == (0, 0, 0, 255)
            and any((x + dx, y + dy) in exterior for dx, dy in N8)}


def material(cell, point):
    pixels, (width, height), (x, y) = cell.load(), cell.size, point
    donors = []
    for dy in range(-3, 4):
        for dx in range(-3, 4):
            distance, nx, ny = dx * dx + dy * dy, x + dx, y + dy
            if 0 < distance <= 9 and 0 <= nx < width and 0 <= ny < height:
                value = pixels[nx, ny]
                if (value[3] == 255 and value[:3] != (0, 0, 0)
                        and 20 <= luminance(value[:3]) <= 180 and max(value[:3]) <= 215):
                    donors.append((distance, value[:3]))
    if not donors:
        return None, "no_bounded_nonhighlight_material"
    minimum = min(distance for distance, _ in donors)
    groups, colors = Counter(), Counter()
    for distance, rgb in donors:
        if distance <= minimum + 2:
            weight = 2520 // distance
            groups[group(rgb)] += weight
            colors[rgb] += weight
    winner, weight = sorted(groups.items(), key=lambda p: (-p[1], p[0]))[0]
    if weight < 0.60 * sum(groups.values()):
        return None, "ambiguous_nearby_material"
    color = sorted(((rgb, weight) for rgb, weight in colors.items() if group(rgb) == winner),
                   key=lambda p: (-p[1], p[0]))[0][0]
    return color, winner


def shade(rgb):
    dark = tuple(round(channel * 0.68) for channel in rgb)
    if luminance(dark) > 64:
        scale = 64 / luminance(dark)
        dark = tuple(int(channel * scale) for channel in dark)
    return dark


def transform(original, width, height):
    result = original.copy()
    totals, retained = Counter(), Counter()
    for frame in range(512):
        x, y = frame % 16 * width, frame // 16 * height
        cell = original.crop((x, y, x + width, y + height))
        occupied = cell.getchannel("A").getbbox() is not None
        if occupied != (frame < 509):
            raise ValueError(f"Unexpected occupied/empty cell: {frame}")
        totals["occupiedFrames"] += occupied
        for px, py in sorted(candidates(cell)):
            totals["candidatePixels"] += 1
            rgb, reason = material(cell, (px, py))
            if rgb is None:
                retained[reason] += 1
                continue
            result.putpixel((x + px, y + py), (*shade(rgb), 255))
            totals["changedPixels"] += 1
    if result.getchannel("A").tobytes() != original.getchannel("A").tobytes():
        raise ValueError("Alpha changed")
    return result, {**totals, "retainedCandidates": dict(sorted(retained.items()))}


def build(output, repo):
    # Do not resolve away symbolic components before checking them.
    output = Path(os.path.abspath(output))
    if any(path.is_symlink() for path in (output, *output.parents)):
        raise ValueError("Output and its parents must not be symbolic links")
    if output.exists():
        raise ValueError("Output must be a new directory, including for a repeat run")
    if not output.parent.is_dir():
        raise ValueError("Output parent must already exist")
    files, images = {}, {}
    for name, width, height, file_sha, rgba_sha in LAYOUTS:
        blob = subprocess.check_output([
            "git", "-C", str(repo), "show", f"{SOURCE_COMMIT}:il2cpp/Assets/{name}"])
        if digest(blob) != file_sha:
            raise ValueError(f"Unrecognized baseline: {name}")
        with Image.open(io.BytesIO(blob)) as original:
            if (original.mode != "RGBA" or original.size != (width * 16, height * 32)
                    or digest(original.tobytes()) != rgba_sha):
                raise ValueError(f"Wrong decoded baseline: {name}")
            result, counts = transform(original, width, height)
        encoded = io.BytesIO()
        result.save(encoded, format="PNG")
        files[name] = encoded.getvalue()
        images[name] = {"sourceBlobSha256": file_sha, "sourceRgbaSha256": rgba_sha,
                        "sha256": digest(files[name]), "rgbaSha256": digest(result.tobytes()),
                        "cell": [width, height], **counts}
    manifest = {"sourceCommit": SOURCE_COMMIT, "selection": "B", "pillow": pillow_version,
                "scope": "Exterior opaque pure-black RGB only; unchanged alpha and frame layout",
                "parameters": {"transparentConnectivity": 4, "exteriorAdjacency": 8,
                               "donorRadius": 3, "donorLuminance": [20, 180],
                               "donorMaximumChannel": 215, "nearestSquaredDistanceSlack": 2,
                               "inverseDistanceNumerator": 2520, "materialWeightMinimum": 0.60,
                               "rgbFactor": 0.68, "luminanceCap": 64}, "images": images}
    files["manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
    output.mkdir()
    for name, data in files.items():
        with (output / name).open("xb") as destination:
            destination.write(data)
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True,
                        help="New explicit output directory; never deploys or starts the game")
    args = parser.parse_args()
    build(args.output, Path(__file__).resolve().parents[2])
