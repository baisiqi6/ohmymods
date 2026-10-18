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
