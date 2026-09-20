# 视觉 slice 结果：拉弓/释放可见性 + 朝向/高度证据（含 operator 授权的事件锚定释放）

2026-09-15 worker（deepseek/deepseek-v4-flash max，本机）。只改允许文件；未构建、未部署、未启动游戏、未提交、未改 PNG。
证据分三类：**实测**（日志/资源解码）、**参考代码**（game-source 2.1）、**推断**（标注）。

本轮按 operator 扩展授权重做两处：
(A) Prepare 窗口改为 **operator 桥旁路记录**（不再自测/种子/钳制）；
(B) 新增 **事件锚定的释放表现**（自有帧 26..30，0.18s 有界），覆盖 perfect 弹跳过原生 Shoot 状态的观感；
(C) 访问跟踪降级为**纯诊断**，并按 reviewer 意见修正放箭证据归属与"访问时长含尾巴"的语义。

## 0. 改动范围（本轮）

| 文件 | 改动 |
|---|---|
| `il2cpp/HeroArcherNativePose.cs` | 保留「clip 时间 / 窗口」归一与 `FrameFor/PreparePhase`；**删除**种子 `0.25`、钳制 `[0.12,0.60]` 与 `ClampPrepareWindow`（窗口只由调用方给出） |
| `il2cpp/HeroArcherVisuals.cs` | 新增 `internal static void RecordPrepareWindow(Archer archer, float seconds)`；`PendingWindow`/`ActiveWindow`（进入/回绕锁入）；新增释放表现（`ReleasePresentationSeconds=0.18s`、`ReleaseFrameFor`、`UpdateRelease`）；访问跟踪改为纯诊断（放箭计数+放箭时刻、同状态回绕检测、帧区间取原生帧）；转场行新增 `nat=`/`win=` 并把 `pose=` 改为**显示帧** |
| `tests/hero-native-pose/**` | 替身 `AnimatorStateInfo.length`；窗口作为纯输入的断言；删除已移除 API 的断言 |
| `tests/hero-native-visuals/**` | `SetState` 支持 clip 长度、`OwnOf` 双英雄定位；新增 pending/active 窗口测试与释放表现测试；保留诊断有界性测试 |
| 本文件 | 交付证据与未决项 |

未改：Runtime/Range/Movement、全局缩放、Shop/Cloth/Priority、插件与 PNG、harness。**不新增 getter hook**；窗口记录由 operator 在既有 cadence prefix 内调用。

## 1. 拉弓看不到（已定位；本轮把窗口来源换成桥记录）

证据链（与上轮相同，未变）：

1. 英雄用**希腊覆盖 clip**（实测）：Walk 的 `nt` 增速 = 2.813/4.212s → 1.497s ≈ `archer_walk_greece`（1.5s）。
   （`hero-shop-20260915/receipts/stability/before.log:170,174,178,193,211`）
2. 希腊 `archer_shoot_prep_greece` clip = **2.1667s**（resources.assets:702，26 键/12fps，2 帧 prep 后是旋转）；
   基础弓手 0.5s（path 887）；Shoot：希腊 0.3s（700）、基础 0.625s（884）。（`clip-timelines.json`）
3. 原生 Prepare 状态只播 `shootPrepTime` 那么长的前缀（实测 0.231s，`before.log:218-219`；参考代码
   `Archer.cs:1022,1026,1027,1031,1037`）。→ 旧全局相位归一在退出点 `nt≈0.107` → 只可能显示 22 号帧。
4. 因此"看不到拉弓"= 4 张拉弓帧的后 3 张永远不会出现（日志里 Prepare 行恒为 `pose=22`，
   `before.log:179,218,221,229`）。

本轮实现：Prepare 相位 = `clip 时间(normalizedTime × clipLength) / 有效窗口`；
**窗口只来自 operator 桥**：`PatchArcher_Options` 在既有 Priority.Last cadence prefix 内、借用之后调用
`HeroArcherVisuals.RecordPrepareWindow(archer, 该步原生的临时 shootPrepTime)` —— 这正是原生同一次
MoveNext 读到的值（rate/DL 效果已含），因此不需要读字段、不需要任何 getter hook。
`pending` 只记录精确已知的同一视觉（GO id + pointer + 当前 life 全同；非有限/非正忽略），
`active` 在 **Prepare 进入或同状态回绕**时锁入；窗口缺失 → 确定性回退原生相位（行为与旧实现一致）。
窗口在采样之后锁入，故**进入帧本身仍用旧窗口**（进入帧 `nt≈0`，两种映射都给 22 号帧，实测不可见差异）。
池/换世界重建后 `pending/active` 归零；locomotion/防守/撤退**永远只跟原生相位**（无自有时钟、不按
瞬时 `Animator.speed` 做除法；`ctMax/len` 已能暴露速度异常，故不再单独记 speed）。

