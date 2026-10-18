# Android #232 G5：main106 验证准备与环境预检结果

## Android 专项协作路由（2026-10-18 北京时间用户更新）

本节适用于当前移动端适配专项 Operator 的后续派发，覆盖本专项原有的 Worker、Reviewer 模型与时段安排；不改变其他 Operator 的协作规则。

| 北京时间派发时段 | Worker | 独立 Reviewer |
| --- | --- | --- |
| 工作日 09:00≤时间<12:00、14:00≤时间<18:00 | Codex subagent，`gpt-6.1-sol`，思考强度按任务选择 | Codex subagent，`gpt-6-astra`，`high` |
| 其余时段，包括午休、夜间和周末 | OMP DeepSeek Flash，`thinking=max` | 独立 OMP DeepSeek Flash session，`thinking=max` |

方案、决策和实现的独立审查均按 Reviewer 列派发。Operator 负责拆分、共享契约、集成、验证及交付，Worker 与 Reviewer 保持独立；每次派发记录北京时间与实际模型证据，不静默替换。新规则从本次用户指令起生效，已完成的历史审查按其原始模型与证据保留。

2026-10-16 UTC，Mac Max / Codex / Android Operator。产品基线 `351aa35466d710a3858828ed1d79ba984f44977c`；G4 PR #244 已合并。本批只修改本机测试控制脚本并记录证据，未修改产品源码、Mono、版本或发布包。

## 保留的产品与本批测试能力

G4 Release Main `c906ca77e86bdd8842a1d0cf5bf01405179a58a6566b33174ee295a152daa4fb` 保留，106 项显式注册及职业/弓弩公共中文面板仍为已合并源码成果。受控配置 `ac31c005cd5d6cbce47348f17b977dee96f6629dda6bc177c6e68277093c2890` 有40键；六个布尔值为false不代表全功能关闭，职业身份生产者仍可能写旁档。

本机复用原 `common.py` 和 R5 成对 Main/cfg helper，未复制多套 runner。新增冻结 CaseSpec，将106注册和可信主场景分为两个目标；规格原字节 SHA 在 prepare/supervisor/runner/Root 逐阶段核验。boot90秒、prepare60秒与 supervisorT0 起180/220/225/230秒分相；Root 亲自 wait/join，记录返回码与回收时刻。

计划在安装前完整备份 UserData 文件、目录及不存在状态，并在退出/成对恢复后只读比较 new/missing/changed；未知变化或读取异常保留当前数据及备份、阻止后续自动 case，不覆盖新 ModSave。目录状态仅接受 test 返回0/1，其他退出码不冒充不存在。截图只在 PNG magic/IHDR/尺寸有效时记 captured；恢复、UserData、截图、game0、Root 回收分别记录。

这些是源码和主机验证能力，本轮两例都未执行到 UserData 备份或候选安装，不能写成设备保护路径已验收。

## 必要审查与主机验证

作者控制器部分交付后由 Operator 修正实际遗漏：恢复分支返回值、严格规格绑定、UserData 目录/错误面、有效PNG、Root 参数和 QEMU 身份接线、wait异常/超时事实与包含自身的源文件 pin。初始红例和作者未完成记录均保留。

独立源码及正常登记脚本审查均 `mustFix=[]`，实际 GLM 5.3/max 非fallback决策与条件回执留本机。真实 runner + R5 pair 的五项主机用例通过：106+main、106无main按时退出、缺specSHA拒绝、错误caseRoot拒绝、UserData读取异常独立unknown并阻断下一例。原parser31项及规格8项、Root/reaper/预算/PNG/目录反例通过。旧runner14项功能用例通过；第15项为旧作者阶段“未来审批文件尚不存在”断言，因合法历史审批文件存在而失败，原红例保留，未删除历史文件或改断言凑全绿。

增量静态 native 盘点将新增21 owner绑定到20 distinct target；当前APK metadata/map均匹配、未见不同方法原生地址折叠。8个新入口仅作有限指令检查；函数body范围、ABI、detour与运行效果仍未证明。

## 两个独立实际用例

