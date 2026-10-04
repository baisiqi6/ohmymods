#!/usr/bin/env python3
"""Coin courier leisure-coin preview replay (NOT in-game footage, no image generation).

Inputs
------
* samples.json  - real C# samples dumped by the regression console:
      dotnet run --project tests/coin-courier-visuals/Regression.csproj -c Release \
          -- --preview-samples artifacts/coin-courier-leisure-20261003/samples.json
  Each sample carries the body frame index (CoinCourierPoseTable.FrameIndex) and the
  leisure coin sample (CoinCourierLeisureCoin.TrySample): local/pixel position, quantized
  spin width and visibility.
* CoinCourierBAtlas.png - the actual shipped atlas (never modified here).

Outputs (this script only replays the samples pixel-exactly at the shipped scale)
-------
* preview.gif        - idle 2s + leisure 2.4s, two loops, 20 fps, nearest upscale.
* contact-sheet.png  - key moments at native 1x and 6x, light and dark plain backgrounds,
                       plus the mirrored (facing left) variant.
* preview-manifest.json - parameter/hash record; states that this is a program replay.

The composite scale follows the shipped view scale (0.625 x 0.65625) with a point filter;
coin positions are the already pixel-quantized C# samples.
"""

import argparse
import hashlib
import json
import os
from PIL import Image, ImageDraw

PIVOT_X = 28          # atlas cell pivot (px from the cell's left/bottom)
PIVOT_Y = 12
GROUND_ROW = 44       # rows from the cell top down to the foot line
CANVAS_W, CANVAS_H = 64, 60
ANCHOR_X, ANCHOR_Y = 32, 52

LIGHT = (231, 226, 209, 255)
DARK = (34, 32, 40, 255)

KEY_MOMENTS = [
    (0.80, "idle", "Idle 站立（无币）"),
    (2.10, "play", " Leisure 把玩"),
    (2.45, "spin", "指尖翻面"),
    (2.90, "toss", "上抛"),
    (3.05, "apex", "顶点略高于头"),
    (3.45, "fall", "下落"),
    (3.62, "catch", "接回掌心"),
    (4.32, "stow", "收袋隐藏"),
]


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 16), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_inputs(samples_path, atlas_path):
    with open(samples_path, "r", encoding="utf-8") as stream:
        data = json.load(stream)
    if data.get("kind") != "coin-courier-leisure-preview-samples":
        raise SystemExit("unexpected samples kind: " + str(data.get("kind")))
    atlas = Image.open(atlas_path).convert("RGBA")
    body = data["body"]
    if atlas.size != (body["columns"] * body["cell"], body["rows"] * body["cell"]):
        raise SystemExit("atlas size does not match the sample metadata")
    coin = Image.new("RGBA", (4, 4))
    coin.putdata([tuple(p) for p in data["coin"]["rgba"]])
    return data, atlas, coin


def body_cell(atlas, frame, cell):
    columns = atlas.width // cell
    column = frame % columns
    row = frame // columns
    return atlas.crop((column * cell, row * cell, column * cell + cell, row * cell + cell))


def scale_size(scale_x, scale_y, cell):
    """Scaled sprite size plus the offsets of the foot pivot inside that sprite."""
    width = max(1, round(cell * scale_x))
    height = max(1, round(cell * scale_y))
    left = round(PIVOT_X * scale_x)
    top = round((cell - PIVOT_Y) * scale_y)   # rows from the sprite top down to the foot line
    return width, height, left, top


