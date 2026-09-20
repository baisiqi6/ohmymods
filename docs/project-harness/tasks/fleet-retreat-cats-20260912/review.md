# Final limited fresh source review

**Source verdict: PASS — no unresolved blocking finding in the reviewed candidate.** Both P2 findings from the first pass are resolved. Fresh focused regression evidence also passes: **91 fleet cases + 63 retreat/cat cases = 154 cases, zero failures.** This is source and managed-test acceptance, not live IL2CPP/game acceptance.

Reviewed six candidate files: FleetGreekSquads.cs, PatchWorld_FleetBoatFormation.cs, SamuraiRetreatSpeed.cs, PatchWorld_DefenseSpacing.cs, PatchWorld_FarmCats.cs, PatchRoles_KnightStyle.cs. Reviewer performed read-only source/evidence inspection and wrote this report only; no canonical/game change, launch, build or installation.

## Source findings resolved

1. Native ActivateFormation exceptions skip Postfix. Finalizer now calls Complete before optionally restoring the empty baseline, preserving the original exception and releasing even already-embarked pending candidates. Maintain can remove genuine partial registration with null currentFormation after checking actual membership, while leaving a different non-null formation alone.
2. Cat retirement exceptions now reconcile actual inactive/destroyed state and count completed removal. Still-active or unobservable partial failure marks the result uncertain and stops that farmhouse's deletion pass. Other farmhouses continue; picked-up/following cats remain ordinary protected skips.
3. Reservation maintenance preserves the native boarding transition: OnEmbarkStart may make the knight stationary before IsEmbarked becomes true. A previously validated exact own-boat target remains valid; stationary without a target, manual control, charge, grab and puzzle takeover still retire the reservation.

## Behavior and authority review

- A finally-restored NativeProbe is limited to candidate discovery. Actual recruitment retains native checks and the reservation gate.
- Assigned Greek squads are matched first, then eligible free squads, globally one-to-one across players. Other boats, main-boat targets and existing formations cannot supply a free squad.
- Legacy non-Greek embarked passengers retain native same-target scores, and their boats are excluded from new selection. Main-boat, worker and stowaway scoring paths remain native. Unknown/multiple Knight-slot prefabs are skipped via `_numSquads != 1`.
- Zero matching pairs replaces the original boat slot with Gap; native recruitment cannot fill an unreserved slot. Matching is bounded by min(eligible boats, eligible available squads), with the existing maximum of four. This is a planning upper bound; boarding still depends on native assignment and reachability.
- Prune/score callbacks only change private metadata and queue cancellation. The existing coordinator performs native UnregisterUnit after authority, world, ownership/replacement-plan and membership checks; native OnLeaveFormation owns return/disembark behavior.
- Unboarded plans expire after 60 scaled game seconds, while boarded plans do not. A boat can be empty while its reserved squad approaches; the contract is no unpaired recruitment and eventual withdrawal of unfulfilled plans, not literally zero empty boats at every instant.
- Knight activation releases pooled-lifetime reservations; world supervisor startup clears metadata. Authority/world loss cannot trigger queued foreign-world or nonauthority mutations.
- Style2 retreat x3 changes only the local ref speed at native GoToWall/isRetreating and native baseline. Busy/embarked/invalid paths are excluded; recursive redirects and already-adjusted values do not multiply again. No actor speed field is rewritten.
- Cats target four and protect unmarked/native/picked-up/following cats. Online sessions are skipped entirely. Protected or native surplus may keep a farmhouse above four by design.

## Fresh test evidence, separate from source verdict

**Fleet: 91/91, exit 0**, `boat-tests/run.log`, `boat-tests/hash-receipt.json`, and `boat-tests/RESULT.md`. The actual helper is Compile-linked; actual candidate-discovery and activation-Finalizer methods are extracted unchanged. New cases execute native-exception finalization with an already-embarked pending candidate, partial null-currentFormation registration, foreign formation protection, successful membership retention/pending completion, success-finalizer inertness, and stationary own-target versus no-target boarding transitions.

**Retreat/cats: 63/63 (43 + 20), exit 0**, `tests/partial-final-receipt.json`, `tests/partial-final-retreat.log`, `tests/partial-final-cats.log`. Before/after hashes match current sources including the updated DefenseSpacing world clear. New cat cases cover Pool deactivation followed by exception, SetActive deactivation followed by callback exception, active deferred partial failure stopping only its farmhouse, and unobservable post-exception state becoming uncertain. The earlier 84/59 logs are superseded and are not used to claim these new cases passed.

Operator reports final canonical compilation at 0 warnings/0 errors. Reviewer independently confirmed all six canonical source hashes equal the reviewed candidates and canonical DLL SHA256 `F32CBE8C769D0D9818EF8143E5F6E32FCA2F5AD6B3C42CC13D1F81BA21C73020`. `build/interop-final.txt` reports **70** reachable managed methods with no unstripping-failure stubs (the earlier audit had 69); `build/hooks-final.txt` reports four unique hook targets and valid finalizer return types. The reviewer inspected these final audit outputs but did not independently rebuild the candidate.

## Reviewed final SHA256

| File | SHA256 |
| --- | --- |
| FleetGreekSquads.cs | 8457993075B76C089E211FC41028FDE0772A6DFFD002B3FB51E31838DEAE31AF |
| PatchWorld_FleetBoatFormation.cs | EE3C98DA1AF02242BA2E6DC50522E6B5E6409FCDC66345775E7B18AD94E27178 |
| SamuraiRetreatSpeed.cs | 3E5CC530619ECD0B601B09B15399EE2241BC02B3AC91503398CB2C6D9689B14D |
| PatchWorld_DefenseSpacing.cs | E981D90B67BD1190D6919A123DB015BBF82F96E8792461404BB4D322DCD5FF17 |
| PatchWorld_FarmCats.cs | 0A6C9435DAAC550CBA13939946FC401E5351ECB350EF4B398647B1E8CE2E8884 |
| PatchRoles_KnightStyle.cs | DE45BA166D1F9E84E27C6D67B8D66A4A1C52B059C2499D0A6FDDB654917D17F8 |

## Acceptance boundaries

Fixtures execute production managed control flow but do not execute IL2CPP detours, the native registrar solver/animation/return callbacks, save reconstruction or networking. Full ActivateFormation integration is source-reviewed; only its extracted Finalizer is executed in the exception fixtures. Actual prefab seat layout, complete boarding/departure/return behavior, save persistence, and multiplayer presentation require controlled runtime acceptance. Previously reviewed samurai visual source is not changed here; no successful in-game visual trigger is claimed by this source review.

