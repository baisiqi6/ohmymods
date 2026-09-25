# 交接文档：武士燕返（编舞式往返）— 2026-09-25

> 写给接手的 operator/agent。读完本文即可开工，不需要回溯历史会话。
> 仓库 `C:/Users/ADMIN/projects/ohmymods`，发布线分支 `release/v9.5.13`（当前 head **67d39b9**）。
> 本任务目录：`docs/project-harness/tasks/samurai-slash-guard-20260925/`（brief.md=设计书 v2、events.md=全程事件、worker-brief.md、omp-worker-run.log）。

## 0. 一页摘要

**功能**：武士骑士（风格 2）的攻击=燕返：CD 好了 + 1.5~8.2 格内有敌人 → 出刀冲刺固定 7 格（路上 HitScan 砍到谁算谁）→ 转身从第 0 帧重放反斩 → 冲回出发点。全程无敌+八道白色定格残影（当前临时 2 秒便于观察）。**目标无关**：不追踪具体敌人（用户终裁，2026-09-25）。

**当前状态**：编舞式重构已合并（PR#68），候选 `97030311 / build=9.14.24-choreo-20260925` 已装 E 盘。实机（09-25 10:05 会话）验证：**转身与回家目标已正常**（多次 roundtrip-turn 朝家正确），但行程仍被中途掐断——**新根因已实锤未修：`isRetreating`**（见 §4-C）。残影系统各层均正常（材质无降级、8 槽、2s、采样充足）但用户仍目视不可见（见 §6）。

**接手第一件事**：按 §5 处方修 isRetreating（P0），重建候选装 E 盘实机验证两腿完整+残影。

## 1. 环境与硬规则（必守）

- **架构**：IL2CPP 2.4.0 + BepInEx 6 是唯一主线（`il2cpp/` 目录，.NET 8 / Il2CppInterop / HarmonyX，现代 C#）。仓库根的 Mono 源码是冻结历史线，不碰。
- **构建**：`cd il2cpp && C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=`（纯验证编译用该开关；去掉开关会把产物复制到开发环境）。
- **测试**（各自 `dotnet run -c Release`，当前全绿基线）：tests/samurai-motion **198/0**、samurai-visuals **17/0**、samurai-night-formation **24/0**、archer-night-band **32/0**、samurai-retreat **43/0**、samurai-diagnostics **9/0**。
- **部署边界**：只写独立测试副本 `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll`；`D:/Steam/...` 禁止；装前备份旧 DLL 为 .bak；游戏运行中**不要装**（Windows 不锁 DLL，但新 DLL 下次启动才生效——装完必须让用户开游戏后用日志横幅 `build=...` 验证真装上了）。
- **日志**：`<测试副本>/BepInEx/LogOutput.log`；玩家存档 `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/` 装前后不得触碰。
- **流程纪律（用户强约束）**：实质决策（修复方案/契约/验收裁决）执行前必须派对抗审查（GLM 5.3 max 强度 subagent，三问：是否正确/是否最优/是否引入新 bug），回执落 events.md；实现派 worker（北京时段路由：14-18 内置 subagent，夜间/周末 OMP `deepseek-v4-flash` thinking=max）；修正后复审；无记录视为未审查。未获用户明确授权不 commit/push（PR 合并除外——用户已默认 PR 流程）。
- **工作树**：`C:/Users/ADMIN/projects/ohmymods-wt-paydiag` 分支 `win/samurai-choreo`（=release head + 未提交的 `PatchDiag_*.cs`×3 随候选诊断器与 `KingdomEnhancedPlugin.cs` 构建戳，这些不提交）。新工作从 `origin/release/v9.5.13` 拉新分支。

## 2. 功能设计（用户终裁 2026-09-25）

用户原话要义："固定播放动画，冲刺过去，留下残影又冲刺回来，CD 好了之后范围内有敌人又执行这一套，完全不需要判定敌人到底是哪个最近、哪个死了哪个没死。"

- **触发**（`StartChoreo`，Knight.Update postfix Tick 内）：`Time.time>=NextAttack` 且扫到敌且 `1.5≤|dx|≤7+1.2` → 只取 `side=Sign(dx)` 与 `homeX=当前x`，不保留敌人引用。成功 `NextAttack=+3s`，落空 `+0.2s`。
- **两腿**（`ChoreoRoutine` 单协程）：出腿 `SetGoal(homeX+side×7, DashSpeed)`+PowerSlash 触发；到点 |x−goal|<0.25 或 1.2s 超时；每 0.1s HitScan（逐敌去重/腿）；每 0.3s 重申目标（对抗原生改写）。转身 `TurnChoreo`：ResetTrigger→`Play(SlashHash,0,0)`（无捕获回退 SetTrigger）→行进向 facing+SetDirection+localScale y 修复→SetGoal(homeX)→入帧 HitScan。回腿同款循环。
- **效果**：出程快照 invulnerable/trail 旧值并生效（无敌全程、trail 钉长），终幕 `Finale` 按值纪律归还+token End+值判 Stop+卡姿捕获网锚点。
- **旗标**（`ActorState.Choreo`）：四消费方=ShouldSlash 前缀压制（防原生 Slash 的 Pause）/HasActiveMotion（夜间列队不抢目标）/Tick 防重入/BeforeNativeUpdate 夜墙队列门。三路收尾（协程出口/OnDisable/3s Tick 硬上限）+启动 try/catch——封"永久无敌"类。
- **残影**：`SamuraiDashVisuals`，8 槽 recipe C（Sprites/Default+白），alpha 阶梯 max(.10, .55−.0625×rank)，Lifetime=2f（临时，用户要观察）。

