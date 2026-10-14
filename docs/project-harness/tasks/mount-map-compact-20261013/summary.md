# 坐骑双岛总览紧凑下排

Issue #200 的总览比例返修，依赖 PR #202 的双岛与详情分页，开发基线为该 PR 的 c6f7524d。用户选择原群岛尽量放大、两座坐骑岛靠拢居中下排。

现象来自旧大岛的固定 78 UI 底部预留和左右半区居中：本机对应日志里原群岛统一倍率仅约0.507，两座岸线各49.7×18.3 UI，却占74.1 UI高的带。当前修改从完整原生图形和扩展岛状态标记推导预留，而非用新的固定魔数。

`Patch_MountIslandCompactLayout.cs` 是无 Unity 依赖的纯规划器。原十岛和确证直属海面装饰共用一个不大于1的统一缩放与平移；两座扩展岸线保留同一普通岛基准和比例，按含固定船标的整个包围盒组成居中一组。船标的原尺寸和相对根位置保持。顶部按钮带从顶部扣除；整体不扩纸、不强制凑77%或100%。

完整静态图形180 UI高、当前可用308×164.6 UI、98×36.1普通岛参考及16×16固定船标的示例，得到倍率约0.6594，较0.507增加约30%。0.768仅是岸线口径理论值；运行时以真实测量为准，不把示例当玩家实测。

接入只在现有稳定视口提交周期运行，稳定帧没有新增扫描。严格收集包含inactive/disabled潜在图形；未知状态不作空。新接管的直属装饰要求exact父级、有限正scale、无不透明Animator/Animation；不满足native接管条件时保留完整旧布局。船/状态未知或写后读回不一致时整笔归还。海面装饰允许当前原生(.5,0)底部pivot，按原锚点共同仿射，不修改pivot。

提交后实测全部native根的完整图形、岸线/outline/点击元素、实际根中心和绝对state包围盒，核包含、间距及原生相对关系；不以计划中心替代实际船标位置。使用既有snapshot/owner/teardown责任，失败立即归还。保持详情分页、资源请求、点击导航、旅行编号、授予、世界大小、森林和存档行为。

验证：生产pure源码直接编译的127项检查通过，含本机窗口、完整图形、顶部按钮方向、非零原点、state不缩放/包围盒间距、窄窗口、native宽度主导、非有限/溢出输入；旧地图286项通过。完整公共IL2CPP源码.NET8（Mac 2.4引用的禁部署包装器）、完整当前累计候选.NET6/.NET8均0W0E。公共源码包装器的首次生成PluginInfo遗漏保留失败记录；按公共csproj元数据补生成文件后通过，没有修改生产解决该包装器错误。

独立源码审查通过；初审暴露的底部pivot误拒和planned-center掩盖actual-state误差已修复并复审。累计二进制只读metadata复核：38资源与已装b8f累计逐SHA相同；仅overview commit的既有语义body改变，新增managed方法归两源，旧generated closure序号变化单独归一保留原始记录，无地图外body变化。保留PR204修复、HUD/字体/性能、盾卫、两岛、宫廷来源；不安装旧简化DLL替代累计版本。

本轮未启动游戏、未写正常环境或共享进度。源码/host验证不等于Mac原生刷新后视觉、归还及玩家验收；Windows和Android原生未验。PR #202仍Draft/Open，Issue #200保持开放，不因本次代码候选通过宣称双岛全部玩法已验。此为既有候选条目内部返修，无新tag/release/版本账目重复计分。


## 2026-10-14 — 右岛宽湾弯月形（源码候选）

用户确认保留当前总览比例/两岛尺寸位置，仅让右岛形状与左岛区分。physical11继续使用原PNG和BuildPrep224，逐RGBA/alpha/outline/placement mask保持；physical13新增Crescent资源，BuildPrep222得到226×82后四边pad1，最终同为228×84，不拉伸。两座岛只改变纸地图美术，不改变世界长度、坐骑分组、授予或旧档。

Art服务以两份固定缓存按physical11/13取shore/outline/prep，共用原有owner租约与无未归还责任的销毁关口。B资源/准备/建Sprite失败即失败，不替换成A；冷开B详情不必先载A。新B顶面mask独立手工轮廓、排除西凹竖崖、内缩2像素，5652可放置像素；包含少量朝上顶沿的浅岸高亮，不声称所有mask都是同一灰色。锚点为不可变profile，仅换前8主锚，后10主锚/全部secondary含NaN保持，容量18和既有分页不变；未采用probe临时修改全局表的方法。

总览和详情都按exactphysical取pair/mask/profile。详情分页上下文保存本岛数据，从该land自身获取art/outline，验证canonical token、live tuple、捕获身份、actual applied pair和几何。审查同时发现旧OnLandUpdated忽略EnsureDetailShape失败：本次在产生处尊重false并撤本view holder/pager，后续普通数据回调才重建；删掉原global detail-excluded事实缓存，避免失败后跨岛混拼。无新定时扫描、重试循环或下游补偿。

验证分层：新helper94、真实MapMountIcons窄host回归161、缺B资源3、原shore101、纯地图286、紧凑规划127检查通过；完整公共IL2CPP源码用当前Mac 2.4 interop的.NET8禁部署包装器编译0W0E。host实际克隆的Prev/Next能换页，坏pair阻止新页且撤pager，数据回调撤旧holder；8/16压力请求身份完整/不重不漏、每页≥.6、完整footprint在本岛mask。此前错误回执（test off-by-one、known-write-vs-unknown-readback、未先读buffer、错误要求view.PageIndex同步）均保留，并以最终证据取代，不修改产品配合假断言。pre-guard私有源投影在跨岛context和匹配fingerprint两条分别RED，当前GREEN。

旧RuntimeProbe最初因缺项目引用/Events等API无法编译；补必要host适配后，旧all-mode仍不通过。用b098f73与当前源码逐S1–S15新进程矩阵保留比较，不能把所有旧失败一概称年代差异。新增S11/S12观测差异来自已删除/早已改名的global字段：私有观察适配后S11=22/0，S12=15/0且饱和9份live lease各Owner逐引用被captured或deferred身份覆盖。未恢复global生产字段来迁就旧测试，也未称整个旧suite通过。独立美术/几何和源码审查通过，详情失败修复有单独GLM5.3 max事前审查；最终源码/host审查与交付回执在本机tasks/map-shield-visual-20261014。

边界：本轮公共源码包装器不是当前累计正常版DLL，禁止直接拿它安装。未启动游戏、未写正常环境/配置/共享存档；正常DLL仍36faec77…84b2c3。Mac原生运行与玩家效果、Windows/Android未验；跨data refresh记忆详情页未修改/未验。继续更新Draft PR222（依赖Draft PR202），Issue200保持开放，不合并、不部署、不新tag，作为既有条目的视觉返修不重复计版。
