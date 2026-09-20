# Hero scarf worker result — 2026-09-15

Implemented the approved two-tail cloth surface in the bounded worker slice. Operator owns final integrated preview, build, regressions, documentation and deployment.

## Files

- `C:/Users/ADMIN/projects/ohmymods/il2cpp/HeroArcherCloth.cs`: each of seven segments has two independent flat-color quads (56 vertices / 84 indices per chain). Vertices, colors and indices are allocated once at creation; only vertex coordinates are updated. Fixed colors match the neck atlas: primary main `BE1C24` with deep `64121D` and sparse bright folds `E43923`; secondary main `E43923` with shade fold `89121D`. No white highlight or interpolated color ramp.
- `C:/Users/ADMIN/projects/ohmymods/il2cpp/HeroArcherScarfGeometry.cs`: new pure geometry API, no Unity types/allocations/state mutation. `TrySegment(x,y,segment,ribbon,clock,out Segment,bool attachmentBridge=false)` returns six exact section points; runtime repeats shared edge vertices between the two colored faces. `FaceRgb` returns each face's fixed RGB24. This API and the actual Mesh arrays both support exact preview reproduction.
- `C:/Users/ADMIN/projects/ohmymods/il2cpp/HeroArcherClothPhysics.cs`: **only HeroArcherClothMath width-related declarations** changed. Final maximum half-width envelope is 2px at nodes 0–4 and 1.5px at nodes 5–7, matching the actual 4px/3px maximum visible band. The initially added 0.5px quantization margin was removed after follow-up investigation: it is unnecessary for the actual integer-pixel ground used by this view (proof below). Compared source before `internal static class HeroArcherClothMath` with task baseline: identical, including all HeroArcherClothChain dynamics.
- `C:/Users/ADMIN/projects/ohmymods/tests/hero-archer/cloth-view/Program.cs` and `Tests.csproj`: topology/section lookup/odd-width center semantics and new helper include; secondary anchor expectation updated.
- `C:/Users/ADMIN/projects/ohmymods/tests/hero-scarf-geometry/Program.cs` and `Tests.csproj`: new geometry and actual view mesh checks.

## Behavior and contract

Two independent 24px/22px chains retain their existing eight-node simulation, flow envelopes, wind, phase and drag differences. Main cloth is 3–4 pixels across its cardinal pixel section; tips stay 2–3 pixels. Every segment exposes two faces with cardinal-section Manhattan width at least one source pixel, with a slowly traveling change in width/fold proportion. Colors remain constant over time. Cardinal sections avoid the rounding collapse and inverted long miters that a very narrow smoothly rotated section can produce at sharp turns.

At Operator's follow-up request, the final secondary anchor is **(-1,+3) pixels** so the wider tails remain visually distinguishable without reducing their available clearance. Its ground is -17/32 in chain-local coordinates, while primary remains -14/32. The intermediate (-1,-3) candidate preserved separation but caused low-speed floor contact; it was superseded after the motion investigation below. Root/shoulder coordinates, 0.9 scale integration, native31 frames, render priority, alpha, flip/reorient lifecycle and caller integration remain unchanged. Operator may adjust the neck/body connection as part of their separately owned atlas slice.

The helper rejects null/short arrays, invalid chain/segment indices, non-finite/out-of-bounds positions and quantized zero-length segments. Invalid clock uses the initial fold. A transient invalid segment retains its previous valid mesh face instead of inventing an endpoint or pushing it through the floor. Hidden ticks continue returning before simulation, fold-clock or mesh updates.

## Verification

Commands (test executables only, no Debug plugin copy target):

```text
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-archer/cloth-view/Tests.csproj
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-scarf-geometry/Tests.csproj
```

- **33 cloth-view checks passed**. Logs: `worker-cloth-view.log` alongside this report. Creation, material/white texture sharing, alpha/layer/flip, hidden freeze, reuse, destroy, ground safety, null/dead handles retained.
- **35 scarf-geometry checks passed**. Logs: `worker-scarf-geometry.log`. Sweeps 120 headings × 8 subpixel phases and both chains; verifies visible body/tip widths, both face widths, all triangle signs, maximum envelope, distinct fold timing, zero pure-geometry allocations. A 2,400-frame / 40-second actual-view run covers idle/walk/run/backpedal cycles and wind changes: exact pixel grid, every bottom edge above true ground, positive nondegenerate bounded-area triangles, index bounds, face-uniform fixed colors, reused vertex/color/index arrays, freeze and destruction behavior.
- Baseline source comparison confirms no HeroArcherClothChain changes.

## Old test semantics and limits

