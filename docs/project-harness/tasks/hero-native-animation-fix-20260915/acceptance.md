# 验收与交付

2026-09-15 本机 EFD44621 / 7.6.5-hero-native-animation-20260915：用户澄清英雄持续可见但姿势轮廓跳变。重制31帧位（18张不同图，6走/6跑），固定头身像素与脚锚、保留腾空高度；仅跟随原生Animator current state/normalizedTime，转场不预取next，LateUpdate单帧去重；未知/停用/非法采样归还原生并保留自有状态。双红飘带仍为独立动态网格，肩锚随躯干起伏；整体0.9及英雄置前保留。73姿势/57真实接线stub/65runtime/68cloth/33cloth-view/146combat通过，真实2.4及普通构建0W0E，2622旧方法不变/13授权改动/5私有旧时钟方法移除，独立review通过。闭游戏备份FAE6后安装，save/config不变，未启动/提交/发布；实机姿势衔接、特殊动作回退、跨世界与像素观感待验，英雄整体仍doing、在线仍关闭。

## 已验证

- 73 native-pose检查：循环/单次相位、31槽布局、转场current-only、非有限采样回退、肩部起伏。
- 57 production-visuals + Unity/game stub检查：真实视觉控制流、开关清理、未知状态归还和恢复、同一对象复用、异常所有权（包括setter部分写入后抛错）、单帧去重、日志上限。stub不是Unity实机。
- 65 runtime、68 cloth logic、33 cloth view、146 combat回归通过。
- Pillow校验384×128、48×32、31有效槽、末格空、二值alpha、18不同图、6走/6跑、固定pivot、腾空高度、素材和生产嵌入PNG完全一致。
- 普通及真实2.4引用完整构建0警告0错误；注释清理后最终DLL再次真实构建并重跑IL审计。2622旧方法未变，13授权集成修改，仅5个明确私有旧姿势时钟helper移除；两张嵌入资源保留。
- 独立review无阻塞。最终安装hash：EFD44621BD86166979DC620D19BA7D91BAD3D960735405D64F517AC751D56C0E。备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-hero-native-animation-20260915-131506-700.bak。
- 安装前两次检查游戏关闭；安装后DLL哈希核验、存档与配置哈希一致。没有启动游戏、提交或发布。

## 待实机

走/跑/停/转向/准备/射击过渡、同时两英雄、不同世界/性别、受击转职/石化/死亡/Spawn未知动作回退、池复用、开关与切岛、布料遮挡、0.9非整数缩放像素观感。在线英雄仍关闭。

31槽含重复保持帧，不是31张不同图片。当前按native状态相位均匀采样自有帧，未逐一匹配所有世界稀疏PPtr时间轴。事件日志仅有界记录native姿态/状态/回退变化，不据此声称已经消除全部闪烁。

研究阶段FAE6为历史基线；新候选的生产权威是HeroArcherNativePose，旧HeroArcherAnimation仅保留旧测试。组合预览使用真实cloth物理核心与合成风/速度输入，不是游戏录像。
