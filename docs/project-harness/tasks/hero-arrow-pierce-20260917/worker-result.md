# worker-result：英雄箭矢 0.65 显示缩放 + 无视城墙碰撞（2026-09-17 夜）

角色：worker（可写，范围严格受限）。cwd：`C:/Users/ADMIN/projects/ohmymods`。
按 `worker-brief.md` 执行；allowlist 之外未做任何修改。

---

## 0. 执行状态（先读这条：验证命令被本 session 的审批门挡住，需要 operator 执行）

实现、接入、测试代码均已完成并落盘（见 §1）。**本 session 无法执行任何命令**：

- 本 worker 以 `--approval-mode=write` 派发（read/workspace-write 自动批准，exec 需交互批准）；当前非交互环境没有审批 UI，因此 `bash` / `eval` / `hub` / `task` 全部 fail-closed，原始报错为：

  ```
  Tool "bash" requires approval but no interactive UI available.
  Options: 1. Set tools.approvalMode: yolo in /settings
           2. Add tools.approval.bash: allow to config
           3. Use an interactive UI to approve the tool call
  ```

- 我没有自行修改 approval 配置（那等于 worker 自我扩权/自我批准），也没有改用其它执行通道。
  `invoke-coding-agents/references/omp.md` 对这种情形的分工写明："若非交互模式因此拒绝必要的
  只读 Bash，由 operator 独立运行该核验"；`worker-brief.md` 的停止条件也含"需要新增 authority"。
- `xd://lsp` 已探测：本工程未配置 language server（`No language servers configured for this project`），
  因此也没有本地诊断可跑。

**结论：§3 的 stub 测试、§4 的两种构建、addendum 的旧套件影响核对，都需要 operator 在获得 exec
authority 的 session 里执行下面 5 条命令（命令与 brief 原样一致，产物全部落在本任务 `receipts/`，
不会复制到任何游戏目录）。**

```bash
# 1) 主线 IL2CPP Debug（对真实 2.4 interop），产物只 stage 到任务目录
cd C:/Users/ADMIN/projects/ohmymods/il2cpp
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/build-stage \
  > ../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/build-debug.txt 2>&1

# 2) focused actual-interop 编译门（HeroArcherWallPierce.cs 对 E 盘真实 2.4 interop 只读引用）
cd C:/Users/ADMIN/projects/ohmymods/tests/hero-arrow-pierce/actual-interop
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug \
  > ../../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/interop-compile.txt 2>&1

# 3) 新增 stub 测试（gold），预期 ALL PASS
cd C:/Users/ADMIN/projects/ohmymods/tests/hero-arrow-pierce
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project Tests.csproj -- --mode=gold \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/tests.txt 2>&1

# 4) fail-closed 模式，预期 ALL PASS（pass>0 fail=0）
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project Tests.csproj -p:EmbedArtemis=false -- --mode=missing \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/tests-missing.txt 2>&1

# 5) 旧 artemis 套件影响核对（**预期构建失败**，见 §5 第 1 条；仅取证，不改动它）
cd C:/Users/ADMIN/projects/ohmymods/tests/hero-artemis-arrow
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-suite-after-change.txt 2>&1
```

预期结果：1、2 为 0 警告 0 错误；3、4 为 `ALL PASS`（gold ≈100 条断言，missing 单例 9 条）；5 失败于
`HeroArcherWallPierce`/替身面缺失（见 §5）。

---

## 1. 改动清单（allowlist 内）

