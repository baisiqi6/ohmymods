# 兵种产生机制

## 两条完全不同的产生链

### 1. 商店直接产出（Bow/Hammer/Ninja/Scythe 等）

```
玩家投币 → PayableShop.HandlePayment()
  → CreateItem() → Pool.Spawn<Droppable>(itemPrefab)
    → DroppableTool 落地
      → Worker 路过拾取 → Character.Promote(tool, controller)
```

部分商店直接生成 Character（如 Ninja 商店直接 spawn Ninja）。

### 2. 工具转化（Berserker/PikeKnight/ShieldKnight）

```
玩家投币 → 商店产出 DroppableTool
  → Worker 拾取 → Character.Promote() → 变成对应兵种
```

---

## Berserker（狂战士）— 工具转化链

### 完整链路

```
BerserkerTool 商店（北境 biome=3 独有，在 uniqueShopPrefabs 里）
  → 卖出 BerserkerTool (DroppableTool, tag="BerserkerTool")
    → Worker 路过拾取 → Worker.TryPickupBerserkerTool()
      → Character.Promote(tool, controller) → Worker 变成 Berserker
```

### 关键代码

**Worker 拾取判定** (`Worker.cs`)——两层判断：
```csharp
// 第1层：_toolsToPickup 白名单（Worker 默认 { "NpcShield", "BerserkerTool" }，无 Katana！）
// Peasant.toolsToPickup = { Pike, Hammer, Bow, Katana, Scythe, Shield, StableKeeperTool }（有 Katana，无 BerserkerTool）
private string[] _toolsToPickup = new string[] { "NpcShield", "BerserkerTool" };

// 第2层：CanPickupDroppable —— NpcShield 有"持盾者"额外条件
public bool CanPickupDroppable(Droppable droppable)
{
    return this._toolsToPickup.Contains(droppable.tag) && this.CanPickUp(droppable)
        && (!droppable.CompareTag("NpcShield")
            || (this.npcShieldUser != null && !this.npcShieldUser.HasShield()));
}

// 实际拾取
private bool TryPickupBerserkerTool(DroppableTool tool)
{
    if (tool == null || tool.pickedUp || !tool.CompareTag("BerserkerTool"))
        return false;
    IUnitController unitController = this._unitController;
    this._unitController = null;
    this._character.Promote(tool, unitController);  // 转化！
    return true;
}
```

**拾取白名单总结（关键，别搞混）：**
- **Katana（忍者道具）**：只有 `Peasant`(平民) 拾取；`Worker`(工匠) 不拾取（`_toolsToPickup` 无 Katana）
- **BerserkerTool（狂战士道具）**：`Worker` 拾取（白名单有）；`Peasant` 不拾取（白名单无）
- **NpcShield（盾牌）**：Worker 拾取，但要求 `npcShieldUser` 存在且没盾

**工具→角色映射** (`Character.cs:890`)：
```csharp
{ "BerserkerTool", "Berserker" },
{ "BerserkerLeaderTool", "BerserkerLeader" },
```

**Leader 工具** (`Berserker.cs:762`)：
- 普通 Berserker 拾取 `BerserkerLeaderTool` 可升级为 Leader
- 条件：`!isLeader && kingdom.isSafe && droppable.CompareTag("BerserkerLeaderTool") && droppable.CanBePickedUp`

### 探测结论（已验证，含纠错）

**BerserkerTool 商店 prefab 存在**——`ShopBerserker_norselands` 在 Resources 里（`Resources.LoadAll<ShopTag>("")` 可搜到），`ShopTag.type=PikeLeft(9)`，`PayableSidedShop(shopType=Pike)`。最初误判"不存在"是因为漏搜了 ShopTag 类型（见 patch-patterns.md 坑3）。

**狂战士商店跨世界方案**：槽位复用（希腊用 ShieldShop 12/13 槽）+ `PayableSidedShop.Awake` 身份改写（见 shop-system.md）。

**但 droppable 可加载：**
```csharp
Resources.Load<DroppableTool>("ToolBerserker");       // tag="BerserkerTool"
Resources.Load<DroppableTool>("ToolBerserkerLeader"); // tag="BerserkerLeaderTool"
```

