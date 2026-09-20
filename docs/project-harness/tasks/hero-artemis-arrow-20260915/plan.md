2026-09-15 英雄神器金箭实现中：用户要求英雄射出的箭使用神器弓金箭；纯外观，覆盖主箭、额外散射箭及打猎主箭，沿现有英雄开关/单机范围，不修改原生Artemis追踪/伤害/碰撞。已核真实资源Sprite9867 artemis_bow_arrow 30x5/PPU32/pivot0.5，原生Renderer79915/材质42 Highlight金色；OMP Flash max隔离worker实现有界生成scope与池归还，Operator负责资源/集成/验证。

验收：所有英雄箭换图、非英雄不变；旧池owner不得误判；嵌套/overflow/finalizer恢复；禁用/换世界/池复用CAS归还；native材料、碰撞、弹道和伤害不改；实际interop编译/独立review/受影响回归。运行游戏不换DLL，不改存档，不提交发布。
