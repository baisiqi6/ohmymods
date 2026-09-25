# 商店覆盖全领地选址（2026-09-25）

用户确认空地位于外墙内，要求火铳铺在整片领地内寻找合适位置。旧共用 HeroShopPlacement.Find 只检查领地中点 ±30，间距0.75，总计81点。英雄驿站使用同一函数。

本轮边界：IL2CPP 主线；完整 intact 城墙内、容纳店铺全部宽度；保留原生 OverlapsAnyExclusions(..., false) 最终判定与原生地面参照、五秒创建重试、单机/暂停/开关门；不搬已有商店、不改支付或枪架。不修改原生存档、配置、Steam副本，不 commit/push/release。

当前工作树 win/samurai-choreo-hard-validity 累积武士候选尚未安装。原候选 DED188D7 已备份到武士任务 receipts/pending-DED188D7.dll.bak；本轮最终构建应包含该候选全部修复，不能回退。现场仍为8E994A98、游戏PID45240运行中。

设计已由独立 GLM5.3/max 复审通过：完整预留边界扩展为商店中心阈值，划分整个合法中心范围；每个分段都提供内部候选，包括被可忽略对象预留区覆盖的分段。只增加候选，不把超集区间当障碍扣除；不使用固定网格或固定epsilon排除窄缝。中点快路径、按距中点排序及同距左优先；候选规模随对象数增长，不随领地长度增长，不截断远端。原生真实2.4 interop 已确认 AllPayables、_allBlockers、PayableExclusionPoint、payablePlacementExclusionDistance、GetExclusionPoint、exclusionDistance 存在；2.1源码只作为逻辑参考，最终仍由真实原生重叠判定决定。

验收：远端左右空地、非对称领地、旧采样间隙、正好店宽、建筑/树木/blocker、边界/浮点非法值、全占满和大领地有限工作量；英雄与火枪相关测试及actual2.4编译；独立最终复审；游戏退出后才能替换既定E盘DLL。当前无运行时全领地生成验收。

已有基线：HeroShop Core54断言、Musketeer side-rack60断言通过。源码基线保存在 receipts/*.before.cs.txt。
