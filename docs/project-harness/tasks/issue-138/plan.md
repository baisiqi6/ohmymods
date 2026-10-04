# Android 快速森林退缩：复用共享等待参数补丁（C7）

依赖 Issue136 normal done/closed + PR137实际merge；Owner mac-codex-ohmymods-android-operator，branch codex/android-forest-recede。peer101 Shared/PR128墙塔不改。未满足依赖/独审/GLM条件之前不实施。

目标是在原有小悬浮球 World→Vegetation 页开启默认关闭的三倍森林等待加速。仅当现存归属门确认本世界普通ForestItem时改本次FadeAndRemove的ref delay：显式positive delay/3，else 用当次removeDelay×Random(.5,1.5)/3，有限positive才提交；默认关闭零native访问。native delay0本会自行随机等待，prefix先算再缩放是为了让该分支也生效，并非修复0s故障。inactive/权威早退可能多消耗一次RNG，保留PC既有语义并记录边界。Fade、fire、destroy、forest边界更新全交原生，native字段/协程/资源/存档不写。

复用：从 il2cpp/PatchWorld_OptionalVegetation.cs 移出三小forest helpers、常数3及唯一Harmony wrapper至新 shared il2cpp/PatchWorld_FastForestRecede.cs；var entry和必要ANDROID aliases适配两平台，Android直接链接，不复制Dense1100行。移除旧forest helpers/wrapper，Dense独立用途的CurrentWorld/FailOnce/租约/回收保持。现有OptionalQoLScope复用，forest active+sameScene+IsChildOf当前layer，item sameScene但允许parallax非child。forest故障单bool warning隔离logger，文案本次保留原生等待参数，无重试/扫描/镜像/新driver。

UI/配置：14entry FastForestRecedeEnabled默认false/loading不Save/Toggle一次Save/READY forestRecede。World原cats行332→Vegetation入口，其他六行640原位；Vegetation328三行Cats98/Fastforest176/Back254，保留WorldPage=true，Back只清VegetationPage回World。原Cats切换及nextlevel文案复用；Home562/Player562/World640/Generation250/Population376/球48命中72默认收起保持。Operator Probe版本0.0.18+router+唯一精确ForestItem.FadeAndRemove(float) Prefix，旧23shape不改→24，无Post/Finalizer或新autoScan。Worker功能/配置/menu/layout/project/tests/docs，独立Reviewer审producer与完整变化。

验证：actualAndroidSDK10/interop Rebuild0W0E；直接编译共享源的PC既有optional-vegetation套件及真实android/OptionalQoLScope typed alias host，覆盖disabled零访问、positive3→1、fallback仅一次RNG、controls/removed/forestactive-child/scene/parallax、异常不改ref且nativebody继续。adapter14entries/load无写/toggle一次写/旧13保持/新页触摸与Back/Cats可达/sourcefreeze受影响许可。PC实际Mac wrapper baseline/candidate不部署0W0E，Dense方法IL/locals/EH/attrs/header逐项同、仅已列forest helper/wrapper移位、新共享类与用途日志允许改变，资源逐字节同；不称PE字节一致或实际PC玩法。

source独审+GLM精确installgate后私有APK Main0.0.18单一，original成员保全/CRC/签名及入口变化核；原API35ARM64 MoltenVK no clear/wipe更新前全prefs/UserData/native备份，首次启动前bytesame。实际24targets/旧23shape/Mainonly/14entry旧13same；World→Vegetation/Cats可达不开启/forestOFF→ON→OFF和ONcold→OFFcold/Back/原生Options不穿透/无ERROR既有Warning/最终forceStop。自然砍树导致退缩可观察才实测，不Invoke/fixture/nativefield/解锁/newcampaign；没有机会则真实三倍等待、手机联机/完整森林cycle/换岛池生命周期pending。

代码提交PR base release/v9.5.13 attach，peer正常merge/代码Issue关闭/同身份normalcanonical packet独立review→markdone→doctor0/0，保存其他完整对象与顺序。实机状态单列。无正式APK/loader/game/tag分发、无PC/手机部署。收尾后继续按功能依赖移植，不逐项等人类许可。


正常注册机械绑定：Issue138 https://github.com/baisiqi6/ohmymods/issues/138；实际依赖 merge 41d9f14f813f1df7edaab56eb758b187f4e13965，issue-136 normal done/closed/doctor0errors0warnings fresh已核。Owner mac-codex-ohmymods-android-operator，session codex-android-forest-recede-20261004，branch codex/android-forest-recede，canonical plan docs/project-harness/tasks/issue-138/plan.md。
