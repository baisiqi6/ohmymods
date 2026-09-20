# 原创角色动作机制研究

用户要求先了解原版如何拆动作和形成连续表现，再沉淀经验文档。范围为当前2.4实际资源与现mod差异、代表性职业动作矩阵、原创制作流程和闪烁诊断计划。禁止本轮试改DLL或存档。

2026-09-15 英雄动画研究：用户实测FAE6移动闪烁，要求先学习原版动作拆分/衔接并沉淀文档。本轮只读研究，不改生产代码/DLL/配置/存档、不操作游戏。实际2.4引擎Unity6000.0.61f1（非旧Mono2022.3）；提取8控制器及15clip离散PPtr时间轴164keys，补AnyState/default/layers/raw，3run各2脚步事件；Greek准备26keys仅6distinct。原版base archer7状态、Speed门1/.005、Idleness与Prepare可被移动退出；原run保留1–2px离地，旧草稿bbox贴底30抹去高度差。45首见帧/16selected12actors只能证实推进/当选记录，不能定闪烁频率或根因。已写game-logic-map/character-animation-production.md和可复现脚本/对照图，后续按原生状态与时间轴、完整生命周期和像素动作规格重做原型；英雄整体仍doing，当前闪烁未修复。
