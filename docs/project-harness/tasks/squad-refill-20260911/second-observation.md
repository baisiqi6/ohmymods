# 第二轮玩家日志 — 2026-09-11 22:35
归档operator workspace squad-refill-20260911/player-second-probe.log；游戏仍运行，未部署或终止。

sample2/5：22骑士，五style汇总名册88/88、alive88、mismatch0，全部far10=0（上次7名离骑士>10的现象本次已消失，不能判持续走散bug）。readMs0.26，不含logger/整帧耗时。

随后明确出现北境补员成功证据：style4 owner#-110216 roster3->4/4 requested1 range10 accepted29 within27 nearest0.1 minFree2 reserveDecrements9 effectiveReserve0 rejected111 occupied87 guard15 crossbow13（无截断、无额外validator）。前一行NorseSquad converted follower证明转换链也执行。观察到此前follower diag norse23而其它style人数未减，之后补至4。OnDisable deadFalse不可独立认定真实战死，但3->4证明有缺额时原生招募+转化在本次可以补员。不外推为其它style战损全部验证通过。

另外两项确凿运行缺陷：
1 GreekFire asset=Buff_fire_attacks_hephaestus_anvil id5 originalDuration3 cloneDuration8 candidates1；随后archer fire-arrow asset/pool not ready，三次cast recipients1，两个Greek骑士这几次仅给本人附魔。不能细分是fireSO/prefab/pool缺失，因为现guard日志合并；需只在失败时精确拆解并修复依赖，不能盲目取消poolguard。
2 MedievalNorse arc build failed MissingMethodException Il2CppSystem.ReadOnlySpan<T>.GetPinnableReference，栈UnityEngine.Renderer.set_sortingLayerName -> EnsureArcBuilt。剑风特效真实兼容错误，需要改用实际可用的sortingLayerID数字路径或核runtime依赖；不更新/替换整套BepInEx作为猜测修复。

末尾ClockDiag t6.15 timeScale0，再次暂停。本轮仅读取与记录，未修上述缺陷。
