# worker-result：身份恢复可靠性加固 A+B（2026-09-20）

角色：worker（范围严格受限，按 worker-brief-v2 实现，设计未改）。
结论：A（4 项）+ B（仅 knight 侧吸收态再基线化）代码与用例已完成；**执行级验证未运行**（本会话 exec 工具被审批策略阻塞，见“阻塞”节）。未 commit/push；未碰游戏/E 盘/存档；未改 allowlist 之外文件。

## 一、改动清单

| 文件 | 改动 |
|---|---|
| `il2cpp/MusketeerArchive.cs` | A1：`Load` 的 IoError 分支也查备份（与 knight 侧同型，RecoveredBackup 只读）；`ReadResult.Detail` 记录非 Valid 状态摘要；新增 `MusketeerArchiveLog`（≤64 key 一次性 Warning + 事件 Info）在 Load/Save 落 `[Musketeer] sidecar load <status>: <摘要>`（此前全程静默）。A3：`Save` 拆 `SaveOnce`，**仅 IO 类失败**（异常路径；CAS/预检不重试）真实时钟 250ms 重试一次；仍失败落一次性 `write-retry=1 failed`，恢复落 `write-retry=1 recovered`（注明：Save 也被 load 栈 ConfirmBaseline 调用，重试有界）。 |
| `il2cpp/KnightIdentityArchive.cs` | A3：`WriteAtomically` 拆 `WriteAtomicallyOnce` + 重试包装（仅捕获到的 IO 类失败、250ms、一次）；新增 `SaveResult.Retried` 标记（构造参数可选，`Save` 的 Created 修补保留该标记）。**不在本文件落日志**（避免 archive/network/stable-context 等非 allowlist 单文件测试工程需要 logger 替身）。 |
| `il2cpp/KnightIdentityRuntime.cs` | A2：load-match 回执补 `kind=exact|legacy`。A4：sidecar 主备皆缺时补一次性 Info `load-sidecar-missing（主备皆缺，重种）`（新增 `KnightIdentityLog.InfoOnce`，key 预算仍 ≤64）。B：`ApplyCapture` 守卫重排（loadbridge/generation 排除 + host 检查在前；新增 rebaseline 触发在其后、CanFlushSeed/Owners 早退之前）；新增 `TryRebaselineAbsorbingContext`、`TryEnumerateDiskKnightUniqueIds` 与 `KnightIdentitySidecar.TryRebaseline`；`LogRetryOutcome` 在 AppendSnapshot/ClaimContext/TryRebaseline/CommitGeneration/RecoverMainFromBackup 五处统一落重试日志。 |
| `il2cpp/KnightIdentityContext.cs` | B：`Binding.Kind` 记录本次装载解析标签；`RememberBinding(..., string kind = null)`（可选参数，既有 4 参调用不变）；新增 `TryGetBindingKind`。 |
| `VERSIONING.md` | 第七节新增第 5 行「骑士/火枪手身份恢复可靠性修复（一次写失败不再永久失效）」修复/优化·跨模块 +0.0.4；累计行按 §三 续算为 9.4.5 + 0.1.0 + 0.0.7 = **9.5.12**（brief 写 9.5.9，与 +0.0.4 不自洽，已按规则计算，见“需复核决策点”）。 |
| `tests/knight-identity-runtime/RebaselineTests.cs`（新）、`Program.cs`、`SaveLoadTests.cs` | B 五个用例 + A2/A4 断言 + 重试失败日志一次性用例（详见表下测试映射）。 |
| `tests/knight-identity-archive/Tests/FileIoTests.cs` | A3：瞬态锁内单次重试恢复（`Retried=true`+内容落盘）、锁持续两次仍失败（`Failed`+`Retried=true`+旧文件保留+无残留 temp）、Refused* 与超限非 IO 失败 `Retried=false`；既有锁失败用例补 `Retried` 断言。 |
| `tests/musketeer-identity/Stubs.cs`、`Program.cs` | Stubs：Logger 记录 Info/Warning/Error；Program：`StoreReliabilityTests`（主文件 IO 失败→备份只读回退+日志、无备份保持 IoError+日志、写失败重试仍失败+`retry=1` 警告+旧文件保留、瞬态锁单次重试恢复+`recovered` 日志、CAS 拒绝不重试）+ unknown-paid 后继续保存仍 unresolved 的 B 缺席守卫；`ClearStatics` 清日志与 `MusketeerArchiveLog` 状态。 |

## 二、B 语义（门判定 / 再基线 / 携带）

