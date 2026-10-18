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

最终 Release SHA256：`e0b7f7482a4bdc45cb38c1239efff510000d7c886ecbdc98359566ce59ddef9e`。Debug：`9398709458b9550329bad1bcbbba1f1ee8d7664745e9b42a03260c96a8514785`。完整本机回执位于 `.local/tasks/android-profession-archery-20261014/g20-economy-dependency-audit-20261018/formal-build/`，游戏文件、APK、interop、存档正文和 provider transcript 不提交。

## 后续全链补查：银行 ID、装载与世界确认桥

初次source/host收尾后继续追调用者，确认已有 `MusketeerPersistence` 的 `GetIdPatch/PopPatch/CreatePatch/VirginPatch` 仍将银行5处调用裁在Android之外：`ObserveId/BeginPop/EndPop/Created/SceneApplied`。编译存在这些类型并不代表保存/加载生产路径已接通。已与Main核对单文件ownership及#250未触及该文件，只在原owner处放开银行调用，保留原异常finally、全部Msk算法和Courier的guard；没有加新patch、扫描或补偿。

新增5项真实产物调用检查，复用既有指令边界reader与MethodDef匹配：修前23d候选1499通过/5条预期失败，修后D/R各1504/0。输入仍206/176/25，407项中仅Msk源字节改变；银行kernel与owner表不变。独立审查 `APPROVE_262_BANK_SAVE_LOAD_BRIDGES_SOURCE_ONLY`，PC预处理字节与原4b639fd4对应输入相同（c9416ac2…）；这一guard差分未重复PC整包构建，旧PC6/8结果是前一阶段证据。原生保存、读档及paid结果仍未运行。

## 审查、登记与尚待验证

周末按本专项用户约定使用独立 OMP DeepSeek Flash / thinking=max；实际 model_change 无 fallback。UI Worker 与 Reviewer session 分离，中央接线由 Android Operator 完成。源码及差分独审已批准 `APPROVE_262_SOURCE_AND_HOST_IMPLEMENTATION_ONLY`，无产品 mustFix；78项严格冻结、最终产物和1499回归（追加五桥检查前）均由独立Reviewer核对。host/目标专项审查另记，不扩展为设备批准。

#262 已正常 create/approve/assignment，operation `ef82994b-4fed-445d-b357-0b3fbe5093b1`，exact plan `9b7b13e6879a0479109fd9992b3aa5a4ec2d3ae1f51a9a54e97c9d4549a5def2`；其余184项/168份计划保持，validator通过。登记只代表实施授权。

本批尚无安装或原生结果。必要运行仍按基础职业、其余补货及全部助手、保存/读档/换岛/池重建分组；同方法多 owner 的保存回调、真实扣款和池行为不能用编译或静态入口检查替代。手机/平板、多人、长期游玩及完整视觉单列。代码交付与必要兼容验证未齐时 #262 保持开放。

#232 职业身份/弓弩的原生保存和池场景继续独立保留。新 world-prereq 工具初审发现日志首次截断误判等问题，正在离线修复；预留的07:50登记与08:00运行窗口已完整归还 UNUSED，未借旧计划运行。G18的562B旁档只证明存在，正文、schema和 paid 状态仍未知；不把本批源码进展写成 #232 验收完成。

下一依赖先完成当前必要运行和代码收尾，再处理尚未接入的共享功能；不逐项新建重复驱动或将普通实现选择升级为用户审批。
