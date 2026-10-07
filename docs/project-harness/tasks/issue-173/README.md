# 原版弓架站定长按只买一份（Issue #173）

用户明确是原版弓架，站定按下只买一份。按 Issue→PR 流程独立修复；本任务仅代码交付，按用户 A 方案等待宫廷 PR 后从最新主线构建一次累积版安装。

## 证据、产生处及责任

诊断时宫廷私有测试副本冻结 DLL 的 HoldPurchase 59 方法与旧372c一致，未含 #169；配置开启并有实际 bind。日志同 price2 目标多次 receipts1 后 out-of-reach，但缺 tag/真实坐标，不能仅凭价格唯一识别全部弓架记录。用户现场坐标与读取异常未测，因此不声称玩家实例唯一根因已确认。#169 修多态终态/恢复统一期限，没有改距离门；本任务基于含 #169/#170 的主线 83ac937，保留两者。

当前2.4 ARM原生 `Player.UpdatePayState` 在 0x7d9fbc–0x7da054 先按 `CanSelect && d<=.5 && d<=r` 保留选中店；失败后在 0x7da058–0x7da09c 调用 `GetClosestPayable(x,.5,player)`。None进入Holding不重复窄门。邻候选关系在 `RetrievePayableIndices` 0x7b4528–4538 / 0x7b4790–47a0 是减法严格 `d-r<range`；最近center不受hard radius限制，不能只凭closest同店允许远程续买。真实 `ShopBow_greece` prefab price2/r1/offset.x.25。

受控场景 playerX1/payPoint.25/d.75 站定，原生fallback可买第一份，83ac937源码随后只买1份并退出。首次偏离是 Mod 把原生保留路径的窄门当成完整续买资格。原生负责选择、投币、钱包/出货、回执/拒绝/退款；Mod只在既有真实hold及成功回执后合成下一次按下，不写selected、不直接Pay、不猜结果。

## 修复及旧保护

连续hold有显式有限空间 `abs(point-x)-playerPayDistance<.5`，使用原生邻候选关系作为Mod保守上限，并非原生完整center/entry选择范围。数字非法/读取故障分别标记，未读数值为NaN。续买entry满足全部既有动作、钱包整价、货架、占用、身份/世界条件后，fast-retention成立时不查询更近别店；fast失效时原生fallback必须返回捕获的同店pointer+实例身份。等待RPC不因forceBlock时CanSelectfalse/querynull误断。

替换旧ShopInReach/entry窄门；保留有独立用途的 #158选择闪断宽限、Baker90秒库存等待、#169多态Injected及恢复期限、RPC5秒上限、owned投币间隔归还。没有新周期扫描、重做交易或持续覆写选择。诊断沿用每会话6行预算，bind/首笔回执/首次拒绝读数有限；历史首次拒绝带捕获时刻，不冒充最终退出原因，scale读取商店自身cachedTransform。

## 验证

固定产品源SHA256：`e06da832a978d64a31bcca2226fec884544bbaa5ab4dc301e898cea2567fa35d`。

- 公共net8测试：`dotnet run -c Release --project tests/hold-purchase/HoldPurchaseTests.csproj`，**103/103**（原82 + 范围14 + 审查反例7）。同套测试换旧83ac源 **18失败/85通过**，核心 expected5/got1；新源站定连续5笔、扣10币、stock5、0误掉地币。
- 覆盖严格边界及相邻ULP、远距nearest center、fast更近别店零query、fallback别店/null/fault、捕获身份复用、RPC回执和原生换店、库存/缺钱/售罄/动作/松手/世界/#169/弹药/归还。fault在native执行前直接核Prefix的payKeyDown与receipt.Injected为false，原生同读随后抛异常另行记录；测试按scenario清日志，不借前例假绿。
- 真实ARM现有interop完整Rebuild **0W0E**；候选DLL SHA256 `241d28a930f03006993f53f41a92dca1eecdc7083a54cdcbb6220a1700127479`，仅构建验证产物，未安装。Cecil scope审计：6817旧方法不变、9处授权变化/11新增仅HoldPurchase及nested、34PNG/338Harmony targets及其余元数据保持。完整资源基线为83ac937。
- GLM5.3 max private worker96用例完成后Root按独审反馈收口7个反例，公共net8保持；独立内置reviewer最终 **Approve（代码交付范围）**；独立运行公共103/103，另加payer POV/r0/overflow/RPC走远单笔履约四项107/107，同107 fixture旧源86通过/21失败。见 review.md。原始player日志、DLL及provider会话仅私有.local，不入公共仓库。

模拟和构建证明静态契约修复与接口可编译，不能证明真实CanSelect/closest组合、真实购弓、联机、Windows或宫廷联动已验。本任务候选未安装，用户A安装归宫廷聊天统一处理，不从公共无宫廷源码单方装机。本任务不修改运行DLL、配置、存档或peer工作树，不操作游戏。

版本账：基于最近正式10.8.35，下次正式发布把“长按续买原生选店范围修复”作为中等修复暂记patch+3，最终整批按完整条目去重复核；内部候选10.9.38不变，无本次tag/正式发布。
