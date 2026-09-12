# population-performance-010 — 超大人口岛性能治理

## 目标

- 修复快速乞丐刷新与面包房互动导致的营地人口泄漏：继续保持每个营地约6秒补1人，但每个营地拥有真实、稳定的5人硬上限。
- 在不直接编辑压缩存档的前提下，对当前异常岛现有超额乞丐做一次安全、分帧、world-authority清理。
- 本次候选只交付营地硬上限与遗留清理；性能诊断、工具分配优化和空闲角色LOD全部留到后续独立切片。

## 已有证据

- 当前campaign 1、land 7存档含2158个对象与1132个`CharacterData`；其他已开发岛仅52～137个角色。
- 当前岛角色：Worker 458、Peasant 301、Beggar 158、Archer 129、Farmer 49、Knight 33、Pikeman 3、Ninja 1。
- 当前岛只有2个BeggarCamp，却有158个Beggar，并有2座面包房升级塔。
- 2.1原生`BeggarCamp.SlowUpdate`每5秒只把距营地小于5单位的Beggar计入`_beggars`；`Baker.ComeToBaker`会从camp移除并清空`beggar.camp`，因此面包房会释放营地名额。
- 2.4 interop仍公开化`BeggarCamp.SpawnBeggar`、`_beggars`、`slowUpdateRoutine`、`maxBeggars`与`spawnInterval`；直接调用wrapper不依赖Harmony命中private native thunk。
- 当前Player.log没有NullReference、Pool not found、unknown pool或RPC刷屏；人口规模是当前最高置信性能根因。

## 高风险安全边界

- 仅IL2CPP主线；不修改/构建/部署Mono，不碰Steam正式目录。
- 游戏运行时禁止替换DLL。部署前再次备份`Release/global-v35`，只写独立测试副本。
- 不直接解压后重写`global-v35`；所有清理只由当前world-authority通过原生Pool/网络生命周期执行。
- 客户端不分配营地所有权、不生成、不清理、不修改人口；只接收原生同步生成/回收。
- 失去authority、切岛、World/gameLayer更换、Mod关闭或对象失活时立即取消当前清理/生成批次并释放托管状态。
- 清理不追求强行降到5：受保护对象超过5时保留所有受保护对象，记录residual overflow并禁止继续生成。
- 不清理settler、正在前往/使用Baker或进食、玩家控制、被抓、inert、DespawnOnLoad、非当前场景、无安全同步身份或没有原生同步Pool的Beggar。
- 第4项不得全局patch所有Haglet/StateMachine/Scanner/Animator来跳帧；施工、战斗、拾取、转职、登船与分屏可见性必须保持原生。

## 本次候选：硬上限与遗留清理

### A. 中央营地所有权与补员

- `BeggarCamp.Awake`将原生`maxBeggars`设为0来抑制原生生成，但始终保留原生`slowUpdateRoutine`及完整生命周期；两个camp各自每5秒一次的局部扫描开销可忽略，且在协调器故障/失权/禁用后只需恢复参数即可自动恢复补员。
- 禁用原生生成前按camp实例保存原始`maxBeggars/spawnInterval`。Mod关闭时严格恢复捕获的原版profile；中央协调器初始化/runner异常、失权或无法继续工作时恢复当前mod既有`maxBeggars=5/spawnInterval=1`作为安全fallback，绝不让营地因协调器失败永久停产。正常world unload只清当前scene状态，不复用旧camp pointer/NetID。
- 每个active且仍注册于当前Kingdom的camp独立维护下一次补员时间；只有稳定归属数少于5、tutorial允许、场景/authority/池/RPC前置有效时，约6秒最多调用一次`camp.SpawnBeggar()`。
- 调用`SpawnBeggar`前再次核对camp仍是当前`Kingdom.BeggarCamps`成员；失权或scene替换后禁止用旧wrapper生成。
- 调用前后对`Kingdom.Beggars`做快照；只有恰好新增1个、当前场景active且原生注册成功的Beggar才提交到该camp所有权。失败不计数、不紧循环连发。
- 运行期已有`beggar.camp`优先作为首次归属；camp为空的旧存档非settler Beggar只在本次scene稳定后按最近camp确定性分配一次。去Baker或越过中线不重新分配；新spawn按调用来源camp绑定。
- 所有权映射用instanceID加当前native Pointer/NetID校验，池复用、OnDisable、换岛与重新生成不得继承旧归属。

