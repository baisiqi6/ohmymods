# worker-brief v2：身份恢复可靠性加固 A+B（2026-09-20，按对抗审查 10 必改修订；取代 v1）

角色：worker（可写，范围严格受限）。cwd：`C:/Users/ADMIN/projects/ohmymods`。
设计已经对抗审查定稿（receipts/decision-review-record.md），**按本任务书逐条实现，不要更改设计**。

## A. 低风险快赢（knight+musketeer 两侧）

1. `il2cpp/MusketeerArchive.cs` Load（:400-414）：**IoError 分支也尝试备份文件**（镜像 knight 侧 Load 行为）；补一次性 Warning `[Musketeer] sidecar load <status>: <异常摘要>`（当前零日志）。已知边界记录：musketeer 无 RecoverMainFromBackup 等价物，RecoveredBackup 后永久只读（本轮不实现修复）。
2. `il2cpp/KnightIdentityRuntime.cs` load-match 日志（:1421）补 `kind=exact|legacy`。
3. 写失败重试一次（`KnightIdentityArchiveStore.WriteAtomically` :969-1011 与 `MusketeerArchiveStore.Save` :435-458）：**仅当 Failed 为 IsIoFailure 类**（RefusedUnknownVersion/RefusedCorruptMain 等保护性拒写不重试）；真实时钟延迟约 250ms 重试一次；仍失败走既有 Failed 路径；一次性 Warning 附 retry=1。注明：musketeer 的 Save 亦被 load 栈（ConfirmBaseline）调用，重试可能出现在 load 栈，有界可接受。
4. knight Missing 分支（:1397）补一次性 Info `load-sidecar-missing（主备皆缺，重种）`。

## B. 吸收态再基线化（**仅 knight 侧**；musketeer 不做 B——unresolved 时预约 NativeId 恒空无法枚举当前盘个体，且可写变体会永久悬空已付职业生涯；其 unknown-paid 保持语义，写入交付说明）

1. **触发锚点**：`ApplyCapture` 内、在 :987 `CanFlushSeed` 早退与 :989 `Owners` 空早退**之前**（保留 :988 load bridge/generation 排除与 :990 host authority 检查）。
2. **触发条件（全部满足）**：本次装载上下文解析 Kind **精确等于 `known-mismatch`**（排除 legacy-pending 与 conflict）；处于 save 路径。
3. **证据门（严格子集）**：当前盘骑士 uniqueID 从 `island.objects`+`RecordIsKnight`（KnightIdentityRuntime.cs:1136-1159）枚举（**不依赖 capture.Owners**——吸收态会话无收据）；历史并集=该 context 全部 Epochs 的快照条目按 uniqueID。当前盘个体必须 ⊆ 历史并集；任何历史外新个体 → 拒绝，一次性 Warning（gate-reject+计数），维持 fail-closed。
4. **再基线写入**：以当前盘 JSON 写 kind2 快照，**新建 epoch**（RecordSnapshot 新 scope + EnsureContext(contextKey, newEpoch, allowNewEpoch:true) 走 KnightIdentityArchive.cs:385-389），旧 epoch/快照全部保留；写路径复刻 AppendSnapshot 单写者纪律。
5. **携带身份规则**：同 uniqueID 在历史多处收据**全一致才携带**进新基线，否则丢弃该个体（其后续走 fresh，绝不按序数/新近猜）。
6. **成功后立即**：`RememberBinding(contextKey, newEpoch, unresolved:false, newEpoch:true)` + `ConfirmContext(false)`——防同会话后续自动保存重复过门重复建 epoch（MaxEpochs=8 会被撑爆）。
7. 日志：`rebaseline: context=<hash> entries=<n> carried=<n> epoch=<新epoch>`；容量耗尽（MaxScopes/MaxContexts/MaxEpochs）fail-closed 并记日志。
8. **不做**会话中途重绑（验收=**下一会话** load-match kind=exact 恢复）；不改 schemaVersion（用既有 v2 结构）；不改正常 load-match/save/跨岛/联机路径。

## 测试（既有套件扩展 + 6 必补）

- A：musketeer IoError→备份回退；IoError 日志；重试成功/仍失败；**Refused* 不重试**；MatchKind 字段；Missing 日志。
- B：①零 tracked-owner 吸收态会话门可触发（覆盖 :987 顺序）；②再基线成功后同会话后续保存走正常路径、不重复建 epoch；③MaxEpochs/MaxScopes 耗尽 fail-closed；④同 uniqueID 历史收据不一致→丢弃走 fresh；⑤历史外新个体→gate-reject 维持 unresolved；⑥musketeer unknown-paid 在 B 缺席下保持 unresolved 的守卫。
- 回归：既有 knight-identity-*/musketeer-identity 套件全绿。

## 构建验证（不部署）

`il2cpp/` Debug 构建 `-p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/identity-restore-hardening-20260920/receipts/build-stage` 0W0E；如有 interop 门一并。

## allowlist

`il2cpp/KnightIdentityRuntime.cs`、`il2cpp/KnightIdentityArchive.cs`、`il2cpp/KnightIdentityContext.cs`、`il2cpp/MusketeerArchive.cs`、`il2cpp/MusketeerPersistence.cs`、`VERSIONING.md`（第七节新增一行："骑士/火枪手身份恢复可靠性修复（一次写失败不再永久失效）"，修复/优化，跨模块，+0.0.4，累计改 9.5.8→9.5.9……按现有累计行续算）、`tests/knight-identity-*/**`、`tests/musketeer-identity/**`、`docs/project-harness/tasks/identity-restore-hardening-20260920/**`。其他禁动。

## 禁止

commit/push、碰游戏/E盘/存档、启动游戏、改其他模块与未发布候选脏文件。

## 交付

worker-result.md：改动清单、B 门判定/再基线/携带语义说明、测试结果、已知边界（严格子集门的活性上限=失败写入后至首次自愈保存前新招募骑士则该次自愈关闭；musketeer 只读边界；RejectedConflict 边角；联机/跨岛不变）。
