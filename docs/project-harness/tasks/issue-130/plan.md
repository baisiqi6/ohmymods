# Issue130 — Android 船员容量：原生槽位与共享策略

GitHub: https://github.com/baisiqi6/ohmymods/issues/130
Plan审批: OMP zhipu-coding-plan/glm-5.3 max/noFallback session01a105da… APPROVE_PLAN_C4，draft9636de15…，允许本次机械IssueID/已读回mergeSHA绑定，元数据并非source/install/device通过。

基线 PR129 正常合并 72a2b7451cf856a4c0b61aaa4fcdfa4226837121（source59b9f8c的文档冲突正常合并）；功能实施只在 canonical127 done/closed 后。Owner/actor mac-codex-ohmymods-android-operator；branch codex/android-boat-capacity；session codex-android-boat-capacity-20261004。复用本chat干净managed worktree，新分支从接受merge创建，不reset/重复checkout。Root peer确认不维护Boat两路径，12非冲突PR129路径保持；其它peer Issue/PR路径重叠在实施前读回核对。

已审事实仅完整129838metadata/125041非零method定义+36Boat家族：OnEnable0x24eece4 this4max44/48/4c/50供原生RegisterUnitSlots capacity，ctor默认3不是prefab当前值证明。Embarkable真实物化EmbarkeeSlot列表，registered先检查早退，之后GetTypeFromHandle；x2/context身份unknown，不说Class::Init。archerPositions原数组长度未采，不改也不称四槽。Pike/Farmer保持原nativegate。此范围外consumer/全部AOT实例/异常注销对称/池化/真正登船航海均unknown，不预设只是UI读或自动自愈，不加全场或全库扫描来凑闭包。

## 复用与 scope
GLM选B已批准方向及具体义务。Worker新增il2cpp/BoatCapacityProfile.cs，无Unity/Ilcpp/loader类型，无方法/状态：public static class BoatCapacityProfile，public const int Workers=8, Knights=6, Pikemen=8, Farmers=3。同namespaceKingdomEnhancedMod。PC il2cpp/PatchWorld_BoatCapacity.cs仅四个赋值literal换上述const，不改PCPrefix/Postfix/Finalizer/State/异常语义；inline后的该类所有方法IL/locals/EH必须等价。PC实际Mac只输出隔离产物、不部署；使用root.local/build/Integration.csproj新私有副本，将Compile/Resource路径指向固定accepted源码snapshot，PluginInfo原样复制。无需构建Mono。若现base全PCbuild被无关缺依赖堵住，先诊断既有路径而不是改功能或静默fake。
Android.csproj只CompileInclude共享profile；不直接link有部分快照/双清理风险的旧PCPatch，不新增genericfieldborrowframework。新android/PatchWorld_BoatCapacity.cs薄适配原生Boat.OnEnable消费位置。

## 具体实现
namespace KingdomEnhancedMod static class PatchWorld_BoatCapacity，alias Boat=Il2Cpp.Boat。Operator注册接口 Prefix(Boat __instance,out Borrow __state)、Finalizer(Exception __exception,Borrow __state)返回原异常。无自动Harmonyattributes。内部Begin/End统一，native__runOriginal/result/slots/船位置/航海/存档/gate不写，无OnDisable/RegisterUnitSlots额外挂钩。
ModConfig.Enabled && BoatCapacityEnabled(defaultfalse)才介入。prefix一个错误包络，先完整读四个当前max成功后才建并发布Borrow（同一Boat引用、四个Original、四个attempt责任位），不要用ctor3当当前值。任一getter失败：无state、零setter/零cleanup，仅可见error。
写每字段前先置其attempt责任位，然后按sharedprofile写Worker8/Knight6/Pike8/Farmer3；setter抛时该位已记录（写前抛/写后抛都能确定责任，不猜成功）。成功与原生异常仅Finalizer一次End。prefix自身写阶段失败已有state则inline一次End并finally清null，Finalizer不可retry。
End只对attempt字段，逐字段独立try/catch：读current，等于该profile applied int值才一次writeOriginal；不同值保留并warning，不能识别写入者/同值外部write。一个归还失败不阻其它字段的独立责任、不再retry该字段。捕获对象参考不可替换；不存/重读同代理Pointer或InstanceID猜life、不加rider/world/scene新guard，native读取真实失败日志。所有cleanup异常不逃出并保持原nativeException同对象。关闭默认零state/字段write，配置切换不全场扫描、不热改已登记slots、不给登记早退/门变化加补偿。

## Config/UI/Operator
单BoatCapacityEnabled boolentry默认false总11，沿现唯一category、ToggleBoatCapacity entry翻转+日志+Save各一次，无配置镜像/settingcallback，旧10保持。World七行98/176/254/332/410Fast/488Boat/566Back，PanelHeightWorld640，末630；Home484/Player484/Population376及球48visual72squarehit不变，同FloatLayout绘制/actor/原生命中层共用几何。文案 Extra boat crew: ON/OFF / Newly initialized boats，不说热扩容，关闭不删除已建slots；特殊两职业受原生权限门。
Operator Probe显式Boat.OnEnable空参Prefix+Finalizer，只新1unique19target，version0.0.15/featuremarker；旧18targetshape保持，modsettingsInitialize先于注册。Operator不写业务patch，Worker不改Probe。Profilepure常量策略共享，不复制登船原生机制。

## 必要验证
实际Androidinterop完整build0W0E、旧AdapterTests配置/几何/metadata/freeze回归、defaultOFF、oldcfg不添旧值改动、加载ON与单次save。新typedhost直接编译实际AndroidPatch+共享profile，不镜像算法/伪Unity：完整快照失败（第2/4getter）零setter；native消费8/6/8/3后恢复各进入值；prefix第2setter写前/写后失败仅attemptfields有责未attempt不动；nativeexceptionidentity+每字段attempt一次；restore个别get/setfail隔离其它责任无retry；途中config变化仍按捕获清理；不同外部值保留，同值不可判来源。模型不实现fake登记池/热重注册/新船玩法。
PC固定baseline/候选actualMacbuild noDeploy，以MetaDump方法IL/locals/EH tokensemantic比较Boat_MainCapacity_Patch所有旧方法等价（constinline）；只有shared新type4const元数据，既有其它方法保持。C#源码hash保护PC除literalrefs/Profile新类之外、Mono未变。曾有同类PC异常路径保留为当前scope外，不在Android夹带修。
源码独审、exactAPK原22313资源/mainonly/签名审计、GLM exactinstall/delivery门；仅隔离API35ARM64同AVD备份旧main/prefs/native档，启动前native+prefsbyteequal；19unique/旧18shape/noERROR。正常UI OFF→ON保存+冷启ON→UIOFF/纯main冷启、旧10保持，不造船/nativeInvoke/跳岛/存档字段/钱包库存，也不PC/phone部署。若自然没有新的Boat.OnEnable/Disable复用样本，则slots容量/登船/航海/池/gate实际玩法单列未验；native注册与host不得假技能/船已完成。

代码交付仅sourcePR base release/v9.5.13，审查验证+对应PR正常merge后GHIssue/canonical代码scope收尾，玩家证据单独记录。正常create/planapprove/dep127doneClosed/accept，同ownerbranchsessionoperation，初始metadata不等source/install/deviceaccepted。无publicAPK/tag/release、外部message或范围扩张。完成后继续依赖块，不停等逐项授权。
