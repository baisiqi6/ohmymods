# worker-brief：身份恢复可靠性加固 A+B（2026-09-20，用户已批准 A 与 B）

角色：worker（可写，范围严格受限）。cwd：`C:/Users/ADMIN/projects/ohmymods`。
背景诊断（已两轮对抗审查定论，回执 tasks/identity-restore-instability-20260919/receipts/decision-review-record.md）：吸收态（H4a）把一次性 sidecar 写失败/失配固化成该岛永久 unresolved；musketeer 读 IoError 静默且不查备份（唯一真逐启动抽奖路径）；load-match 日志缺匹配类型。

## A. 低风险快赢（四处）

1. `il2cpp/MusketeerArchive.cs` Load（约 :400-414）：IoError 分支**也尝试备份文件**（镜像 knight 侧 KnightIdentityArchiveStore.Load 主缺/损→查备份的行为）；并补**一次性 Warning** 日志（`[Musketeer] sidecar load <status>: <异常摘要>`）——当前全程零日志。
2. `il2cpp/KnightIdentityRuntime.cs` load-match 日志（约 :1421）：补 `kind=exact|legacy` 字段（判别 H1 玩家）。
3. 写失败重试一次：`KnightIdentityArchiveStore` 与 `MusketeerArchive` 的原子写（temp+Flush+Replace）路径，IoFailure 时**延迟约 250ms 重试一次**（真实时钟），仍失败走既有 Failed 路径与日志；重试本身计入既有一次性 Warning（附 retry=1 标记）。不改变任何语义，只降低瞬态锁文件被固化的概率。
4. knight Missing 分支（约 :1397）：补一次性 Info `load-sidecar-missing（主备皆缺，重种）`——现状看似成功的 load-fresh，取证时无法与"读到空档"区分。

## B. 吸收态再基线化（保守证据门，用户已批准方向）

目标：把"一次性失配被永久固化"改为"可自愈"，同时不放弃防串人/防回滚误吞。

设计（在 knight 与 musketeer 两侧同型实现）：
1. **触发条件（全部满足）**：上下文解析结果为 known-mismatch 类 unresolved（不是 IoError/Missing/版本不支持等）；当前处于 save 路径（原生存档刚落盘、我们拿到当前岛 JSON）。
2. **证据门（保守子集校验）**：当前盘 JSON 里的本系统人群（knight=骑士行 uniqueID；musketeer=记录 GUID/native 锚）**必须是该 context 历史快照条目并集的子集**——即"同一人群、内容前进"才放行；出现任何历史中不存在的新个体 → 不放行（可能是别人的档/超历史回滚），维持现行 fail-closed。校验失败要有一次性 Warning（含 gate-reject 与个体计数），**静默保守**。
3. **放行动作**：以当前盘 JSON 写入一个**新基线快照**（kind2 时钟无关指纹），归入该 context 的 Active epoch（或按现有 context 结构新建 epoch，遵循 EnsureContext 放行规则）；**旧快照全部保留不删**；历史条目中与当前盘个体匹配者（按稳定 uniqueID/GUID）**携带身份进入新基线**，当前盘上的未知个体不给身份（后续走现行 fresh 路径）。
4. **写后行为**：本会话即完成重绑（复用现行 load-receipt 绑定路径或下一巡检），日志 `rebaseline: context=<hash> entries=<n> matched=<n>`；玩家下次启动 load-match kind=exact 恢复。
5. **不改动**：正常 load-match/save 路径、跨岛迁移、联机同步、schema 已有版本（若实现确需 schema 变更，停下来在回执里说明，不要自行 bump）。

## 测试（对应既有套件扩展：tests/knight-identity-runtime、knight-identity-archive、knight-stable-context、musketeer-identity 等）

A：musketeer IoError→备份回退用例；IoError 日志出现；写失败重试成功/仍失败两分支；MatchKind 出现在 load-match；Missing 分支日志。
B：吸收态场景（一次失配+自动保存推进 → 下次会话 evidence 门放行 → 再基线 → 身份恢复）；门拒绝场景（当前盘出现历史外新个体 → 不写、Warning、维持 unresolved）；门拒绝场景二（人群为空/校验异常 → fail-closed）；旧快照保留验证；knight 与 musketeer 两侧各覆盖。
回归：既有身份套件全绿。

## 构建验证（不部署）

`il2cpp/` Debug 构建 `-p:BepInExPluginsPath=<task>/receipts/build-stage` 0W0E；如有对应 interop 门一并跑。

## allowlist

`il2cpp/KnightIdentityRuntime.cs`、`il2cpp/KnightIdentityArchive.cs`、`il2cpp/KnightIdentityContext.cs`（如需）、`il2cpp/MusketeerArchive.cs`、`il2cpp/MusketeerPersistence.cs`、`il2cpp/KnightIdentityLoadSeed.cs`（如需）、`tests/knight-identity-*/**`、`tests/musketeer-identity/**`、`docs/project-harness/tasks/identity-restore-hardening-20260920/**`。其他文件禁动。

## 禁止

commit/push、碰游戏/E盘/存档、启动游戏、改其他模块（尤其未发布候选脏文件）。

## 交付

worker-result.md：改动清单、B 的门判定与再基线语义说明、测试结果、已知边界（联机/跨岛/极端回滚下的行为）。
