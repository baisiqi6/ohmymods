# Three knight style powers — 2026-09-06

User scope: implement agreed medieval, Norse and Deadlands powers. Samurai current prototype and Greece untouched.

- Medieval(style0): melee reach1.5x provisional operator choice (no prior exact multiplier); extend forward damagebox, preserve rear/height and nativeSlash damage/dedup/fire/animation/network. No independent slashFX exists: preserve native slash and add local cached short arc as cosmetic only, using native trail material or verified fallback. No body/root scaling.
- Norse(style4): own knight coin capacity and retention/tax threshold2x; statue-target2x; never add or remove currency. Possession changes _wallet to player wallet: modify only verified own _originalWallet. Recover on stylechange/configoff/disable/poolreuse; preserve existing surplus as native does. No customsave orRPC.
- Deadlands(style1): Knight + current followers attack cadence2x and movement1.5x relative current package. Existing crossbow follower reload2x becomes1x native after halving, not a second unexplained bonus. Include prep/interval/finalcooldown and compatible animation; no effect on standalone crossbow. Preserve sharedformations/nativebuffs.

Delegation: ZCode0.16.5 GLM-5.3 requested max (verify native model; effort may remain unverified). Two isolated snapshots with new-file allowlists: PatchRoles_DeadlandsPowers.cs and PatchRoles_MedievalNorsePowers.cs. Operator holds canonicalintegration/build/deploy; read-only subagent scout/reviewer permitted by user. No commit/push or releaseZIP update.

Acceptance gates: inspect live2.4 interop for exact hook/member names and unstripping stubs (old2.1 source only logical reference); no fullscene polling/log flood/sharedprefab edits; baseline restoration and no multiplicative drift; deadlands waits/cooldown compose with old crossbow package; ownwallet identity; native attack delegation. Build0W0E, independent review, appropriate regression checks. Deploy only E test after game-exit+backup+hash/atomicreplacement; runtimevisual/combat/readload/online remain pending until observed. Preserve existing UI/HUD and all unrelated dirty changes.
