# Issue122 — Android 快速建造初始化接入

- GitHub: https://github.com/baisiqi6/ohmymods/issues/122
- Owner/actor: mac-codex-ohmymods-android-operator
- Session: codex-android-construction-20261004
- Branch: codex/android-construction
- Coding host: /Users/Admin/.codex/worktrees/android-construction/ohmymods
- Source baseline: 46f4aeded223d0614efd2de6b2842ccfb5bfaa8b (Issue119 PR121 source)
- Dependencies: issue-119 (merge + normal done/closed before implementation)
- Approval: OMP zhipu-coding-plan/glm-5.3 max, no fallback, session01a1058e-1acc-72a9-a7ba-69fb8dda26fe APPROVE_PLAN_122; minor conditions incorporated below.

## 目标与证据

链接未修改 il2cpp/PatchWorld_Construction.cs，复用原生 InitializeBuild/AutoBuild，不复制施工。
实际 Android 2.4.0 / Unity6000.0.61f1 原生 full129838metadata/125041body map 核 InitializeBuild ptr0x2792b7c 与 WorkableBuilding.Start 各唯一。
Start尾调InitializeBuild；_hasStarted 在原方法体早退，前缀先于早退执行。因此准确写点是每次 InitializeBuild 调用，不把此句偷换为只在首次真正初始化或付款后才运行。
AllAutoBuild为真时原生也写50；AutoBuild逐点读取rate等待1/rate，只在原生auth/scaffolding条件下启动原owner协程。共享前缀仅Enabled&&FastBuild写50；关闭不热还原已有实例。全库其它writer/池复用尚未知，旧失败扫描不得当零writer证明。

## 实现范围与分工

Worker：共享源CompileInclude；MobilePlayerConfig单FastBuild默认false（总9entry、现category、切换save一次，日志readiness追加）；MobileWorldMenu新增FastBuild行并明示at init/Existing rate stays; no hot restore；FloatLayout只把World分支改562，Home484/Player406/Population376保持；项目引用/有意义tests/README适配。
Operator：Probe.cs显式注册唯一 InitializeBuild()->共享 public Prefix，按现LogHookCounts计数，version0.0.13及marker；不自动Harmony扫描。现reported15个，加本次1个为16；真实读取逐行核验含新增target，不能照抄review文本误写的16→17。
World六行98/176/254/332/410/488、按钮64，height562（末行底552、余10）；同FloatLayout给绘制/actor与UGUI命中层供矩形。球48visual/72squarehit不变。
不修改PC共享源/Mono、原生AllAutoBuild/progress/prefs-save/wallet/库存/池/施工事件，不新driver/coroutine/scan/retry/rate镜像/热复位；保留仍有独立用途的actor隔离。

## 必要测试与审查

实际interop/loader引用完整build0W0E。host检查单defaultfalse、loadtrue、单Toggle单save、Back flags/几何、产物链接真实Prefix和rate setter/具体target，冻shared和未改适配源。
同处清理FrozenSources中从未迭代的NuGet.Config死哈希，恢复未改AdapterTests.csproj真正冻结；不做假UnityRuntime或镜像prefix行为测试。独立源码审查与安装/交付精确候选GLM关口。

## 私有设备采证与授权边界

私有独立helper只观察原生InitializeBuild：HarmonyPriority(Priority.First) prefix抓pre-rate/started、postfix抓after-rate/started/progress/total；每条同时只读AllAutoBuild。prefix在native早退前，post对started/rate可区分。无 __runOriginal 写、无 InitializeBuild/AutoBuild/ForceComplete 调用或任何游戏字段修改。每process最多64次日志并标truncated；异常报unknown/FAIL，不假通过。
外部每条snapshot命令只做一次FindObjectsOfType<ConstructionBuildingComponent>(true)，报total及top12样本rate/started/progress/scaffolding、player位置/coins/GameState；不定时扫场、不改当前EventSystem或游戏对象。只有命令文件与日志I/O。
APK保留原游戏22313资产逐字节验证、main唯一、签名；exact安装前备份旧DLL/prefs/nativeSave并复核。诊断不入APK/Git，采完移出Mods后cleanrestart仅主Mod0.0.13、16reported各1、无ERROR。
正常原生收入/付款与建筑生命周期采样：OFF基线须排除AllAutoBuild混淆；UI ON后真实原生callrate变化/建造progress；UI OFF后已有rate保持、后续call关闭。未触发对应call不能编造；不手写余额/库存或存档。配置OFF恢复与coldreadback。
手机/联机/所有施工types/跨岛文件读档等按实际保留待验；不得把元数据链接当真实玩法验收。只源码PR，正常merge后代码scope关单；无publictag/release/APK分发、PC/phone部署。

## 正常登记与完成

planHash经正常Coordinate task create / plan approve / dependencies / assignment accept登记，读取deployed字节一致。实施前依赖119必须doneClosed。登记仅认可metadata，不是implementation/install/device accepted；完成必须独立审查+必要验证+对应PRmerge后normalcloseout，不直接写canonical JSON/DB，不改其他item。
