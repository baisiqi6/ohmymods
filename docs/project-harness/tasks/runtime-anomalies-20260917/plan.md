# 英雄动作抖动 / 乞丐穿地 / 弩手缩放定位修复

用户明确授权定位并先修可定位问题。基线069527B6529AD86C9D29F472186F0B2EE037A91E62956C6E927B34A5CFBEB3C2，保留最终英雄中央握把弦上横持、火枪HUD/补货、商店和全部其他修订。原8张PNG本轮都不改。

冻结日志见tasks/musketeer-save-check-20260917：旧build musketeer-20260916，13:44退出，当前并无新版实机。已证实英雄actor -93020在8.556秒102次Walk/Run且nt=0；6名不同Beggar被World.HandleFallenThroughObject拉回（参考条件y<-100）；1次Crossbowman scale drift=1/7。DropItem三次与Knight load-mismatch已在后续候选改过，本轮核对覆盖而非重复改。

北京时间工作日15:35后派内置workers。Hero worker限定运动/动画源头，不靠掩盖日志或任意跳帧；population worker先查来源与原生边界；crossbow worker查缩放ownership/池复用/换皮，必须证据足够才改，否则输出事实、假设与有界后续诊断方案。只允许各slice文件，公共集成root统一。

不写原生档/附加档/配置、不启动/自动操控游戏、不运行时替换DLL、不提交发布。不得全局改Animator/Physics或新增短getter detour，不新增每帧全场扫描。若需要native新hook先证明原生方法唯一且不短；优先既有长边界。兼顾关闭/死亡/池复用/换world/暂停/联机。确认原因、最小修订、回归、actual2.4构建及独立复核后按已有授权本机候选交付。实际游戏效果仍待用户验证。
