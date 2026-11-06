# 商店系统全链路

## 完整数据流

```
BiomeData.biomeSpecificAssets.uniqueShopPrefabs (List<ShopTag>)
         │
         ▼
ShopPlanner.InitializeShopTypePrefabPairs()  ← Start() 调用，构建 prefab 字典
         │   shopTypePrefabPairs: Dict<ShopType, GameObject>
         │   只加载当前生物群系(curBiomeAssets)的商店
         ▼
Castle.CatchupToLevel() / ReQueueAllBuildings()  ← 决定哪些商店"应该出现"
         │   调 QueueNewShopForPlacement(shopType, side) → _queuedShopPlacements
         ▼
ShopPlanner.PlanShops() (核心协程，循环执行)
         │   → PlaceQueuedShops(): 按优先级排序队列
         │     → AttemptPlaceShop(type, side): 搜索位置
         │       → CanShopFit(): 检查位置是否可用（科技时代/重叠/exclusion）
         │         → CreateShop(): 实例化 prefab, NotifyShopPlaced(), 注册网络
         │       → 放不下 → 留在队列，等边界扩张后重试
         ▼
ShopPlanner.ValidateShops() / ShuffleEdgePreferenceShops()  ← 持续维护
```

---

## 关键数据结构

### ShopPlanner 字段（反编译，private）

| 字段 | 类型 | 说明 |
|------|------|------|
| `shopTypePrefabPairs` | `Dict<ShopType, GameObject>` | ShopType → prefab 映射，InitializeShopTypePrefabPairs 填充 |
| `shopPrefabs` | `List<ShopTag>` | 当前世界预设的商店（Inspector 配置） |
| `_placedShops` | `GameObject[15]` | 已放置商店，按 ShopType 枚举值索引 |
| `_queuedShopPlacements` | `List<ShopPlaceQueueData>` | 等待摆放的队列 |
| `raisingShops` | `Payable[15]` | 正在建造中的商店 |
| `freeSpotResults` | `Dict<float, bool>` | 位置可用性缓存 |

### ShopType 枚举值（数组索引用）

| 值 | 名称 | 科技时代 | 优先级 |
|----|------|----------|--------|
| 0 | Bow | — | 0 |
| 1 | Hammer | — | 0 |
| 2 | Scythe | Wood | 10 |
| 3 | WorkshopLeft | Stone | 1 |
| 4 | Pike_OLDHANDLE | — | — (废弃) |
| 5 | Forge | Iron | 0 |
| 6 | WorkshopRight | Stone | 1 |
| 7 | NinjaLeft | Stone | 5 |
| 8 | NinjaRight | Stone | 5 |
| 9 | PikeLeft | Wood | 9 |
| 10 | PikeRight | Wood | 9 |
| 11 | ChangeRuler | Wood | 2 |
| 12 | ShieldShopLeft | Wood | 9 |
| 13 | ShieldShopRight | Wood | 9 |
| 14 | ChangeItem | Wood | 2 |
| 15 | Total | — | — |

> **坑：** `_placedShops` 数组大小 = 15（枚举值数），按 `(int)shopType` 索引。Pike_OLDHANDLE(4) 是废弃值。

---

## prefab 注册：InitializeShopTypePrefabPairs()

```
shopTypePrefabPairs.Clear();
list = shopPrefabs + curBiomeAssets.uniqueShopPrefabs;   // 只加载当前世界
foreach shopTag in list:
    if PayableSidedShop → 注册 Left + Right 两个 key
    elif PayableWorkshop → 注册 WorkshopLeft + WorkshopRight
    else → 注册 shopTag.type（单 key）
```

**关键：** 原版只加载 `curBiomeAssets`（当前世界）。跨生物群系需要 mod 遍历所有 `biomePathStrings` 加载全部 BiomeData（见 `Patch_ShopPlanner`）。

---

## 位置搜索：AttemptPlaceShop(type, side)

搜索逻辑（从中心向两侧扩展，步长 0.75）：
1. 检查是否已放置（`_placedShops[type] != null` → 跳过）
2. 获取 prefab → `GetShopPrefab(type)` → 从 `shopTypePrefabPairs` 查
3. 确定搜索中心：
   - 普通商店：以 campfirePosition 为中心
   - `preferKingdomEdge` 商店（Workshop/SidedShop）：以对应边 border 为中心
4. 某些商店有前置依赖：
   - ChangeRuler/ChangeItem 需要先有 Scythe
   - ShieldShop/Pike/Forge 需要先有 Scythe