def render_sample(data, atlas, coin, sample, background, mirror=False, zoom=1):
    """Replay one sample at the shipped scale (optionally mirrored / zoomed)."""
    scale_x, scale_y = data["body"]["appearance_scale"]
    canvas = Image.new("RGBA", (CANVAS_W, CANVAS_H), background)
    frame = sample["body_frame"]
    cell = data["body"]["cell"]
    body_sprite = body_cell(atlas, frame, cell)
    width, height, left, top = scale_size(scale_x, scale_y, cell)
    body = body_sprite.resize((width, height), Image.NEAREST)
    if mirror:
        body = body.transpose(Image.FLIP_LEFT_RIGHT)
        left = width - left
    canvas.alpha_composite(body, (ANCHOR_X - left, ANCHOR_Y - top))
    payload = sample.get("coin")
    if payload and payload["visible"]:
        coin_w = max(1, round(coin.width * scale_x * payload["width"]))
        coin_h = max(1, round(coin.height * scale_y))
        sprite = coin.resize((coin_w, coin_h), Image.NEAREST)
        offset_x = round(payload["local_x"] * 32 * scale_x)
        offset_y = round(payload["local_y"] * 32 * scale_y)
        if mirror:
            offset_x = -offset_x
        center_x = ANCHOR_X + offset_x
        center_y = ANCHOR_Y - offset_y
        canvas.alpha_composite(sprite, (center_x - coin_w // 2, center_y - coin_h // 2))
    if zoom != 1:
        canvas = canvas.resize((canvas.width * zoom, canvas.height * zoom), Image.NEAREST)
    return canvas


def sample_at(data, t):
    """Nearest 60 Hz sample at timeline time t (idle 0..2s, leisure phase 0..2.4s)."""
    hz = data["timeline"]["sample_hz"]
    index = min(len(data["samples"]) - 1, max(0, int(round(t * hz))))
    return data["samples"][index]


def text_chip(text, color, scale=1):
    """Draw text at native size, then nearest-upscale so the sheet keeps the pixel look."""
    probe = Image.new("RGBA", (1, 1))
    box = ImageDraw.Draw(probe).textbbox((0, 0), text)
    chip = Image.new("RGBA", (box[2] - box[0] + 4, box[3] - box[1] + 4), color)
    ImageDraw.Draw(chip).text((2 - box[0], 2 - box[1]), text, fill=(95, 95, 95, 255))
    if scale != 1:
        chip = chip.resize((chip.width * scale, chip.height * scale), Image.NEAREST)
    return chip


def label_image(text, height, width, color):
    strip = Image.new("RGBA", (width, height), color)
    chip = text_chip(text, color)
    strip.alpha_composite(chip, (4, max(0, (height - chip.height) // 2)))
    return strip


def build_gif(data, atlas, coin, out_path, zoom=3, fps=20, loops=2):
    timeline = data["timeline"]
    total = timeline["total_seconds"]
    frames = []
    step = 1.0 / fps
    for _ in range(loops):
        t = 0.0
        while t < total - 1e-6:
            frames.append(render_sample(data, atlas, coin, sample_at(data, t), LIGHT, zoom=zoom))
            t += step
    footer = label_image("C# sample replay - NOT in-game footage", frames[0].width, frames[0].width, LIGHT)
    composed = [Image.new("RGBA", (frames[0].width, frames[0].height + footer.height), LIGHT)
                for _ in frames]
    for target, frame in zip(composed, frames):
        target.alpha_composite(frame, (0, 0))
        target.alpha_composite(footer, (0, frame.height))
    palette = [image.convert("P", palette=Image.ADAPTIVE, colors=64) for image in composed]
    palette[0].save(out_path, save_all=True, append_images=palette[1:],
                    duration=int(round(1000.0 / fps)), loop=0, disposal=2)
    return len(palette)


def build_contact_sheet(data, atlas, coin, out_path, zoom=6):
    rows = []
    headers = [
        ("native 1x - light", False, 1, LIGHT),
        ("6x - light", False, zoom, LIGHT),
        ("6x - dark", False, zoom, DARK),
        ("6x - dark, mirrored (facing left)", True, zoom, DARK),
    ]
    label_w = 190
    columns = [render_sample(data, atlas, coin, sample_at(data, t), bg, mirror=mirror, zoom=z)
               for _, mirror, z, bg in headers
               for t, _, _ in KEY_MOMENTS]
    index = 0
    for title, mirror, z, bg in headers:
        cells = columns[index:index + len(KEY_MOMENTS)]
        index += len(KEY_MOMENTS)
        width = label_w + sum(cell.width + 4 for cell in cells)
        height = max(cell.height for cell in cells) + 8
        row = Image.new("RGBA", (width, height), bg)
        row.alpha_composite(label_image(title, height, label_w, bg), (0, 0))
        x = label_w
        for cell in cells:
            row.alpha_composite(cell, (x, 4))
            x += cell.width + 4
        rows.append(row)

    # 时间/相位标签条（与 KEY_MOMENTS 对齐，使用 6x 行的列宽；文字按 3x 放大保持像素观感）
    cell_width = CANVAS_W * zoom
    chips = [text_chip("t=%.2f %s" % (t, name), LIGHT, 3) for t, name, _ in KEY_MOMENTS]
    strip_height = max(chip.height for chip in chips) + 6
    width = label_w + sum(cell_width + 4 for _ in KEY_MOMENTS)
    strip = Image.new("RGBA", (width, strip_height), LIGHT)
    x = label_w
    for chip in chips:
        strip.alpha_composite(chip, (x + 2, 3))
        x += cell_width + 4
    rows.append(strip)

    width = max(row.width for row in rows)
    height = sum(row.height for row in rows)
    sheet = Image.new("RGBA", (width, height), LIGHT)
    y = 0
    for row in rows:
        sheet.alpha_composite(row, (0, y))
        y += row.height
    sheet.save(out_path)
    return sheet.size


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--samples", default="artifacts/coin-courier-leisure-20261003/samples.json")
    parser.add_argument("--atlas", default="il2cpp/Assets/CoinCourierBAtlas.png")
    parser.add_argument("--out-dir", default="artifacts/coin-courier-leisure-20261003")
    parser.add_argument("--gif-zoom", type=int, default=3)
    parser.add_argument("--fps", type=int, default=20)
    parser.add_argument("--loops", type=int, default=2)
    args = parser.parse_args()

    data, atlas, coin = load_inputs(args.samples, args.atlas)
    os.makedirs(args.out_dir, exist_ok=True)
    gif_path = os.path.join(args.out_dir, "preview.gif")
    sheet_path = os.path.join(args.out_dir, "contact-sheet.png")
    manifest_path = os.path.join(args.out_dir, "preview-manifest.json")

    frames = build_gif(data, atlas, coin, gif_path, zoom=args.gif_zoom, fps=args.fps, loops=args.loops)
    sheet_size = build_contact_sheet(data, atlas, coin, sheet_path)
    manifest = {
        "kind": "coin-courier-leisure-preview",
        "not_in_game_footage": True,
        "note": "Program replay of the real C# leisure-coin samples over the shipped atlas; "
                "no image generation, no game recording. Pixel scale follows the shipped view "
                "scale (0.625 x 0.65625).",
        "inputs": {
            "samples.json": {"path": args.samples, "sha256": sha256_file(args.samples)},
            "CoinCourierBAtlas.png": {"path": args.atlas, "sha256": sha256_file(args.atlas)},
            "tools/preview_coin_courier_leisure.py": {"sha256": sha256_file(__file__)},
        },
        "parameters": {
            "gif_zoom": args.gif_zoom,
            "gif_fps": args.fps,
            "gif_loops": args.loops,
            "gif_frames": frames,
            "contact_sheet_zooms": [1, 6],
            "contact_sheet_mirror_row": True,
            "backgrounds": {"light": list(LIGHT), "dark": list(DARK)},
            "key_moments": [{"t": t, "name": name, "label": note} for t, name, note in KEY_MOMENTS],
        },
        "outputs": {
            "preview.gif": {"sha256": sha256_file(gif_path)},
            "contact-sheet.png": {"sha256": sha256_file(sheet_path), "size": list(sheet_size)},
        },
        "timeline": data["timeline"],
    }
    with open(manifest_path, "w", encoding="utf-8") as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
    print("preview.gif frames:", frames, "sheet:", sheet_size)
    print("manifest:", manifest_path)


if __name__ == "__main__":
    main()
