# #101 R12：完整 Mod 基底与400u接线检查点

> 最新边界：Phase B 的400u地面/布局与收费记录接线已实现并完成源级验证；完整R12资源与scene消费者未齐，Native往返尚未运行，未合并安装。下文Phase A原记录保留当时“仍44u”的历史边界，最新以Phase B节为准。

2026-10-23。用户已批准约 400u 宫廷画卷及后续素材制作、隔离接入。本记录仅交付当前完整 Mod 与既有宫廷修订的源级整合；**宫廷仍为旧 44u 配方，400u 地面和最终素材尚未接入**。#101 与对应宫廷 PR 保持进行中；未合入 release、未安装正常包、未启动游戏。

## 整合与审查

- 基底：release `f2b6dcaa017ebeb236ba09150336b18cea8b30ce`；宫廷来源：PR #265 的 `2b2147bde6348c780ec9b4a8725351e8b75b0d70`，另精确继承 5 个已归属修订文件与 4 个 FreshSeed 测试源/项目文件。
- 唯一文本冲突是 progress.md，保留 release 原文并追加当前边界。19 个最新坐骑源/资源路径字节保持；撤回的 #264 旧档回填不进入此树。旧工作树及其未提交内容保留。
- 保留同一 returned WalletData.currency 的八项 indexer 读取、fresh 原生构造形状识别、历史 Game 的 CLR 引用存在性三项窄修复。当前 Game 存活、owner/provider/根指针和保存责任门保留；新增诊断默认关闭。
- Android 仅更新三条 frozen-source SHA；四个公共文件的 ANDROID 有效代码与 release 一致。
- 阶段计划由实际 `zhipu-coding-plan/glm-5.3`、thinking=max 独立审查通过；另由独立 Reviewer 复核精确源差异、资源/引用来源和默认诊断排除。审查不能代替实机。

## 本轮验证

四个私有 Mac 构建包装器（default/diagnostic × net6/net8）均 0W0E。每变体实际输入 333 Compile、161 Reference、183 EmbeddedResource；源码/资源绑定本任务树，161 引用与当前 ARM64 包逐字相同，734 项综合输入前后无漂移。没有部署 target，构建输出及中间目录隔离。

| 验证 | 实际结果 |
|---|---|
| Paid 当前源 / 显式旧 guard 负对照 | 312/0；319/0 |
| Fresh 当前源 / 显式旧构造形状负对照 | 407/0；412/0 |
| 目的地资格 / 付款准备 | 35 cases、39 assertions；13 cases、71 assertions；均 0 失败 |
| Clock | 256/0 |
| Coin persistence | 54/0；私有 host 的 Palace 替身仅补 API 形状，不证明真实保存交接 |
| 坐骑 map-result | 22/0，230 assertions |
| 源漂移负对照 | 私有源改变、DLL 不重编时明确拒绝；产品源未改 |
| Android host | probe 1W0E（既有 CS0169）；Adapter 0W0E、1265/0 |

默认 net6 DLL SHA256：`b503f997f4dd2990b1d57867edb896a484a3aba234cc190b77efc62ecef16a15`。Android probe SHA256：`2571feee2507ffb88f9c66e436d83a1a4649ce387f2782dcd20ab45116973b7c`。这不是安装回执。

未变的 Journal strict codec 24 项、归档 SourceProof reader 及 Return 34 断言/20,000 次回调证据按当前完整源 SHA 与原件哈希复核复用，未冒称本轮新跑或完整 Native 通过。首次私有 wrapper 的 MSB3540 与旧 coin host 缺 API 的原始失败保留；修正只在私有构建命令/host，不弱化产品守卫或原断言。

## 后续边界

下一阶段需要显式 Legacy44/Capital400 配方、真实 100×4u 地面、严格版本化元数据及门轴 0。源对象遍历预算保持，只有自有目标预算按精确配方推导；旧 44u 不自动扩建。收费前冻结的布局必须同时进入 durable 付款记录与旅程记录，不能付款后重新选择布局。源端完整磁盘恢复、saved GroundData、两岸/船位和冷读身份仍需可靠证据。

