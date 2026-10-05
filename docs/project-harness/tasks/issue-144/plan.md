# Android 公共面板批次计划草案

状态：GLM批次草案方向已批准；本最终文件仍需精确绑定。须PR143/Issue142正常done/closed释放后登记新Issue/plan/assignment。
目标：PC现深色卡片与金色视觉基准、中文文案、共用绘制控件；Android小浮球不变，触屏分区/可滚内容减少遮挡。未来Android功能只接入同UI路径。Dense产品顺延，调查保留；当前15配置/25hook/原36源码功能不扩容。功能逻辑driver仍为Probe.OnUpdate＋OnLateUpdate（现Y维护），本UI不新增driver。

1. 批次输入：合并后的真实sourcehead，PC源Version真实csproj/全部资源。对2299→live已知7path delta只增量盘点：当前准备232CS/57Bind/34declaredresources，原230/54/33冻结记录保留；合并后default/optin各一次actualSDK10diagnostic，逐行错误去重/分层，不create configstub/fakeAPI。不重复全230研究或称墙基已移植。
2. 共有源码：从il2cpp/ModPanel.cs现palette/style/Texture/stylefactory提取纯UI样式源码（拟ModPanelStyles.cs）；提取纯绘制Card/ToggleAction/ValueStep形态（拟ModPanelControls.cs），和实际共用中文title/status/featuretitle（可以并在同控件file常量，少量常量非54配置registry）。PC+Android.csproj link同files，PC保持原font/字号/几何/控件行为、ConfigEntry和shortcut/native驱动原位。Card纯绘制允许调用方提供Rect/布局规格；PC title width−210/value162不能原样套窄页。共享style纯factory不写GUI.skin/matrix/enabled/depth；调用方save/restore/skin持有；PCsharedstyles缓存事务create，只借原生fontchain；不新genericISettings/controller/feature-registry/事件/扫描重试。
3. Android容器：FloatLayout一个外panel来源，读取Screen.safeArea在Probe平台层转换top-left交纯geometry；球48/72和原边贴/drag/默认折叠。展开边缘小面板固定外viewport建议约min(320*scale,可用宽)、min(480*scale,可用高)，1280×720最大153600px²≤旧World179200px²；FloatLayout从屏幕/safearea求固定外矩形，页面内容测高只决定scrollMax，不反向改外panelheight，因此不要上一帧panelrect镜像/新旧union遮罩。具体尺寸由真实小屏几何验证；不全屏。标题“王国 · 增强设置”、固定关闭、中文分类短行/导航，content viewport裁剪。换页与开关清当前scroll/gesture；累计当前真实卡片/中文字高度，不每页640/562等固定height，不另featurecounttable。PC cards两列title/value可原样；Androidtitle/value合理分行或窄端并列，help按CalcHeight包行，动作touchheight约48logic以上，额外参数采用既有Cycle/Toggles不改变档位。共享偏移/字号显式scale，PC调用1因为其GUI.matrix已scale，Android传Layout.Scale且GUI.matrixidentity，禁止双缩放。未来append card返累计height，无临时另页height。
4. 滚动输入契约：原TouchClaims/FloatInput/two transparent native UGUI Images不替换，同FloatLayout物理外rect。浮球事件先于panel捕获（窄屏布局重叠仍球优先），一份公共panel hotControl gesture（推广已验浮球MouseDown/Drag/Up模式）；卡片不用GUI.Button抢hotControl，contentviewport内tap于release命中同起始action rect才调用现action；固定导航/关闭同gesture但不滚。只能Began在contentviewport成为滚动owner；Tap→Scrolled同finger单向，超过阈值之后直到release禁止action，release在任意区域吞本指并归还自有GUIhotControl（不清别人），未过阈tap仅一次现action+一次原Save。ScrollWheel/桌面emulator鼠标也自然工作。native外指从不被面板接管；多指在外的游戏操控保持。失焦、关页/关闭、非RunningGame、销毁取消自有gesture和hit对象，不新增gameplayhook。Probe finally保存恢复GUI.skin/color/bg/content/matrix/enabled/changed；不修改game全局skin/font。
5. 功能接线与中文：既有Player/World/Generation/Vegetation pages统一commonCard/Toggle/Step方法；可保原页面逻辑布尔结构到最小改动，或归并独占Page enum去掉多布尔互斥（worker必要范围由review）；人口八native角色+骑士只中文展示，Calendar只日期季节/状态文案中文，Tick采样/计数/日志行为不改。名称基准现PC: 君主移动速度、坐骑无限体力、长按连续购买、坐骑技能冷却、法杖神器冷却、每波怪物数量、怪物时间线推进、无限金币、快速建造、船只额外乘员、地图大小、森林快速消退。Android限定如staff基础部分/下次使用、地图新岛、建造之后初始化、猫后续载入且已有保留、Greek普通鹿目标/补充×3按已批native事实准确说明。Calendar/Population PC暂无15设置中的同功能字段引用也不能强拉全部services。
6. 现有冗余清理：去Probe旧label/title/button样式factory，去各MobileMenu重复固定64rows坐标/returnHeight，去旧页面PanelHeight数表；若Navigation统一则去失用bool/Back变量，不保两套menu+registry。原inputSurface/touchownership和renderfailure退出仍有独立第三方边界用途保留。共享样式创建cache可析构释放仅自有纹理；不引入多层fallbackFont/重建/每帧扫描font/skin。
7. 实现角色：符合AGENTS功能worker派工按freshBeijing时段。Root在Probe.cs统一入口UI路由/version experimental0.0.20（game hooksetup25不变）与正常plan/docsprogress；Worker允许上述共用2files/PCModPanel视觉调用替换、FloatLayout/newpanel容器/gesture必要purehelper、4Menu/Calendar/Population纯presentation、android csproj/adapter测试/readme；不MobilePlayerConfig/nativegameplay/Coredetours/Save/PC部署/RootMain/根Mono/GH/canonical。Corecode边界PCstyleprovider便于worker单一owner，RootProbe适配接口由合同定死。
8. 验证与交付：必要Mainactual SDK10 0W0E+一次strictAdapter（旧hash机械对新sourceactualpin不加skip；旧fixedcoord/Englishassert替换成新layout/Chinese/gesture实际helper检查，原15config/25hook/Nativebinding+其他sourcefreeze全部保留）；PC完整SDK8不部署、resource34actual覆盖＋具名ModPanel/UIhelper差异audit，所有非UI方法/配置写路径不变。不构建Mono；不每小功能单独pack/review；一码批sourceReview→一次模拟器安装门→一次APK/Main最小安装→Chinese/screenshots/lastcardreachable/scrollnoaction/orb边拖&折叠/nativeMenuoverlap/outsidepointer/focus必要回归→deviceclaimreview→exactdeliverygate→normalPRmerge/codeIssueclose→canonical记录。设备备份Main/prefs/native档保全；原玩法不在UI批重做全验。CJK若实际缺字先定点查实际native字体边界，不未经证据盲扫或向用户微审批。手机/联机/所有玩法pending如实记录。

