# Issue192 — Android 公共平台边界：真实配置条目类型与持久化目录

owner mac-codex-ohmymods-android-operator；session codex-android-common-seams-20261008；branch codex/android-common-seams；base af1ae0b657234d874cfc755a86b956b6703aa4be；依赖 issue-188 已正常 done/closed。GitHub https://github.com/baisiqi6/ohmymods/issues/192 已fresh读回OPEN，公开 Mac/Codex/AndroidOperator责任及scope。

## 已观察证据及修复点
fresh真实SDK10全量 inventory 259CompileItems=230shared+27Android+1diagnosticAlias+1generated，canonical37resources；default5E0W停于Bep配置/原生detour声明。optin115E1W：95个ModConfig引用点/42成员、19个真实AndroidCoroutine与仅诊断BepCollectionExtensions混用的CS0121、1个Mod人口CrossbowmanRole缺口；DrawTexture旧3错误已消失。input271pins/refs130或132实际hash不变。并未证明其它源码运行正确。
两份共享源仅3处把 Bep ConfigEntry 固定写在signature/local：CoinCourierRuntime int/float；AutoRestockCounts bool。Android实际20设置已有MelonPreferences_Entry，不应增加wrapper镜像或将PC依赖放进loader。
五份共享源实际5个ConfigPath消费者：HeroRecruitment、MusketeerPersistence、KnightIdentityRuntime、HermesHeadwearCycle的原sidecar路径，以及DeadlandsFollowerCapture诊断路径。Android实际 MelonLoader.dll public static MelonLoader.Utils.MelonEnvironment.get_UserDataDirectory 存在；sourcegetter使用loader已有UserData，现有20prefs也在此根。只在引用产生处适配root；不猜游戏路径/Environment目录。
本批不涉及任何原生hook或副作用触发：编译与平台目录边界准备，尚未启用这些功能。

## 固定实现契约
1. CoinCourierRuntime 文件头使用 #if ANDROID 真实 MelonPreferences_Entry<int/float> 闭合类型alias（名称由worker选清晰一致），#else 真实 Bep ConfigEntry<int/float> alias。将原 BepInEx.Configuration using 替换为两平台闭合alias头；仅替换原2type tokens；其Value/null检查/clamp/原catch和fallback不变。
2. AutoRestockCounts同理一个bool闭合typealias；只替换On参数type，不新建ConfigEntry假类/Adapter/Value副本/状态缓存。
3. 新 il2cpp/PatchShared_ModDataPaths.cs 仅 #if ANDROID 定义 KingdomEnhancedMod.ModDataPaths，一个只读 ConfigPath getter直接return MelonLoader.Utils.MelonEnvironment.UserDataDirectory。無cachedroot、静默默认、额外目录创建、I/O、扫描、重试、catch。
4. 五consumer原仅为Paths使用的 using BepInEx 替换为文件头 #if !ANDROID using ModDataPaths = BepInEx.Paths; #endif（Hermes用后述更窄条件）。只将原 Paths.ConfigPath / BepInEx.Paths.ConfigPath 改为 ModDataPaths.ConfigPath；HermesHeadwearCycle alias额外受 !HERMES_CYCLE_TEST 限制，保持原纯单文件host没有Bep依赖；原测试ResolvePath分支与Override不改。其它Path.Combine components/filenames及权限/atomic/nativewriter/readonly/冻结/catch保持。PC编译alias解析旧真实类型，公共helper在PC为空，要求完整语义比较无差异。
5. Operator只在 android/OhMyMods.AndroidProbe.csproj 加一个helper Compile link；产品现有50inputs到51，20prefs35hooks及Probe.cs全保持；Operator机械刷新 android/tests/Program.cs 原project强pin并添加helper新pin，55→56，所有旧checks保留无跳过。默认diagnostic仍保留INativeDetour缺口（不exclude该source、不造fakeAPI、不加载Bepmetadata到产品）；optin仍明确mixed artifacts。AndroidCoroutine现有16files19callshape复用，无需任何代码改动。

## Worker界限
Worker只允许 D/common-seams/worker-source/ 下交付7份原共享source完整改后文件 +新的 PatchShared_ModDataPaths.cs（8份），以及精简manifest/report；禁止直接写ownedW/primary、Main/Probe/project、device/canonical/Git/network。Operator读取diff审核后合入W并统一project链接。派发前实时上海时间和现役模型核验；夜间OMP deepseek-flash/max，不切模型。允许读W本8指定源、AndroidConfig、globalalias/project、既有hosttests；禁止重造feature配置或删失败代码。

## 验证与交付范围
- Operator逐一diff核对只3个entry类型+5个root引用；零原业务方法条件/数据格式/存档side effects/新hook/20cfg改动。
- 实际SDK10当前Android产品构建0W0E，所有compiledrefs仍真实Androidinterop+loader/support；不混入Bep且helper准确type/MemberRef。无安装/启动/新APK请求，本批产品行为不变，native/phone/MP完全pending。
- 现有Android adapters默认与真正 --oldcfg 都跑；项目/helper强pin随已审改动机械更新，不减检查。
- fresh default/optin全量诊断，输入不被exclude；default的ConfigEntry2+namespace1错误消失，INativeDetour声明剩余如实保留。缺42config不为盘点补假的members；optin19mixed/95cfg/CrossbowRole未完成不标green。
- 真实PC独立副本纯编译，无部署：全types/methods IL/locals/EH/attrs/signatures及37resources对同base完全一致，alias不改实际calltargets；这比重复镜像测试有力。
- 至少复用现有 hermes-headwear-cycle 纯host 验证条件编译未引入Bep依赖；现有typed消费者host工程如可直接编译两平台模式，复用必要用例，不单为这可逆type/path替换发明镜像测试或stub API证明native。新增真实loader只读metadata核验/编译契约已经是本差异有效证据。
- independent源码/验证审查+actualGLM5.3max决策关口；授权内commit/push/PR到release/v9.5.13，policy正常merge无bypass；GHIssue代码范围完成且merged后normalcanonicalcloseout，实机另列待验。发布/tag/formalPCMono安装/公开APK不属本批。

## 任务/共享资源边界
创建Issue并公开Mac/Codex/AndroidOperator认领，按正常Coordinate combined-create/planapprove/start。canonical仅在Primary明确临时single-writer窗口写，逐次fresh保护全部其它161objects/order和101最新对象/plan、186/188accepted与done。回还后不常规刷主Operator进度；产品W是独立writer。此次文件可能与全Mod identity/persistence邻接，窗口请求一次合并scope确认和canonical职责，不向peer派工。
计划准入覆盖Issue创建/机械号码绑定/正常task注册/实现worker和上述验证；具体安装、代码交付、收尾另按实际证据过关口，不预付。

## 剩余工作量
暂维持8–10组、80–210工程小时的条件区间；本轮仅可确认UI公共3编译缺口消失，95cfg/42names和身份、战斗、存档等主要依赖未减少，不能按错误数折算功能完成率。剩余顺序：此公共平台边界→角色/基础战斗及补给→世界防线/阵形→神器与跨世界依赖→火枪/英雄/盾兵身份资源→经济/银行→跨世界坐骑/扩展岛nativeABI；分组以实际依赖可合并，手机/MP验收日历另列，下一高风险native缝隙重估。
