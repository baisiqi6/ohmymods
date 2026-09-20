# 立即销毁生命周期隐患：本机候选验收

2026-09-14 实现、构建、回归和必要启动验证完成，已仅安装 E 盘独立副本。
构建 `6.1.5-destroy-lifecycle-20260914`，SHA-256 `319418A073E47D943B9CA068BA8F229E231E6C1A3D8B913ABF823F34EED86E48`；上一版本46AD1CF9及其所有其他功能保留。

## 确认的事实与修复边界

交接 `C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/crash-risk-handoff-20260912.md` 的三处DestroyImmediate在当前源码中仍存在。玩家Player (1).log仍检出17次Unity非法立即销毁报错，但没有直接调用堆栈；本机当前日志检出0次不是复现证明。**修复的是这三处确认存在的生命周期隐患，玩家闪退根因仍未确定，不能宣称闪退已消失。**

弩手marker改为可复用组件，Selected/Active/Residue/Revision分离选择、当前资格、待恢复责任与重入代次。Strip第一步取消旧资格；Apply/巡检/招募判据均读取有效状态，冷却账本避免同帧Strip→Apply或异常重试重复乘、重复恢复。真实Pool.FastSpawn作用域内的Archer.OnEnable先归还旧包再让原生招募运行；重复active NetID回包不会被无条件当成新生命。普通隐藏保留选择，缓存巡检不误清；全局关闭即时资格失效，panel安全Tick处理自有registry含inactive，不新增逐帧全场扫描。

异常池交接用PendingPoolHandoff：若prefix归还失败且原生已分配实际_knight新owner，旧账本退休，不再按相同SO/12射程/1.15缩放误认旧写权。新小队共用的现有弩矢、皮肤、缩放登记保持，后续postfix/scan/unwind也不撤新包。这是root最终整合修正，有精确共享资源回归；初版用新建SO的测试不足，已补齐。marker本身不Destroy或DestroyImmediate。

Castle两个错误商店清理入口只登记精确对象。ModPanel.Update安全阶段按实际world/layer/planner/scene、对象、tag、slot和仍错误物品复核后，分阶段原生注销、停用、延迟Destroy；每个回调后重新捕获世界并复核，列表null是unknown，不能当注销成功。失败退避与限额日志，已开始清理不因3次失败、暂时配置关闭或未知状态丢账；已发起Destroy只等真实null，再补位。Legacy和Placed申请正确合并；同world补池不会无条件Clear队列。

## 实际2.4核验

GameAssembly SHA `CD8C2B822B12F5416E73234D6D1052EFB499FE324ACEFF6264E0C87C6F8EDFC1`。2.1参考中的RemoveShop行为不能直接套用：实际2.4 `RemoveShop` 0x762ef0/272bytes，0x762fbe比较槽位对象，只有精确匹配才0x762fe2 SetPlacedShop(null)。PayableShop.OnDestroy 0x67a080/336bytes仍会再次RemoveShop，但原生已有保护新槽位的身份检查。因此不新增商店RemoveShop/OnDestroy钩子，也不跳过原生建筑回调解绑。Payable.OnDisable清payable与高亮，不代替商店注销。

Archer.OnEnable 0x4b32d0/1040bytes、OnDisable 0x4b2c10/1104bytes、Pool.FastSpawn 0x6c10a0/3120bytes均实际唯一长入口；新增生命周期补丁不含短getter/Dispose钩子。原生AddArcher/DistributeFreeArchers在OnEnable主体中执行，旧包必须在之前处理。

## 已通过

- root亲跑31项弩手/410断言、49项商店回归。含真同SO接管、隐藏旧缓存、池重复回执、重入、恢复中断、关闭inactive；含同回调改正确物品/tag/scene/world、重新登记、unknown注册表、副作用前后抛错、连续Destroy失败及同world重新注册池。测试直链生产逻辑，Unity/game边界为替身。
- 既有26套回归通过：23套原console、archer combat75项、Hermes core55项与visual27项。它们的生产模块没有因后续本轮修订而变化。
- IL2CPP强制Rebuild 0警告0错误；285个相关可达Unity方法无unstripping失败。旧1831方法中1807原样；其余变化仅本次两个模块、相关调用/方法迁移、panel接线和构建标记。全部旧Harmony属性保持、无任何DestroyImmediate调用残留。银行/缩放/鹿/补货/便捷/弓箭/Hermes/隐士模块保持。
- 独立内置reviewer多轮原生与代码复核，曾只读执行已构建测试DLL复现两个初版缺陷及共享SO交接缺陷；最终小改复核通过，并独立执行31项/410断言通过。
- 精确候选PID 27704，2026-09-14T07:11:40.7639543+08:00 至 2026-09-14T07:13:33.2410228+08:00，约110秒受控运行，chainloader/RunningGame成功。实际Archer.OnEnable/OnDisable、Pool.FastSpawn及此前生命周期入口读到FF25；隐士短getter17原字节保持，日志无IL2CPP backend fallback。仅既有NpcShieldUser.SetShieldEnabled NRE，不将其称为本次修复。
- save `3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A` 保持；config `4CE570229201E9D6AFA77ED66B89C7628A3C34C2DDF506844A6E77B50C9BD398` 保持；银行5709→5709。游戏退出后原子安装同一候选；保留备份，未覆盖用户存档、未提交发布、未写D Steam、未修改Mono。
- 两实现worker本机OMP deepseek/deepseek-v4-flash max，provider-native元数据无fallback。初轮write审批拦shell，root纠正“测试已写”和“已执行”的区别，调整隔离执行模式后真正测试；最终均exit0。root额外完成共享SO交接与测试隔离修正。

## 仍待实际验收

| 场景 | 当前证据 | 实机状态 |
|---|---|---|
| 物理拾弓转职、普通/弩手交替 | 代码生命周期回归 | 未主动实机触发 |
| 同帧池复用、死亡掉弓、隐藏再启用、关闭功能 | 回归与native顺序 | 完整实际操作待验 |
| 城堡升级、旧错误商店修复、补位 | 49回归与原生注销审计 | 实际旧店现场待验 |
| 换岛、正常保存读档、联机重复回包 | 代码边界/native依据 | 本轮未完整实机走通 |
| 玩家闪退及17条报错是否同源 | 只有原始日志及风险点 | 根因未确认，未证明消失 |

受控启动不替代上述真实场景。harness状态保留doing，表示场景验收未完成；本机候选交付已完成。

## 证据

`C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/destroy-lifecycle-20260914`：baseline/source-manifest、verification.json、build.txt、unity-audit.txt、crossbow-tests.txt、shop-tests.txt、clean-test-results.json、hermes-core/visual-tests.txt、previous-test-results/archer-prior.trx、native-disassembly.txt、native-findings.md、readonly-log-summary.json、worker-receipts.json、permanent-receipt.json、permanent-LogOutput.log、installation.json。private-*状态备份留本机，不纳入项目或发布。
