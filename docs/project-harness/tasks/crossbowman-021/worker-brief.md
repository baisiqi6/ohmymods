# worker 任务书：crossbowman-021 弩手（3:1 交替换皮弓箭手）

> Operator 已完成全部侦查，本文中的"已实锤事实"直接采用，不要重新侦查。
> 设计定稿见同目录 design.md（可读，但以本文为准——本文含 Operator 对数值矛盾的最终裁决）。

## 身份与边界

- 你是 OMP worker（deepseek-v4-flash，thinking=max）。
- 仓库：`C:/Users/ADMIN/projects/ohmymods`，分支 `agent/post-release-candidate`（不要动 git）。
- **只允许新建一个文件**：`il2cpp/PatchRoles_Crossbowman.cs`。
  禁止修改任何其他文件（KingdomEnhancedPlugin.cs、csproj、其他 Patch_*.cs、docs）。
  csproj 是 SDK 风格自动 glob 新文件；补丁注册是全程序集 `harmony.PatchAll(Assembly)`，
  `[HarmonyPatch]` attribute 即生效，无需登记。
- 禁止：commit / push / 部署到任何游戏目录 / 运行游戏。
- 日志统一前缀 `[Crossbowman]`，用 `KingdomEnhancedPlugin.Instance?.LogSource.LogInfo/LogWarning/LogError`。
- 语法：IL2CPP 主线现代 C#（.NET 8）。il2cpp 对象身份比较用 `.Pointer`；
  协程返回托管 IEnumerator 后由宿主 `WrapToIl2Cpp()` 启动（本文已给出宿主代码范式）。
- 整体功能受 `ModConfig.Enabled.Value` 门控（参考其他 Patch 文件开头写法）。
- 代码注释用中文，密度与现有 Patch 文件一致（解释"为什么/约束"，不写流水账）。

## 编译验收命令（唯一允许的执行命令）

在 `C:/Users/ADMIN/projects/ohmymods/il2cpp/` 下执行：

```
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=NONE
```

`-p:BepInExPluginsPath=NONE` 禁用自动部署（部署由 Operator 做）。0 error 才算过。

## 需求总览

居民捡弓转弓箭手时，**每第 4 个**变成"弩手"：死地（deadlands）动画皮肤 +
索敌/射击参数强化 + 独立外观弩矢。弩手仍是原生 Archer（无新兵种、无新池、无新商店），
且**永远不被骑士编队招募**。

## 已实锤事实（2.1.0 源码 + 2.4.0 interop 二进制双验证）

1. **转职链**：`Character.Promote(DroppableTool tool, IUnitController)` → 内部
   `Promote(Professions[tool.tag])` → `ReplaceBy` → `Pool.Spawn<Character>` 返回新实例。
   弓的映射是 `{"Bow", "Archer"}`（Character.cs:885），即 `tool.tag == "Bow"`。
   同签名的 Prefix/Postfix 挂法先例：`PatchRoles_Worker.cs`（alternate hammer promotion）。
2. **Archer 关键字段**（interop 全部暴露，直接读写的实例字段/属性）：
   - `private ArrowAttack ActiveArrowAttack { get; set; }`——可写。原生在 Awake/OnEnable
     重置为 `_arrowAttack`；火矢 buff（`ActivateBuff(BuffType.FireAttacks)`）会切到
     `_fireArrowAttack`；网络收包（Archer.cs:1725）也会重置。
   - `public float shootRange = 8f`、`public float towerShootRange = 12f`。
   - `private Vector2 _shootIntervalRange`、`private Vector2 _shootIntervalRangeFormation`——
     射击间隔（x/y 都乘系数即冷却 ×2）。**SO 的 ShotCooldownSeconds 不被 Archer 用**。
   - `private Scanner _enemyScanner`——索敌扫描器，构造时用 shootRange；
     `Scanner.range`/`rangeBehind` 可写（塔上会被原生改成 towerShootRange，别跟它抢）。
   - 索敌双重门控：扫描器 range（发现目标）+ `ActiveArrowAttack.Range >= |dx|`（决定开火/推进）。
