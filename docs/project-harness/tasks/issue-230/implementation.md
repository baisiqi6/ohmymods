# Issue230 — Android 公共存档导入与依赖盘点

基线为 `release/v9.5.13@53afef0aa6beecb64fce217aa78a81df4aeda356`。本批仅修改两个共享源的 import：ANDROID 导入 `Il2CppCoatsink.Common`，PC `#else` 保留原 `Coatsink.Common`。两个原文件其余字节完全保持，各净增54字节；没有新类型、空 namespace、配置占位、业务复制、扫描或重试。

错误产生处已核实：现有 `global using Coatsink = Il2CppCoatsink` 不参与其它 using 指令的 namespace 解析。真实 Android interop 包含 `Il2CppCoatsink.Common.SaveLoadResult` / `Routine`，且 `GlobalSaveData.Save/SaveAsync/_TryLoad` 签名引用它们。此前20条声明错误不能作为 Android 缺少原生保存 API 的证据。本批只修导入，不证明原生ABI、保存时序或完整存档玩法兼容。

## 实际验证

| 项目 | 结果与边界 |
|---|---|
| 全量诊断，原始 Android references | 修前22条声明错误；修后精确2条：`PatchExtensionIsland_Progression.cs` 的 CS0234 Hook 和 CS0246 INativeDetour。依旧失败，不称全量可编译。 |
| 仅诊断 opt-in 桌面metadata | 修前20条声明错误；修后进入body阶段，96条错误/1条诊断警告。76处ModConfig引用涉及37成员；另1处PopulationCounts.CrossbowmanRole；19处混合metadata WrapToIl2Cpp歧义。未加BepInEx DLL到Android产品。 |
| 现有Android产品，真实引用/SDK10 Release | 0警告0错误；实际66个Compile输入，两份改动源均不在其中，工程和输入列表未改。 |
| PC共享源码私有Mac编译 | 当前配置net6，0警告0错误；与精确上一主线源码产物比较，1356类型/7141方法/37资源语义MATCH，允许差异0。本轮未运行Windows构建或玩家实机。 |
| 产物/环境 | 未重打包、安装或运行游戏，没有修改Mac正常版、Steam正式目录、存档或配置；Android仍保留既有0.0.30成果。 |

上表的22/20与96属于同一当前基线的不同编译阶段，不能相互相减估算剩余移植率。96条也不等于96处原生缺口；显示日历/人口的PC诊断引用已有Android平台替代，配置名数量不能当作未移植功能数量。

## 后续功能依赖

先以真实职业身份与现有保存责任核定人口分类，再接入普通弓箭选项、英雄覆盖层和弩手生命周期；经济/自动补货保持独立组。原来的depth≤2类型图仅为直接边线索，未当作完整源码闭包。不会单独新增永远为零的身份分类或假配置来令诊断变绿，也不会仅凭compile-only结果注册保存/射击hooks。

本批为ordinary公共编译小改，采用任务spec、worker候选、Operator逐字集成、独立审查与必要编译后Git交付；没有新high-risk canonical写入。功能启用批将按重要任务正常登记并另与主Operator协调窗口。代码PR尚待本批审查与交付；不宣称新增玩法或实机通过。
