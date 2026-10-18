# 宫廷接入与背景收口（2026-10-16）

对应 #101，分支 `codex/palace-playable-20261016`。代码与素材整合进行中；尚未完成真实付费往返验收、合并或正常版安装。构建、源码审查与实机结论分别记录。

## 已完成的源码范围

- 两端背景及相关动画帧精修；14 张 PNG 保留原画尺寸和中央区域，改动限定端柱、树根与残留截边。离线逐帧检查通过，左右实机步行复核仍待完成。
- 固定宫廷运行 profile；诊断与免费 QA 入口关闭，原生十金币交易承担扣款。付前快照、来源原生保存、已付责任、冷读恢复和免费返程使用同一责任记录，异常时保留证据并停止推进。
- 宫廷专属地形、人物与画卷层；来源岛时钟保护、宫廷独立时钟及返回保存接线。源码保留 Android 原分支，新增宫廷不自动加入 Android 项目的显式注册表。
- 标准 IL2CPP 工程补齐五个直接引用：`Il2CppInterop.Common`、`MonoMod.RuntimeDetour`、`UnityEngine.UI`、`UnityEngine.UIModule`、`UnityEngine.AudioModule`。仅 IL2CPP/Debug 组，Mono 不变。

来源基线 `351aa35466d710a3858828ed1d79ba984f44977c` 已快进到 `1f9c6753ef2a87ba994d38c96dc49b111927551c`；增量仅三份 Android 验收文档。52 份接入源码按已审 R15 逐 SHA 采用，183 项资源与 LogicalName 一致。共享入口的 Android 分支 543 tokens 与原分支相同；另三份地图/塔/墙门的 Android 分支也保持等价。

## 构建与审查证据

R15 是真实源码整合，无代替游戏类型的形状 stub。收费责任、实际文件存储、来源存档证明、时钟、返程保存回调分别独审并修复已发现问题。原失败与返修后结果保留，未将宿主测试写成原生验收。

直接构建仓库标准 `il2cpp/KingdomEnhancedMod.csproj`，使用冻结的当前 2.4.0 依赖、本机离线 NuGet feed、独立 obj/output，禁止自动部署：

| 目标 | 编译 | 实际输入 | DLL SHA-256 |
| --- | --- | --- | --- |
| net6.0 | 0 warnings / 0 errors | 325 份仓库源、正常生成 PluginInfo、2 份 SDK 源；183 资源；24 直接引用 | `412b6d5a67d40760a0d89e9b7c8672ebfd7729753727ad043568ccdc59f26b98` |
| net8.0 | 0 warnings / 0 errors | 同上 | `6f587fdffc102f16754ca958563c87441852ba8c8448c9c9aa9987deaebf645b` |

标准工程及生成 PluginInfo 保持正式版本号 `10.8.35`。待测累计私有 R15 使用此前预览标记 `10.9.38`，net6 DLL 为 `3ca40427dd521e2af1a2e9a5a72eacb8a027e451ba6a567b01ecc9b199273c33`；两组不同产物不互相冒充。未创建正式 release/tag，最终安装输入须另记实际 SHA。

本机构建回执在 `.local/tasks/palace-completion-20261016/standard-project-build/`：`final-output-readback.json`、各目标 `actual-input-closure.json`、`normal-and-frozen-protection.json`。首次构建恰逢测试副本 interop 被加载器删除而失败，已保留；改用逐 SHA 冻结的 161 个依赖后完成标准双目标构建，未修改正常环境。

## 已观察的隔离启动边界

游戏为 2.4.0r23485，Unity 6000.0.61f1；共享只读 `.app` 的 GameAssembly SHA 为 `738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`。

首次 PID 定向窗口查询、窗口截图已真实成功；测试 PID 20271 的截图仍在希腊加载画面。当时 Mod、保存重定向 helper 和观察器尚未加载，45 秒准备限触发后进程退出，未执行付款。以下是该轮故障的根因；修复后的 R6 复测见下一段：

