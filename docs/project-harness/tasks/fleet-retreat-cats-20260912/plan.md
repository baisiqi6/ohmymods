# 希腊舰队配队、武士防御后退与农场猫
用户2026-09-11要求评估并限制其他风格骑士上希腊小船，召集按可用希腊小队和船数较小值；检查上轮武士残影；只加快防御性后退至原来三倍；猫每农场四只。用户明确速度仅武士style2。

舰队：FleetBoat不是主Boat。现有PlayerFormation可用同侧船候选，添加Greek style3一对一预留，max4，真实_numSquads必须1；已分配本船队优先，已有nonGreek乘员船不纳入新召集。短期NativeCandidate probe避免CanJoin自阻塞；评分拒绝非Greek与错配，保留同船已上船旧乘员原分数，不造单位/船。仅PlayerFormation阻止预留Greek被陆队抢走。预留持有至原生登船/formation结束；未登船60s超时，新主船/外部任务接管则退役；评分只标记取消，既有0.5scoordinator验证仍属自有世界/formation/membership后原生Unregister自有船，回城由native处理。主Boat和workers一般评分、随从stowaway原生不改。队伍/船数是当前可用匹配，不是永久全世界总量；真实注册/登船异常需运行时验收。

武士：既有float SetGoal prefix ref speed，仅实际GoToWall+isRetreating+style2+输入等于正finite _retreatSpeed+auth/live/free时本次speed*=3，不写基值/普通run/dash/return，once canary用于实战命中证据。
猫：现目标6改4，希腊单机/分屏原有范围；既有加载延迟协程只从当前归属本农舍、驯化、active、KEM_FarmCat标记、未抓/未跟玩家的多余猫里回收；非mod猫保留。Pool.DespawnOrDestroy正常返回后若仍active立即失活，避免非池Destroy帧尾延期引起超额删除。存档含24个KEM_FarmCat名字，读档可识别；不直接写存档。

残影：现DLL与源已含45/25/10、.2s3ghost及body，接线/scout无明确缺陷。现保留的是暂停启动日志，未记录SamuraiVisuals ready/失败，不能推定成功可见或失效；保持参数，下次真实战斗观测。新client残影信号上一轮未做，本轮不借机扩网络特效。

ZCode优先worker cats，operator接线退速和舰队；独立tests/review。只E测试副本，保持此前未提交修复，不commit/push/public ZIP/config/save直接写。真实登船、回城、武士退速/残影和旧猫缩减需用户游戏验收。
