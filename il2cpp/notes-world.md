# 世界战斗域迁移笔记（IL2CPP 2.4.0）

> 迁移源：Mono 2.1.0（GOG）Patch_Mover / Patch_Construction / Patch_Level / Patch_Kingdom /
> Patch_EnemyManager / Patch_Artemis / Patch_HermesStaff / Patch_FriendlyTroll。
> 签名验证源：`E:/QQ/QQ下载文件/Kingdom Two Crowns (1)/Kingdom Two Crowns/BepInEx/interop/Assembly-CSharp.dll`
> （ilspycmd 8.2.0 反编译元数据）。编译引用：`E:/mod-dev/KingdomMod/deps/.../BIE6_IL2CPP/interop/Assembly-CSharp.dll`。

---

## 0. 关键结论：get_type_members.py 对 `unsafe` 桩漏报（重要）

`get_type_members.py` 的 `parse_members()` 用正则匹配方法声明行
`(public|private|...)\s+(static\s+)?(\w+(<.*>)?)\s+\w+\s*\(`，**不匹配 `unsafe` 关键字**。
Cpp2IL 生成的 IL2CPP 桩方法全部形如 `public unsafe void Foo(...)`，于是除构造函数
（`public unsafe Mover()` 恰好被正则当作"返回类型=unsafe、方法名=Mover"误命中）外，**所有方法都被漏报**。

这直接导致了任务书里的"已知漂移"误报：
> "2.4.0 的 Mover 类还在但 Update 方法没了（仅构造函数）"

**实测（ilspycmd -t 原始输出）Mover.Update() 存在（private）、SetSpeed/SetSpeedToGoal 均存在。**
本次全部签名改用 `ilspycmd -t <类型>` 原始输出核对，未依赖 get_type_members.py 的正则解析。

## 1. Mover 漂移结论（任务指定的待决策项）

- **结论：Mover 无漂移。** 2.4.0 的 `Mover` 类完整保留：
  - `Update()`（private）、`SetSpeed(float)`、`SetSpeed(float, int)`、`SetSpeedToGoal(float)`、
    `ForceSetSpeed(float, int)`、`SetSpeedMultiplier(float, float)`、`_moveSpeed`(public float) 均在。
  - Mono v3 的 `SetSpeed_Prefix` 迁移成立：patch `SetSpeed` 两个重载 + `SetSpeedToGoal`，
    prefix 缩放 `moveSpeed` 参数。
- 迁移差异：Mono 的 `ConditionalWeakTable<Mover, Player>` 身份缓存弃用，改为 prefix 内联
  `GetComponent<Player>()`（Il2Cpp 对象用 CWT 缓存不可靠，每帧一次 GetComponent 开销可忽略）。
- **无需 search_types.py 搜 Movement/PlayerMovement** —— 移动逻辑仍在 `Mover` 类内。

## 2. 各文件 2.4.0 签名核对汇总

| Patch 文件 | 目标 | 2.4.0 存在性 | 签名差异 |
|---|---|---|---|
| PatchWorld_Mover | Mover.SetSpeed / SetSpeedToGoal | ✓ | 无（见上） |
| PatchWorld_Construction | ConstructionBuildingComponent.InitializeBuild / _autoBuildRate | ✓ | `_autoBuildRate` 私有字段→public，免反射 |
| PatchWorld_Level | Level.GenerateInternal / LevelConfig.minLevelWidth | ✓ | GenerateInternal 增加 `int seed` 参数（Mono 无） |
| PatchWorld_Kingdom | Kingdom.OnLevelLoaded / Init / cats + BiomeHolder + Cat | ✓ | `biomePathStrings` string[]→Il2CppStringArray；`Cat.farmHouse` 私有→public |
| PatchWorld_EnemyManager | EnemyManager.AddEnemies / GetEnemies | ✓ | `AddEnemies.multiplier` ref float→float（按值） |
| PatchDivine_Artemis | ArtemisArrow.DamageAffectedEnemies / _maxHitsPerArrow | ✓ | `_maxHitsPerArrow` 私有→public |
| PatchDivine_HermesStaff | HermesStaff.Awake / _maximumConvertedTrolls；FriendlyTroll.ShouldRevertToTroll | ✓ | `_maximumConvertedTrolls` 私有→public；ShouldRevertToTroll public |
| PatchDivine_FriendlyTroll | FriendlyTroll.IsTargetValid | ✓ | 重实现（Mono 版 transpiler 禁用） |

## 3. 版本漂移清单（已在代码内适配，无需 Operator 决策）