| 文件 | 状态 | 关键行为 |
|---|---|---|
| `il2cpp/HeroArcherWallPierce.cs` | 新建（10811 B） | 英雄箭「无视墙碰撞」的全部逻辑：`Apply` / `Restore` / 墙碰撞体快照（TTL 5s + world/layer/scene 换代）/ 有界日志 / 异常隔离 / 测试钩子。**不含任何 Harmony 钩子** |
| `il2cpp/HeroArcherArrowVisuals.cs` | 修改（983 → 1006 行，仅 4 处） | ① `HeroArrowDisplayScale = 0.65f` 常量；② `EnsureSprite` 里 `Sprite.Create` 的 PPU 改为 `NativePixelsPerUnit / HeroArrowDisplayScale`（+ `displayPixelsPerUnit` 日志）；③ `ResetArrow` 开头无条件 `HeroArcherWallPierce.Restore(arrow)`（独立 try/catch）；④ `OnSpawn` 英雄分支「外观写入成功」之后 `HeroArcherWallPierce.Apply(arrow)`（独立 try/catch）。类头 doc 同步 |
| `tests/hero-arrow-pierce/Tests.csproj` | 新建 | net8.0 控制台，直接编译两个生产文件 + 替身（`EmbedArtemis` 开关同 artemis 套件） |
| `tests/hero-arrow-pierce/UnityStubs.cs` | 新建（13023 B） | artemis 替身 + `Collider2D` + `Physics2D`（记录每次 `IgnoreCollision(箭, 墙, bool)` 序列、可按对注入异常）+ `Object.FindObjectsOfType<T>()`（活动对象语义、可注入异常）+ `Object.ResetScene()` |
| `tests/hero-arrow-pierce/GameStubs.cs` | 新建（6634 B） | artemis 替身 + `Arrow._collider` + 全局命名空间 `Wall`（仅 `GetComponentsInChildren<T>(bool)`，可注入枚举异常） |
| `tests/hero-arrow-pierce/PngCodec.cs` | 新建（复制，未改） | 自包含，不跨工程引用 |
| `tests/hero-arrow-pierce/Program.cs` | 新建（33.6 KB） | 12 个 gold 用例 + 1 个 fail-closed 用例 + 钩子契约反射核对（≈109 条断言） |
| `tests/hero-arrow-pierce/actual-interop/Interop.csproj` + `InteropStubs.cs` | 新建 | 只编译 `HeroArcherWallPierce.cs`，对 E 盘 2.4 interop 全量只读引用；仅替身 MOD 侧 `ArcherOptionsScope` / `KingdomEnhancedPlugin` |

行为要点（与 brief 契约逐条对应）：

1. `Apply(Arrow)`：取自建箭碰撞体（`arrow._collider`）→ 要求 world 上下文可用 → 快照（TTL/换代）可用 →
   对快照里每个活动墙碰撞体 `Physics2D.IgnoreCollision(arrowCollider, wall, true)`；单对失败隔离；
   拿不到箭碰撞体/上下文/快照失败 → fail-closed 直接返回（保持原生碰撞），绝不外抛。
   每箭调用量 = 快照碰撞体数（硬上限 256）。
2. `Restore(Arrow)`：从 `ResetArrow`（OnEnable **Prefix**，早于原生 body）**无条件**调用（不受功能开关
   与英雄资格限制）；只遍历当前快照、**从不重扫**；逐对 `IgnoreCollision(..., false)` + 单对失败隔离；
   从不触碰地面无视对（快照只含 Wall 下的碰撞体）。
3. 快照：`FindObjectsOfType<Wall>()`（仅活动墙）→ 每墙 `GetComponentsInChildren<Collider2D>(false)`
   （仅活动碰撞体）→ 扁平列表；TTL 5s + world/layer/scene 换代失效；单墙枚举失败只跳过该墙；
   顶层查询失败 fail-closed 且**保留旧快照**给归还路径；成功重扫才整体替换。
4. 日志：`[HeroArcherWallPierce]`，每 world 换代最多 8 条（换代扫描摘要 / 首次 apply 对数 / 截断 /
   失败），once 键随换代清空。
5. 零新增：无 Harmony 钩子、无配置项、无全场逐帧扫描、无 cl.exe/csproj/Main 系改动。

---

## 2. 缩放实现路线：自建 Sprite 的 PPU（首选路线）

- 实现：`Sprite.Create(..., NativePixelsPerUnit / HeroArrowDisplayScale, ...)`，即 PPU 32 → 32/0.65 ≈ 49.2308。
  世界尺寸 = 30 px / 49.2308 = **0.609375** = 原生 0.9375 的 65%。rect 仍 30x5、纹理仍 30x5（Point/Clamp/
  无 MipMap）、pivot (.5,.5) 不变。
- 为什么可行：自建 sprite 的 PPU 只被本模块写；缩放发生在"显示"层，`transform.localScale`、碰撞体、
  弹道、伤害、池语义全不动；池复用归还时换回原生 sprite，天然还原、无乘叠（不存在 transform 回退方案
  的"碰撞体随缩放变小 + 需显式还原防乘叠"代价）。
