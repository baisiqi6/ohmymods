# 剑风数组互操作修复验收 — 2026-09-13

用户要求先修复已定位问题；已集成canonical源码及测试，E副本已装热修。未commit/push/修改公开6.0.0资产。

- 原因证据：玩家5.0日志EnsureArcBuilt→SetPositions(Il2CppStructArray)→Span ctor→ObjectCollectedException；6.0同源码路径。实际2.4 wrapper标量SetPosition直接native invoke。
- 主变化：预设13点，再用原坐标逐点写入；原材质/排序/几何/伤害/生命周期不变；成功后一次日志。
- Worker报告旧调用注入故障对照26pass/5fail，新实现31pass/0fail。Operator在canonical直接源码项目重跑31pass/0fail，覆盖正常/备用坐标、重复20次攻击与disable复用、第0/6/12点故障后清理重试。这是故障边界模拟，不是IL2CPP GC复现。
- 完整IL2CPP构建0W0E，actual2.4 API递归检查109方法无unstripping失败；所有candidate生产.cs与canonical哈希相同。
- 独立只读reviewer release600_review PASS，无新增P1/P2；原子隐藏构建、清理与复用保持。
- 实际E游戏已完成13点SetPosition剑风构建，成功日志出现，无原异常。 运行时间2026-09-13T01:14:29.3495530+08:00至2026-09-13T01:18:04.3300691+08:00，停止原因：native wind arc 13-point construction observed。
- save前后4E0E22D9951774D45B90B57A0F9D9A6C245BF7E1BA86BC05F06CB93022F6C468一致；bank 5305→运行后5333→恢复5305；config精确字节恢复。仅停止Operator自启进程。
- DLL SHA256 3399c131f9c9f1e070ba9abd18e52489aacfa98e4511f4c2e90f076990698cb8；生产补丁SHA256 21704f9663d854b92663cd3432326cb304a444b48a7bb131a67851d3d2b61369。

私有详细证据位于本机任务目录wind-arc-interop-20260913：build-results.txt、canonical-regression.txt、line-wrapper-audit.txt、unity-api-audit.txt、WORKER.md、runtime-receipt.json与两份runtime日志。没有发布存档/配置/玩家日志。

范围：只解决已定位剑风数组/Span调用链；Griffin InvalidNetID/PositionSync尚无根因证据，未推测性修改；本地日志不替代目视与联机验收。
