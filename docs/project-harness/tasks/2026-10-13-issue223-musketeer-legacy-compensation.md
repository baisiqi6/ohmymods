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

## Mac 原生验收与正常安装

2026-10-13 在独立副本用实际2.4.0原生进程验证：首次创建8名全新火枪手，生产场景序列化、物理存盘和配对备份中均有8个独立身份及对应Archer行；Complete回执只有一份，旧17条历史及其他上下文完整保留。实际枪架付款完成1次，新增付费身份在保存退出后的冷进程中延续，并在两轮原生档中各对应唯一角色行。第二次付款在暂停时取消并退款，不计为两份购买通过。

冷进程真实加载为exact、bound=8、pending=0、conflict=False，未重走补偿生成或保存请求。测试中出现掉枪和兵力损失，回执与8名授予预算保持，未补发；历史NativeId变化不能解释为全部阵亡。同场测试证明该旧档的购买恢复与存读档，不替代多人、换岛和所有失败路径验收。

退出后的两次严格归档校验失败均保留：第一轮为偏好晚写，第二轮在UI读取退出窗口时出现额外无测试参数进程并改写测试共享数据。额外进程已按精确身份关闭，其输出另行保留，未作为冷读通过证据。恢复仅使用测试前的完整原件；五通道字节一致、无游戏进程及active-case已由独立复核确认。

正常版本已于15:53 UTC安装累计候选36faec77及固定8名的私有Plan，旧DLL、配置和共享进度先行备份。安装器独立复审、GLM5.3/max精确安装审查及前后launcher check-only均通过；实际只改变DLL、SHA256SUMS和新增Plan，普通配置、ModSave与共享进度保持。补偿在玩家下一次加载授权旧战役的目标岛时执行；正常档尚未替玩家启动，不能把隔离生成的8名声称为正常档已到账。

私有证据保留在本机任务目录：原生验收、五通道恢复、安装决定及install-receipt分开记录。PR224保持Draft、Issue223开放；Windows与Android游戏未验证，未正式发版或重复计算本轮返修。玩家日志、真实Plan/GUID与原始存档均不提交。
