# 生物群系资源系统

## 核心类关系

```
BiomeHolder (MonoBehaviour 单例)
├── biomePathStrings: string[]          // 所有生物群系的 Resources 路径（全局数组）
├── biomeData: BiomeData[]              // 按索引缓存的 BiomeData
├── BiomeIndex: int                     // 当前生物群系索引
├── curBiomeData: BiomeData             // 当前 BiomeData
└── curBiomeAssets: BiomeSpecificAssets // 当前生物群系资源（从 curBiomeData.biomeSpecificAssets 取）

BiomeData (ScriptableObject)
└── biomeSpecificAssets: BiomeSpecificAssets

BiomeSpecificAssets (ScriptableObject)
├── uniqueShopPrefabs: List<ShopTag>       // 独有商店 prefab
├── uniqueCharacters: List<Character>       // 独有角色 prefab
├── uniquePrefabMasterCopies: List<PrefabID>
├── uniqueScatterableData: List<ScatteredObjectData>
├── biomeSteeds: List<Steed>
├── itemsOfPower: List<ItemOfPower>
├── rulerPortraits: List<RulerPortrait>
└── biomeBanner / biomeBannerTorn: Sprite
```

---

## Resources.Load — 与当前世界无关

**关键认知：** `Resources.Load<BiomeData>(path)` 从 Unity 的 Resources 文件夹加载，与当前游戏世界无关。只要路径正确，任何世界都能加载任何生物群系的 BiomeData。

`biomePathStrings` 是全局数组，包含所有生物群系的路径。这意味着：
- **在希腊世界也能加载北境的 BiomeData**
- **在幕府世界也能加载希腊的商店 prefab**
- 探测任何生物群系的资源数据，不需要切换到那个世界

### 代码模式

```csharp
var biomePathStrings = BiomeHolder.Inst.biomePathStrings;  // public 字段
for (int i = 0; i < biomePathStrings.Length; i++)
{
    var biomeData = Resources.Load<BiomeData>(biomePathStrings[i]);
    var assets = biomeData.biomeSpecificAssets;
    // 访问 assets.uniqueShopPrefabs / assets.uniqueCharacters 等
}
```

---

## biomePathStrings 索引与 BiomeIndex 对应

| 索引 | 生物群系 |
|------|----------|
| 0 | Europe |
| 1 | Shogun |
| 2 | Dead Lands |
| 3 | Norse Lands |
| 4 | (?) |
| 5 | Call of Olympus |

> `biomePathStrings[3]` 对应北境，`biomePathStrings[5]` 对应希腊。
> biome 3 和 5 是 DLC。

---

## 跨生物群系共享资源的标准 patch 模式

### 1. 注册所有商店 prefab（Patch_ShopPlanner）

hook `ShopPlanner.InitializeShopTypePrefabPairs` Postfix，遍历所有 biomePathStrings，把每个生物群系的 `uniqueShopPrefabs` 注册到 `shopTypePrefabPairs`。

**注意：** `InitializeShopTypePrefabPairs` 在 `Start()` 调用，此时 `curBiomeAssets` 已初始化。用反射访问 private 字段 `shopTypePrefabPairs`。

### 2. 注册所有角色 prefab（Patch_Holder）

hook `Holder.InitializeTagCharacterPairs` Postfix，遍历所有 biomePathStrings，把每个生物群系的 `uniqueCharacters` 注册到 `tagCharacterPairs`。

---

## BiomeSpecificAssets 已知独有资源

### 北境 (biome=3)
- `uniqueShopPrefabs`：BerserkerTool 商店、ShieldShop
- `uniqueCharacters`：Berserker、BerserkerLeader、（北欧猫？）

### 幕府 (biome=1)
- `uniqueShopPrefabs`：NinjaLeft/NinjaRight 商店
- `uniqueCharacters`：Ninja

### 希腊 (biome=5)
- `uniqueShopPrefabs`：PikeLeft/PikeRight、（ChangeItem 走 Castle 升级）
- 城堡的 `optionalShopType == Pike`

---

### 探测盲区教训（血泪史）

**案例**：最初判断"BerserkerTool 商店不存在于 Resources"是错的——`ShopBerserker_norselands` 一直就在 Resources 里，`Resources.LoadAll<ShopTag>("")` 一搜就有（共 32 个 ShopTag prefab）。

**错误原因**：第一轮只搜了 `Resources.LoadAll<DroppableTool>("")`（工具）、`uniqueShopPrefabs`（列表）、`uniquePrefabMasterCopies`、ShopType 枚举——**唯独没搜 `Resources.LoadAll<ShopTag>("")`（商店类型本身）**。

**规则**：探测时必须用 `Resources.LoadAll<T>("")` 覆盖**所有相关组件类型**，不要假设资源一定挂在某个 biome 列表里。商店 prefab 可能在 Resources 但不在任何 `uniqueShopPrefabs` 里（场景预置但 prefab 本体在 Resources）。

**另一个关键事实**：`PayableSidedShop.Awake()` → `OverrideSide()` 会把实例的 `ShopTag.type` 覆写为按落点侧计算的 sided 类型（如 Pike→PikeLeft/Right）——**实例身份由自身 shopType 决定，不由摆放参数决定**。跨槽位复用商店时必须在 Awake 阶段改写身份。

### 探测未知 prefab 数据的技巧

在 `InitializeShopTypePrefabPairs` 或 `InitializeTagCharacterPairs` 的 Postfix 里加临时日志：

```csharp
// 探测某生物群系的商店 prefab
for (int i = 0; i < biomePathStrings.Length; i++)
{
    var biomeData = Resources.Load<BiomeData>(biomePathStrings[i]);
    if (biomeData == null) continue;
    foreach (var shopTag in biomeData.biomeSpecificAssets.uniqueShopPrefabs)
    {
        Debug.Log($"[PROBE] biome={i} shopTag.type={shopTag.type} " +
            $"sided={shopTag.GetComponent<PayableSidedShop>() != null} " +
            $"workshop={shopTag.GetComponent<PayableWorkshop>() != null} " +
            $"name={shopTag.gameObject.name}");
    }
}
```

> **不需要切换到目标世界**——biomePathStrings 是全局的，任何世界运行时都能加载。
