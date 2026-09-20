# 骑士身份稳定上下文持久化修复 — worker 结果

任务：`docs/project-harness/tasks/musketeer-live-fixes-20260917/knight-worker.md`
状态：代码 + 测试 + 本报告完成；未编译/未安装（无 shell，Operator 负责构建与实机）。
基线缺陷（冻结日志实锤）：`LogOutput.log:49` `load-mismatch:85ad9fec` → `:131-132` `save scope=ea638638 / seed scope=ea638638`，22 名骑士每读档重种；根因是 save/load scope 都含 `island.realStartDateTime.Ticks`（每次读档重建，native-proof.json `islandRuntimeCreationTimePersisted=false`），且快照 hash 把三个活时钟也算进去。

## 交付物（仅动 allowlist 内文件）

生产代码

- `il2cpp/KnightIdentityArchive.cs`
  - schema v2：root `{schemaVersion, scopes, contexts}`；v1（legacy）仍按原封闭 schema 解析（快照一律 kind=1、无 contexts），schemaVersion ≥3 仍 `UnsupportedVersion`（只读、绝不覆盖）。
  - 快照新增 `kind`：1 = 全量 JSON hash（legacy 配方，`Sha256`），2 = 时钟无关指纹（`Normalized`，只剔除 `playTimeDays/lastPlayedTimeDays/_islandTimePlayed` 三个 hero 已实测时钟，其余字段全参）。同 hash 不同 kind = `RejectedConflict`。
  - `KnightIdentityContext{Active,Epochs}` + `ContextKey(file,campaign,challenge,land)`（稳定、SHA256、不含运行时值）+ `NewScope()`（私有随机 64-hex）+ `EnsureContext`（一个 scope 最多一个 context；active 回退 = rollback；容量 fail closed）。
  - 迁移扫描用 `Sha256Bytes` / `NormalizedPayload`+`NormalizedFromPayload`：每份岛 JSON 只编码/解析一次，逐 scope 只做 SHA256，避免 100×MB 级重复分配。
- `il2cpp/KnightIdentityContext.cs`（新增，纯函数、无 I/O、不碰 Unity 类型）
  - `Resolve(archive, contextKey, rawJson)`：仅两种精确 hash；匹配范围 = 本 context 的 epoch + 未被任何 context 拥有的 scope；多个匹配全部一致才取（kind2 优先→scope 序数），否则 `conflict`。
  - unresolved 三分支：`conflict` / `known-mismatch`（已知历史对不上）/ `legacy-pending`（仍有未归属历史）→ 不恢复、不建 epoch、不写、不种。
  - 全新上下文（无任何未归属历史）→ 新 epoch + kind2 fresh hash；会话内 load→save 绑定缓存（save 路径复用 load 判定）。
- `il2cpp/KnightIdentityRuntime.cs`
  - LoadBridge：稳定上下文解析；`load-match scope=… hash=… entries=…`（精确命中）/`load-fresh …`/`load-unresolved:<kind>`；精确命中且 scope 未归属时在 finalizer 里一次性 `ClaimContext`（非破坏、失败只降级）。
  - SaveBridge：kind2 快照写入解析出的 epoch；unresolved 时 `save-preserve-unresolved` 跳过（不再产出匿名新快照）。
  - Sidecar：`AppendSnapshot(scope, snapshot, contextKey, newEpoch)` 把快照与「context→epoch」映射放同一次原子写；`ClaimContext`；`TryResolveForWrite`（无绑定时的只读解析，保留原有 corrupt/unsupported 只读日志）；`TryBuildScopeKey`（含 ticks）删除。
  - 顶部契约同步更新；`ResetForTests` 一并清绑定缓存。
- `il2cpp/KnightIdentityLoadSeed.cs`：批次冻结 `ContextKey/NewEpoch`，kind2 快照 + 同写 context 映射；unresolved 时 `LoadScope.ScopeKey=null`，本模块自然不开始（不覆盖历史）。

测试

