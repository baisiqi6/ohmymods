# Issue #254 — 恢复皇冠窃贼原生速度（PC/Mac 主线）

## 目标
用户反馈此前调速后部分皇冠窃贼个体异常缓慢，要求恢复原版速度：PC/Windows/Mac 不再由 Mod 写
CrownStealer 的 walkSpeed/runSpeed/jumpSpeed/chargeSpeed/wallJumpForce.x/chargeJumpForce.x，
六个水平量交由原生逻辑负责；不反乘、不逐帧覆写、不改伤害/攻击节奏/其他敌人。

## 基线（实际游玩版核对，非默认主目录）
- Mac 游玩 DLL：`/Applications/ohmymods/arm64/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll`
  SHA256 `5fb9c5f8afcdb2774e7f250dc9ae9bd3c24f9218f37e241c262b6acd4ee89352`，插件 v10.9.38；安装回执
  `.local/tasks/release-consolidation-20261015/final-delivery-receipt.json` 记录 release
  `ee153aca3f2b70aee5f73de46af4362b08e332b8`。
- Mac 游戏 2.4.0（Info.plist），Unity 6000.0.61f1，BepInEx 6.0.0-be.788，.NET6。
- 主线 `release/v9.5.13@1f9c675`；`git diff --stat ee153ac..1f9c675` 共 19 个文件，
  `git diff ee153ac..1f9c675 -- il2cpp` 为空，其中唯一测试文件是
  `tests/knight-identity-runtime/SaveLoadTests.cs`。本任务涉及源码与游玩版一致。
- Windows 2.4.3 是独立宿主（BuildInfo 2.4.3 rev23537 hash36d5d6e9a5，Unity 6000.0.66f2）；本 Mac
  会话未在其上构建/实机，证据另记。

## 根因与写入责任
- 观察事实：`PatchRoles_CrownStealer.Awake_Prefix` 是全部生产源码中唯一的 CrownStealer 六字段
  写入者（`receipts/source-audit.txt`）。该文件此前对所有非 ANDROID 构建也编译出补丁（仅 Android
  的独立开关在 `#if ANDROID` 内），所以 Windows PC、Mac 私有 wrapper 与任意 `il2cpp/*.cs` glob 都会
  在非 Android 构建中无条件乘 0.75，只受总开关 `ModConfig.Enabled` 控制。
- 推断（未实机取证）：不同 prefab/个体原值不同，统一 0.75 让低原值个体更显异常缓慢；移除非
  Android 写入后所有实例恢复原生 prefab 数值。不把该推断写成已确认的个体差异根因。
- 决策：用**源级整文件 `#if ANDROID` 守卫**代替项目级排除。任何非 ANDROID 构建路径（Windows
  `il2cpp/KingdomEnhancedMod.csproj`、Mac 私有 wrapper/任意 `il2cpp/*.cs` glob、临时工程）都会把
  该文件编成空单元，因此不需要修改私有 wrapper，也不会出现“Mac 构建走 wrapper 仍编入补丁”的缺口
  （曾评估 csproj `Compile Remove`，会漏掉 Mac wrapper，见复审记录）。
- Android 分线保留 Issue196 用户明确要求的默认 OFF 独立开关（独立平台线，默认即原生速度）：
  Android probe 显式 Include 本文件；本提交同时重钉 Android frozen-source pin、重建 probe 并跑
  AdapterTests，避免共享线变红。

## 改动
- `il2cpp/PatchRoles_CrownStealer.cs`：整文件包进 `#if ANDROID`；文件头说明非 Android 构建退役与
  Android 保留原因；保留 Android 既有热切开关边界注释。新 SHA256 `e4821c83…afe34`。
- `android/tests/Program.cs`：把 `il2cpp/PatchRoles_CrownStealer.cs` 的 frozen-source pin 从
  `46a06704…6924f` 重钉为 `e4821c83…afe34`（同提交改源+重钉，遵循该清单约定）。
- 删除 `tests/crown-stealer-speed/`（只测 PC 全局 ×0.75 行为，PC 已不编译该类型）。
- 本任务文档与回执。

