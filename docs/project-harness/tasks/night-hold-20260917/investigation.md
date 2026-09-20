# 2026-09-17 长夜只读调查

用户询问本轮夜晚过长是否逻辑故障。本轮仅读取日志、存档、实际2.4原生代码，以及限定地址的只读进程内存；没有替换DLL、启动/停止游戏、修改存档、强制时间或清怪。

## 已确认

- 本机v9.4.5，正确E盘独立副本，PID47812，23:19:41启动。
- 多次ClockDiag：islandDays86、t3.90、timeScale1，角色动作/射击继续，确为昼夜时钟暂停，而非只有菜单暂停。
- 19:50:35保存的当前战役1/岛9已包含Director currentTime3.904639、clockSpeedModifier0；EnemyManager redMoon=true、darkness=false、endlessNight=false、retaliationsQueued0；Kingdom unsafe。因此冻结状态早于此次23:19启动，不能认定为9.4.5新增功能导致。
- 实际2.4原生ShouldPauseTime RVA5022B0，检查危险、时钟非零、黑暗或血月暂停区间；CheckDanger RVA4FE5E0，在威胁解除且王国尚不安全时调用ResumeTime RVA4E5790并清血月/黑暗。
- 随后现场自然恢复：ClockDiag行994 t4.01，998 t4.50，999 t5.00；继续日间，到行1306 islandDays87 t0，后续第87天t5白天。不是本次永久死锁。
- 23:35:11只读内存核对（对象类名匹配，未调用游戏方法）：t2.0464385、clockSpeed1、redMoon/darkness均false、retaliationsQueued0。蛇仍Spawning，dangerDistance10，敌人safeBorderDistance40。

## 根因边界与可修风险

直接机制为血月等待清场；尚未采到停钟期间每一项危险来源、远程吐怪连续计数，不能证明这次唯一拖延来源。

现有PatchWorld_SerpentLeash.TryRemoteProximityWave只看IsNight/口门/冷却，不排除血月停钟阶段。现场日志cooldown1.5，而MOD每2秒检查，可在血月清场等待期间持续追加远程近距波；这会延长危险状态，存在反馈循环风险，但本轮最终能自然清场。其“每岛日一次”字典只约束首波提前补偿，并不约束正常补怪。

另有已确认配置时序问题：NightStartTime写死17.5，实际本轮19.56仍白天、21.51黄昏、22才IsNight；这是提前补偿边界风险，不能当本轮停钟的已证根因。

后续最小修正可在血月黎明停钟清场时抑制MOD额外远程注入，保留原生口门队列、近身反应、敌人死亡和自然恢复。不得直接ResumeTime、删敌人或写存档。需要行为回归与真实血月验证后才称修复。用户本轮只询问原因，当前没有实施该修正。

FriendlyTroll不是Enemy子类，未发现MOD将其注册进敌方集合的证据。实际2.4 CheckDanger额外遍历段条件为Director.IsDaytime且非血月/黑暗，来源Kingdom+238；不得误称夜间独立敌人遍历或据此归咎友好巨魔。

## 证据

本机Operator目录：`C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/night-hold-20260917/`。
包含LogOutput.snapshot.log、clock-samples.json、save-summary.json、live-clock.json、原生入口/短getter的只读反汇编与源DLL哈希、只读内存脚本。无全内存扫描、无远程执行/注入。

独立只读复核：night_hold_reviewer，结论一致；没有把代码分析当成实机修复验收。