- 新增 `tests/knight-stable-context/`（纯 resolver，14 用例）：上下文键稳定性/分离性；fresh 不透明 epoch；同内容同收据；时钟漂移可匹配、对象变化拒绝；legacy 精确迁移保留 GUID/style；两个冲突匹配 unresolved；一致匹配确定性选择（kind2 优先）；已归属 scope 绝不迁进第二 context；已知历史对不上 unresolved；未归属历史阻塞全新上下文而空历史不阻塞；回滚到旧快照恢复那一代；空快照命中≠unresolved；非法/不可用 JSON fail closed；Resolve 绝不改 archive。
- `tests/knight-identity-archive`：`kind` 绑定与非法 kind 用例；v1→v2 升级（legacy 快照保留 kind1、contexts 落盘、备份保留 v1 字节供 rollback）；contexts 封闭校验（active/epochs/重复 kind/未知字段/一 epoch 两 context 全 Corrupt）；“未来版本”夹具 2→3。
- `tests/knight-identity-runtime`：stub 岛 JSON 改为原生真实字段（`realStartDateTime` 不入 JSON，三个活时钟 + lastPlayedReign/biome 入 JSON，CloneIsland 同步）；新增「runtime start date 重建 + 三时钟漂移 → 同 GUID/style 恢复；对象变化 → 不恢复且 unresolved」与「v1 legacy 精确命中 → 恢复旧 GUID/style + 一次性持久化 context→epoch 映射（旧记录原样）」两条走真实 Save/Load 桥的用例；mismatch 用例改为 unresolved 语义（sidecar 逐字节保留、不种批）；scope 断言改为从 archive 读 context/epoch。
- `tests/knight-load-seed`：stub JSON/CloneIsland 同上；全部 scope 断言改为稳定 context/epoch + kind2；`ExistingHistoryIsPreserved` 改为「未归属历史 + 失配 → 不种、不覆盖、旧 GUID/style 原样」；“未知 schema”夹具 2→3。
- 新增 Context.cs 到 runtime/load-seed/integration 三个 csproj（主工程默认通配自动包含）；network 项目无需改动（只依赖收据类型，接口未变）。

## 关键语义（便于 Operator/Reviewer 核对）

- 幂等：同一份岛（模三时钟）→ 同一 epoch/kind2 hash → 同一 GUID/style；跨会话不再受 `realStartDateTime` 影响。
- 迁移：legacy 记录只按「stored scope 盐 + 当前 raw/normalized 全岛 JSON」精确匹配；精确命中即把该 scope 归并为本 context 的 epoch（一次写入），旧记录原样保留。
- 保守边界：冲突 / 已知历史对不上 / 仍有未归属历史 → unresolved：不恢复、不写、不种；空条目历史不阻塞；被别的 context 拥有的 scope 绝不作为迁移来源。
- 不回写原生存档；不猜 firstN/位置；原子写 + 备份 + 只读降级（Corrupt/未来版本）全部沿用；客户端零写入（load/save 解析都在 host 门内）。

## 已知限制（如实）

1. **未编译/未运行**（本 worker 无 shell）。构建与测试需 Operator 执行：`dotnet8 build -c Debug`（主工程，覆盖/禁用 `BepInExPluginsPath`）+ 运行 `tests/knight-stable-context`、`tests/knight-identity-archive`、`tests/knight-identity-runtime`、`tests/knight-load-seed`、`tests/knight-identity-integration`、`tests/knight-identity-network`。
2. **legacy 精确匹配是唯一迁移入口**：v1 快照只有 kind1（全量 JSON）配方，因此只有当当前文件内容与某份已存快照逐字节一致（含三时钟）时 legacy 记录才被认领；匹配时用**存储的 scope 作盐**重算 hash，不需要旧的 ticks。三个活时钟（或任何其他字段）若在两个写点之间变化，该会话落到 unresolved（不种、不覆盖），直到一次原生 Save 在已认领 epoch 下写出 kind2 快照（kind2 天生容忍这三个时钟的漂移）。这是任务书「unmatched history => unresolved」的既定保守语义；对当前用户侧档的首次迁移成功率取决于其最后一份快照与文件内容是否逐字节一致（冻结日志里同会话 save/seed 与下次 load 的短 hash 相同，倾向能命中；无法在无用户数据的情况下断言）。
3. **unresolved 期间风格迁移哈希仍含 ticks**（`PatchRoles_KnightStyle.TryComputeIdentity`，只读文件）：旧档一次性迁移 hash 未改，收据一旦建立并由稳定快照持久化即不再重算；若首迁落入 unresolved，会话内迁移仍可能逐次读档重摇，留待 Operator 的“微小最终接线”（建议用稳定上下文 + 可稳定重取的输入替换 ticks/NetID/instanceID 熵）。
4. **全新文件/新存档槽 + 仍有未归属历史**时按 hero 同款规则 fail closed（不种）；真正无历史（或全部历史已被各 context 认领）才允许新 epoch 落盘。
5. 文档同步（AGENTS.md、harness-checklist.json、progress.md、patch-patterns/domain-model）不在本 worker allowlist 内，留给 Operator。