| 用例 | 实际走到的位置 | 未触及范围 | 收尾证据 |
| --- | --- | --- | --- |
| `swiftshader-main106-20261016-single1` | boot阶段 `sys.boot_completed` 单条2秒读取超时，空stdout/stderr；83条原始命令，无prepare子进程 | 无备份、候选安装、game launch、106注册、主场景观察 | Root wait返回-9并记录回收时刻；之后无VM/game，5582/5583空闲 |
| `swiftshader-main106-20261016-single2` | boot完成46.506秒，身份、game0、API35/ARM64检查通过；prepare第9命令读取设备原Main时2秒超时，空stdout/stderr | cfg未读，备份、候选安装、游戏和pair ledger均未开始 | prepare停止回执false原样保留；Root随后明确wait返回-9、已回收；之后无VM/game，双端口空闲 |

single2修正的是boot读取预算：读取启动属性使用原90秒阶段的剩余时间，不增加总预算或重试；本例实际通过boot阶段，不证明其他读取或后续功能已好。原Main297472字节是此前预期值，single2本次没有读出字节，未重新验证其大小或内容。文件读取阻塞的原因尚未确定，不归因为网络、内存不足、资源缺失或 Mod 故障，也不将两个超时直接定为同一根因。

single1原Root回执预填 `mainOrCfgEdited=true` 是控制器源码错误，不能当作实际写入。原回执不改，独立纠正说明及执行Root原字节 `ffd3f2b1…` 已保存；single2删除这项预设，实际写事实仍取pair ledger。两例 `restoreProven=false` 只表示没有恢复证明；两例均未到pair写入，不能据此宣称恢复失败，也不能宣称Release/UserData设备字节保持。

single1规格 `eba7afa4…` / manifest `604249e0…`；single2规格 `17bc52c5…` / manifest `5f5e6608…` / Root `1dac5dbb…`。复用packet路径的历史source pins按对应执行版本解释，不能用当前文件替代旧执行字节。两个具体gate均已原字节归档为spent，未复用或自动重跑。

两例实际结果均完成非作者独审，`mustFix=[]`：single1为`verified_actual_boot_transport_inconclusive_no_pair_mutation`（报告SHA `7dadd0fb…`）；single2为`verified_boot_success_then_inconclusive_original_main_read_preflight`（报告SHA `4705c1d7…`，379项实际pin）。Root再次核验报告pin；原始失败、停止回执及边界保持。

## 登记、保护与后续边界

正常入口先登记single1计划 `de160136…`，再登记single2计划 `15b1ad8de1e050cdba498b1d8f2099137208ac7282248ef36d14bd62553a7af1`；后者是当前计划，分支 `plan.md` 与之逐字一致，旧本地 `d8b48d11…` 仅为历史。两次均保持原owner/session/operation、doing-ready-running与lease，177个其他任务对象/顺序及161个其他计划保持，validator通过、doctor 0 errors/0 warnings；未裸改 checklist/DB。本机主目录7项未拥有改动、PC正常DLL、独立任务工作区源码均保持；测试窗口已提前交还主Operator。

完整 #232 继续开放，106 Main 本轮尚未实际加载。主场景、九值实际读取/触屏、射速散射/希腊火矢/弩矢、完整工具/投射物/技能/死亡撤退FX/召唤物、Holder/sync池重建、职业保存事务/换岛读档及手机平板/多人/长期玩法均不因本批通过。

继续有限离线池依赖审计，复用既有同APK资源索引，只沿NorseArcher、Crossbow和GreekFire/Impact具名根追踪；custom serialized refs未解析处如实保留缺口，不加register-all/forcePoolFix或无依据LoadAll、扫描、重试。ADB读阻后续先用最小只读探针区分guest文件系统、输出通道和冷启动就绪；本批不再盲调读取时限或开启第三例。

私有证据根：`.local/tasks/android-profession-archery-20261014/g5-main106-runtime-20261016/`，single2在其`boot-budget-main106-single2/`。原始APK、native、设备/玩家数据及模型完整流不随本次文档交付发布。

## 2026-10-16 后续 G9–G14：资源责任、测试基线与准备范围

上文“当前计划15b1”仅代表G5时点。G12已正常登记2c8a，G14已正常登记b2d960；完整#232仍开放，分支计划镜像按本次最终登记结果同步。每次正常登记分别核177个其他任务与161个其他计划不变，validator通过、doctor0 errors/0 warnings。

G9沿同APK具名箭资源根解析三种Arrow组合及有限直接边：_impactSpawner实际类型为ParticleSystem，同SO HitObject存在null跳过Play分支，不据null追加PoolSpawner补丁。Buff receiver有限原生路径读取本地BuffData与渐变overlay。静态依赖责任不代表整个池链、对端或视觉玩法验收。

