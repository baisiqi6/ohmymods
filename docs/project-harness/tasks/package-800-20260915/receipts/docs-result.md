# V8.0.0 玩家文档 worker 结果

状态：完成。仅修改获授权的五份玩家文档；未改源码、打包脚本、配置、存档或独立历史版本说明。未提交、推送、发布、安装或启动游戏。

文件清单（仓库 C:/Users/ADMIN/projects/ohmymods）

- release/MOD_V8.0.0版本更新说明.txt（新建）
- release/MOD_USER_GUIDE_ZH.txt（重写当前指南）
- release/MOD_UPDATE_AND_FIX_LOG_ZH.txt（重写当前汇总与验证边界）
- release/MOD_CAPABILITIES_AND_ROADMAP_ZH.txt（重写当前能力与待验方向）
- release-notes-il2cpp.md（与新V8说明正文一致）

主要校正

- 统一8.0.0/build8.0.0、本地完整包/尚未公开发布，不使用Latest或全功能实测口径。
- 删除当前四份聚合文档中的过时历史叠加，独立MOD_V*历史说明不改。
- 希腊style3骑士火焰窗口随从火矢：半径0.25/额外1点、原生直击保留/直接目标排除/同轮去重/无长期DOT；普通Greed/Troll受击宽0.375、高0.65625，直径0.5约1.33个受击宽度，并非固定目标数。
- 中世纪style0随从对敌散射1–3默认3；额外箭淡金，打猎不触发；普通射速独立1–2默认1.5。
- 骑士独立附加档、加载seed/补少与精确岛快照，Squire排除；原版重存可失配需重建，不承诺永久无损身份或匿名跨岛逐人保持。
- Hermes默认累计30%=每10新转换3顶（从零4/7/10），44款按序轮换，已有对象不重抽；可见原生面具/原帽、周年帽及有效新增头饰免新主动锁定/反制，仍受AOE且已起手可完成，无数值加成。原生面具保护随总开关；新增头饰保护以有效显示为准。原帽消失未确认根因。
- 英雄默认关、全部世界单机每侧最多1名现有archer、1.5/max与1.25/3箭/.25+1、打猎单发、独立火爆发开关、31槽原生动作/.9/置前可盖场景/金弓金箭/双尾长围巾、未知特殊动作原生回退、在线含host整项关闭。
- 核当前ModConfig/ModPanel：8项补货全off/15/1–200，便捷4项全off，人口120/4、HUD默认值、常用滑块范围；银行与既有缩放Greek-only，英雄自有.9单列；Critter原版/Greek鹿3倍/4猫1.25/在线猫跳过。
- 明确hero/围巾最终实机观感待验、急转贴地瞬跳/半透明接缝仍待、网络/存读/无限体力技能等未实测不冒充完成，旧盾牌/狮鹫/原帽/全部闪退未称解决。
- 包不带个人cfg/存档/sidecar；备份原生global-v35、现有BepInEx/config及ModSave整体，指出两类sidecar路径，退出游戏后完整解压，旧ImpactEnabled=true升级采用新规则。

核验依据

已读AGENTS.md；核对il2cpp/ModConfig.cs、ModPanel.cs、HeroArcherCombat.cs、HeroArcherRuntime.cs、HeroArcherNetwork.cs、PatchArcher_GreekImpact.cs、FriendlyTrollDisguise.cs、PatchDivine_HermesHeadwear.cs、HermesHeadwearCycle.cs、KnightIdentityRuntime.cs/LoadSeed.cs相关路径、GreekScaleScope.cs、PatchWorld_FarmCats.cs及对应历史任务验收/Greek影响说明。旧版V4.5、V6、V7.5/V7.6.5仅用于确定保留内容，过期参数按当前源码替换。

检查

- git diff --check 对四个已跟踪改写文件通过。
- 五份均含8.0.0、本地未发布、0.375参照、英雄31槽、围巾、附加档边界。
- 所有五份使用UTF-8 BOM、CRLF。
- 当前五份无“全局散射/火效纯视觉/1–5箭/头饰等概率”等矛盾表述；“独立随机”仅用于说明旧行为已改。
- 未跑代码测试：仅文档变更，版本构建与整包审计由Operator负责。