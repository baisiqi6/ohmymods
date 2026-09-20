# worker-result：举旗编队火枪手行紧接弓箭手（中插 + Squire 行槽，2026-09-19）

状态：实现与测试改写完成；operator 首跑结果 = e2e `passed=23 failed=0`、unit `1 FAIL`（已定位为测试镜像 typo 并修复，见 §0a）、il2cpp/interop 构建待与修复后复跑一并执行。未 commit/push、未部署、未碰游戏/E 盘/存档。

## §0a 修复轮 1（2026-09-19，operator 首跑后）

- 症状：unit FAIL 1——`any row occupancy keeps the nearest musketeer one row step from the bow line: one row step for any occupancy (left=False, boats=0, subset=1) [expected=0.21875 actual=0.875]`。
- 根因（测试镜像 bug，生产无关）：`LayoutTests.cs` 任意占用用例的清零写成了 `originals[i] = false`（i=0..3），实际清掉的是合成数组下标 0..3（0 船船位转 Gap、两个原生 Gap、行座 `seats[0]`），行座 `seats[1..3]` 仍是 `true` 被当作占用；`subset=1` 再把 `seats[0]` 置 true 后四行座全部计步 → 距离 4×.21875 = 0.875，与实测吻合。
- 修法（仅测试）：`originals[seats[i]] = false`（只清四个行座）。生产文件零改动，几何契约"边界恒 1 步"不变；手算复核 boats=0/subset=1：archer1=.375、nearest=.15625、差=.21875 ✓；同轮 e2e 23/0（真实生产数组）亦印证生产侧无违约。
- 复核：ast-grep 全文件仍解析干净；`grep` 确认无同类下标误写（case 2 用 `originalsWith[seats[i]]` ✓）。
- 复跑预期：unit 12 case 全 PASS；e2e 仍 23/0；两项构建按 §0 原始预期。

## §0 Operator 运行命令（按序，输出即回执）

```sh
cd C:/Users/ADMIN/projects/ohmymods
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project tests/musketeer-formation/MusketeerFormationTests.csproj \
  > docs/project-harness/tasks/musketeer-banner-rowgap-20260918/receipts/unit-tests.txt 2>&1
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release --project tests/musketeer-formation/e2e/FormationPipelineTests.csproj \
  > docs/project-harness/tasks/musketeer-banner-rowgap-20260918/receipts/e2e-tests.txt 2>&1
cd il2cpp && C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug \
  -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/musketeer-banner-rowgap-20260918/receipts/build-stage \
  > ../docs/project-harness/tasks/musketeer-banner-rowgap-20260918/receipts/build-debug.txt 2>&1
cd .. && C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release \
  tests/musketeer-formation/interop-check/MusketeerFormationInteropCheck.csproj \
  > docs/project-harness/tasks/musketeer-banner-rowgap-20260918/receipts/interop-check.txt 2>&1
```

预期：unit 末行 `passed=N failed=0`（0 fail）；e2e 末行 `passed=23 failed=0`；两项构建 `0 个警告 / 0 个错误`。
任何 `FAIL`/`error`/警告请原样回传，不做就地修复。

## 改动清单（严格按 worker-brief，几何未改）

