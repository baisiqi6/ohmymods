# IL2CPP 迁移坑点沉淀（2026-08-13 实机踩坑记录）

> 所有坑都是 Steam 2.4.0 IL2CPP 实机验证 + IL 反汇编确认过的，**不是猜测**。
> 新增坑点持续追加。每个坑：现象 → 根因 → 解法 → 状态。

## 1. Cpp2IL 生成 thunk 方法 HarmonyX 挂不上
- **现象**：`PoolManager.OnLevelLoaded` 的 Harmony prefix 从未执行（诊断日志 0）。
- **根因**：interop 壳里的 OnLevelLoaded 是 Cpp2IL 生成的 thunk（`il2cpp_runtime_invoke` 转发到原生 `NativeMethodInfoPtr_*`），游戏原生虚调用不经过托管 thunk。
- **解法**：挂**真实被调方法**——池初始化改挂 `PoolManager.Init`（每场景必调，验证 ENTERED=1）。✅
- **通用规则**：IL2CPP 下先验证 patch 是否真执行（诊断日志），不执行就查 IL 里方法是否 thunk。

## 2. IL2CPP 反射游戏私有静态字典不可靠
- **现象**：反射拿 `Pool.poolsByPrefab` 做 ContainsKey 判断，与原生行为不一致（池缺失时兜底不触发）。
- **根因**：il2cpp 原生字典与托管包装的快照不同步。
- **解法**：不查字典，**行为侧修补**——直接 try CreatePoolFor + 实例 ID 防重（PatchPoolFix）。✅

## 3. Font.CreateDynamicFontFromOSFont 被 IL2CPP 裁剪
- **现象**：`NotSupportedException: Method unstripping failed`（面板 OnGUI 崩 + GUILayout 布局损坏）。
- **解法**：`Resources.LoadAll<Font>("")` 加载游戏内置中文字体（Zpix），找不到用默认。✅

## 4. get_type_members.py 正则漏报 unsafe 方法
- **现象**：Mover "Update 丢失"误报（实际无漂移），PoolManager 成员查询空结果。
- **根因**：Cpp2IL 桩方法全带 `unsafe` 关键字，脚本正则不匹配。
- **解法**：签名核对一律用 `ilspycmd -t` / `-il` 原始输出。⚠️ 脚本待修或弃用。

## 5. 读档不触发新游戏开局流程
- **现象**：`OnGameStartHandler`/`ChangeCurrencyBag` 在新游戏才触发——**读档恢复的钱袋不扩容**（"钱袋变大没生效"）。
- **解法**：功能必须挂在读档也走的点（如 `CurrencyBag.Awake`）。⚠️ 钱袋 Awake 仍未触发（见 issue #1），深挖中。

## 6. IL2CPP 默认参数生成完整重载
- **现象**：`Pool.SpawnGO` 的 Harmony patch 静默失效。
- **根因**：C# 默认参数在 IL2CPP 编译成完整 7 参方法，nameof 匹配歧义。
- **解法**：`[HarmonyPatch]` 显式列出全部参数类型。✅

## 7. Il2CppObjectBaseToPtrNotNull 抛 = il2cpp 对象已销毁/过期
- **现象**：`ShopPlanner.QueueNewShopForPlacement` 调用抛（城堡升级时序）。
- **解法**：降级 Warning + 依赖调用方重试（CatchupToLevel 多次触发）。✅

## 8. BepInEx 6 IL2CPP 首启生成 interop，二次启动才加载插件
- **现象**：首启插件不加载、日志 0 字节。
- **解法**：首次启动等 interop 生成完（几分钟）→ 关掉 → 二次启动。安装说明已注明。✅

## 9. 多副本共享存档目录，版本互相升级
- **现象**：Steam 2.4.0 把 `Release\global-v35` 升级到 v16 → Mono 2.1.0（上限 13）读不了。
- **解法**：mod 侧版本容忍（JsonUtility.FromJsonOverwrite prefix 16→13）+ 测试前备份纪律。✅

## 10. UMM StartingPoint 挂 Managers.Awake 的死锁
- **现象**：存档版本警告界面先于 Managers.Awake → mod 永不加载 → 容忍 patch 永不生效 → 卡死。
- **解法**：StartingPoint 提前到 `ProgramDirector.Run:Before`。✅

## 11. 幂等机制必须核对反编译，不能假设
- **现象**：假设 "Dictionary.Add 重复键抛异常" 判池已存在——实际 2.1.0 `Pool.Init` 用 **ContainsKey guard 不抛** → 重复调用静默建孤儿池。
- **解法**：实例 ID 集合防重（Mono/IL2CPP 双端）。✅

