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

## 14. 2.4.0 Wallet.TotalCapacity 是遗留字段（托管层无读取方）
- **现象**：设 TotalCapacity=2000 后容量可能仍不生效（get/set xref 调用者=0，158k 方法全量扫描；2.1.0 的 Archer/Banker/Knight 读取方在 2.4.0 全部消失）。
- **状态**：⚠️ 容量上限机制待深挖（SetCurrency clamp / CurrencyConfig / OVERFLOW_LIMIT），实机验证 2000 金币能否装下。
- **注意**：2.4.0 读档也走 OnGameStartHandler→ChangeCurrencyBag（与 2.1.0 假设不同），但该时机 wallet 可能未就绪——CurrencyBag.Awake 补容量是实际有效路径。

## 待办 issue
- **#1 钱袋 Awake 不触发**：✅ 已解决（坑 #13 类级注解）。经济域全部激活（2026-08-13）。
- **#2 TotalCapacity 死字段**：容量上限机制待深挖 + 实机验证（坑 #14）。
