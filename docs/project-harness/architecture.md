# ohmymods — 架构

> 游戏反编译业务逻辑地图（商店/兵种/城堡/生物群系全链路、已验证 patch 模式与已知坑）：
> 见 [game-logic-map/](game-logic-map/README.md)。

## 总览

单 DLL（MyMod.dll）Harmony patch 集合。入口 `Main.cs` 注册所有 Patch 类，每个 Patch 类只 patch 游戏的一个领域。

```
Main.cs                 UMM 入口（OnToggle/Update），Main.Enabled 全局开关
Patch_Shop.cs           PayableShop.Awake 改写：希腊 12/13 槽位 → 狂战士商店
Patch_SidedShop.cs      SidedShop 实例以 ShieldShop 身份自注册（配合商店刷新）
Patch_Castle.cs         城堡商店入队（狂战士/忍者）+ 角色池 sync 注册
Patch_Kingdom.cs        地图扩展 + 希腊猫生成（狂战士/忍者 hack 已退役注释）
Patch_Holder.cs         希腊世界 tagCharacterPairs：Worker→Worker_norselands，Peasant→Peasant_norselands
Patch_Character.cs      乞丐→北境 WarriorPeasant 替换（Promote_Prefix）
Patch_Worker.cs         工匠缩放（OnEnable）+ 北境工匠出生带盾 + y 轴守护注册表
Patch_WorkerScale       单位缩放统一注册：Mover.Update y 轴守护（每帧恢复 y）
Patch_Banker.cs         银行家相关（金币/利息调整）
Patch_Knight.cs         骑士相关
Patch_Enemy.cs          敌人相关
Patch_FriendlyTroll.cs  友好巨魔
Patch_Construction.cs   建筑
Patch_Mover.cs          （旧版缩放方案遗留/其他移动相关）
Patch_World.cs          世界
Patch_PoolManager.cs    池管理
Patch_Probe.cs          运行时探测工具（打日志查 prefab/缩放，用于调参）
```

## 关键机制

### 1. 商店槽位劫持（狂战士/忍者）
- 希腊城堡 `optionalShopType=Pike`，原版不会入队 ShieldShop（那是北境 biome=3 的）。
- `Patch_Shop`：把 `shopTypePrefabPairs[ShieldShopLeft/Right(12/13)]` 改写为狂战士商店 prefab。
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
- 缩放值：北境工匠 1.175、希腊工匠 1.075、北境居民 1.125、狂战士 1.2、鹿 0.55、小动物 1.8。

### 4. 北境工匠带盾
- 希腊 12/13 槽位被狂战士商店占用 → 无盾牌商店 → 工匠买不到盾。
- `Patch_Worker.EquipShieldIfNorselands`：出生时 `NpcShieldUser.SetShieldEnabled(true)` 直接装备
  （跳过购买，盾是 prefab 序列化引用）。

## 性能约束

- 禁止每帧 FindObjectsOfType / 多次 GetComponent（旧版掉帧源，已删）。
- Mover.Update postfix 是唯一每帧 hook：一次字典查找 + 一次比较，未变零写入。
- OnEnable 是出生/池复用的唯一 hook 点（对象池游戏，Awake/Start 只在首次创建触发）。
