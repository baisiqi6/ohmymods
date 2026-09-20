# Tower overlap final source review

**Final source verdict: PASS — no unresolved blocking finding in the frozen operator candidate.** Fresh independent regression: **46/46 pass, exit0**, with identical before/after source hashes. Worker red cases and earlier operator runs are superseded evidence; they are not represented as final acceptance.

Reviewed `build/PatchWorld_TowerSpots.cs`, SHA256 `381D96464ED3330C4509580F562ED22CEBA3DA1B4E1CC35E512E9C71CF5C5B0C`. Read-only reviewer; no canonical change, build, deployment, game operation or save mutation. User's running game is untouched by reviewer.

## Source and geometry

- Each Expand builds a local snapshot from the existing Tower tag scan and one WorkableBuilding plus one Scaffolding scan. It includes ordinary built/native bases, untagged special buildings, active scaffold roots and explicitly linked inactive Building roots. Pointer de-duplication does not lose the scaffold association.
- Occupancy checks revalidate current layer/scene/active/boat status. An inactive Building requires the still-active current-layer scaffold to still point to that exact root.
- Old cleanup measures the actual spot, preserving its real offset/scale. New placement measures the same live native template used for SpawnSpot's transform copying, while the asset supplies placement metadata. Same-type MinSpacing is not reintroduced as a blanket density veto.
- Finite positive visual bounds are combined with valid payable/native rectangles. Empty child bounds do not pull spans to world zero; non-finite/unknown footprints are not interpreted as empty space.
- OverlapResult.Unknown blocks placement but cannot by itself authorize deletion. In cleanup, a confirmed root overlap or a native-tag collision with blockedTag other than check-error is required; a genuinely overlapping retained generated peer is also independent valid evidence. Thus native check-error plus otherwise-clear/unknown occupancy does not produce an unsupported deletion.
- Safely removable peers are skipped in the initial full-root collision pass, then processed in stable x order against retained peers. Native/built/paid/selected/construction/ineligible KEM actors stay obstacles. Successful newly created spots enter both root and x occupancy immediately.
- Removing one old root removes only one x entry, so a distinct building at the same coordinate remains represented.

## Protected state and exceptions

Deletion requires marker, active/current scene, nonboat, level0, Persistent and SemiStatic header, expected PayableUpgrade, no root construction/workable/scaffolding component, no payable selection/interaction and no related selectedPayable or _completingPayable on either player. The complete guard runs again immediately before native deregistration.

The review found unsafe false defaults in IsInLayerScene/IsSameHierarchy/IsOnBoat. These are now corrected: query errors propagate to the caller, where occupancy becomes Unknown, CanRetire returns false, and player-engagement checks return true. The added current-layer active guard also blocks a deactivated layer even if its pointer has not changed.

An exception from retained-peer IsLiveOccupant can abort the entire cleanup/Expand via the outer delayed-callback catch. This is conservative interruption, not unsupported deletion, and is acceptable. A partial native retirement leaves unresolved active roots in occupancy; completed deactivation is counted even if a later callback throws; further deletion stops after failure. No direct save or manual native collection rewriting is introduced.

## Scope and nonblocking observations

Cleanup runs before multiplier<=1 and native-reference-count<2 exits, but global disabled/online/noauthority/invalid-context gates still precede it. No new native hook or periodic scan was added.

The original full-Expand world/layer guard remains. Cleanup-only early-return paths do not mark that guard, so a duplicate same-world OnLevelLoaded callback can repeat those scans. They do not schedule themselves or run periodically; this is a nonblocking lifecycle/counting boundary and should not be described as a strict one-scan-per-world guarantee on all branches.

Only structures present during the delayed load scan are covered. Later same-scene upgrades can enlarge a tower after the pass; permanent prevention across all later upgrades is not claimed. Unknown geometry can conservatively suppress extra placement. Real old-save cleanup, purchase/upgrade/scaffold behavior, visibility and resulting saved state still require controlled gameplay acceptance.

## Evidence status

Operator reports actual-reference candidate compilation0 warnings/0 errors. Reviewer inspected tests/final-receipt.json and tests/final.log:46 passed,0 failed, exit0; before/after SHA256 both381D96464ED3330C4509580F562ED22CEBA3DA1B4E1CC35E512E9C71CF5C5B0C and match the current source. The complete production file is linked into the managed fixture. New cases verify scaffold deactivation/repointing invalidates old inactive-building evidence; unknown occupied bounds cannot authorize cleanup; one root removal preserves another same-x root; payment starting after collision proof is protected by final recheck; occupied layer/scene lookup failure yields conservative behavior; selected/completing child-payable hierarchy failure preserves the candidate; and an inactive current game layer cannot be mutated. The final comment-only adjustment correctly distinguishes the observed3.0-wide sprite from native MinSpacing. Reviewer inspected evidence rather than independently running the suite. Managed fixtures do not execute real native callbacks, persistence, game animation or the five-second load pass; those remain runtime acceptance boundaries.

