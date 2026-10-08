# Android 人口补员批：完整范围与实施计划

基线固定79cea1441d7967d9721d6b8b2895e50b1caa1c45（release/v9.5.13，源worktree codex/android-population）。已合并Android公共UI/Dense/Night完整保留；最新共享HoldPurchase实际SDK10Main0W0E/strict697/0，旧17entry30targets仍通过。人口三共享源与10月5日逐字相同。独立原生局部事实及未证边界见本批lifecycle-independent-review与旧两份资源审，原scanner超预算/越界worker退出历史完整保留。最新用户直接恢复授权，不假主线已实机验收。canonical单写交接尚未完成前不进行task mutation。

## 产品范围

移植PC完整营地补员/容量/刷新间隔：共享`PatchRoles_BeggarCamp.cs`＋`PatchPerformance_Population.cs`＋`PopulationGrounding.cs`同源链接，原生SpawnBeggar补员、稳定营地归属、world-authority、当前scene/layer和tutorial/registered/native sync-pool门保持，调低或加载不删已有乞丐。Grounding只读诊断沿原reconcile，无新物理/位置写或全场扫描。Ninja藏点附带调用只ANDROID预处理排除一行；跨世界Ninja/Holder/Castle与工具/弹物/FX/sync完整族留后续，不宣称本批Ninja已移植，不stub类或照搬PC41/43。

平台修订全部`#if ANDROID`，PC原预处理/实际DLL类型方法资源零差异；现代C#；不构建/部署Mono或启动PC游戏。Android使用现global namespace/log桥，不新增第二宿主。Coordinator继续唯一注入MonoBehaviour.Update，Root不新populationTick，不重做计数器/存档schema。

## 实际配置和公共UI

唯一Melon分节新增3项：`PopulationEnabled=false`分批门、`BeggarCampCapacity=4`范围1–20、`BeggarSpawnIntervalSeconds=120`范围1–120；Constants同PC120/4。加载只在边界clamp，无Save/扫描/nativeapply；值来自真实entry，无状态镜像/每帧磁盘读回。单个真实变更调用一次Save、同值0；原17值不变。容量和间隔改动沿现0.5s RefreshSettings只重置未来补员deadline，不追补、不删人。

公共人口页在名单之前接`Toggle`/`Step`既有控件，中文沿PC“乞丐刷新间隔”“每座乞丐帐篷上限”，单位秒/人；容量点击循环1…20→1；间隔通过既有Step循环120→60→30→10→5→1→120（完整1–120合法载入值保留，非预设中间值进入下一较小档，例如45→30），帮助明确触屏档位而不宣称连续slider。配置仍接受PC完整合法范围；手机控件可后续共同integer交互改进，不为本批重建UI/手势。两行原默认与PC一致。人口门中文“营地补员增强”，帮助说明开启按当前/后续场景、关闭交还原生；精确生效语义以以下事件为准。Ui信息不披露内部工程过程。

## 写责任与原补偿清理

原生Camp.Start已观察注册营地与Haglet句柄，委托目标未解析；HandleAuthorityChange尾调enabled，SlowUpdate迭代器每次循环原生清 `_beggars`再近距离重建，5s/interval为请求等待而非实测墙钟。不能从选定body称全writer。保留原生协程不hook薄driver，不复制它。

ANDROID default OFF在Camp/Campaign/Beggar五事件及Coordinator入口、Grounding观察进入时先纯托管门，无自有责任时在null/Pointer/native访问前退出。ON由真实设置动作一次调用Coordinator.EnableCurrentScene（实际`CampaignSaveData.current`已有getter声明；复用BeginScene），冷加载由原ApplyToScene postfix，晚营地由原Awake。这不是扫描新场景/虚构getter；BeginScene仍验证真实manager/world/layer/current，并原3s等待/network-ready。Attach不成功或ctx缺失不写营地参数，保持原生，显式日志；不安静等待2s无限恢复。

每活体CampProfile持有本次作用的原始参数、已提交值和两字段独立写凭据（已成功写入者才拥有；部分成功在同catch归还已确证字段，无猜测写失败结果/重写）。首次接管时Capture原生值，中央模式写max0和备用interval；对同一已拥有目标值不再次写。配置间隔变化才在原RefreshSettings边界更新备用interval。现`ConfigureCurrentCampsForCentralMode`的0.5s反复覆盖删除ANDROID路径，保留原有reconcile仅给归属计数/发现新营地/已回池life，旧PC路径完全保持。

