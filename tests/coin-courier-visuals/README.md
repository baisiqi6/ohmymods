# coin-courier-visuals

Managed regression suite for the coin-courier display layer:

- `il2cpp/CoinCourierVisuals.cs` — pose table (`CoinCourierPose`), view lifecycle, atlas slicing.
- `il2cpp/CoinCourierTeleportFx.cs` — handle/generation teleport pool (4-dash ripple groups) shared
  with the tax assistant.

The suite compiles both production files against local UnityEngine doubles (`Stubs.cs`); it needs
no Unity and no game install. It checks the frame/phase math, view lifecycle, the FX handle
contract (settle/fade envelope, once-per-frame `Tick`, generation-safe `Cancel`, fixed group cap)
and injected resource-failure cleanup, and cross-checks the runtime tables against the generated
`manifest.json` (grid, cell, PPU, pivot, pose spans, per-frame mapping, per-pose x offset, atlas
sha256).

Run from the repository root (use the .NET 8 SDK; on this machine `dotnet` on PATH is SDK 6, so):

```sh
python tools/prepare_coin_courier_b_atlas.py --assets
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project tests/coin-courier-visuals/Regression.csproj
```

The manifest check fails when no manifest exists; point it at another copy with
`COIN_COURIER_MANIFEST=<path>`, or skip it with `COIN_COURIER_SKIP_MANIFEST=1` while iterating on
the pure logic tests. A passing run does not establish real rendering, in-game visuals or the
contact-sheet aesthetics; the previews and the in-game pass own that evidence.