## 2. 释放表现（operator 授权的新增）

* **触发**：`NotifyRelease`（原生 `FireArrowInternal` 每发一次；散射补箭同帧多发）。
  同帧重复调用**不重启**（`ReleaseStartFrame == FrameCount()`），隔帧的真实放箭**重启**；
  不存在敌人事件外推：只有已登记自有视觉的英雄 actor 会被驱动。
* **表现**：`ReleasePresentationSeconds = 0.18f`，把自有帧 26..30 按 `elapsed/duration` 均分播放
  （26=拉满、30=收势，即"释放→回位"），只覆盖**自有 sprite**；不写 Animator、不动位置/朝向、
  不推进布料、不推动 locomotion。
* **取消**（立即，不给自由时钟）：Walk / Run（不冻结移动）、Shoot（原生自己会播 26..30，优先原生）、
  Unknown（含 animator 停用/非法/回退）、**新的 Prepare 进入或同状态回绕**、时长走完；
  保留情形：放箭当帧原生可能仍在 Prepare 尾巴、以及 perfect 弹的落点 Stand。
* **暂停**：只用 `Time.deltaTime`，`dt=0` 时释放冻结、不推进也不超时。
* 两个英雄各自独立（状态在 `VisualState`）；关闭/池复用/移除时随状态一起丢弃，不残留。

## 3. perfect 弹跳过 Shoot 状态（原生行为，已用释放表现覆盖观感）

* 资源实测（`controllers.json`，resources.assets:6128）Prepare 的转换顺序：
  ①`Shoot` 触发器 → ②`Prepare==false` → Stand → ③`ShootPerfect` 触发器 → ④`Speed>0.005` → Stand。
* 参考代码（`Archer.cs:1028,1037`）：`FireArrow` 先设触发器，同一协程步紧接着在最后一发设
  `Prepare=false`；同一次 Animator 求值里 ② 在 ③ 之前命中 → **进 Stand，不进 Shoot**，触发器残留。
* 实测吻合：`before.log:217-220` 为 Prepare→Stand（`shot=1`，无 Shoot 行）；`before.log:217-223` 的另一串为
  Prepare→Shoot→Stand。触发条件 `Deity.Archer==Activated && Random<perfectArrowProbability`
  （2.1 默认 0.5；2.4 实际值与用户存档状态未核实，需以实机 `next=` 统计为准）。
* 处理：原生行为不改（无 animator 写入）；观感由 §2 的释放表现补齐。诊断行 `next=Stand` 可直接统计比例。

## 4. 素材缺口（只报告；PNG 由 Main 处理）

`il2cpp/Assets/HeroArcherAtlas.png`（384×128，6319B）与 `artifacts/hero-archer/20260915-scarf/HeroArcherAtlas.png`
同尺寸；参考快照 `atlas.json` 的 sha256 显示 Shoot 槽是 Prepare 的逆序复用（26=25、27=24、28=23、29=22、30=22），
即当前素材没有独立"箭已离弦"帧。若需要释放瞬间更清晰，可替换 26 号槽（当前 = 拉满姿势 25）
或 30 号重复帧——**本轮未改 PNG、未调用 imagegen**。

## 5. 夜晚守墙朝向 / 邻近角色高度（无本 slice 的静态错误；已补证据字段）

* 朝向：`Mover` 用根 `localScale.x` 符号表示朝向（`Mover.cs:219/224/237/291/315/407`），弓手面向射击目标
  （`Archer.cs:1010-1012`）；原生素材朝右（`reference/contact-sheet.png` 复核）。自有 sprite 是
  **原生 renderer 所在 Transform 的子物体**（2.4 prefab 实测：Archer(19817) 的 SpriteRenderer 68174 在根上），
  每帧复制 `flipX` 后才显示 → 自有朝向 ≡ 原生朝向，结构上不可能双翻。
  新增只读诊断 `face=<父链符号>/<自有符号>f<flip>` 与 `y=<世界y>`（转场行，有界 120）。
* 高度：本 slice 不写自有 body 根的 y（只写世界 z 做置前）；pivot (31,2) 与格底对齐。
  可疑观感来源（推断）：身份抖动导致视觉被拆除/重建——旧日志中 `[HeroArcher] selected …` 与 `src=apply`
  在 t=19.7/23.8/28.9/33.1/36.9/70.2/88.3 成对出现，且重建时 `forceoff=0`（原生可见）。
  新增 `[HeroArcherVisualLife] attach/detach reason=invalid|scale|plane|clear|remove|replace`（有界 60）与其配对，
  根因在 Runtime（本 worker 不可改）。