**全量 DroppableTool 列表（Resources.LoadAll 验证，共12个）：**
Crown, ToolArmor, ToolBerserker, ToolBerserkerLeader, ToolBow, ToolHammer, ToolNinja, ToolNpcShield_norselands, ToolPike, ToolScythe, ToolShield, ToolStableKeeper

### 自洽方案：动态构造虚拟商店（已废弃）

> **已废弃（2026-08-12）**：克隆商店 + 换 itemPrefab 的方案最终被放弃——克隆的
> `PayableShop.Awake` 会覆盖原商店槽位、网络组件注册冲突（NetID 冲突）、无法存档持久化。
> 详见 [patch-patterns.md 坑10](patch-patterns.md#10-克隆商店方案已废弃记录原因)。最终采用
> **槽位复用 + Awake 身份改写**（见 shop-system.md）。以下内容保留备查，勿再实现。

既然没有商店 prefab，克隆一个已放置的 `PayableShop`，替换其 `itemPrefab` 为 `ToolBerserker`：
1. 加载 `Resources.Load<DroppableTool>("ToolBerserker")`
2. 克隆希腊已放置的商店 GameObject（如 Bow 商店）
3. 反射设置 `PayableShop.itemPrefab = toolBerserker`
4. 放在城堡附近，玩家投币产出 ToolBerserker → 工匠拾取 → 转化为 Berserker

BerserkerTool 商店没有自己的 `ShopType` 枚举值。它是北境 `uniqueShopPrefabs` 里的一个 ShopTag，注册到 `shopTypePrefabPairs` 时用的是它自己的 `shopTag.type` 值（需要运行时探测确认具体值）。

### Kingdom 管理

- `Kingdom.Berserkers` → `List<Berserker>`（public 属性，get 返回 `_berserkers`）
- `Kingdom.AddBerserker(berserker)` / `RemoveBerserker(berserker)`
- `Kingdom.DistributeBerserkersAcrossSides()` → 按 X 坐标分配到 Left/Right

---

## Ninja（忍者）— 商店直接产出

### 完整链路

```
NinjaLeft/NinjaRight 商店（幕府 biome=1 独有）
  → 玩家投币 → 直接 spawn Ninja (Character)
```

### ShopType 完整支持

Ninja 有完整的 `ShopType.NinjaLeft(7)` / `NinjaRight(8)` 枚举值，走标准队列系统。

### 入队条件

- `Castle.CatchupToLevel`：Castle5 时 `optionalShopType == Ninja`
- `ReQueueAllBuildings`：`ShopPlacementData { biome = 1 }`

### 让希腊世界出现忍者商店（已验证方案）

1. **注册 prefab**：`Patch_ShopPlanner.Prefix` 全量替换 InitializeShopTypePrefabPairs（return false），遍历所有 biomePathStrings，注册全部 uniqueShopPrefabs（包括幕府的 Ninja 商店 prefab）
2. **入队摆放**：`Patch_Castle` patch `CatchupToLevel` + `ReQueueAllBuildings`，在希腊(biome=5) Castle5 时入队 NinjaLeft/NinjaRight

---

## Character.Promote — 核心转化方法

`Character.Promote(DroppableTool tool, IUnitController controller)` 是所有"工具→兵种"转化的核心入口。

通过 `tool.tag` 查 `Character.toolToCharacterMap` 确定目标角色类型。

---

## Holder — 角色 prefab 容器

`Holder` 持有 `tagCharacterPairs: Dict<string, Character>`，由 `InitializeTagCharacterPairs()` 填充（当前世界的 uniqueCharacters）。

跨生物群系共享角色 prefab 的方式：遍历所有 biomePathStrings 加载全部 BiomeData 的 `uniqueCharacters`（见 `Patch_Holder`）。

### 常用 tag

| Tag | Character |
|-----|-----------|
| `Berserker` | 狂战士 |
| `BerserkerLeader` | 狂战士队长 |
| `Ninja` | 忍者 |
| `Worker` | 工匠 |

---

## WarriorPeasant

Ninja hack 版本里替换的目标是 `WarriorPeasant`（武装农民/民兵），不是 `Worker`。这是 mod 演进过程中选择的替换目标。

**自洽方案不需要替换任何单位**——玩家在忍者商店花钱购买即可。
