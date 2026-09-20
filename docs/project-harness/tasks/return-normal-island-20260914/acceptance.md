# 返回普通希腊岛屿（2026-09-14）

用户要求从最终决战回普通世界测试。已完成：currentCampaign1/currentLand10→9，仍是希腊biome5，返回原来建成的上一岛；正确E独立副本PID27604可见窗口保持打开，日志timeScale=0确认暂停。

原存档在过渡点：land10没有岛屿记录，PreviousLand9；land9最近保存时间与campaign时间532.112575526809相同，3157对象。carryForward.present=true、TrojanHorse=1，Archer/Worker各12项（每项是一人的工具flag，不是统计总和）、玩家金币134/8、舰队数4；land9已有有效舰船1–4共4条。

只手动改4个标量：campaign.currentLand和currentReign.currentLand 10→9；carryForward.sailAwayGroup 1→0（Boats原生返岛）；carryForward.numFleetBoats 4→0（岛上已有四船，不额外生成）。PreviousLand9保持，避免LevelTransitionPositionOverride；carry.present/货币/随行数组、所有岛屿记录和任务进度均保持。JSON定位标量片段替换，逆向还原证明其他解压字节完全不变，并DeepEquals精确预期树；不重新序列化各组件payload。

实际2.4原生确认：ApplyCarryForward 0x71db10按+0x234的group==0转SpawnCarryForwardUnits0x723560，之后调用ApplyFleetBoats0x71e980并清过渡struct；ApplyFleetBoats按+0x230生成指定数量，没有按当前岛四船扣减。4条现存FleetBoat是普通存活记录，置新增数0可避免额外生成。2.1 Game.Sailing与当前快照舰船持久化存在时机差异，不据单档绝对推断历史单位身份；本次按游戏原有carry消费还人，实机计数符合24人。

完整原档备份：`C:\Users\ADMIN\AppData\LocalLow\noio\KingdomTwoCrowns\Release\global-v35.before-return-normal-20260914-082115-863.bak`。原SHA `3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A`，4字段候选SHA `2B589F87F8862180EA3F87F7182E5402DA89BDD61EB3318783106A2A822E0ACA`；游戏退出后同卷File.Replace，双哈希复读通过。模组DLL319418A0未动，配置手工操作前后FDADB211未动，银行手工写入时5709未动。

实机加载验证：newIsland=False/sailingIn=True/newReignForIslandPostLoss=False，RunningGame成功；PopulationHUD工人35/弓手132，相比原岛23/120各加12；FleetBoatRecovery active4/materialized4/recovered0；已有弩炮5、火焰塔1。原生返岛会落在码头/沉船附近，并按原生流程推进时间，不承诺停留原坐标x84或原时刻。

游戏已自动保存：SHA `A588D870BB527ED1AA79E81327A2CD751A90FEAFAC96D6FA1DFD90CBB25B4A0D`，currentLand与reign.currentLand均9，carry.present=false，3183对象、Archer132、Worker35、FleetBoat4。**这是新有效进度，未来测试不可恢复旧3DE17864决战前档或4字段临时候选。** 游戏运行期间银行自然收取变为5791，未回滚。完成时日志t16.49/timeScale0，已通过Esc暂停供用户测试。

过程记录：sky.launch_app即使传完整路径仍误启动旧E:/Kingdom Two Crowns（PID24720），发现后强制关闭，global-v35校验未变。随后正确副本PID29848用Hidden启动无可见窗口，用户指出后关闭，无保存变化。最终用完整路径+Normal启动PID27604并复核路径/可见窗口。截图API报SetIsBorderRequired不支持接口，未宣称截图目视验证；改用窗口accessibility、原生日志、自动保存复读确认。只保留这一可见游戏进程，未干扰其他任务OMP进程。

证据：`C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/return-normal-island-20260914` 内prepare.py、dry-run.json、apply.ps1、write-receipt.json、loaded-save-receipt.json、runtime-evidence.log、native-disassembly.txt；private-*备份不纳入仓库。独立reviewer /root/return_island_reviewer只读复核四字段策略、2.4枚举和原生carry逻辑。没有提交/发布或改模组代码。


后续纠正：本次只验抵达/人口/舰队，漏验原岛通关状态导致蛇/马/夜袭缺失；已通过restore-current-battle-20260914恢复。有效新档83863274及后续用户进度，旧A588/3DE不得回滚。
