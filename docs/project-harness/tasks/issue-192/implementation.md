# Issue192 — 公共平台边界实现与验证

基线 release/v9.5.13 `af1ae0b`，分支 `codex/android-common-seams`；依赖188代码任务正常done。冻结plan `204be5a8` /6971B，normal operation `3e849caf-45f6-47fe-ba6b-020ba2d0439e`。本轮只交付公共编译边界，不启用身份、经济、角色、存档功能。

已在7份共享源的3个显式配置类型与5个数据目录引用处适配：配置类型直接使用真实 MelonPreferences_Entry / ConfigEntry 条件别名；Android单一 ModDataPaths.ConfigPath 直接返回加载器 UserDataDirectory，PC alias仍解析原BepInEx.Paths。helper仅ANDROID编译，无字段/cctor/I/O/cache/fallback/额外目录创建；原文件名、Path.Combine、原子写、权限及失败处理不变。Hermes alias额外排除 HERMES_CYCLE_TEST，原纯host保持无Bep依赖。复用既有AndroidCoroutine，没有再造协程层；旧目录消费者没有独立补偿可删除，不扩大原catch。

Worker实际 OMP deepseek/deepseek-flash、max，公开native route及消息身份已核，session `01a11d89-622a-7004-a8f0-242210eeba6b`；候选只写私有任务目录，7input+8output hash复核后由Operator审核diff并集成。Operator统一增加一个产品 Compile link、把原project强pin刷新并新增helper pin（55→56），所有旧checks保留。Probe、20配置、35hook、UI及原生业务入口保持。

实际验证：Android51CompileItems SDK10 0W0E（2.86s），Main `4c129bb6…` /219648B（Debug，仅编译，未安装）；真实artifact绑定 MelonEnvironment /get_UserDataDirectory，零Bep引用。PC独立纯编译0W0E（4.27s），对同base完整语义比较1311types/6906methods、37资源全部MATCH，未部署。原adapter正常793/0、真正 --oldcfg633/0。既有送币runtime桥223检查通过、自动补给86/0；均为host，不当作手机玩法验收。

Hermes纯host正确编译0W0E，运行25通过/1失败：原“共享读取句柄会拒绝原子替换”的fixture在本机未造成预期拒写。同base未经修改源码也25/1，原版与候选完整host程序集语义MATCH；私有仅加一个Console阶段标记的诊断确认第一段独占读拒绝已符合预期，第二段预期拒写没有发生。未改生产写入策略、未跳过/删除/弱化测试、不宣称权限/Android持久化验收通过。此为现存fixture在当前主机的验证局限，后续功能真启用仍需具体权限与保存边界验收。

全量实际盘点260CompileItems=231shared+27Android+1alias+1generated；37canonical资源hash保持。default由5E0W降2E0W：3个配置声明错误不再产生，剩扩展岛 Hook/INativeDetour声明；停于声明阶段不是全方法体兼容。optin115E1W仍95配置引用/42成员、19混合集合扩展歧义、1弩手人口索引；两个Bep DLL只用于此诊断，不进产品。未填假成员、排除失败源码、默认假值或修改协程来凑绿。

代码审查/PR合并/normal收尾按各自回执继续记录；本次没有安装、启动、APK打包、公开发版/tag或PC/Mono部署。仍安装并保留此前188成果；本helper未通过角色调用触发新保存。真实目录/权限、相关身份/交易玩法、手机/联机/长时池与读档验收均pending。

剩余规划仍为8–10组、80–210工程小时的条件区间；配置95引用/42名字和主要高风险业务依赖未减少，不能从3个编译错误的消失折算完成率。此公共边界后按实际依赖合并角色/战斗/补给、防线/阵形、神器、身份资源、经济、跨世界坐骑与扩展岛ABI；手机与联机验收日历另列。

私有raw、input/reference pins、worker原件、scope diff、baseline/candidate编译与比较、host日志、原版失败对照、normal登记和保护回执在 `.local/tasks/android-port-resume-20261008/common-seams/`。游戏、loader、interop、私有APK不进仓库。
