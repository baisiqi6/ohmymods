# 英雄实机反馈修复候选验收

2026-09-15 英雄实机反馈修复候选已闭游戏备份安装正确E盘：50d04ef3 / build=8.0.0-hero-live-fixes-20260915。用户确认cfe暂停修复已好，真实日志有多次pause/resume且无重建。此轮旗帜与建筑同sortingOrder，z-.001不变，避免晚画旗受透明FX深度干扰；实际PowerFire ZWrite1无discard为候选遮挡源，未把候选当实机定因。英雄本人walk/run基准×1.5、射程/私有弹道克隆×2；Prepare用既有cadence Last prefix内真实临时prep值，进入前锁窗；perfect跳过原生Shoot时由真实放箭事件播放0.18秒自有释放，首帧强制可见后推进，移动/新Prepare/未知动作让位。按用户最新纠正改为斜上举弓，动作层10/18/22度连贯抬起与收势；仅23..29七槽改手臂/金弓，头脚、其他槽、双围巾和三张其他PNG保持。夜间有原生goto8守位目标证据才向外，8→1保留守位；射击/移动交还原生朝向，未知life或恢复失败保留责任，Clear/Forget等待清账再删life。邻近跳动尚未定因，新增真实setup触发最多2邻居×12批只读位置/缩放/外形诊断，每上下文3次会话，不逐帧扫描全场。构建与实际interop0警告0错误，独立复核通过；103视觉/90姿势/106移速/155守位诊断/96runtime/146战斗/109购买/54商店/31塔位检查通过。DLL对cfe审计2847方法保持/23授权修改/103新增/7替换移除，Harmony类型集保持，准备窗口实际接线核验。存档配置全部hash保持；未启动游戏/提交/公开发布，公开8.0.0不变。新遮挡效果、斜举弓/释放、夜守向外/射程速度仍待实机，普通人物跳动待本轮日志；跨岛运输仍待。

## 证据分层

1. 已实机：上一轮cfe版本启动、购买与连续Esc暂停保留，用户明确确认；日志在operator目录stability-live.log。
2. 本轮代码回归与资源检查：receipts内各测试、actualinterop、DLL方法/资源/接线审计；guard和movement三轮独立复核，visual最终补首帧锁窗与低帧率释放，全部通过。
3. 本轮未实机：透明区域遮挡是否完全消失、斜举弓和释放观感、夜晚向外、速度与实际命中距离；邻近角色跳动尚未定位，不能称已修复。

## 实施依据

PowerSprite2基线材质正常alpha裁切；4种FLASH+EMISSIVE变体按常量裁切，实际ThunderstormFlash使用。PowerFire8种像素程序无discard且深度写入开启；旗原sortingOrder+1导致跨档延后，因此恢复同档+局部z。没有改全局共享材质/玩家特效/模板。DXBC原生审计工具shader-audit.py及operator/shaders保留。

旧Prepare按整段2.1667s希腊投掷clip相位取4帧，短准备窗常只落首帧；现在使用构造原生Wait时的真实准备窗，保留原生Animator相位推进。原生perfect末发可能由Prepare直接Stand，事件释放只填自有表现，游戏伤害/箭速/射击行为保持原生路径。

用户早先明确允许本地脚本整理游戏素材，本轮保留已确认设计，用prepare_bow.py只改动作像素；用户再次纠正水平应改斜上，最后产物已按此更新。修改槽、头脚一致、二值alpha与PNG哈希见artifacts/hero-archer/20260915-bow-release/receipt.json，preview为精灵帧预览而非游戏录像。

## 运行边界

既有原生移动指令已捕获的速度会持续到下次SetGoal；切换/关闭效果或转为DL随从时也存在这个过渡，不宣称帧级即时归零或DL过渡绝不短暂叠加。实际2.4编队若设置overrideMoveSpeed，该原生覆盖不由walk/run字段提升。地图、机关、原生存档与其他职业缩放本轮未改。

## 本机安装

目标：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll

备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-hero-live-fixes-20260915-211100-127.bak

校验24份用户文件前后hash一致。安装时游戏关闭，未启动游戏。
