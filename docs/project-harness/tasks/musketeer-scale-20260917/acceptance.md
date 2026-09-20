# 火枪手0.9候选验收

2026-09-17 用户要求火枪手当前外观缩为0.9：共用AppearanceScale，仅自有sprite child绝对(.9,.9,1)、枪口localXY同系数，脚点/朝向保持；不改actor/物理/速度/动画时钟/伤害/射程/节奏/全局缩放/商店工具弹丸大小/持久化，8PNG与上一combat-reliability保持。候选63ea50ad / build=9.0.0-musketeer-scale-20260917；79runtime回归、actual2.4接口/完整build0W0E、范围审计和独立复核通过。已闭游戏备份安装既定E独立副本，28份原生档/附加档/配置hash保持，未启动游戏。本次19:34启动日志23名火枪loaded exact/bound23且saved23，marker注册正常、未见伤害故障日志，但不能当逐弹/高密度/帧耗时验收。Player另有城堡盾牌店InvalidNetID11次、2名Archer穿地被引擎搬回、英雄快速Stand/Walk，根因与具体职业未确认；仅记录，不把0.9当修复。未commit/push/publish，公开9.0.0不变，0.9观感/枪口实机待验收，保持doing。
