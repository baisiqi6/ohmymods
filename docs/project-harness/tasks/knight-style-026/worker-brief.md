# worker 任务书：knight-style-026 骑士随机风格（四风格+随从联动+希腊0.9）

## 身份与边界

- 你是本仓库的 worker（subagent 实现）。仓库 `C:/Users/ADMIN/projects/ohmymods`（IL2CPP 主线，
  BepInEx 6 + HarmonyX + Il2CppInterop，.NET 8，游戏 2.4.0，无反编译——以
  `game-source/Assembly-CSharp-2.1.0/` 为逻辑参考 + interop 暴露成员为签名事实）。
- **只允许新建一个文件**：`il2cpp/PatchRoles_KnightStyle.cs`。禁止修改其他任何文件、
  commit、push、部署、运行游戏。
- 补丁注册是全程序集 PatchAll（KingdomEnhancedPlugin.cs:60），[HarmonyPatch] 即生效。
- 日志前缀 `[KnightStyle]`，用 `KingdomEnhancedPlugin.Instance?.LogSource`。
- 整体受 `ModConfig.Enabled.Value` 门控。代码注释中文，解释"为什么/约束"。

## 编译验收命令（唯一允许的执行命令，在 il2cpp/ 目录）

```
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=NONE
```
0 error 才算过；构建产生的 il2cpp/NONEKingdomEnhancedMod/ 目录验证后删除。

## 需求（用户原话转写）

招募骑士时，大小骑士随机为中世纪/死亡之地/幕府时代/希腊四种形象之一；
它们的随从士兵（跟随骑士的弓箭手）也换成对应形象；希腊风格骑士 y 缩放 0.9。

## 已侦查实锤（直接采用，不要重新侦查）

1. **转职入口**：`Character.Promote(DroppableTool, IUnitController)` postfix，
   `tool.tag == "Armor"` → `Professions {"Armor","Knight"}`（Character.cs:885）。
   结果 `__result.GetComponent<Knight>()` 即新骑士。同签名多 postfix 先例：
   PatchRoles_Worker.cs（alternate hammer）与 PatchRoles_Crossbowman.cs。
2. **四套骑士动画控制器**（2.4.0 resources.assets 实测存在）：
   中世纪=基底 `knight`；死地=`knight_deadlands`；幕府=`knight_bamboo`；希腊=`knight_greece`。
   （`knight_norselands` 也在但用户四选不含北境。）
3. **四套士兵控制器**（随从用，2.4.0 资产实测存在）：`archer_soldier`（基底中世纪）、
   `archer_soldier_deadlands`、`archer_soldier_bamboo`、`archer_soldier_greece`。
   注意：随从跟随骑士时原生 ConvertToSoldier（Archer.cs:859）已把它们换成当前世界的
   士兵控制器，本任务改为覆盖成"骑士风格对应"的士兵控制器；离队时原生
   ConvertToHunter（Archer.cs:881）自动恢复猎人外观，无需我们清理随从。
4. **Knight 字段**（interop 暴露）：`_animator`（私有，root 上）、
   `_archers`（HashSet<Archer>，随从集合，foreach 遍历用
   `System.Collections.Generic.List<Archer>` 拷贝或 il2cpp 枚举注意——参考
   PatchDivine_FriendlyTroll 遍历 Il2Cpp 集合的写法，或 foreach 直接可用则用）。
5. **缩放基建**：`ScaleRegistryHolder.Register(Mover, float y)` / `Unregister(Mover)`
   （PatchRoles_Worker.cs:56-67，按 gameObject.GetInstanceID() 键控，Mover.Update
   postfix 每帧守卫 y）。**只动 y，x 是朝向符号（坑11）**。
6. **确定性风格判定范式**：PatchDivine_FriendlyTroll.cs 的 TryComputeDesignation
   （FNV 哈希 mix campaign/challenge/land/reign + 网络身份 → uint）——照抄该模式，
   结果 `% 4` 映射风格。确定性哈希的价值：读档/联机双端算出同一风格（外观级一致，
   无需网络同步）。身份源用骑士的 NetworkHeader NetID（FriendlyTroll 里找 Troll 的
   NetworkHeader 的写法可直接参考；骑士身上应有同型组件——找不到就退化为
   `GetInstanceID()` 并注释联机读档后可能换风格，可接受）。
