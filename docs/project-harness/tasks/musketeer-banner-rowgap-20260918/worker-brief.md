# worker-brief：举旗编队火枪手行紧接弓箭手（中插+Squire行槽，2026-09-18）

角色：worker（可写，范围严格受限）。cwd：`C:/Users/ADMIN/projects/ohmymods`。
本方案已经 operator 对抗审查逐场景核算并定稿（回执 receipts/decision-review-record.md），按任务书执行，**不要改方案几何**。

## 用户报告

举旗（2 金币）编队=4重装长矛兵+4弓箭手+最多4火枪手。火枪手没有紧接在弓箭手后面，中间的空隙"像又站了四位弓箭手"。要求：直接复用弓箭手队列逻辑接上队伍，火枪手↔弓箭手之间不超过正常队列间隙。

## 诊断（已核实，作为背景）

- 原生 `Formation.GetXPosForIndex`（game-source/Assembly-CSharp-2.1.0/Formation.cs:319-334）：位置=按槽位自身类型查 `UnitSpacing` 累加；`units[i]!=null || type==Gap` 才计步（Gap 恒占位、空非Gap槽压实）。
- 基线编队实测（tasks/musketeer-banner-deer-20260917/geometry-notes.md）：`[FleetBoat,Gap,Gap,Archer×4,Gap,Pikemen×4]`，spacing：Archer=.21875、Gap=.34375、FleetBoat=0（≥2船时 mod 覆写 1.0）。
- 现行实现把 4 个 Gap 槽行**前置到编队最尾**，行距取 Gap 值 → 火枪手↔弓箭手间隔 = 行边界 .34375 + 船块（0/1船≈0；2+船=1.0×船数）+ 原生 2 个尾部 Gap（.6875）≈ **4.7×弓手步**（0船5.7×、2船13.9×）。

## 修复方案（定稿，逐条实现）

1. **中插**：`TryCompose`（il2cpp/Patch_MusketeerFormation.cs:290-349）把行插到**第一个 Archer 槽之前**（多船展开后计算绝对 index），不删任何基线槽。新序：`[FleetBoat,(船槽),Gap,Gap, 行×4, Archer×4, Gap, Pikemen×4]`。
2. **行槽类型 = `Formation.UnitTypes.Squire`**（对抗审查已验证零认领风险：七个 IFormationUnit 实现者的类型表都不含 Squire、Recruit switch 无分支、无 UniqueType 查询者）。
3. **行距**：`TryExpand`（il2cpp/PatchWorld_FleetBoatFormation.cs:380-432）的 rowSpacing 改查 `BaselineSpacing[(int)Formation.UnitTypes.Archer]`，并增写 `spacing[(int)Squire]=rowSpacing`（与现有 :406-407 船间距覆写同层）；`rowUsable` 门改验 Archer spacing 可用性；startOffset 补偿式（:414）结构不变（offset=基线−rowLength×rowSpacing）。
4. **语义注释**：空 Squire 行槽不占位=原生压实语义（Formation.cs:324 同源）；未满员时队列向船侧收紧、贴弓手侧优先占座（NextFreeMusketeerSlot 从高位填已保证）。把这些写进代码注释。
5. **不动**：TryPlanDirected、TryRestoreBaseline（整表还原 spacing 已覆盖 Squire 覆写）、UpdatePhalanx、维护/封口逻辑。
6. 预期几何（审查已核算，作为测试基准）：满员 4 火枪手时弓手 4 槽/gap7/长矛 4 槽坐标=基线值（.6875/.90625/1.125/1.34375/1.5625/1.90625…）；**火枪手↔弓手1 边界恒=1×.21875，对 boatCount∈{0,1,2,4} 与任意占用成立**；fleet 块整体刚体平移 −4×.21875（内部相对布局不变）。

## 测试（tests/musketeer-formation/，按审查 E 清单）

- 重写 Composition 断言：行位置（中插 index）、行槽类型 Squire、多船 index 平移、零船 case（现行 :46 附近"fleet 槽转 Gap"逻辑保持——注意行插点在 Gap 之后不影响它）。
- 新增核心断言：边界=1×ArcherStep 对 boatCount∈{0,1,2,4} 恒成立（真实 spacing 数值、左右镜像）；满员时弓手/长矛逐槽=基线值；未满员 n 名时弓手1=基线−(4−n)×.21875 且占座贴弓手侧；spacing[Squire] 写入与还原逐项断言；fleet 块平移 −.875 显式断言。
- e2e PipelineTests：25 case 契约更新（坐标不动改为"弓手侧精确+fleet块平移"新契约）；directed 快照含 Squire 行槽。
- 跑法沿用工程既有方式；输出留回执。

## 构建验证（不部署）

`il2cpp/`：`C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=C:/Users/ADMIN/projects/ohmymods/docs/project-harness/tasks/musketeer-banner-rowgap-20260918/receipts/build-stage` → 0W0E。

## allowlist

`il2cpp/Patch_MusketeerFormation.cs`、`il2cpp/PatchWorld_FleetBoatFormation.cs`、`tests/musketeer-formation/**`、`docs/project-harness/tasks/musketeer-banner-rowgap-20260918/**`。

## 禁止

改其他任何文件（尤其未发布候选脏文件如 HeroArcherWallPierce.cs / MusketeerShop.cs）、commit/push、碰游戏/E盘/存档、启动游戏。

## 交付

worker-result.md：改动清单、新旧几何对照（含数字）、测试结果、已知边界（fleet 块内移 .875、未满员收紧语义、实机待验项）。
