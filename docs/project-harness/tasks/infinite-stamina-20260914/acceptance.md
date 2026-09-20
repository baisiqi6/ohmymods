# Optional infinite mount stamina — local candidate

Implemented user request with F5 → 便捷 → 坐骑无限体力 (firstcard, conveniencepage now4cards). Config Player.InfiniteSteedStamina=false bydefault. Allworlds, locally authoritative rider only, including localclient and local splitscreen semantics; remoteplayer mounts skipped. Disable resumes native drain from current stamina, without replaying an old exhaustion state. Prior author's pixel effect and all earlier work preserved. No new commit/release, public7.5.0 unchanged.

## Implementation

Player.UpdateActionState prefix refills currentmountedSteed and clears_tiredTimer, borrows only negative run/walk/stand/glide rates as0 during originalcall, then restores ownoriginalvalues usingCAS. Reference Borrow+Cleaned ensures exactlyonecleanup throughPostfix/Finalizer. Identity+local-authority+bidirectionalRider/currentSteed checked beforewrites; cleanup still restores the captured oldinstance afterworld/switch/authoritychanges. Per-field exception guards prevent onefailedrestore from blockingothers or hidingnativeexceptions. No permanentrate/profile/asset writes; no DebugInfiniteStamina globalflag, extraRPC, cooldown/speed/WellFed modifications.

Three skill-side prefix/postfix refills use capturedability/steed/rider identity: SteedAbility.Activate, GlideMovementSteedAbility.Activate, RunningAttackSteedAbility.OnPushedObjects. Originalcost,damage,CD,duration,movement/saturation effects execute normally; stamina restored before returning to nativeRun's fatiguebranch. Off-entry/on-exit and swappedtarget rejected. Nativepuff synchronization left to originalPlayer.Update.

Actual2.4 nativeaudit: Player.UpdateActionState RVA0x699eb0 (~4240bytes,nextmethodbound) unique; baseActivate0x7961d0/272,Glide0x77f2a0/784,Running.OnPushed0x794480/496 unique. All12 ordinaryderivedActivate implementations call/jump base, no missedinlineStamina writes. Do not hook1098-way sharednoop methods or16byte Stamina get/set.

## Verification

OMP deepseek/deepseek-v4-flash max worker in isolatedfolder/same resumed session; independentreview reported then verified lifecycle,authority,skills andtest-scope corrections. Root also made class-level Harmony target explicit, fixed both exclusion-test helpers to reset feature/scope, and guardedUnitynullcheck insidecleanup. Final forcedRebuild:25featurecases/254assertions pass. All29 executable regressionprojects pass; one additional Library project compiled againstactualgameinterop (runner initially trieddotnet run onLibrary, then correctlyvalidatedwithbuild; no hiddentestfailure). Fullmodbuild0W0E,27reachableactualUnityAPI paths no unstripping stubs.1917 unrelatedmethodbodies unchanged, including pixelFX; onlymodule + config/panel/buildstamp change.

Installed while gameclosed, exactE Build22992091 only. CandidateSHA 7614e05c0722f452d397270df5643f0ca55b723c4d6935811ba1d59fc2cf962c, old34182546backup. Startup exactcandidate wasvisiblePID30448, native4long entries readFF25; Stamina getter/setter original16bytes. Logsno module registrationfailure/fallback; defaultfalse preserved. Gamepaused atClockDiag t6.93/day71/timeScale0. Existingcfgvalues deep-equal prior, onlynewdefaultfalse added; existingImpactEnabledtrue stays. Save not manuallyedited/restored; currentnativeprogressSHA ca34c7dec5a30de8436b9f4ead9bb4fc3a69b37eaf1e8c47bda9edf0b50d9d3f.

## Remaining runtime acceptance

This startup used defaultOFF. Actualenabled long running/gliding, diversemountskills,enable-disable visualpuff,realmountswap/worldchanges andonline-two-machine tests remainpending; automatedtests andnativehookinstallation are not a claim of thosegameplayresults. Keepfeatureharnessdoing until thatevidence exists. Exact externalratewrite0 during ourborrow cannot be distinguished from own0 byCAS; ordinarynativepaths audited, arbitrarythird-partymods not guaranteed. GenericUI screenshots unavailableonhost previously; codepagecardcount checked, no pixel-layoutclaim.

Evidence in operator infinite-stamina-20260914: native/ability mapsanddisassembly, worker/WORKER.md+sessions, stamina-tests.txt,test-build.txt,test-results.json,build.txt,api-audit.txt,unrelated-methods.json,install-receipt.json,runtime-native-hooks.json,runtime-receipt.json,runtime.log. No temporaryruntimeprobe installed.