- `tests/hero-archer/cloth-logic/Program.cs:460–462` encodes old half widths 1px/0.5px and strict decrease at every node. The final values are maximum safe envelopes 2px/1.5px with a broad body and a narrower tail, so that assertion requires semantic adaptation by Operator.
- `tests/hero-cloth-turn/Program.cs:17` uses secondary ground -13/32. If it constructs the old standalone chain intentionally, keep it; if it follows actual final view anchor, use -17/32.
- Explicit Compile projects that include Cloth.cs must now also include HeroArcherScarfGeometry.cs. Worker updated only the authorized cloth-view project and the new test project.
- Integer-pixel fold changes and cardinal-section heading changes are discrete. This is not a claim that all visual jumps have been removed. Existing low-speed ground-fold direction snapping remains in unchanged chain dynamics.
- Individual quads are validated against inversion/degeneracy. A physically folded chain can still overlap itself or the second chain; no collision system or global self-intersection solver was added. Final combined appearance and real-game motion remain Operator/user visual acceptance work.
- No game start/stop, DLL deployment, save/config writes, commits, pushes, body/atlas/build-motion/Visuals/version/docs edits or further delegation performed by this worker.


## Intermediate follow-up: minimum sufficient floor envelope and motion evidence

At this intermediate investigation stage, production still used secondary anchor (-1,-3), drag 0.9 and unchanged Chain dynamics. This anchor was subsequently replaced by (-1,+3), as described in the final follow-up below. Only the Math envelope was reduced from initial 2.5/2px to final **2/1.5px**. No old test assertions were edited by worker.

### Why geometryCardinalRound does not need the extra 0.5px

Use pixel units. The actual local grounds G are integers (-14 primary; -11 in the intermediate lower-anchor candidate and -17 in the final upper-anchor candidate). Simulation enforces y >= G + H, where H is the maximum half width at that node. For a horizontal segment with a vertical cross-section, the lowest mesh endpoint is Round(y - w/2), where w/2 <= H. Thus y - w/2 >= G, and Round(t)=floor(t+0.5) cannot be below an integer G when t >= G. For a vertical segment with a horizontal cross-section, both endpoints use Round(y), which is also >= G. A triangle's interior is a convex interpolation of its vertices, so it cannot be below their minimum. This proof covers either chain direction and the maximum temporal widths. The root's y=0 is already well above either ground.

This is a ground-specific guarantee, not a claim that any rounded vertex is within H Euclidean distance of its unrounded center. The new geometry test checks the actual lower-edge inequality against integer ground over 120 headings x 8 subpixel phases; actual-view 40-second lowest-edge checks also pass. The pure helper supports arbitrary coordinates but the runtime's strict floor guarantee assumes its documented integer-pixel ground.

### Regression results after reduction

- `worker-scarf-geometry.log`: 25/25 pass, including actual lower edges on integer ground.
- `worker-cloth-view.log`: 33/33 pass.
- `worker-logic-width2.log`: idle 0.2-second softness and traveling-wave checks restored. Two failures remain in the unchanged older project: its now-stale half-width expectation of 2.5/2px, and run-vs-walk difference 0.04998490u vs 0.05u.
- `worker-flight-width2.log`: 44/45 pass. Old secondary fixture ground=-13, drag=.9 at 1.2u/s has minimum center clearance .0136404px instead of required 2px. It is a real contact behavior, not floating point noise. Actual new anchor ground=-11 needs separate evaluation and is more prone to floor contact.

The specific run-vs-walk threshold deficit is about .0000151u / .000483px; a 1e-4u tolerance would cover that one fixed-window comparison. But its global-clock-dependent snapshot is not a universal separation invariant: matched-start-phase sweeps (24 phases at 0.5s intervals) show baseline 1/.5px width differences .0408898–.0739337u and final 2/1.5px differences .0391565–.0914026u. Both retain run > walk in every sampled phase. Therefore characterize any tolerance as robustness of this particular snapshot, not proof that all phases exceed .05u.

### Independent contact/drag probe (production drag untouched)

Probe command: `dotnet run --project tests/hero-scarf-geometry/Tests.csproj -- --metrics [secondaryDrag]`. It samples 24 start phases, 6-second sequences, stable window 2.5–6 seconds; reports center clearance, exact geometry tip-edge clearance, fraction of samples with center clearance <.5px, mean tip drop and horizontal extension. `-p:ClothSource=<baseline/HeroArcherClothPhysics.cs>` allows identical-chain-input baseline evidence without modifying sources.

| Ground / speed | drag .9 | drag 1.0 | drag 1.1 |
|---|---:|---:|---:|
| Old -13 / 1.2: minimum center clearance px | .00027 | 2.80484 | 3.39026 |
| Actual -11 / 1.2: near-contact samples | 27.01% | 27.01% | 27.01% |
| Actual -11 / 1.6: near-contact samples | 25.79% | 2.29% | 0% |
| Actual -11 / 1.6: minimum center clearance px | .00006 | .03550 | 2.68812 |

Actual mesh tip-edge minimum is 0px at actual -11/1.2 for all three probe drag values; actual -11/1.6 improves from 0px to 0px to 3px. All are nonpenetrating. The widened/low-mounted secondary can remain in the existing grounded friction state at low running speed. Increasing drag to 1.0 only fixes the old higher-anchor fixture; it does not fully solve actual low-speed secondary contact. Recommended decision: explicitly accept the observed lower-tail contact while keeping requested width/separation and frozen dynamics, or separately authorize a wider presentation-parameter investigation. Do not claim drag1.0 solves actual view behavior.

