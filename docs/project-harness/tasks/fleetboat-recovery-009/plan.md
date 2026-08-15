# fleetboat-recovery-009 — 奥林匹斯小船所有权幂等恢复

## 目标

- 修复死亡换君主后，四个神像交付任务仍为完成但 `FleetBoat` 所有权、standby、carryForward 与岛屿实例全部归零，导致小船永久丢失且无法重新领取的问题。
- 以四个已完成的神岛交付任务作为所有权下限：Athena、Artemis、Hephaestus、Hermes，每项最多贡献 1，期望值严格限制在 0～4。

## 安全边界

- 只在 Call of Olympus 且 `NetworkBigBoss.HasWorldAuth` 执行；要求 campaign 与当前 BiomeHolder 都是
  Greece biome，并排除 `GlobalSaveData.loaded.InChallenge`；客户端不生成、不写 standby、不改任务。
- 不解压、重写或修补 `global-v35`，不重置任务，不调用 `IdolCollector`，不重播奖励动画。
- 备份已创建：`Release/KEM-backups/global-v35.before-fleetboat-recovery-20260815-162933`，SHA-256=`1D50D6CE1B0DD49D30F85C0BB8B57BB88C0AFE599FC718B18F705E93D7359822`。
- 恢复安排在原生 `CampaignSaveData.ApplyToScene` 完整返回后；必须确认 Kingdom、BiomeHolder、PoolManager、world gameLayer 与保存场景应用已就绪。
- 遵循原生唯一来源语义，不把同一批 active、standby 和 carryForward 重复相加；只补缺口，不删除多余，不超过 4。
- riverless/缺少安全生成条件时只写原生 standby；能生成时只用当前 biome 的 `fleetBoatPrefab` 与既有同步池。不得新增 syncID、RPC、sidecar 或自定义持久化。
- 游戏运行时禁止替换 DLL；只部署独立测试副本，Steam/Mono/共享存档内容不改。

## 原生生命周期待核对

1. `PopulateCarryForward` 在 active 非空时取 active count，否则取 standby；两者是同一所有权在不同场景阶段的互斥表示，不应求和。
2. `ApplyCarryForward` 在 riverless 岛把 carry 数写入 standby；可生成岛则从当前 biome prefab 生成并注册 active，之后清空 carryForward。
3. Patch 在 `ApplyToScene` Prefix 捕获本次有效 carry 的所有权目标，在 Postfix 读取已经完成的场景表示；
   `desired=max(expected,capturedCarry)`，而实际表示严格按 `active>0 ? active : standby` 选一项，不把三者相加。
4. 生成前验证当前 prefab 已存在于原生同步池、boatSailPosition/world/pools 可用；否则 fail closed 到 standby。

## 实现契约

- 计算 `expected`：精确调用 `QuestManager.GetQuestCompleted` 检查
  `QuestType.GodIslandAthena`、`QuestType.GodIslandArtemis`、`QuestType.GodIslandHephaestus`、
  `QuestType.GodIslandHermes`，按真值计数并 Clamp 0～4；禁止改用后续 Athena/Artemis 等任务。
- 计算 `active`：`Kingdom.FleetBoats` 中非空、active 且属于当前场景的有效实例，Clamp 0～4。
- `standby`：Clamp `Kingdom.NumFleetBoatsOnStandby` 到 0～4。
- `carry`：只在本次 `ApplyToScene` 开始时 `carryForward.present` 且航行数据有效时捕获 0～4；原方法返回后只按原生阶段选择一个 canonical actual，禁止三者求和。
- `desired = Clamp(max(expected, capturedCarry), 0, 4)`；`missing = max(0, desired-materialized)`；
  `materialized >= desired` 时零写入，绝不因任务数较少删除已有所有权。
- 2.4 interop 没有 `SetNumFleetBoatsOnStandby` wrapper；只允许 authority 写 publicized
  `Kingdom._numFleetBoatsOnStandby` 字段。standby 已非零时只把该唯一表示提升到 desired，不同时生成 active。
- active 已非零时只补 active 缺口；单次生成部分成功后保留已成功 active、记录实际 recovered，失败剩余留到
  下一次 `ApplyToScene` 重试，绝不再写 standby 形成原生下次航行会忽略的混合表示。
- active/standby 均为零时：riverless 或生成前置不完整则把 desired 写入 standby；可生成时从当前 biome
  `fleetBoatPrefab` 在原生 boat sail position 附近通过原生池同步生成 desired。每次成功后由 FleetBoat
  原生 OnEnable 注册，绝不把失败数量当成功。若首个生成失败且最终 active 仍为零，则整批 fail closed
  到 standby；只要最终已有任一 active，就绝不同时写 standby，剩余缺口留到下一次 Apply 重试。
- 每次 ApplyToScene 最多输出一条摘要日志：expected/active/standby/carryForward/actual/recovered/mode；同一加载不得每帧重试或刷屏。

## 验证

1. worker 对 2.1 逻辑说明书与 2.4 interop 签名/调用时序做双重核对并实现最小独立 patch。
2. reviewer 独立检查来源去重、world authority、riverless、pool/RPC、重复加载与异常路径。
3. `dotnet build -c Debug --no-restore -p:BepInExPluginsPath=` 必须 0 warning / 0 error；`git diff --check` 通过。
4. 游戏退出后只部署独立测试副本，确认构建/部署 DLL 哈希一致；不自动启动游戏或改存档。
5. 当前异常存档首次加载恢复至 4；重复读档不会变成 8；换岛、死亡换君主仍为 4；2 项任务最多 2；0 项不生成；日志无 unknown pool、重复 syncID、RPC/FleetBoat 异常。

## 退出条件

- 静态 reviewer APPROVED、构建与独立副本部署完成后保持 `doing/review_approved`，等待上述游戏内门禁；不得因“已部署”提前标 done。
