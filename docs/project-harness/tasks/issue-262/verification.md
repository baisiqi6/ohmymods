# Issue #262：Android 经济、八助手与九类自动补货

> Agent provenance: Mac Max / Codex · Android Operator

本批沿用原 Lemon/Melon 架构，接入现有 PC 经济驱动、共享银行和补货逻辑。Android 只补平台配置、显式注册与触屏展示，未新增一套采购、保存、对象池或重试实现。基线为 `02758672703a335974e5e2ac16a711098215beeb`，分支 `codex/android-economy-262`；未导入 Windows 2.4.3 Draft PR #250。

## 实现

- 链接17份现有经济共享源；原协调器仍是唯一 `AutoRestock.Tick` 调用者。复用原有两组件注入、三个外围 Tick、30120..30127 sync 池及4张 PC atlas。助手不是 Character，不添加 Holder 或额外池重建层。
- 40个原有真实 MelonPreferences 项保留，新增9类补货的18项：开关默认关闭、目标默认15，载入时限制1..200。原职业 `MusketeerEnabled` 和补货 `MusketeersEnabled` 分别保存。
- 显式 owner 表由106扩为133，原106顺序和7个动态 TargetMethod owner 保留。`PatchEconomy_Banker` 接受原有9个目标，其余 owner 期望1个，合计141个关联，不能当作141个唯一原生方法。
- 获 Main 单文件交接后，仅放开现有 `CoinCourierPersistence.Hooks.cs` 中银行保存/marker/campaign/challenge 调用；CoinCourier 自身 PC 分支保留，未复制 Android 保存 patch。SharedHooks、ModPanelControls与MusketeerPersistence的PC预处理结果均逐字节等值。
- 世界页新增经济子页，保留小浮球与固定外框。九类中文顺序、单位、说明沿 PC；数量行复用公共测量、绘制及裁剪命中，两个按钮按次增减1。Draw 不写配置，边界点按不保存；有效动作只写对应 entry、记录和保存各一次。删除关闭职业页时不可达的经济页清理。

## 已运行的验证

下表记录首轮源码阶段；后续单次装机诊断和AOC修复见文末，不能把历史“未安装”当作当前总状态。

| 层次 | 实际结果 | 边界 |
|---|---|---|
| Android 产品 | SDK10、实际 net6 Debug/Release 均0错误，保留已有 CS0169 提示；206 Compile / 176 ReferencePath / 25资源，407输入绑定 | 未安装 |
| 最终 Adapter | Debug/Release 产品各1504通过、0失败 | 配置与菜单实源、host doubles、产物元数据；真实 Quantity 手势/视觉未运行 |
| 旧配置与边界 | oldcfg661/0，新增目标载入821/0 | 58真实 entry、旧键保持、1..200与动作保存次数 |
| 共享经济回归 | Hero/Bank组合22/0、助手73/0、固定边界31/0、补货159/0 | 生产核配既有host边界；实际Harmony与native调用仍未运行 |
| PC 影响面 | 当前公共全源 wrapper net6/net8 均0警告0错误；两个共享文件 PC 预处理字节保持 | 禁部署，未用公共候选替换私有正常包 |
| 新目标静态审计 | 当前 Android lib/metadata 的34个具名目标唯一解析；未观察到入口折叠证据 | 有界入口检查，不能证明完整 ABI、detour 或运行成功 |

历史40项期望及旧冻结 pin 的首次失败回执已保留，修正在测试契约处，没有跳过测试或放宽校验；新菜单和测试也加入原严格冻结表。另外，既有银行host套件漏了生产边界常量 `PatchWorld_WallSpots.MarkerPrefix`，两套件首次编译失败如实保留；仅在三处既有game边界fixture补齐当前真实常量，并增加“排除补放墙基、保留原生边界”行为用例，生产算法没有替换。

产品工程默认目标还保留过 net10，现已在工程处改为实际 loader 的 net6，并用不带 TargetFramework 覆盖参数的 Debug/Release 构建复核；两份 DLL 与前述 net6 候选逐字节相同。新增真实 TargetFramework 元数据断言：旧 net10 诊断 DLL 1498通过/1预期失败，实际 net6 两份各1499/0。host 测试自身仍由 net10运行。

首次构建的私有 restore 错配 net10 失败保留，构建命令修为显式 net6 restore 后 `build --no-restore`，没有添加占位代码。

首轮源码阶段 Release SHA256：`e0b7f7482a4bdc45cb38c1239efff510000d7c886ecbdc98359566ce59ddef9e`。Debug：`9398709458b9550329bad1bcbbba1f1ee8d7664745e9b42a03260c96a8514785`。完整本机回执位于 `.local/tasks/android-profession-archery-20261014/g20-economy-dependency-audit-20261018/formal-build/`，游戏文件、APK、interop、存档正文和 provider transcript 不提交。

## 后续全链补查：银行 ID、装载与世界确认桥

初次source/host收尾后继续追调用者，确认已有 `MusketeerPersistence` 的 `GetIdPatch/PopPatch/CreatePatch/VirginPatch` 仍将银行5处调用裁在Android之外：`ObserveId/BeginPop/EndPop/Created/SceneApplied`。编译存在这些类型并不代表保存/加载生产路径已接通。已与Main核对单文件ownership及#250未触及该文件，只在原owner处放开银行调用，保留原异常finally、全部Msk算法和Courier的guard；没有加新patch、扫描或补偿。

新增5项真实产物调用检查，复用既有指令边界reader与MethodDef匹配：修前23d候选1499通过/5条预期失败，修后D/R各1504/0。输入仍206/176/25，407项中仅Msk源字节改变；银行kernel与owner表不变。独立审查 `APPROVE_262_BANK_SAVE_LOAD_BRIDGES_SOURCE_ONLY`，PC预处理字节与原4b639fd4对应输入相同（c9416ac2…）；这一guard差分未重复PC整包构建，旧PC6/8结果是前一阶段证据。原生保存、读档及paid结果仍未运行。

