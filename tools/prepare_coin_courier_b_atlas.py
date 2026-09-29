#!/usr/bin/env python3
"""Prepare the coin-courier B atlas from the operator-generated pose sheet.

Fixed-spec technical extraction for one pinned source image. It never redraws the character
and never chroma-keys: the sheet's own alpha channel is authoritative.

Spec (reviewed 2026-09-27):
  * source: artifacts/coin-courier-blueprint-b-20260927/animation-draft/generated-sheet.png,
    RGBA, sha256 5d8daf04... pinned; original file is never modified;
  * coverage = alpha >= 128; alpha below 128 is low-alpha noise and is cleaned to 0
    (the raw run-row row band carries faint A<128 noise that must not become geometry);
  * grid: 8x4 cells; cell boundaries are rounded per grid edge (fractional 221.75 px pitch)
    plus two explicit cuts where the nominal boundary sliced real art:
      row 1 boundary 5 -> x 1121  (nominal 1109 cut frame 12's reach / frame 13's left)
      row 2 boundary 3 -> x 681   (nominal 665 cut frame 18's coin hand / frame 19's left)
  * all frames scaled by ONE nearest-neighbour factor 32/179 (stance median height 179 px,
    measured on the cleaned alpha); no per-frame stretch;
  * output cells 56x56 at PPU 32, pivot (28,12) bottom-origin; per-row ground line = lowest
    frame bottom of that row; each frame keeps its drawn elevation above its row ground and
    the elevation is never clamped - a frame that does not fit fails;
  * horizontal placement: per-frame foot anchor = x midpoint of the cleaned content in the
    bottom 8 source rows; each pose applies ONE common x offset (median plant offset) so the
    pose's feet stay planted and no frame is placed by pouch/arm/coin bbox centers;
  * outputs under artifacts/coin-courier-blueprint-b-20260927/animation-draft/:
    atlas/CoinCourierBAtlas.png, manifest.json, preview/contact-sheet.png, preview/pose-*.gif.

Run:
    python tools/prepare_coin_courier_b_atlas.py [--assets]

Exit codes: 0 ok / 2 validation failure / 3 IO or dependency failure.
Requires Pillow (`pip install pillow`) and Python 3.9+.
"""

import argparse
import hashlib
import json
import math
import statistics
import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover - environment guard
    print("FATAL: Pillow is required (pip install pillow)")
    sys.exit(3)

try:
    NEAREST = Image.Resampling.NEAREST
except AttributeError:  # Pillow < 9.1
    NEAREST = Image.NEAREST

# ---------------------------------------------------------------- fixed spec

GRID_COLUMNS = 8
GRID_ROWS = 4
FRAME_COUNT = GRID_COLUMNS * GRID_ROWS

CELL_SIZE = 56
GROUND_FROM_BOTTOM = 12
PIXELS_PER_UNIT = 32.0
TARGET_HEIGHT = 32
STANCE_MEDIAN_HEIGHT = 179          # measured on the cleaned alpha of the pinned sheet
COVERAGE_THRESHOLD = 128            # alpha >= 128 is geometry; below is noise

GENERATED_SHEET_SHA256 = "5d8daf045d9fd92bacaec2e7dbafab8dd54097c7fe8c41519071fec28c29c19c"

# Explicit cuts for boundaries whose nominal position slices real art (review 2026-09-27).
ROW_COLUMN_CUTS = {
    (1, 5): 1121,
    (2, 3): 681,
}

# Pose spans are sheet cell indices (row-major, cell = row*8 + col) == atlas frame indices.
# name, first, last, seconds_per_frame, looping
POSES = [
    ("Idle", 28, 31, 0.50, True),
    ("Leisure", 8, 11, 0.60, True),
    ("Run", 0, 7, 0.075, True),
    ("Collect", 12, 15, 0.18, False),
    ("Deliver", 16, 19, 0.20, False),
    ("Jump", 20, 23, 0.10, False),
    ("Fall", 24, 25, 0.12, False),
    ("Land", 26, 27, 0.12, False),
]

# Frames used to measure the stance median height.
STANCE_POSES = ("Idle", "Leisure", "Run", "Collect", "Deliver")
FOOT_BAND_ROWS = 8  # bottom source rows defining the foot anchor


class ValidationError(Exception):
    pass