G10读到原Main空字节；G11另次明确Main/cfg都是普通空文件，保留实际空原件后，只将这两成员从已核G2备份分阶段push/readback/sync/mv恢复。第二次独立只读启动六文件匹配，原Main297472/5460efd9、cfg875/9758bda8。该有限恢复和重启证明不确定此前变空原因；旧同boot读回与后续空文件事实并存。

G12原Main shell2秒超时，G13 exec-out首读2秒也超时且未执行旧shell对照，两者均无当前文件内容证明，不归因单一通道。G14仅修任务私有prepare预算来源：Root与child Timer/read共享同一绝对60秒截止，保持原shell -T cat及checked语义，无重试/fallback，不改common/runner/writer或产品源码。旧冷读RED保留，独立13项host通过。

G14单次实际已完整读到原Main297472/5460efd9、cfg875/9758bda8和四runtime pins；随后prepare60耗尽，未到devicebackup、候选安装或游戏。原“日志父目录”实际193项包含net6、Dependencies、Il2CppAssemblies；573逐项test后开始内容读取，host partial日志目录21文件24,827,625B，Latest.log尚未备到。部分host原件/raw/spent门均保留，不称备份完成或游戏失败。

G14实际独审报告1f05494c…，2254稳定pins由Root再次核验；Root真实wait0与后续host无本案进程/端口分别留证，prepare进程快照不代独立wait。候选c906/106到G14仍未安装或加载，九值/触屏、攻击、转职、保存/完整对象池及手机多人均未提前验收。后续G15只修日志保护入口为Latest.log+完整Logs归档，不扩大prepare预算；实际结果单列。

## 2026-10-16 G15：精确日志范围后的单次启动

G15仅修改私有prepare日志入口与Root prepare pin：保护Latest.log和完整Logs归档，使用原Case.read/snapshot/verify_tree，不遍历或复制其runtime父树。旧错误范围RED保留；新日志7项、阶段reader12项、原UserData1项及六源AST通过，独立20项重跑和76项source pins已核。产品Main c906、cfg ac31、common、runner、writer、所有预算均未改。运行库仍只核四项pin，不声称备份全runtime树。

单次 `swiftshader-main106-20261016-log-scope1` 的正常计划为8816b473c87486defd7a75eb7cc5a55d54aafd7d754e766f7593ec04265b80f7，06:35:28Z已通过正常登记、177/161保护、validator和doctor；本分支当时的plan.md逐字镜像该计划，未裸改checklist/DB。前文15b1、2c8a和b2d960是各阶段历史计划。具体actualGLM单用途门与canonical截止澄清保留，原source/raw/spent不改。

本次在原60秒准备上限内完成原Main/cfg、Release、精确logs与UserData的host/device备份及读回。候选pair实际安装读回；当前PID的新鲜日志中106注册顺序、组件创建、preloader→loader→main目标均达到，main回调为23:38:04，零UI操作。唯一PNG已人工查看：黑底加载条，未见world、面板或浮球；有效PNG和main回调不代表画面就绪、可游玩或触屏验收，不据单帧定案图形问题。

游戏停止后Main/cfg由唯一pair helper恢复并独立读回5460efd9/9758bda8，Release字节/Mods列表/四运行库/APK核对保持，UserData文件及目录差异为空。guest sync ACK不提升为再启动持久性证明，不声称日志或整环境恢复原样。Root真实wait返回-9并于06:38:25Z记录回收；父Root进程exit0与case/facet结果分开。06:39:08Z host后快照无VM/game/5582–5584监听、PC正常DLL和localChecklist保持，工作区当时clean。资源已提前归还，未追加病例。

本次非作者actual独审为verified_single_case_startup_and_scoped_restore，报告SHA1f210e1c…、2665稳定pins由Root再次核验，mustFix=[]只表示本范围证据一致，不是玩法或完整native验收。允许的33文件device备份及host证据原样保留；Latest.log改变的原件与本次版本都已留证，运行后Logs归档未再次inventory，不声称全文件系统零残留或日志全恢复。完整#232继续OPEN；九值运行读取/触屏、实射/转职、完整Holder/sync/FX池、保存事务/换岛读档、手机平板及多人长期仍未验收。本轮不发布APK/loader/game，不把启动目标通过写成完整移植完成。私有证据：`.local/tasks/android-profession-archery-20261014/g15-loader-log-scope-20261016/`。

