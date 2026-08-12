# Mod Patch 模式与已知坑

## 项目约定

- Harmony v1.2：用 `HarmonyInstance.Create(id)`，不是 `Harmony.Create(id)`
- 每个 patch 类有 `Register(HarmonyInstance harmony)` 静态方法
- `Main.Load()` 里统一调用所有 `Register`
- 反射访问 private 字段：`typeof(X).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance)`
- 代码风格：保持和现有 patch 一致（反射式，非 attribute 式）

## 编译

`build.bat` 在本仓库根目录（从游戏 `Mods/MyMod/` 迁入，路径引用游戏 Managed DLL）：

```batch
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /out:MyMod.dll ^
    /reference:..\..\KingdomTwoCrowns_Data\Managed\Assembly-CSharp.dll ^
    /reference:..\..\KingdomTwoCrowns_Data\Managed\UnityModManager\UnityModManager.dll ^
    /reference:..\..\KingdomTwoCrowns_Data\Managed\UnityModManager\0Harmony-1.2.dll ^
    ... 其他 UnityEngine DLL ...
    *.cs
```

`build.bat` 已通配化：`for %%F in (Main.cs Patch_*.cs)` 自动收集源文件，新增 .cs 文件无需改列表；
编译成功后自动拷贝到游戏 `Mods/MyMod/MyMod.dll`（见 harness-checklist.json maint-003）。

---

## 已验证的 patch 模式

### Postfix 修改私有字段

```csharp
var field = typeof(TargetClass).GetField("fieldName",
    BindingFlags.NonPublic | BindingFlags.Instance);
if (field != null)
    field.SetValue(__instance, newValue);
```

### 条件性 Prefix（拦截原始方法）

```csharp
public static bool Prefix(TargetClass __instance, ...)
{
    if (!Main.Enabled) return true;  // return true = 执行原方法
    // ... 自定义逻辑 ...
    return false;  // return false = 跳过原方法
}
```

### Transpiler 修改 IL

参见 `Patch_FriendlyTroll`（FriendlyTroll.MoveToTargetRoutine）。复杂且脆弱，**Harmony 1.2 下已禁用**（崩溃），勿用。

---

## 已知坑

### 1. 商店"时有时无"（两条入队路径）

**症状：** 新建游戏商店出现，读档后消失（或反之）。

**原因：** Castle 有两条独立路径入队商店（CatchupToLevel / ReQueueAllBuildings），清单不等价。只 patch 一条会导致不一致。

**解法：** 两条都 patch，共用幂等 helper + `IsPlacedOrQueued` 检查。见 castle-upgrade.md。

### 2. biomePathStrings 是全局的

`biomePathStrings` 包含所有生物群系路径，`Resources.Load` 与当前世界无关。不要以为"在希腊世界加载不到北境数据"——可以。

### 3. BerserkerTool 商店定位（探测盲区教训）

**结论：** `ShopBerserker_norselands` 在 Resources 里（`Resources.LoadAll<ShopTag>("")` 共 32 个 ShopTag prefab 之一），`ShopTag.type=PikeLeft(9)`，`PayableSidedShop(shopType=Pike)`。

**教训：** 最初误判"它不在 Resources 里"是因为只搜了 `DroppableTool`/`uniqueShopPrefabs`/`uniquePrefabMasterCopies`，**漏搜 `Resources.LoadAll<ShopTag>("")`**。探测必须覆盖所有相关组件类型，不要假设资源挂在某个 biome 列表里。

### 4. Pike_OLDHANDLE (ShopType=4) 是废弃值

`ShopPlanner.ValidateSidedShopStatus()` 会把队列中的 `Pike_OLDHANDLE` 转换为 `PikeLeft + PikeRight`。不要直接使用这个值——无法作为"空闲槽位"。

### 5. 反编译代码中的编译器生成名

反编译代码里有 `<>c__DisplayClass`、`<>9__0` 等编译器生成的闭包类名。读逻辑时忽略这些，关注实际行为。

### 6. UMM 从未加载 mod（配置陷阱）

**症状：** 游戏正常运行，但 mod 代码完全不生效，Player.log 里没有任何 `[MyMod]` 输出。