- 已排除：`transform.localScale` 回退路线（会缩小碰撞体并破坏 artemis 套件原先的"根变换逐字节一致"契约）。
- TrailRenderer：**未动**。trail 粗细是 prefab 内 authored 的世界单位值，本 session 无法渲染观察"是否
  明显偏粗"；改它需要新增 trail 写入的回执/CAS 归还机制（超出"仅调用点 + 缩放逻辑"的 allowlist 描述），
  因此保持原样并在此注明。若实机观感需要，请再派一片（可复用既有 Receipt 的 CAS 模式）。

---

## 3. 测试清单（tests/hero-arrow-pierce/Program.cs）——代码已就位，**待执行**

钩子契约（反射）：
1. 受 patch 的原生方法仍**恰好**是 `Arrow.OnEnable` + `ArrowAttack.FireArrowInternal`；
2. `HeroArcherWallPierce` 无任何 Harmony 特性（穿墙不新增钩子）；3. 两个 Priority.First 前缀；
4. Postfix Priority.Last 恰 1 个；5. Finalizer 恰 1 个。

gold（12 用例，逐条对 brief §C）：

| # | 覆盖点（brief §C） | 断言摘要 |
|---|---|---|
| 1 | 门控 + 缩放 + 资产身份 | 英雄 shot：上金色；`HeroArrowDisplayScale==0.65`；PPU≈49.23、世界宽 0.609375、rect/纹理 30x5、pivot 不变；3/3 墙碰撞体 true 对；fresh spawn 无 false 对；非英雄箭不染不穿（但其 spawn 仍无条件归还）；功能关同样不染不穿；首次加载日志含 ppu/scale；无对象销毁；真实 PNG 仍 150px/52 opaque（并入 #12 详查） |
| 2 | 归还 + 缩放还原 | 首次 life true×2；`ResetArrow` 后 false×2 且顺序在 true 之后；外观同步归还；第二个 life 复用同一缩放 sprite（PPU 不变、无乘叠）、只写 sprite+color；功能关闭下 `ResetArrow` 仍归还（false 累计 6） |
| 3 | 缓存 TTL/换代（brief §C-3） | 首次 1 次扫描；5s 内复用不重扫；TTL 过期重扫 1 次并带上新墙；world 换代强制重扫；停用墙被排除；归还路径永不重扫、用换代前快照 |
| 4 | 查询异常 fail-closed（§C-3） | `FindObjectsOfType` 抛异常：不穿墙、不抛、旧快照保留、归还仍可达、日志含 `scan failed`、总日志 ≤8/换代；恢复后下一次成功 |
| 5 | 异常隔离（§C-5） | 单对抛异常：apply 与 restore 都只丢该对、其余照常；直接调用 `Apply/Restore` 不外抛 |
| 6 | 有界 | 300 碰撞体 → 快照截断 256、apply=256、`capped at 256` 只记一次 |
| 7 | 外观耦合 fail-closed | 半写（color 抛异常）→ 不挂穿墙；既有 Tick 仍能归还半写外观 |
| 8 | 无快照/TTL 过期归还 | 无快照时 `Restore` 零调用零扫描；TTL 过期后仍用旧快照归还不重扫 |
| 9 | 只碰墙对 | `NonVisualSignature` 逐字节一致、sharedMaterial/enable/sorting 不动、只写 sprite+color 各 2 次、无销毁、root scale 仍为 1 |
| 10 | 同 shot 多箭 + 日志预算（§C-1） | 主箭+2 副箭全部无视墙；同代内多次射击日志不增长；换代后预算重置 |
| 11 | 上下文/碰撞体缺失 fail-closed | 无 world 上下文：不上色不穿墙（归还仍生效）；`_collider==null`：仍上色但不穿墙不抛 |
| 12 | 原生资产身份 | 嵌入资源存在；extrude 0 / FullRect / Point / Clamp / 无 mip；150px、52 opaque |

missing 模式（`-p:EmbedArtemis=false`，1 用例，9 条断言）：资源缺失时保持原生外观、**不挂穿墙、无快照**、
无回执、至多销毁自建纹理、穿墙日志 0 条、外观诊断恰 1 条。

**结果：未执行（见 §0）。** 预期 3、4 两条命令 `ALL PASS`。

---

## 4. 构建证据

**未执行（见 §0）。** 需要落盘的产物：