5. `CanShopFit(type, position)` 检查：
   - 不与正在建造的商店重叠（`ShopOverlapsRaisingShop`）
   - 不与 payables exclusion 重叠
   - 位置的科技时代 >= 商店所需时代（`GetTechAgeForPosition`）
6. 找到位置 → `CreateShop()` 实例化；找不到 → 加入队列

---

## 持续维护

### PlanShops 协程（主循环）

```
循环:
  等待触发(OnLevelLoaded / OnBorderUpdate / SetPlacedShop)
  → ValidateShops(): despawn 科技时代不匹配的商店
  → ValidateSidedShopStatus(): 修复 Pike 商店两侧一致性
  → PlaceQueuedShops(): 按优先级排序队列，逐个 AttemptPlaceShop
  → ShuffleEdgePreferenceShops(Left): 重排边缘偏好商店位置
  → ShuffleEdgePreferenceShops(Right)
```

### ShuffleEdgePreferenceShops（边缘重排）

每轮循环检查 `preferKingdomEdge` 的商店（Workshop + 部分 SidedShop），如果发现更靠近边缘的可用位置，会 despawn 然后在新位置重建。这是正常的游戏行为，不影响 mod 添加的非边缘商店。

### DespawnShop

按 ShopType 索引操作 `_placedShops`，播放消失动画后置 null。不影响其他商店。

---

## mod patch 级别

| 层级 | 方法 | 效果 |
|------|------|------|
| 注册 prefab | `InitializeShopTypePrefabPairs` Prefix | 让某 ShopType 的 prefab 可用（Prefix 完全接管，避免原版 Add 崩溃） |
| 入队摆放 | `Castle.CatchupToLevel` / `ReQueueAllBuildings` Postfix | 让某商店在指定世界实际出现 |
| 直接摆放 | `CreateShop(prefab, type, position)` | 跳过队列直接实例化（需自己算位置，少用） |

---

## 跨世界添加商店：槽位复用方案（已验证）

### 核心机制：实例身份由自身 shopType 决定，不由摆放参数决定

`PayableSidedShop.Awake()` 是身份自注册的关键：

```
Awake() → ResolveSidedShops(go) → 按 campfire 位置判断 side
       → OverrideSide(side):
           sidedShopType = GetSidedShopType(this.shopType, side)  // 自身类型决定槽位
           ShopTag.OverrideTag(sidedShopType)                      // 覆写 ShopTag.type
           gameObject.tag = GetShopTag(sidedShopType)              // 覆写 GameObject.tag
       → base.Awake() → PayableShop.Awake → shopPlanner.AddShop(this)
           → SetPlacedShop(ShopTag.type, this)                     // 注册进 _placedShops
```

**推论**：无论你用什么槽位摆放，实例最终会按自身 `shopType` 注册进对应的 `_placedShops` 槽位。所以**让一个商店出现在"额外"槽位，必须在 Awake 阶段改写它的 shopType**。

### 案例：希腊世界狂战士商店（方案C，已验证成功）

**目标**：希腊(biome=5)原生刷新狂战士工具商店（卖 ToolBerserker），同时保留长矛商店。

**资源**：`ShopBerserker_norselands` 是北境狂战士商店 prefab，`Resources.LoadAll<ShopTag>` 可加载，`ShopTag.type=PikeLeft(9)`，`PayableSidedShop(shopType=Pike)`。

**三步实现**：
1. **注册槽位**（`Patch_ShopPlanner` Prefix）：希腊时 `shopTypePrefabPairs[ShieldShopLeft/Right] = ShopBerserker_norselands.gameObject`。选 ShieldShop(12/13) 因为希腊 `optionalShopType=Pike`，原版从不会入队 12/13——**槽位空闲**。
2. **入队**（`Patch_Castle` Postfix）：`QueueNewShopForPlacement(ShieldShopLeft/Right)`。希腊原版逻辑会入队 Pike(9/10) 摆长矛，我们额外入队 12/13 摆狂战士。
3. **身份改写**（`Patch_SidedShop`）：patch `PayableSidedShop.Awake`，仅当 `希腊 && itemPrefab.tag=="BerserkerTool"` 时：
   - Prefix：`shopType = ShieldShop`（临时）
   - 原版 Awake 执行 → OverrideSide 计算 12/13 → 注册进 `_placedShops[12/13]`
   - Postfix：`shopType = Pike`（恢复，全代码库仅 Ninja.GetDojo 消费 shopType，安全）