## 验证
- **PC 风格构建**（Mac wrapper 模板，源 glob 指向本 worktree，无 Compile Remove；含 canonical EmbeddedAssets.props 的嵌入资源）：0 warning / 0 error，
  DLL SHA256 `c0586367a3228387eb88aa4cb4925d1cd9f22dbafabff99ef004d9c82eb2de4e`；metadata
  `PatchRoles_CrownStealer`=0、UTF-8 `CrownStealer` 类型=0、对照 `PatchRoles_Castle`=1
  （`receipts/pc-build.log`、`pc-metadata.txt`）。
- **Mac 真实私有-wrapper 风格构建**：复制本机 `.local/build/Integration.csproj`（macos-workspace.md
  的 Mac 构建入口，私有、不入库），只按 sync 脚本方式把 Compile glob 指向本 worktree、保留其余
  （含 EmbeddedAssets Import、无 Remove）：0 warning / 0 error，DLL SHA256 `11d8b6dc…e72ba`；
  metadata 0/0/1；PDB 因受守卫文件仍是空编译单元而记有该文档，但类型/IL 为 0（`receipts/mac-wrapper-build.log`、
  `mac-wrapper-metadata.txt`）。该证据只按 sync 脚本方式重定向 Compile glob；EmbeddedAssets Import 仍指向
  canonical props（资源未重定向，不影响 C#/类型结论）。正式安装构建须同时按 sync 脚本重定向 Import。
- **Android probe**：用本机既有 interop/loader 三个目录实际构建 `OhMyMods.AndroidProbe.csproj`：
  0 errors（1 个既有 CS0169 warning，来自 Patch_HeavyShieldBanner.cs，与本改动无关），probe SHA256
  `7a5492d2…e6d12`；`AdapterTests` 1265 checks / 0 failed（含 frozen-source 校验）；
  `android/tests/combat-parameters` ANDROID host 27/0。见 `receipts/android-*.log`。
  probe 仅本地构建，未安装/未部署。
- 未做：实机；真实 Mac 安装/部署；Windows 2.4.3 构建与实机；Android 设备/模拟器玩法。

## 生效边界
- BepInEx 插件在游戏启动时加载；对象池由游戏自身重建。已运行实例的旧缩放值不会被回滚，需**完整
  退出游戏后重进**；新实例直接使用原生 prefab 数值。
- 六个字段无证据显示被存档持久化（2.4 interop 无 CrownStealerData；抽查样档 JSON 无 speed 字段）；
  该点为推断，实机读档时复核。
- Mac 当前停在保存失败弹窗：本任务未操作该进程、未替换正在使用的 DLL、未覆盖/恢复共享存档。
- Mac 安装构建前置：必须先把 Compile（与 EmbeddedAssets Import）重定向到本提交源码；当前
  `.local/build/Integration.csproj` 仍指向 10-06 canonical 快照，不能直接用旧快照构建本提交。
- 独立审查：reviewer-a 已确认 M1（Android frozen pin）与 M2（Mac wrapper 路径）均关闭（源级守卫
  对任何编译该文件的非 ANDROID 路径生效；Mac wrapper 风格构建与 Android probe/AdapterTests 已实跑复核）。

## 未验证边界
- Mac 2.4.0 实机速度观感（wrapper 构建已验证类型缺失，真机未跑）。
- Windows 2.4.3 构建与实机（走 `il2cpp/KingdomEnhancedMod.csproj`，同一源级守卫，但本机无法验证）。
- Android 真机/模拟器玩法（probe 编译与 AdapterTests 通过；Issue196 默认 OFF 行为未改）。
- 个体异常缓慢若在原生速度下仍出现，属原生/其它写入者问题，按新证据另开 Issue。
- 下一正式发布时需更新 `release/` 面向玩家文档中仍把 ×0.75 写成 PC 现有能力的条目
  （`功能总览.txt`、`用户指南.txt`、`能力与路线图.txt`、`更新日志.txt`、
  `MOD_V10.7.34版本更新说明.txt`）；历史版本说明本身不改写。