3. **ArrowAttack SO**（ScriptableObject，全体弓箭手共享，**禁止改原资产**，必须克隆）：
   - `Range = _shotMagnitude² / -_arrowGravity`（Util.GetProjectileRange）。
   - 字段：`_arrowPrefab`（Arrow 类型，SO 只认 Arrow，**Bolt 类塞不进去**）、
     `_shotMagnitude`、`_boostedShotMagnitude`（默认12）、`_arrowGravity`（编辑器期由
     arrow prefab 的 Rigidbody2D.gravityScale 推导，运行时直接写字段即可）、
     `_maxLead`、`_maxForceError`、`_dropInWaterProbability`、`_arrowOriginOffset`、
     `_shotCooldownSeconds`。
4. **Arrow prefab**（发射时 `Pool.Spawn<Arrow>(SO._arrowPrefab,...)`，按 prefab 引用走池）：
   - `public int hitDamage = 1`、`public int perfectDamageMultiplier = 2`、
     `private DamageSource _damageSource = DamageSource.Arrow`（保持 Arrow 不动）。
   - 飞行重力来自 spawned 实例的 Rigidbody2D.gravityScale。
5. **弩炮弹矢**：`Bolt` 类（非 Arrow 子类，`DamageSource.Bolt`），不直接用；
  仅取其 SpriteRenderer.sprite 作弩矢外观。Ballista.boltPrefab / `Resources.LoadAll<Bolt>("")`。
6. **换皮**：`archer_deadlands` 是 RuntimeAnimatorController（resources.assets 内置，
   非独立兵种）。解析先例：`PatchEconomy_BankAssistants.cs` 的 `TryResolveControllers`——
   `Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()` 按名匹配 +
   `Resources.LoadAll<RuntimeAnimatorController>("")` 兜底；赋值
   `animator.runtimeAnimatorController = controller`。
7. **骑士招募排除点**：`Kingdom.FetchArchersForJob` 逐个调
   `Archer.IsAvailableForJob(GameObject jobObject)`，其内部已 `jobObject.TryGetComponent<Knight>`。
   给骑士当随从的 AssignJob 就是 knight.gameObject。GuardSlot/塔位走同一入口但 jobObject 无 Knight。
8. **同步池注册**（弩矢独立池必需，否则 Pool.Spawn 报错/联机 desync，AGENTS 坑 11/14）：
   `PoolManager.Init` 会清掉运行时注册的池（PatchPoolFix.cs 已证），所以本文件要自带
   `[HarmonyPatch(typeof(PoolManager), nameof(PoolManager.Init))]` postfix 幂等重注册。
   注册代码范式抄 `PatchRoles_Castle.cs` 的 `RegisterSyncedPool`（`pm.CreatePoolFor(prefab)`、
   `pool.sync=true`、从 `PatchRoles_Castle` 同款 30000 段分配器思路**自建独立计数器**——
   不要 import PatchRoles_Castle 的私有分配器，自持 `static short _nextSyncId = 30130` 起，
   并检查 `cachedSyncIdPoolPairs` 冲突时拒绝+报错（范式同 PatchRoles_Ninja.EnsurePool）。
9. **World 协程宿主范式**：`[HarmonyPatch(typeof(World), nameof(World.OnLevelLoaded))]` postfix →
   `world.StartCoroutine(XxxRoutine(world).WrapToIl2Cpp())`，完整抄
   `PatchWorld_DefenseSpacing.cs` 的 SupervisorRoutine 写法（含 per-world 指针守卫）。
10. **标记组件**：`class CrossbowmanMarker : MonoBehaviour`，`AddComponent` 即被
    Il2CppInterop 自动注册（先例 SpecialTowerRebuildMarker）。挂在弩手 Archer 的
    gameObject 上，用于：骑士排除过滤、完整性巡检遍历、池复用清污。

## 实现规格（照此实现，数值已由 Operator 裁决）

### A. 静态资产（惰性初始化，全部 DontDestroyOnLoad，缓存为 static）

