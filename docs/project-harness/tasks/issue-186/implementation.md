# Android 编译盘点刷新：公共资源导入

2026-10-08，Issue186，base fb60c276；本批仅诊断工具与对应记录，不修改游戏功能或操作设备。

旧 CollectDeclaredResources 直接 XmlPeek PC csproj；当前PC通过 EmbeddedAssets.props导入，
direct0、公共37，原实际构建因此在 PrepareForBuild 报1错误/0warning，尚未进入C#源码编译。
修复在错误来源直接 Import 同一props；删除失效收集target、临时items、派生命名及空/数量守卫。
ValidateInventoryDependencyDirs与GenerateMyPluginInfo的XML内容、其余属性/源项/引用保持；
未发现仓库外部consumer（非仓库外全面证明），不另建资源清单或扫描Import兜底。

当前 MSBuild 实际37资源逐项 path/LogicalName/内容SHA等于PC实际集合；PC237源码、57Bind；
诊断项目选228共享源（9具名平台替代排除）＋26Android＋1alias＋1生成=256项目源码项，
不含SDK自动AssemblyInfo。预求值255existing，两个真实构建生成MyPluginInfo，实际内容与
原AssemblyName/Version10.8.35一致、两模式同SHA。XML源项与255非生成路径保持；MyPluginInfo生成
槽位保持，绝对路径随私有obj目录变化，不称生成FullPath字节相同。没有删失败源码。

SDK10实际Android interop/loader/support：default 5E0W（3CS0234＋2CS0246）仍是Configuration/
ConfigEntry与Hook/INativeDetour；optin118E1W=99CS0117（95配置引用/42成员＋3图片绘制＋1弩手分类）
＋19CS0121混合WrapToIl2Cpp，warningCS0649 PrepareWindowObserver。去重同源行，不累加构建总结
重复报错。optin两BepDLL仅compile-only；不称118原生缺口、不强求全绿、不证明运行时兼容。
二构建各3.08/4.63秒，120秒预算内；输入/引用SHA前后一致。图片接口缺口源于ANDROID下未提供
完整 DrawTexture，纯色绘制与完整图片裁剪不能视为相同，下一公共UI批单独处理；CrossbowmanRole
保持弩手批依赖，不添加无依据常量；配置逐功能真实接入。

当前Android Main47精确源码inputs、20entry/35注册形状与0.0.24未变；本批不构建/打包/安装/运行
Main或PC/Mono，不公开binary/tag，不改正式版本。已有手机/多指/联机、自然Spawn差量、OFF原值、
池/换岛/读档/长期边界仍pending。本批只接受编译盘点工具，不升级NativeReady或完整移动版。

## 剩余工作规划（判断，非验收或排期保证）

旧230源码词法16组是candidate依赖；现在237新增7：HeavyShieldEnrollment、ModPanelControls、
ModPanelStyles、PatchUI_CalendarGems、PatchWorld_WallSpotDiagnostics、PatchWorld_WallSpots、Patch_ShopV2Art。
两公共UI源已复用，其余按依赖再核。42配置成员不等于42个需重写系统，也不等于整个功能覆盖。

| 规划依赖组 | 工程小时 |
|---|---|
| 公共图片绘制与只读HUD | 2–6 |
| 宠物隐士、普通角色/招募补员 | 8–20 |
| 弓弩、墙塔与编队 | 12–30 |
| 希腊/北境/死地/幽灵与神器 | 16–40 |
| 跨世界坐骑与持久身份 | 8–20 |
| 英雄/盾卫/火枪资产、商店与身份（可拆） | 20–50 |
| 银行送币与自动补货 | 12–30 |
| 批次联调、原生接口与冷启 | 4–12 |

合计82–208，取宽范围80–210，约8–10交付组；包含实现/兼容/必要审查与模拟器验证，配置接线
内含。已读宠物找回的不限次数retry与状态先写责任需审计，不能因PC有现成代码跳过冗余准则。
未验证原生池/身份/存档链可触发重估；手机/MP/长期验收日历另列。旧65–180/6–10和80–200/8–12
均保留历史，不假借错误数下降宣称工作量完成率。

## 保留失败与验收证据

实际DS Flash/max worker一轮私有候选，不改W；eval非交互批准被拒后改用允许bash，不扩权限。
worker几次验证脚本XmlPeek文本计数断言失败只发生私有构造前，最终内容/元素核对通过。
Root首次用XML序列含tail误把删除后空白当目标差异，在写前拒绝；随后误启动两编译读原工程，
各1resource错误。该轮完整日志/收据留history，不能冒充fixed结果；修正后只比较XML元素内容，
检查成功才启动依赖构建，以上fixed5/118分别来自新source。Root错误解析器为实际中文引号补
读取规则，旧脚本留档，raw日志未改。worker README中提前描述的固定求值由Root真实MSBuild证据
复核后才纳入本最终记录。

私有locator .local/tasks/android-port-resume-20261008/compat-refresh/ 包含原失败、worker模型/public结果、
source/ref pins、fixed logs、validation-summary、37资源与generated-plugin-info证明。独立审查与
正常PR合并/受管closeout仍待此批收尾；代码与游戏实测分开记录。
