# 后继批次草案：现存角色体型叶组

依赖Issue196代码收尾，继续LemonLoader/MelonLoader+Il2CppInterop/Harmony原架构，不切Bep。只有原生现存Ninja/Banker/Hermit/Dog的y体型，无跨biome角色生成、工具/投射物/池注册、身份分配、保存schema或PC新玩法。当前shared PatchRoles_ScaleAdjustments与GreekScaleScope、现有Probe Tick/LateUpdate复用，source经过精确PC预处理比较保持行为。

有界8native查询：8声明各unique map地址，FDE216–964B，但Hermit.OnDestroy private按当前坑32不得directhook；显式只接7长目标：Ninja.OnStyleSwap(int)、SetAnimation(int)、Persistent_IBehaviour_ApplyData(Object)、DeserializeFromData()，Banker/Hermit/Dog OnEnable()。OnDestroy方法可作为未接源码编译，不能注册/移植标为8已接；GreekScope Tick本已有Missing/identity变化清Requests，Deferred异常保留，池inactive保留，不新补清理或以新扫描代替。FDE/map唯一不自证detour安全，具体hook机制/地址与最终writer按已有边界真实审查/有界加载验证，天然效果未样本pending。

ANDROID file-local using ScaleRegistryHolder=KingdomEnhancedMod.GreekScaleScope，直接reuse Register/Unregister，不创建薄壳类/假型，不为registry名字引入Worker整包或新ClassInjector/MonoBehaviour/driver。现Tick/LateUpdate和猫所有权保持；Ninja权威/remote形态调用原PC读Animator bool路径，不触网络buffer。只y，x朝向/z保持，native最终写者/世界切换/池复用与异常责任按sharedscope原语义，不补guard/retry。

分批启用使用一个真实MelonPreferences bool RoleScaleEnabled默认false，唯一实际consumer为加载时显式7目标注册。世界页用现Step中文暗金控件“希腊角色体型”，按钮“下次开启/下次关闭”，帮助“下次启动游戏生效；只调整希腊世界现有角色体型”。用户修改只变下次启动注册选择，一次真实变化Save，初始化不Save；不增加已应用bool镜像/全演员扫描/热关时猜测账本释放。不得让该门改变GreekScope总Enabled或猫，亦不得一边永久旧registered y一边在UI承诺即时关闭。若发现必须即时生效才正确，应先论证既有scope ownership路径，不先造角色owner子账本。

Worker按真实北京时间/路由只ownedsharedsource候选alias及direct-sourcehost，Root统一cfg/menu/project/Probe显式target。现代C#，真实game/loaderrefs；SDK10产品0W0E、sourcepins保全、真实default/oldcfg adapter、PC当前release完整语义零diff/资源MATCH无部署，必要host覆盖sharedy写入/世界active-unknown-inactive/池/identity/xz及真实Ninja各形态/六HermitType值及默认unknown；尽量复用已存在GreekScaleScope/FarmCat host，不复制算法或假native保存/生物生成。Probe新版本0.0.27、7目标增量精确audit、未启时count仍39（加载时角色门OFF）；ON相同explicit contracts原39保持+7=46。新UI文案source/真实Menu持久设置读取/热切明确重启作用域；后续冷启动ON/OFF注册验证以artifact/备份/真实runtime gate分批，手机MP/自然role效果独立pending。

计划与Issue/normal受管登记在196全部相关PR正常合并后；branch codex/android-role-scale。不预付fullMod complete，全库存2/111缺口保留，预计此叶组2–6工程小时（观察样本另计），整体8–10风险组/80–210小时仍条件区间。