`EnsureAssets()`（幂等，所有入口都先调它）：

1. 从 `Managers.Inst.holder.tagCharacterPairs["Archer"]` 取原生弓箭手 prefab：
   - 缓存基础默认值：`shootRange`、`_shootIntervalRange`、`_shootIntervalRangeFormation`
     （读 prefab 的 Archer 组件）、基础 Animator 的 `runtimeAnimatorController`
     （`GetComponentInChildren<Animator>()`）。
   - 缓存基础 SO：prefab.Archer 的 `_arrowAttack` 字段 → 读 `_shotMagnitude`、
     `_boostedShotMagnitude`、`_arrowGravity`、`_arrowPrefab`。
2. **克隆弩矢 prefab**：`Object.Instantiate(baseArrowPrefab.gameObject)`，命名
   `KEM_CrossbowBolt`，DontDestroyOnLoad：
   - `hitDamage = 2`（perfect 自动 ×2=4，原生 1/2）。
   - `Rigidbody2D.gravityScale *= 0.5f`。
   - SpriteRenderer.sprite ← 原生 Bolt 外观：`Resources.LoadAll<Bolt>("")` 里取名字含
     "Bolt" 的第一个的 SpriteRenderer.sprite；取不到 LogWarning 并保留原箭外观（降级可用）。
   - 其余组件（TrailRenderer/NetworkSoftSimulator/碰撞/音效）原样保留。
3. **克隆 SO**：`ScriptableObject.Instantiate<ArrowAttack>(baseSO)`... 实际用
   `Object.Instantiate(baseSO)`，命名 `KEM_CrossbowAttack`，DontDestroyOnLoad：
   - `_shotMagnitude *= 1.5f`、`_boostedShotMagnitude *= 1.5f`、`_arrowGravity *= 0.5f`、
     `_arrowPrefab = 克隆弩矢`。
   - **Operator 裁决（勿改）**：由此 SO 内部 Range≈36（v²/g），这是有意为之——
     36 让弩手在射程内"站桩狙击不冒进"（Archer.cs:1116 的推进判断用它）。
     实际交战距离 12 由 D 段的 shootRange/扫描器硬约束。
4. **解析控制器**：`archer_deadlands`（第 6 条先例），缓存 static；解析失败 LogWarning，
   弩手功能继续（只缺皮肤）。
5. **注册弩矢同步池**（第 8 条），label "CrossbowBolt"。

### B. 转职交替（主入口）

`[HarmonyPostfix] Character.Promote(DroppableTool, IUnitController)`：

1. `ModConfig.Enabled` 门控；`tool == null || tool.tag != "Bow"` 早退。
2. `__result` 为空/无 Archer 组件/ GameObject 为空 → 早退（记 LogWarning 一次性即可）。
3. **先清污**：若结果实例已有 CrossbowmanMarker → `Strip(result 的 Archer)`（见 E）。
   （池复用会把带皮肤/参数的旧实例发给普通弓箭手。）
4. `static int _bowPromoteCount`（进程级静态，跨岛延续，永不重置）`++`；
   `_bowPromoteCount % 4 == 0` → `Apply(result 的 Archer)` 并 LogInfo
   （`[Crossbowman] bow promote #N -> crossbowman (25%)`）；否则不动。

### C. Apply(Archer)（弩手打包，幂等）

1. `EnsureAssets()`；任一关键资产缺失（弩矢 SO 克隆）→ LogError 并放弃（不能半套）。
2. `AddComponent<CrossbowmanMarker>()`（已有则跳过）。
3. `archer.ActiveArrowAttack = 克隆SO`。
4. `archer.shootRange = 12f`；`_enemyScanner.range = _enemyScanner.rangeBehind = 12f`
   （仅 Apply 时设置；塔位切换由原生管理，巡检不碰扫描器）。
5. `_shootIntervalRange` 与 `_shootIntervalRangeFormation` 的 x/y 全部 ×2（读现值乘，不读缓存——
   buff 可能已改过）。