## 12. 共享存档/独立副本的存档污染
- **现象**：独立副本测试保存会覆盖主档（单存档设计）。
- **解法**：测试前备份（global-v35.pre-test 惯例）。✅

## 13. 类级 [HarmonyPatch] 缺失 → PatchAll 静默跳过整个类
- **现象**：PatchEconomy_CurrencyBag/Banker/Shops 三个类的所有 patch 从未执行（[Economy] 日志 0），无任何报错。
- **根因**：本 0Harmony 版本的 PatchClassProcessor 只读类级注解（`if (!allowUnannotatedType && fromType==null) return;`），方法级注解不足够——无类级注解的类被静默跳过（反编译 0Harmony.dll 实证）。
- **解法**：每个 patch 类必须带类级 `[HarmonyPatch(typeof(X))]`（方法注解显式类型时不依赖类级类型，但类级是"容器标记"必需）。✅ 已修（2026-08-13，worker BagInvestigator 反编译定位）。
- **教训**：patch 不生效时先查类级注解，再怀疑 IL2CPP 机制。

## 14. TotalCapacity "死字段"是 xref 扫描的假象（已纠正，EconomyFixReviewer 终审）
- **误判**：曾以"get/set xref 调用者=0、158k 方法全量扫描"判定 2.4.0 TotalCapacity 是遗留字段。
- **纠正**：interop 壳方法全是 il2cpp_runtime_invoke thunk（无成员引用），原生字段读取是直接内存访问——**xref 扫描结构上永远看不到字段读取**。仓库 2.0.1/2.1.0 源码实证 TotalCapacity 是活的容量杠杆：Wallet.SetCurrency `Mathf.Clamp(value, 0, TotalCapacity)`、CanGrabCurrency/SuckCurrency 门控拾取；2.4.0 interop Wallet API 与 2.1.0 1:1 相同。**赋值 TotalCapacity=2000 即正确扩容**。
- **注意**："2.4.0 钱包重做为 CurrencyMap"也是误判——CurrencyMap 2.1.0 就存在。
- **验证方式**：实机把袋装满超 2000，确认拾取在 2000 停止。

## 15. 跨 biome 迁移角色时必须迁移传递依赖池
- **现象**：希腊忍者能成功转职并开始战斗，但数次攻击后停止；怪物不再攻击它，天亮也不恢复钓鱼形态。`Player.log` 同时出现 `Pool not found for ThrowingStar` / `Ninja.ThrowStar()` NRE，以及 `Pool not found for Smokebomb` / `Ninja.SmokebombRoutine()` NRE。
- **根因**：只注册了 `ToolNinja` 与 `char:Ninja` 主池，漏掉 Ninja prefab 字段引用的投射物和视觉对象。烟雾 NRE 发生在死亡分支已经 `Stop()`、`damagedBy=0` 之后，却早于恢复受伤状态和 `Character.Demote()`，因此整个 `Ninja.Behaviour` 协程中断并留下无敌僵尸状态。
- **原始资源证据**：2.4.0 `Object Pools/bamboo` 中 `ThrowingStar` 为 `sync=true, syncID=41`；`Smokebomb` 为 `sync=false`。希腊原生池未占用 41。烟雾由动画 RPC 在各端本地生成，禁止错误注册为 sync 池。
- **解法**：从有效 Ninja prefab 的 `arrowPrefab` / `smokebombPrefab` 取得真实资产，在 Holder 稳定初始化以及每次强制 `InitPools()` 后按固定顺序预注册；飞镖进入 sync 缓存，烟雾只进入普通池/name 缓存。不要依赖攻击热路径临时建池，也不要通过 `allowInstantiate=true` 绕开池生命周期。
- **通用规则**：跨 biome 单位的依赖闭包不止“角色+工具”；还要审计 projectile、VFX、掉落物、召唤物和动画事件触发对象，并逐项复刻原池的 sync/local 语义。⚠️ 代码已实现并静态构建通过，仍待独立副本实机验证。

## 待办 issue
- **#1 钱袋 Awake 不触发**：✅ 已解决（坑 #13 类级注解）。经济域全部激活（2026-08-13）。
- **#2 容量上限验证**：TotalCapacity=2000 应为正确杠杆（坑 #14 已纠正）——实机装满超 2000 验证拾取停在 2000。