**为什么必须第3步**：如果实例以 Pike 身份自注册，会进 `_placedShops[9/10]` 覆盖长矛商店——用户明确不要。

**已知副作用**：购买狂战士工具会虚增 `NpcShieldsBought` 统计（tag 被改写为 LeftShieldShop 的必要代价），纯观感无功能影响。

### 槽位选择决策表（希腊视角）

| 槽位 | 空闲? | 说明 |
|------|-------|------|
| PikeLeft/Right (9/10) | ❌ | 希腊原版长矛商店（optionalShopType=Pike） |
| ShieldShopLeft/Right (12/13) | ✅ | 希腊从不入队（那是 biome=3 北境的） |
| Pike_OLDHANDLE (4) | ⚠️ 不可用 | `ValidateSidedShopStatus` 会把队列中 type=4 转成 9/10 |
| NinjaLeft/Right (7/8) | ❌ | 希腊忍者商店（mod 已加） |

### 旧存档残留处理

槽位复用后，旧存档可能在目标槽位摆了**错误内容**（如盾牌商店占 12/13）。入队前必须校验：

```csharp
// 检查槽位已放置的商店是否卖对东西，不对就销毁清槽
if (sp.HasPlacedShop(type)) {
    var placed = /* 反射读 _placedShops[type] */;
    var ps = placed.GetComponent<PayableShop>();
    if (!(ps.itemPrefab != null && ps.itemPrefab.CompareTag("BerserkerTool"))) {
        sp.RemoveShop(placed);
        GameObject.DestroyImmediate(placed);
    }
}
```

## 跨世界扩展槽位计划（IL2CPP 2.4.0，Issue #255）

PC 2.4.0 把希腊方案泛化为 `WorldShopPlan`（`il2cpp/WorldShopPlan.cs`）：忍者用 7/8，狂战士用 12/13
（Awake 临时改 shopType=ShieldShop 注册，Postfix 恢复 Pike）。原生白名单见
[castle-upgrade.md](castle-upgrade.md)；Norse 保留原生 ShieldShop/Berserker，Shogun 保留原生 Ninja。

| 世界 | 新增 | 槽位 | 城堡门槛 |
|---|---|---|---|
| Europe(0) | Ninja + Berserker | 7/8 + 12/13 | Castle5 / Castle4 |
| Dead Lands(2) | Ninja + Berserker | 7/8 + 12/13 | Castle5 / Castle4 |
| Norse(3) | Ninja | 7/8 | Castle5 |
| Shogun(1) | Berserker | 12/13 | Castle4 |
| Greece(5) | 既有 | 7/8 + 12/13 | Castle5 / Castle4 |

注意：
- Android 编译时 `WorldShopPlan` 只返回 Greek 计划（保持 Android 现有行为）。
- 新世界的 12/13 若被未知商店占用，`EnsureBerserkerShop` fail-closed 跳过；不把希腊的旧店清理
  机制复制到未验证世界。
- 池/角色依赖：Ninja 的 ThrowingStar(sync41)/Smokebomb(local)、Berserker/BerserkerLeader/Tool 池，
  每次 `PoolManager.InitPools` 后由 `ReRegisterModPools` 按世界计划重注册。
- 实机购买→领取→转职→战斗、迁址库存、读档/换岛、照明与联机仍待验收（见 `tasks/issue-255/`）。


## 快速购买与自定义店履约（#300，IL2CPP2.4.0 / Win2.4.3 source/host）

火枪4 Coins由自有 PayableComponent 付款，不能按原生工具店tag或自动补货资格识别手动购买：AutoRestock主动排除手动交互。`MusketeerShop.ManualPurchase`只核当前付款点/owner/GO/层身份；原生CanPay/库存/存档保护仍决定可否交易。

HoldPurchase的goods资格允许加速与续买；非goods且当前整价≥7 Coins只加速当笔，完成/取消即结束，不把“钱包仍够整单”用于已投币的加速。不要通过扩goods白名单实现全部高价单次功能。

火枪OnPay出货失败可退4币后正常返回，故这个自定义路径的PerformPay本身不是履约成功证明。两处实际拒绝责任边界先通知终止精确本地hold，再保留原取消/退款，避免自动重发；AlreadySettled重复回调不通知。源码链接133场景/独立144反例、三端编译与产物IL已核；Native未验，见[任务回执](../tasks/issue-300/receipt.md)。
