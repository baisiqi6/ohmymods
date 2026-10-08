# Gem shield back-walk native frames

Issue [#182](https://github.com/baisiqi6/ohmymods/issues/182).
`frames/detail/` and `frames/coarse/` contain the reviewed complete native-resolution
frames for six back-walk phases in four shield wear states. These are editable
artwork, not runtime procedural animation. The original head, upper body, hand
and shield remain in each frame; the lower legs and boots form the new cycle.

The packer starts from immutable commit `2df52f38910f44fdbbd2670fc27e687100853222`,
checks both original PNG hashes, and replaces only slots 4–9, 95–100, 186–191,
277–282. It requires that Git object locally. It never reads the current atlas
as its baseline, preventing accidental repeated application. Pillow is required.

```sh
python3 tools/heavy_shield_walk/build_atlases.py --output /tmp/shield-walk-rebuild
python3 tests/heavy-shield-walk/verify_pixels.py --evidence /tmp/shield-walk-check
```

Rebuilt PNGs match the committed PNGs byte for byte with the production Pillow
encoder. Across other Pillow versions, compare decoded RGBA pixels; PNG container
encoding can differ. The packer does not install anything or change animation
metadata. Actual runtime remains the existing six-frame, 9fps sprite sequence.

The pixel check independently covers changed-cell scope, untouched upper body,
source palette, binary transparency, original per-frame support row, known
shield-rim regression coordinates and whole-frame opaque 8-neighbor boot
connectivity. It catches the rejected intermediate disconnected boots and
missing shield rim. It does not prove full shield ownership, leg identity,
animation quality or game acceptance; those require visual review and play.
