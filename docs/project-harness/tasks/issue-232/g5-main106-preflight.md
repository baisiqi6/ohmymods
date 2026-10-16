# Android #232 G5：main106 验证准备与环境预检结果

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

单次 `swiftshader-main106-20261016-log-scope1` 的正常计划为8816b473c87486defd7a75eb7cc5a55d54aafd7d754e766f7593ec04265b80f7，06:35:28Z已通过正常登记、177/161保护、validator和doctor；本分支plan.md逐字镜像该计划，未裸改checklist/DB。前文15b1、2c8a和b2d960是各阶段历史计划。具体actualGLM单用途门与canonical截止澄清保留，原source/raw/spent不改。

本次在原60秒准备上限内完成原Main/cfg、Release、精确logs与UserData的host/device备份及读回。候选pair实际安装读回；当前PID的新鲜日志中106注册顺序、组件创建、preloader→loader→main目标均达到，main回调为23:38:04，零UI操作。唯一PNG已人工查看：黑底加载条，未见world、面板或浮球；有效PNG和main回调不代表画面就绪、可游玩或触屏验收，不据单帧定案图形问题。

游戏停止后Main/cfg由唯一pair helper恢复并独立读回5460efd9/9758bda8，Release字节/Mods列表/四运行库/APK核对保持，UserData文件及目录差异为空。guest sync ACK不提升为再启动持久性证明，不声称日志或整环境恢复原样。Root真实wait返回-9并于06:38:25Z记录回收；父Root进程exit0与case/facet结果分开。06:39:08Z host后快照无VM/game/5582–5584监听、PC正常DLL和localChecklist保持，工作区当时clean。资源已提前归还，未追加病例。

本次非作者actual独审为verified_single_case_startup_and_scoped_restore，报告SHA1f210e1c…、2665稳定pins由Root再次核验，mustFix=[]只表示本范围证据一致，不是玩法或完整native验收。允许的33文件device备份及host证据原样保留；Latest.log改变的原件与本次版本都已留证，运行后Logs归档未再次inventory，不声称全文件系统零残留或日志全恢复。完整#232继续OPEN；九值运行读取/触屏、实射/转职、完整Holder/sync/FX池、保存事务/换岛读档、手机平板及多人长期仍未验收。本轮不发布APK/loader/game，不把启动目标通过写成完整移植完成。私有证据：`.local/tasks/android-profession-archery-20261014/g15-loader-log-scope-20261016/`。
