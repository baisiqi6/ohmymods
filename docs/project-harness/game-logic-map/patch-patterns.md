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

### 13. 2.1.0 Worker.OnTriggerEnter2D 的 npcShieldUser 早退（狂战士商店卡死）

**症状：** 希腊世界狂战士商店旁工匠卡住、工具滞留、商店锁死买不了。

**根因：** 2.1.0 的 `Worker.OnTriggerEnter2D` 新增 `this.npcShieldUser == null` 早退；
希腊原版 Worker prefab 无 NpcShieldUser 组件（Awake 里 TryGetComponent 不创建）→
无法拾取 BerserkerTool。原版希腊没有狂战士商店所以从未暴露，mod 带进希腊即触发。

**解法：** `Patch_Worker.EnsurePickupCapability`——OnEnable 时给无 NpcShieldUser 的工人
补组件 + 反射回填 Worker.npcShieldUser 字段（Awake 已缓存，只 AddComponent 不够）。

### 14. 2.1.0 注入必须用 BepInEx 5 doorstop

UMM 21.0.32 自带 winhttp（旧 UnityDoorstop）不识别 Unity 2022.3.51f1，静默放弃。
用 BepInEx 5.4.23.3 的 winhttp.dll（x86）+ `[General] target_assembly=` 格式
doorstop_config.ini 指向 UnityModManager.dll（详见 runbook "注入方案"）。

### 15. IL2CPP 空 Nullable 字段的生成 getter 可能先于 HasValue 崩溃

**症状：** 读取 `ShopPlanner.ShopPlaceQueueData.shopSide` 时，在业务代码执行
`HasValue` 之前就从生成 getter 抛出 `NullReferenceException`；堆栈位于
`Il2CppSystem.Nullable<T>(IntPtr)` / `CreateGCHandle`。

**根因：** interop getter 会先把 native 内嵌的 `Nullable<Side>` value-box 成对象。旧存档中
`shopSide` 为空时，该包装路径可能返回空指针并在构造 wrapper 时直接崩溃，不能用
`field == null || !field.HasValue` 安全探测。

**规则：** 当 `shopType` 已能唯一推导 side 时，不读取旧 nullable getter，直接通过 setter 写入
`new Il2CppSystem.Nullable<Side>(expected)`。setter 会 unbox/cpblk 覆盖 native 内嵌字段。
另外，`ShopPlanner.Start` 只初始化 prefab 映射，`coreRoutine` 在 `Init` 创建；禁止在 Start postfix
提前调用 `TriggerShopPlanning`。旧队列交给原生 `OnLevelLoaded` 触发，新入队由
`QueueNewShopForPlacement` 自己触发。

### 16. 静态游戏字体不能直接注入新版 IMGUI/TextCore

**症状：** 打开 mod IMGUI 面板后，`Player.log` 每帧重复输出
`Unable to find a font file ... [Zpix]`、`Unable to load font face ...`，并沿
`FontAssetFactory.ConvertFontToFontAsset -> IMGUITextHandle -> GUILayout` 形成巨量堆栈。

**根因：** 游戏内置 `Zpix` 是静态像素字体，未包含 TextCore 动态生成字体面所需的数据。
将它写入 `GUISkin.font` 后，新版 Unity 会在每次 Repaint 重试转换。为寻找该字体而调用
`Resources.LoadAll<Font>("")` 还会强制扫描全部资源，并暴露无关的缺脚本资源警告。

**规则：** IL2CPP IMGUI 默认复用 Unity 已创建的 `GUI.skin`；禁止全量扫描 Font、禁止把
静态游戏字体写入 skin，也不调用当前二进制已 stripped 的
`Font.CreateDynamicFontFromOSFont`。如果以后必须完整显示中文，应随 mod 提供许可允许再分发、
包含字体数据且经当前 Unity 版本验证的动态字体资源；在此之前优先保证面板可操作和日志安静。

### 17. IL2CPP 私有 helper 的内部调用可能绕过 Harmony thunk

