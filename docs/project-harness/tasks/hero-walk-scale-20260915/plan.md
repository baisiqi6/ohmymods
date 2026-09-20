# 英雄滑行表现与0.9显示缩放

用户实测移动时人物看似不动，要求整体缩小至0.9。代码确证原MotionOf以Mover._movingToGoal为前置门，而原生SetSpeed将该标记设false但仍然设置非零移动速度；存在移动中误判Idle路径。修正移动判据，补被动有界帧推进证据，ModPanel既有Update直接调用Sync（frameCount去重），但不宣称现有LateUpdate已证实未回调。

缩放仅自有body/cloth的xy及肩部偏移，绝对0.9且保留flip/z，原生actor/碰撞/移动/伤害/金箭不变。保留758B5990英雄优先、全部旧修复。现有walk四帧已查看不同，暂不改素材；行为判据修复后再实测步态。受击等特殊动作不属于本轮，也不称完成。

北京时间工作日上午ZCode0.16.5 GLM5.3 max配置，隔离worker Read/Edit/Write；Operator负责测试/interop/独立review/安装（须游戏关闭）。不动存档配置、不启动/关闭游戏、不提交发布。
