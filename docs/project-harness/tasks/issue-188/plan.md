# Issue188 — 公共完整图片与只读随身钻石 HUD 实现批

owner mac-codex-ohmymods-android-operator；session codex-android-ui-textures-hud-20261008；branch codex/android-ui-textures-hud；base 8b435b76790327980a3a46d084ebfcea22968719；依赖186正常done。原accepted a0a27e10/7624B及两轮校准原件保留；本修订须正常 revise/approve 后才实现，不新建 operation。

## 证据与修复位置

当前 Android 只有1x1纯色 DrawSolidTexture，公共 DrawTexture 缺失使全功能编译出现3个调用差异；既有 MobileCalendar 无钻石显示。完整图片缺口在公共 Android 绘制边界解决，钱包复用已存在的 PatchUI_CalendarGems。
Round1 outerGroup→matrix→Label 的9组60,160像素确认完整外溢；Round2 matrix→innerGroup(q)→完整Label(-q) 保留图案且远侧外溢消失。独审初核：C/D保留区各1列80像素差异，delta≤35；E/F各1行80像素，delta≤64；邻裁剪线外侧亦有80个弱非黑像素。不得写像素完全一致/外侧全黑/唯一根因。A/B/G/H同坐标跨轮完整ROI相同。当前组合支持裁剪，细边取样边界与手机/DPI仍待验。首图加载过渡，钱包/中文展开面板/触屏未观察。
真实 texture 8x8、GUI640x360/PNG1280x720；两轮实际零PID分别32.834492s/25.663569s。20cfg、35hook、具名native同；不是证明从未自动保存。Round2 source190431b5/Maina2fccf7a/APK444f20fe现仍私有安装且已停机。本批产品从干净原47源开始，绝不包含 LOCAL、校准helper或旧诊断include。
本次只读七个实际游戏interop声明 Managers.kingdom / Kingdom.playerOne,playerTwo / Player.wallet,hasLocalAuthority / Wallet._playerRef,Gems：均存在。声明及managed wrapper不能代替native可调用或双人验收。

## 冻结公共绘图契约

Worker只交自己的 PatchUI_AndroidImages.cs（namespace KingdomEnhancedMod；#if ANDROID；partial ImGuiCompat），Root将既有 ImGuiCompat 声明改partial、移除旧ANDROID块、项目显式链接新文件。PC块的预处理与已编译语义保持，不提前启用未移植 CalendarHud/KnightStylePanel。
Android DrawTexture(Rect rect,Texture2D texture) 是根GUI、全图片、无额外调用方clip入口：等价显式入口(rect,texture,Vector2.zero,rect)。不能从未知外层Group猜origin/clip；嵌套调用方必须用四参入口。旧PC两参语义仍GUI.Box不改，Android两参只是此真实能力，不空桩。
DrawTexture(Rect localRect,Texture2D texture,Vector2 groupScreenOrigin,Rect localClipRect)：O为当前group局部0的绝对位置（当前GUI.matrix变换前），dest/clip均当前局部坐标；本机实际观测仅parent=1和0.6、fit=1；0.6仅完整图，非1父缩放的部分裁剪组合未观察。实现契约可接受正轴缩放，但其他scale取值、>1放大、任意旋转/负scale不作已验承诺。整张纹理铺满dest，UV自然完整0..1；交集Q只限定可见区域，不把残余内容重新拉满Q。null纹理或非正dest/空Q不画；真实width/height每次从纹理读，非正实际尺寸明确抛错，不猜1或8、不缓存失效尺寸、不catch后重试。
沿Round2公式 sx=dest.w/width、sy=dest.h/height，m03=O.x+dest.x-sx*O.x，m13同；先matrix=restore*stretch，再BeginGroup(q)，q=(Q.x-dest.x)/sx,(Q.y-dest.y)/sy,Q.w/sx,Q.h/sy；Label(-q.x,-q.y,width,height)。嵌套finally先EndGroup、后恢复matrix；Begin失败不做无配对End。原异常上抛由现有调用方责任处理；不改skin/color/enabled/hotControl/changed或输入，不新建driver/hook。
保留DrawSolidTexture独立正确的1x1交集填色语义与公式，免内层group；原WeakContent缓存和borderless style统一给两种路径，最多一份CWT<Texture2D,GUIContent>、一份GUIStyle、既有四WriteOffset/wordWrap=false。只抽取确实被两路径共同使用的矩阵构造；不新增纹理复制、GetPixels、Shader/Graphics/UV扫描、资源注册或默认尺寸。纹理属于调用方，此公共类不Destroy外部纹理；weak缓存不阻止回收。
原注释“所有nativeBeginGroup不裁matrix”改成具体旧组合证据；旧solid几何补偿仍有独立纯色用途，保留依据记录。私有fixture清理，不变成常驻诊断或新功能入口。

## 冻结只读HUD与责任

