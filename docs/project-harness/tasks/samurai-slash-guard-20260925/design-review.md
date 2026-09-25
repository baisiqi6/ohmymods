在给出结论之前，我正在确定用于发现的精确调用点行号。
# 独立对抗审查 — limited-fresh 设计审查（武士燕返 limited-fresh 修复方案）

**结论：APPROVE**（附 2 条 P2 实现精度要求，须作为验收条款写入 worker 任务书；不改任何设计决策）。

基线核对：`ohmymods-wt-choreo-fix @67d39b9` 与 handoff（samurai-slash-guard-20260925/zcode-handoff.md）所述候选一致。原生证据读自 `game-source/Assembly-CSharp-2.1.0/Knight.cs`（**2.1.0 反编译，仅参考**；handoff 引用的 2.4 行号行为一致）。

## 三问

**1. 是否正确？** 是。方案与两条已实锤根因逐点对合：
- 根因 C：夜间冲刺把骑士带过墙线 → 原生 `GoToWall()` 入口判 `|x|>|border|` 且 10 格有敌 → `SetRetreating(true)`（参考版 Knight.cs:610-633/635-648）→ 当前 `Eligible` :181 判死 → Tick :863 与协程 :1065/:1073/:1085/:1087 三处 `!Eligible` 中断（09-25 会话 9 条 `step=ineligible`）。硬/软拆分让“我们自己的冲刺触发的原生反应”不再杀死行程，触发门不动（出发时在墙内 `isRetreating=false`），Return/Walk 阶梯经 `ValidMotion` :271 继续用完整 Eligible——三条消费方（ShouldSlash 压制/夜队列门/Tick 防重入）语义自洽。
- 根因 D：recipe C 是 Sprites/Default 乘法公式，顶点色 (1,1,1) 为恒等乘 → 从未发白。烘焙 RGB=255/保 alpha + `Sprite.Create` 沿 rect/pivot/PPU 是确定性美白的最小手段；A/B 臂已被 PowerSprite2 浮点门判死，自写着色器在 IL2CPP 剥离环境下更差。`ReadPixels` 读 active RT、`Blit` 不要求源可读——兜底链技术判断正确。

**2. 是否为足够简单的最优局部方案？** 是。硬/软拆分是对 handoff 修法 A 的忠实落地；修法 B（行程中 `SetRetreating(false)`）与修法 C（全局删 `isRetreating`）的否决理由成立：前者与 GoToWall 3 s 重入打架且抖动网络同步的 APRetreat bool，后者波及触发门/Return 族/白天撤退拦截。1.2 s 腿窗 + 3 s 总帽 + OnDisable 三路收尾保留，软旗冲突的最坏暴露被压到 ≤3 s 有界窗。

**3. 是否引入新 bug？** 设计本体不引入；但有两处实现半做就会引入实害的精度点（P2-1/P2-2），及若干有界边缘（P3）。详见下。

## Findings

**P2-1 硬门替换的调用点清单不全（任务书级遗漏，非设计错误）**
- 位置：`ChoreoHitScan` :1157、:1163、:1168 三处 `!Eligible(...)` return。方案文本只点名“ChoreoRoutine 和 Tick 存续”。
- 最小反例：夜间出腿过墙 → `SetRetreating(true)` 翻真；若只改循环三处（:1065/:1073/:1085），行程继续但每次 HitScan 在 :1157 早退——出腿段（墙外，恰是敌人所在）**零伤害**，白影穿怪而过，正是主场景静默失效。
- 最小修正：worker 任务书把 6 个替换点列全（循环 :1065/:1073/:1085 + 命中 :1157/:1163/:1168），并显式声明 `BeforeNativeUpdate` :207 与 `ValidMotion` :271 **保持完整 Eligible**（返回族与夜队列门语义不变）。

