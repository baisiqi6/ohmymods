# R12 400u production coordinates

This document fixes independent art coordinates. It does not declare Native ground, water, adult size, camera or parallax measurements.

- Physical domain is `[-200, 200]` game units. Design pixel X domain is `[20,12820]`, 32 px/u, gate axis X=6420/worldX=0.
- R12 **authored street reference is design Y=224**, top-left pixel coordinates, Y down. This is a new layout plane, not a claim about R11's painted street or the former 187 line.
- A source/master rectangle is provenance and transport mapping. **Its source Y does not determine runtime placement.** In particular do not perform a generic `(187-sourceY)/32` conversion.
- Each final asset declares an explicit local semantic attachment/support. `localOrigin=[0,0]` is the asset canvas top-left, never a recentered opaque bounding box. Individual slice origins stay in that one master.

For a street object whose local support is `(sx,sy)`, the layout records:

```
referencePlane = native-ground
supportWorldX = (designSupportX - 6420) / 32
supportOffsetYUnits = authored offset (normally 0)
designTopLeft = [designSupportX-sx, 224-sy-32*supportOffsetYUnits]
world(localX,localY) = [supportWorldX + (localX-sx)/32,
                       measuredNativeGroundY + supportOffsetYUnits + (sy-localY)/32]
```

The support is an actual painted footing / declared construction attachment, not the lowest opaque pixel automatically inferred from alpha. The final registry/layout supplies the local anchor per object; old audit `sourceOriginCandidate` is not a runtime freeze.

Final data checks cross-check every placement against that asset's declared attachment, its finite coordinates, explicit designSupportPixel-to-world conversion, district and draw role. Brick face and both stairs use canvas-internal top-edge anchors plus explicit negative supportOffsetYUnits; this is algebraically identical to the former external virtual socket and does not move the art. The authored design reference is Y224 for each referencePlane; a water placement's design point does not claim the preview Y262 or measured Native water height. Three decorative background instances explicitly extend beyond the physical walk domain; walkable objects remain within [-200,200].

Other families:

- Boat anchors use `referencePlane=native-water` with a declared hull waterline; Native water Y stays unknown until measured. No image reflection/water is part of the boat.
- The paving attaches its top surface to the ground plane. The quay attaches below the paving; its course count/underwater extent must fit the measured ground-water difference. No Y stretch of a building or whole stair is permitted.
- Shore endcaps are whole rock/tree compounds. Their explicit local shore socket and authored offset preserve the rock mass below the walk plane; do not align the lowest rock to player feet.
- A dancer uses the theatre's **local deck anchor transformed through its own building placement**. The deck-to-foundation offset comes from the final theatre art, not old 187/178/.675 values.
- Background compounds / trees use their own authored layout positions and separately declared motion groups. Their supports are art composition anchors; Native parallax rate and full camera reveal remain for runtime measurement.

All final PNGs have binary alpha, RGB0 under alpha0 and Point sampling. A production entity is a complete declared compound; hidden member assets are not separately ready. Transport entries use `masterId/masterRect` in one master coordinate space and are checked by exact reconstruction. A single PNG normally has `[0,0,width,height]`; R11 lineage is stored separately as `provenance.sourceRect`, not used to recenter transport.

Environment palette is B128-r2, SHA `f9a2bf767906fff2e7ca30a6de040dc5d7f7a589561b294bd89cd37ff6d87841`. Player actor palettes remain original. Actor batch1's 32 is a design PPU; original undeclared actor PPU remains null. Dancer timing entries must read explicit frame rectangles (26 temporal frames, 21 physical cells).

Final batch will carry both the complete registry and the 400u layout. This contract alone is not permission for Capital400's completeness gate to pass, and none of its unknown Native values is replaced by a historical R7 measurement.