## 3. 生产代码地图

`il2cpp/PatchRoles_SamuraiPowerDash.cs`（主战场，~1430 行）：
- `Eligible` **:173-191**（资格门——**当前根因所在**，`isRetreating` 在 :181）
- Tick 骨架 :855+（`if(Choreo){3s 硬上限;return;} if(Motion){Return/Walk 阶梯;return;}` → 触发段）
- `StartChoreo` :1000+ / `ChoreoRoutine` :1040+（两腿循环）/ `TurnChoreo` :1113-1147 / `Finale` :1180-1202
- `SuppressesSlash`（ShouldSlash 前缀）:993,:1415+；`HasActiveMotion` :1372；BeforeNativeUpdate 门 :207-213
- `IsReturning` :1289+（Return 族挥砍压制，独立保留）
`il2cpp/SamuraiDashVisuals.cs`：`Lifetime` :13、8 槽 Build、GhostOpacity、recipe C 三级 shader 降级链（Sprites/Default→Unlit/Texture→源克隆，每级 LogOnce）
`il2cpp/PatchRoles_SamuraiNightFormation.cs`（HasActiveMotion 消费方，勿动其接口）
测试：`tests/samurai-motion/`（NativeSlashAttempt 桩 :45-48 反射调真前缀=根因判别器模式，值得复用）

## 4. 三个已实锤的根因链（按时间序，前两个已修）

**A.（已修，PR#68）原生 Slash 的 Mover.Pause 杀租约**：旧目标耦合租约时代，`Knight.Update:417 if(ShouldSlash()) StartCoroutine(Slash())`，Slash() 首行 `_mover.Pause(0.5f)`（Knight.cs:896），旧 ValidMotion 的 `_pauseTimeout<=0` 子句把暂停判成失主 → 12/13 次往返死于 0.1-0.52s。修法=旗标期压制 ShouldSlash（编舞式结构性免疫）。

**B.（已修，PR#68 推倒重做）目标耦合设计本身**：持有 Follower 引用、目标生死/被抢全分支=每分支都是缝。修法=编舞式（本文 §2）。

**C.（实锤未修，接手的 P0）`isRetreating` 掐断编舞**：
- 证据（09-25 10:05 会话，choreo 构建）：9 条 `[SamuraiDash/choreo] step=ineligible`——出腿 0.16/0.325/0.337/0.344/0.362/0.394s、回腿 0.531/0.537/1.631s；同时多次 roundtrip-turn 朝家目标正确（-111.17→-104.03 等）→ 编舞机械本身工作。
- 机制：`Eligible` :181 把 `k.isRetreating` 判为不合格。原生 `Knight.GoToWall()`（Knight.cs:610-633）：**夜间 `|x|>|墙x|` 且 10 格内有敌 → `SetRetreating(true)`**。武士被冲刺带到墙外的那一刻（=燕返的目的地！）原生夜间系统必然打撤退标记 → 循环 `!Eligible` → 终幕（无敌归还、停在半路）。出腿死亡时刻=从槽位冲过墙线的时间差，回腿死亡=3s 级 GoToWall 重入落在回程中的时间差，完全吻合。
- 讽刺闭环：这个门的本意是"原生在撤退时别抢它"，但撤退标记正是我们自己的冲刺触发的。
- 连带：`SetRetreating(true)` 还会设 `APRetreat` 动画 bool（Knight.cs:616-617 播撤退姿势）——修 P0 时必须一并观察/处理动画面。

## 5. 接手处方（P0 + 观察）

