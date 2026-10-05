# Issue #144 公共 UI 实现与验证

本批把 PC 调色板、样式与纯绘制控件放在同一份 ModPanelStyles/ModPanelControls 源码中。PC 继续使用原有配置、原生 Button/Slider、字号与宽卡几何；Android 四个功能页和人口页统一调用 MobileModPanel.Toggle/Step/Info。浮球保留原小尺寸、默认折叠与贴边拖动；展开页改为固定标题/导航和可滚内容，外框、触摸过滤与原生透明命中面均从 FloatLayout 读取。

唯一 PanelGesture 持有 ScrollY 和 sticky Moved，任意方向超过阈值、拖回起点或抬手落在面板外均不调用设置。普通关页只清本面板手势；已持有触摸按原 Ended 生命周期释放。移除了各页固定行坐标、页面高度表与旧重复样式工厂。未增加配置镜像、接口假面、通用 feature registry、字体扫描、重建或重试。

实际 Android GUI 中，整对象 RectOffset setter、GUIStyleState.background 和旧 CalcSize/DrawTexture 路径缺失。修正在产生处：写各自 style 的 RectOffset 四字段；Android 从 label 派生无游戏 box 装饰的文字样式；共享固色 API 用真实 native Label 绘制 1×1 色块并在生产处明确裁剪；前景仍走 native Group。绘制与命中共用 IntersectRect，坐标使用显式 origin/clip；尺寸测量用真实 CalcSizeWithConstraints(Vector2.zero)。只创建实际使用的纹理，PC 专用 slider/background/宽卡路径在 Android 编译排除。旧 native 失败和不成立的 clip 假设均保留采证，没有用兜底层掩盖。

最终 Main 5fc712f4/151040B：实际 Android SDK10 0W0E、strict 623/0、43 pins/0skip。PC SDK8 Release 419d2aa3 0W0E；6796 共有方法中6791保持、5个具名纯UI变化，增加1个纯数学helper与3个纯UI类型，34资源逐字保持。源码独审通过，详见私有 source review packet。PC未运行/部署，Mono未构建。

最终私有 APK f9702e2b/1,908,870,179B：唯一Main5fc、没有诊断插件或采证metadata；22313原游戏文件逐字保持，变化仅原loader入口和重新签名Manifest，全部CRC与apksigner核验通过。模拟器同签名更新不clearData，安装首启前原配置和原生档逐字保持，installed base.apk与候选SHA一致；自然原岛冷加载25旧hook shapes、15原值、无ERROR。完整同bits UI协议和最终包抽验分开记录。

已观察中文/深金风格、固定导航、滚动裁剪和末卡可达、各方向拖动不误触、浮球拖边与折叠、日历开关恢复、覆盖原生菜单的点击隔离及外侧可用、失焦持按取消与新tap恢复。结尾设置恢复、游戏停止；named cfg/native快照相等仅证明各具名时点。skin四组数值的两点一致不证明全时刻无别名或所有属性不变。操作脚本曾有一处错误坐标和一处错误点击预期，原失败快照与即时UI恢复记录保留；不当作游戏崩溃或滑动误触。

手机/平板、多指同时面板外操作、全部DPI/旋转/文字尺度和联机仍待验；本批不替代既有玩法验收。以后功能直接追加公共控件，按原功能依赖/风险分组继续；Dense植被批保留前期调查。全量移植余量仍按8–12批、80–200工程小时估算，设备/联机日历另列；完成下一实际功能批后再收窄。正常PR合并与canonical代码收尾尚未发生时，不宣称代码任务done。