1. **Level.GenerateInternal(LevelConfig, int seed)**：Mono `(LevelConfig)` → 2.4.0 增加 `int seed`。
   挂载用字符串名，prefix/postfix 未声明 seed（Harmony 按名匹配，省略即忽略），无需改动逻辑。
2. **EnemyManager.AddEnemies.multiplier 按值**：Mono `ref float` → 2.4.0 `float`。
   HarmonyX prefix 仍用 `ref float multiplier` 拦截缩放（对按值值类型注入 ref 可改原方法入参值）。
3. **BiomeHolder.biomePathStrings 类型**：`string[]` → `Il2CppStringArray`。仍支持 `.Length` / `[i]`。
4. **Cat.farmHouse / Cat.domesticated 可见性**：Mono 用反射读 `farmHouse`（私有）、写
   `<domesticated>k__BackingField`（私有）；2.4.0 `farmHouse` 为 public 字段、`domesticated` 为
   public 只读属性。迁移改为：读 `cat.domesticated` + `cat.farmHouse`，写用
   `SetFromSavedState(CatSaveStatusData)`（public，Mono 版的 fallback 路径，现为主路径）。
5. **Kingdom 狂战士/忍者生成已退役**：Mono 源码中 SpawnBerserkersInGreece 已注释退役，由商店系统
   （Patch_Castle + Patch_SidedShop，其它 worker 域）原生生成。本迁移仅保留"希腊补足猫"逻辑。

## 4. 待 Operator 决策清单

1. **Mover 漂移结论（已解决）**：确认无漂移，按原设计迁移。此前任务书"Update 没了"是
   get_type_members.py 正则漏报 `unsafe` 桩所致，见第 0 节。
2. **PatchWorld_Kingdom 猫生成的运行时验证**：`GetNorseCatPrefab()` 依赖
   `Resources.Load<BiomeData>(norsePath)` + `biomeSpecificAssets.uniqueCharacters` / `swapData.prefabSwapPool`
   深层资产遍历，且 `Cat.SetFromSavedState(Cat.CatSaveStatusData)` 涉及 Il2Cpp struct 封送。
   编译可通过，但**本环境禁止运行游戏**，无法验证运行期行为。**需 Operator 实机验证**：
   - 希腊世界（BiomeIndex==5）进岛后各农场是否补足 3 只家养猫；
   - `Resources.Load<BiomeData>` 是否返回非 null、`uniqueCharacters`/`prefabSwapPool` 是否命中 Norse 猫 prefab；
   - 若 prefab 查找失败（返回 null）则静默不生成猫，不会崩。
3. **PatchDivine_FriendlyTroll 的近似语义**：重实现挂在 `IsTargetValid` 上（Mono 版 transpiler 已禁用）。
   CrownStealer 恰为最近敌人时，巨魔会反复"选中→校验失败→游荡 2 秒"，期间不转而攻击次近的地面敌人。
   若要"完全在选目标循环内排除飞行怪"，需 patch 生成的状态机 `_MoveToTargetRoutine_d__46.MoveNext()`
   （脆弱）。当前近似实现可满足"不追飞行怪"目标，是否接受此局限由 Operator 决定。
4. **PatchWorld_Level 的静态状态**：`_modified`/`_originalWidth` 为静态字段（沿用 Mono 设计），
   依赖 `GenerateInternal` 的 prefix→postfix 同步配对。若 2.4.0 的生成流程改为异步/并发（如
   `LoadBlocksAndGenerate` 协程内多次调用），需改成分岛存储。当前按 Mono 同构同步假设实现。

## 5. IL2CPP 专项注意

- 集合类型一律使用 interop 壳类型（`Il2CppSystem.Collections.Generic.List<T>`、`Il2CppStringArray`），
  代码中通过 `var` / `foreach` 隐式使用，未显式引用类型名。
- 协程：IL2CPP 下 `StartCoroutine` 只接受 `Il2CppSystem.Collections.IEnumerator`，托管 `IEnumerator`
  必须先 `.WrapToIl2Cpp()`（`BepInEx.Unity.IL2CPP.Utils.Collections`）。PatchWorld_Kingdom 已按此处理
  （`#if IL2CPP` 分支）。
- 泛型方法（`GetComponent<T>` / `FindObjectsOfType<T>` / `Resources.Load<T>` / `Instantiate`）为
  Il2CppInterop 提供的泛型包装，KingdomMod 参考工程（BetterPayableUpgrade/OverlayMap）同法使用，编译通过。
- 全程序集由 Plugin `harmony.PatchAll(typeof(KingdomEnhancedPlugin).Assembly)` 自动注册，本组只写
  `[HarmonyPatch]` 类，不写注册代码。