1. 此 ARM64 shim 将 `BEPINEX_GAME_ASSEMBLY_PATH` 与 dyld 已加载路径作字符串匹配，`Path.GetFullPath` 不解析符号链接。隔离启动应沿用正常环境的物理 GameAssembly 路径，保留同一文件 SHA、shim 与 detour；不修改原生入口。
2. 私有准备遗漏正常环境 72 个 Unity 基础库文件（71 DLL），导致实际 `ComputeHash` 从正常 `cad3e3884e08fb0291fcd5b98c177203` 变为 `3a5fe4acdf364401c9681fde21d2a135`，加载器先删 interop DLL 再因禁网而重生成失败。独立纯 BCL 按实际 IL 复算，补入同一批库后精确恢复正常 hash。应补齐依赖并恢复原本逐 hash 相同的 interop，保持 `UpdateInteropAssemblies=true`，不伪造缓存或放开下载。

此前按路径获取已退出游戏的窗口曾误启动正常进程，并实际推进正常战役 25 秒。误写原件已保全，正常 `global-v35` 已恢复为近期检查过的 `f2af0bd7dc5ff9e6680aaa96d5b800cdba66863a4616d093b6ca3af2a79096b0`；正常 DLL、运行文件、共享游戏文件核对保持。独立 preferences plist 缺少事前字节基线，保留未知且未擅自覆盖。后续按实际 attempt 的 PID、启动时间、完整命令和物理路径绑定，不依赖可能自动启动应用的路径绑定。

R6 已在实际私有目录补入缺失文件，纯 BCL 重算 `cad3…7203` 与原缓存一致，再以 455 项私有输入和 71 项游戏文件的闭包启动。09:32:29Z 的 PID 41819 实际加载 Mod、保存 getter 重定向、agent 路径回读及六个观察目标；45 秒内观察到 14 个真实事件，进入希腊来源岛画面，入口价格十金币。此结果确认前两处加载修复有效，不等于宫廷付费往返完成。

首次发送右移前，工具因 `NSRunningApplication.launchDate` 等可选元数据缺失而拒绝，未激活或创建按键事件，整轮随后停止。SDK 明确 `launchDate` 不对未经 LaunchServices 启动的进程提供；后续控制工具改用实际内核 PID、出生时间和执行路径身份。私有战役只发生自然原生推进，保存为有效 gzip/JSON，保留而不回滚；没有付款或已付责任被丢弃。正常存档仍为 `f2af…96b0`。

09:56:10Z 的独立 UI3 轮次实际验证了有界左右移动，并在画面中找到红色入口和“十金币 · 进入天朝”标签。10:04:42Z 截图因同一窗口坐标变化而拒绝，重新查询、截图后仍有移动；这一过程不能描述为全程无拒绝，也不推断窗口变化来源。该轮尚未投币，运行至十分钟上限后由控制器终止，原生退出码 143，不计付费或保存退出验收。现存私有存档为有效 gzip/JSON，SHA `6f56b8c2667e6904534f82aa989a7f794c7a1ef90c36a3c32eaffa9dc555a797`；动态匹配的 P1 保存位置 x=53.62168，钱包八值为 `[66,0,1,0,0,0,0,0]`，这是 10:04:08Z 的保存时点，不能冒充停止位置或付款前实时余额。已付目录为空、Trip 缺失。退出后正常存档、正常 DLL、372 项运行文件、71 项游戏文件以及另行固定的 preferences/VDF 均核对保持。

## 待交付