6. Animator 换 `archer_deadlands` 控制器（解析失败跳过）。

### D. 读档重算（数量守恒 25%）

`World.OnLevelLoaded` postfix 启动协程（第 9 条范式）：

1. 每世界一次：`yield return WaitForSeconds(15f)`（等单位恢复完）后执行
   `RecomputeOnLoad()`；同一 world 指针不重复执行。
2. `RecomputeOnLoad()`：`FindObjectsOfType<Archer>()` → 过滤掉 null/ inactive →
   按 `GetInstanceID()` 升序排序（联机下客户端选择可能与服务端有外观级分歧，
   伤害判定在权威端，已知并接受——代码注释里写明）→ 每 `i % 4 == 3` 的 Apply，
   其余有 Marker 的 Strip。
3. LogInfo：`[Crossbowman] recompute on load: total=N crossbowmen=M`。

### E. Strip(Archer)（清污，恢复原生）

移除 Marker；`ActiveArrowAttack = _arrowAttack`（原生私有字段，直接读该实例的）；
`shootRange`/两个 interval 恢复为 A.1 缓存的 prefab 默认值；扫描器
`range/rangeBehind = 恢复后的 shootRange`；Animator 换回缓存的基础控制器。
（对象池 respawn 不重拷序列化字段，所以必须显式恢复。）

### F. 完整性巡检（自愈一切重置路径）

同一 World 协程内，在 15s 首算之后每 5s 一轮（同一协程里继续 `while` 循环即可，
注意保留 per-world 指针守卫的既有范式）：

对每个 `FindObjectsOfType<CrossbowmanMarker>()`：

- Archer 组件没了/对象死了 → Destroy(marker)，continue。
- `ActiveArrowAttack == archer._arrowAttack`（等于基础值才说明被重置）→ 换回克隆SO。
  **当前是 `_fireArrowAttack`（火矢 buff 中）时绝不动**——这是与原生 buff 的兼容契约。
- `shootRange != 12f` → 12f；intervals 检查方式：不检查（buff/阵形可能合法改它们，
  只在 Apply 时设置一次；巡检只兜最常被 OnEnable 重置的 ActiveArrowAttack/shootRange/animator）。
- Animator 控制器不是 deadlands（也不是 null）→ 换回（换皮也可能被池路径重置）。

### G. 骑士排除

`[HarmonyPostfix] Archer.IsAvailableForJob(GameObject jobObject, ref bool __result)`：
`__instance` 有 CrossbowmanMarker 且 `jobObject.GetComponent<Knight>() != null`
（用 TryGetComponent 就 TryGetComponent，interop 不支持就 GetComponent）→ `__result = false`。
LogInfo 一次性（static bool 去重）：`[Crossbowman] excluded from knight recruitment`。

## 明确不做（non-goals）

- 不做保存档写入（皮肤不进存档，读档重算兜底——设计定稿）。
- 不改骑士、塔、商店、银行任何现有逻辑。
- 不做武士/骑士小队（后续任务）。
- 不区分 biome：任何世界捡弓都参与 3:1 交替。
- 弩矢不带 DoT/穿透等新机制——只改伤害数值与外观。

## 自查清单（实现完成后逐项核对并在汇报中确认）

1. 编译 0 error（贴出 build 输出尾部）。
2. 所有 Harmony patch 都有 try/catch + LogError（防一个异常炸全链，参考现有文件）。
3. EnsureAssets 在 holder 未就绪时（tagCharacterPairs 空）安全跳过且不缓存半成品。
4. Promote postfix 对非 Bow 工具零开销早退；对 __result==null 安全。
5. 巡检协程在 world 销毁时自然退出（范式自带）；per-world 幂等。
6. 池注册冲突检查（syncID 已被占且非本 prefab → 拒绝+LogError，不覆盖原生池）。
7. 没有修改任何既有文件（`git status` 只显示新增 PatchRoles_Crossbowman.cs + 未跟踪任务书）。
8. 汇报：文件行数、关键决策点、自查清单逐项结果、任何你不确定的点（列出，别猜）。
