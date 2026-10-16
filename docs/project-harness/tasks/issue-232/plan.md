# Issue232 — 明确日志保护范围后的单次 main106 场景验证

工作树基线 bcccb0e81d8b7a836a8ae136999c0b926aa1e6fc（产品源码仍351aa354），保留已合并G4/G3移植；Main候选c906ca77e86bdd8842a1d0cf5bf01405179a58a6566b33174ee295a152daa4fb与40键受控cfgac31c005cd5d6cbce47348f17b977dee96f6629dda6bc177c6e68277093c2890不变。六boolfalse、ratio25%/volley1/rate1不是全功能off，职业身份生命周期和旁档仍有真实写责任。

背景：G5两旧个案分别在boot与原Main读取前停止，均未装候选；原件/spent gate/实际回执不改。G9有限解析三箭prefab及同APK optional粒子、本地BuffData渐变回调，仅静态责任证明，不判完整池。G10确认原Main读到0字节；G11确认原Main/cfg均普通空文件且四运行库正确，保留实际空原件后从已核G2备份修复，第二次独立boot六文件匹配：原Main5460efd9(297472B)、cfg9758bda8(875B)。旧同boot恢复读回与后变空的原因仍未知，不能把sync当唯一根因或声称游戏问题已修。

新个案 swiftshader-main106-20261016-log-scope1，仅独立API35 ARM64 AVD5582/SwiftShader/no-snapshot/no-wipe的一次无UI候选启动；保持106/requireMain=true。CaseSpec原字节SHA跨Root、prepare、supervisor、runner逐段绑定。当前PID、deviceepoch/stat/bytes新鲜日志内有序五注册标记与trusted main场景分别成立才达此例目标。未到main如实inconclusive，不当作卡死，不延时/重试；Main后预算内一次PNG只作图片事实，无tap/swipe或额外observer/Addon/APK。

Root boot90秒与prepare60秒为前序分相；supervisorT0起soft170/restore220/watchdog225/hard230，总上限不扩。完整安装准入声明链53.0秒：evidence2.2+helper前置6.4+分类两读4+两次(push3/sync3/read2)=28.6；启动freshness三读6+am3+PID软5及尾差2.2+首host2.2+首sample6=24.4。二次启动门24.4。所有在途命令仍remaining裁剪；安装准入不足则零写/零launch。退出名义50秒，含新flush的核心pair恢复及外层/独立读约46秒；Release/Mods/refs/APK/UserData等后续面不能据此保证最坏均fit，来不及保持unknown并阻后继自动例，绝不借220..230补写或延长观察。

原common1d08/R5helper3e527冻结；六个旧packet源（含实际Root1dac）逐字归档。本例使用G15任务私有packet副本（原G12/G14已执行六源保持冻结），继续使用独立pin的G12 writer：两现有push后/readback前各一次guest sync3，ledger明确ACK None/True；同步超时沿failed-unknown保留，不假成功/不重试。实际游戏/业务源码不动，不新增扫描/占位/全池注册。主机volatile模型是区分测试契约的反例，不是对真实零字节根因的模拟定案；新helper/runner通过独审后仍须本计划typed正常登记、具体actualGLM单用途门与新的Main排他窗口才可启动。

游戏启动前依旧完成原Main/cfg、Release树、loader日志、UserData全树（含目录及ModSave存在/不存在）的host与device备份读回。唯一G12 pair writer临时安装/恢复Main与cfg，unknown pair不盲写；Release只读验证。原pair恢复后分别保留独立读回、Mods、运行库、APK和UserData每路径new/missing/changed；未知差异不删除、不覆盖新旁档，不称整环境已恢复。所有原始日志、备份、部分结果和未知面保留。Root真实持有Popen，按exactPID/lstart/argv信号保护并有界真实wait回收，身份变化拒kill后仍wait；ps退出/端口空与真实reap分记，不当恢复证明。

本例接受条件逐面记录：候选pair安装读回与sync ACK；fresh106有序注册；trusted main或如实inconclusive；截图独立；game0；原Main/cfg读回与sync ACK；Release/Mods/refs/APK；UserData差异；Root回收和端口。此例恢复读回不提升为再次重启后持久性验收。没有native批准时不运行；任unknown保留现场并停，不复用旧门。

完整#232继续原owner/session/op/doing-ready-running，不关闭。启动注册、主场景、触屏九值、射速/散射/希腊火/弩矢、保存与对象池、换岛读档、手机与多人分别留证，本个案不替代玩法或整项完成，不发布APK/PC正常版本。

G12后来在prepare原Main shell -T cat2秒超时处停止，未安装或启动；G13只读exec-out第一步也2.019秒超时，旧shell对照未执行，结论inconclusive。零捕获不等于远端零文件，单通道归因无据。G11既有完整记录中冷启动首读2.549/6.787秒、11MB读取3.014秒，支持测试reader隐式通用2秒预算不足这一范围判断，不定案历史文件变空根因。

G14只在私有prepare与Root修预算来源：Root为同一次prepare派发固定绝对monotonic deadline（60秒），可选CLI明确传入且spec必需；child核finite、未来且剩余不超过60，原watchdog和binary read共同使用这个deadline。保留common原shell -T cat与checked exit语义，read只获该阶段剩余预算；耗尽拒发，异常一次停止，无重试/fallback。legacy未传参数保持旧行为；runner、sync writer、common、产品源码不改，原53.0秒安装准入与24.4启动账目不变。boot90/prepare60/runtime170..230所有上限不扩，不承诺完整备份必在60秒完成。Root外层真实wait/单用途门、完整备份与逐面恢复边界全部保留。

这份计划尚未登记或批准设备执行；必须G14源码/host独审、normal脚本审查、实际GLM具体单用途执行门与Main新窗口全部具备，且启动前剩余至少420秒才执行一次。旧G12/G13单用途门已消费，不能复用。

G14实际首次原Main完整297472/5460、cfg875/9758与四runtime pins通过；prepare随后在60秒父时限耗尽，未安装候选/未启动游戏。raw00033日志父inventory193=6目录187文件，包括Latest.log、Logs及net6/Dependencies/Il2CppAssemblies；573逐项test后才进入内容读取，到627命令只备了21个非日志runtime文件24.8MB，Latest.log尚未备到。原host partial目录与raw/spent门保留，Root真实wait0/host后保护与端口另证，不称备份完成或游戏失败。

G15修范围产生处：日志保护只包括当前Latest.log和同级Logs完整归档目录（实际10历史文件和目录，不按数量裁剪）。Latest.log checked非link/regular后复用Case.read保存原字节；Logs复用既有Case.snapshot/verify_tree。原pre.originalLogSHA256仍来自真实Latest字节，runner两处消费不变；pre.logs显式latest/archive结构仅prepare消费。devicebackup/logs只cp当前log+cp-R归档，逐面readback，绝不snapshot/cp日志父运行库全树。不增加扫描/过滤/fallback/重试；缺目录、坏文件、symlink、remote exit/timeout仍失败。Main/cfg、Release、UserData完整保护及APK完整hash与四runtimepins均保留；只声称四runtime pin保护，不声称全部runtime树已备份或可写。Root/prepare共用60秒deadline与所有运行预算保持，代码/runner/writer未改。原错误scope补偿不保留在本例active source，原执行历史冻结不改。

本次为新独立log-scope1，G14旧single-use门已消费；离线源码/host及normal独审、typed精确计划登记、实际GLM本次执行门与Main新窗口齐备后才一次执行，启动剩余至少420秒。不提前批准任何设备操作、安装或整232完成。