**症状：** patch 编译、加载均无报错，目标方法在反编译和 interop 中也存在，但实机业务发生多次后
Prefix/Postfix 日志始终为 0，功能整体 no-op。狂战士序列曾挂私有
`Worker.TryPickupBerserkerTool`，普通/队长池均正常却永远不计数。

**根因：** IL2CPP 原生方法之间的内部调用不保证经过 interop runtime-invoke wrapper/Harmony 可替换
入口；私有 helper 尤其容易由原生直接调用或内联。方法“能反射、能 patch”不等于 native caller
会穿过该 thunk。

**规则：** 优先挂已经有实机命中证据的公开稳定边界，并在入口用对象类型、工具状态、tag 与结果
identity 收窄语义。狂战士序列改挂 `Character.Promote(DroppableTool,IUnitController)`：要求 active
Worker、active 且未拾取的普通 BerserkerTool，只有返回角色 tag/prefab 匹配后才提交计数。
所有新私有方法 hook 必须有至少一条一次性 entered 日志作为运行门禁；日志为 0 时先判 thunk 未命中，
不要继续调业务条件。

### 18. 跨 biome 功能必须审计完整对象池依赖链

**常见误判：** 角色本体或转职工具能生成，就认为跨世界迁移已经完成。真正的故障往往到首次攻击、
释放技能、死亡/撤退、召唤，或 `PoolManager` 重建对象池后才出现。忍者曾经能正常生成，却因
飞镖与烟雾弹池漏注册而在战斗中抛异常、卡死状态机。

**固定依赖链：**

1. 角色本体。
2. 转职或招募工具。
3. 攻击投射物。
4. 技能特效。
5. 死亡、撤退或变身特效。
6. 召唤物与子单位。
7. 主客机同步池：双方必须注册同一 prefab，并使用确定且不冲突的 syncID。
8. 每次 `PoolManager.InitPools`、换岛、读档或池重置后的幂等重注册。

**注册规则：**

- 网络同步对象必须进入同步池和必要缓存；host/client 按确定顺序注册，syncID 冲突时 fail closed，
  不覆盖无关 prefab。
- 纯本地视觉特效只进入本地池与名称缓存，不进入同步映射，避免客户端重复生成效果。
- 先注册依赖，再注册消费者：投射物、技能特效、召唤物应先于使用它们的角色。
- `Resources.LoadAll` 只允许出现在初始化或缓存预热阶段，禁止放进拾取、攻击等热路径。
- 池对象复用时在 `OnEnable` 恢复可变状态；需要发 RPC 的逻辑必须等待网络对象完成注册。

**验收标准：** “成功出生”不算完成。至少要实测攻击与投射物、技能、死亡/撤退、召唤、
despawn→reuse、换岛/读档、对象池重新初始化，以及适用时的主客机追赶同步；日志中不得出现
`Pool not found`、重复 syncID、相关 NRE 或 RPC-before-spawn。

### 19. 改全局候选集合时必须使用公开作用域和双重恢复

**适用场景：** 原生状态机内部直接枚举共享集合，但需要只对某一类调用临时隐藏或注入候选。只 patch
私有校验 helper 可能被 IL2CPP native caller 绕过，也可能在“已选出最近目标”之后才拒绝，造成重复
选择同一无效目标。

**规则：** 优先选择有真实 native 入口的公开方法，用对象或 FSM/delegate 指针 O(1) 收窄到目标调用；
Prefix 只记录本次实际修改的条目，Postfix 负责正常恢复，Finalizer 负责异常兜底，恢复必须幂等且逐项
try/continue。不得把临时条目永久登记进全局缓存，也不得用每帧 `FindObjectsOfType` 重新扫描全场。

友好巨魔采用该模式：在其公开 StateMachine step 内临时移除 active Squid；反制 TrollWeak 则只在其
绑定的 TargetCacher delegate 查询中临时加入 active FriendlyTroll。两处都需要一次性 canary 日志证明
IL2CPP 实机命中，编译成功和 interop 中存在签名不能代替运行证据。

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
| PatchDivine_FriendlyTroll | 候选阶段只排除 Squid + 约10% TrollWeak 反制友好巨魔 | 🧪 静态通过，待实机 |

> 状态与 `docs/project-harness/harness-checklist.json` 同步维护。