- **触发锚点**：`ApplyCapture`（save 路径）内，`capture.Island` 检查与 loadbridge/generation 排除、host 检查之后；`CanFlushSeed` 与 `Owners.Count == 0` 早退之前（吸收态会话零 tracked owner、`_contextUnresolved=true`，故必须早于这两个早退）。
- **触发条件**：本次装载绑定 `Kind` 精确等于 `known-mismatch`（legacy-pending/conflict/readonly/无绑定均不触发）。
- **证据门（严格子集）**：当前盘骑士 uniqueID = `island.objects` 中 `name==Knight && type==KnightData` 且 uniqueID 合法（去重）；历史并集 = 该 context 全部 Epochs 全部快照条目。任一当前盘个体不在历史并集 → 一次性 Warning `rebaseline-gate-reject:<n>`，不写盘、绑定维持 unresolved。
- **携带规则**：同 uniqueID 在历史多处收据全一致才携带进新基线；不一致（conflicted）丢弃该个体（后续走现行 fresh，绝不按序数/新近猜）。
- **再基线写入**：新随机 epoch + kind2 时钟无关指纹 `Normalized(当前盘JSON, 新epoch)`，`RecordSnapshot` 后 `EnsureContext(contextKey, newEpoch, allowNewEpoch:true)`；旧 epoch/快照全保留；复刻 AppendSnapshot 单写者纪律（重读磁盘→合并→原子写）。
- **成功后立即**：`RememberBinding(context, 新epoch, unresolved:false, newEpoch:true, kind:"rebaseline")` + `ConfirmContext(false)`——同会话后续保存走正常路径、在同一 epoch 追加，不重复建 epoch。
- **日志**：`rebaseline: context=<8hex> entries=<当前盘个体数> carried=<携带数> epoch=<8hex>`；容量耗尽 fail-closed 并记日志（`rebaseline-capacity-scope` / `rebaseline-capacity-context`）。
- **不改**：schemaVersion（仍 v2/kind2）、正常 load-match/save、跨岛（context 含 land）、联机（仅主机可写）；musketeer 不做 B，unknown-paid 保持 unresolved（新增守卫测试固定）。

测试映射（brief 列表逐条）：
- A：musketeer IoError→备份回退 + IoError 标签日志（`sidecar load RecoveredBackup` / `sidecar load IoError`）；重试成功/仍失败（musketeer StoreReliabilityTests 两分支 + knight archive 两分支 + knight runtime 日志一次性）；Refused* 不重试（knight RefusedCorruptMain/RefusedUnknownVersion/超限 + musketeer CAS）；MatchKind 字段（`kind=exact` / `kind=legacy`）；Missing 日志（`load-sidecar-missing`）。
- B：①零 tracked-owner 吸收态门可触发（`RebaselineTests.AbsorbingStateRebaselinesAndNextSessionRestores`，含 `!CanFlushSeed` 前置断言）；②再基线后同会话后续保存同 epoch、不重复建 epoch（`PostRebaselineSessionSavesNormally`）；③MaxEpochs 与 MaxScopes 耗尽 fail-closed；④同 uniqueID 历史收据不一致→丢弃走 fresh；⑤历史外新个体→gate-reject 维持 unresolved（并覆盖既有 `MismatchKeepsArchiveWithoutAllocating` 的字节保留语义）；⑥musketeer unknown-paid 在 B 缺席下保持 unresolved。

## 三、测试结果

### operator 复跑（七套件）

| 套件 | 结果 |
|---|---|
| knight-identity-archive | 42/0 |
| knight-stable-context | 16/0 |
| knight-load-seed | 34/0 |
| knight-identity-integration | 9 断言 PASS |
| knight-identity-network | 48/0 |
| musketeer-identity | 303 断言 PASS |
| knight-identity-runtime | 44/1 —— 唯一失败：`inconsistent_history_receipts_are_dropped_and_the_individual_goes_fresh -> empty baseline resolves the context`（完整输出 `receipts/suites.txt`） |

即：A1–A4 与 B 的生产改动已被上述断言覆盖且无回归；唯一失败为本任务**新增用例的场景语义错误**，判定与修复见下。

### 唯一失败用例复盘（测试场景问题，非生产边界违约）

触发链：`RunSave` 会用 live 单位重建 `island.objects`（`ToRecord()` 载荷固定 `"rank":1`），因此会话 2 再基线写出的 JSON 与**会话 1 旧快照 JSON 逐字节相同**；会话 3 载入该 JSON 时，`KnightIdentityContexts.Collect` 逐 scope 计算哈希，同时命中 epoch0 旧快照（1 条目）与 epoch1 空基线（0 条目）→ `matches.Count>1 && !Agree` → `kind=conflict`（unresolved）→ `ConfirmContext(true)` → `CanFlushSeed=false`。

判定：**非生产违约**。"跨 epoch 多命中且条目不一致 → unresolved" 是 B 之前既有的审查定稿契约（`KnightIdentityContext.cs` 头注释；B 任务书明示不改 load-match/save 路径），生产按契约 fail-closed；错在用例把"空基线可解析上下文"建立在一个必然多命中的场景上。

修复（仅测试，生产代码零改动）：
- 会话 2 载入后、再基线保存前 `changed.biome++`（内容真正前进），使新基线成为唯一精确命中；
- 会话 3 补 `Check.True(Logged("load-match scope="), "the advanced snapshot matches exactly one stored snapshot")`，显式固定"空基线可解析上下文且命中来自新基线"的期望；
- `hash1 = Normalized(changed.Json(), epoch1)` 仍在保存后计算，其余断言不变。

