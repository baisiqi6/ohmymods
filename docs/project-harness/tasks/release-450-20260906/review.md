# Independent release review

ZCode0.16.5 session sess_14987965-5f5d-46b3-9c8e-4d09e13f62bd; native events prove bigmodel/GLM-5.3; requested max effort not independently verified. Read-only plan mode, Bash/Edit/Write/Task/Agent denied. Operator performs actual build/startup/package/remote verification.

## 复核修订 — v4.5.0 发布审查（第二轮，2026-09-06）

重新读取了 `docs/project-harness/tasks/release-450-20260906/`（user-acceptance.md、plan.md）、progress.md 与当前源码后，修订如下。

### P1 撤销

原 P1（“玩家反馈当前游玩未见异常”无证据支撑）**不成立**，予以撤销：

1. `user-acceptance.md:3` 记录了用户本轮原话“可以，好像没什么问题……应该作为4.5版本了”，并明确区分了“通用玩家正常游玩反馈”（构成发布授权）与“未完成的战斗/联机/换岛/权威迁移专项验收”（checklist 保持 doing）。发布说明中的该句是对前者的事实性转述，不是对后者的宣称。
2. `release-notes-il2cpp.md:52-53` 自身已带边界声明（“不同存档、长时间游玩、联机及权威迁移等边界仍需持续验证；不宣称所有联机场景已通过”），措辞与验收证据的限度相符。
3. progress.md:10 与 incident.md 的“公开ZIP未更新”是 14:58 启动修复时点的历史记录，progress.md:1-3 顶部已进入 4.5.0 发布准备状态（构建/回归通过，包/tag/发布待执行）。我此前把历史时点状态当作当前状态对比，是误判。

### P2 修订（原 P2-2 已修复）

- **原 P2-2（Samurai OnDisable 清理被 Enabled 门控）已在源码落地修复**：`PatchRoles_SamuraiPowerDash.cs:235` 现在只保留 null 守卫，门控已移除，与 `plan.md:7` 记录的清理处方一致。**注意一点**：该修复晚于已实测的 8390AF DLL，plan.md 自己也把它列为发布前置门（“requires final compile/source review/startup”）——这不是缺陷，而是必须按 plan 顺序执行的既有门禁：打包前须用包含此修复的源码重新构建并通过受控启动，不得直接复用 8390AF 产物打 ZIP。

### 维持的观察项（均非阻断）

- P2-3：`PatchRoles_MedievalNorsePowers.cs:890-900` Knight.Update postfix 每帧逐骑士 Reconcile（含 `GetDeityStatus` 原生 interop），高骑士数量下有可量化开销；已有 2s 巡检覆盖收敛，建议后续降频。
- P2-4：`PatchRoles_DeadlandsPowers.cs:585-586` Slash 守卫放行“已在运行的未注册 vanilla 枚举器”，与 owner 并发共享 `_hitObjects` 的原始风险面残留（代码注释已自认，建议在验收文档中明示为已知限制）。
- P3 项不变：instanceID 键控无指针校验（SamuraiPowerDash:56-57）、`invulnerable=false` 无条件覆写（:183,205）、`_patrolWorld` 强引用不清理（MedievalNorsePowers:80）、`ModConfig.Enabled.Value` 直读无 null 防御（:257,355）。

### 结论

## Verdict: **PASS**

条件（均为 plan.md 已内置的门禁，非新增要求）：打包前用含 Samurai OnDisable 修复的 canonical 源码重新构建（0W/0E）、完成源码终审与受控启动，再执行 clean-worktree 打包、tag/非强制推送与远端 digest 核验；checklist 中的联机/换岛专项验收继续保留 doing，不因发布而关闭。

Operator disposition: performance profiling, pre-existing vanilla overlap and external invulnerability ownership observations remain follow-up scope; a single overwritten _patrolWorld field is not evidence of unbounded cross-world accumulation. No other gameplay retuning is included.