## 审查、登记与尚待验证

周末按本专项用户约定使用独立 OMP DeepSeek Flash / thinking=max；实际 model_change 无 fallback。UI Worker 与 Reviewer session 分离，中央接线由 Android Operator 完成。源码及差分独审已批准 `APPROVE_262_SOURCE_AND_HOST_IMPLEMENTATION_ONLY`，无产品 mustFix；78项严格冻结、最终产物和1499回归（追加五桥检查前）均由独立Reviewer核对。host/目标专项审查另记，不扩展为设备批准。

#262 已正常 create/approve/assignment，operation `ef82994b-4fed-445d-b357-0b3fbe5093b1`，exact plan `9b7b13e6879a0479109fd9992b3aa5a4ec2d3ae1f51a9a54e97c9d4549a5def2`；其余184项/168份计划保持，validator通过。登记只代表实施授权。

本节对应的首轮源码阶段尚无安装或原生结果；后续事实见文末。必要运行仍按基础职业、其余补货及全部助手、保存/读档/换岛/池重建分组；同方法多 owner 的保存回调、真实扣款和池行为不能用编译或静态入口检查替代。手机/平板、多人、长期游玩及完整视觉单列。代码交付与必要兼容验证未齐时 #262 保持开放。

#232 职业身份/弓弩的原生保存和池场景继续独立保留。新 world-prereq 工具初审发现日志首次截断误判等问题，正在离线修复；预留的07:50登记与08:00运行窗口已完整归还 UNUSED，未借旧计划运行。在该G18阶段，562B旁档只证明存在，正文、schema和 paid 状态当时未知；后续真实Decode分类见232 G19记录，仍不代表新付费保存往返或#232验收完成。

下一依赖先完成当前必要运行和代码收尾，再处理尚未接入的共享功能；不逐项新建重复驱动或将普通实现选择升级为用户审批。


<a id="g20-aoc-getter"></a>
## 2026-10-18：133-owner诊断与 AOC getter 根因处修复