- `receipts/build-debug.txt`：`dotnet build -c Debug -p:BepInExPluginsPath=.../receipts/build-stage`（真实 2.4
  interop；`CopyOutput` 只写任务目录，不部署）→ 要求 0 警告 0 错误；
- `receipts/interop-compile.txt`：focused interop 编译门 → 要求 0 警告 0 错误。

离线已完成的静态核对（无 exec 也能确证的部分）：

- `Arrow._collider` 在**真实 2.4 interop** 中确有暴露：`docs/project-harness/tasks/musketeer-20260916/actual-interop.txt:322`
  （TYPE Arrow 段 `PROP UnityEngine.Collider2D _collider`）。这是本改动最关键的私有字段，已证存在。
- `Physics2D.IgnoreCollision(Collider2D, Collider2D, bool)` 形状：2.4 原生 `Arrow.OnEnable` 自身调用
  （与 `game-source/Assembly-CSharp-2.1.0/Arrow.cs` 同构），说明该重载在本次游戏/引擎构建里真实存在；
  最终仍以 §0 命令 1/2 的编译为准。
- `GetComponentsInChildren<T>(bool)` 生产先例：`il2cpp/PatchWorld_SpecialTowerRebuild.cs:893`；
  `FindObjectsOfType<T>()` 生产先例：`il2cpp/PatchRoles_Crossbowman.cs:642`；
  `renderer.Pointer`/GO Identity 模式与 `HeroArcherArrowVisuals` 既有实现一致。
- 两个生产文件通过本机结构解析（read 摘要正常），无结构性语法损伤；编辑处已逐段复核。
- 无新增 Harmony 入口（文件级静态核对；测试内还有反射断言）。

---

## 5. 已知边界 / 未覆盖项（如实列出）

1. **旧 artemis 套件（`tests/hero-artemis-arrow`）在本次改动后预期不再可编译**（allowlist 禁止我改动它）：
   - 该工程直接编译 `il2cpp/HeroArcherArrowVisuals.cs`，但不包含新文件 → `CS0103 HeroArcherWallPierce`；
   - 即使补上新文件，其替身面也不足：游戏替身 `Arrow` 无 `_collider`（CS1061）、Unity 替身无
     `Collider2D`/`Physics2D`/`Wall`/`FindObjectsOfType`（CS0246）；
   - gold 套件把"PPU 32"钉成原生身份，而 0.65 显示缩放（用户需求）使真实 PPU 变为 49.2308，该断言
     已过时。
   建议 operator 二选一：(a) 把新文件与替身面并入该工程、同步替换 PPU 断言；(b) 归档该套件（其"原生尺寸"
   契约已被 0.65 需求取代）。**这属于契约变更后的测试维护，超出本 worker 的 allowlist。**
2. 实机语义未验证：`IgnoreCollision` 后箭不再触发墙的 `OnCollisionEnter2D`（穿墙生效）、池回收/换岛/
   联机的实际观感、0.65 的实机观感、trail 粗细、密集齐射下的帧耗时——全部待游戏内验证。
3. 极窄边界（已在代码 doc 注明）：`SetActive(false)` 但仍存活的墙不进入活动扫描，其旧无视对可能在
   快照换代后无法再被归还命中（真实路径里墙通常销毁而非停用）。
4. `Restore` 对每支箭（含非英雄箭）在快照存在时会写 ≤N 个 `false` 对（无条件归还的设计代价）；未实机
   测帧耗时；`Apply` 每箭 ≤256 对同理。
5. 未改：伤害/射速/弹道/射程/池语义/其他 PNG/其他功能；未 commit/push/deploy；未触碰任何游戏目录、
   存档或配置。

---

## 6. 修复说明（2026-09-18，第一轮回执之后）

### 6.1 回执核对结论

