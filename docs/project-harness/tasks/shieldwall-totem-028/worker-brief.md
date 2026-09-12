# worker 任务书：shieldwall-totem-028 盾墙雕像移植（希腊）

## 身份与边界

- 你是本仓库 worker（subagent）。仓库 `C:/Users/ADMIN/projects/ohmymods`（IL2CPP 主线）。
- **只允许新建一个文件**：`il2cpp/PatchWorld_ShieldWallTotem.cs`，以及**修改一个文件**：
  `il2cpp/PatchWorld_DefenseSpacing.cs`（仅加镜像豁免，见 F）。禁止其他文件、commit、push、部署、运行游戏。
- 日志前缀 `[ShieldWallTotem]`。`ModConfig.Enabled` 门控。中文注释。
- 编译验收：`C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=NONE`，0 error，删 NONEKingdomEnhancedMod。

## 需求

希腊世界激活北境的盾墙玩法：城墙旗帜旁出现可交互图腾（1 币），付费后拉一支持盾武士队
在墙外结阵并冲锋清怪（原生 Active Shield Wall）。兵源=我们的北境随从（带盾弓箭手）。

## 侦查实锤（直接采用，全部有文件:行号/资产证据）

1. **放置点**：原生北境把 totem 挂在 PayableBorder（城墙旗帜）子物体——
   `PayableBorder.Setup(Side)`（PayableBorder.cs:86-96）级联
   `GetComponentInChildren<PayableShieldWallActivator>()?.Setup(side)`。
   希腊的 border_greece 无 totem 子物体 → 挂 `PayableBorder.Setup` postfix：
   Enabled && BiomeIndex==5(Greece) && `GetComponentInChildren<PayableShieldWallActivator>()==null`
   → 解析 totem prefab → Instantiate 为 banner 子物体（继承局部坐标）→ `activator.Setup(side)`。
2. **totem prefab 解析**：`Resources.LoadAll<PayableShieldWallActivator>("")` 名含
   "ActiveShieldWallTotem"（资产实锤存在）缓存 static，30s 限频重试（跨资产解析先例：
  FarmCats/KnightStyle）。
3. **biome 门**：原生 `Kingdom.TrySpawnShieldWall`（private，Kingdom.cs:1953）硬门
   `BiomeIndex != 3 return null`（仅北境）。绕法：**prefix**——BiomeIndex==5 时托管侧复刻
   方法体（`Instantiate(kingdom.activeShieldWallPrefab, pos, identity, world.gameLayer)` →
   `SetSide` → `StartRecruiting` → `shieldWalls[side]=formation`）并 `return false`；
   其他 biome `return true` 放行原生。这同时解锁付费（TrySpawnActiveShieldWall）、
   被动墙（CheckShouldSpawnShieldWall）、读档重建三条路径。
4. **prefab 字段兜底**：希腊场景 Kingdom 的 `activeShieldWallPrefab` 可能为 null（场景序列化
   grep 不可验）——prefix 里判空，null 则 `Resources.LoadAll<Formation>("")` 名含
   "ActiveShieldWallFormation"（资产实锤）赋给本地使用（不改 Kingdom 字段，仅本次 Instantiate
   用），一次性 LogWarning 提示。passiveShieldWallPrefab 同理（"PassiveShieldWallFormation"）。
5. **Setup 网络注册**：`activator.Setup(side)` 内部自动走
   `RegisterObject(gameObject, 974/975, Dynamic)`（原生常量，interop 实锤）——直接调原生
   Setup 即可，不要手工注册。
6. **付费语义**：付一次拉一队（非开关），墙存活期间不可再付（IsPayable 恒 false），墙自毁后
   恢复可付。价格/冷却/交互全部原生继承，零接线。
7. **兵源**：Archer 盾墙门=`_npcShieldUser!=null && HasShield()`——北境随从合格；
   Knight 无盾门也能入（原生行为，接受）；希腊 Worker 我们没装盾（不入）。
