# ohmymods — 项目范围

## 项目是什么

《王国：两位君主：奥林匹斯的召唤》(Kingdom Two Crowns: Call of Olympus) 的双架构 mod：
Steam 发布与端到端验收主线使用 IL2CPP 2.4.0 + BepInEx 6；Mono 2.1.0 + UMM 是冻结历史/自用线。
以希腊世界为基准，把北境（norselands）的兵种体系引入希腊：狂战士、忍者、北境工匠（带盾）、北境居民，
同时保持两种形象的视觉一致性和原生行为（盾牌防御、挥砍、拾取）。

## 长期目标

- 希腊世界可原生招募北境兵种（狂战士、忍者），无需每局 hack 生成。
- 北境工匠出生自带盾牌（希腊无盾牌商店，槽位被狂战士商店占用）。
- 单位缩放：北境形象与希腊形象最终视觉高度一致；狂战士/鹿/小动物有差异化缩放。
- 性能：单位缩放零每帧扫描（y 轴守护替代 FindObjectsOfType 轮询）。
- 代码可维护：patch 按职责拆文件（Patch_Castle/Patch_ShopPlanner/Patch_Worker...），决策落盘到本 harness。

## Non-Goals（明确不做）

- 不改游戏资源文件（prefab/贴图/动画），一切通过运行时 Harmony patch + 代码逻辑。
- 不做联机专用的新协议（沿用游戏原生 RPC/序列化，必要时仅注册 sync 池）。
- 不碰希腊原版兵种的行为（除缩放对齐外）。
- 不引入两条技术栈以外的新运行时依赖（IL2CPP：BepInEx 6/Il2CppInterop/HarmonyX；Mono：UMM/Harmony v1.2）。
- 不另做独立 UI；IL2CPP 使用现有 ModPanel，Mono 使用 UMM 面板。

## IL2CPP 适配（2026-08-12 启动，执行中）

- **决策**（用户拍板）：发布受众 = Steam 正版玩家 → 必须 IL2CPP 版。Mono 侧维持 UMM 现状自用。
- **目标**：Steam 2.4.0 IL2CPP（开发环境 `E:\QQ\...\Kingdom Two Crowns (1)` 已装 BepInEx 6 + interop 壳）。
- **技术路线**：BepInEx 6 + Il2CppInterop + HarmonyX；"Rosetta Stone"——本仓库 Mono 反编译源码作逻辑说明书。
- **工程**：`il2cpp/KingdomEnhancedMod.csproj`（插件名 KingdomEnhancedMod，Debug=IL2CPP 配置）。
- **迁移分组**：M1 经济（CurrencyBag/Banker/ShopPlanner/SidedShop）、M2 角色（Holder/Castle/Knight/Character/Worker/World/BeggarCamp）、
  M3 世界战斗（Mover/Construction/Level/Kingdom/EnemyManager/Artemis/HermesStaff/FriendlyTroll）。
- **已知决策**：池修复类（Patch_PoolManager）不进第一批，冒烟测试乞丐拾取场景复现再补（`il2cpp/notes-operator.md`）。
- **参考**：`docs/多架构开发指南.md`、`il2cpp/notes-*.md`（各组迁移笔记）。

## 环境约束

- **[IL2CPP 发布线]** 游戏兼容版本 2.4.0；.NET 8 SDK；BepInEx 6 + Il2CppInterop + HarmonyX；
  `il2cpp/KingdomEnhancedMod.csproj` → `KingdomEnhancedMod.dll`。
- **[Mono 自用线]** 游戏版本 2.1.0；C# 5 / Framework csc.exe；UMM + Harmony v1.2；
  `build.bat` → `MyMod.dll`。
- 2.1.0 反编译源码只作为业务逻辑说明书；IL2CPP API 必须再用 2.4.0 interop 壳核对。
- 实机只在独立测试副本进行；Steam 正式目录和共享存档都不作为自动化测试目标。
- 协作流程：见 `collaboration-protocol.md`（Operator/Worker/Reviewer 顺位与模型约定）。
