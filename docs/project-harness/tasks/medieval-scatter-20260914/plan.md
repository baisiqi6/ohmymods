# 中世纪随从专属散射与淡金色额外箭

用户已同意：仅中世纪style0骑士弓箭手随从对敌散射、打猎单发，总箭数1..3含原箭；色差选择淡金色且只染额外箭。按实际射击队籍与_shootingTarget检查，不按世界/昼夜猜测。开关/配置key沿用，默认关闭，滑块上限3。射速与希腊火矢/骑士独立档/日志修复保留。

OMP deepseek/deepseek-v4-flash thinking=max两个隔离worker实施资格和色彩helper，内置archer_reviewer只读审查。Operator做现有散射/OnEnable/Tick/UI接线与真实模块整合回归。Tint首轮恢复回执/池复用/网络解析边界问题已由原OMP修订，44项目回归与最终review通过，闭游戏已安装C6B71AA6；实机待验。

颜色只SpriteRenderer实例color，不新增贴图/材质实例/renderer/粒子，保持alpha。自有回执上限128，池复用前恢复，写失败转恢复回执，CAS保护外部颜色，world/layer变化清理。softsim沿原RPC，6bytes自有payload由Arrow.ReceiveInitialise长入口Prefix严格识别接管；未知payload原样，完美射击与火矢语义保持；双端需新版。原生336B/same_slots1及严格1byte分支已核。

验收：policy+color生命周期+协议+既有scatter/rate与实际组装回归，实际interop/native/API/build/独立review。游戏关闭才备份安装本机候选，不改存档或手工覆盖配置，不提交发布。实际淡金观感/敌人和兔鹿射击/复用主箭/两机待实测，状态保持doing。