| 命令 | 回执证据 | 结论 |
|---|---|---|
| 1) 主线 Debug 构建 | `build-debug.txt`：`0 个警告 / 0 个错误` | **0W0E 通过**（真实 2.4 interop；产物 stage 在 `receipts/build-stage`，未部署） |
| 2) interop 编译门 | `interop-compile.txt`：`0 个警告 / 0 个错误` | **0W0E 通过**：`Arrow._collider`、`Physics2D.IgnoreCollision(Collider2D,Collider2D,bool)`、`FindObjectsOfType<Wall>()`、`GetComponentsInChildren<Collider2D>(bool)` 对真实 interop 全部成立 |
| 3) gold | `tests.txt`：pass=86 fail=3 | 3 个失败全部是**测试替身误用**（见 6.2）；其余 86 条（缩放 0.65 世界尺寸、TTL/换代、scan fail-closed、异常隔离、256 上限、无副作用等）通过 |
| 4) missing | `tests-missing.txt`：pass=8 fail=5，首行 `resource embedded: True` | 运行的程序集**仍嵌入资源** → 实际跑的是正常外观路径，fail-closed 分支根本没被触发。**不是生产违约**（见 6.3） |
| 5) 旧 artemis 套件 | `artemis-suite-after-change.txt` | 唯一错误 **2 条** CS0103（`HeroArcherArrowVisuals.cs:338,453` 找不到 `HeroArcherWallPierce`），MSBuild 摘要重复列出一次（共 4 行）；与 §5 预判一致 |

### 6.2 gold 的 3 个失败 = 测试侧 bug（已修）

根因（operator 已诊断、回执证实）：`Physics2D.Count(collider, ignore)` 只按 `Calls[i].Arrow`（箭一侧）
匹配，而 Program.cs 在三处把**墙碰撞体**传进去 → 恒 0；`CountWall(wall)` 又不过滤 ignore 方向。
受影响共 4 处（含 1 处因此恒真"假通过"的断言），已全部修复：

- `UnityStubs.cs`：`Count` → **`CountArrowPair(arrow, ignore)`**、`CountWall` → **`CountWallPair(wall, ignore)`**；
  名字自带方向语义，XML 注释写明"把墙传进箭侧计数会恒 0"与"墙侧必须同时过滤 true/false"。
- `Program.cs`：
  * `PairsFor` 改用 `CountArrowPair`；
  * 用例 1「true pairs are exactly the wall colliders」→ `CountWallPair(墙, true)`（3 个墙碰撞体各恰 1 次）；
  * 用例 5「apply/restore 单对失败隔离」→ `CountWallPair(墙, true/false)`；原「the failing pair is the
    only missing one」是**恒真假通过**，改为断言该对在 true/false 两个方向都从未被记录
    （`CountWallPair(bad, true/false) == 0`）。

预期：gold 复跑 **ALL PASS: pass=89 fail=0**（断言总数 89 不变）。

### 6.3 missing 的 5 个失败 = 构建/增量卫生问题（非生产违约）

回执首行 `resource embedded: True` 证明这次运行的程序集里仍有 `KingdomEnhancedMod.ArtemisArrow.png`
（大概率是 `dotnet run -p:EmbedArtemis=false` 复用了 gold 步的增量输出而没有真正重建），于是模块走
正常外观路径：上色、挂穿墙、建快照、写日志 → 5 条失败随之而来（"no snapshot built at all" 之所以
通过，是因为首次 spawn 的归还发生在快照建立之前，与资源无关）。

生产代码**没有违反 brief 契约**，按构造即 fail-closed（已静态核对，复跑将实证）：

