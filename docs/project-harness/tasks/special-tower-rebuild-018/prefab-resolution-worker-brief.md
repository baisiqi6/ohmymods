# worker 任务书：修复特种箭塔重建交互不出现——源 prefab 解析错误（special-tower-rebuild-018）

## 根因（Operator 已实证，不要重新推导）

实机日志（E 盘 2026-08-16 23:50 会话）只有：
`[SpecialTowerRebuild] Ready source=Tower Ballista target=Tower6 price=18 biome=5`
之后全程无任何 `Blocked` 日志、无交互 → `CanSelect` 从未作用于重建 payable →
场上弩箭塔实例上根本没有 marker/PayableUpgrade。

存档证据（`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35`，gzip 解压后）：
场上已建弩箭塔的持久化 prefabPath 为
`Prefabs/Buildings and Interactive/greece/Tower Ballista_greece`（2 座）。

而 `EnsurePrefabLayout` 在 `PoolManager.Init` 前缀里解析到的源是名为 `Tower Ballista`
的**基座资产**（Ready 日志可证）——`Tower6` 模板 passengerUpgrades 的 route prefab 经
`BiomeData.GetAssetSwap` 后未换皮到 `_greece` 变体（早期时点 swap 未生效，或基座模板本就
引用基座资产）。**组件加在了错误资产上；真实建造/存档恢复的实例全部来自
`Tower Ballista_greece`**（原生 `PayableUpgrade.Pay()` 用的是被升级塔自己的 payable route +
当时的 swap，得到 `_greece`；存档恢复 `IslandSaveData.TryCreateOrFind` 直接按保存的
prefabPath 实例化 `_greece`；池路径 `FastSpawn→FastClone→Instantiate(this._prefab)` 是惰性
克隆，prefab 上配了组件就会带上）。

上一轮"驻守工匠阻断"修复（提交 1f9f988）修的是真问题但不是主因，本任务不回退它。

## 修复设计（Operator 锁死）

把"单一源解析"改为"配置全部安全候选"，不依赖 GetAssetSwap 时序：

1. **候选收集**（`EnsurePrefabLayout` 内，biome 守卫与现有失败处理保持）：
   - 候选 A：现有解析结果（Tower6 模板 ballista route prefab + `BiomeData.GetAssetSwap` 结果）；
   - 候选 B：`Resources.LoadAll<Ballista>("")` 全量扫描（仓库已有 `LoadAll<WarriorGhostLeader>`
     同型先例），取 `name` 含 `"Tower Ballista"` 的资产；
   - 对每个候选执行现有安全检查（`GetComponent<Ballista>()` 非空、无 `FireTower`、
     无 `OilFireArcherTower`、无 `TowerKnight`），不安全者跳过并计入一条汇总日志；
   - 按 Pointer 去重。
2. **逐候选配置**：沿用现有单源逻辑——已有原生 `PayableUpgrade` 且无 marker 的候选
   **跳过该候选**（沿用 ReportFailure 风格但不得中断其他候选），其余补 marker、
   补/复用 `PayableUpgrade` 并 `ConfigurePayable`（template/nextPrefab 继续用 Tower6 基座
   模板——原生 Pay() 付款时会对 nextPrefab 做 asset swap，此行为不动）。
3. **幂等状态**：`_configuredSourceId`（单 int）改为已配置 instanceID 集合
   （`HashSet<int>`）；biome 变化时清空重配（保留 `_configuredBiome` 语义）。
   每个候选自身已有 marker+PayableUpgrade 且在集合中则跳过。
4. **Ready 日志**改为列出全部已配置源名，例：
   `[SpecialTowerRebuild] Ready sources=[Tower Ballista, Tower Ballista_greece] target=Tower6 price=18 biome=5`
   （price 取首个已配置 payable 的值）。另加一条一次性诊断日志列出"发现但被安全检查跳过"的
   候选名，便于下次实测直接从日志验证 `_greece` 是否配置到位。
5. **调用点不变**：仅现有 `PoolManager.Init` prefix。不要加 World.OnLevelLoaded 等新钩子
   （惰性克隆保证 Init 时配好即可覆盖存档恢复；若实测仍缺再议）。

## 边界与约束

- 只修改 `il2cpp/PatchWorld_SpecialTowerRebuild.cs`；`CanInteract`/`TryPrepare`/`ConsumePrepared`/
  `Pay` 网关与 token 逻辑一行不动；其他文件一律不动。
- IL2CPP 习惯用法沿用现状（Il2CppReferenceArray、TryCast 不需要；LoadAll 返回 Il2CppArrayBase
  按 2.1 参考与仓库先例写）。现代 C# 语法，不引入依赖。
- 注意 2.1 参考源码只读：`game-source/Assembly-CSharp-2.1.0/` 下 `IslandSaveData.cs`（TryCreateOrFind
  822-916 行）、`Pool.cs`（FastSpawn/FastClone 惰性克隆）、`BiomeSwapData.cs`（swap 查表）。
- 禁止 git commit/push/stash/reset；禁止部署；禁止写任何游戏目录。
- 你的沙箱无法跑 dotnet 构建（上轮已证）：请做 AST 语法自查 + 逐符号语义核对，
  构建由 Operator 执行。

## 验收（汇报必须包含）

1. 修改后的 `EnsurePrefabLayout`（及相关新辅助方法）完整代码；
2. 逐条对照本任务书 5 个设计点的落实说明；
3. `git status --porcelain` 输出（应只有本文件 + PatchWorld_SpecialTowerRebuild.cs 变更，
   Operator 的其他未提交文件除外）；
4. 明确声明未改动网关/token 逻辑、未动其他文件。