## 2026-10-18 UTC G16–G17：APK 核对职责与浮球观察

G16 的单次 `swiftshader-main106-20261016-gui1` 已在 prepare 内完成33文件的 host/device 备份；随后 runner 重复计算1.9GB APK的完整SHA，在原30秒上限内超时且 stdout/stderr 为空。未到 pair 写入、游戏或截图。独立实际审查报告 `65586cad…`、1832项 pins 已核；Root真实 wait=-9，原失败、备份、raw与spent门保留，未把 restore unknown 提升为恢复证明。底层读取延迟原因仍未知，不据此认定 Mod 或资源故障。

G17 在私有测试控制器的责任处去掉 runtime 两次重复完整 hash：prepare 只做一次完整 APK SHA，并在其前后及 runtime 安装前、恢复后核对同一 checked 安装路径与普通非链接文件的完整 identity。字段含 device/inode/size、完整 mtime/ctime、mode/uid/gid；缺失、变化或读取异常均拒绝，不退回重算或重试。`apkSame` 与结束后完整SHA保留 null，独立记录 `identityHeld`，不冒称末次全字节校验或对抗 root 篡改证明。固定总预算、唯一 pair helper、GUI单图/下一fresh采样和产品候选均不改。54项源码用例与6项AST检查、独立源码审查已完成；旧重复hash红例保留。

新单次 `swiftshader-main106-20261016-gui-apk1` 使用原 c906 Main、ac31配置及351aa354产品源码。33文件 host/device 备份逐文件核验，实际四条 push→sync→readback 链分别完成候选安装和原 pair 恢复；另行读取原 Main `5460efd9…`、cfg `9758bda8…`。Release原存档、Mods清单及四运行库保持。prepare一次完整APK SHA为 `9c6d65d1…`，四阶段原始stat字节一致、`identityHeld=true`、APK写入0；没有结束后的重复完整hash。

本次 fresh日志达到106有序注册、组件创建、preloader→loader→main，以及component/main之后唯一 `OHMYMODS_FLOAT_GUI`。正向候选只取一张PNG，再用下一次既有可信PID/fresh采样确认。实际PNG为1280×720、8809B、SHA `f96d84af456e0bb825864221c6fe84acd61605ae58aeb40a78097e0af1bd1c66`；人工及独立查看均见黑底、加载条和小金色浮球，未见世界或展开面板。没有tap/swipe；日志、浮球可见与画面/触屏/玩法验收分别记录，不凭单帧定案图形根因。

UserData原19个文件字节保持，无 missing/changed；新增 `KingdomEnhancedMod/ModSave/hero-identities.v1.json` 及其两级目录。本次diff仅采新路径的存在/类型，未读取新JSON payload，大小、SHA、schema及付费身份持久性未知。状态保持 `unknown-preserved`，当前文件与备份均保留，阻止下一自动case。精确351aa354源码表明既有成功加载后的 ConfirmBaseline→Commit→Store.Save 能创建该路径，与本次 loaded:fresh 日志一致；这只是写入责任线索，不代替内容或重启验证，也不据六个开关false新增 Enabled 守卫或删除旁档。

Root于03:30:40Z真实 wait=-9并记录reap；父进程exit0与case各facet分开。03:33:02Z host后快照无本案VM/game、5582–5584无监听，PC正常DLL和localChecklist按当时基线保持。资源已提前归还；主Operator后来按用户授权于03:43:34Z安装新正常Mac DLL `4a388011…`，不将这一已知后续变更误报为本例保护失败。本例仅证明 scoped pair 恢复，不声称日志或整个环境恢复原样。

本次独立实际审查为 `verified_gui_observation_pair_restore_with_userdata_delta`，JSON报告SHA `c93b74a8d474310e89a6ba82ea7cac75c9ac225b4dcada97f9d00ce28fb72898`，`mustFix=[]`；2689稳定pins经Root再次核验无漂移。`aggregateNativeOrGameplayPass=false`、`entireEnvironmentRestored=false`，旧失败和旁档缺口不改写。该审查早于本专项新路由指令，保留其原模型记录。

正常入口在原owner/session/operation下完成续租和精确计划登记：当前计划 `f1e957521f75b167205cd187c3f6d3dd519b232bfa4fc75850444b8e2d34faf3`，本分支plan.md逐字镜像；原8816、75a7等为各阶段历史计划。03:27:52Z续租到05:27:52Z；续租保持全部167计划，登记只改232计划，182个其他任务对象及顺序、166个其他计划保持，validator通过。全局doctor实际为3 errors/0 warnings，均为已有247/251问题，232无blocking finding；本批只按精确无关finding集合核验，未修其他任务，也不报告全局全绿。私有管理员脚本增量经独立审查与正反例验证，不修改产品源码。