400u 原生场景、最终素材、真实相机曝光域/成人脚点/水线，以及自然付满 10 币→进入→保存退出→新进程重进→免费返回完整链仍未验。当前完整 Mod 公共 ConfigGet/保存入口的运行组合也未被 host 套件完全覆盖。Windows 2.4.3、Android 设备及联机不推定通过；Windows 性能实测继续暂停。正常 ARM64 DLL 保持 `25d9ffef5ad20436bf49c8f7f7c7fdb395e16a86f2bae63900ee6df0a8f4c156`，正常存档/配置未写。


## Phase B — 400u源级接线检查点（2026-10-23）

- 单一不可变Recipe同时承担布局身份：Known44保留旧配方；Capital400为100×4u、[-200,200]、门轴0、fresh站位-2。源/嵌套预算不扩大，最终owned GameLayer按源角色计数+配方推导精确预算；原生UID仍交现有生命周期分配，未手工拼ID或自动扩旧44。
- CourtArea仍是地面/sidecar唯一writer。schema1旧48/schema2旧44原wire保持；schema3绑定Capital400。冷读核实际saved GroundData/Tile/UID与活体覆盖，不拿Level scratch0当存档，也不移动cold玩家。
- 首币前顺序为describe→真实来源anchor→完整freeze→durable Begin(layout)。journal schema2与trip v4携带同一layout，config/handoff/generation/return贯通；旧unknown pending不新扣/退款/改账/默升400，已在旧44的严格返回路径保留。
- 新资格由RAM、完整global目标投影、实际Filer表filename对应岛文件、唯一sidecar四证组成。FileStream只FileNotFound判Missing；CanPay仅缓存身份，完整IO/结构复核在prepare/freeze及已证paid source-save边界。准备前先等共享表Ready，再消费一次性机会；其它终态失败仍不重试。
- 新设计224与实体local-support转换、显式actor帧rect读取已实现。10actor/294时间帧批只证明数据可读；没有创建完整400场景、promote建筑/背景资源或用旧R7冒充400完整性。Capital400收费资源门明确false。

Root独立复跑：G157、P216、C139+live4、J60、W56、D27/46/10/4、V29/326/9/21均通过，各针对构建0W0E。保留原journal历史套件47/48及相同原基底失败，未删断言求绿；独审确认payer_uniformity与原current-only payer合同不一致，不是本轮layout回归。H私有controller候选10/10，修正常退出poll/scope竞态；active控制器及旧case13许可未改/未复用。

冻结源的default/diagnostic×net6/net8四完整构建均0W0E；default-net6 DLL SHA256=`b116e1addc69d28565670e4dd70b6e22d6dcc42fc4bf30b7270888a17e99b383`。这是源级候选，不是安装回执。正式PC编译清单已核无测试桩；公开fixture仅合成schema数据，真实存档样本保留私有。Android当前236输入逐SHA与Phase A相同，因此复用原probe1W0E/Adapter1265结果，不外推设备玩法。

GP/C/J/H经实际GLM5.3 max源码复核采纳，D/W/V独立builtin代码审查及最终GLM5.3 max组合增量审查通过；批准范围仅源级检查点与Draft更新，不包含Native/合并/安装。审查过程中错误使用旧map-blank包装器的编译结果已排除；采用的四个结果只来自本任务锁定wrapper与稳定输入。原始失败/边界保留于私有task回执，不提交存档、interop或私人原始日志。

当前待办：完整R12 registry+400u layout→完整scene/resource消费者与真实ground/BODY/water/camera标定→隔离自然10币进入、保存退出、新进程同后像重进、免费返回并再冷读来源→最终合并/安装。54主体生产仍在美术专项进行；仅actor批与设计合同不解除资源门。#101保持进行中，PR265保持Draft，不因源测试通过关闭任务；正常DLL仍25d9…c156，正常存档/配置未写，未启动任何游戏，Windows测试继续暂停。版本账目与安装/实机回执在完整交付关口单列。