Worker另交 PatchUI_MobileCalendarHud.cs（namespace OhMyMods.AndroidProbe）和明确Root接线recipe，不改Probe/Main/csproj/现有MobileCalendar。Root原项目增加此源及未改共享 PatchUI_CalendarGems.cs。GlobalAliases已有global using Il2Cpp，不另建同名aliases/复制共享钱包读取器。
MobileCalendar原0.5s Tick只有一个驱动：当前Playing/NetworkClientPlaying有效日期时，调用共享 Refresh(managers.kingdom,world.gameLayer)，共享每次取当前两个本机槽、active/current-scene/local-authority、wallet owner一致后只读Gems。0显示0、失败/负值未知—、两人保留1P/2P、不合并钱包、不读金库或存档。读取失败不清日期，且隔离须包含进入Refresh前的m.kingdom实参求值：日期成功后在该交接点的最小try/catch包住m.kingdom与Refresh，异常仅共享Clear令钻石未知，利用MobileCalendar原faults日志预算记具体GEMS_UNAVAILABLE，不新增counter/retry/catch层；共享Refresh内部已有钱包故障隔离不复制。原Clear同步共享Clear，当前world/layer变化/无效退出/关闭清除。原同scope Menu保留有效日历策略保留。原隔离异常记录沿共享机制，不加scan/retry/wallet mirror或常驻Player引用。
绘制复用当前gold(.93,.78,.47)、GUI.skin.label、字号18*scale(min8)，两个被动单行Label：左日期/季节约60%、右共享CaptionText/ValueText约40%（双人ValueText为1P，CaptionText含2P；显示仍保留中文“钻石”）。顶部高度仍30*scale；总宽min(900*scale,安全区宽-16*scale)，在已有FloatLayout安全区居中，y=SafeTop+6*scale。不加第二行/遮罩/按钮/面板；可用宽非正或SafeTop+6*scale+30*scale>SafeBottom时直接不画，两个Label矩形必须非负且在真实安全区内（8x8退化区不撑开），文本极端长数字/字体/DPI实际可读性未验如实列出。不调用已缺失CalcSize，不新增字体资源或UI系统。
Root将MobileCalendar.Draw(scale)接线改成Draw(existingLayout)，现有唯一OnGUI位置不变。renderBlocked及原fault控制沿现有责任，不扩大catch范围；新绘制 helper只负责当前两Label和实际写的GUI.color/contentColor恢复，不重复外层故障锁存。首个有效数据可沿既有READY日志同次记录共享文本一次，不加Tick或持续日志。
本批20cfg/35hook保持，日历现有开关同时控制日期与钻石，无新开关/钱包更改/存档写/时钟驱动/RPC/manualSpawn/新campaign。

## 实现、审查与验证

freshShanghai时段派一个有界worker（<=10min；DSFlash/max当前夜间偏好，实际路由核验；最多一次有针对性的限时修正）。允许仅phase2/worker-source/两Patch源、integration-recipe.md、report.md、manifest.json。允许只读W和精确校准/声明证据；无Git/canonical/设备/网络/SDK/产品写。Root集成精确审查后的源与recipe，owned W修改只限两个新Patch、ImGuiCompat、MobileCalendar、Probe的一处接线、android csproj、本任务记录及android/tests/Program.cs。验证文件仅机械更新实际变更源的strong pin，登记两新Patch与共享Gems输入，保留原检查不skip/降严；必要项目source数变化按实际新增登记，不把旧基线校准文件计入产品。共享Gems源不改，Root不改PC业务/根Mono。
实际SDK10 0E；必要host行为验证覆盖整纹理不重缩、交集四侧/空交集、非零origin/父正scale和finally恢复；有状态fault测试只在host替身，不冒native故障验证。新增实际MobileCalendar接线的kingdom-getter抛错场景必须保留日期并令钻石未知；Draw纯几何覆盖整屏720x720但安全区8x8的skip及正常safe rect边界。共享钱包既有场景测试优先复用，不为已有代码重新镜像一套测试。PC实际编译/语义对比仅编译产物不得部署，证明PC语义未变；检验47源基线除allowlist之外字节相同、20cfg/35hooks不增、产品无LOCAL/fixture/include。
完成源、必要测试、独审/GLM后才私有打包，原成员/CRC/同cert/单Main校验；精确installgate另审，计划不预批准安装。产品候选从干净源；一个新私有观察候选可沿已审fixture仅替换调用为公共DrawTexture，以证明实际产品路径，独立marker/attempt/目录，不复用两轮预算或改公式。
必要native验证预算最多一次coldlaunch窗口<=120s（launch到真零PID确认，host105s停止覆盖blockedADB），观察公共图片四向/比例/nonzeroorigin、产品HUD真实本机钱包与中文球/展开页；若已有游戏可继续，仅点击可观察到的继续/球控件，禁止新campaign/钱/钟/生成动作。无world或wallet不足以宣称成功，如实pending；原故障保log/png立即停，不无限重启。输入动作必须核当前foreground与所见控件位置，停机前不发最后一次竞态输入。onlyreadonlyUI动作不动20prefs；若既有日历开关关闭，以现状记录不可见，不手改cfg造通过。source finally/host恢复/native真实state分别记。
诊断观察与产品DLL/APK各有精确独立pin；最终交付或恢复安装必须不带LOCAL/fixture。未能观察2P、rotation、高DPI、手机/MP、长时间、动态resize纹理明确pending，不对外宣称全部已验。必要公共路径与HUD本机验证未满足时保持代码任务开放并继续定位实际缺口。

## 收尾与后续

完整本批功能与所需审查验证完成、scope对应PR全部merge后关闭188代码Issue并normal任务done；手机/联机/长期玩家接受另记，不为了关单伪完成。任务implementation记录阶段实证，80–210工程小时/8–10组剩余估算根据本批实际更新，非日历保证。
canonical仅单writer明确窗口、正常Coordinate/harnessctl plan revise/approve与必要续租；188保持同op/owner/session/branch，不rawJSON/DB，其他160 fullobjects/order/101与186plan原样保护。合并共享接口与规范证据，不发常规逐项消息给primary；只合并窗口/冲突/真正阻塞事项联系。