完整#232因交付范围尚未齐而继续OPEN，不发布APK/loader/game或变更版本。下一组先核对已合并共享保存差异，再在新计划/资源窗口内最小只读分类新增旁档；普通弓箭/弹丸池、职业付款/保存/池重建按真实依赖推进。九值运行读取与触屏、攻击/转职、完整Holder/sync及FX链、换岛读档、手机平板、多人长期仍待验。G17只验证旧c906/351aa354候选，不将后续主线f60a83d6产物视为已装机。私有证据根：`.local/tasks/android-profession-archery-20261014/g17-apk-verification-scope-20261016/`，actual在`native-single1/`，本轮登记与增量审查在`resume-20261018/`；原始设备、游戏和模型完整流不随文档发布。

## 2026-10-18 G18：共享保存增量与首次只读分类

G17证据收尾PR259已合并为3b340713；随后自有源码前移到f60共享保存修复，再前移到已合并PR249的906da521。旧c906/ac31、原raw/backups/spent及G17四个可变工作树输入的精确归档保留。351→f60的11文件增量经Worker盘点、独立Reviewer与Root文件/日志绑定核对，维持Android既有行为无需新增适配实现；当前前移是source-only，未安装新Main或APK。249修改GreekScaleScope与对应3个Android测试pin，Android仍走原GreekOnly；真实Decode的9个编译输入在906与3b逐字相同。Main另行安装的正常Mac DLL8345是已知外部授权变更，不混入Android验收。

五个Persistence owner与三个NativeSave gate的target/handler契约在351→f60保持，三个writer类本体未改。当前APK索引可定位八入口；method-map按ptr聚合后本就全唯一，其family=1不能当无folding证明。metadata定义同entry计数仅排除第二个同址方法定义，仍不排除thunk/generic/shared body；Observer只有type+name唯一定位，token未证。只对AsyncWriterGate的260B固定片段完成独立线性反汇编核对，局部条件直接边在窗口内、末尾RET、8处BL调用（5个不同callee）未跟踪；不提升完整extent/ABI/inline/detour/runtime安全，不外推另七入口。G2四个72是async/sync两面的code/bits镜像，physical648的harness终态不是两面失败或实付保存通过。原错误口径及修正记录保留，未据此新增Mod补丁。

首次分类工具复用Case台账、原stat/reader和LauncherReaper；仅在唯一底层command传递阶段剩余预算，删除冗余adb/shell包装。Root真实Case调用链复现旧TypeError后修正；host进程判断按本机真实comm列完整字段取basename，删除args恢复分支并拒绝空表。登记前共用纯plan/window校验，防止先写坏计划再在下游拒绝。最终源e01cd322…、测试ff871c70…经独审，Root22项host及2个空表反例通过；登记producer正1/拒6，真实Archive.Decode薄入口6fixture（含非空历史记录）通过。它们仅证明源码/host接线，不代表真实旁档、paid或设备通过；未造业务parser/stub。此前Source/预算/审查中止与修正均留证，不将无正式结论的中止视为批准。

原04:22/04:25窗口因源码未齐取消，零登记/VM；新明确窗口内04:50正常登记计划063ffc6eb4b16111b58c6064acfc7fbf41e91ef68bf0299d4068a5353c23d08a，本分支plan.md逐字镜像。原owner/session/op/doing-ready-running保持，182其他任务对象及顺序、166其他计划保持，validator0；doctor仍原3E0W/232无block，不报全局全绿。plan.ready事件内嵌的10-15旧lease是历史operation快照；本例实际raw00004的live232行与normal receipt均为03:27:52→05:27:52，controller严格比较这两者才允许Popen，不消费旧快照授权。随后普通续租于05:16:17→07:16:17，只改lease/updated_at，182其他rows与全部167plans保持，未改063计划或完成任务。

