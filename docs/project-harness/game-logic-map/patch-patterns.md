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

### 20. 跨岛所有权状态不能把 active、standby 与 carryForward 相加

**症状：** 死亡换君主或航行载入后，任务仍显示奖励已领取，但场景实例、standby 和 carryForward
全部为零；奖励入口又不可重复触发，所有权永久丢失。

**原生语义：** `PopulateCarryForward` 有 active 时只保存 active 数，否则才保存 standby；
`ApplyCarryForward` 在 riverless 场景写 standby，在可生成场景把 carry 物化为 active，随后清空 carry。
三者是同一批所有权的阶段性替代表示，不是可以求和的三份库存。

**规则：** 在 `ApplyToScene` Prefix 只捕获 present carry 作为 desired 下限，Postfix 等原生物化完成后
按 `active>0 ? active : standby` 选择唯一 materialized 来源。standby 非零时不同时生成 active；active
非零时不同时写 standby。部分生成失败只保留已成功 active、下次场景应用继续补；首个生成失败且仍无
active 时才整批回退 standby。恢复来源必须是已经完成且原生确实发放奖励的任务，不能重放领奖动画或
直接改写存档。网络对象只复用当前 biome 已注册的同步池，并由 world-authority 生成。

### 21. 完成态专精建筑不能在实例注册后临时追加付款组件

**症状：** 普通建筑能通过 `PayableUpgrade` 继续升级，但完成后的专精建筑没有任何下一步交互；直接
`SetNextPrefab` 无入口，运行时 `AddComponent<PayableUpgrade>` 又可能造成主客 RPC 索引、序列化组件列表
和旧存档 componentData 不一致。

**原生语义：** `PayableUpgrade` 同时实现 `IRPCable` 与 `Persistent.IBehaviour`。它的组件顺序必须在
`CRPCHeader.RegisterComponents` 枚举前确定；`Pay()` 还负责预留 next NetID、biome swap、乘客消耗、
`IUpgradeable` 迁移与旧根销毁，不能用裸 `Instantiate/Destroy` 替代。

**规则：** 若确需为完成态建筑补交互，只能在双方确定性的 prefab 初始化阶段、任何 pool/CRPC 注册之前
加入同一个原生组件，并在 Mod Disabled 时仍保留组件布局、只关闭交互。目标和价格优先从当前原生升级图
读取；源建筑的驻员、库存、投射物与旁挂商店必须有逐类型 teardown 证明，未证明的类型 fail closed。
新增 Persistent 组件会让已增强存档携带额外 componentData，完全卸载 Mod 后可能被原版忽略并记错误，
必须显式写入兼容边界并做保存往返门禁。

### 22. 动态扩展 Formation 必须把类型、成员和间距作为一个事务

**症状：** 只复制`FleetBoat`类型槽会因原始间距为0而让多艘船重叠；只改`unitTypes`却不同时创建
等长`units`会破坏注册/离队索引。留下空FleetBoat槽还可能被原生周期招募补入错误一侧的小船。

**规则：** 首次按Formation实例捕获原始`unitTypes`和`UnitSpacing`；在inactive且units全空时一次性构造
新的等长类型/成员数组，间距只改克隆。显式招募结束后立即把所有未占用预留槽改Gap；单位离队时优先在
公开`UnregisterUnit`返回后即时封槽，并用小范围低频协调器兜底。只有原生OnDisable完成全量离队且units
全空后才能恢复baseline，异常路径不得覆盖已成功注册的成员。网络权威端负责招募，客户端是否需要同构
阵列必须由目标单位原生客户端生命周期决定，不能一概假设双方本地Formation都运行。

### 23. 可失败的旧对象清理必须发生在付款提交前

**症状：** 在`PayableUpgrade.Pay` Prefix才回收旧建筑投射物时，离线失败已经晚于金币提交；在线更可能
发生Pay RPC已批准、authority取消而client继续替换的主客分叉。

**规则：** 仅在原生最终`CanPay=true`之后、`TransactionComplete`之前做可失败准备；失败把CanPay结果改为
false，让原生Cancel与DropFloatingCurrency退款。成功准备生成绑定payable/player/world/scene/frame的一次性
token，Pay阶段只消费token，不再执行可失败动作。若现有RPC流程没有“批准前双方事务”入口，在线功能必须
整体fail closed，不能用Pay Prefix单端取消。池回收异常还要区分“对象仍活动且仍属原池”与部分回收：前者
才可恢复引用，后者必须归一到原生可重新装填状态。

