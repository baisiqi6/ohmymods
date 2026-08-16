# save-repair-017 — 当前存档火焰塔隐士 Passenger 所有权恢复

## 授权与目标

- 用户明确授权对当前 `global-v35` 执行一次性高风险修复：仅恢复缺失的
  `HermitType.Fire` 所有权，使下次读档由原生 `CampaignSaveData.ApplyAppearance` / `SpawnNearP1`
  路径让 P1 携带一位火焰塔隐士。
- 不创建第六座小屋，不手工插入 Hermit/CRPC/NetID 对象，不重放购买、宝石、动画或任务奖励。
- 用户已退出游戏并同意采用原生 Passenger 规范。2026-08-16 14:28 +08:00 重新确认
  `KingdomTwoCrowns` 进程为0；即时输入长度=817,111 bytes，SHA-256=
  `C3A8CEF5B3B59B0C4A763235B138381ED6327ABAAA2311F95530624AC17E55E8`，mtime=
  `2026-08-16T14:27:59.1672660+08:00`。后续每一阶段仍须重新核对，禁止追随变化后的输入。

## 原生语义与唯一允许改动

- 仅限活跃 Call of Olympus campaign；退出后从即时存档读取 currentCampaign、reign、currentLand。
- 退出后的即时存档必须严格验证：schema16、`_currentCampaign=1`、campaigns恰2项、目标campaign
  `biomeIndex=5`、`challengeId=0`、`reign=1`、`currentReign.isCurrent=true`，且campaign/currentReign
  的 currentLand 都为6并相等；任一值变化即停止并重新审查，不自动追随。
- 两份 `hermitStatuses` 必须各恰7项且起始整体 DeepEquals；2.4枚举证明 Fire=index6、
  Passenger=5。Fire两层起始必须精确为 `{position:0,player:0,land:0}`，并且全部7项中当前无任何
  Passenger，避免覆盖P1已有乘客。
- 扫描目标campaign全部岛对象：Dynamic `crpcType=1 && netID=980` 必须为0；name/prefabPath/uniqueID
  任一包含真实资源名/标签 `Hermit Fire`、`HermitFire`（以及防御性 `Fire Hermit`）的对象必须为0。
  任何非Dynamic netID980模糊匹配也 fail closed。
- 用户已同意原生规范：唯一目标变更为
  `/campaigns/1/hermitStatuses/6/position` 与
  `/campaigns/1/currentReign/hermitStatuses/6/position` 的整数 `0→5`；`player=0、land=0` 只强断言、
  绝不写。原生 `ApplyAppearance` 不检查land，后续 `Player.PickUpPassenger` 也会规范写回land0。
- 除上述 Fire 状态字段外，两棵候选 JSON root 必须 `DeepEquals`；现有五位隐士、Cabin、岛屿对象、
  地图、任务、神器、坐骑、建筑、货币、玩家、网络与其他未知字段均不得变化。

## 脚本与写入门禁

- worker 只新增专用 `scripts/repair-fire-hermit-passenger.ps1`；使用 `System.Text.Json.Nodes`，
  禁止 `ConvertFrom-Json` / `ConvertTo-Json` 整档重写。
- 默认 dry-run；只有显式 `-Apply`、`-ExpectedSHA256`、`-ExpectedLength` 和新的不覆盖 `BackupPath`
  同时提供时才允许进入写路径。
- 开始、候选生成后、备份后及 `File.Replace` 紧邻前重复检查游戏进程、源长度/hash与关键schema。
  任一变化 fail closed，不得通过更新 ExpectedSHA 跟随运行中存档。
- dry-run 只写系统临时目录并清理；Apply 才在源目录创建同卷 `CreateNew` candidate。
  gzip 关闭后 `Flush(true)`，再严格 UTF-8/gzip/JSON 独立复读。
- 候选验证必须报告即时 campaign/reign/currentLand、Fire 前后 position/player/land、全岛
  HermitFire 对象数、candidate hash及唯一允许变化的 JSON 路径；源文件与 BackupPath 在 dry-run 后不变/不存在。
- 候选复读后，把before/after两棵独立root中的上述两个position路径统一归一为同一值，再执行整棵
  `JsonNode.DeepEquals`；另验证两个完整hermitStatuses数组只在Fire.position处存在差异。
- Apply 前创建新的完整备份并验证长度/hash等于输入；仅使用四参数
  `File.Replace(candidate, source, rollback, false)`。写后失败优先用已验证 rollback 原子恢复；rollback
  不可用时从已验证独立备份复制到同目录 `CreateNew` restore candidate 后原子恢复，禁止 Move-Force/删源降级。

## 审查、执行与验收

1. worker 完成只读 schema 证据与脚本；reviewer 逐行静态审查。
2. 游戏退出后重新采集即时指纹，运行真实 dry-run；只有输出与本计划完全一致且 reviewer 明确给出
   `APPLY_APPROVED`，operator 才可使用相同参数加 `-Apply`。
3. Apply 后独立复读：源hash等于 dry-run candidate hash，备份hash等于原输入；只有两份 Fire status
   的 `position` 从0变为5，`player/land`保持0，其余整棵结构语义不变。
4. 更新 checklist/progress/events、执行 receipt 与玩家更新日志；只 path-scoped stage 脚本与文档。
   `global-v35`、备份、临时文件、反编译参考源码和旧 release ZIP 永不进入 Git。
5. 实机：首次读档 P1 只携带一位 Fire 隐士；放下后原生变 Roaming，可升级希腊火焰塔；重复读档、
   登船、换岛、死亡换君主不重复、不丢失；既有五位隐士/小屋不变，日志无 HermitFire/Pool/NetID/RPC异常。

## 当前状态

- 2026-08-16 14:21 +08:00 曾检测到 PID23896，因此当时没有备份、dry-run、Apply或存档写入，
  运行中观察指纹全部废弃。14:28用户退出并同意只改两处position、保留原生player0/land0；已冻结上述
  新即时指纹。
- 专用脚本 SHA-256=`86F58C237C4D77833565E525B9A84909FE97F5C698B1863E62C225E24DE26C0B`；
  真实dry-run candidate=`5C43780197C30F2B2F843D7139A5281A76CD836C9295F9307310F9A24FEE0DFE`。
  reviewer给出`APPLY_APPROVED`后已原子Apply并独立复读，最终source与candidate一致，备份与原输入一致，
  归一两处position后整root DeepEquals=True；最终`EXECUTION_APPROVED`。详细证据见`receipt.md`，
  当前任务继续保持doing，等待游戏内Passenger→Roaming与跨岛/读档门禁。
