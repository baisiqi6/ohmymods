# worker-brief：英雄箭矢 0.65 显示缩放 + 无视城墙碰撞（2026-09-17 夜）

角色：worker（可写，范围严格受限）
cwd：`C:/Users/ADMIN/projects/ohmymods`（主工作区，v9.4.5 已发布内容的未提交工作树）

## 用户原始需求（背景）

1. 英雄弓箭手的金箭当前用了原版神器箭（Artemis）的完整尺寸，太大。用户已确认缩小到**现在的 65%**，只改显示大小，保留神器箭矢样式。
2. 英雄弓箭手守家时箭矢经常打在城墙上（高抛/平射弹道被墙挡）。用户明确选择的最简方案：**给英雄箭矢一个例外——与墙壁没有碰撞，正常穿过去攻击墙外目标**。

## 侦查事实（Operator 已核实，直接采用，不要重新侦查）

### 挂钩模式（已存在于 il2cpp/HeroArcherArrowVisuals.cs，982 行）

英雄箭矢已有完整的「发射作用域」追踪机制，你**必须复用**它，不得新建重复机制：

- `ArrowAttack.FireArrowInternal` Prefix（Priority.First）→ `HeroArcherArrowVisuals.BeginShot(GameObject source)`：本次 shot 是否英雄射的（`HeroArcherRuntime.IsHero` 判定，含开关门），嵌套安全。
- `Arrow.OnEnable` Prefix（Priority.First）→ `ResetArrow(Arrow)`：池复用/新生命**无条件**归还外观责任（不受功能开关限制——关闭期间发生的复用同样要清干净）。
- `Arrow.OnEnable` Postfix（Priority.Last）→ `OnSpawn(Arrow)`：英雄 shot 作用域内才写外观；OnEnable 时刻**绝不**读 `arrow.archer`（sync 注册未完成）。
- `ArrowAttack.FireArrowInternal` Finalizer → `EndShot(token)`：出栈并复核回执。

**注意陷阱**：`ScopeDepth > 0` 不等于英雄箭（嵌套时栈里可能有非英雄 shot 的帧）。判定以 `OnSpawn` 内部的英雄分支为准——你的两个改动都从这个分支内部调用，门控天然一致。

### 原生 Arrow 行为（参考 game-source/Assembly-CSharp-2.1.0/Arrow.cs，2.4 同构）

- `OnEnable`：`isKinematic=false`、`_collider.enabled=true`、重置 `_hasHit/_perfect/_trail`；`_isBelowGround`（bounds.center.y < 0.875）时会 `Physics2D.IgnoreCollision(_collider, World.GroundCollider, true)`——这是 IgnoreCollision 的原生先例。
- `OnCollisionEnter2D` → `HitObject(physicalHit=true)`：`target.GetComponentInParent<Wall>()` 命中墙 → 播 wallHitSound、50% 概率弹开或停驻、`TryDamage`。墙物理碰撞就是箭被挡住的机制；IgnoreCollision 后 `OnCollisionEnter2D` 不会为墙触发，箭自然穿墙，敌人/地面分支不受影响。
- 箭 GO 根上同体挂 `SpriteRenderer + Collider2D + TrailRenderer + Rigidbody2D`（`Require.Component` 全在根）。

### Wall 类型

- `Wall : Workable, IRPCable, IDestructiblePlayerStructure`（game-source/Assembly-CSharp-2.1.0/Wall.cs）。
- MOD 里已有 `target.GetComponentInParent<Wall>()` 用法（il2cpp/PatchArcher_Impact.cs:320），类型引用无 interop 风险。

## 实现契约

### A. 新文件 `il2cpp/HeroArcherWallPierce.cs`（你独有）

纯静态类，**不新增任何 Harmony 钩子**（由 HeroArcherArrowVisuals 的既有 OnSpawn/ResetArrow 分支调用）：

1. `internal static void Apply(Arrow arrow)`——英雄 shot 作用域内（从 OnSpawn 英雄分支调用）：
   - 取墙碰撞体缓存（见下），对箭的 `Collider2D` 逐个 `Physics2D.IgnoreCollision(arrowCollider, wallCollider, true)`；单个失败 try/catch 隔离，不影响其余。
   - 每次 IgnoreCollision 的调用量有界（每侧墙数 × 每墙碰撞体数，通常 < 30 对/箭）。
2. `internal static void Restore(Arrow arrow)`——池复用归还（从 ResetArrow 调用，**无条件**，不受开关限制）：
   - 对缓存墙碰撞体逐个 `Physics2D.IgnoreCollision(arrowCollider, wallCollider, false)`，把复用为普通箭的前一支英雄箭恢复与墙碰撞。
   - 语义对齐 ResetArrow：归还必须发生在原生 OnEnable 重置之前（Prefix 时序已保证）。
   - 注意：不得触碰 `_isBelowGround` 的地面 Ignore 对（那是 GroundCollider 对，与墙对互不相干）。