2026-10-16 11:54Z 按用户要求暂停在离线节点。隔离专用 CanPay 观察插件已完成 net6/net8 编译（均 0 warnings / 0 errors）及 80 项纯宿主测试，Root 已读取原始输出并核对产物 SHA；代码最终复核、隔离安装与真实观测均未完成，不把 worker 交付写成已接受或原生已验证。该插件未写入任何游玩环境。现无测试游戏或模拟器运行，正常 DLL、原存档、运行文件、共享游戏及另行固定的 preferences/VDF 已核对保持基线。不安排自动续跑；恢复时先重核当前环境，若操作复杂，准备隔离候选、备份和简短步骤后由用户手动验证。

后续独立 UI3B 轮次进入同一来源岛，45 秒内取得当前进程观察事件。角色到达入口前，唯一一次 S 短按为 0.9 秒；前后已取得的画面未见入口投币圆点，业务日志未见 Started/OnPay/切换。API 返回成功仍不证明游戏收到或完成付款；私有存档没有产生新保存，因此也不能用旧钱包值证明内存未变。该轮停止继续付款，由 Root 核对 PID 后终止（native 143，168.86 秒），未使用剩余轮次自动重试。正常侧保护保持。

当前需要区分付费准备、安装核验缓存与原生输入/选中三类原因。源码未发现安装漏接，尚不据此定根因或放宽条件；正在准备隔离专用的有界只读观察，不修改当前 Mod 或正常游玩版。

真实十金币进入、左右端部步行、宫廷保存退出、新进程冷读、免费返回、真实来源保存尚待完成。仅这些结果和必要审查通过后才合并并安装正常游玩版。源码构建不代替实机；Windows、Android、联机及故障注入未在本轮验证。


## 2026-10-18 恢复后的真实观测边界（06:38Z）

用户已要求继续。现 r5-fixed 14 张 PNG 独立逐像素复核通过：1448×252/RGBA、中央区域不变、35840 变化像素均在允许端部范围，当前候选与 W 的14图一致；原尺度离线预览通过，不替代宫廷实际全镜头与动画验收。

两轮新进程仍未取得收费原因：N0 PID40072 在诊断 interop 成员检查失败（把 property 查成 field）；R4 针对实际 2.4.0 metadata 的19项查询修正后，N1 PID45273 通过检查但诊断安装抛 HarmonyException。N1 未保留失败 target/inner stack，不能推论首个 target 失败或 managed 后端不支持。两轮约8秒内回收（143），均0 UI、0付款；各轮原始日志已冻结，旧case/审批不重用。N1后06:11Z正常492文件/游戏71/共享存档2/preferences/log/VDF均与事前一致，私有档仍6f56…5797。

下一离线步骤已获实际 GLM5.3/max 对抗审查：仅私有源码编译有界的自然 CanPay 结果与场景快照，零新增观察 detour、无额外业务调用、保留原 bool/短路/异常；同SDK无符号基线与诊断构建作完整IL/183资源对照，再独立审查。当前仅实施离线候选，未批准下一轮游戏、未修改产品源码、未合并或安装宫廷。真实十币进入、端部步行/连播、宫廷保存冷读、免费返回及来源状态仍待验证。

完整本机证据：`.local/tasks/palace-resume-20261017/` 的 `art-review/`、`native-n0/`、`native-n1/`、`interop-api-review/`、`harmony-n1-static-review/`、`direct-diag-decision.json`。正常版已由独立 #249 流程更新为8345…9e1b，不再把历史f2af存档或旧R15 DLL当现正常包。最终交付须在最新主线重新整合，不能用历史3ca整DLL覆盖已合并修复。


## 2026-10-18 自然结果诊断离线交付（07:37Z，未启动 N3）

源内联自然结果观察已完成；net6/net8 trace、同SDK基线及symboloff共五次产品构建0W0E。同源同SDK对照仅三业务方法的诊断条件编译与新helper不同，其余IL/183资源一致；相对原3ca二进制另有21处编译器差异，在新baseline和trace中相同，不将旧整DLL当最新主线。最终net6产品351b37b72fc6903b1ff3c2a1567a8b2d2c1938e2fccedb6e94a25294cf40615a。

