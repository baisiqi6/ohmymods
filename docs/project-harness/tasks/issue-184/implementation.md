# Android 营地补员：实现与验证

2026-10-08；代码任务 Issue #184，实验入口版本 0.0.24。基线 `79cea144`。
本批保留既有公共 UI、密灌木和夜袭移植；不修改 Mono，不部署 PC，不发布游戏/loader/interop。

## 修复点与写责任

Android 配置使用三项真实 MelonPreferences entry，直接链接三个共享生产源。复用原
Camp/Campaign/Beggar 生命周期和唯一 Coordinator，未增加第二个 Tick、场景扫描或身份存档。
原 Android 接入候选在稳定等待前可能在上下文不足时写参数、故障后恢复写入、捕获旧原值、
在 OFF 门之前触发 Unity 对象访问，以及部分字段释放异常中断清理；这些候选未安装。
最终首写检查现有上下文/营地 scene 身份，两个字段独立持有成功写入凭据；稳定 reconcile
不重复写，配置变化只更新确实改变的 interval。释放逐字段隔离、只归还仍等于 Applied 的值，
一次停止即清责任；外部写入偏离、Spawn 异常或名册差量不确定后，本场景停止，显式重新启用或
真实新 Apply 才重新进入。Android 删除原周期覆写、2 秒故障 retry 和 attach fallback 写入；
PC 原分支保留，其行为差异由实际 DLL 对照核验。

原生 SlowUpdate 保持存活，中央模式按既有机制使用 max=0 交接补员责任，调用原生 SpawnBeggar。
同值外部写入不能通过值比较识别；所有原生 writer 与联机生命周期没有完整证明。营地场景判据
只比较 Unity scene.handle，当前 world/gameLayer 身份另校验，未证明同 scene 不同子树会排除。
一次 scope/spawn 日志使用现有 profile/ownership 收据，只观察、不改变资格或纠正状态。

## 源码与编译证据

最终实际 W 源与静态冻结检查一致，独立复审通过，无跳过旧断言。

| 验证 | 结果 | 边界 |
|---|---|---|
| SDK10 实际 Android interop Release | 0 warning / 0 error | 最终 Main `a93656e7…`，199168 B |
| 生产代码 typed Android host | 106 passed / 0 failed（原98＋8诊断路径） | 使用游戏 API doubles；OFF、接管、故障、释放、计时、归属、回池 epoch |
| 最终 Main 适配 / 旧配置 | 789 / 629 passed，各 0 failed | 20 项 entry、6 handler 的真实类型/caller、源码冻结和 UI 接线 |
| 原 Grounding 套件 | 17 passed | 只读诊断，无位置或物理写入 |
| SDK8 PC 全源候选 | 0 warning / 0 error | 237 源、37 份正式声明资源；只构建，不部署 |
| PC 真实 DLL 语义/资源对照 | MATCH，0 差异、0 允许项 | 1307 共同类型、6901 共同方法；37 资源一致，PE/MVID 不要求相同 |

私有证据目录：`.local/tasks/android-port-resume-20261008/`。前两轮失败和首次 PC 签名差异、
错误资源导入的构建/比较记录保留；不以返修成功抹去历史。本版重建并复审后才打包。

## 私有包与设备验收

以下 a551/926f 为初版已安装候选，保留失败历史；最终兼容候选为
Main `a93656e7c4a55ae3d817360863b6542bddf0adb8cee1a7e0bd30b2bf0460d409`、
APK `b2d8b2f3a8927d703eb619a15b25de35036282f218a0e81768f7bb2f9f1daecd`，
全 ZIP CRC/单 Main/签名与同测试证书已通过；最终改变路径的设备验证已完成。

Main SHA256：`a55107a969298e6dd3292a3ce2ddb959ff5954ffc459e9b2cf2b7b3f11df5ff1`。
APK SHA256：`926f4572266e8cd8c726ab8177782263dccf68c0884eef2020bfccd06f6faed9`。
全 22687 ZIP entries 已读取并核 CRC；保留原 22313 entries 字节，单 Main，无 helper，
签名通过且与原已安装实验包同一测试证书。APK 仅私有本机实验，不入 GitHub 分发。

本机 API35 ARM64 模拟器保留数据更新已完成：35 个真实目标形态符合预期，20 项配置
触屏设置及 ON/OFF 冷读回、旧 17 项保持已观察。两个 ON 会话各有一次 camp scope：
原 max=2、interval=120 → max=0、interval=1，当前样本 childOfLayer=true。
该样本只证明接管，不证明全部营地范围或所有 writer。自然 NativeSpawn 未观察，原值读回待验。

首次 ONtoOFF 窗口实际 125.86017 秒（含冷启动，超原计划 120 秒 5.86017 秒），
原始时间戳与超时记录保留；该窗口内只读 Grounding 首次读出现新的 MissingIl2CppInternalCallException；
当前日志仅有异常类型，已保留完整现场并恢复 OFF/4/120、停止游戏。
已用原一次日志补充异常消息/栈：`Rigidbody2D.get_simulated_Injected` 无法解析，
取证 1.4756 秒内立即关闭并停止。原始 APK 完整类型声明与正确版本基库比对确认
simulated/constraints/collisionDetectionMode getter 和碰撞层查询未含原生方法；
现 wrapper 由基库补全。后面三项仅有静态风险证据，不宣称曾实测失败。
已对 Android 只读观察内容作显式不可观测适配，保留 bodyType、gravity、实际 linearVelocity
转发及 collider bounds 等原生已有读取；代码 Issue 保持开放；
不能因为只读异常被隔离就宣称 Android 完全兼容。冷 OFF 仅两条已知 loader warning、无 ERROR。
具名 before/final native SHA 相同，不表示中间从未自然保存。
手机、多指、全部 biome、联机、完整换岛/读档/池重建和长期运行继续待验。

## 版本与余量

0.0.24 只标识 Android 实验入口，不增加 PC 正式发行版本、不计多轮内部返修。
完整盘点已完成；公共差异优先集中处理，后续按角色/战斗/身份与存档/经济助手依赖组推进。
剩余约 6–10 组、65–180 工程小时（含本批；宽区间规划），手机和联机验收日历单列。

## 最终本机改变路径验证

最终候选 a936/b2d8 保留数据更新后，ON→停止的计时为 0.99034 秒（含末尾截图/取证的整轮约 1.6375 秒）；该窗口收到 8 条
`first-observed` 样本并关闭停止；不是 8 次新生成。实际 body=Dynamic、gravity=1、
velocity、ground top=0.875、物理 Collider17 minY≈0.88 等支持的读取成功，四项
缺失读证据字段固定为 unavailable，对应完整 loader 日志没有诊断失败/MissingICall 或新 warning/ERROR。
35 实际补丁形态、单 Main、20 配置/旧17保持；最终 Population=false、4人/120秒、
游戏停止。具名 pre/final native SHA 同为7ba5f975…，只证明两个时点相等。

本批源码/host/PC/API来源与改变路径审查通过。自然原生 SpawnBeggar → 名册差量1、
实际关闭后的 camp 参数读回、同 scene 异子树、全部 world、换岛/读档/池重建、手机/多指/
联机/长周期继续待验；不以接管或 first-observed 样本宣称这些完成。
