# Issue223 — 旧档火枪手授权补偿

Mac Max / Codex · Maintainer Operator · IL2CPP 2.4.0 r23485。公开源码基线 PR204 merge38e1a67e；正常累计母版575。Issue223保持开放，源码PR与本机候选、安装、实机验收分开。

## 问题与范围

枪架和原生付款回调已注册，但旧档加载为unknown-paid、缺少精确epoch/baseline，因而禁止继续收费。历史身份记录不等于当时实际存活兵力；保护规则早于PR204，最早失配原因尚未确认。本次用户选择对这份旧档一次性补偿8名全新火枪手，保留17条原历史，恢复以后正常购买。不是所有未知、损坏、未来或只读档按一半自动发兵。

## 实现与边界

私有Plan限定request/context/源epoch/源档SHA与历史数量，当前授权固定8。场景就绪后注册源Archer池、创建独立原生单位和新GUID，不征用居民、不猜旧NativeId。保存屏障在任何生成前取得；Pending只在内存，临时schema2空baseline不能写盘，包括zeroProtected路径。生产Save/GetID采集完整八行后才能建立schema3 Complete回执，旧epoch留作历史；普通档保持schema2原编码。

原生scene采集先于SaveAsync；完整回执/八行同payload，经实际disk/proof与paired备份确认才解除支付锁。旧zero-rights callback不能借后来的盘档确认发放。授予预算不可因死亡、掉枪或重进重置。出生ticket与Global/prefs/filename/世界生命同一性检查阻止复用与跨owner操作。已完成外部自动存档也可正常结束补偿，不再多请求一次SaveAsync。未知生成产物或capture后的失败保留可见保护，须完整退出重启；仅能证明清理全部本次对象且CAS撤销成功的capture前失败允许恢复原未决状态，本场不自动重试。

## 已验证

- 纯规则/codec202、Archive21、Runtime311、实际Identity/Persistence348项通过；历史future fixture仅3→4。相关保存/库存/守家/商店回归通过。
- 源码反例：无grant Complete、zeroProtected partial write、旧zero-rights callback、外部autosave收尾、匿名spawn、owner三元组变化、选中pending岛无native门；均保留失败与修复证据。
- 独立源码复审通过。GLM5.3/max方案和恢复边界审查证据留本机私有任务，不把Worker自评当Reviewer。
- 当前本机累计.NET6、累计.NET8与公开源码.NET8编译0警告/0错误。累计候选36faec77叠加六个本次文件，保留CanonicalHashBuffer优化；38资源与非本次类型/字段/方法IL相同。原地图/HUD/储存钻石/字体性能/盾卫/宫殿保留。

## 尚待验证

真实八名出生、完整原生采集和物理存盘、冷进程重载不再发、实际枪架付款仍待Mac隔离验证；源码/host合成行不证明原生ABI与玩家验收。Windows与Android游戏未验证（仅匹配现有Android路径shim条件编译）。正常安装须另过精确候选/Plan/恢复/安装脚本门。未合并此PR、未关闭Issue、未正式发版或重复计算本轮返修。玩家日志、真实Plan/GUID与原始存档均不提交。
