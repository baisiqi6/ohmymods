# #101 R12：完整 Mod 基底与400u接线检查点

> 最新边界：完整400u与Mac ARM64 2.4.0单机隔离付费进入、保存退出、宫廷冷读、免费返回和来源冷读已通过；PR #265 merge `fa54574426a528f135e79b2f9d9bf4c1c9a80e15`，`2026-10-23T19:15:01.156608+00:00` 已安装到正常独立ARM64包并通过原launcher只读预检。Issue #101关闭，#279素材经 #265纳入后关闭未独立合并。诊断实机与默认安装证据分开；下文旧44u、未齐消费者、未实机及失败时点均保留为历史，当前状态以文末收尾为准。

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


## Phase C source checkpoint — 2026-10-23

- Frozen R12 r1 art is locally integrated as `0ef5d44f6f664882db498011c40263b80fbbe816` (cherry-pick of art PR #279 `3dfad32c498d7505275652ba3ad7488f0ec01daa`). The complete binary diff is identical; all 92 production files match the frozen pack. This is a local integration commit, not a mainline merge or installation.
- Production reader: 201 checks; ActorBatch1 326 plus geometry 29 / binding 9 / arrival 21 independently rerun. S1 geometry/frame resolver: 16470 checks after a reproduced tiny-positive-double-to-zero-float scale bug was fixed; the independent delta fixture passes 16478. These are host/source results, not Native rendering acceptance.
- Full Mod source checkpoint (338 source files; O/F/R1/S1, no R12 Unity consumer yet) builds normal/diagnostic net6/net8 with 0 warnings / 0 errors. The pre-resource-loader artifacts are frozen separately; later source deltas require their own checks.
- Case01 failed before payment with Native SIGABRT in `FileProps.get_filename`. Actual ARM64 Dictionary.TryGetValue writes a 16-byte FileProps value through an 8-byte Interop out slot (CLR class/native value type), followed by incorrect boxing. The reader now uses ContainsKey then the runtime-invoke boxed indexer and reads filename once. No writer/MAX/ready/path or shared Interop change. The actual old binary fails the focused Cecil check; the new source8 diagnostic DLL passes all 15 binary/contract checks. Ordinary exception rejection is present at the outer prepare/evaluate/begin-charge/departure call sites; no AccessViolation recovery was added.
- Case02 with DLL `91c257b9e35facd8e7214212ae33f9dccd664e6bbed8aac0ff29f8c9eed9e959` loads source land8 successfully and reaches the one-shot O sample without the original filename AV. Its save/exit result is recorded separately after the case ends, not inferred from successful load.
- O Water result is invalid: current Mac 2.4.0 CamWater token `0x060023bc` maps to ARM64 `0x729328`, a RET-only function retaining self. Case02 returned MainCamera as Water. All Water-specific fields/quad from that projection are Unknown; they are not measurements and cannot calibrate a visible waterline. The original one-shot sample is preserved, not silently rewritten. Other camera/body metadata keeps its own limits; adult BODY/stance remains Unknown.
- Resource preload (S2a) and the Unity consumer/sole CourtScene wiring remain unfinished. Capital400 payment resource precheck still refuses. Issue #101 and PR #265 stay open/Draft; no merge, normal installation, or full paid→400 arrival→native save/exit→new-process reload→return claim.

Local raw evidence: `.local/tasks/palace400-runtime-20261023/phase-c/` (`fileprops-astra-audit`, `water-getter-audit`, `rs-independent`, `rs-final-build`, `source8-case01/02`). Shared normal save/config/runtime/game protections remain enforced; no restore or replacement of normal player data is performed.

Case02 terminal result: script run bound reached, exit `143` after 905.46s; no native-save-quit success. Terminal normal-six protection and resource check passed; actual 15-file private postimage was copied and retained. No additional window was started.


## Phase C — 完整400u消费者接线检查点

- 冻结r1资源由显式92项props接入；单点缓存只有全包校验成功才发布，准备窗口/冷读创建可预热，CanPay只读缓存。唯一CourtScene按有效layout互斥分流；400u使用12840×252、axis6420、street224校准，不叠旧HalfCourt偏移，不回退旧44。
- 452节点沿素材真实hint排列：393后景、10角色、49前景；校验实际float worldZ/view-depth唯一与clip，冻结P1/镜头/Level/World/layer/借用renderer来源。镜头Z/朝向/clip或借用体漂移即故障清理，无重选/扫描。人物比例、水线0及静止背景是显式待验初值；舞者dance、荷花calm、其他角色idle，不宣称完整NPC行为。
- 独立审查发现并修正：分配后尚未登记的GO/Sprite丢失、worldZ误当view-depth导致unitZ0首Tick失败、失活renderer/Level替换漏检、新outer-root清理吞错及无效dedicated view落旧分支。原红例保留；S2b最终独立16392项及10个额外故障案例通过，S2c定向增量套件通过、最终Astra独立增量20/20通过（完整CreateCapital/Cleanup/route方法逐字抽取核对）。绘制失败不写钱包/玩家位置/paid journal，不改原付费事务。
- Root冻结完整622项源/资源输入，default/diagnostic×net6/net8均0W0E且构建前后/source live一致。实际diagnostic-net6 DLL `91ba334e80d8d8c47a88abe2b03184c01c2d7e15320d541861478b18cfd0de80`，default-net6 `faad931d8b7df7dd2071ffa9ea4460c26400e2fbb126c01c386f44320720fa72`。
- Cecil静态读取真实DLL：275总资源/92新资源逐字节相同，真实embedded stream交给工具重链的R1/S2a loader，86PNG/452placements及275检查通过，独立正向结果一致；不执行受检Mod DLL，也不证明Unity/Native。FileProps当前DLL的15项binary/model检查通过；Android既有probe仍1既有warning/0errors，产物无新资源prefix（非设备验收）。
- 新案例工具只做准备：新PID UI pair的hold29、Python28、family28及当前prepare32个targeted guard案例独立通过。旧case13/case02许可不迁移；正式运行须新exact source/DLL/UI/备份/进程身份凭据。当前457私有文件、71游戏文件、15可变输入只读快照就绪，资源无冲突，未stage或启动。

待办：新的隔离自然付款/400u画面与两端→原生保存退出→同一实际后像新进程重进→免费返回（及来源冷读）→最终合并/安装审查。正常数据及正常DLL `25d9…c156` 保持；构建/host/审查不代替上述实机。


## Phase C — 首次完整候选Native准入反馈

2026-10-23，`palace400-paid-r12-20261023-01`，实际ARM64 2.4.0、diagnostic DLL `91ba…de80`。源岛8载入/真实Playing、helper/observer六目标与PID/私有路径匹配；两次实际付款开始后松键，原生取消与durable `Cancelled`一致，无OnPay或settlement。跨日原生自动保存后，下一次首币前拒绝 `destination-content-drift-before-charge`，未付费进入。独立私有前后像读回：slot/biome/reign/start/来源岛与目标land12缺失状态不变，变化为源岛进度、统计与银行；当前跨时刻SameWitness还要求完整global压缩SHA相同，修复点在准入比较，不放宽同次完整读取双读和四证。

已fail-stop结束准确controller/child（143/receivedSignal15，主动终止，不称闪退或保存退出通过），保留原始日志和17文件实际私有后像。10条启动Error与上轮源8逐行相同；正常六对象及物理游戏终态校验通过。400画面、完整10币、UI保存退出、冷进与返回均仍未验，未合并/正常安装。旧case/longpay凭据已消费；新案例须绑定修后source/DLL与新凭据。

修后源级增量：删除跨时刻全global rawSHA比较，保留全部目标/身份/epoch约束；首差异以字段标签报告。同次完整gzip严格解析+bytes双读、付费源保存proof的rawSHA精确匹配未改，不自动reprepare/重绑owner/改账。旧实现3红，私有78/0，公开产品方法抽取17/0且Root独立复跑17/0；Astra独立86/0（含本轮真实跨日文件），均0W0E、最终source `3c40e469…0646`，增量approved。修后完整四变体均0W0E，实际diagnostic-net6 DLL `bac68deca5240fa14c489d3bb37c2957909a108d920fc25e9420a699cace38ea`，嵌入资源275检查/92项和FileProps15检查通过。第二轮新case仅已完成只读预检与工具增量审查，尚未stage/开窗；仍不推定Native完成。


## Native实测推进与冷进失败（2026-10-23）

修后paid02实际自然跨日global变化后首币准入成功；`OnPay floating=10`、原生settlement、钱包观察140→130，source capture/file proof和一次Reload12均成立；fresh ground100×4u/edges[-200,200]、schema3及452/86 scene实际创建，journal Entered。两端已走到/截图；右端树根和宫廊底部露边仍待修，中央/左端静帧可复用，连续人物/荷塘动画未验。三条land12 MountFresh NoEnvelope仅为既有非目标observer噪声，独立源码确认不跳过原执行、NoEnvelope撤销no-op；其它新Error不豁免。paid02到900秒边界143退出，**未执行UI保存确认，不能记保存退出通过**。18文件实际后像保存，normal六对象和物理游戏终态通过。

cold01从该实际原生自动保存后像新进程加载12，失败于postPop `saved-ground-count`；随后NativeBGNone拒绝接管、原BG NRE与Capital拒绝。立即fail-stop（不是UI SaveQuit）、保留日志/18文件终态，不续开原cold02/03。独立实档与Root严格validatorProbe确认保存里Ground1/Tile100/400u正确；当前2.4 ARM64 TryPop body SHA70aed349…5b94在完成时清除this+0x50恢复列表，因此错误是CourtArea把postPop已消费RAM再次当保存前像。修复保持strictGroundCount，移至exact prePop验证并以同次成功加载/owner-bound证据供cold消费者，liveGround/Tile仍须核验；不补NRE、不改保存/重种。R1明确要求snapshot自身异常不阻原Pop/clock，只令geometry证据失败；原生异常原样传播。源码实现/增量复审及新Native案例尚待。PR265继续Draft，未合并/正常安装，正常存档与DLL保持。


### 冷进根因修复与右岸增量（R4候选）

复用既有Bootstrap原生Pop调用边界，在prePop严格校验保存几何；同次original/result成功且无原生异常才发布凭据，绑定owner/Trip/recipe/Level/Game/World/layer/scene及代次。新exact尝试（含skip）撤旧proof、cold缓存与in-flight代次；不清fresh span，不阻原调用/改clock，不写回保存。独立审查抓出的skip复活与World替换反例已补齐：Root产品方法抽取81/0、Astra原55断言55/0。首次完整R3构建7条新增nullable警告，已保留failed receipt；准确注解修正后R4四变体均0W0E。diagnostic-net6 SHA `62a5fa1f9af5cad83400b264aaadc29bd906248cfd182cc0558d5e03f5db27b7`，完整源/资源冻结前后不变，实际92嵌入/275检查、FileProps15检查通过。

右岸仅forest-74内收X198.8125、far-palace-34内收X196，forest域内标志同步；86PNG、其余450实例不变。最终index `49b99d18f959f08c7ba9b165713358fca56ffb533e31514a7f0c1d3a2d61c43b`与loader同步，独立增量approved；Native落脚与动画仍待。新cold04/05/06工具仅参数化案例/构建，Root和独立各32/32，必须逐案读取实际前案后像并发新凭据，不seed/复用旧许可。此记录只代表源级/构建通过，尚未完成R4冷进、真实UI保存退出/返回，PR仍Draft，未合并安装。完整回执留本机 `.local/tasks/palace400-runtime-20261023/phase-c/`，不将批量过程文件加入仓库。


### R4隔离往返推进（非最终交付）

cold04实际冷读land12成功：prePop严格几何凭据、live Ground1/Tile100、400u及452/86绘制成立；实际菜单SaveQuit后自然exit0，新的gzip存档完整。独立复核原来源岛8的2639对象JSON与Entered proof未变，P1=130、steed36保持。cold05再次冷读成功及右岸两处P2实际图增量通过；舞女/一朵荷花确有动作、Menu两独立PNG逐字停帧，仅为有限采样，不代替完整连续动画。cold05到900秒边界143退出，不记第二次UI保存/回程通过。

后续自动输入的eventDelivery始终UNKNOWN，实际位移和菜单导航不可靠。Root曾把grep截断误判为回程未挂载，独立R4 DLL Cecil14/14与真实日志259行已排除；门X0/near1.7，而cold07保存P1=-1.895，在范围外。几次Down因Root未再次核对Menu而在Playing投币招募，只改私人档，记录为操作错误、不补币、不当产品缺陷。cold07后来实际Menu SaveQuit/exit0；08未启动。

cold09由用户明确接手并完成免费回程，随后确认停手并手动UI保存退出：同trip一次Reload12→8、到达P1=128与回程前相同、落位53.86969/.88、真实source checkpoint=True；实际终态VisitCommitted rev9（不是Returned中间态），原收费capture/fileProof未改。源加载ApplyData/TryPop期间raw34不变，Playing后原生setter34→35、getter+1对应HUD36，未将宫廷local计日写回。终态钱包132作为新冷读基线，不把退出前的收钱/现场操作反推成回程费用。独立后像审核通过，可作cold10新输入；11/12额外开窗安排因用户已在09完成回程而撤回，未启动。

cold10已用新exact凭据从09真实后像冷读source8：raw35在原生加载前后保持、Hero2席bound2，16随从/4侍从保持；初始HUD弓手/弩手等待重算由原生Crossbowman重分类日志解释，不能按瞬间HUD认定丢兵。最终实际SaveQuit/终态独立复核仍待。正常6对象/15文件备份与物理游戏71文件持续核验一致，没有正常存档恢复或安装写入。


### 完整隔离往返链通过（2026-10-23；合并/安装另记）

用户实际完成cold10最后一次SaveQuit，natural exit0/errors=[]。最终独立核对19/19终态文件SHA一致：source8，P1落位53.86969/.88，原生加载raw35保持，Playing期间自然35→37；Hero两席bound2、Knight身份附档未变、VisitCommitted rev9与原capture/fileProof保持，无新收费或回程。终态global SHA `c56eafeb4dee1f6c8b4e81b18aeaacdab6d75f2fc59a4d64b5182da965420858`。源世界等待期间正常收钱和战斗变化不当作冷读丢档，也不称全军静止。

本轮明确通过的链为：paid02真实10币/原生结算→fresh400u创建→cold04真实保存退出与完整disk→新进程宫廷冷读→cold09用户自然免费回程/一次Reload8/钱包128→128/真实原生提交及UI退出→cold10来源新进程冷读与UI退出。cold05的900秒超时、Root输入误操作、工具EventDeliveryUNKNOWN及被证伪的未挂载推测均保留原始证据，不把失败改写成功。两端旧P2静帧已闭合；连续60fps动画观感、全部动作、Windows2.4.3、Android和联机没有在这条Mac ARM64 2.4.0单机链代验。

R4产品/source未再改，default-net6 SHA `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf`。正常DLL `25d9…c156`及正常15数据备份和物理游戏71文件保持；合并与正常包安装仍须独立最终决策和回执，未将私人测试存档移到正常版。

### 合并与正常包安装收尾（2026-10-23）

- PR #265 merge `fa54574426a528f135e79b2f9d9bf4c1c9a80e15`，实际GitHub已读回MERGED。#279原提交3dfad32c与采用提交0ef5d44f的完整`git show --pretty=format: --binary`字节相同，stable patch-id均`d7483b316f5c6442b93611afe57cced4b372f9f3`；按incorporated via #265关闭，不虚构 #279另有merge，不改master/删作者分支。Issue #101已按本轮代码与原生完整链收尾。
- Native使用diagnostic-net6 `62a5fa1f9af5cad83400b264aaadc29bd906248cfd182cc0558d5e03f5db27b7`；实际安装default-net6 `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf`。623项冻结源/资源一致，四变体0W0E、actual92资源/275检查通过；不将同源等同为default已另做实机。
- 旧“仅DLL安装”方案经实际launcher依赖核对被推翻：launcher每次硬校验SUMS，须同步manifest中的DLL哈希/大小和SUMS对应两行。最终只改三键；manifest仅3字段，SUMS274行只改DLL与manifest两行。独立GLM5.3/max集中审查批准；Root84/84受影响host回归、独立18/18包增量通过，未重跑未变化产品/Native。copy/hash失败漏清的原反例保留，修到exclusive stage成功后立即登记owned临时文件，保留foreign stale。
- `2026-10-23T19:15:01.156608+00:00` 完成一次受管三文件安装：DLL、package-manifest.json、SHA256SUMS；写后SHA精确，normal树恰3键变化。旧DLL及原metadata、15份save/config/prefs备份均读回校验保留；正常数据/71个物理游戏文件未变，未写Steam、未迁入私人测试档。原launcher `--check-only` exit0，前后树一致，未启动正常游戏。本地实际回执在`.local/tasks/palace400-runtime-20261023/phase-c/normal-install-plan/receipt.json`与`postinstall-receipt.json`，非公开原始日志。
- 正常DLL由25d9ffef…c156变为055368a3…59fcaf，旧隔离控制器/批准的正常指纹已过时，后续测试重新准备匹配的基线与单次凭据。两端截边已静帧闭合；完整连续动画/全部动作、Windows2.4.3、Android设备、联机及Windows性能不由本轮代验。未发布公共release/tag，宫廷功能在下一正式版本只按一条+0.3登记，内部返修不重复计分。未发现checklist对应既有节点，未裸改JSON/DB或新增虚假项。
