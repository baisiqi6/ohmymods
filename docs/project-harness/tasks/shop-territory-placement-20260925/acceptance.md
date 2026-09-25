> **2026-09-26 实机反馈与发布交接**：用户反馈累计94CC219A目前没有明显问题；已核对安装摘要及实际启动日志标记。当前单机整体反馈良好，专项边界仍按原记录保留。完整发布说明、源码范围和接手注意事项在同目录release-handoff.md。

# 商店全领地选址候选

状态：代码与构建验证通过，最终独立GLM5.3/max复审APPROVE；2026-09-25 23:55确认游戏退出后已安装94CC219A，旧DLL已备份，存档/配置hash保持，未启动游戏，实际游戏效果待验。

用户确认空地在外墙内。已验证的代码缺陷是共享选址函数只探测领地中点±30、每0.75一个点；现场日志不含失败边界快照，不能声称已逐项还原这一局空地为何被拒绝。火铳铺和英雄驿站走自定义Create流程，不依赖原生ShopPlanner排队。

改动为读取整个intact城墙范围的完整AllPayables/_allBlockers预留边界，在合法商店中心范围内划分候选分段。超集只细分分段，从不扣除分段；原生可忽略对象不会人为删掉空位。原生OverlapsAnyExclusions(...,false)仍是最终门。中点先试，无固定搜索半径/采样间距/固定epsilon/对象或候选截断。每段至多3个内部float候选，按距中点排序，同距先左；计算double阈值前保留原生float物理边界。缺失/非finite数据不静默视为空地，等待现有失败重试。

生产范围仅HeroShop.cs选址core/只读native adapter/创建接线/状态文字、MusketeerShop.cs创建接线/状态文字，以及插件build横幅。已有店不会搬迁；五秒创建重试、暂停/网络/开关门、地面参照、付款和枪架未改。没有新Harmony hook，也没有native集合写入。

验证：HeroShop Core 67断言、Musketeer shop18、side-rack60、production wiring35，均通过。Core覆盖远端左右、窄缝、超集巨型可忽略区、闭区间相触、精确领地宽、浮点窄缝、非法输入/集合、采集异常、双店避让、1502区间无截断及与地图长度无关的探针上界。实际2.4 HeroShop Interop和完整IL2CPP Debug构建0警告0错误；musketeer-shop测试构建有4条既有stub CS0649警告。Operator复跑Core/接线及改build标记后的完整构建通过。

上一轮武士运动/视觉及motion测试源码SHA256与DED188D7候选完全一致，保留前轮328/0证据；本轮未重复声称做了武士实机验收。旧候选已在武士任务receipts/pending-DED188D7.dll.bak备份。

候选build=9.14.24-choreo-shopland-20260925，MD5=94CC219A57909EA19D4F366F84758DB6，SHA256=9E56BF4BB873DC68F7D65A5492D52196D76CB5D8E9F2A7E6231C512F636411C4。当前测试副本旧DLL预期SHA256=EC86481FF07E7ECB9D1CC5541500F22357D29F41A4B194608EDFE7FACD18ABA1；运行时不得替换。安装仅在游戏退出后，既有安装脚本核精确新旧hash、备份、保存/配置前后hash，且不启动游戏。

剩余：实机确认远端店铺生成和美术位置、重进/扩墙后的重试、累计武士候选的收势/回程/残影观感。任务不标玩法done，不commit/push/publish。

附带只读EXharness校验因历史七条任务缺handoff/verification等字段报15项错误；本轮checklist未修改，不能宣称全项目harness通过。主树的既有冲突仍保持。
