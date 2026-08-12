# 城堡升级链路 — 两条入队路径的陷阱

## 核心陷阱：商店入队有两条独立路径，各自硬编码，不等价

商店进入游戏世界经过 `Castle.Start()` 分流：

```
Castle.Start()
├── _justBuilt == true → CatchupToLevel(false)    // 新建/升级城堡
└── _justBuilt == false → ReQueueAllBuildings()    // 读档恢复
```

**这两条路径的商店清单各自独立硬编码，不等价。** 如果只 patch 一条，商店会"时有时无"（新建游戏有，读档后消失；或反过来）。

---

## 路径1：CatchupToLevel(bool includePrevious)

触发时机：
- 新建城堡：`Start()` 里 `_justBuilt==true` → `CatchupToLevel(false)`
- 读档恢复：`OnPrefabCatchup()` → `DelayCastleCatchup()` → `CatchupToLevel(true)`

逻辑：按城堡等级逐级执行，每级有条件入队商店。关键分支：

| 城堡等级 | 入队商店 | 条件 |
|----------|----------|------|
| Castle1 | Bow, Hammer | 无条件（通过 PrefabPlaceholder swap） |
| Castle3 | Scythe | `!director.doExpoRun` |
| Castle3 | ChangeRuler | `BiomeIndex == 2` |
| Castle3 | ChangeItem | `BiomeIndex == 3` |
| Castle4 | ChangeItem | `BiomeIndex == 5`（希腊） |
| Castle4 | ShieldShop / Pike | 由 `optionalShopType` 决定 |
| Castle5 | Ninja | `optionalShopType == Ninja` |
| Castle5 | Pike（fallback） | `optionalShopType == Pike` |
| Castle5 | WorkshopLeft/Right | `!noWorkshop` |
| Castle7 | Forge | 无条件 |

> **`optionalShopType`** 是 Castle 的 public 字段（`PayableShop.UnsidedShopType`），由 prefab 预设。
> 希腊城堡的 `optionalShopType == Pike`，所以 Castle5 走 Pike 而非 Ninja。

---

## 路径2：ReQueueAllBuildings()

触发时机：`Start()` 里 `_justBuilt==false`（读档时）

逻辑：按城堡等级遍历 7 个硬编码数组（array~array7），每个数组对应一个等级。每个 `ShopPlacementData` 带 `biome` 字段（-1 = 所有生物群系），与当前 `BiomeHolder.Inst.BiomeIndex` 匹配才入队。

**biome 白名单（已验证）：**

| 商店 | 允许的 biome |
|------|-------------|
| Scythe | -1（全部） |
| ChangeRuler | 2 |
| ChangeItem | 3 |
| ShieldShopLeft/Right | 3 |
| PikeLeft/Right | 0, 2, 3, 5 |
| NinjaLeft/Right | **仅 1**（幕府） |
| WorkshopLeft/Right | 0, 1, 2, 5 |
| Forge | -1（全部） |

> **注意：** Ninja 在白名单里只有 biome=1，所以读档时希腊世界不会恢复忍者商店。
> Workshop 在 biome=3（北境）没有——北境用 ShieldShop 代替。

---

## 如何让某生物群系出现额外商店（自洽方案）

同时 patch `CatchupToLevel` 和 `ReQueueAllBuildings` 的 Postfix，共用一个幂等 helper：

```csharp
private static void EnsureShopInBiome(Castle castle, int targetBiome,
    Castle.Level minLevel, PayableShop.ShopType leftType, PayableShop.ShopType rightType)
{
    if (BiomeHolder.Inst.BiomeIndex != targetBiome) return;
    if (castle.level < minLevel) return;
    var sp = SingletonMonoBehaviour<Managers>.Inst.shopPlanner;
    if (!sp.IsPlacedOrQueued(leftType))
        sp.QueueNewShopForPlacement(leftType, Side.Left);
    if (!sp.IsPlacedOrQueued(rightType))
        sp.QueueNewShopForPlacement(rightType, Side.Right);
}
```

**为什么安全：**
- `IsPlacedOrQueued(shopType)` 做 `raisingShops` / `_placedShops` / `_queuedShopPlacements` 三重检查，不重复入队
- `_placedShops` 按 ShopType 枚举值做数组索引，不同商店不冲突
- `CanShopFit` 保证位置不重叠，放不下进队列等边界扩张

**前提条件：** 目标商店的 prefab 必须先注册到 `shopTypePrefabPairs`（见 Patch_ShopPlanner / biome-asset-system.md）。

---

## 验证机制（不会误删 mod 添加的商店）

`ShopPlanner.ValidateShops()` 每帧检查已放置商店的科技时代是否匹配当前位置：
- 如果 `TechAgeForShop > TechAgeForPosition`，despawn 该商店
- Ninja 是 Stone Age，希腊 Castle5 也是 Stone Age，不会被误删
- `ReQueueAllBuildings` 只添加不清理，不会主动移除 mod 添加的商店