7. **协程宿主范式**：`[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]`
   postfix → `world.StartCoroutine(Routine(world).WrapToIl2Cpp())`，
   per-world `IntPtr` 指针守卫（抄 PatchWorld_DefenseSpacing.cs / PatchWorld_SerpentLeash.cs）。
8. **控制器解析范式**：`Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()`
   按名匹配 + `Resources.LoadAll<RuntimeAnimatorController>("")` 兜底（抄
   PatchRoles_Crossbowman.cs 的 ResolveDeadlandsController，四套各解析一个，
  任何一个失败只影响该风格——降级为跳过该风格重摇，LogWarning 一次）。
9. **状态容器范式**：`static Dictionary<int, KnightStyleState>`（instanceID 键控，
   参考 PatchDivine_FriendlyTroll 的 TrollState/GetTrollState）——缓存每骑士的
   原生控制器（首次覆盖前读取）与风格值，池复用 Strip 时恢复。

## 实现规格

### A. 静态资产（惰性、幂等）

`EnsureStyleAssets()`：解析四套骑士控制器 + 四套士兵控制器（第 8 条范式）。
全解析成功才置 ready；部分失败记录缺失项，风格池收缩为可用的（均匀重映射），LogWarning 一次。

### B. 转职入口（主入口）

`[HarmonyPostfix] Character.Promote(DroppableTool, IUnitController)`：
- `ModConfig.Enabled` 门控；`tool == null || tool.tag != "Armor"` 早退。
- `__result` 取 Knight 组件，null 早退。
- **池复用清污**：若该实例已在状态表（有风格记录）→ `StripKnight`（恢复原生控制器、
  若希腊风格则 Unregister+回 y=1、移出状态表）。
- `ApplyKnightStyle(knight)`：算确定性哈希 → 风格 index；缓存原生控制器（首次）；
  设骑士控制器；希腊风格 → `ScaleRegistryHolder.Register(mover, 0.9f)` +
  `localScale.y=0.9`；非希腊确保 y=1 且 Unregister。
  LogInfo 一次每风格（去重）：`[KnightStyle] knight styled as <风格名>`。

### C. 随从联动 + 完整性巡检（同一 World 协程，5s 一轮）

- 对每个 `FindObjectsOfType<Knight>()`：在状态表里的才处理。
- 幂等重断言：骑士控制器等于风格控制器（被原生重置则重设）；希腊缩放 y=0.9。
- **随从**：遍历 `knight._archers`，对每个 Archer：跳过带 CrossbowmanMarker 的
  （调用 `PatchRoles_Crossbowman.IsCrossbowman(archer)`，internal 可访问；
  实际上弩手根本不入队，这是防御）；其 animator 的控制器设为对应士兵控制器。
  只在当前控制器是某套 archer_soldier 系（含基底）或与我们记录不符时才写，
  避免覆盖火矢 buff 等合法状态（士兵控制器与 buff 无冲突，但避免每帧写）。
- 读档恢复：世界加载后无 Promote 机会的存量骑士（读档不重跑转职），协程首轮
  扫描全部 Knight：状态表无记录但存在的（读档恢复的）→ 按确定性哈希直接
  ApplyKnightStyle（幂等，双端一致）。

### D. Strip（清污）

恢复缓存的原生控制器；Unregister + y=1（若曾缩放）；移出状态表。
死亡/离场不需要显式清理（inactive 对象 FindObjectsOfType 扫不到），池复用由 B 的清污兜底。

## 明确不做

- 不动骑士战斗数值（攻击/血量/冲锋）——纯外观。
- 不处理 Squire（Shield 转职的侍从是过渡态，用户只说骑士）。
- 不含北境风格（用户四选）。
- 不改弩手系统（随从联动里跳过弩手即可）。
- 大小骑士编队（knight-squad-023）是后续任务，本任务只管现有单骑士类型全覆盖。

## 自查清单（完成后逐项核对并汇报）

1. 编译 0 error（贴输出尾部）。
2. 所有 Harmony 入口 try/catch + LogError。
3. EnsureStyleAssets 部分失败的降级路径（风格池收缩）可用。
4. 池复用清污 → 重 Apply 全链幂等。
5. 只读档场景（无新招募）下协程首轮能给存量骑士上风格。
6. 希腊 0.9 与非希腊 y=1 的注册/注销对称，无泄漏（Strip 有 Unregister）。
7. `git status` 只显示新增本文件。
8. 汇报：行数、关键决策、自查结果、不确定点（列出勿猜）。