| 文件 | 关键行为 |
|---|---|
| `il2cpp/Patch_MusketeerFormation.cs` | `TryCompose` 行槽从"数组头 Gap×4"改为**第一个 Archer 槽之前中插 ×4**，槽型 `Formation.UnitTypes.Squire`，不删任何基线槽；零船船位→Gap 转换保持在行插点之前、互不影响；fleet 展开/索引/`TryPlanDirected`/调用约定不变。类注释与方法注释同步新语义。 |
| `il2cpp/PatchWorld_FleetBoatFormation.cs` | `TryExpand`：`rowSpacing = BaselineSpacing[(int)Archer]`（`rowUsable` 同步改验 Archer 条目）；新增 `if (rowLength > 0) spacing[(int)Squire] = rowSpacing`（与船间距覆写同层）；`startOffset` 补偿式结构不变；`NextFreeMusketeerSlot` 注释补"空 Squire 槽不占位→向船侧收紧"。 |
| `tests/musketeer-formation/LayoutTests.cs` | Composition/Placement/DirectedPlan 全部迁移；新增边界/压缩/任意占用用例（见 §2）。 |
| `tests/musketeer-formation/e2e/PipelineTests.cs` | 座位图工具与 22 个既有用例逐一迁移 + 新增未满员压缩用例；满员契约改为"弓手侧精确 + fleet 块 −.875 + 边界 1 步"；Squire spacing 写入/还原断言。 |
| `tests/musketeer-formation/interop-check/SignatureProbe.cs`（+ csproj 注释） | 新增 `Formation.UnitTypes.Squire` 与 `formation.UnitSpacing[(int)Squire]` 索引写探针，真实 2.4 interop 编译门覆盖本次新增写路径。 |

## §1 新旧几何对照（真实 2.4 spacing：Archer=.21875、Gap=.34375、FleetBoat=0，≥2 船覆写 1.0）

| 量 | 旧（行前置，Gap 槽，行距=Gap=.34375） | 新（中插，Squire 槽，行距=Archer=.21875） |
|---|---|---|
| 合成顺序 | `[行Gap×4, 船块, Gap, Gap, 弓×4, Gap, 矛×4]` | `[船块, Gap, Gap, 行Squire×4, 弓×4, Gap, 矛×4]` |
| 行槽索引 | 0..3 | `max(boats,1)+2 .. +5` |
| 火枪手↔弓手1 | .34375 + 船块 + .6875 → 1 船 1.03125（4.71×弓手步）/ 0 船 5.7× / 2 船 13.9× | **恒 = 1×.21875**（boatCount ∈ {0,1,2,4}、任意占用、左右镜像） |
| 满员时弓手/矛坐标（1 船） | 整体被推后一行+船块 | **= 基线**：弓手 .6875/.90625/1.125/1.34375；gap 1.5625；矛 1.90625… |
| 未满员 n | 无此语义（行恒占位） | 弓手1 = 基线 − (4−n)×.21875；空座留船侧（贴弓手侧优先占座，`NextFreeMusketeerSlot` 从高位填） |
| fleet 块（船位+2 Gap） | 原位 | 整体刚体平移 **−4×.21875 = −.875**（任意船数/占用；同船数"无行"布局对比；内部相对布局不变） |
| 0 船 | 船位转 Gap | 同前（转换在行插点之前）；行插点仍在两个原生 Gap 之后 |

满员几何推导（1 船，startOffset 0 → −.875）：弓手1 = −.875 + (0 + .34375×2) + 4×.21875 = .6875 = 基线；
边界 = 弓手1 − 行末座 = 1×.21875；fleet 块 = 基线 − .875。逐项与审查核算一致。

## §2 测试更新（已写入，待 §0 运行）

unit（`LayoutTests`，共 12 case）：
- Composition 7 例：中插索引/Squire 槽型（简基线 3 船；真实 2.4 基线 2 船）、0 船船位转 Gap、feature off、无弓手槽、非法基线 fail-closed、不改输入数组；
- Placement 3 例：(a) 满员=弓手侧精确 + fleet −.875 + 边界 1 步 + 同行等距； (b) 未满员 n∈0..4 压缩 (4−n)×.21875 + 边界 1 步 + 相邻占用 1 步； (c) 全部 15 种非空子集：最近火枪手↔弓手1 恒 1 步、位移只数占用数（boatCount ∈ {0,1,2,4}、左右镜像、真实 spacing；`ExpandedSpacing` 镜像 TryExpand 的覆写）；
- DirectedPlan 2 例：目标暂寄 Archer、空行槽保持 Squire、空 AnyShieldedUnit 关 Gap、占用槽保型、输入不变。