## 6. 诊断（本轮按 reviewer 意见修正）

* `[HeroArcherPoseVisit]`（≤60 行/自 Clear 起，动作结束时一行）：
  `dur`（访问时长，**含放箭后的状态尾巴** —— 明确只是证据，不再是窗口）、`ctMax/len`、
  `frames`（**原生帧**区间，不含释放覆盖）、`win`（当前 active 窗口）、`next`（离开后的动作，
  `next=Stand` 即原生跳过了 Shoot）、`shots=N@ct`（放箭次数与**放箭时刻**的 clip 时间）。
* 放箭证据归属：`NotifyRelease` 在**事件发生当时**把计数/时刻记到「那时打开的访问」上
  （reviewer 指出的"随后开启新访问吞掉 VisitShot"与"同帧 Notify 后重置"路径已消除）；
  访问时长不再参与窗口计算，`Prepare` 同状态回绕会关闭旧访问并开新访问。
* 转场行新增 `nat=`（原生采样帧）与 `win=`；`pose=` 改为**显示帧**（可能被释放表现覆盖），
  因此两者可区分。`[HeroArcherVisualLife]` 有界 60 行。所有行都事件触发，不逐帧。

## 7. 测试（替身层，非实机）

* `tests/hero-native-pose`：窗口作为纯输入（0.25→23 / 0.12→24）、希腊退出点 0.107→25、
  旧算法对照（无窗口 → 22）、基础 clip、窗口外钳制、循环不受影响、非法长度/窗口/溢出回退、
  桥带窗口/无窗口/`length` 非法三条路径。
* `tests/hero-native-visuals`：
  - 窗口：缺失回退 22；记录 0.2 → 进入帧仍旧窗口、次帧 24；进行中不被 pending 0.5 改动；
    下一次进入用新 pending（22）；同状态回绕重锁（回绕帧 22 → 次帧 24）；
    非法/陌生/life 不符忽略；池复用重建后不沿用（22）。
  - 释放：perfect→Stand 时 26,26,27,27,28,28,29,29,30,30 → 第 11 帧回原生 0（0.18s 有界）；
    同帧两次 `NotifyRelease` 与单次进度完全一致；隔帧新放箭重启（26）；
    原生 Shoot 优先（nt 0.9 → 30 而不是 26）且不重播；
    Walk 立即取消（11）；新 Prepare 立即取消（22）；暂停冻结（30 帧 dt=0 仍 26）后恢复推进；
    关闭后不残留；两个英雄独立（A=26 与 B=11 同帧）。
  - 诊断：访问行/生命周期行事件触发与 60 行上限（沿用上轮断言）。

operator 运行（本 worker 无 shell 权限，未执行）：
```
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-native-pose/Tests.csproj
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/hero-native-visuals/Tests.csproj
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=      # 生产编译（新 API：AnimatorStateInfo.length、RecordPrepareWindow）
```
接线（Main 已落地，签名与本模块一致，测试不链接 cadence 模块）：
`KingdomEnhancedPlugin.cs:57` → `PatchArcher_Options.PrepareWindowObserver = HeroArcherVisuals.RecordPrepareWindow;`
`PatchArcher_Options.cs:110` `PublishPrepareWindow(Archer._Shoot_d__225 iterator)` 读取 `iterator.__4__this.shootPrepTime`
并在 `Archer_ShootCoroutine_OptionsCadence_Patch.Prefix`（`Priority.Last`、借用之后，`PatchArcher_Options.cs:1237`）
以 `Action<Archer, float>` 调用一次；observer 内部 try/catch，绝不改变协程/付款/玩法行为。
因此本模块收到的就是原生同一步 MoveNext 读到的临时值（rate/DL 已含），无 getter hook。

## 8. 未决项（不置 done）

1. 实机：拉弓 4 帧是否可见、与放箭同帧对齐；释放表现观感与 0.18s 是否合适；`win=` 实测值。
2. perfect 弹（`next=Stand`）比例与用户存档 `perfectArrowProbability` 实测。
3. 夜晚守墙朝向：需用户原话/画面 + `face=`/`y=` 日志；若是原生 AI 朝向，则不属于本 slice。
4. 邻近角色高度：需 attach/detach 节奏与用户描述对齐；身份抖动根因在 Runtime。
5. 联机（英雄在线仍关闭）、跨世界/性别、受击/石化/登船等未知状态回退（沿用原策略）。