唯一readonly-sidecar-20261018-single1于04:52:42启动，raw00040于04:53:05.639577见sys.boot_completed=1；raw00043在+0.146s记录game0，raw00044在+0.240s的test-L返回1，raw00045在+0.301s的UserData test-d返回1，raw00046在+0.366s的目标stat返回ENOENT。没有cat、没有payload，decoder未处理真实数据；checked shell非零触发旧helper的transport-failed标签，不证明ADB断线或timeout。只能记路径当时不可见，不定性永久丢失、存档损坏、权限或挂载根因；surface图形错误独立记录，不作因果。约27.1秒后Root真实wait=-9并于04:53:07回收，04:55后快照无本案game/VM、5582/5583空、正常DLL8345及本机checklist当前字节保持。未启动APK/游戏、未向游戏目录发写入/恢复命令，不声称VM系统盘零变化或整环境恢复。

实际结果经独立审查收口为inconclusive_no_payload_no_game_write；证据语义mustFix已补明确，仍不是目标采集通过。G17旁档payload/schema/paid仍unknown，未解除自动后继case阻断，未删/覆/重建旁档。Scout对照旧成功只发现：G17直到boot标志后约2.65s才证实UserData可读，且media进程状态不同；这支持进一步测用户/存储状态，不能据此添加sleep/retry或定案。该时点计划一次新的具名存储状态诊断，不复用spent或旧窗口；后续执行结果见下文。完整232必要交付/验证范围继续逐项核对，不以玩家实机未验作为唯一关单理由。私有证据在`.local/tasks/android-profession-archery-20261014/g18-shared-save-sidecar-20261018/`，原始设备/用户数据及模型流不发布。

功能范围盘点随后经独立审查收窄：当前906的232具名职业/弓弩闭环没有已确认的源码缺口；当前OPEN的理由是关键共享保存body尚缺真实付款/晋升→保存→同会话装载的兼容触发，以及两池InitPools后的必要生成/攻击/回收检查。手机平板、多人长期和完整FX观感属后续玩家验收，不能作为唯一关单前置。既有构建按完整inputs相同可复用，不能因merge hash变了强求另编；旧c906的106注册成功已蕴含每owner count=1，不再要求重复回读目标数，但也不称新fd969已装机。GhostSquads四hook属于SummonGhostSteedAbility，当前职业池链不调用它，是232范围外候选，不补注册凑完整。Hero/Msk/HeavyShield商店已有真实Tick/付款producer，不因Castle/Ninja未启而新增重复商店机制。下一必要运行组按当前保存body、具体producer和池交接合并设计，目录可见性先作最小诊断，旁档实读可纳入随后必要备份/保存验证，不再为每个只读字段单独全交付。


同日新诊断已按独立源码审查收尾：objective明确为diagnose-storage，8个系统查询、10条具名raw stat和一次game0采样各执行一次；capture与diagnose的marker/verdict互斥，diag不cat、不回退为采集。最终controller c148deaf…/测试694a95fc…/admin b46be7ce…经独审，Root与Reviewer各29项host通过，登记producer在最终c148上正1/拒6重新核对。Main明确交还N0资源后，新的05:55–05:58登记窗及05:58–06:05原生窗才生效；正常入口将计划从063更新为58161139…，operation/lease07:16:17保持，其他182rows/166plans、validator0与原doctor3E0W/232无block边界保持。Root独立远端读回与本分支plan逐字一致。

readonly-storage-state-20261018-single1实际30.21s，boot23.83s，8查询/10stat完成、0 unknown/超时/非零退出，cat0、game0，无APK/game/UI启动或设备文件写命令。当前user0、CE=true、boot=1，而sm采样显示emulated;0仍checking；随后/sdcard解析到/storage/emulated/0，全部具名祖先及target stat可见，target为562B普通文件。该同次时序直接证明boot标志与卷状态不同步，支持先前目录过早读取的假设；两次启动样本不能证明上例ENOENT的唯一根因，也不能从metadata推出旁档payload/hash/schema或paid。原件/备份保持保留，G17内容未知未解除。后续必要场景备份/保存可合并读取旁档，不再单为每个字段新增整轮case。

Root于05:58:41.934Z真实wait=-9/reap，自有QEMU退出；05:58:55独立host检查无game/VM、5582/83空，正常DLL8345与primary checklist efb52c89字节保持，窗口提前归还Main。本例只完成只读诊断，未安装新Android产物、未读取正文、未验真实付款/原生writer/池重建或玩家设备；不将无写命令等同整游戏目录字节恢复证明。私有新证据位于上述G18目录的storage-diagnostic/{normal,actual,review,host-after.json}，历史失败、已执行源码及spent均保留。
