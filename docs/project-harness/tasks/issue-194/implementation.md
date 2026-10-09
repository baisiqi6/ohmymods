# Issue194 实现与验证边界

本批接入 Android 狗与隐士防抓，默认关闭；使用现有小浮球、PC 金色中文通用 Toggle 与触屏滚动。自动找回/跨岛召回仍为完整移植的明确后续依赖，界面如实说明，未宣称完整角色功能完成。

复用两份 PC receipt 源与两个 Droppable 长生命周期入口，每个目标一个薄聚合 handler，现有 Probe.OnUpdate 同序调用 Hermit→PetGuard。ANDROID 编译隔离召回专属字段、表、helpers与spawn/save调用。首次绑定按真实scene+layer判断，未归属不同scene不等于离开世界；无扫描、超时重建或新的更新驱动。正常OnDisable策略reset归native；callback异常未必到达reset，不增finalizer猜测回写。

第一轮独审发现同GO双角色receipt会在外部接管后互相覆写，并发现host Tick反序。实际同产品顺序、直接生产源红测23通过/2预期失败/196检查。修复在Hermit receipt创建处按真实同对象Dog分类，dual初始generation由Dog唯一负责以保留船上OFF延期；PC条件外原行为保持。旧通过日志与未安装APK保留历史，不把旧测试全绿当无竞争的证明。无OnDisable时动态加入/删除角色组件不在已验证合同内，继续待验。

当前精确plan为9e03eda7/9687B，正常plan revise/approve保持assignment operation/owner/session/branch/lease与其它162objects/163顺序；旧f25f/7920B作为历史。实现worker实际DeepSeek Flash/max、Operator统一注册接线，修复点和源验证均独审。所有原adapter检查保留，62强pin；新host直接链接这两份实际生产源和薄handler，替身无保存/生成API，证明控制流而非native。

已实跑：Android实际interop+loader构建0W0E，Main f6e9d7fba3f479f74dafa0da9ddb744a1b298ea2b3ba5c05ba494f351aaac219 /228352B；54产品源码，GenerateAssemblyInfo查询另含1SDK生成文件。21真实MelonPreferences条目、37显式目标；默认OFF仍做真实identity/组件跟踪，不声称零native访问。保护host27/0/222；现PC宠物host32/0/154；adapters813/0、实际--oldcfg653/0，同一Main。PC全1311type/6906method/37resource语义MATCH，无PC部署。

全范围fresh盘点仍未全绿：default2E0W为原Hook/INativeDetour声明；optin111E1W含91个ModConfig引用/41成员、19个混合extension与1个CrossbowmanRole。诊断optin额外引用Bep元数据不进入产品，不排除错误源/造fake成员使盘点绿。剩余维持8–10风险组、80–210工程小时条件区间，召回事务单列，手机/联机日历单列；不按错误数折算功能完成率。

修正后的源码已独审准入。首轮APK安装/观察45.924s后已零PID停止，实际仍运行487旧Main；原development/seed策略有意保留已有文件，不能据该运行接受新功能。cfg/native bytes保持、无输入。已在打包声明产生处采用development profile加精确 `Mods/OhMyMods.AndroidProbe.dll=refresh`，只更新本ownedMain，不全局enforce、不manual热推DLL、不删除用户数据或盲重试。旧APK/日志/attempt保留；新精确包及第二次有界观察另过关口。核心game/interop、APK与原始日志/保存仅本机；公开记录不含private资产。代码收尾需相应PR全部合并与必要验证，实机结果单独留待验。

## 精确包与触屏有限观察

精确Main refresh包 f6ebbb0d4e32ec091b04faf7c8aa49e885da15ea62bee78af622e4092f550670，实际Main保持上述f6e9/228352B。CRC与签名有效，22313个原始非签名/loader成员字节保持；123份重新生成interop仅各自16B GUID heap变化，其余字节完全一致。未修改原生业务方法或向公共仓库提交这些私有输入。

第二次有界观察确认loader replaced1而非preserved1，实际0.0.25/37唯一type+参数目标、两个Droppable各1handler。98.055s零PID，两个菜单tap、实际世界/浮球/金色玩家页；防抓行在下方，未据截断画面猜测点击。第三次滚动前控制脚本错误读取记录的purpose层级，79.409s安全停止，无swipe/开关；旧log首次marker不作新启动证据。第四次修正字段与具名prelaunch log hash判据后真正23.014s启动，但调用方未分配TTY、stdin EOF主动终止，23.167s零输入停机。两项测试控制缺陷均在产生处修正，旧attempt全部保留，无异常吞掉、schema镜像或重试游戏兜底。

第五次launch-only采用持久TTY输入，同产品/包未再安装；22.932s当前新日志启动，29.468s实际森林与小浮球。玩家页在观察到的内容范围内一次竖直滚动后显示完整“宠物与隐士防抓”与自动找回待接入说明；52.276s实际开启图与True日志，59.112s关闭图与False日志，59.606s零PID。5输入=4菜单tap+1swipe，无游戏操作；0 ERROR/receipt故障。配置仅增加PetGuardEnabled=false，其余20值保持，事先具名约束逐项核验后恢复原563B配置；native save具名安装前/最终hash相同。此证据证明包更新、启动、注册与真实触屏设置，未观察到天然狗/隐士receipt写入或释放，不以没有相关日志推断角色缺席。

自然角色保护/解除、callback异常、池复用与换岛、同generation动态组件变化、自动找回/跨岛召回、手机与联机均待验；不将UI、host或注册成功升级为完整玩法验收。必要审查完成后按本批代码范围PR收尾，后续完整移植继续推进。
