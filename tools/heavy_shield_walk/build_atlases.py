"""Pack reviewed native-resolution walk frames into a pinned atlas (offline only)."""
import argparse
import hashlib
import io
from pathlib import Path
import subprocess

from PIL import Image

SOURCE_COMMIT = "2df52f38910f44fdbbd2670fc27e687100853222"
SLOTS = tuple(start + i for start in (4, 95, 186, 277) for i in range(6))
LAYOUTS = {
    "detail": ("HeavyShieldSoldierAtlas.png", 64, 48,
               "d3ca984a184dea49c7f2ab72ae4e9e3e25b1299f6404c5c8b6236efa8839a6cd"),
    "coarse": ("HeavyShieldSoldierAtlasCoarse.png", 48, 32,
               "0d337b876f149ad4081b1086abd1a989f67440c6c5eab569f578d38481cbfcfb"),
}


def build(output, repo, frames):
    output.mkdir(parents=True, exist_ok=True)
    for tag, (name, width, height, digest) in LAYOUTS.items():
        source = subprocess.check_output([
            "git", "-C", str(repo), "show", f"{SOURCE_COMMIT}:il2cpp/Assets/{name}"])
        if hashlib.sha256(source).hexdigest() != digest:
            raise ValueError(f"Unrecognized baseline: {name}")
        atlas = Image.open(io.BytesIO(source)).convert("RGBA")
        if atlas.size != (width * 16, height * 32):
            raise ValueError(f"Wrong baseline layout: {name}")
        for slot in SLOTS:
            frame = Image.open(frames / tag / f"{slot:03d}.png")
            if frame.mode != "RGBA" or frame.size != (width, height):
                raise ValueError(f"Wrong native frame: {tag}/{slot}")
            atlas.paste(frame, ((slot % 16) * width, (slot // 16) * height))
        target = output / name
        atlas.save(target)
        print(name, hashlib.sha256(target.read_bytes()).hexdigest())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True,
                        help="Explicit output directory; does not deploy or start the game")
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    build(args.output.resolve(), here.parents[1], here / "frames")