联合测试曾发现Python按UTF8字节拒收C#按UTF16截断的合法中文结果，故N2在stage前取消。只改私有消费者为同一UTF16计数；外层64KiB行/32MiB流字节上限不变。4d2bd216ac63b6622aefe9c204141d9814217ec2b04700c0e10f0d069b2a231e控制器获独审：128/128宿主、七组真实C#中文/emoji producer旧RED新GREEN；C# helper81e3及产品351b、身份源4946保持。Init在宿主中为seed，不冒称真实初始化成功。

N3仅拟零付款自然CanPay采样：现有PID guard截图、必要一次Continue、最多三次0.3秒左右纠偏，禁付款键和主动业务调用。精确准备与新GLM Native门仍独立办理；stage未执行、normal8345未替换、共享存档未恢复/覆盖。完整10币进入、保存冷读、返回、端部动画仍待验。最终交付将把本任务增量移至最新release，保留已装#249/#253/#257/#258，不安装历史R15整包。

证据：direct-final-repair/、direct-source-review-final/、direct-source-review-unicode/、direct-control-review-unicode/、direct-unicode-repair/；N2取消证据native-n2/superseded-before-stage.json，新准备native-n3/。独立审查/模型门与Native回执分别记录。

## 2026-10-18 N3 已执行：启动环境未通过（07:46Z）

单次候选N3私有安装及事前备份19项完成，产品351b/控制器4d2/身份4946严格闭包456项、恰三插件；独立准备审查及实际GLM5.3/max精确门通过。初份无工具模型公开批复曾自称本机实测，Root拒绝使用并保留，续审公开撤销该证据归属错误、仅按粘贴源码与Root实查复核后才采纳；实际磁盘校验由Root和独审完成。

PID68482运行48.674秒，在45秒加载门失败后由控制器回收，exit143。preloader记录Doorstop.Entrypoint.Start→Mutex.CreateMutexCore打开Global\c0e73…失败；没有BepInEx LogOutput、产品/观察helper加载或真实Init，0UI/0付款/0样本，不据此判断收费、图层或完整往返。07:46:31再次fresh核正常六类对象492/71/2/1/2/1全部保持，私有gzip/JSON档仍6f56…5797，game/VM零；窗口已实际交回，无自动重试。

只读进一步发现exact IPC文件的两个/global父目录均不存在，旧sandbox只允许文件本身，目录创建不在白名单；这是具体隔离启动依赖缺口，真实因果仍待no-game CLR红绿探针。拟只允许两个精确目录元数据、保留其他deny，未改生效sandbox、共享目录或正常设置。产品收费根因与完整Native流程仍待取得。原始日志/退出/保护SHA见native-n3/post-native-audit.json，旧N3 gate已消费。

## 2026-10-18 N4 真实启动与诊断前置修正（08:56Z）

N3 的真实 kernel deny 已定位为 .NET Global Mutex 父目录创建被隔离策略拒绝。旧策略和旧失败保留；新策略仅为两处精确 global 目录增加 create/mode，精确 mutex 两叶不扩。独立 no-game 探针未运行；经实际 GLM 5.3/max 替代方案审查，以受保护的新游戏实例验证启动，不再将制造共享 namespace 缺失作为前置，也不停止外来 compiler 或删除共享目录。

N4 私有新 controller e7ba101、策略 ea516 经独立 22+27 项 host 检查与实际 GLM exact 门后运行。PID19352 于08:55:25启动，7.19秒后因诊断自身的 preloader-file-missing 拒绝而停止，已真实 reap143；三插件已进入加载日志，但完整 bootstrap 身份、自然 CanPay、UI、付款、保存冷读和返回均未通过。两个 IPC 父目录当时已存在并有外来 VBCSCompiler，故仅属 warm 分支观测，不能声称验证冷目录创建。normal6与此前逐字节一致、正常8345未换、共享档未恢复或覆盖，私有6f56仍有效。实际Bep日志已独立归档，失败和已消费门保留。

