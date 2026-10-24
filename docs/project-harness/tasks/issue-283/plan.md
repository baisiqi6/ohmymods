# #283 两座坐骑岛地图装饰

用户选择左岛疏林、右侧弯月岛沙岩海湾；看过离线试作后要求装饰更明显，并补充周围水域。仅大地图/详情页，不改实际岛屿世界。基线 release/v9.5.13@98259aff338bb9e5ee1b666e4f5c67da24eed6fe，分支 codex/mount-map-decoration-20261024。

## 采用方案

- 原两个岸线 PNG、228×84 准备画布、颜色基底、Mask/PlacementMask/outline、比例与船位保留。新增每岛一张228×84二值透明六灰阶装饰图：左岛两组低树冠，右岛低砂岩草石，南侧岸外各一组小礁石与短海纹。R4只修正右岛B2可读性，其余R3不变；两位目视审查者已复核1×/放大图。
- 四个陆地装饰框（左上原点、半开）：A [62,8,82,20]、[146,22,166,34]；B [38,28,50,36]、[146,22,158,30]。像素在真实顶面内。水域像素 top-y>=55、原 Mask/outline 外、离岸至少2px；北侧船/涟漪道留空。新增暗灰116/155只用于装饰，原岛底色四档/主色227不变。
- 同一profile的AppendDecorationBlockers一次翻转/缩放并加既有1.5 UI间隔，在PlanExtensionIsland与TryBuildDetailPageContext各接一次。每岛8图完整、scale>=.6优先，放不下就减装饰，不隐藏或继续缩小图标。总览强传8图的host测试仅为接线/压力验证，不声称原版总览默认显示8个。
- 可选资源在原ArtCache冷加载一次；原shore texture不合成。每个绑定shore下append一张stretch、raycastTarget=false的静态Image，与图标/高亮保持层序；共两纹理、每个地图owner最多4个view，无Button/额外输入过滤/动画/Update或逐帧层级扫描。绑定边界继承原颜色。无效装饰一次可观察警告，岛和图标仍正常。
- 自有节点复用既有BorrowedImage/Leases责任链，与原生Image“缺失原sprite快照=unknown”分支区别。先登记再配置，失败回滚；同owner幂等，需当前父shore exactowner租约才可创建，新owner接管、旧owner迟到清理不交叉；父归还时级联销毁，自有责任未清时缓存不能提前销毁。无第二套注册/调度框架。
- CollectNativeBoxes、TryCompactGraphics/其验证仅排除exact登记自有Rect，不能按名字跳过任意Graphic。clone sanitizer只新增KEM_MapIslandDecoration名，清理clone不伤母本；append末child，保护现有GetChild(0)查找链，不重排原生siblings。

## 决策演进与审查

早期内侧RGB烘焙被用户的岸外水域需求替代。随后零节点shore像素合成试案经原生输入审计撤销：实际FindArtTransform优先Land Button，2.4.3 Image.IsRaycastLocationValid在正alpha阈值下会采样sprite；现场阈值unknown，不能从模组grep缺失推出矩形命中。当前独立不可点击显示层保持原shore像素，避免依赖该假设。没有强设原生阈值、关闭原按钮raycast或弱化守卫。

GLM5.3/max已对当前增量三问审查通过；公开结论明确纠正上一轮不足的推断。Worker实际OMP DeepSeek Flash/max（北京时间周末），独立Reviewer核素材与真实source-linked接线。后续修改只复审受影响增量，构建/审查/安装/实机分别留证。详细过程仅保留任务私有材料，公开结果见verification.md与receipts/。

## 验证与边界

- 实际生产Prepare、原始decoded alpha和准备alpha/outline/双mask固定golden保持；源PNG hash不变。装饰像素与4框、水域要求独立验证，不能更新geometry golden掩盖变化。
- 已完成4框/两入口红绿：Worker502 runtime/105 art，独立198针对检查；分别去掉任一入口调用均精确报碰撞。当前UI增量完成后检查实际两个入口显示、原input字段、8图、重复bind/owner交接、失败清理、父释放/cache、exact排除与clone净化。
- 必要Mac240/Win240/Win243纯编译使用私有输出、NoDeploy=true且BepInExPluginsPath空；官方Compile/完整资源闭包求值，Win243单独私有PR250 alias输入，不写官方工程或运行包。最终日志/计数及hash单列。
- Android Probe的当前显式Compile/资源/pin不包含本次源与资源，未导入新增props，无Android改动；文件范围若变重新核对。未变产品验证复用，不重跑无关保存/战斗/设备用例。
- Native可视、实际选岛输入/颜色状态与裁剪、draw-call/帧率尚待独立实测，host通过不冒称实机。无正常存档/配置或游戏写入，无Steam写入，未覆盖Windows #281已安装诊断候选。
- 当前只交代码/美术及必要验证，正常commit/push/PR；合并/安装另保留对应实质审查，不复用旧范围或过期运行凭据。