### 24. 跨 biome 资产补丁不能依赖单点 GetAssetSwap 解析

**症状：** 特种箭塔重建补丁在 `PoolManager.Init` 前缀里按
`Tower6模板.passengerUpgrades route → BiomeData.GetAssetSwap` 解析源 prefab，实机日志
`Ready source=Tower Ballista` 证明拿到的是**基座资产**；但存档（gzip 解压）里已建弩箭塔的
prefabPath 是 `Prefabs/Buildings and Interactive/greece/Tower Ballista_greece`。组件加错资产 →
场上实例无 marker/PayableUpgrade → `CanSelect` 从不被调用 → 无交互且无任何 Blocked 诊断日志
（PayableManager 只对已注册 payable 调 CanSelect，静默失败）。

**原生语义：** `BiomeData.GetAssetSwap` 经 `LoadedBiome.swapData` 显式查表
（`BiomeSwapData._prefabSwapDictionary`，按对象引用匹配），早期时点（PoolManager.Init）可能
未生效或基座模板本就引用基座资产；而真实建造走"被升级塔自己的 payable route + 付款时 swap"、
存档恢复走 `IslandSaveData.TryCreateOrFind` 直接按保存的 prefabPath 实例化，两者都会落到
`_greece` 变体。池路径 `FastSpawn→FastClone→Instantiate(this._prefab)` 是惰性克隆，prefab
配好组件即可遗传给恢复实例。

**规则：** 需要把组件配置到"会被实际实例化的资产"时，用 `Resources.LoadAll<T>("")` 按组件类型
+ 名字模式扫描全部候选（如所有含 `Ballista` 组件且名含 "Tower Ballista" 的资产），对每个通过
安全检查的候选幂等配置，`GetAssetSwap` 结果只作为候选之一且 try/catch 包裹；绝不能假设单点
swap 在任意早期钩子都已就绪。Ready 日志必须列出全部已配置源名，被安全检查跳过的候选单独汇总
输出——下次日志即可直接验证配置是否命中真实资产。存档 prefabPath（gzip 解压 grep）是判断
"实例到底来自哪个资产"的最终证据。

### 25. 自定义 MonoBehaviour 的 ClassInjector 注册必须先于一切类型接触点

**症状（crossbowman-021，reviewer 拦截）：** 标记组件只在 `AddComponent` 前做了
`ClassInjector.RegisterTypeInIl2Cpp`，但**未注册时 `GetComponent<T>()` 与
`FindObjectsOfType<T>()` 同样抛异常**。读档重算协程（进程内最早的 marker 接触点，
此时还没发生过任何弓转职）在循环里 `GetComponent<CrossbowmanMarker>` 直接炸，
被 try/catch 吞掉后**整个读档重算静默中止**——玩家侧唯一症状是弩手在读档后消失。

**规则：** 自定义类型的注册函数（`IsTypeRegisteredInIl2Cpp` + `RegisterTypeInIl2Cpp`
幂等短路）必须在**每一个**类型接触点之前调用（含防御性重复调用，幂等零成本），
不能只挂在 `AddComponent` 前。"AddComponent 会自动注册"是错误认知——本仓库 9/9
自定义 MonoBehaviour 先例（ModPanel、各 Coordinator、Marker）全部显式注册。
坑 17（私有 helper 绕过 Harmony thunk）的姊妹坑：IL2CPP interop 对未注册托管类型
的一切泛型 API 都不宽容。

**附带沉淀（同任务）：** `ArrowAttack` 是全体弓箭手共享的 ScriptableObject，
改数值必须克隆（`Object.Instantiate` + DontDestroyOnLoad）；`Range = _shotMagnitude²/-_arrowGravity`
（Util.GetProjectileRange），初速/重力/射程三者不独立；实际索敌距离由
`Archer.shootRange`（扫描器）与 SO Range 双重门控，弹道观感与交战距离可以解耦设计
（弩手：v×1.5/g×0.5 平直快弹 + shootRange=12 硬约束）。私有方法（如 `HasKnight()`）
不进 interop，但等价的私有字段（`_knight`）直接可读。


### 26. Il2Cpp HashSet 枚举器运行时不可靠——用反查字段替代集合遍历