- `OnSpawn` 中 `if (!EnsureSprite()) return;` 位于分配回执/写外观/**调用 `HeroArcherWallPierce.Apply`
  之前** → 资源缺失 = 不上色、无回执、不挂穿墙、不建快照、无 pierce 日志；
- `ResetArrow` 的 `Restore` 在无快照（`_cacheValid == false`）时立即返回 → 零调用零日志；
- 唯一日志是既有的 1 条 `[HeroArrowArt] ... unavailable` 诊断。

已做测试侧加固（防止"静默跑错程序集"再次产出误导性结论）：

1. `Program.cs`：`--mode=missing` 启动时若检测到本程序集仍嵌入资源 → 打印 `INFRA:` 三行并以
   **退出码 2 硬失败**（归因到构建，不产出假的 5 条失败）；
2. `Tests.csproj`：新增 `EmbedArtemisSummary` 目标（`BeforeTargets="BeforeBuild"`）打印
   `EmbedArtemis=$(EmbedArtemis)`，让构建日志本身成为"属性是否到达 MSBuild"的证据。

**生产代码零改动**（`il2cpp/HeroArcherWallPierce.cs`、`il2cpp/HeroArcherArrowVisuals.cs` 自第一轮 0W0E
构建以来未再改动）→ §0 命令 1/2 的回执仍然有效，**无需重跑**；命令 5 也无需重跑。

### 6.4 修订后的复跑命令（仅命令 3/4；每模式先显式重建，再 `--no-build` 运行，避免增量串扰）

```bash
cd C:/Users/ADMIN/projects/ohmymods/tests/hero-arrow-pierce

# gold：带资源重建后运行
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release -t:Rebuild \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/tests-build-gold.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --no-build -- --mode=gold \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/tests.txt 2>&1

# missing：不带资源重建后运行
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release -t:Rebuild -p:EmbedArtemis=false \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/tests-build-missing.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --no-build -- --mode=missing \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/tests-missing.txt 2>&1
```

预期：

- `tests-build-missing.txt` 含 `EmbedArtemis=false`（属性确实到达 MSBuild）；
- `tests.txt`：`resource embedded: True` + `ALL PASS: pass=89 fail=0`；
- `tests-missing.txt`：`resource embedded: False` + `ALL PASS: pass=13 fail=0`（hook 契约 5 + fail-closed 8）；
- 若 missing 首行仍为 True → **退出码 2 + `INFRA:` 行**：说明 `-p:EmbedArtemis=false` 未生效，
  请把 `tests-build-missing.txt` 回传，按构建日志再定位（届时可能需要清理 `obj/Release` 或调整传参顺序）。

本轮改动仅 `tests/hero-arrow-pierce/**` 与本文档；未 commit/push/deploy。

---

## 7. 第三轮修复：`tests/hero-artemis-arrow` 适配新生产现实（2026-09-18）

背景：该套件直接编译 `il2cpp/HeroArcherArrowVisuals.cs`；第二轮的 0.65 显示缩放与穿墙两处调用点令其
2 条 CS0103 无法编译（`receipts/artemis-suite-after-change.txt` 的 338/453 行）。本轮**只改
`tests/hero-artemis-arrow/**`**，生产文件零改动（与第二轮 0W0E 构建内容逐字节一致）。

### 7.1 改了什么

1) `Tests.csproj`
   - 新增 `<Compile Include="../../il2cpp/HeroArcherWallPierce.cs" />`；
   - 顶部注释补上两个生产文件与穿墙调用序列（BeginShot → ResetArrow → Restore → 原生 body → OnSpawn → Apply → EndShot）。
2) `UnityStubs.cs`（从 `tests/hero-arrow-pierce` 移植最小替身面，全部为"加成员、不改成员"）
   - `Object`：`AllObjects` 场景登记 + `FindObjectsOfType<T>()`（镜像活动对象语义、可注入异常）+ `ResetScene()`（清登记/计数/`DestroyCalls`）；
   - `Collider2D`（`Label` 仅测试用）与 `Physics2D`（`IgnoreCollision` 记录 (箭, 墙, ignore) 全序列；`OnIgnore`/`Throws` 可注入；`CountArrowPair` / `CountWallPair` 两侧计数且命名显式，避免把墙碰撞体传进箭侧计数而恒 0）；
   - 头注释把 Physics2D 记账并入"可观察写入"证据链。
3) `GameStubs.cs`
   - `Arrow` 增 `public Collider2D _collider;`（对应真实 interop `PROP UnityEngine.Collider2D _collider`，第二轮编译门已实证存在）；
   - 追加全局命名空间 `Wall`（仅 `GetComponentsInChildren<T>(bool)` + 枚举异常注入），与真实 Assembly-CSharp 的解析路径一致。
4) `Program.cs`
   - `ResetWorld`：新增 `HeroArcherWallPierce.ResetForTests()` 与 `Physics2D.Reset()`；原 `DestroyCalls = 0` 由 `UnityEngine.Object.ResetScene()` 一并清零（语义不变）；
   - `NewRig` / `NewChildRig` / `Rig.AliasArrow`：补齐 `_collider`（别名共享同一 native 碰撞体引用）；**不加入 `go.Components`**，`NonVisualSignature` 的 `comps=` 与既有断言逐字节不变；
   - 新增 `NewWall(pointerBase, colliderCount)` 夹具（墙 + N 个活动子碰撞体，登记进场景替身）；
   - **被 0.65 取代的断言**（identity 用例改名并更新）：
     `PPU = 32f / HeroArcherArrowVisuals.HeroArrowDisplayScale`（用常量表达式，不重复出现 32 或 49.23 裸魔数），
     新增 `WorldWidth = (30f/32f) * HeroArrowDisplayScale`（0.609375）与 `WorldHeight = (5f/32f) * ...`；
   - 新增 gold 用例「0.65-scaled hero arrow ignores a registered wall; pool reuse hands the pairs back」：
     1 面墙 2 碰撞体 → 英雄箭 true×2（墙侧各恰 1 次）→ `ResetArrow` 复用归还 false×2 + 外观还原。
     让移植的 Physics2D 记录面在 gold 下非空可证（否则记录器只是摆设，且坏替身会被静默吞掉）；
   - 钩子契约：新增 `HeroArcherWallPierce` 无任何 Harmony 特性的断言（该文件现编入本程序集，防止未来偷偷加钩子）；
   - fail-closed 套件：注册 1 面墙（让"不挂穿墙"是非空断言），在三条箭（英雄/后续/非英雄）跑完后断言
     `Physics2D.Calls.Count == 0`、登记墙两碰撞体 true 对为 0、`CachedColliderCount == 0`、日志无 `[HeroArcherWallPierce]`；
   - 新增 `invalid-resource.bin`（89 B 非 PNG 夹具），三/四模式复跑不再依赖外部文件。

### 7.2 为什么这样改

- **语义保持优先**：既有 144 条 gold + 14 条 fail-closed 断言全部原样保留；只更新被新契约（0.65 显示缩放）
  明确取代的两类断言，并新增非空可证的穿墙正/负断言；
- **替身合并零副作用**：`Arrow.NonVisualSignature`、`Sprite.Writes` 记录、receipt 语义、`Object.Destroy`、
  `HeroArcherRuntime`/`ArcherOptionsScope` 缝全部原样；`_collider` 不进入组件计数；
- **三模式语义不变**：资源缺失/非法/尺寸不符时生产在 `if (!EnsureSprite()) return;` 处提前返回，`Apply`
  不会被调用、`Restore` 无快照即早退；本轮把"不挂穿墙、无快照、无 pierce 日志"固化为断言，防回归。

### 7.3 预期复跑（三种模式 + 可选 wrongsize；先 `-t:Rebuild` 再 `--no-build`，避免增量串扰）

```bash
cd C:/Users/ADMIN/projects/ohmymods/tests/hero-artemis-arrow

# gold
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release -t:Rebuild \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-gold-build.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --no-build -- --mode=gold \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-gold.txt 2>&1

# missing
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release -t:Rebuild -p:EmbedArtemis=false \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-missing-build.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --no-build -- --mode=missing \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-missing.txt 2>&1

# invalid（本目录自带非 PNG 夹具）
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release -t:Rebuild -p:ArtemisPng=invalid-resource.bin \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-invalid-build.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --no-build -- --mode=invalid \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-invalid.txt 2>&1

# 可选 wrongsize（用既有 672x192 图集当"尺寸不符"的 PNG）
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release -t:Rebuild -p:ArtemisPng=../../il2cpp/Assets/HeroArcherAtlas.png \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-wrongsize-build.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --no-build -- --mode=wrongsize \
  > ../../docs/project-harness/tasks/hero-arrow-pierce-20260917/receipts/artemis-wrongsize.txt 2>&1
```

预期：

- **gold**：`resource embedded: True` → **`ALL PASS: pass=153 fail=0`**
  （基线 144 + 钩子 1 + 世界宽高 2 + 新穿墙用例 6）；
- **missing**：`resource embedded: False` → **`ALL PASS: pass=19 fail=0`**
  （基线 14 + 钩子 1 + fail-closed 穿墙负断言 4）；
- **invalid**：`resource embedded: True` → **`ALL PASS: pass=19 fail=0`**（非 PNG 在 `LoadImage` 处 fail-closed）；
- **wrongsize（可选）**：`ALL PASS: pass=19 fail=0`（尺寸校验处 fail-closed）。

若任一模式未达上述数字或出现 FAIL，请回传对应 receipt（尤其 `artemis-*-build.txt` 里的
`EmbedArtemis=`/`ArtemisPng` 生效证据）。未 commit/push/deploy；本轮改动仅 `tests/hero-artemis-arrow/**` 与本文档。


