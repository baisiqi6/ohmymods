# 经济域迁移笔记（notes-economy.md）

迁移范围：Hermes 钱袋（PatchEconomy_CurrencyBag.cs）、银行家增强（PatchEconomy_Banker.cs）、
跨世界商店 + 狂战士商店改写（PatchEconomy_Shops.cs）。

编译状态：`dotnet build -c Debug -p:BaseOutputPath=bin/economy/ -p:BaseIntermediateOutputPath=obj/economy/`
→ **0 错误 0 警告**（签名验证源：interop Assembly-CSharp.dll 2.4.0，get_type_members.py 核对）。

---

## 待 Operator 决策

### 1. 钱包容量机制在 2.4.0 疑似重做（CurrencyBag 扩容效果待验证）
- 现象：Mono 2.1.0 `Wallet.TotalCapacity`（int 字段，硬编码 1000）是扩容唯一杠杆；
  2.4.0 `Wallet` 仍保留 `TotalCapacity` 字段，但同时新增多币种 `CurrencyMap<int> _currencyAmount`、
  `CurrencyMap<bool> _allowedCurrencies`、`GetCurrency(CurrencyType)/SetCurrency(CurrencyType,int)`。
- 风险：`TotalCapacity` 在 2.4.0 可能已退化为遗留字段，直接赋值不再控制金币上限
  （IL2CPP 方法体为 stub，无法静态确认）。扩容可能失效或行为异常。
- 本组处置：忠实移植 `ChangeCurrencyBag_Postfix` 写 `player.wallet.TotalCapacity`（编译通过，字段存在），
  未硬写 `CurrencyMap` 假设代码。
- 待决策：集成冒烟测试验证赫尔墨斯袋实际容量；若失效，需改用 `SetCurrency`/`_currencyAmount` 重写扩容，
  或放弃容量段只保留解锁 + 视觉。

### 2. BagCurrency.Reset 改名 + 加参（已适配，语义待确认）
- 现象：Mono `BagCurrency.Reset(int nthCoin, bool stack)` → 2.4.0
  `BagCurrency.ResetVisuals(bool backLayer, int nthCoin, bool stack = true)`。
- 本组处置：改 Hook `ResetVisuals`，前缀签名 `(BagCurrency, bool backLayer, int nthCoin, ref bool stack)`，
  沿用"nthCoin < 600 堆叠、超出散落"逻辑；`backLayer` 参数语义不明、未参与逻辑。
- 待决策：确认 `backLayer` 是否影响堆叠判定（若"后层金币"单独计数，600 上限需按层拆分）。

### 3. CurrencyBag 视觉/扩容体系整体重做（2x 缩放方案效果待验证）
- 现象：2.4.0 `CurrencyBag` 引入 `CurrencyType`（替代旧 `CurrencyBagType` 作金币生成类型）、
  `CurrencyMap`、`SpawnCurrency(CurrencyType)`、`OVERFLOW_LIMIT` 静态常量；金币生成已非单一 `BagCurrency.Reset` 流程。
- 本组处置：`CurrencyBag.Awake` + `CurrencyBagHandler.SetCurrencyBag` postfix 放大 2x、`_container` 反向缩放 1/2
  （`_container` 字段 2.4.0 仍存在，interop 暴露为 public 属性，替代 Mono 反射）。结构上编译通过。
- 待决策：冒烟测试确认视觉"大袋子装更多金币"效果；若 2.4.0 溢出机制改由 `OVERFLOW_LIMIT` 控制，
  应改为 Hook 该常量/`SpawnCurrency` 而非缩放容器。

### 4. Banker Awake 去重前提（NetID 903）在 2.4.0 未验证
- 现象：Mono 版去重理由是 `Banker.Awake` 硬编码 `RegisterObject(903, Dynamic)`，多实例 → duplicate key 崩溃。
  2.4.0 `Banker.Awake` 存在但方法体为 stub，无法确认是否仍注册 NetID 903、是否仍有竞态多实例。
- 本组处置：忠实移植 `Awake_Prefix` 去重（销毁重复实例并跳过 Awake）+ `Update_Postfix` 清理残留 `Banker_Extra`/超量实例。
  未移植已删除的"补员到 5 个"逻辑（与去重自相矛盾，Mono 侧 ArchReviewer P1 已删）。
- 待决策：冒烟测试观察多 Banker 竞态是否复现；不复现可简化/移除去重段。

### 5. 希腊世界 biome 编号由硬编码 5 改为 GreeceBiomeIndex（改进，需确认）
- 现象：Mono 硬编码 `BiomeHolder.Inst.BiomeIndex == 5` 判希腊；2.4.0 新增 `K80sOakAndBirchBiomeIndex`，
  biome 编号可能漂移，同时新增 `BiomeHolder.GreeceBiomeIndex` 静态常量。
- 本组处置：改用 `BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex`（按名引用，规避编号漂移）。
- 待决策：确认 `GreeceBiomeIndex` 运行时值正确、且 ShopPlanner/SidedShop 执行时该静态已初始化。

### 6. PayableShop.ShopType 枚举重排（无影响，仅备案）
- 现象：2.4.0 `ShopType` 枚举插入 `Pike_OLDHANDLE`（值重排，原 PikeLeft/PikeRight/ShieldShopLeft/ShieldShopRight
  数值已变）。本组全部按名引用（`ShieldShopLeft/ShieldShopRight/WorkshopLeft/WorkshopRight`），不受数值影响。
- 待决策：无（仅备案；若未来有序列化/存档依赖枚举数值的地方需注意）。

---

## 迁移技术要点（备案，非决策）

- 集合类型：`ShopPlanner.shopTypePrefabPairs`/`shopPrefabs`、`BiomeSpecificAssets.uniqueShopPrefabs` 为
  `Il2CppSystem.Collections.Generic.*`（非标准 .NET），SafeAdd 签名用全限定 `Il2CppSystem.Collections.Generic.Dictionary<...>`。
- 数组类型：`Object.FindObjectsOfType<Banker>()` 返回 `Il2CppArrayBase<Banker>`、`Resources.LoadAll<ShopTag>("")` 返回
  `Il2CppArrayBase<ShopTag>`、`BiomeHolder.biomePathStrings` 为 `Il2CppStringArray`——统一用 `var` + `.Length`/`foreach`。
- 字段访问：interop 将游戏私有字段暴露为 public 属性，全部由 Mono 反射改为直接属性访问
  （`_stashedCoins`、`coinScanRange`、`_coinScanner`、`walkSpeed`、`runSpeed`、`_container` 等）。
- Banker 的 DropOff/Hide/FinaliseEmerge/Payout 在 2.4.0 为 `IEnumerator` 协程（Mono 可能为 void），postfix 仅用
  `__instance`、不依赖返回类型，签名兼容。
- 共享存款 PlayerPrefs 键名沿用 `MyMod_SharedBankStash`。
- CurrencyBagType 枚举 2.4.0 新增 `EggBasket`；容量段 `(type == Hermes) ? 2000 : 1000` 会把 EggBasket 归入 1000 档（未单独处理）。
