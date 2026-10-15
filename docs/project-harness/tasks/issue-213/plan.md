# issue-213 Android 独立测试环境挂死：有界对照

依赖代码组212已正常merged/closed/receipt consumed，环境跟进213 OPEN。两个同API35/host/MoltenVK/4GB4core QEMU实例主循环与四CPU线程hang，真实Mac IPS hangDetector→GenerateDumpAndDie；不是GPU或Mod定责。保持现Android Mod源码与原游戏/存档、保留全部旧成果和失败证据。

先只读确认CLI图形后端（当前help支持software/swiftshader/lavapipe）及现native deployment的obsolete refresh机制，不新增游戏逻辑/扫描/兜底，不用featurefalse冒充noMod。构造私有noMod测试APK仅通过原producer省略 --deployment 和 Main exact --policy（它们不能用于空manifest；空目录明确被拒），利用新空manifest及已有上一manifest refresh退休Main；真实loader日志须0Mods/无Probe或hooks，Main缺失才接受noMod对照。若空manifest无retire不能接受对照，停留证据，另定最小方案，不临时手写Loader.disable（Android该字段NonSerialized）。同cert、原资源/CRC/interop等核验；现修后私有APK6c7923及Main3aa冻结，可保数据安装恢复。

所有启动只在独立Android AVD，先零PID、备份Main/真实25cfg/native及fullRelease。第一步同原host环境冷启动 noMod 最多2窗，各180秒总bound、无玩家操作；对照Loader/Game相同、只移除Mod，记录appPID/日志/PNG、APK/Main/CFG/native、hostIPS/退出。若同host再次hang，确认“本Mod并非必要触发因素”的有限范围，但不排除loader或GPU。下一步才选择software后端（API35/存储/CPU/内存/安装包/数据同一其余保持），最多2窗noMod+2窗恢复Mod，逐变量检查；任一failed保存、停止该窗口，不无限重启或切GPU。

不得并行游戏，不启动PC/Mono/Steam正式目录/手机。仅停止自建精确ownedPID，公共adbserver不kill，不wipeAVD/snapshot或丢cfg/save，不删除旧工件/工作树；全部材料本机，不发布游戏assets或tag。初始准则：host稳定性至少完整cold观测/退出/读回及原selectednative/old25cfg保持，不能把两个窗过等于长期无bug；天然HP/jobs、MP继续pending。若软件后端不可用/观察无法区分，先只读具体失败，再决定范围，不宣称环境修复。

实现不修改Mod/loader产品源码；必要工具和记录在本任务R。任何运行/安装之前独立GLM5.3/max事前门审及脚本审，重要受管task正常注册acceptedplan/owner/session，不裸改JSON/DB。后续环境参数或runbook修正若有，代码文档必要审查/验证/PR正常merge后代码任务才close，环境效果另记录。共享currentpacket任务窗与Primary对齐后才写；旧167objects/order/9plans/local119保护。当前这是准备草案，不是accepted或已执行。

## 事前审查修正与预注册判读

历史launch日志mtime减am WaitTime到真实IPS captureTime约T1=79.633s/T2=54.753s，1.5max约119.45s；该时间为近似dump时点，不是精确首次stall。每窗180s足以覆盖已观察冷启早期发作；两个host历时分别约4m/60m，不能排除累计运行/重启序列。全清只说明这些有限窗口未复现，不称环境长期稳定或Mod无因。

host/noMod首窗同签名hang即跳过第二窗，进入必要software相位；software/noMod挂死即停止该相位并默认不跑Mod臂。只有software/noMod两窗完整干净，才跑software/Mod两窗。相同host signature需mainloop及多CPU≥约15s stall+真实hostIPS或等价日志；恢复卡顿/ANR/静默退出第三类单列，不能归clean或同signature。

host/noMod同signature ⇒ 本Mod不是必要触发条件；software/noMod同signature ⇒ host/MoltenVK后端不是必要条件，QEMU/CPU/loader仍未知；software/noMod干净+software/Mod失败 ⇒ Mod或其后端交互成为候选而非直接定因；全清 ⇒ 间歇/累计序列未排除，保留证据再决定，不靠两个窗宣布修好。

所有APK同包名同cert安装-r保数据，不克隆app数据目录。恢复6c/Main3aa后必须以真实1Mod/Probe/51hooks回归证据才接受Mod臂，不能名义恢复实际noMod。producer源码只提供线索，实际binary/设备deployment state未知，noMod必须0Mods/无Probe或hooks/Main缺失三门；任何不成立即停。
