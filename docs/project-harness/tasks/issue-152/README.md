# 地图资源图标岛内布局修复（Issue #152）

范围：原生岛详情与总览的资源图标摆放。未探索的额外坐骑岛 physical11 不显示资源是正常状态，本项保持该语义。

## 原因与修复点
冻结实际安装 DLL 1c00e9b2…及 release/v9.5.13 基线43cf88b；159个相关 map 方法语义一致。旧详情将 Land Image/Outline 当障碍并扩大岛 root 面，导致资源排到左下岛外；旧总览 RegionRects 明确选岛外空白。另有总览 prefab 图标遗漏岛图→paper自然倍率的问题，造成相对岛图过大与容量误判。

在既有 Rebuild/PlanOverviewIsland producer 修改：按当前请求身份匹配详情原生 Steed/Hermit/Statue 槽位，保留其位置及原始占地；新增条目限定到所属岛真实绘制 mesh 内，底图/接管槽不再自挡。总览以相同 art→paper 变换缩放规划与最终 clone。形状读取采用实际 Image Simple+useSpriteMesh 的 active sprite、pixel-adjusted size、bounds/pivot 与 transform 链，不读纹理、不加扫描或周期纠正。无法证明几何或容量不足时整组保留 native，并记录原因；无岛外放置兜底。

删除原生岛外 RegionRects/最近岛 planner 和详情额外边距。扩展岛保留自己的顶面 mask、身份、探索锁与原有生命周期；仅将其被接管动态槽的占位 Image 排除自挡。原生地理全局 fit 保持。

## 验证
- 旧 producer 同输入：详情/总览最终 clone 的岛内约束各失败，建立复现；旧测试原182断言仍通过，说明旧测试覆盖了错误布局契约。
- 当前纯规划827/0；真实 Rebuild 接线 stub 236/0；岸线101/0。覆盖原生槽身份与位置、extras完整岛内、总览四资源与自然倍率、renderer逐顶点一致、容量不足/未知shape还原、换岛/重建/开关地图/viewport/探索锁及扩展16项。
- 非居中pivot、bounds区别于rect/ppu、overrideSprite、preserveAspect模式等接口反例，旧v2红/新v3绿；合成拓扑反例不声称是玩家已观察根因。
- SDK8.0.425/net6.0，实际ARM64 161引用、34 PNG完整构建0W0E；产物 SHA256 97a92a31de429fa4d8cd666372882b858140607db856741364e896512ac5809c。
- Cecil审计：6803个baseline方法保持，14个地图授权变化、25新增，34PNG与Harmony目标保持；无其他玩法改动。
- 静态游戏资源核对：十原生详情模板均Simple/useSpriteMesh1，最大51顶点/56三角，在支持预算内；运行时bounds/pixel-adjusted结果由实际getter读取，未声称测过游戏实值。
- 独立review：APPROVE；固定6hash核对通过，独立接线236/0、规划827/0、关键字面反例12/0。

## 边界与账目
代码审查阶段未操作游戏、未改存档/配置。PR154随后已合并并安装，见[安装记录](installation.md)；实际画面、Windows及联机未验证。代码交付和玩家实测分别记录。
本项为单模块详情/总览协同修复，建议下一发布账目patch +3，作为一个完整条目去重；内部v1–v3返修不追加计数。本PR不发布/tag或改当前版本号，安装候选仍须由累计版本负责人核对。
私有原始截图、提取数据、完整日志、provider session、构建与probe保留在 .local/tasks/map-icons-inside-islands-20261005/，不进入公开仓库。