**P2-2 Tick 重构必须双侧成立 + Remove 条件化**
- 位置：Tick :861-867（`Finale("ineligible")` + `Actors.Remove` + `if (!Eligible) return;`），choreo 分支在 :871-874。
- 反例 A（可达性）：新设计删掉了 :863 的软关闭后，若 `if (!Eligible) return;` :867 仍挡在 choreo 分支之前——协程未跑 finally 死亡（第三方 `StopAllCoroutines` 类）+ 软失效（retreating）时，**3 s 硬帽永远不可达** → 永久无敌 + 永久 ShouldSlash 压制 + `HasActiveMotion` 永真。这正是帽存在要封的缺陷类；现版本靠 :863 误打误撞封住了它，新设计必须显式保住。最小修正：`a.Choreo != null` 分支提到软 Eligible return 之前（或 :867 对 choreo 活体放行）。
- 反例 B（Remove）：若 :865 `Actors.Remove` 仍按软失效触发——软失效帧移除状态，协程持旧 `ActorState` 继续按 0.3 s 重申写 goal；Tick 下一合格帧重建新状态并可再开第二程，同一 mover 两写者至多 3 s。最小修正：整块（Finale+Remove）按硬门判定。
- 反例 C（子集缺项）：方案枚举的硬子集**没有 Owner 身份**（`!Same(a.Owner, knight)`）与 `k.gameObject==null`。补进硬门（一行，零成本，保留现行防御纵深；mover 替换分支 :857 已覆盖另一半）。

**P3-1 软旗窗内不再中断的边缘**：`_harmless`/`isStationary`/embarkee/helPuzzlePillar/player-control 中途翻真现在被忽略，有界于 1.2 s/腿 + 3 s 帽（handoff 已自认同款风险）。注意 `_harmless` 中途翻真不再停止我们的 HitScan 伤害（触发门仍排除）。留档为有意行为。

**P3-2 契约翻转的既有测试须显式改**：`RoundTripInterruptions`（tests/samurai-motion/Program.cs:791-811）中 manual control/formation/charging/other FSM task 四行从“关闭行程”翻为“行程存活”；config/authority/dead/grabbed 保持关闭；矩阵补 `retreating` 行（中途翻真仍走完两腿）。Return 族矩阵（:363/:1237）不动。方案第 4 点的“临时退回旧门红验证”正好覆盖，按规则这是契约改定而非删测。

**P3-3 烘焙形态建议**：逐姿态子矩形烘焙（`Graphics.Blit(src, dst, scale, offset)` + `ReadPixels` 到无 mipmap RGBA32 小图），键控 `(texture.Pointer, rect)`，上限 32-64 条 + 逐出/ClearAll 时 `Destroy`；整 atlas RGBA32 拷贝也可但每次 16 MB 级。一次性 GPU→CPU 停顿按姿态计，可接受。SpriteRenderer 绘制时以 `sprite.texture` 绑定 _MainTex，自建 Sprites/Default 克隆 + 烘焙 sprite 成立。

**P3-4 烘焙失败回退要定死**：建议回退到现 recipe C 原色残影 + 一条一次性降级日志（镜像 `LogShaderFallback` 风格，写明未白化），资源侧 finally 归还 active RT、销毁临时 RT/Texture2D；测试断言日志与零泄漏（方案第 4 点已含资源归还测试）。

**P3-5 流程留痕**：harness-checklist/progress 同步与任务 receipts 照仓库规则落档；版本账目按 handoff §8 池子走，不在本候选内结算。

## UNVERIFIED（不可宣称）

- 实机两腿一气呵成、异常行 9→≈0（handoff §7 清单）。
- 白影夜间观感（.55→.10 阶梯可能仍偏暗，常量备选）。
- 行程中 APRetreat 撤退姿势是否可感（原生 `SetAndSendAnimationBool` 网络发送；方案“暂不反写+先查风险”是正确保守姿态——任何反写都过不了 animSync Send 语义这关就先别碰）。
- 联机主客外观一致性（残影为本地渲染器，无网络同步，既有缝隙宽度不变）。
- 原生行为引用基于 2.1.0 反编译 + handoff 转述的 2.4 证据；2.4 原文未在本仓直接核对。
