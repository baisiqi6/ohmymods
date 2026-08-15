# ohmymods — 架构

> 游戏反编译业务逻辑地图（商店/兵种/城堡/生物群系全链路、已验证 patch 模式与已知坑）：
> 见 [game-logic-map/](game-logic-map/README.md)。

## 总览

项目保留两套独立源码，但只有 IL2CPP 2.4.0 + BepInEx 6 是发布与端到端验收主线；
Mono 2.1.0 + UMM 已冻结为历史/自用线，除非用户明确要求，否则不随主线同步。

### IL2CPP 发布线（`il2cpp/KingdomEnhancedMod.dll`）

```
KingdomEnhancedPlugin.cs  BepInEx 6 插件入口、HarmonyX 注册
ModConfig.cs / ModPanel.cs 配置持久化与游戏内面板
PatchEconomy_*.cs         钱包、银行家、商店等经济域
PatchRoles_*.cs           Holder、角色池、转职、工匠、盾牌等角色域
PatchWorld_*.cs           移动、建造、地图、敌人和神器等世界战斗域
```

边界：2.4.0 interop 壳决定可用签名；world authority 负责发起网络状态变化；
对象池 OnEnable 早于 CRPC 注册，任何 RPC 必须等 NetworkPostbox 发布对象后再发送。

### Mono 自用线（`MyMod.dll`）

入口 `Main.cs` 注册 Harmony v1.2 Patch，每个 `Patch_*.cs` 只负责一个领域。

```
Main.cs                 UMM 入口（OnToggle/OnGUI），Main.Enabled 全局开关
Patch_ShopPlanner.cs    ShopPlanner.InitializeShopTypePrefabPairs 的 Prefix 全量替换（return false）：
                        跨生物群系注册全部 uniqueShopPrefabs（含狂战士/忍者商店）+ 安全写入
                        （[] 赋值防重复 key 崩溃，见 biome-asset-system.md / shop-system.md）
Patch_Castle.cs         城堡商店入队（狂战士/忍者）+ 角色池 sync 注册
Patch_Mover.cs          玩家移动速度倍率：SetGoal/SetGoalSpeed/SetGoalNoHaglet 入口乘倍率
                        （仅 Player，speed 参数 prefix；不再 patch Mover.Update）
Patch_Construction.cs   快速建造
Patch_Kingdom.cs        地图扩展（幂等：原生基准值缓存 _vanillaMinExtents）+ 希腊猫生成
                        （狂战士/忍者 hack 已退役注释，只留猫生成）
Patch_Holder.cs         希腊世界 tagCharacterPairs：Worker→Worker_norselands，Peasant→Peasant_norselands
Patch_FriendlyTroll.cs  transpiler 已禁用（Harmony 1.2 崩溃），占位保留
Patch_EnemyManager.cs   怪物数量/时间线倍率（AddEnemies/GetEnemies Prefix）
Patch_Knight.cs         狂战士跟随骑士（护驾行为）
Patch_Banker.cs         银行家去重 + 旧存档残留清理 + 共享银行增强
                        （补员到 5 已删除：NetID 903 唯一不可实现，见 domain-model.md D8）
Patch_Worker.cs         工匠缩放（OnEnable）+ 北境工匠出生带盾 + y 轴守护注册表
Patch_WorkerScale       单位缩放统一注册：OnEnable 登记 + Mover.Update postfix 每帧恢复 y
Patch_Character.cs      乞丐→北境 WarriorPeasant 替换（Promote_Prefix）
Patch_SidedShop.cs      SidedShop 实例以 ShieldShop 身份自注册（配合商店刷新）
Patch_PoolManager.cs    原生池重建 + mod sync 池重注册
Patch_World.cs          希腊草地补充（ExpandGrass）
Patch_BeggarCamp.cs     [IL2CPP 临时] 每帐篷 spawnInterval=1f、上限5（原生扫描段使实际约6秒/个）
Patch_Artemis.cs        单发箭伤害 20 次（_maxHitsPerArrow=0f → 上限 0+20）
Patch_HermesStaff.cs    权杖控制 16（_maximumConvertedTrolls 8→16；控制永久原生已对齐）
```

## 关键机制

### 1. 商店槽位劫持（狂战士/忍者）
- 希腊城堡 `optionalShopType=Pike`，原版不会入队 ShieldShop（那是北境 biome=3 的）。
- `Patch_ShopPlanner`：`InitializeShopTypePrefabPairs` 的 **Prefix 全量替换**（return false 跳过原版），
  把 `shopTypePrefabPairs[ShieldShopLeft/Right(12/13)]` 注册为狂战士商店 prefab（同时跨生物群系注册全部商店）。
- `Patch_SidedShop`：让实例以 ShieldShop 身份自注册，商店刷新逻辑不崩溃。
- 结果：狂战士/忍者走商店原生购买生成，不需要 hack 每局刷。

### 2. Holder 角色替换（北境形象）
- `Patch_Holder`：biome=5 时把 `tagCharacterPairs["Worker"]`→`Worker_norselands`、
  `tagCharacterPairs["Peasant"]`→`Peasant_norselands`。
- 所有生成路径（投币招募、工具转化、读档恢复）都走 `GetCharacterByTag`，一处替换全覆盖。
- 必须配套 `Patch_Castle.EnsurePoolForCharacter(tag)` 注册 sync 池，否则 Pool.Spawn 失败/联机 desync。

### 3. 单位缩放（y 轴守护）
- 根因：游戏用 `localScale.x` 符号（±1）做朝向，`Mover.Update` 每帧把 localScale 覆盖为 `(±1,1,1)`，
  一次性设置下一帧被清。
- 解法：`UnitScaleRegistry`（ConditionalWeakTable<Mover,float>）登记目标 y 缩放，
  `Mover_Update_Postfix` 每帧恢复 y。x 不动（朝向 + `velocity.x *= localScale.x` 依赖）。
- 所有单位 OnEnable（对象池复用也触发）时登记；弱引用无泄漏。
- 缩放值：北境工匠 1.175、希腊工匠 1.075、北境居民 1.125、希腊居民 1.05、鹿 0.55、小动物 1.8。

### 4. 北境工匠带盾
- 希腊 12/13 槽位被狂战士商店占用 → 无盾牌商店 → 工匠买不到盾。
- 只允许 world authority 对北境实例装备，盾引用必须属于当前实例；网络对象完成注册后再
  `NpcShieldUser.SetShieldEnabled(true)`。OnEnable 只做本地登记，不能直接发送 shield RPC。

## 性能约束

- 禁止每帧 FindObjectsOfType / 多次 GetComponent（旧版掉帧源，已删）。
- Mover.Update postfix 是唯一每帧 hook：一次字典查找 + 一次比较，未变零写入。
- OnEnable 是池复用本地初始化点（Awake/Start 只在首次创建触发），不是网络注册完成事件。