预估本公共UI批工程4–10小时，交互字体若实际存在平台差异另加已确认处理；总全移植原规划80–200h大区间仍保留，增量232/57/34不等于功能移植；本UI批完依第一功能批native证据再收窄。GLM01a10927/glm-5.3max/noFallback两轮方向审查通过，最小修正与替代均保留；本文件精确绑定再normal批准，不视已source通过。

方向审核6项的修正/最小替代：
- 共享控件无ConfigEntry/MelonPreferences/Il2Cpp.*，PC配置修改留原方法；卡片/Button外观共享，不新增Func<string>动态值镜像或genericactivationregistry。PC原GUI.Button与Slider都保原Event.current门控+GUI.changed合并，Slider/Archer/Restock/Float数学不提取不改动（抽取palette/style/card足以复用；这样不用为纯UI改写全部PCslider）。若worker确需移动则完整旧逻辑逐字保留并验证0.375在Repaint不改值。
- stylefontSize scale变化在调用方同一provider.SetScale，PC1/AndroidLayout.Scale；终端fontchain实际验收仍后置。
- panel级hotControl握唯一UI手势；越10*scale之后零action，drag反向/回原位仍Scrolled。Map/World末卡用scrollMax可达，裁剪显示和hit都用viewport。
- 中文height CalcHeight用于contentcard实际测高累计，PC固定122px geometry保持；固定外viewport不接受一帧内容测高union扩大遮罩，因此取消“上一帧panelrect”方向5的补偿，屏幕safearea更改统一Resize边界让GUI/filter/surface读同对象。
- oldfixedheight/English测试改不变量不加skip，原严格SHApins/15配置/25hook/compileactualbindings保。

## 正常任务关联（机械追加）

GitHub Issue: https://github.com/baisiqi6/ohmymods/issues/144
Canonical task: issue-144
Canonical plan: docs/project-harness/tasks/issue-144/plan.md
Dependency: issue-142 done/closed（正常收尾与独立回读完成后接受）
Implementation branch: codex/android-common-ui
Owner: mac-codex-ohmymods-android-operator
Session: codex-android-common-ui-20261004