e2e（`PipelineTests`，22 例迁移 + 1 例新增 = 23）：
- 新增满员契约例：弓手侧逐槽=无行基线；fleet 三槽 −.875；边界 1 步；`Spacing[Archer]` 与 `UnitSpacing[Squire]` 逐项断言；
- 新增未满员例：n=0..4 的弓手1 位移与边界；
- furl/异常回退/再举旗：`UnitSpacing[Squire]` 写回基线与重覆写、`FleetBoat` 多船间距还原/重写；
- 座位迁移：所有 `CountOccupied(0,4)`/`units[0..3]` 改为真实行座 `RowStart/RowSeat`（0 船行座 3..6，1 船 3..6，2 船 4..7）；
- 脚本化尝试号逐次核算：type-write 用例保持 `9/12`（索引 8 语义不变）；dirty 用例 `FailRestoreFromAttempt 4→7`（新目标座第 6 次写应用、第 7 次抛出，残留=行座第 3 个）。

## §3 静态核验（已做，不替代编译）

- 两个生产文件改动区逐行复核；新 `TryCompose` 的 `write == length` fail-closed 保持（rowLength>0 必有弓手槽，`rowWritten` 防御性保留）。
- `grep` 证据：`UnitSpacing` 仅 `PatchWorld_FleetBoatFormation` 读写；`Formation.UnitTypes.Squire` 无其他消费者（KnightIdentity 的 "Squire" 是 tag，无关）。
- `tests/fleet-greek-squads` 的 source-extractor（fleet 模式）切片终点标记为 `    private static bool TryExpand(`、另取 Finalizer 块——两者均未改动，该套件不受影响、无需改其文件；其用例只测 FleetGreekSquads 与 TryBuildCandidates/Finalizer。
- **ast-grep 语法核验（替代不了 dotnet 编译，仅作区域级语法检查）**：
  - `il2cpp/Patch_MusketeerFormation.cs`、`il2cpp/PatchWorld_FleetBoatFormation.cs`、`tests/musketeer-formation/LayoutTests.cs` 全文件解析干净；
  - `tests/musketeer-formation/e2e/PipelineTests.cs` 被 ast-grep 报 parse error，已定位为**预先存在**的 `Archer partial = Fixture.AddMusketeer(1f);`（Ghosts 用例，行 ~375）：`partial` 是 C# 上下文关键字，作标识符完全合法（前任务 dotnet 编译通过即证），仅 tree-sitter 语法树不识别。逐区域重构探针验证：helpers/Composition/FeatureSwitch/Recruitment/TransactionFailures/Lifecycle/AuthorityAndScene 全干净，Ghosts 重命名该标识符后亦干净 ⇒ 报错与本轮改动无关，dotnet 编译不受影响。
  - 诊断用临时探针文件在 `receipts/parse-probe-*.cs`（未纳入任何 csproj，可删）。
- 未运行游戏/构建；未 commit/push；未动 E 盘与用户存档。

## §4 已知边界

- fleet 块 −.875 刚体位移（含 0 船时被转 Gap 的船位）；船与船相对布局不变。
- 未满员时弓手/长矛/船整段向船侧收紧 (4−n)×.21875，空座恒贴船侧——即"复用弓箭手队列逻辑"的语义。
- 0 船时船位转 Gap 使其下游相对"原生无 mod"多计 1 个 Gap 步（.34375）：旧版既有行为，本次前后一致；测试以同船数"无行"布局为基线口径。
- dirty 临时类型/回执/守卫语义未改；仅目标槽从 Gap 槽变为 Squire 槽（e2e 已覆盖 dirty 残留=行座、普通弓手被拒、回执还原为 Squire）。
- 实机观感（边界是否自然、密集队形）、4 船、认领窗口交互、联机：未启动游戏、未部署，保持待验；expected geometry 来自真实资源 spacing 与原生 `GetXPosForIndex` 语义的逐次推演 + 测试基准。