当前 managed Bep DLL 的 Start IL 与 N4 现象共同确认 preloader.log 是 catch 异常产物，旧诊断把存在性误当加载凭证，原断言已被推翻。独立设计审查和 GLM delta 均支持：C#存在时拒绝，Python在原有采样循环和reap后的终检用lstat拒绝任意迟到错误产物；缺失仅中性，所有原生正向身份仍保留。正在只写本机私有目录实现和复跑，尚未替换候选产品或再次启动。此前“仅删除 File.Exists”提案已被更完整的负向错误证据方案取代，未采用。

原始证据在 `.local/tasks/palace-resume-20261017/native-n4/`；后续修正与模型证据在 `preloader-observation-fix/`。最终交付仍须移到最新 release 基线，不能把旧 R15 诊断整 DLL 装回正常版覆盖 #249/#253/#257/#258。

## 2026-10-18 最新主线整合与入口确定性状态缺陷（10:07Z）

新的受管工作树 `codex/palace-latest-mainline-20261018` 以 release `02758672703a335974e5e2ac16a711098215beeb` 为基线接入原205项 owned源码/素材，旧工作树index和主目录10项非本任务改动保留。最新 net6/net8独立组合均0W0E、326 production/161 refs/183资源闭合，六组宿主1029/0；仅机械更新既有Android字典三条源SHA，76条pin与有效ANDROID源复核，Debug/Release probe各0E/既有CS0169一警，AdapterTests各1265/0，未引入#263或设备通过结论。旧R15/trace6154不得安装正常版覆盖已合入功能。

H4真实两个方法体字节提取、plain边界host RED由Root独立复跑30断言29过1失败：精确missing且没有slot12本应Fresh，却因_classified=false被未准备门拒。最小生产修复只将missing判定同时写入classified/state，保留missing+已有slot12及corrupt/foreign/reign/结构错误拒绝。新35用例/39断言freshbuild通过；独审又复现旧测试DLL在磁盘source/metadata/generated被更新后仍可冒充新身份的P2，必须把同快照SHA编入程序集并复审，旧原始结果保留，当前不采纳“已硬绑定”结论。

H3完整调用/写入审计提供明确反例：外层先消费Game准备标记，内部TryOwner可能因首tick Mod关闭/身份尚未就绪零IO返回；同Game后续启用/就绪不能重新准备。实际GLM5.3/max批准显式区分WaitingForOwner与Attempted，只有未开始的纯守卫沿既有20秒/0.25秒窗口等待，开始IO后的失败及异常仍终态不重试。正在有限owned源与host回归中实现；这不证明当次自然CanPay首先命中H3，clock安装审计也未发现漏装。

N5因09:47准入过期而真实0stage/0launch/0UI，记UNUSED不重放；Root09:47与09:56鲜核normal6及私有6f56全部保持。N6只用历史受控trace6154观察自然CanPay，不付费/保存，不用于最终修复版验收；31离线与5接线mock独审通过。后续启动与所有实际结果另记，不能把离线门当Native通过。normal DLL8345保持，未替换/恢复共享存档。

本机回执：`.local/tasks/palace-resume-20261017/` 下 `latest-integration-build-review/`、`latest-android-build-review/`、`h4-followup/`、`h4-fix-review/`、`h3-fix-decision/`、`native-n5/actual-unused-handoff.json`、`native-n6/`。尚无本轮PR/合并/正常安装，#101仍进行中。

## 2026-10-18 H3/H4 最小修复与最终 host 独审收尾（10:28Z）

生产 H4 只修missing分类写入；H3只用显式 WaitingForOwner/Attempted 交接一次性准备责任，保留已开始IO后的终态不重试。独立额外审查确认后置config/entry并不负责使TryOwner就绪，没有新增自等待环。原生自然CanPay首因仍不从这些host反例推断。

