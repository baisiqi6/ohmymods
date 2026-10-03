# Final gem shield art validation (2026-10-01)

The production metadata in `il2cpp/PatchRoles_HeavyShieldArt.cs` is compiled directly.
The independent oracle is the exact final `animation-manifest.json` and
`shop-bilateral-manifest.json` in `artifacts/gem-shield-assets-20261001/`.
Original atlas ids and default entry points use Detail; ids 4–7 load Coarse.

- `Program.cs` checks all eight layouts, all 77 character sequences, exact FPS/Loop,
  continuous slots 0–508, 19 actions × 4 wear states + break, every frame/offset,
  foot pivots, per-atlas PPU and invalid tables.
- `verify_atlases.py` checks the pinned ZIP hash, original PNG bytes, every cell,
  binary alpha, zero RGB transparency, both facings, canonical endpoints, palette,
  clipping guards, padding, four bilateral shop variants, independent paid shields,
  empty fixtures and the existing left/right pickup anchors. `--pack-root` overrides
  the final source pack path; `--evidence` chooses a private output directory.
- `test_loader.py` embeds the ten real PNGs and compiles the full production file
  against recording Unity stubs. It checks loading/cache/rect/PPU/pivot/sampler
  parameters and repeated conservative failures with partial-resource cleanup.
  Its PNG decoder returns exact final pixels in Unity's bottom-up order.

For isolated core output, use `dotnet build` with private
`BaseIntermediateOutputPath` and `OutputPath`, then execute that absolute DLL.
`dotnet run` can resolve its executable from the default path despite an overridden
output directory. The project excludes old `obj` and `bin` compile items.

Both Python scripts accept `--evidence`; the loader also accepts `--dotnet`.
Pixel evidence defaults to `evidence/final-20261001/`; existing top-level
`evidence/atlas-evidence.json` is historical evidence for the older 136-frame pack.

All evidence is offline. Body world height 0.7 follows the artist's body metadata
and PPU; it does not verify native parent scale, live mirrored transforms, timing,
pickup/physics, saving, ownership cleanup or rendering inside the actual game.
