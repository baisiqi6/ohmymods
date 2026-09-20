# 本机实现、部署及启动检查

用户要求希腊舰队小船限定希腊骑士小队，召集量按可用小队/船较小值；只把武士的防御性后退加速3倍；猫每农场四只；核对残影。任务目录标识20260912，实际本轮工作和部署日期为2026-09-11。

## 实现结果
- 舰队：新增FleetGreekSquads。既有Player.ActivateFormation候选阶段按同侧当前可用Greek style3与FleetBoat一对一配对，上游最多4艘，实际_numSquads必须1；已分配本船者优先，不能重复预留另一玩家/船/主Boat/其他formation的队伍。候选专用NativeProbe避免预留资格检查自阻塞。真实评分拒绝成本100000核实，非Greek新登船或错配拒绝，已有同船已登船旧乘员保留原分数，载旧nonGreek队的船不参与新旗帜召集。只在PlayerFormation防预留骑士被陆队抢招；随从stowaway和工人/主Boat一般路径原生。没有生成/删除船或造骑士。
- 预留生命周期：pending跨越原生激活调用，postfix及异常finalizer都Complete；未登船60秒游戏时间超时，已登船不超时。外部任务、失效身份、世界/authority变化等退役。原生登船target仍匹配时允许BecomeStationary→IsEmbarked的过渡。评分只清managed记录及排取消，既有0.5秒coordinator确认本世界/仍自有formation/真实membership后原生Unregister失配船，让native回港；兼容注册后未写_currentFormation即抛错，但不碰另一个nonnullformation。新mainBoat接管队伍优先，原旗帜失配船取消。世界边界和Knight既有OnEnable复用入口清旧预留。
- 武士：已有float SetGoal prefix改ref speed，只在host/live/free/style2/GoToWall/isRetreating且输入等于正finite _retreatSpeed时本次乘3。_retreatSpeed、_runSpeed、冲刺/返队SetGoalNoHaglet路径不修改；新增首命中一次日志SamuraiRetreat。
- 猫：既有希腊单机/分屏加载协程6→4；只回收domesticated、active、明确farmHouse归属且KEM_FarmCat命名的多余猫，保留native、被抓和跟玩家的猫。原生Pool.DespawnOrDestroy正常返回后若仍active就同步失活，避免帧尾Destroy误计。回调先失活后抛错按实际回收计；不能确定完成则停止本农场继续删，其他农场继续。旧存档只读观察共24条KEM_FarmCat命名，可识别旧mod猫。不直接改存档或native登记集合。
- 残影：保留上一轮45/25/10、0.2秒、3ghost+body设计。独立检查Begin/Driver/End接线完整，未见明确静态缺陷；本轮没有获得实战画面或SamuraiVisuals ready，不能说已实测可见。上一轮没有新增client残影触发信号，本轮没有扩展。

## 验证与部署
ZCode0.16.5 worker session sess_839aeca9-5e74-4528-8bbd-4c620fd14333，provider-native modelId及responseModelId均GLM-5.3、provider bigmodel；请求max但实际reasoning未独立证明。Worker提交猫候选，operator修复延迟销毁和部分异常并统一接线；独立review发现并复核关闭两P2。

最终154/154回归：船91（真实helper编译+真实候选/异常finalizer提取），退速43、完整猫源20。独立review PASS；canonical六源与审查candidate完全匹配。实际依赖构建0W/0E；70个可达Unity方法无unstrip桩；4个所选hook解析实际wrapper通过；既有float SetGoal/ref Single核验通过。三个新hook实际GameAssembly地址在Assembly-CSharp方法表各唯一，见native-boat-research.md；此验证不替代运行时分支命中。

E测试副本DLL F32CBE8C769D0D9818EF8143E5F6E32FCA2F5AD6B3C42CC13D1F81BA21C73020，build=4.5.0-fleet-retreat-cats-20260911。确认游戏未运行、旧C1763E2F匹配后备份原子替换；启动2026-09-11T23:58:36.5624549+08:00至2026-09-11T23:59:17.6749084+08:00，仅自建PID31120并于ClockDiag停止。恢复第61天t8.85/daytime/timeScale0暂停场景，无新mod异常。峰值私有内存2.17GiB，最低系统提交余量7.21GiB，所有采样responsive。

存档前后hash均0019A23A1693ECD2F992C9FCF27068A046FC47EBDB4F3B33040163D946B5E985。Player.log中的AppID/用户LogError、Unknown Character Tag、Spart Farticles missing script、CastleShieldShop Invalid NetID等均在替换前日志出现，见逐项比较；不宣称整个Player.log无报错。

## 尚待实际游玩验收
本次是暂停恢复启动，尚未实际举旗、登船、武士后退/残影或执行五秒scaled延迟猫调整，不能把测试等同真实行为已验证。数量min指当前同侧可用匹配；等待登船期间会出现尚未上人的已召小船，不承诺即时零空船。保护猫或无法识别的native猫过多时总数可大于4，不强删；原猫功能仍只希腊单机/同机分屏，线上跳过。已有nonGreek在船航程不强踢；未来新登船受资格限制。真实两端联机/跨岛/取消召集/老档重建及画面仍待玩家验证，任务保持doing。

之前出征动态跟随、Greek火资源、Medieval剑风、Samurai返队伤害、补员诊断、银行HUD与120秒4人配置均保留。未修改配置、D/G游戏目录或公开ZIP，未commit/push/publish。