### B. 一次性安全清理

- 只在`ApplyToScene`完整返回、Beggar ApplyData/Baker引用/动态header与client catch-up就绪后创建当前scene generation清理批次；不使用永久PlayerPrefs marker。
- 每个camp先计入所有受保护Beggar，再按距所属camp由近到远保留`max(0, 5-protectedCount)`个安全普通Beggar；其余进入一次性队列。
- 每帧最多回收1名；每次回收前重新验证authority、scene、所有权、保护条件、active、Pool与在线header，然后调用原生`Pool.Despawn(gameObject, true)`。
- 任何验证失败都跳过该对象，绝不因目标数量强删。清理结束输出一条摘要：before/assigned/protected/removed/residual/camps。
- 每次scene load可以重新审计；已符合上限时零写入且不重复刷日志，因此重复读档幂等。

### C. 后续独立切片（本次不实现）

- 一次性人口分类与约300个稳定帧的平均/P95/最大帧时诊断另开任务，不进入本候选。
- 原版`DroppableRegistrar`每3秒已做中央工具分配，但在角色远多于工具时仍以`JobAssigner`构建接近角色数平方的矩阵；“每个工具只寻找最近合格居民”的反向分配作为后续优先候选，必须保留一工具一人、原生资格/危险区/认领/目标粘性语义，且只替换工具分配路径，不全局替换`JobAssigner`。
- 该候选必须先用只计数/计时且完全放行原版的`DroppableRegistrar.ReassignClaimers`探针证明2.4调用真实命中；零命中即停止，禁止退而patch还服务农田、工作与登船的全局`JobAssigner.Compute`。
- 命中后仅在高人口且工具稀疏时，用补丁私有`JobAssigner`转置原生评分矩阵；评分仍唯一复用原生资格、距离、危险区、墙内外、目标粘性与状态切换成本。完整算出一对一结果后先统一释放变化目标，再由角色自身接口重新断言所有目标与claim；任何异常同轮回退原版。
- 通用AI分帧与离屏Animator LOD继续后移，不与本次清理一起发布。

## 第二阶段：低风险空闲角色LOD试点

- 仅在第一阶段实机日志证明清理后仍有明确帧时压力时启用；先选Beggar或Peasant一种，不同时覆盖Worker/Knight/Archer/Farmer。
- 优先使用角色既有Scanner随机相位并降低已证明热点的扫描频率；不重写通用Scanner缓存，不共享带claim/observer语义的结果。
- 动画仅允许可逆的离屏变换裁剪；必须同时正确处理P1/P2双摄像机，进入玩家控制、Baker、grab/inert、拾取、敌人或其他交互状态立即恢复。
- 如无法仅通过公开稳定入口判断“安全空闲”，第二阶段停止在诊断结果，不以private thunk或全局StateMachine跳帧硬做。

## 验证

1. worker核对2.1逻辑与2.4签名并仅改IL2CPP；独立reviewer先审plan再审diff。
2. `dotnet build -c Debug --no-restore -p:BepInExPluginsPath=`为0 warning/0 error；`git diff --check`与EXharness validator通过。
3. 游戏退出后备份共享存档，再只部署独立测试副本；构建/部署DLL SHA一致，不自动启动游戏。
4. 当前异常岛首次载入：158 Beggar逐帧安全下降，目标通常约10但受保护者可残留；无单帧大卡、无重复生成、无unknown pool/RPC/NullReference。
5. 两营地各自缺员时约6秒补1且达到5后停止；Baker吸引、面包耗尽、换岛、读档、死亡换君主、联机均不突破硬cap。
6. 确认其他职业数量未被清理，金币拾取、Baker、教程、忍者帐篷伏击槽与现有功能无回归；帧时对比另行实施。

## 退出条件

- 硬上限与清理静态reviewer APPROVED、构建、备份与独立副本部署后保持`doing/review_approved`等待实机。
- 工具反向分配、诊断与单职业LOD必须另开任务并独立review；不得因计划存在就宣称已经实现。