**根因：** UMM 安装器游戏列表里没有 Kingdom Two Crowns。如果安装时选了其他游戏（如 "A Dance of Fire and Ice"），`Config.xml` 的 `StartingPoint` 会指向不存在的类（如 `ADOStartup`），导致 mod 注入链路从第一步就断。

**确认方法：** 看 Player.log 开头，如果出现 `[Manager] Game: A Dance of Fire and Ice` 和 `[Manager] [Error] Class 'ADOStartup' not found`，就是这个坑。

**解法：** 修改 `KingdomTwoCrowns_Data/Managed/UnityModManager/Config.xml`：
```xml
<StartingPoint>[Assembly-CSharp.dll]Managers.Awake:After</StartingPoint>
<UIStartingPoint>[Assembly-CSharp.dll]Managers.Awake:After</UIStartingPoint>
```
`Managers` 是游戏核心单例（`SingletonMonoBehaviour<Managers>`），Awake 100% 会被调用。

### Player.log 路径

```
%USERPROFILE%\AppData\LocalLow\noio\KingdomTwoCrowns\Player.log
```
公司名是 `noio`，不是 `Raw Fury`。

### 7. 跨生物群系商店投币崩溃 + sync 池

**症状（两层）：**
1. 投币时报 `Pool not found for ToolNinja ... allowCreatePool/allowInstantiate is false` + NRE
2. 池建了但角色转化崩：`Pool not found for Ninja`，或 `Ninja.SendSide` 的 `parentHeaderRef.CallMethodRemotely` NRE

**根因：** `PayableShop.CreateItem()` 和 `Character.ReplaceBy()` 都用 `Pool.Spawn`。每个生物群系只为自己独有的 prefab 注册池。希腊世界没有 ToolNinja/ToolNinja角色/Berserker角色的池。而且**动态建的池默认 `sync=false`**——`FastSpawn → AttemptSpawnSync` 里 `if (this.sync)` 才注册网络（设置 `parentHeaderRef`），非 sync 池 spawn 的角色 `parentHeaderRef` 为 null，`Ninja.SendSide()` 直接 NRE。

**解法（两步）：**
```csharp
// 1. 创建池（幂等）
var pm = SingletonMonoBehaviour<Managers>.Inst.pools;
pm.CreatePoolFor(prefab.gameObject);

// 2. 关键：设置 sync=true + 唯一 syncID + 注册进 PoolManager 三个缓存
pool.sync = true;
pool.syncID = /* 高位唯一值如 30000+ */;
// 反射：cachedPools.Add(pool)、cachedNamePoolPairs.Add(name, pool)、
//       cachedSyncIdPoolPairs.Add(syncID, pool)
// 否则：联机不同步、延迟销毁不执行（死单位堆积）、ResetPools 不重置
```

**注意：** `Resources.Load<DroppableTool>("ToolNinja")` 按名字找不到子目录资源，必须用 `Resources.LoadAll<DroppableTool>("")` 按 name 匹配（`LoadDroppableTool` 模式）。

**影响范围：** 所有跨生物群系的 droppable/角色产出——ToolNinja、ToolBerserker、Ninja 角色、Berserker 角色。

### 8. Resources.Load 找不到子目录资源

`Resources.Load<T>("名字")` 只搜 Resources 根目录。反编译游戏的 prefab 常放在子目录，`Load` 返回 null 但 `LoadAll<T>("")` 能扫到。**统一用 `LoadAll` + 按 `gameObject.name` 匹配**（缓存结果避免重复扫）。

### 9. 旧存档残留商店占槽位

槽位复用后，旧存档可能已在目标槽位摆了错误内容（如盾牌商店占 12/13）。入队前校验 `_placedShops[type]` 的 `itemPrefab.tag`，不对就 `RemoveShop` + `DestroyImmediate` 再入队。`DestroyImmediate` 避免同帧 Find 到"僵尸"对象重复克隆。

### 10. 克隆商店方案已废弃（记录原因）