8. **无持久化**：activator/Active 墙都不进存档，原生每次读档由 banner 重建——postfix 天然幂等。
9. **风险已知**：`shieldWalls[side]` 依赖 Unity fake-null 判活（Il2CppInterop 行为待实测，
   不处理，验收观察项）；图腾用北境美术（无希腊变体，用户已知情）。

## 实现规格

### A. 文件头

类文档：需求、侦查事实引用（PayableBorder.cs:86/Kingdom.cs:1953/Formation.cs:574）、
与 NorseSquad 的联动（北境随从=兵源）、已知风险（fake-null/北境美术/联机待实测）。

### B. PayableBorder.Setup postfix（放置）

按实锤 1-2 实现。注意：Setup 是 public（`nameof` 可用）；希腊 biome 判定
`BiomeHolder.Inst.BiomeIndex == BiomeHolder.GreeceBiomeIndex`；banner 子物体 Instantiate
后 `activator.Setup(side)`（读 `__instance.side` 或 activator 所在 banner 的 side——
PayableBorder 有 side 字段，interop 读）。一次性日志
`"[ShieldWallTotem] totem attached to <side> border banner"`（static 去重每侧一次）。

### C. Kingdom.TrySpawnShieldWall prefix（biome 门绕过）

按实锤 3-4 实现。字符串名补丁（private）。复刻体引用的原生成员全部 interop 实锤
（Instantiate/SetSide/StartRecruiting/shieldWalls 索引赋值/world.gameLayer）。
逐字对照 game-source Kingdom.cs:1953-1975 方法体（位置=border[side]、rot=identity、
parent=gameLayer）。失败（prefab 双兜底都 null）→ LogError 一次 + `return true`
（放行原生=原生会因 biome 门 return null，付费静默失败，降级可接受）。

### D. 兵源联动验证钩子（只加日志不加逻辑）

Formation.Recruit 拉到北境随从时无现成钩子——不加（原生 Recruit 遍历
Knights+Archers+Workers 候选，我们的随从在 kingdom._archers 在册，天然可见）。
仅在盾墙销毁（Formation.OnDisable 或 RushEnd）加一次性 LogInfo 观察成员去向
（可省——首版只靠 diag 日志，标注"验收观察项"即可，不做）。

### E. 联机

单机语义优先：prefix/postfix 无 HasWorldAuth 门（Setup/Pay 原生自带权威端逻辑）。
联机标注"待实测"（图腾双端注册/Formation 同步），文档已知边界。

### F. 镜像豁免（PatchWorld_DefenseSpacing.cs，唯一既有文件改动）

`MirrorNightArcherGoal` 带内匹配处（现有 inGuardSlot/y 豁免之后）加：
`var f = archer.GetFormation(); if (f != null && f.IsShieldWall) return true;`
（GetFormation public 方法 Archer.cs:1816 返回 _currentFormation；IsShieldWall public 属性；
不要用 IsInFormation()——过宽，骑士队/玩家队也命中）。一行注释引侦查 §7 依据。

## 明确不做

- 不做希腊风图腾美术（北境原样，用户知情）。
- 不改 Formation/Kingdom/PayableShieldWallActivator 原生行为（prefix 仅绕 biome 门）。
- 不做被动墙（PassiveShieldWall）的额外逻辑——biome 门绕过后原生自动解锁（外墙被毁时触发），
  属原生行为。
- 不处理 shieldWalls fake-null 判活（验收观察项）。

## 自查清单（汇报逐项确认）

1. 编译 0 error（贴尾部）。
2. postfix 幂等（banner 每次读档重建→重挂，无重复 totem）。
3. prefix 只在希腊 biome 改道，其他 biome 零影响。
4. prefab 双兜底路径完备（Kingdom 字段→Resources）。
5. 镜像豁免一行式、判据精确（GetFormation+IsShieldWall）。
6. `git status` 只显示两个允许文件的改动。
7. 汇报：行数、关键决策、自查结果、不确定点（列出勿猜）。