最终产品：Mac net6 `dd98771bd0452e7e5b3c4cb3d3bbb82d349449d9c01eabe84d491727d7e5ed3b`、net8 `525b2971ce5ee4913ff23f2585c5835b001189da9d42fa2cb4cbb334bfc6af95`，各0W0E、326源/161refs/183资源前后保持、既有1029/0。net8使用Mac interop引用，不称Windows实机通过；私有support仍10.9.38，不作为正式新版本计分。生产三源与两套测试当前hash均由独审及Root核实。

H4 R2独立35用例39断言，H3 R2独立13用例71断言，P1/P2全0。两套测试编译内digest由同一次源码快照生成；旧DLL+仅重提取行为变更、注释变更、普通漂移均在RunAll前exit2，错误行为真重编则实际断言失败exit1，恢复fresh全绿。此前“已硬绑定”的过度结论被替代，旧raw证据保留。H4作者末尾在报告后把metadata.source改为固定路径，旧manifest中提取器8abe SHA过早；独审实际冻结并构建acc16 SHA，取代该一项。H3/H4部分worker在有界cap被aborted，不能以其报告存在当完成；收口依据是独立实测与Root当前源/文件匹配。

证据在本机 `.local/tasks/palace-resume-20261017/latest-final-h3h4-review/`、`h4-fix-review-r2/`（Root再跑35/39）、`h3-fix-review-r2/`。N6仍仅历史trace6154的自然收费条件观察，不能验证这些最新修复；完整最新NoTrace候选的收费—保存—冷读—返回准备另过离线门，尚未安装/启动。当前没有宫廷PR合并/正常安装或正式发版。

## 2026-10-18 N6 实际观测与完整流程准备（10:53Z）

N6 于10:30:22Z启动PID56817，历史trace6154、helper/observer及实际OS身份全部就绪，自动进入既有希腊战役。94条Playing快照加2条生命周期已耗尽96条预算，Root接近入口过慢，未取得任何自然CanPay记录。其后只做一次0.3秒右移，截图出现十金币入口与红斜线；没有S/下/Enter或保存菜单操作，不把输入成功、入口显示或零次付款当作付费通过。该轮由Root有界停止，真实143/reap；原始日志、两张截图和所有输入回执保留。

实际私有global变为`bcab27d57d70f1414d50ff4dd4431d90ea11a9e6d721ab0738bf3d9ff77c9756`，保持有效gzip/JSON，slot0/Greek/land8/reign3、九岛、无slot12；没有Palace付费/旅程文件。已封存15份实际save/config/home文件，manifest SHA `211fcdcf80f267ecb4b5e32745a226cd719afae0aa2c4d78cffd6661eebaa0b5`，不回滚旧6f56。未主动保存不等于文件不会自然推进，尚无写调用证据确定本次写盘触发。10:35:36Z鲜核正常六类对象492/71/2/1/2/1均保持，DLL仍8345；没有覆盖或恢复共享存档。

完整NoTrace实测首版脚本R12经独立反例审查为REQUEST_CHANGES：备份路径、物理/alias启动身份、日志正向来源、后续case生命周期、源码SHA绑定及窗口/进程门等存在缺口。作者19项绿色测试没有覆盖这些真实反例，故整体封存为被拒提案，未运行实际planner/stage/launch，也不改生产修复。R13只从已验证N6/R11做首程F1最小机械准备；F2必须继承真实已付入宫并原生保存后的postimage，再单独核验冷读返回，不预造玩法PASS或自动连开。

本机证据：`native-n6/post-native-audit.json`、`native-n6/postimage/`、`full-flow-preparation-review/`、`full-flow-r13-decision/`。受管#101租约已按真实同owner/session正常续至12:43Z，其他184行及169计划保持；续租不是Native或安装许可。正常包/存档未改，完整收费—保存—冷读—返回及端部镜头仍待实际验证，未合并安装。
