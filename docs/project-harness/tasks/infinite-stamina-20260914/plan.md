# Optional infinite mount stamina

User requests a panel toggle. Implement defaultoff, allworlds, current locally-controlled rider's mount; no remote-player override. Preserve all previous changes including author pixel fire candidate34182546 and public7.5.0. No new commit/release requested.

Worker localOMP deepseek/deepseek-v4-flash max, isolatedmodule+tests;root addsModConfig Player.InfiniteSteedStamina and F5Convenience fourthcard, performsbuild/native/API/regression/integration. Independentreviewer.

Targetactual2.4 Player.UpdateActionState nativeRVA0x699eb0,4240bytes to next method,unique slot. Stamina get/set are16bytes and must remain unhooked. Prefixrefill currentSteed/clearfatigue; protect within-call negative movementdrains and reliably restore own temporary rates viaPostfix/Finalizer. No DebugInfiniteStamina globalcheat flag, permanentprefab changes, extraRPC, skillCD/WellFed/speed edits. Disable lets currentfullstamina drain naturally, not restore stale pre-enable exhaustion.

Tests: off/nativeequivalence,Run/Glide/extremedelta,exhausted enable,localclient/remote guards,split screen,swap/world/unmount,exceptionandnestedcleanup/CASexternalchanges. Build/API/nativehook/checkexistingmethods preserve. Installonlygameclosed; correctEBuild22992091 only. Defaultoffstartup and appropriateactivepath evidence; don't claim longallmount/online gameplay without testing. Save/config preserve; no userprogressrollback.