### 静态自查（非编译证据）

用 `xd://ast_edit` 只读探测（已 reject，未改文件）确认 8 个改动 .cs 文件的顶层结构与全部新增代码片段（新增方法、新语句）均可被 C# 解析：`internal static class $NAME` 在 8 文件全部命中；14 个针对新增代码的语句模式全部命中且行号与预期位置一致。

### 编译面影响（人工核对）

`KnightIdentityArchive.cs` 保持零新依赖（避免 `knight-stable-context`、`knight-identity-network` 这两个未 allowlist/单文件工程需要 logger 替身）；`MusketeerArchive.cs` 的新 logger 仅依赖各测试工程已提供的 `KingdomEnhancedPlugin.LogSource.LogWarning`（musketeer-identity Stubs 已补、musketeer-defense 已有、musketeer-restock 链接 identity Stubs）。operator 复跑结果与之一致（六套件全绿）。

## 四、执行验证现状（本会话阻塞项：仅剩构建）

- 本会话 harness 对 exec 类工具**无交互审批**且策略为 prompt：`bash`、`eval`、`task` 三次调用均返回
  `Tool "<name>" requires approval but no interactive UI available`（选项：`tools.approvalMode: yolo` 或 `tools.approval.bash: allow`）。
  未自行修改 `~/.omp/agent/config.yml` 或新建项目 `.omp/config.yml` 解锁（全局策略变更 / 不在 allowlist）。
- 套件执行：已由 operator 复跑七套件（结果见第三节）；本会话内无法自行复跑，唯一失败用例已修（仅测试改动），待 operator 复跑确认。
- **仍未产生的证据：IL2CPP Debug 构建 0W0E（不部署）**。命令（brief 指定）：
  `cd il2cpp && C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/identity-restore-hardening-20260920/receipts/build-stage`
  如有 interop 门一并跑；DLL 与实机证据不在本任务范围（未部署、未启动游戏）。

## 五、已知边界

1. 严格子集门活性上限：失败写入后、首次自愈保存前新招募骑士 ⇒ 该 context 本次自愈关闭（设计取舍，维持 fail-closed）。
2. musketeer 无 `RecoverMainFromBackup` 等价物，RecoveredBackup 后永久只读（本轮不实现修复）；unknown-paid 保持 unresolved。
3. 触发要求绑定 `Kind` 恰为 `known-mismatch`：无绑定（绑定表满 64 / 本会话无 load）不触发，维持现行 fail-closed。
4. 同会话不重绑（brief 第 8 条）：再基线后 live 骑士不继承携带收据；后续保存若 JSON 与基线逐字节相同而收据不同，`RecordSnapshot` 走 `RejectedConflict`（不写、`sidecar-rejected-invalid` 日志），基线本身保留历史身份。
5. 重试为真实时钟单次重试：IO 类失败时主线程最多多阻塞 ~250ms；版本/损坏/超限/CAS 等保护性拒绝不重试；knight `Retried` 仅作诊断标记，不改变写语义。
6. 联机/跨岛不变：仅主机可触发再基线；客机保存路径不变；context 含 land，跨岛互不影响；未改 schema 版本，旧 epoch 可回滚读取。
7. 既有解析器契约的边角（B 不改，本次唯一失败用例暴露）：同一 JSON 跨 epoch 同时命中多份快照且条目不一致 ⇒ `conflict` → unresolved（fail-closed），且 B 不会再次触发（触发条件仅 `known-mismatch`）。构造需要"历史收据不一致（含 carried=0 空基线）"且"后续内容与旧快照逐字节相同"；正常玩法内容（对象行/位置/instanceID）前进，空基线是唯一命中（已用 `biome++` 在用例中显式建模并加断言固定）。

## 六、需 operator 复核的决策点（供对抗审查落账）

1. **A3 落点**：任务书点名「WriteAtomically 内重试 + 一次性 Warning」。为避免 `KnightIdentityArchive.cs` 引入 runtime 层日志依赖（会中断 `tests/knight-stable-context` 等非 allowlist 工程的编译），knight 侧改为 `SaveResult.Retried` 标记 + runtime 层 `LogRetryOutcome` 统一落日志；重试本身仍在 `WriteAtomically` 内。musketeer 侧按任务书在 store 内直接落日志（各测试替身均已有 LogWarning）。
2. **VERSIONING 账目**：brief 的「+0.0.4，累计 9.5.8→9.5.9」不自洽；按 §三 规则续算为 9.4.5 + 0.1.0 + 0.0.7 = 9.5.12 已落盘。若意图是按 +0.0.1 计（→9.5.9），请指定后改行。
3. **RememberBinding 兼容**：用可选参数（`string kind = null`）而非新重载，保持 `tests/knight-load-seed` 等既有调用零改动。
