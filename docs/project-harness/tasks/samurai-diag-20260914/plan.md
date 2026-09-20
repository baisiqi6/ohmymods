# 武士冲刺被动诊断

用户授权在冲刺和效果产生时输出日志，要求被动、无主动扫描。实际采用现有managed Begin/End/Emit/LateUpdate调用点，不增加Harmony/native入口、逐帧全场枚举或新的driver。功能只诊断，不修改冲刺伤害/速度/无敌/冷却/残影亮度/寿命，也未修骑士类型读档重新分配问题。

OMP18.1.19 deepseek/deepseek-v4-flash thinking=max（native model event fallback=false，session01a09eb2-ed3d-7408-ab7c-d41046100438）实现独立helper，现有cs零修改；Operator统一接入与渲染测试。内置samurai_visual_audit独立审查通过，修复了静止残影先到期后End漏tail-cleared的诊断缺口。
