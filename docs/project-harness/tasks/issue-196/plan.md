# Android 战斗参数兼容批次计划（Issue #196）

owner mac-codex-ohmymods-android-operator，拟branch codex/android-combat-parameters，base release/v9.5.13 3f2cb37f15a15edcb8c34d30c23b6fc29fe10463。依赖194正常代码收尾，不丢已有54源码/21设置/37目标或PC中文金色触屏/小浮球成果；共享召回责任独立待交接，本组不改PC召回/保存。GitHub https://github.com/baisiqi6/ohmymods/issues/196已创建并公开scope；当前未创建canonical/task或实施产品。

## 分组与证据边界

把两项低耦合、无新资产/生成/存档的参数增强合并一批：CrownStealer六水平速度/冲量×0.75；Knight jobObject调用Kingdom.FetchArchersForJob保留原PC谓词：maxRange>=120时不改，其余值设120。共享原生移动/筛选/排序/招募机制保持，不复制逻辑。已读当前两共享源。实际Android2types/3method名/6fields，Awake2799554 FamilyVirtual且interopPublicVirtual、Fetch23d7d50非virtual，两个拟目标各唯一methodDef，无同址折叠；OnEnable2799ac8另只读不hook。exactFDE两Crown正文1284+548=1832B，无六偏移直接store，但this/虚callee副作用仍UNKNOWN。“挂点无需改”worker过强结论已由Operator撤回，不拿PC注释证明Android。声明可接不等于全部writer或玩法通过；interop六get/set只调用、不detour。

## 精确实现合同

1. 两共享源码直接Compile。Crown原Awake_Prefix和Knight现Prefix各仅加#if ANDROID真实对应开关检查；PC预处理除无效注释外原逻辑完整semanticMATCH。ANDROID不改0.75/120常量、six字段顺序/Vector2局部结构体和y保留、原Knight组件判定/minFree筛选/原catch；PC Enabled门与Android新增bool门同时保留，不把原主开关替换掉。原 >=120 谓词原样复用：真Knight的NaN与负Infinity会设120，正Infinity/120/更大值不改；不把它宣称严格仅finite低range，不新增无触发现象依据的NaN守卫。不新增逐帧写/实例镜像/池缓存/扫描/handler算法/强制restore。Crown只有Awake前缀，一对象原生一次初始化，现存/池内实例不因热切换重新应用；不能为弥补旧实例加OnEnable累乘或Update覆盖。
2. 配置真实MelonPreferences bool CrownStealerSlowdownEnabled和KnightRecruitRangeEnabled默认false，只有实际消费者此批编译；23entries。各Toggle真实变化保存一次/日志一次，同值UI无重复写，初始化不Save。主Enabled现实现保持，不新总开关、不假配置或角色slot。
3. 公共世界页同PC金色中文Toggle新增“皇冠窃贼降速”“骑士小队补员距离”，沿现浮球、布局、触屏scroll/metrics。帮助只写玩家可用效果：皇冠“下一次创建实例生效，重进游戏应用；竖直跳跃不变”，骑士“下次补员扩大至120格，原生预留与筛选保持”。不要把未知native字段/工程实施细节放玩家流程。
4. Operator统一在Probe显式两个精确长目标：CrownStealer.Awake()prefix1；Kingdom.FetchArchersForJob(GameObject,int,float,Il2CppSystem.Func<float,bool>)prefix1，绑定现internal nestedhandler的Prefix（private方法通过HarmonyMethod可达），明确priorityHigh与PC类意图一致。所有旧37真实目标保持至39，禁PatchAll/shortgetter/动态扫描，不hookCrown.OnEnable。仍原Update，无新driver。版本仅本地probe0.0.26两个marker，正式版本/tag不动。产品56共享/adapterCompile与1SDKgenerated分开计。
5. 接口/readonlynative证明并不预付启用与效果。新flags默认OFF；必要审查/真实compile后才给独立有界模拟器入口。当前bodycallees仍UNKNOWN，有限观察应只接受实际数值/注册/UI；若自然对象没有触发就如实pending，不能根据没有日志声称对象不存在、用fake生成/改保存凑通过，或在字段被原生回写时加守卫。出现首次偏离要先定位当前writer，代码范围收尾与phone/MP/长时验收分开。

