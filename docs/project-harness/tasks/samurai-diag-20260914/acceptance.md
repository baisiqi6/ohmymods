# 武士冲刺被动诊断

用户授权在冲刺和效果产生时输出日志，要求被动、无主动扫描。实际采用现有managed Begin/End/Emit/LateUpdate调用点，不增加Harmony/native入口、逐帧全场枚举或新的driver。功能只诊断，不修改冲刺伤害/速度/无敌/冷却/残影亮度/寿命，也未修骑士类型读档重新分配问题。

OMP18.1.19 deepseek/deepseek-v4-flash thinking=max（native model event fallback=false，session01a09eb2-ed3d-7408-ab7c-d41046100438）实现独立helper，现有cs零修改；Operator统一接入与渲染测试。内置samurai_visual_audit独立审查通过，修复了静止残影先到期后End漏tail-cleared的诊断缺口。

2026-09-14本机9F1F61A0/build7.6.5-samurai-diag-20260914：武士冲刺被动诊断[SamuraiDiag]，armed/start/trail/visual-ready/skipped/first-update/stop/tail-cleared/end，突发8次后每游戏秒补1次完整链、每链最多12行；无新native钩子或扫描。123冲刺+15视觉/日志+9诊断测试、0W0E/2374API/1941无关方法保持/独立review通过；闭游戏安装，save/config保持，未启动/未发布。保留希腊火矢0.25所有更改，真实冲刺日志待玩家游玩。见tasks/samurai-diag-20260914/acceptance.md。

日志：
- armed：插件诊断已加载，不能当成发生冲刺。
- start：进入既有冲刺Begin；编号dash关联全链，mode=attack/return，knight为本次运行中的GO ID，仅供日志关联；time/x是该时刻快照。
- trail-state / effects-restored：present/active/enabled/emitting/points/time/width/sorting及elapsed，均是读取状态。
- visual-ready：ghostSlots=3、enabledGhosts、成功启用后的ghostSamples、whiteEnabled、source sprite/material/shader/sorting；不宣称屏幕可见。
- visual-skipped：invalid-owner/paused/retry-backoff/具体source组件或sprite/material/overlay缺失/exception。
- visual-first-update：现有LateUpdate确实到达，且读出当刻trailPoints等；只一行。
- visual-stop/visual-cleared/tail-cleared/end：结束、提前清理原因和尾迹结束，含elapsed；被挡住原地超过0.2s后结束也恰好记一次tail-cleared。

性能：先准入再读Unity与拼字符串；全进程tokenbucket容量8，每游戏秒补1，下一条start带suppressedSinceLast；每Trace最多12行。无长期每骑士字典、Trace不持Unity对象。现有三ghost循环仅增加一个成功样本计数和首帧/尾结束事件判断。写日志有少量开销，不宣称零开销。

验证：123运动回归（视觉接口stub）、15实际视觉helper+诊断helper直连回归（native边界stub）、9logger限频/异常/时间回退测试通过；15项包含现有12个视觉行为回归，冻结姿势、材料、方向、淡出、清理均保留。最终构建0W0E，2374全模块Unity可达API无unstripping失败，1941无关方法IL一致；仅三诊断相关类与Plugin.Init(build/armed)变更。新增日志读取参数均在准入后且try/catch内。worker建议不作为自动验收证据；Operator已独立运行上述测试。

最终本机hash见install-receipt.json；5CED0D25旧DLL已备份。游戏关闭时安装，save/config哈希保持。未启动游戏，未取得新[SamuraiDiag]实战事件，不把编译/stub日志宣称实机日志。未提交、推送或发布。根Mono未改。下一次正常游戏观察日志即可，不需要诊断专用开关。