**症状（knightstyle1/2 两轮实锤）：** 遍历 `knight._archers`（Il2Cpp HashSet）先在循环体内写控制器抛
"Collection was modified"，改纯读快照后 **MoveNext 本身就抛**——非泛型枚举器路径对该集合类型运行时
不可靠，与用法无关。且异常被日志去重吞掉后每轮静默失败，表象是"功能整体没生效"。

**规则：** 需要"集合里都有谁"时，优先**反向归属**——扫描全场目标类型（FindObjectsOfType）+ 读
对方的回引字段（如 `archer._knight`）判归属；不枚举原生集合。回引私有字段 interop 可靠（坑25 同族）。

### 27. 泛型游戏结构体经 interop marshal 可能读出垃圾值——用原生公式复刻

**症状：** `world.worldBounds.right` 读出 4.7e19（Sided<float> 泛型结构体封送损坏），上界钳制静默失效。

**规则：** 对泛型游戏结构体属性，静态无法判读数是否可靠；关键数值改**复刻原生计算公式**从
基础组件取（如 worldBounds.right = ground.transform.position.x + collider.size.x/2 − 8，
World.cs OnLevelLoaded 同式），并在日志里输出实测值对账。

### 28. 与原生系统的对抗要用"目标"不要用"位置"，且恢复碰撞必然爆炸

**症状（三轮教训）：** ①瞬移钳位 vs 推挤系统 = "弹回又挤出"拉锯；②定时恢复友军碰撞 = 深穿插对被
物理按重叠深度弹飞上天；③条件式恢复（探测无人重叠才开）仍是给恢复动作叠补丁。

**规则：** 给单位下发**它自己会走过去的目标**（SetGoal），永不如物理/推挤系统硬碰位置写
transform。友军密度问题根治=关碰撞就别恢复（恢复瞬间必有无解的穿插对）；代价是视觉互穿，
用站位分配类机制补散开（见坑28附：白天拥挤按原生狩猎公式重掷）。

### 29. 私有 Unity 消息 patch 前必须反射确认该类声明了它

**症状：** 给 `SteedAbility` 挂 "OnEnable" 补丁——反射核实该类 **DeclaredOnly 里没有 OnEnable**
（只有 Awake/OnDisable/OnDestroy）。硬挂会让 `harmony.PatchAll` 抛异常、**整个 mod 加载失败**。

**规则：** 字符串名补丁私有 Unity 消息前，反射 DeclaredOnly 方法表确认存在（Dog/Banker 有 OnEnable
不代表基类有）；不存在时改挂消费点（如 SteedAbility.Activate 读取点前缀）。PatchAll 是全有或全无。

### 30. 墙后射击的平直弹道被自家墙挡——出膛点前移解 ParabolaCast

**症状：** 弩手平直弹道参数怎么调都是高抛——原生 BestShot 的 ParabolaCast 发现直线路径被自家
城墙挡住会主动选高抛解。

**规则：** 射击单位在掩体后时，平直弹道的钥匙是 SO 的 `_arrowOriginOffset`（Vector2 序列化字段，
按目标方向侧移）——出膛点前移过掩体沿，低弹道解自然被选中；配合初速放大（射程包络>索敌距离）
得到"近距平直快弹"。

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
| PatchWorld_FleetBoatRecovery | 死亡换君主后按四个神像交付任务幂等恢复小船所有权 | 🧪 静态通过，待实机 |
| PatchRoles_KnightStyle | 骑士随机五风格（+北境）确定性哈希+随从联动翻牌治理+体型表+死地随从弩手化 | 🧪 3.2.0-dev2 待实机 |
| PatchRoles_NorseSquad | 北境小队：随从窗口技巧转真北境prefab+程序化装盾+读档巡检兜底 | 🧪 3.2.0-dev2 待实机 |
| PatchRide_SteedCooldown | 坐骑技能CD倍率（Activate读取点前缀，实例缓存原生值幂等） | 🧪 待实机 |
| PatchRoles_Crossbowman | 弩手：每第4个弓转职换皮强化（deadlands皮肤/射程12/冷却×2/伤害×2/平直弩矢/骑士排除/读档25%重算） | 🧪 编译+review通过，待实机 |

> 状态与 `docs/project-harness/harness-checklist.json` 同步维护。