## 委派与验收

worker按freshShanghai规则与actualnative模型/max核验，唯一写任务privatecandidate root：两共享source候选仅上述ANDROID gates、直接编译生产候选的minimalhost，不改W/Probe/注册/config/menu/project、PC/Mono/Git/network/device/canonical。现代C# ANDROID/IL2CPP不是C#5。host无需复制纯乘法算法：真正直接调用生产handler，覆盖OFF零写/native保持、ON四float与两vectorX各0.75/Y逐字、关闭后新实例零写/既有对象不再改、普通非Knightjob及既有>=120参数不变、真实Knight低range至120、其他原传参保持；补真Knight NaN→120、负Infinity→120、正Infinity保持及120边界，准确锁定原谓词。中途getter或setter抛错，前序已写字段保持、后序不执行且仅当前catch日志，无自动retry/二次乘，不称全六项成功或原值全部保持；不能用测试替身证明nativecallees。最小doubles只测试本控制流，不造native保存/生成API。

Root真实SDK10+actualinterop构建0W0E，compiler不能靠fake成员/删源码过；artifact强pins保留全部旧62检查、新2shared及hostfiles按实际清单增加，产品两个hook形状/唯一count/优先级和初始化/Toggle真实onceSave都由实际artifact/adapter核。PC真实纯构建（禁BepInExPluginsPath部署）与同base完整类型/method/attrs/locals/EH/resource比较MATCH，原共享PC行为回归保持；不构建Mono/运行正式PC。

独立source+actualGLM5.3max三问先审修复点和scope，再审实现。FULL编译盘点保持全部source/资源与缺口（新开关不消除其余missing config）；已有素材/配置统一机制复用。必要有界模拟器先审exactprivateAPK+Mainrefresh既有已证producerrule、签名/成员/GUID审计、具名cfg/save备份与停止/输入预算；实际UI两开关按观察布局操作，原21值持平后最终OFF。Crown自然数值与原生后续写者链、池复用/新实例、真实Knight筛选与远距招募、phone/MP/long作为独立观测边界，未发生就pending。runtime不假绿不篡既有cfg/userdata。

精确Issue公开claim和normalacceptedplan/task/assignment在Primary单写窗口完成后才functionalworker；保护194已done及其plan/101/186188192全部旧objects顺序，正常review/PR至release/v9.5.13无admin/force/CIbypass。全部本Issue代码PRmerged+对应必要审查验证后关Issue并正常codeDone；真正native问题另Issue/rootcause，不为关单宣称实机通过。共享召回合同只读没有PCwrite。

## 剩余估算

两参数小功能合并实现/审查/打包，不为每项独立完整交付。按当前完整库存维持8–10风险组/80–210工程小时条件区间，本组不按两源码或错误数换算完成率；更大对象池/身份/ABI/资源与保存责任是主要剩余风险。phone/联机验收日历另列。

## 独审修订（保留原证据）

worker原60/23调用点计数撤回；Root逐行校验完整FDE内321/137指令，Awake59处bl/blr，OnEnable21处bl/blr+1外部tail b共22。raw与worker原报告保留，机械errata单列，不改1832B范围及unknowncallee结论。选择保持原PC maxRange>=120谓词，在合同与测试明确NaN/-Infinity/+Infinity，而非没有真实触发依据地新增NaN guard；只有两ANDROID bool门，PC逻辑完整MATCH仍是要求。默认OFF只是分批启用，不作为partial字段更新安全证明；实际发生partial时日志/观察如实记录并定位首次偏离，不补retry/持续回写。