**修法 A（推荐）**：编舞存续期（旗标为真）的循环退出条件从完整 `Eligible` 换成"硬失效子集"：dead/inert/grabbed/`!k.enabled`/mover 替换（Same 失败）/style 失效/世界权丢失。**不含** isRetreating/isCharging/_shouldCharge/GetFormation 等行为旗标——这些是原生对"骑士出现在墙外"的正常反应，不是真冲突。触发门（开始那一刻）仍用完整 Eligible（骑士在墙内，isRetreating=false，无影响）。兜底不变：每腿 1.2s 窗+3s 总上限+OnDisable。
- 风险：真 embark/编队到达时我们最多多跑 ≤1.2s——有硬失效+窗口兜底，可接受。
- 动画：行程中 isRetreating 翻真时 APRetreat bool 会被原生置起——处方 A 落地后实测观察武士是否边冲边播撤退姿势；若是，在旗标期重设（重触发 PowerSlash/Play 已有先例）或抑制该 bool（注意网络 animSync 的 Send 语义）。
**修法 B（否决理由留档）**：行程中 `SetRetreating(false)` 反置——与 GoToWall 3s 重入打架+撤退动画状态抖动。
**修法 C（否决理由留档）**：全局从 Eligible 删除 isRetreating——影响触发门/Return 族/Tick 多处共用语义，波及白天撤退拦截。
**随修**：`step=ineligible` 日志扩一列转储具体失效子句（预算内）——若实测还有别的子句翻假（如 `_shouldCharge`），一次看清。红验证：临时把 isRetreating 加回循环退出条件 → "行程中 isRetreating 翻真仍完成两腿"用例红。

## 6. 残影可见性（根因 D 已实锤：配方 C 从未"变白"——接手第二优先）

**根因 D（2026-09-25 深挖实锤）**：`SamuraiDashVisuals.MakeRenderer` recipe 2（C）= `Shader.Find("Sprites/Default")` 新材质 + **原贴图** + 顶点色 `Color(1,1,1,alpha)`（:186/:385）。标准 sprite 着色器的像素公式是 **乘法**：texel × 顶点色——顶点色 RGB=(1,1,1) 是恒等乘，**结果=武士原色的半透明分身（55%→10% 透明度），从来就不是白色剪影**。白的 `_Overlay` 属性只存在于 PowerSprite2 着色器（且 :136 那行 SetColor 只在 A/B 配方分支，recipe==2 在此之前已 return）。夜间暗环境+人群+≤55% 原色复制 = 用户每次都说"看不到白色残影"都是对的——**白色从未存在**。`shader-fallback=0` 只证明 Sprites/Default 找得到（一级命中），不证明结果发白——此前"材质层已排除嫌疑"的判断是错的，特此更正。
**为什么 A/B/C 诊断时用户选了 C**：A/B 被 PowerSprite2 浮点门废掉（盲审反汇编已证 FLASH_ON/Overlay 无效），C 是唯一渲染出来的臂——用户看到的"可见"是原色半透明复制，当时的记录把它误标为"标准白配方"（与 PR#67 抓到的降级链记录失实同类：实现与记录不符）。
**修法（盲审 P2 当时的原处方，实现时走样了）**：烘焙白贴图——对每个采样姿态帧 `texture.GetPixels32()` → RGB 全置 255、**保留原 alpha** → 新 Texture2D → `Sprite.Create` 沿用原 rect/pivot/PPU；按 texture 指针+rect 键控缓存（武士冲刺动画帧有限，缓存有界）；贴图不可读时兜底 RenderTexture+`Graphics.Blit`+`ReadPixels`（ReadPixels 不要求源可读）。然后 Sprites/Default × 白剪影 = 真正的白色定格剪影（顶点色 alpha 继续管淡出）。测试面：samurai-visuals 加"ghost 材质的主纹理=烘焙白贴图（RGB=255、A=原值）"断言（桩加 texture 数据钩）。
**修后仍要看**：夜间观感（alpha 阶梯 .55→.10 可能仍偏暗，临时提亮常量备选）；排序 order+2 已抬升。

## 7. 实机验收清单（修完 P0 装机后）

1. 夜战武士：出刀冲 7 格→转身反斩→冲回出发点，一气呵成，无敌全程不死在墙外
2. 八道白光停 2 秒（沿两腿路径）
3. `[SamuraiDash/roundtrip-turn]` 多次且 goal≈出发点；`[SamuraiDash/choreo]` 异常行应罕见（当前 9 次/会话→修后应≈0）
4. 远敌（>8.2 格）不开程只原生挥砍；CD≈3s
5. 顺带同候选待验：弓手 [2,3) 站位带存活（夜间盯防）、弩手随从走路不抽搐
6. 联机（如有条件）：主客外观级一致

## 8. 未决/留档池（非阻塞）

- P2：turn 腿在飞姿势采样删除（同姿势 no-op+finish-capture 兜底）；ChoreoHitScan 无 MaxRange 上限（固定 7 格下等价）；夜间 isRetreating 下回程 sprite 不翻面（与旧行为一致；用户若要"夜间也肉眼转身"=新需求）；ChoreBudget 3s 与腿窗 1.2×2 耦合（调窗须同步）
- 残影 2s 是临时观察值，终版待用户定（原设计 1s）
- 版本账：PR#46-#68 共 23 个 PR 未发版；发版等用户实机验收+Mac ARM64 重建（基线 67d39b9 已通报 issue #29）
- 游标/协作：每小时 GitHub 轮询 cron 在跑（Mac 平级 Operator，issue #3 约定）；另一 Windows Codex 会话在做重盾兵 #51 与金币哥布林 #56（win/knight-coin-courier），勿抢