def iround(value):
    return int(math.floor(value + 0.5))


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def grid_bounds(width, height):
    """Per-grid-edge integer cell boundaries (generated sheet pitch is fractional)."""
    xs = [iround(i * width / GRID_COLUMNS) for i in range(GRID_COLUMNS + 1)]
    ys = [iround(i * height / GRID_ROWS) for i in range(GRID_ROWS + 1)]
    return xs, ys


def cell_rect(xs, ys, row, column):
    left = ROW_COLUMN_CUTS.get((row, column), xs[column])
    right = ROW_COLUMN_CUTS.get((row, column + 1), xs[column + 1])
    return (left, ys[row], right, ys[row + 1])


def alpha_bbox(alpha, rect):
    local = alpha.crop(rect)
    bbox = local.getbbox()
    if bbox is None:
        return None
    return (rect[0] + bbox[0], rect[1] + bbox[1], rect[0] + bbox[2], rect[1] + bbox[3])


def foot_anchor_x(alpha, bbox):
    """x midpoint of the cleaned content within the bottom FOOT_BAND_ROWS source rows."""
    left, top, right, bottom = bbox
    y0 = max(top, bottom - FOOT_BAND_ROWS)
    lowest = alpha.crop((left, y0, right, bottom))
    box = lowest.getbbox()
    if box is None:
        raise ValidationError("foot band is empty after cleaning")
    return left + (box[0] + box[2]) / 2.0