3. 墙碰撞体缓存（本文件私有，不改 PatchShared_ScanCache）：
   - `FindObjectsOfType<Wall>()`（仅活动对象），每墙 `GetComponentsInChildren<Collider2D>(false)` 收集活动碰撞体。
   - TTL 5 秒 + world 实例指针换代失效（参考 HeroArcher* 文件里既有的 world 代判定写法）。
   - 缓存为空/查询失败 → fail-closed：不穿墙（保持原生），不抛异常。
4. 有界日志 `[HeroArcherWallPierce]`：每 world 最多 8 条（应用对数采样 + 缓存换代），沿用 `Logged HashSet<string>` once 模式。
5. 开关门控：不新增配置项。英雄运行时关闭时 BeginShot 不会进英雄作用域 → Apply 自然不被调用；Restore 无条件保留（关闭期间复用同样要清干净）。
6. 单机/主机语义：英雄功能本身沿现有单机门，本文件不再重复判 network；OnEnable 时刻不读 `arrow.archer`（既有铁律）。

### B. 修改 `il2cpp/HeroArcherArrowVisuals.cs`（只加两个调用点 + 缩放）

1. `OnSpawn` 的英雄分支（外观写入成功路径）追加：`HeroArcherWallPierce.Apply(arrow)`（try/catch 隔离，失败不影响外观）。
2. `ResetArrow` 追加：`HeroArcherWallPierce.Restore(arrow)`（try/catch 隔离）。
3. **0.65 显示缩放**：
   - 目标：英雄箭显示为当前 Artemis 金箭的 65%，**优先纯显示**方案——在既有 `EnsureSprite()` 的自建 Sprite 上做（rect/PPU 调整到 65% 尺寸），不动 `transform.localScale`、不动碰撞体、不动弹道/伤害。
   - 若自建 Sprite 路线确实不可行（读代码后给出具体理由），回退方案才是箭根 `transform.localScale=(0.65,0.65,1)`，且必须在代码注释与回执里写明「碰撞体随缩放变小」的代价。
   - TrailRenderer 若明显偏粗，可同步 `widthMultiplier`×0.65（纯显示）。
   - 复用归还：ResetArrow 恢复原生外观时缩放必须一并还原（自建 Sprite 路线天然还原——换回原生 sprite 即可；transform 路线需显式还原并防御乘叠）。
   - 常量：`internal const float HeroArrowDisplayScale = 0.65f;` 放在 HeroArcherWallPierce 或 ArrowVisuals 均可，写明单位含义（相对当前完整尺寸的比例）。

### C. 测试 `tests/hero-arrow-pierce/`（新建，参考 tests/hero-artemis-arrow/ 的结构）

纯逻辑 stub 测试（无 Unity），至少覆盖：
1. 门控：非英雄 shot 不 Apply；英雄 shot Apply（用 stub 模拟 OnSpawn 分支调用）。
2. 归还：Restore 后缓存墙对恢复碰撞（IgnoreCollision stub 记录 true/false 调用序列）。
3. 缓存 TTL/换代：同 world 5 秒内复用、跨换代重查、查询异常 fail-closed。
4. 缩放常量与还原：0.65 只作用显示路径；复用还原后无残留（transform 路线时验证无乘叠）。
5. 异常隔离：Apply/Restore 内部单点异常不外抛、不影响其余对。

### D. 构建验证（不得部署）

- 在 `il2cpp/` 目录：`C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/build-stage`
  （**必须**用 `-p:BepInExPluginsPath` 覆盖到任务目录，禁止复制到任何游戏环境）。
- 要求 0 Warning 0 Error。若项目还有「actual interop」构建口味（见既有任务回执），同样跑一遍并落盘输出到 receipts/。
- 跑新增测试项目并保存输出到 receipts/tests.txt。

## 允许修改（allowlist，此外一律不动）

- `il2cpp/HeroArcherWallPierce.cs`（新建）
- `il2cpp/HeroArcherArrowVisuals.cs`（仅上述调用点 + 缩放逻辑）
- `tests/hero-arrow-pierce/**`（新建）
- `docs/project-harness/tasks/hero-arrow-pierce-20260917/**`（回执）

## 禁止

- 禁止 commit / push / merge / rebase / reset / tag。
- 禁止修改其他任何 il2cpp/*.cs、csproj、Main 系文件、存档、配置、E 盘或任何游戏目录。
- 禁止启动游戏、替换 DLL、写入 BepInEx 目录。
- 禁止新增 Harmony 钩子 / 配置项 / 全场逐帧扫描。
- 禁止改伤害、射速、弹道 magnitude、范围、池语义。

## 验收输出

写 `docs/project-harness/tasks/hero-arrow-pierce-20260917/worker-result.md`（中文，路径/标识符英文）：
1. 改动清单（文件 + 关键行为）。
2. 缩放实现路线（自建 Sprite 或 transform 回退 + 理由）。
3. 测试清单与结果（逐条）。
4. 两种构建的 0W0E 证据（路径）。
5. 已知边界/未覆盖项（如实列出，不得宣称实机验收）。

## 停止条件

越界、侦查事实与代码不符（停下来报告而不是自行改契约）、构建无法 0W0E 且原因不明、需要新增 authority。