Evidence: `worker-width2-metrics.log`, `worker-baseline-width-metrics.log`, `worker-width2-drag1-metrics.log`, `worker-width2-drag11-metrics.log`. The baseline narrow envelope with new wide geometry produces -1px rendered lower edges in the probe, demonstrating why reverting all the way to old 1/.5px would not be safe.


## Final follow-up: upper secondary anchor preserves separation and run lift

Operator authorized testing the secondary from above instead of below to preserve the original run-lift goal: **SecondaryAnchorPixelsY = +3**, X=-1, length22, drag0.9, phase1.1 and windScale1.15 remain. This is the final source state. Primary anchor/length/dynamics are unchanged; Math remains the minimum sufficient **2px/1.5px** envelope.

Pure fixed-input sweep (`worker-width2-anchorplus3-metrics.log`, 24 start phases, ground=-17) gives secondary minimum center clearance 6.13741px at 1.2u/s and 7.64005px at 1.6u/s; corresponding minimum actual geometry tip edges are 6px and 8px. Both have zero near-contact samples. Walking0.65 still naturally contacts the floor. Drag was never changed in production.

The new actual-view test uses **Create/Tick**, full mesh arrays, actual root+renderer anchor coordinates and 24 real idle waiting periods of 0–11.5 seconds before starting, instead of mutating the runtime clock. Each phase runs at1.2/1.6 for6s, then stops for3s:

- All vertices stay above ground throughout.
- Stable running window (2.5–6s) has minimum rendered lower edge **3px across both chains**.
- Maximum simulation step along either individual axis is **1.7980px**, below the same per-axis2px metric used by the old flight suite.
- Maximum Euclidean step is **2.4164px**; we explicitly do not describe this as Euclidean motion <=2px.
- After3s stopped, largest tip-center clearance over its safe floor is **2.3607px**, within the natural grounded wave band.
- **29 geometry/actual-view checks +33 cloth-view checks pass**. Logs were refreshed after the final anchor change. No old behavioral assertions were altered by worker.

The earlier contact/drag probe table is retained as evidence explaining why the down-anchor candidate was rejected; it is not the final production behavior. Old fixture ground=-13 remains a different presentation setup and should be labeled or adapted to actualground=-17 by Operator/reviewer, not fixed by silently changing Chain forces.

Width qualification: test distances use **cardinal cross-section Manhattan width in source pixels**. A1px cardinal lane can have only approximately0.707px perpendicular thickness on a diagonal (and0.636px after0.9 scale), so the tests do not establish that every fold color occupies one full screen pixel at every instant. No additional widening was applied to hide this distinction. Runtime triangle connectivity/positive area and final preview raster evidence are separate validations.


## Final presentation polish: neck bridge and distinct secondary palette

Operator authorized two final presentation-only changes after inspecting the upper-anchor preview:

1. Physical secondary anchor stays **(-1,+3)**, but its drawn node 0 is lowered by **2 source pixels** and drawn node 1 by **1 source pixel**. Remaining drawn centers are unchanged. `Cloth.WriteMesh` explicitly opts the secondary into `attachmentBridge:true`; the pure helper defaults to no bridge, preserving its generic ground contract. All changes are local scalar copies inside the helper: no writes to `PointsX/PointsY`, no chain length, phase, drag, force, save or lifecycle changes. The visible root center is now 15px above the foot pivot (14px shoulder +3px anchor -2px bridge), matching the neck upper edge, with at most .5px midpoint rounding.
2. Secondary main faces now use existing **E43923 bright fire red**, and fold faces use **89121D shade red**. Primary main remains **BE1C24**. Colors are still allocated/set once at creation, use only the four approved palette colors, and never vary with time.

The bridge affects only the first two segments, whose simulation centers remain close to the pinned root by fixed 22/7px segment length. Even the lowest possible bridged node 1 center is above -22/7-1 = -4.143px local; its width cannot reach the actual -17px floor. Node 2 is unshifted and is also far above that floor. The one-pixel change between endpoint bridge offsets cannot collapse a normal 22/7px segment: its unrounded length remains at least 22/7-1 = 2.143px, greater than the maximum sqrt(2)px endpoint-rounding difference capable of collapsing it. Runtime transient unusual rebase inputs still retain the existing invalid-segment fallback rather than destroying the handle.

Added checks sweep **120 headings** through both bridged segments: valid noncollapsed positive-area faces, intended center offsets, actual-floor safety, untouched input arrays, and unchanged default API. The existing 40-second actual-view run now also verifies that every frame's bridge remains valid and its root center remains at **15px +/- .5 source pixels**, plus actual mesh palette values. These checks supplement the existing hidden creation/freezing/destruction tests; no runtime failure path or buffer topology changed.

**Final verification: 35 geometry/actual-view checks + 33 cloth-view checks passed.** Logs refreshed in `worker-scarf-geometry.log` and `worker-cloth-view.log`. The 24 real-idle-phase run/stop metrics remain unchanged: stable-run minimum mesh edge 3px, maximum per-axis simulation step 1.7980px (Euclidean 2.4164px), stop-3s maximum tip clearance 2.3607px. Actual combined raster/neck appearance remains Operator's final preview check.