def median_rounded(values):
    ordered = sorted(values)
    count = len(ordered)
    if count == 0:
        raise ValidationError("median of an empty set")
    middle = ordered[count // 2] if count % 2 else (ordered[count // 2 - 1] + ordered[count // 2]) / 2.0
    return iround(middle)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--assets", action="store_true",
                        help="also copy the atlas to il2cpp/Assets/CoinCourierBAtlas.png")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[1]
    sheet_path = repo_root / "artifacts/coin-courier-blueprint-b-20260927/animation-draft/generated-sheet.png"
    out_dir = repo_root / "artifacts/coin-courier-blueprint-b-20260927/animation-draft"
    if not sheet_path.is_file():
        print("FATAL: sheet not found: " + str(sheet_path))
        return 3

    actual_sha = sha256_file(sheet_path)
    if actual_sha != GENERATED_SHEET_SHA256:
        print("FATAL: generated-sheet sha256 mismatch")
        print("  expected " + GENERATED_SHEET_SHA256)
        print("  actual   " + actual_sha)
        return 2

    sheet = Image.open(sheet_path)
    if sheet.mode != "RGBA":
        print("FATAL: sheet is " + sheet.mode + ", expected RGBA")
        return 2
    image = sheet.convert("RGBA")
    width, height = image.size
    raw_alpha = image.getchannel("A")
    alpha = raw_alpha.point(lambda value: value if value >= COVERAGE_THRESHOLD else 0)
    histogram = raw_alpha.histogram()
    cleaned_px = sum(histogram[:COVERAGE_THRESHOLD])
    xs, ys = grid_bounds(width, height)
    ground_top = CELL_SIZE - GROUND_FROM_BOTTOM
    pivot_pixels = (CELL_SIZE // 2, GROUND_FROM_BOTTOM)
    scale = TARGET_HEIGHT / float(STANCE_MEDIAN_HEIGHT)

    print("sheet %dx%d sha256=%s" % (width, height, actual_sha))
    print("alpha spec: coverage >= %d; %d px below threshold cleared (includes background)"
          % (COVERAGE_THRESHOLD, cleaned_px))
    print("uniform scale %.10f (%d px stance median -> %d px)"
          % (scale, STANCE_MEDIAN_HEIGHT, TARGET_HEIGHT))
    for (row, column), cut in sorted(ROW_COLUMN_CUTS.items()):
        print("explicit cut row %d / column boundary %d: x=%d (nominal %d)"
              % (row, column, cut, xs[column]))

    # ---- per-frame measurement on the cleaned alpha -----------------------
    frames = []
    for index in range(FRAME_COUNT):
        row = index // GRID_COLUMNS
        column = index % GRID_COLUMNS
        rect = cell_rect(xs, ys, row, column)
        bbox = alpha_bbox(alpha, rect)
        if bbox is None:
            print("FATAL: frame %d is empty after alpha cleaning" % index)
            return 2
        edges = []
        if bbox[0] <= rect[0]:
            edges.append("left")
        if bbox[1] <= rect[1]:
            edges.append("top")
        if bbox[2] >= rect[2]:
            edges.append("right")
        if bbox[3] >= rect[3]:
            edges.append("bottom")
        if edges:
            print("FATAL: frame %d content touches its cell %s edge" % (index, "/".join(edges)))
            return 2
        frames.append({
            "index": index,
            "row": row,
            "column": column,
            "cell_rect": list(rect),
            "bbox": list(bbox),
            "bbox_size": [bbox[2] - bbox[0], bbox[3] - bbox[1]],
            "foot_anchor_x": foot_anchor_x(alpha, bbox),
        })

    # ---- stance median height (pinned) ------------------------------------
    stance_heights = []
    for frame in frames:
        for name, first, last, _, _ in POSES:
            if name in STANCE_POSES and first <= frame["index"] <= last:
                stance_heights.append(frame["bbox_size"][1])
    measured_median = median_rounded(stance_heights)
    print("stance median height measured %d px (pinned %d px)" % (measured_median, STANCE_MEDIAN_HEIGHT))
    if measured_median != STANCE_MEDIAN_HEIGHT:
        print("FATAL: stance median drifted from the pinned spec; review before regenerating")
        return 2

    # ---- per-row grounds and per-pose common x offsets ---------------------
    row_ground = {}
    for row in range(GRID_ROWS):
        row_ground[row] = max(f["bbox"][3] for f in frames if f["row"] == row)
        print("row %d ground y=%d elevations %s" % (row, row_ground[row],
              [row_ground[row] - f["bbox"][3] for f in frames if f["row"] == row]))

    pose_by_index = {}
    pose_offsets = {}
    for name, first, last, seconds, looping in POSES:
        plant_offsets = []
        for index in range(first, last + 1):
            frame = frames[index]
            bbox = frame["bbox"]
            local = frame["foot_anchor_x"] - bbox[0]
            plant = CELL_SIZE // 2 - iround(local * scale)
            frame["foot_anchor_local"] = local
            frame["plant_offset_x"] = plant
            plant_offsets.append(plant)
        common = median_rounded(plant_offsets)
        pose_offsets[name] = common
        print("pose %-8s common x offset %2d  plant offsets %s"
              % (name, common, plant_offsets))
        for index in range(first, last + 1):
            pose_by_index[index] = (name, index - first, seconds, looping)

    # ---- place every frame -------------------------------------------------
    placed = []
    for frame in frames:
        index = frame["index"]
        bbox = frame["bbox"]
        crop = image.crop(bbox)
        crop.putalpha(alpha.crop(bbox))
        scaled_w = max(1, iround(crop.width * scale))
        scaled_h = max(1, iround(crop.height * scale))
        if scaled_w > CELL_SIZE or scaled_h > CELL_SIZE:
            print("FATAL: frame %d scales to %dx%d beyond the %d px cell"
                  % (index, scaled_w, scaled_h, CELL_SIZE))
            return 2
        elevation = iround((row_ground[frame["row"]] - bbox[3]) * scale)
        if elevation + scaled_h > ground_top:
            print("FATAL: frame %d does not fit: height %d + elevation %d > headroom %d "
                  "(no clamping by design)" % (index, scaled_h, elevation, ground_top))
            return 2
        pose_name = pose_by_index[index][0]
        dst_x = pose_offsets[pose_name]
        dst_y = ground_top - scaled_h - elevation
        if dst_x < 0 or dst_x + scaled_w > CELL_SIZE or dst_y < 0:
            print("FATAL: frame %d placement x=%d y=%d w=%d does not fit the %d px cell"
                  % (index, dst_x, dst_y, scaled_w, CELL_SIZE))
            return 2
        scaled = crop.resize((scaled_w, scaled_h), NEAREST)
        out = Image.new("RGBA", (CELL_SIZE, CELL_SIZE), (0, 0, 0, 0))
        out.paste(scaled, (dst_x, dst_y))   # 空透明格：无 mask 直拷 RGBA（带 mask 会把 alpha 再乘一次）
        placed.append(out)
        frame["scaled_size"] = [scaled_w, scaled_h]
        frame["elevation_px"] = elevation
        frame["dst"] = [dst_x, dst_y]
        print("frame %2d %-8s bbox %s %dx%d -> %dx%d at (%d,%d) elev %d"
              % (index, pose_name, bbox, frame["bbox_size"][0], frame["bbox_size"][1],
                 scaled_w, scaled_h, dst_x, dst_y, elevation))

    # ---- atlas -------------------------------------------------------------
    atlas = Image.new("RGBA", (GRID_COLUMNS * CELL_SIZE, GRID_ROWS * CELL_SIZE), (0, 0, 0, 0))
    for frame, image_cell in zip(frames, placed):
        column = frame["index"] % GRID_COLUMNS
        row = frame["index"] // GRID_COLUMNS
        atlas.paste(image_cell, (column * CELL_SIZE, row * CELL_SIZE))
        frame["atlas_pos"] = [column * CELL_SIZE, row * CELL_SIZE]
        frame["sha256"] = hashlib.sha256(image_cell.tobytes()).hexdigest()

    # 装配校验 1：每个格与装配输入逐字节一致（paste 不得改变任何 RGBA）。
    for frame, image_cell in zip(frames, placed):
        column, row = frame["atlas_pos"][0] // CELL_SIZE, frame["atlas_pos"][1] // CELL_SIZE
        box = (column * CELL_SIZE, row * CELL_SIZE, (column + 1) * CELL_SIZE, (row + 1) * CELL_SIZE)
        if atlas.crop(box).tobytes() != image_cell.tobytes():
            print("FATAL: frame %d differs between placement and atlas cell" % frame["index"])
            return 2
    # 装配校验 2：atlas 不得出现低于 coverage 阈值的 alpha（双乘回归的直接指纹）。
    alpha_histogram = atlas.getchannel("A").histogram()
    below = sum(alpha_histogram[1:COVERAGE_THRESHOLD])
    if below:
        print("FATAL: atlas has %d px with alpha below %d (double-multiply regression)"
              % (below, COVERAGE_THRESHOLD))
        return 2
    print("atlas alpha check: 0 px below threshold; every cell byte-identical to its placement")

    atlas_dir = out_dir / "atlas"
    preview_dir = out_dir / "preview"
    atlas_dir.mkdir(parents=True, exist_ok=True)
    preview_dir.mkdir(parents=True, exist_ok=True)
    atlas_path = atlas_dir / "CoinCourierBAtlas.png"
    atlas.save(atlas_path)
    atlas_sha = sha256_file(atlas_path)

    # ---- previews ----------------------------------------------------------
    zoom = 4
    cell_px = CELL_SIZE * zoom
    margin = 8
    board = Image.new("RGB", (margin * 2 + GRID_COLUMNS * cell_px, margin * 2 + GRID_ROWS * cell_px), (32, 36, 40))
    draw = ImageDraw.Draw(board)
    checker = Image.new("RGB", (cell_px, cell_px))
    checker_draw = ImageDraw.Draw(checker)
    for cy in range(CELL_SIZE):
        for cx in range(CELL_SIZE):
            shade = 58 if ((cx // 8) + (cy // 8)) % 2 == 0 else 50
            checker_draw.rectangle([cx * zoom, cy * zoom, cx * zoom + zoom - 1, cy * zoom + zoom - 1],
                                   fill=(shade, shade + 2, shade + 4))
    for frame, image_cell in zip(frames, placed):
        column = frame["index"] % GRID_COLUMNS
        row = frame["index"] // GRID_COLUMNS
        big = image_cell.resize((cell_px, cell_px), NEAREST)
        x = margin + column * cell_px
        y = margin + row * cell_px
        board.paste(checker, (x, y))
        board.paste(big, (x, y), big)
        draw.rectangle([x, y, x + cell_px - 1, y + cell_px - 1], outline=(90, 96, 104))
        ground_y = y + ground_top * zoom
        draw.line([x, ground_y, x + cell_px - 1, ground_y], fill=(200, 90, 70), width=1)
        foot_x = x + iround(frame["dst"][0] + frame["foot_anchor_local"] * scale) * zoom
        draw.line([foot_x - 1, ground_y - 6, foot_x - 1, ground_y + 6], fill=(90, 220, 120), width=1)
        pivot_x = x + pivot_pixels[0] * zoom
        pivot_y = y + (CELL_SIZE - pivot_pixels[1]) * zoom
        draw.line([pivot_x - 3, pivot_y, pivot_x + 3, pivot_y], fill=(250, 210, 90), width=1)
        draw.line([pivot_x, pivot_y - 3, pivot_x, pivot_y + 3], fill=(250, 210, 90), width=1)
        pose_name, phase, _, _ = pose_by_index[frame["index"]]
        mark = " +%d" % frame["elevation_px"] if frame["elevation_px"] else ""
        draw.text((x + 3, y + 3), "%d %s%s" % (frame["index"], pose_name, mark), fill=(235, 235, 235))
    board.save(preview_dir / "contact-sheet.png")

    pose_gifs = []
    for name, first, last, seconds, _ in POSES:
        tiles = []
        for index in range(first, last + 1):
            frame_image = placed[index].resize((CELL_SIZE * 5, CELL_SIZE * 5), NEAREST)
            tile = Image.new("RGB", (CELL_SIZE * 5, CELL_SIZE * 5), (46, 50, 54))
            tile.paste(frame_image, (0, 0), frame_image)
            tiles.append(tile)
        gif_path = preview_dir / ("pose-%s.gif" % name.lower())
        tiles[0].save(gif_path, save_all=True, append_images=tiles[1:],
                      duration=max(40, iround(seconds * 1000)), loop=0, disposal=2)
        pose_gifs.append("preview/" + gif_path.name)

    # ---- manifest ----------------------------------------------------------
    manifest = {
        "tool": "tools/prepare_coin_courier_b_atlas.py",
        "spec": {
            "coverage_threshold": COVERAGE_THRESHOLD,
            "stance_median_height_px": STANCE_MEDIAN_HEIGHT,
            "target_height_px": TARGET_HEIGHT,
            "uniform_scale": round(scale, 10),
            "row_column_cuts": {"%d:%d" % key: value for key, value in sorted(ROW_COLUMN_CUTS.items())},
            "elevation_clamping": False,
            "placement": "per-pose common x offset from per-frame bottom-band foot anchors",
        },
        "source": {
            "path": "artifacts/coin-courier-blueprint-b-20260927/animation-draft/generated-sheet.png",
            "sha256": actual_sha,
            "size": [width, height],
            "low_alpha_cleaned_px": cleaned_px,
        },
        "atlas": {
            "file": "atlas/CoinCourierBAtlas.png",
            "sha256": atlas_sha,
            "size": [GRID_COLUMNS * CELL_SIZE, GRID_ROWS * CELL_SIZE],
            "columns": GRID_COLUMNS,
            "rows": GRID_ROWS,
            "cell": [CELL_SIZE, CELL_SIZE],
            "pixels_per_unit": PIXELS_PER_UNIT,
            "pivot_pixels": list(pivot_pixels),
            "pivot_origin": "bottom-left of the cell",
            "ground_row_from_top": ground_top,
            "alpha_px_below_threshold": below,
            "logical_name": "KingdomEnhancedMod.CoinCourierBAtlas.png",
        },
        "poses": [
            {"name": name, "first": first, "last": last, "frames": last - first + 1,
             "seconds_per_frame": seconds, "looping": looping, "cell_offset_x": pose_offsets[name]}
            for name, first, last, seconds, looping in POSES
        ],
        "frames": [
            {
                "index": f["index"],
                "pose": pose_by_index[f["index"]][0],
                "phase": pose_by_index[f["index"]][1],
                "source_cell": [f["column"], f["row"]],
                "source_rect": f["cell_rect"],
                "bbox_in_sheet": f["bbox"],
                "scaled_size": f["scaled_size"],
                "elevation_px": f["elevation_px"],
                "foot_anchor_x": round(f["foot_anchor_x"], 2),
                "foot_anchor_local": round(f["foot_anchor_local"], 2),
                "plant_offset_x": f["plant_offset_x"],
                "cell_pos": f["dst"],
                "atlas_pos": f["atlas_pos"],
                "sha256": f["sha256"],
            }
            for f in frames
        ],
        "previews": ["preview/contact-sheet.png"] + pose_gifs,
    }
    manifest_path = out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    if args.assets:
        assets_path = repo_root / "il2cpp/Assets/CoinCourierBAtlas.png"
        assets_path.parent.mkdir(parents=True, exist_ok=True)
        assets_path.write_bytes(atlas_path.read_bytes())
        print("copied atlas -> %s (sha256 %s)" % (assets_path, sha256_file(assets_path)))

    print("")
    print("OK: %d frames, atlas %dx%d, ppu %g, pivot %s"
          % (FRAME_COUNT, atlas.width, atlas.height, PIXELS_PER_UNIT, pivot_pixels))
    print("  atlas    %s  sha256=%s" % (atlas_path, atlas_sha))
    print("  manifest %s" % manifest_path)
    print("  previews %s" % preview_dir)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ValidationError as error:
        print("FATAL: " + str(error))
        sys.exit(2)
    except OSError as error:
        print("FATAL: IO error: " + str(error))
        sys.exit(3)