OFF真实动作/失权/scene变化/注入OnDisable/异常停止：沿唯一释放方法一次处理剩余字段凭据，原生当前值与本次Applied精确等值才还原Original，不抢外国writer已改变值；尝试后释放责任并清上下文/名单，不反复每帧Restore，不新增重试/镜像。相同值外部写无法分辨，明确边界。新scene或玩家重新OFF→ON才可再开始；不在故障阶段沿原2s retry恢复。去掉ANDROID attach-fallback/故障2s retry与逐帧restore重复支路；外部瞬态证据缺失，不以旧代码存在证明必要。自动周期补员deadline原语义保留，SpawnBeggar调用异常/名册差量不为已付操作做盲重试，按下述故障停止。

真正准备原生SpawnBeggar前用现commit门复核所用Camp仍拥有本次参数（当前==Applied）及配置/authority/context。外写偏离则停止中央补员、一次交还仍拥有字段，不周期覆写/重新夺权；字段检查仅现spawn提交点，非新增scan。每次Spawn成功仍沿真实名册差量1绑定；调用异常/差量不确定关闭本scene中央工作并交还，不在下个deadline盲目重复同次结果。身份/原生池/sync不新造；既有Beggar enable/disable epoch与回收名单保持。native间接callee/完全MP/所有writer未知保留到实机验证。

## 实现责任与验证

Worker限定上述3共享源(Android条件块)、MobilePlayerConfig/MobilePopulation、csproj/test相关，Probe/版本/显式Harmony注册Root独占。实验版本0.0.24。Root显式注册全部既有population方法集：CampaignSaveData.ApplyToScene postfix、Camp.Awake prefix+postfix/OnDestroy prefix、Beggar.OnEnable与OnDisable prefix（新增5个真实target，旧30保持到35），实际alias/sharedptr可达与完整prefix/post/final形态核；不PatchAll、不hookthinHandleAuthorityChange/Start。

必要typed Android host直接链接生产Coordinator/Camp/Grounding和真实ModConfig（实际GameAPI onlydoubles，不复制业务）：OFF默认/旧17/冷ON与Save矩阵，scene进入/晚Camp/3s/network-readypause/auth/跨context，稳定归属/回池epoch/提高降低容量/间隔重计时无追补、不删旧人；每field写计数证明稳定多个reconcile0repeat、settings真实interval1write、OFF清账只一次、外写两字段分别不夺权、局部写失败/Spawn结果异常不重试。真实nativeAPI签名编译与injected static-storage/HideFromIl2Cpp必要annotation核，不镜像假interop。

一次最终SDK10Main0W0E、default与oldcfg strict（20entry35targetshape/源码pin无skip）、affected typed suite、已有Grounding套件；相关PC代码分支不变，SDK8完整237源/37canonicalassets baseline/candidate实际DLL语义比较0diff/0allow。源审/独立必要UI审后一次私有APKpack/CRC/signature/contentonlyMain审，再保data安装。设备UI三项/实cfg冷读回/old17same/35targets/无新增loadererrors；自然真实ApplyToScene/camp首1组日志/有普通营地时短interval真实NativeSpawn→名单差量与OFF原值读回，没有条件则精确保留pending，禁止manualInvoke/spawn/time改档/新造战役。结束Population=false、仅Main、游戏停止；原存档nativehash具名读前后，不声称自动保存完全未发生。phone/多指/全部world/联机/长周期验收另列。

正常Issue/assignment/PR交付合并后关代码任务，实机待验单列，不发行原APK/loader/interop。必要canonical写权窗口/主线实质重叠再集中联系peer；日常步骤只本任务记录。

## 估算

本批预计6–12工程小时（原ownership/writer适配与首次完整host为主要成本）；剩余约6–10个依赖组/65–180工程小时，包含本批与角色/战斗/身份持久化/经济助手，wide规划非工期承诺。配置与UI接线计入相应组，不按key/错误数当功能比例；玩家phone/MP日历另列。出现角色/资源架构差异后重新按证据估算。


## 正常任务关联

GitHub Issue: https://github.com/baisiqi6/ohmymods/issues/184
Canonical task: issue-184
Canonical plan: docs/project-harness/tasks/issue-184/plan.md
Dependency: issue-148（normal done/closed，当前canonical读回核对）
Owner: mac-codex-ohmymods-android-operator
Session: codex-android-population-20261008
Branch: codex/android-population
Implementation plan core SHA256: 615142f2d354c3f076d9a3f9cd7deae198ea5373c91e06a2fe9c4fda9c95c412