曾经用"克隆 Bow 商店 + 换 itemPrefab"做狂战士商店，最终废弃，原因：
- 克隆的 `PayableShop.Awake` 同步执行，`AddShop → SetPlacedShop` 覆盖原商店槽位
- 克隆带 `CRPCStamp` 等网络组件，`NetIDCacheHack` 注册冲突（`Attempted to register ... with NetID 1270`）
- 无法存档持久化（存档按 `Persistent.path` 走 `Resources.Load` 恢复，克隆不在 Resources）
- 外观是 Bow 帐篷，不像狂战士商店

**正确方案：槽位复用 + Awake 身份改写**（见 shop-system.md），全部走原生队列。

### 11. 单位缩放不能只设一次（y 轴守护）

**症状：** 出生时设置 `localScale=(1.3,1.3,1)`，下一帧被清回 `(1,1,1)`，"缩放不生效"。

**根因：** `Mover.Update` 每帧用 `localScale.x` 符号（±1）做朝向翻转，整值覆盖为 `(±1,1,1)`——y 被写死 1。

**解法：** `UnitScaleRegistry`（ConditionalWeakTable）登记目标 y，`Mover.Update` postfix 每帧恢复 y。
x 绝不能动（朝向 + `Mover.cs:405 velocity.x *= localScale.x` 速度依赖）。详见 architecture.md / domain-model.md D3/D4。

### 12. Worker/Peasant 没有 Start 方法

**症状：** `GetMethod("Start", NonPublic)` 返回 null，patch 静默失败。

**根因：** Worker 只有 `Awake` + `OnEnable`（无 Start）；Peasant 只有 `Awake` + `OnEnable`。
对象池游戏 `Pool.Spawn` 复用走 `SetActive(true)`，只有 **OnEnable** 每次出生都触发。

**规则：** 单位生命周期 hook 一律挂 OnEnable。详见 domain-model.md D6。

---

## 当前 mod 功能清单

| Patch 类 | 功能 | 状态 |
|----------|------|------|
| Patch_ShopPlanner | InitializeShopTypePrefabPairs Prefix 全量替换：跨生物群系注册商店 prefab + 希腊狂战士槽位（12/13） | ✅ |
| Patch_Holder | 跨生物群系注册角色 prefab + 希腊 Worker/Peasant → 北境替换 | ✅ |
| Patch_Castle | 希腊忍者商店 + 狂战士商店原生刷新 + sync 池注册 | ✅ |
| Patch_SidedShop | PayableSidedShop.Awake 身份改写（狂战士→ShieldShop 槽位） | ✅ |
| Patch_Kingdom | 地图扩展（幂等，基准值缓存）+ 猫生成 | ✅（狂战士/忍者 hack 已退役注释） |
| Patch_Worker | 工匠缩放（y 守护 1.175/1.075）+ 北境工匠出生带盾 | ✅ |
| Patch_WorkerScale | 单位缩放统一：OnEnable 登记 + Mover.Update y 守护 | ✅ |
| Patch_Mover | 玩家移动速度倍率（SetGoal/SetGoalSpeed/SetGoalNoHaglet 入口乘倍率，仅 Player） | ✅ |
| Patch_Construction | 快速建造 | ✅ |
| Patch_EnemyManager | 怪物数量/时间线倍率（AddEnemies/GetEnemies Prefix） | ✅ |
| Patch_Knight | 狂战士跟随骑士 | ✅ |
| Patch_Banker | 银行家去重 + 残留清理 + 共享银行增强（补员已删除） | ✅ |
| Patch_FriendlyTroll | ~~transpiler~~ | 🗑️ 已禁用（Harmony 1.2 transpiler 崩溃） |
| Patch_Character | 乞丐→北境 WarriorPeasant 替换（Promote_Prefix） | ✅ |
| Patch_World | 希腊草地补充（ExpandGrass） | ✅ |
| Patch_PoolManager | 原生池重建 + mod sync 池重注册 | ✅ |
| Patch_BeggarCamp | 乞丐生成间隔 90 秒（spawnInterval=209f） | ✅ |
| Patch_Artemis | 单发箭伤害 20 次（_maxHitsPerArrow=0f） | ✅ |
| Patch_HermesStaff | 权杖控制 16（_maximumConvertedTrolls 8→16） | ✅ |

> 状态与 `docs/project-harness/harness-checklist.json` 同步维护。