旧Release `e0b7f748…`、58键受控配置和V3在独立临时overlay实际启动，达到133注册、world/严格触控表面与水平手势附近的移动响应。但浮球open=True后的唯一PNG未见面板，且 `PoolManager.Init` 路径真实抛出 `AnimatorOverrideController::get_runtimeAnimatorController_Injected` 未解析；不能记录银行/助手初始化或UI通过。两次Root输入、两张PNG、真实reap和八项原盘/保护文件独立核对的完整边界见[232 G19输入诊断](../issue-232/g5-main106-preflight.md#g19-input-ui)。本批因此仍有必要原生兼容检查，PR263保持Draft，262未关闭。

修复提交 `0ccefc1` 仅在共享 `TryResolveControllers` 的Android分支移除两处不可用AOC底层getter及重复AOC专用查询，复用现有live Animator、BiomeHolder original/swap与唯一RuntimeAnimatorController基类资源查询。四名、缺失/歧义/实例检查、失败关闭、原池职责与原加载节流保持。没有getter替身、native地址、catch、额外扫描或重试。PC完整active源逐字节相同，SHA `d137565e…`；后续合入PR267只有文档差异，没有改产品输入。

现有池fixture增加5条Android用例：banker为基类、其它三名为真实AOC派生fixture；Resources-only只通过基类查询得到具名AOC。抛错getter负控在旧生产源实际调用处触发：旧源75通过/3预期失败，新Android78/0、PC73/0；缺三名/缺一名保持失败关闭，同实例多来源不误判、异实例同名继续拒绝。Root独立重新编译运行了同一组结果，未新增生产占位或另造池/账本。

默认net6的Debug/Release实际构建均0错误、各1条既有CS0169；evaluated Compile/ReferencePath/资源仍206/176/25，407输入在构建前后保持。实际两产物Adapter各1504/0。Root直接检查真实DLL的 `TryResolveControllers` IL：旧候选AOC getter调用2、资源查询2；新D/R getter0、基类查询1。新增Release SHA `b9dc46c8e99842649627ee2637aa4cbcac2b861866999d118e1a8e4dc96b20b5`，Debug `3a397a1038bb95e88d07f85b686ba4bb22c9ec8a1b2e9e603335fefbd13098f8`；两者均未部署。独立limited-fresh审查 `APPROVE_BANK_AOC_SOURCE_AND_HOST_ONLY`，报告SHA `f7b28f1906f203e202c79660a2e98fd7e240e96391c6f21e6176139a72faa1de`，实际DeepSeek Flash/max、无fallback。

构建工具最初用过宽的TargetFramework子串校验，误命中CheckEolTargetFramework，未开始编译即中止；已在校验产生处改为精确属性前缀。另补显式Configuration的metadata核对：首版getItem默认Debug，真实build一直使用正确的-c；补查D/R各407输入与原绑定完全相同。旧失败和补充回执保留，不扩大为新产品失败或反复重建。

本次只确认错误调用已从真实Android产物移除；当前Android四名控制器实际加载、助手池初始化/复用、业务补货/扣款、保存/装载/换岛仍未由新候选验证。UI源码尚无已定根因，采用私有编译隔离的首个Repaint ENTER/COMPLETE诊断准备下一有界案例；生产UI没有猜测改动，Draw完成也不代替最终像素。其它引擎API只按具名证据继续核对，不用缺少字符串推断所有平台必然故障。

本地证据根为G19的 `post-input-ui-runtime-triage/`（worker、源码审查、正式产品与真实IL），旧e0b候选和失败原始包均保留；不发布游戏、APK、interop或用户存档。手机/平板、多人和长期玩家验收继续单列。


<a id="g20-bank-ui-api"></a>
## 2026-10-18：四名原生控制器与面板开关已观察，模板初始化仍未通过

新单次 `controlled-economy133-ui-api-20261018-single1` 在独立临时overlay实际加载私有Main `3e7cdf7c…`。它包含同一银行修复及仅限诊断的首个Repaint/七项ICall读取；默认Release `b9dc46c8…` 与Debug `3a397a10…`仍未部署，生产UI源码 `abcd27cf…`保持。58键配置及V3沿用原件，没有安装新APK。

fresh日志实际解析了banker、bamboo、deadlands、norselands四名controller；旧AOC getter首错不再出现。这解决上一节“新候选四名实际加载未证”的局部缺口，但随后在EnsurePrefabs→EnsurePools→PoolManager.Init触发 `SpriteRenderer::get_flipY_Injected` 缺失，另有clerk图集decode的 `MissingIl2CppInternalCallException`，现有catch未记录具体成员。七项只读resolve结果中Animator runtime-controller为true，AOC getter、flipY get/set、avatar setter、applyRootMotion setter、updateMode getter为false；未调用返回指针，非零不代表ABI或可调用性。模板、池、八助手与业务补货没有通过。

Root依次执行一次300ms swipe、浮球open和close，共三次输入/三张PNG。首次Repaint ENTER/COMPLETE同为frame709，之后取得的open图实际可见暗金中文面板，open=False后闭合图不见面板；没有改生产Draw或增加截图重试。一次开关可见不等于所有菜单/滚动/配置验收，世界仍有暗黑区域。旧case单张open图的唯一根因未证，不能把本次成功反写成旧UI已通过或资源问题已解决。手势附近moving/位置变化只记时间对应，不证明Rewired同帧消费。

QEMU56614于14:08:57.202780Z真实reap=0，controller56556于14:09:06.960030Z实际wait=0。Root14:09:42.005172Z fresh核对八项原盘/保护文件SHA及metadata保持、game/VM0、5582/83空闲；回执SHA `34330099ae25f4e20ed302f6e5d9a7cd3bd3cc8f3f6f4c4e1f5389f97a83ce03`。没有原盘恢复层；游戏可自然加载并写临时overlay，无有意支付、保存/装载或RPC，不扩大为native零写。资源已归还主Operator。

actual独审 `APPROVE_DIAGNOSTIC_UI_OPEN_CLOSE_ACTUAL_ONLY`，报告SHA `05bc264c5d4370b0fc8668151c2e6d5c88ae75b036f2e156330986e23aa8a366`；实际OMP DeepSeek Flash/max、无fallback。源码、编译、装机观察和完整玩法分开。静态配对engine/API调查仅用于下一步区分假设，尚未用常量、删除校验或回退贴图绕过本次失败。PR263继续Draft，#232/#262保持开放；真实交易/身份保存与池重建必要验证继续推进，手机平板、多人和长期验收单列。完整输入、图像与收尾见[232新单次记录](../issue-232/g5-main106-preflight.md#g19-ui-open-close-actual)。


<a id="g20-android-api-checkpoint"></a>
## 2026-10-18：Android 原生接口适配与用户暂停检查点

本批集中处理已确认的引擎接口差异，保留原 Lemon/Melon 架构和共享经济源。三套 Android AOC 已沿 ResourceManager→BiomeData.swapData→PPtr 定位，九个 override clip 与三份 base clip 都只有精灵帧绑定，没有根位移或骨骼曲线；Root与独立Reviewer分别重读3个AOC/9个片段，Root还逐字对比同APK原件。原先仅base三片段的证据缺口已补足，不再借Mac资产代替Android。调查实际读取30个具名引用节点，超过任务原20节点预算，已如实保留；未逐个扫描无关GUID。静态资产仍不代表live实例状态。

二维模板在Android省去缺失的flipY复制、avatar/rootMotion setter和source updateMode getter，使用原生Normal更新模式，坐标仍由PositionSync负责。source avatar非null时在资源查询/成功日志之前，经原_lastControllerFailure同一owner去重并失败关闭；没有假getter、默认返回值、克隆Banker、额外扫描或重试。其它视觉字段、八槽/同步ID、池、保存和业务职责保持。四套动画无root曲线使该配置不依赖“新Animator默认rootMotion=false”的猜测；live updateMode无法读取，未宣称逐字段运行等价。

图集只在Android改用现有typed format/GetImageDataSize/GetWritableImageData接口，验证RGBA32/ARGB32、严格4N字节和非空指针后立即一次复制；按Unity格式定义转换，保留每帧透明检查、32 sprite及失败清理。未用泛型NativeArray、native地址、PNG解码器或回退贴图。原catch仅补异常Message，下一次运行可直接定位具体成员；现有clerk异常尚无完整栈，不将该适配冒称唯一根因已经实机证实。PC两个完整active源码逐字保持。

Root独立host结果：银行PC73/0、Android85/0，旧源78/7预期失败（包含五项实际缺失API路径）；图集PC37/0、Android40/0，旧源37/1预期失败且实际进入GetPixels32。新增测试用真实AllocHGlobal/Marshal.Copy与独立非对称颜色字节，覆盖格式/字节数/空指针/透明帧和清理；测试桩不证明Native默认值。默认net6 Debug/Release实际构建各0错误、1条原CS0169，两个实际产品Adapter各1504/0。输入仍206 Compile/176 ReferencePath/25资源；407项相对上批只有两份产品源改变，其余405项保持。Release SHA `807e5b95b8328f35e88105a7f31dc049455ade3722992051ee28d7e1f9c6d48b`，Debug `91a0613184aea2a52c6dc71514828618188f1c74fe808ea46097bdc72f355f36`，均未安装。

图集独立代码审查为 `APPROVE_ATLAS_NATIVE_PIXEL_SOURCE_AND_HOST_ONLY`，银行模板为 `APPROVE_BANK_ANDROID_TEMPLATE_SOURCE_AND_HOST_ONLY`，均无mustFix，仅批准源码/host范围；未开放设备或合并关口。方案与实现由不同OMP DeepSeek Flash/max session承担，Root核验原生model_change/thinking_level_change、无fallback；不以Reviewer自述代替模型证据。两组源码/host结果及正式产品核对已完成，作为本次Draft暂停检查点。

用户要求在本批适当位置暂停：只收拢当前离线改动、必要审查验证与Draft保存，不再启动下一单次或下一功能组。14:31正常入口仅同身份续租232/262到16:31:13Z，全部169plans及183其它row保持；Root14:31:53fresh185rows/169plans与producer-after一致、validator0，doctor仍旧3E/56且232无block，非全局通过。后续14:56–14:59登记与15:05–15:30运行申请已撤回，旧spent门不复用。PR268诊断文档已合并a8efb06；PR263继续Draft，#232/#262保持OPEN。新候选八模板池、实际助手/补货/扣款、保存装载、暂停朝向及池重建尚未运行，手机平板/多人长期另列。恢复时重新核对源码、租约与环境，再申请新的精确运行门。

本机证据：G19 `post-ui-api-runtime-fixes/` 的两实现/审查目录、`bank-android-asset-trace/root-independent-asset-check.json`、`root-host-verification/`和`product-build/`；本批不发布游戏、APK、interop或玩家数据。

<a id="g20-bank-init-actual"></a>
## 2026-10-18：银行初始化单次实际观察

用户已直接恢复运行，旧暂停状态仅为历史、不再生效。默认Main `807e5b95…` 与私有Addon `2831e044…`（version 0.0.5、58键）为同件产物，无新APK；normal plan `cc4a45c8…`。new case `controlled-economy133-bank-init-20261018-single1`：fresh日志4名controller均Resolved；13条单次snapshot标记（1 SNAPSHOT+8 SLOT+4 SHEET，同秒单组）；atlasState=1、Ready=1、Failed=2；4张纹理各有32帧存储引用；8个template/pool为30120..30127，prefab/sync/byId/byName/syncId匹配。旧AOC getter、flipY、atlas decode failed、S1_READY/INPUT_UNKNOWN在fresh中缺席。边界：日志不证明每slot的controller绑定，managed引用非native alive，非完整ABI、非像素可见；GetValue可能触发managed cctor（atlas 4次StringToHash），helper不ForceEnsure，不称零副作用。

Root 0输入、1张controller baseline PNG；画面暗，orb/dateHUD/moon+水面可见，未见assistants。无有意交易/保存/换岛/池重建；允许app自然load与临时overlay写入。QEMU12810于15:29:03.186474Z真实reap=-9，controller12644 actual wait于15:29:14.543869Z=0；Root 15:29:31.736041Z fresh核对8项SHA+metadata保持、game/VM进程0、5582/5583空闲、无restore层，postflight回执SHA `078ce2d0…`。normal 15:15登记、15:16 fresh其余rows/plans/leasehold一致，validator 0；doctor旧3E/56，不是全局通过。actual独审仅`APPROVE_BANK_INITIALIZATION_OBSERVATION_ONLY`（报告SHA `d9ee3d37…`）；PR263 Draft、#232/#262 OPEN。据此允许下一依赖组处理其余7个当前产品owned PNG readback，尚未实现，不声称通过。

<a id="g20-shared-owned-png"></a>
## 2026-10-18：集中适配当前产品的自有 PNG 像素读取

将银行原有 Android 读取方法体原样抽到唯一 `android/AndroidTexturePixels.cs`，银行、闲暇图集、英雄人物/箭、火铳人物/弹丸、重盾两处及商店共八文件九处复用。仅适用于自建、可读、无 mip 的 PNG 纹理；保留 RGBA32/ARGB32 白名单、checked 4N、实际数据大小/非空指针验证与单次复制，完整保留 RGB。原银行私有副本及其无用 using 已删。各 consumer 的透明度/透明 RGB、尺寸、缓存、异常和资源清理责任保持，没有新增 decoder、地址读取、扫描、重试或回退机制。四份尚未进入产品的调用者未改，Samurai 的导入纹理需要不同契约，不盲套。

Root 独立核对八份完整 PC active 源逐字相同，九个原 GetPixels32 留在 PC 分支；Android 两个实际产品 IL 均为旧调用0、公共方法9。默认 net6 Debug/Release 编译0错误/各1条既有CS0169，207 Compile/176引用/25资源；相对上一候选399输入保持、8共享源改变、增加1 helper。产品工程显式链接，CompileInventory 按原 android/*.cs 自动纳入；该诊断工程这里只核接线，未宣称其全量构建通过。Release `0cd539392b23ca0cea9b13e85879f77803890cbec00f9a9fb9cc9e471cf30344`，Debug `59a98bad4b4ff18b2dffc0679cb3ceb4b955f603d7bb2e452d51142ec86012da`，均未安装。

最终 Root host：银行图集PC37/0、Android41/0；商店PC372、Android383且实际输出 Android consumer 标记。银行集中保留非对称 RGBA/ARGB、格式/大小/指针、透明帧/清理与 count/4N 两级真实溢出覆盖；商店去掉重复 helper 通道断言，改由真实 ValidateCells 在两种格式下覆盖三类商店及透明 RGB 原异常，保留 unsupported/shortsize 与 GetPixels32 硬抛、调用计数0。两组旧生产源在真实 GetPixels32 路径分别预期失败，未拿 helper 缺失充当红例。stub 的 LoadImage 只提供尺寸与 fixture，不冒充 PNG 解码或 Unity 实际颜色证据。两个实际产品 Adapter 各1504/0。初次 Root host 还原误取net8，显式 host TargetFrameworks=net10后修正；旧负控的收集器也曾过度要求旧catch未记录的Message，原始失败记录保留，未改生产或测试预期迁就。

独立 OMP DeepSeek Flash/max（无fallback）源码/host审查批准 `APPROVE_SHARED_OWNED_PNG_SOURCE_AND_HOST_ONLY`；采纳精简建议后仅变 Android 测试，Root重新构建运行上述四组及商店旧源反例。方案审查修正过点数和 Android fixture 接线，未引入无用 Compile。上节807的初始化观察只适用于当次产物，本次抽取后的 Bank/Shop 仍需新的具名 Native 兼容场景；真实助手、交易补货、保存装载与池重建继续待验。PR263保持Draft、#232/#262开放，手机平板、多人和长期玩法单列，不发布游戏或APK。

本机证据在G19 `resume-1453/shared-pixel-adaptation/`（source audit、正式产品绑定/IL、root-host-final、Adapter），独立审查在同级 `shared-pixel-code-review/`。15:50正常入口仅续租232/262至17:50:19/20Z，183其它rows/169plans保持，validator0；Root15:50:54Z完整独立readback一致（回执 `543be915…`），旧doctor3E/56非全局通过。该续租未改plan/runtime或扩大运行权限。

<a id="g20-ownedpng-native"></a>
## 2026-10-18：新候选的七张图集与固定像素原生观察

单次 `controlled-ownedpng133-bank-shop-isolated-20261018-single1` 在 SDK read-only 临时 overlay 装入默认 Main `0cd53939…`（0.0.30）及私有 AddOn `cf3fdf5b…`（0.0.6），58键受控配置保持、没有新APK。实际读回校验后达到133注册、world与严格surface；四名bank controller解析、八模板/同步池30120..30127及byId/byName缓存匹配，四张图集各32个精灵存储引用。当前fresh没有先前AOC/flipY/图集decode及Unknown错误。

新私有组件在原one-shot位置先读四张银行纹理，再对三类商店各调用一次现有 `ShopV2Art.TryGet(kind,0)`；这是主动资源生产入口，可加载/校验/创建Sprite并写cache，失败会置Unavailable，不能称整体只读。日志没有preState，故不归因该call首次触发Load。三类cache实际Ready=1、sprites为20/26/10，返回值与cache0的managed引用一致。七张纹理本轮均为真实ARGB32；每张一次额外公共helper读回、一个非对称不透明RGBA点与一个只比较A0的透明点，共十四点与按输入PNG SHA固定的独立oracle完全匹配，验证了这些样本的通道和左下行序目标。Native RGBA32没有样本，仍只具host证据；未逐路径验证其余七份产品consumer，更不代表全图像素、renderer可见性或完整ABI。

按用户最新隔离准则，本机新candidate仅将两处全局“其它KTC/QEMU即拒绝”收窄为自己的AVD/serial/端口、物理FD、配置和六镜像身份、原ADB88434身份；未知VM不放行，既有八项SHA/metadata、只own PID/start/exe停止、spend门和预算保持。未改旧spent/live代码，未新增调度框架/全局锁。实际 `lsof +D` 能检出目录外hardlink/symlink持有者，故撤回无依据的nlink限制；本机exit1+数字PID也按占用处理。66项host、7项真实小fixture文件/5582端口边界通过；SDK `-no-window` 与定向ADB通道避免桌面输入互斥，未声称本轮恰有两端同时运行或做性能比较。

Controller90803于17:19:34.900031Z实际wait=0，QEMU90872于17:19:25.747426Z实际reap=0；observer按原120秒预算停止（120.11s、events1）。Root17:20:19.503749Z独立fresh八项字节/metadata与resume原件及源码before/after相等，own PID不存在、AVD无holder、5582/3释放、原ADB同身份，无restore层。本次Root0输入/0额外PNG，controller仅1张基线；原图仍暗黑，仅日期HUD/小浮球/月水面可见，未见玩家/助手/商店，不记视觉通过。app自然load与临时overlay写入存在，未执行有意付款、保存装载、RPC或池重建。

实际结果独审仅批准 `APPROVE_NAMED_ASSET_PIXEL_OBSERVATION_ONLY`（report SHA `d08a0d7444ee67fb…`），Root接纳同一窄范围；新source及exact数据分别独审，正常计划 `2105140e…` 已经受管登记并fresh读回，184其它row/168其它plan及262计划保持，validator0；旧doctor3E/56不是全局通过。没有复用撤销的全局Native排队窗口或旧运行凭据。PR263继续Draft、#232/#262开放；真实身份付款→原生保存装载、职业/弓弩/助手生成攻击回收及池重建仍属必要兼容事项，手机/平板、多人长期验收另列。

证据：G19 `next-bank-shop-isolated-case/actual/`；fresh log SHA `d66976cf…`、Root最终裁定 `dd3571f9…`、postflight `7fb38c36…`，代码/边界测试与分组审查在 `resume-1453/` 对应目录。不分发游戏、APK、interop或玩家存档。原银行私有读取副本已清理，所有本批生产读取继续复用唯一helper。

<a id="g20-profession-activation"></a>
## 2026-10-18 UTC：三职业自然启用与共享选址状态观察

新单次 `controlled-profession133-activation-isolated-20261018-single1` 保持 Main `0cd53939…`，使用私有只读 AddOn `3b78e784…`（0.0.7）。配置 `16b68641…` 相对受控58键基座 `f49c3cfc…` 只把 `InfiniteMoney/MusketeerEnabled/HeavyShieldEnabled/HeroArcherEnabled` 由false改true，其余54项保持；这不是与设备原始31键配置 `9758bda8…` 的逐项等同比较。会话主开关仍复用原内部 `Setting<bool>`，未添加配置项。0.0.7从0.0.5保留Bank快照与原readiness/120秒/128事件生命周期，只在首个surface和原Stop各读一次真实managed状态，不含0.0.6主动TryGet/额外像素读回。独立net6编译0W0E、68项控制器host通过，source与21份快照/具体文件绑定分别独审；本批未修改产品源码或PC业务。

设备字节读回核对Main/配置/AddOn及六份runtime引用后达到133注册、world与严格surface。两个时点共12条职业标记，无PROFESSION_UNKNOWN；四项配置与内部主开关均true，`Wallet.InfiniteMoney`原生getter为true且与配置一致，但玩家实际coins仍0。HeroShop、MusketeerShop、HeavyShieldShopShell的`_registered`均true，`_object`及付款点/points存储均null，stage均`find-position`；文案分别来自共享Find正常返回false。源码顺序说明各Create已越过素材预检，不能据此扩成精灵视觉、native owner存活、接口回调或商店完整创建通过；两固定点也不证明其间连续状态。

共享Find没有吞异常catch。当前false尚不能区分边界/半宽无效或过窄，与候选全被原生exclusions拒绝；不能直接归因资源未加载、Android API失败或“只需扩城”。下一步仅补读当前真实边界与参数，不加新候选搜索、扫描/重试、放宽排除或强制Create。当前fresh另有`KnightStyle`缺`knight_norselands`、风格池缩为4/5的残余，北境风格行为待核；本次未隐去或宣称已修。

本Operator未发送输入或额外PNG，controller只取1张基线；该行动记录不冒充对其它输入来源的独立测量。截图仍暗，仅HUD/浮球/月与水面可见。observer在120.04秒/events1按原预算Stop，QEMU14485于18:16:35.775687Z真实reap0，controller14383实际wait0；Root18:17:47.613671Z重新核对八项SHA/metadata与原件、before/after一致，own PID/AVD holder消失、5582/5583释放、ADB88434身份保持，无restore。未执行有意付款、保存/装载、RPC或池重建；app自然加载和临时overlay写入仍存在。

正常计划 `2fe840be…` 于18:10登记，18:11独立fresh185 rows/169 plans与producer after一致，184其它rows/168其它plans及262计划保持；validator0，原doctor3E/56且232无blocker，不写全局通过。旧门已消费，未复用旧凭据。实际审查首轮因Deadline exceeded退出、未产出verdict，不计通过；同一独立OMP Flash/max session续接后公开批准 `APPROVE_ACTIVATION_STATE_OBSERVATION_ONLY`（报告SHA `94ea77177f946ea4…`），Root仅采纳上述窄范围。证据在G19 `next-profession-activation-case/actual/` 及 `resume-1453/profession-activation-actual-review/`：fresh log `b257e798…`、Root裁定 `78cdf698…`、独立postflight `9a4dd704…`。PR263继续Draft、#232/#262开放；真实身份付款、职业/助手生成攻击回收、原生保存装载与池重建仍属必要兼容事项，手机/平板、多人长期验收另列。

<a id="g20-placement-parameters"></a>
## 2026-10-18 UTC：当前选址参数与原生领地前置

`controlled-profession133-placement-isolated-20261018-single1` 保持Main `0cd53939…`及四项打开的配置，私有0.0.8 `26bc4bd9…`复用原观察器，仅在原有效固定时点新增参数读回。Hero半宽来自实际`_sprite.bounds`，Msk与Shield布局常量读实际Main元数据，Shield图集复用原只读`CurrentShopAtlas()`；不重复Find/Collect/候选搜索。场景失效时先Unknown，新增原生读取不介入；原6项快照、Wallet读取和Stop finally保持。Root独立net6 0W0E、真实source-linked net10 host11行为检查及controller68通过，源码/24快照绑定分别独审。

实际14条标记中无Unknown，原四项/钱包一致；两点都读到`left=right=width=0`、`HasBorderLoaded=true`、半宽`2/2.75/2`、图集`GreekShopLayers`。据共享Find的double区间公式，此时三家均`low>high`，当前参数会在原生exclusions前被拒绝；不代表完整记录每次历史Find，也不证明为何边界为0。实际图集查询纠正了先前从`islandBiome0`推非Greek/半宽1.5的假设；没有采用硬编码。两套interop的Side原值为-1/+1，原始v31值一致，无Side占位0问题。

当前2.4原始反汇编另显示：`GetBorderSideIntact`只读缓存，`HasBorderLoaded`在首次计算前置true；`CalculateBorderOnSide`先把旧border写入intact缓存，有intact墙再覆盖，墙/城堡事件才推动后续重算。未发展场景是候选原因，尚需非intact边界、castleBuilt及墙状态闭环；未将旧2.1源码或偏移变成运行时补丁，不改Find、不强制重算或伪造支付状态。

observer按120秒/events1结束；controller38364实际wait0，QEMU38540于19:11:31.755169Z实际reap0。Root19:15:30.484775Z fresh八项SHA/metadata与原件及before/after一致，AVD/5582/5583释放、ADB88434保持，无restore；本Operator0输入/0额外PNG，controller1基线，画面不作角色/商店视觉证据。计划`96f73eef…`正常登记及独立读回保持其它184rows/168plans、262计划与primary；validator0、原doctor3E/56未扩大。18:29同身份官方续租仅更新TTL至20:29，全部169计划保持。独立actual批准`APPROVE_PLACEMENT_PARAMETER_OBSERVATION_ONLY`，报告SHA `a7a9c4967450e1e8…`，Root裁定`a5d11a6b…`、postflight`f233fc1a…`。完整商店、付款、角色/助手、保存池与视觉继续待验。

<a id="g20-flipy-boundary"></a>
## 2026-10-18 UTC：四处已裁剪 flipY 调用的公共平台边界

当前Android原始v31 metadata中SpriteRenderer保留flipX方法，却无get/set flipY及Injected定义，与实际缺ICall一致；未用生成interop的MemberRef缺失替代原游戏证据。离线动画初次2359条因遗漏level/split/builtin被Root拒绝全量结论，补齐后覆盖2449 AnimationClip、4296条SpriteRenderer绑定。普通属性无flipY；34条customType22按[AssetRipper解码实现](https://github.com/AssetRipper/AssetRipper/blob/master/Source/AssetRipper.Processing/AnimationClips/CustomCurveResolver.cs)属于material属性，未命名shader参数不冒充SpriteRenderer的Y字段。原APK与历史9c6候选的22147个Data条目CRC/解压尺寸及metadata SHA匹配；未重复整包SHA，不把CRC当全资产密码学证明。原生插件、外部资源和未来版本写入仍留边界。

在HeavyShieldActorVisuals、HeroArcherVisuals、MusketeerVisuals的原复制点，以及CoinCourierTeleportFx原offsetY计算点加Android条件，不读取当前不可用的Y接口；新建自有renderer的原朝向、父transform符号、颜色/层/材质/PropertyBlock与几何继续原路径。Coin当前Android在编sized caller仅BankAssistant自建renderer；完整repo另有CoinCourierRuntime的PC调用，保留原路径，未来新增caller须复核。没有假getter、重复状态/新helper、扫描/重试/回退；一般catch/CopyFailure仍有其它独立故障责任，未盲删。

Root四份完整PC有效源码保留空行和注释后逐字节相同。Android host：Hero160、Msk113、Heavy真实actor focused3、Coin15；PC包含Heavy完整1144及各Y正负/offset反号。相同strict Y桩跑真实旧源，分别观察到getter调用与字段复制中断/attach或bodybox失败，不用缺helper编译失败充当红例。测试用像素桩仅隔离已有解码，不证明Native图像。独立Reviewer在私有副本复跑上述正反例并批准`APPROVE_ANDROID_FLIPY_SOURCE_AND_HOST_ONLY`（报告SHA `55fb3779…`）。

Root默认net6 D/R均0E/1条既有CS0169，207源码/176引用/25资源；408输入中404保持、仅上述4源改变。Release `a757fbe777db220c60b0020d3d1fc35c27d6ca57e97f88b3a0c79ff79ef09dd2`，Debug `91fb78dc66d1fe206e816074e7e023505116712a5869bf55a25d1207168c1dd0`，实际全装配get/set flipY均0，GetPixels32仍0、ReadOwnedPng9；基础Animator getter21不误算AOC。新产物未装载，不能复用旧0cd的Native结果。Coin身体框仍有GetPixels1待具体ICall/纹理契约核对，角色/助手真实显示与生命周期继续待验；SamuraiDash/HermesHeadwear等未入Android闭包的Y路径未擅改。PR263保持Draft、#232/#262开放。

证据：G19 `resume-1453/flipy-adaptation-worker/`、`flipy-root-product-build/`、`flipy-root-il/`、`flipy-code-review/`，以及`next-profession-placement-case/actual/`、`native-border-precondition-audit/`。本批不分发游戏、APK、interop或玩家存档。

<a id="g20-native-readiness"></a>
## 2026-10-18：原生边界前置、实际浮球几何与剩余像素接口已观察

新单次 `controlled-profession133-native-readiness-isolated-20261018-single1` 在同一独立 SDK 临时 overlay 装载正式候选 Main `a757fbe7…`、58 键受控 cfg `16b68641…` 与私有 `.9r2` `15872f36…`。观察器沿原两固定点增加当前 Kingdom 和纯 managed 浮球 Layout 读数，首 surface 仅解析两个具名 ICall，不调用返回指针。源码/host（19 项、控制器 77 项）、exact binding 与正常登记分别留证，未使用旧 spent 门。原先未证为正对照的 TextureWritable 名称已替换为既有实际可解析的 Animator getter；不能据旧错误名的 0 推断 helper 缺失。

本次 `Texture2D::GetPixels_Injected` 返回 0，`Animator::get_runtimeAnimatorController_Injected` 非 0，确认当前精确名字的解析差异；不是所有纹理 API 都不可用的结论，也不是函数调用 ABI 验收。剩余 Coin body-box 像素路径可据此复用已验证的自有 PNG helper，限定真实所有权，不增加 getter 替身或逐像素 Native 调用。

baseline 与 final 两点一致：**ordinary / `GetBorderSide` 为 -20.75 / -3.25**；**intact / `GetBorderSideIntact` 为 0 / 0**，商店半宽 Hero 2、Musketeer 2.75、Shield 2，当前图集 Greek。`castleBuilt=false`、`Walls.Count=0`、`CampfireWorldPosition=-12`、`HasBorderLoaded=true`。普通边界、完整城墙边界与“已加载”是不同事实；本例未满足完整边界选址前置，不放宽共享选址，也不宣称 cache 失效。快照是两个当前值，不是全部历史 Find 参数或连续状态。

实际 managed Layout 为 `640×360`、GUI 位置 `86,79.2`、触控方边长 36、ready=true、expanded/captured=false；当前 logical display 与唯一 baseline PNG 均 `1280×720`，rotation=90。原“0°才可输入”假设已由当前原始显示信息推翻，输入使用当前显示像素与 GUI/touch Y 转换，不再镜像默认浮球位置或安全区。唯一图可见小浮球、HUD、月亮与水面，暗黑世界区域仍在，未记完整视觉/可玩性通过。

Root 曾启动一次移动脚本的只读检查，但 `action_budget` 在创建 intent / 派发 swipe **之前**因剩余时限不足拒绝：实际 Root Native 输入 0、Root PNG 0、controller baseline PNG 1；未尝试 pay、未重放、未热改已审源码。不能把“拒绝后 0 次输入”写成移动或投币校准通过。后续合批运行将把有界 Root 动作接在当前 baseline 到达处，避免操作调度延迟；仍保留 unknown 即停与原观察器结束。

观察器在 `23:20:40Z` 以 time-budget STOP，elapsed=120.12s、events=1；controller11213 实际 wait=0，QEMU11272 于 `23:20:41.680459Z` 已 reap，returnCode=-9（不记为优雅退出 0）。Root 独立 fresh 八项原件字节及 metadata 相等，own AVD 无 holder、5582/5583 空闲、ADB88434 原身份仍在，无 restore 层。postflight SHA `46c97be846e4b02c7465de6fce14d8a2c336b969e219579cc0b5585f4e1228f5`。

plan `716b7823…` 正常 revise/approve 后独立读回185行/169计划；其余184行/168计划保持，validator 0，doctor 旧3E/56 findings、232无阻塞，未称全局健康通过。cfg ledger 只证明安装时 readback；result.userData 的 `9758…` 是 prepare 原件，不是 post-run 回退，运行 flags 真另由日志证明；post-run cfg 文件体未采样。源/构建/安装只读观察与玩法验收仍分开。

独立 actual 审查与 Root 裁决见本机 G19 `resume-1453/native-readiness-actual-review/`、新 case 的 `actual/root-actual-adjudication.json` 与 `root-independent-postflight.json`。本例不证明原生付款、建城、角色生成/攻击/回收、保存/装载、换岛或池重建；手机/平板、多人、长期与完整视觉继续单列。#232/#262 OPEN、PR263 Draft；当前 API 与 Norse 视觉控制器缺口合批修复，必要运行继续。

Actual 独审修正后报告 SHA `d1b39a4d9a348f1b2c94ef709f372dc70fd21c04ef163c0e9df5836b6fe2560f`，Root 接受为 observation-only；原审稿及勘误保存，不沿用其 ordinary/intact 与 cfg 时序误标。


<a id="g20-pixel-norse-batch"></a>
## 2026-10-18：传送像素读取与北境控制器 producer 合批兼容

当前设备已记录 GetPixels 精确名0/Animator正对照非0，故在共享 Coin body-box 原位置接入现有自有 PNG reader。新增 Android-only `OwnsTexture` 只读已发布四纹理数组，以 Unity native instance ID 判同实例（不按名/尺寸/CLR包装引用）；foreign、未 ready、已销毁拒收。像素源、非零 rect 的整图 stride、byte alpha<8 三处作平台边界，共用原 bbox 扫描、缓存、失败、Clear、pivot/PPU/父级 transform 与朝向逻辑。Root 删除初稿重复的整段 Android bbox 算法；width 只读一次，没有新缓存、逐像素 Native getter、Marshal 或回退。

同批核实持续 4/5 告警来自**视觉风格池**。当前2.4 APK中北境骑士资产存在，原生将它放在 prefabSwapPool（base Knight→Knight_norselands），其 animatorSwapPool 没有该映射；不是 sync 池丢失或 APK 缺资产。只在 Android 的现 producer 补 full BiomeData 原生 prefab 映射：精确 base Knight 实例、根 Animator、精确 controller 名才填空 slot4；不实例化、不改角色身份/存档/随机选择/五 style 顺序/缩放/RPC。Android 两次重复全量 FindAll 改用同次已填 base slots；其它风格仍需要的 loaded/animator/旧 LoadAll 与渐进 ready 职责保留，完整10/10后不介入。静态图读取16具名节点9,477,328B，不能代替live唯一性或style4实际角色应用。

三份 PC 完整active文本（含注释/空白、不normalize）与基座逐字相同：Bank 25,566B、Coin 59,207B、Knight 87,137B。Root source-linked独立host：atlas Android42/PC37、FX Android18/PC15、bank teleport17、coin visuals31；旧FX真源在缺GetPixels fixture中14通过/4预期失败。Norse Android12/PC2，旧真源双变体各1项确认4/5；成功后第二次ensure无新扫描/加载。所有host为离线替身/源码验证，不是Native ABI。

默认net6 Debug/Release均0错误/1既有CS0169，207 Compile/176 refs/25资源；408输入中405保持，仅三份产品源改变。实际两DLL各Adapter1504/0；Root指令边界检查 GetPixels/GetPixels32/flipY get/set均0，ReadOwnedPng由9→10，未加patch owner。新Release SHA `2e55f5bcc6af844111ee1d89147bca91b13c3250ab6b4efde8db7e0fb50e4754`，Debug `993ec885d412a12f1011fa0322e6655728211ad7baa33f1d234a1cfbdd990c51`，本阶段均未装载。host移动obj时曾吸入项目旧obj导致CS0579，已在构建命令的默认Compile来源排除旧obj，未改产品/未关生成属性掩盖问题；旧输出保留。

独立代码审查 `APPROVE_GROUPED_ANDROID_PIXEL_AND_NORSE_SOURCE_ONLY`，实际OMP DeepSeek Flash/max、无fallback，报告SHA `328124a854d0086debb508122243406617b2a9c34b2d9d1759d3107079338c65`。Root接受为source/host范围；后续合批Native验证真实owned纹理路径、Norse5/5与有界输入，实际FX/角色/付款/保存装载/换岛/池重建仍待验。#232/#262开放、PR263 Draft，不以源码通过代替安装/玩法或合并授权。证据本机G19 `resume-1453/grouped-root-*`、两Worker与`grouped-api-style-code-review/`；未提交游戏/APK/interop/用户存档。
