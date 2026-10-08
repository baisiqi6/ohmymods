# Issue #186 — Android 当前源码编译盘点刷新

2026-10-08。owner mac-codex-ohmymods-android-operator，session codex-android-compat-refresh-20261008，branch codex/android-compat-refresh。
代码基线 fb60c27623a72b73542ce0d74a2882c682775000；依赖 issue-184 正常代码 done，手机等仍 pending。

## 目标与证据

完整移植继续按用户授权推进。本批仅修复和刷新诊断工程，先取得可信的全源编译差异再选下一依赖组。
当前原 PC 工程 direct EmbeddedResource=0，Import EmbeddedAssets.props 中 actual 37；诊断 XmlPeek 查旧直接声明，空清单 Error 在编译前发生。
GenerateMyPluginInfo 仍可从原工程读 AssemblyName/Version10.8.35，正常部分不修改。原 B0 issue142 已合并关闭，这次是公共资源迁移后的新偏差。

## 实现契约

仅 android/compat/CompileInventory.csproj 与 README.md。直接导入同一 il2cpp/EmbeddedAssets.props，沿用 props 自身目录、实际 LogicalName，不另建资源表/派生命名/扫描 Import/fallback。
删除 CollectDeclaredResources 及其失效 item/数量/空清单守卫；确认无外部 consumer，若发现 consumer 超过本范围则记录、不静默扩改。MyPluginInfo 保留。
全共享 il2cpp 源扣除具名9平台替代，android 顶层真实源与诊断 alias照旧；不增加 dummy 配置/类型，不删失败功能，不引入 BepInEx 到产品。
README 所有现状数字用当前实际构建；保留历史计数为历史，错误分类区分 default 声明阶段与 optin body/mixed metadata。
若新错误出现，具名记录为后续输入，不顺手修产品。PC/Mono/Main/UI/20真实配置/35钩子完全保持，不安装、不打包、不发布、不加正式 Mod 版本。

## 验证与验收

1. 原工程实际复现错误，保存当前源码、refs pins和原完整日志。
2. SDK10 真实 Android interop/loader/support，两次 default/optin 编译在私有独立 obj/bin、每次<=120秒；超时保留失败、不反复重试。optin两 BepInEx DLL严格compile-only，不把 mixed WrapToIl2Cpp 当 native 缺失。
3. MSBuild evaluated items 与原项目共37资源逐项 path/LogicalName/内容SHA相同；全部当前源、9替代/obj/bin排除正确。精确输入前后保持，Android Main 编译输入未改。编译报错是盘点结果，不强求全源绿。
4. 按真实报错去重分列配置/平台/其他，给依赖分组与65–180h旧估算的更新依据。数字是编译表面，不是完整runtime依赖/完成率。
5. Worker实际 deepseek/deepseek-flash/max（上海夜间），一个<=8分钟有界轮，仅两文件；独立 reviewer核修复点和实际MSBuild证据；Operator验收及正常 PR合并之后关闭本代码Issue并同步canonical。玩家实机结果另列，NativeReady/fullAndroidLive不升级。

## 审查与受管职责

本方向已实际GLM5.3/max三问审批准 APPROVE_ANDROID_CURRENT_COMPILE_INVENTORY_REFRESH_AT_CANONICAL_RESOURCE_IMPORT；public结果私有locator compat-refresh/direction-result.md，模型receipt实际 zhipu-coding-plan/glm-5.3/max。
受管 canonical 正常注册/plan approval/accept 由主 Operator 单写或明确指定窗口；不改本地checklist冒充canonical、不裸写JSON/DB。代码交付和closeout遵循用户既有自主授权，不逐步问用户。
