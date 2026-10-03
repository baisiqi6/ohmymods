# teleport-fx-sequence

Issue-104 sequence contract suite for the shared teleport FX. It compiles the **real**
`il2cpp/CoinCourierTeleportFx.cs` against the `tests/coin-courier-visuals` UnityEngine doubles
(linked, not duplicated) and verifies the style/direction model without Unity or the game:

- sixteen distinct stripes in one spec table; group cap 18; one shared uniform tilt
  (`TiltSlope = .10` ≈ 5.7°): every stripe of a style leans the same way with |ratio| = .10 at every
  age in both directions, any two normalized directions stay parallel (cross ~ 0), and horizontal
  stays x-dominant / vertical y-dominant;
- style-owned palettes: horizontal is clear gold (never washed white), vertical stays near black
  with restrained warm end colours (LineRenderer interpolates, so the whole line is warm
  near-black) and ignores the caller's base RGB (only `.a` still scales alpha);
- arrival density/length grow monotonically to a full-length peak exactly at the materialize
  anchor and only thin afterwards; departure is dense/full-length at t=0 and only thins;
- strictly staggered, deterministic activation windows inside the anchor;
- style/direction/anchor validation before any pool slot is taken;
- pool cap 18 with stale handles inert after recycling and `Clear`;
- pause/frame-dedup freeze the sequence; a departure and an arrival owned by the same caller
  coexist (begin-to never cancels begin-from); `.12` and `.18` anchors shift the peak accordingly.

`dotnet run -c Release --project tests/teleport-fx-sequence/SequenceTests.csproj`

The same binary exports the production frame data used by the private review previews:

`dotnet run -c Release --project tests/teleport-fx-sequence/SequenceTests.csproj -- --export <dir>`

which drives the real `Begin`/`TickForFrame` and dumps per-frame positions / start-end RGBA /
width keys + multiplier / visibility for the six (style, direction, anchor) runs into
`teleport-fx-frames.json`. The preview renderer only draws that data; it never re-authors the
curves. Passing this suite (or drawing those frames) does not prove real Unity rendering — the
in-game check owns that.
