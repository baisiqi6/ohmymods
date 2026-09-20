# Independent Greek candidate review

Reviewed build candidate SHA256 `90B2002DCB801007565DFC2866E75C7BCF8FFF1E02824D55295BD98475E60287`. No outstanding actionable finding in this bounded Greek scanner fallback review. Real Compile Link result: 143/143; baseline: 119/143. Full evidence is in `../greek-tests/RESULT.md` and `receipt.json`.

The change preserves knight and valid `_shootingTarget` fast paths. Fallback is exclusively each current owned follower's `_enemyScanner.GetAll`, using the same Target validation and the follower's current ActiveArrowAttack.Range. After native GetAll it rejects ownership/liveness/authority loss, scanner pointer changes and missing active attacks; current position and attack range are read again. Native pointer aliases remain valid. Buffer access is bounded by count, length and 64; no array is retained and no new scene scan/forced refresh is added. Non-finite range/source/target coordinates fail closed.

The 64 cap is a bounded-work policy: a valid candidate at index 64 or later is intentionally ignored. Test coverage proves index 63 is included and index 64 is excluded; it does not assert the actual native scanner capacity. Existing 8-second duration, 15-second cooldown, RPC registration readiness, pool/asset readiness, authority, pause and cast callback guards are unchanged and remain green.

Any actual tower guard whose native `_knight` has been cleared remains excluded, as required by the explicit own-squad contract. This change solves missing trigger evidence from a stale target when another owned follower scanner entry is valid; it does not enlarge squad membership by appearance, same side or tower proximity.

Verification uses managed API stubs; native IL2CPP and actual visible gameplay require the operator's separate build/runtime validation. This review agent made no production or deployment changes.
