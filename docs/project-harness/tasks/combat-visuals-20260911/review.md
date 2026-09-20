# Final independent combat visuals review

Verdict: PASS for the bounded integrated change. No blocking P0-P2 source finding.

Reviewed source SHA256:
- SamuraiDashVisuals.cs: 980137C8606D27ABDAAC47D91ABBFEDC5D4F475AD44DD4D5A1C781BDBDEB8173
- PatchRoles_SamuraiPowerDash.cs: 3D35E55C021A16AB2242E2A9E4911735585B0F4712590546A4A6E13DB4ECD715
- PatchRoles_GreekFireAssets.cs: 958CE9EBC92774C42833DE7C7BE78647A486E8700E085B56413A615EF9A65117
- PatchRoles_GreekFire.cs: 3607BA796CF1D0D384124C9F075761C427EB1310DF3ABAD0F65BF1B9E907E1A4

Also reviewed the KnightStyle integration diff and the single numeric sortingLayerID change in MedievalNorsePowers.

## Confirmed behavior and ownership

Four self-owned SpriteRenderers are bounded per knight, under a separate world-identity root. Ghost pose is copied only at emission, so later owner/root movement does not drag historical images. The fourth white overlay tracks current sprite/pose and is disabled at motion effect termination. Sprite, flip, signed scale, world position/rotation and numeric sorting layer/order are copied; original renderer material, MPB and color are not written. Materials are references; MPBs belong to helper renderers. Three slots reuse recency-ranked .45/.25/.10 opacity with .2 scaled-second fade. No motion-frame renderer/material allocation occurs after construction. Partial construction failure retires the owned root.

Begin/End uses token identity; old End cannot terminate a new motion. OnDisable prefix clears visuals before native/gameplay cleanup callbacks. Replacing source/owner/root at Begin retires old resources. Driver lifetime and root lifetime are distinct: driver persists, visual root does not persist across scene unload. Configuration/style/death/source invalidity removes effects; cleanup precedes pause handling. Original GlowOverlay scheduling is removed, avoiding broad manipulation of native SpriteFX routines.

Greek RestoreMissing is both-peer, null-field-only data repair with live/dead/applicable checks; Ensure adds Greek owner scope for casting. It maps the base ArrowAttack through the current biome and maps its projectile consistently with native Spawn before checking the existing pool. No new pool, RPC, buff, expiry or ActiveArrowAttack mutation is introduced. It rechecks world/biome and nullness before writing. Non-null original instance data is preserved. Existing ConvertToSoldier/Hunter postfix entry invokes it before owner/marker gates, addressing the client DeserializeFromData ordering; the existing patrol covers paths with already-correct skins. Missing pool fails safely without inventing a registration.

Medieval sword arc now uses sortingLayerID instead of sortingLayerName, avoiding the verified ReadOnlySpan.GetPinnableReference string wrapper failure.

## Evidence and limitations

Reviewed operator-final-receipt.json: before/after source hashes are identical to those above. Final motion 79/79, Greek 79/79, visuals 12/12 passed (170 total). Source was not changed by this reviewer, and tests were not rerun by this reviewer.

The new visual emission follows host-side motion only. This does not establish new afterimages on network clients; native damage and movement remain their existing paths. Do not claim complete client visual support.

Visual shader output, transparent sorting, signed scale under the real transform hierarchy, and actual frame cost need game inspection. The final driver retains lifecycle validation each frame, but skips SourceReady and all renderer/material/property reads for idle owners. Thus idle cost is reduced and bounded by stored owners; it is not literally zero. Readiness and missing-source logs are once-only and their logger calls are isolated from gameplay.

If the first client's fire deserialization occurs before the effective pool is ready, RestoreMissing refuses the write. A later field-only repair does not repair a previously selected ActiveArrowAttack=null. The tested first-client path assumes pool readiness; remaining loading-order cases should not be represented as proven.

All performed actions were source/evidence reads and this review report. No canonical/game/config/save mutation, deployment or game launch was performed.

Final small-change closure: independently reread the 980137C8 visual source. Idle early-return is after lifecycle cleanup but before SourceReady, preserving cleanup while avoiding idle material reads. Once-only success/warning output safely catches logger exceptions. No token, pose, opacity, fade or network behavior changed. Operator-final three suites again pass 170/170 with stable matching hashes; no additional finding. Scope remains local/host visuals, with client visual synchronization requiring separate design.
