# IL2CPP 迁移 — Operator 决策笔记

## 决策 1（2026-08-12）：池修复类 patch 不进第一批迁移
- 背景：Mono 2.1.0 的 Patch_PoolManager（force InitPools + RegisterAllBiomePools 全 biome 池补注册）
  修复的是"InitPools 只建当前 biome 池 → 特效池缺失 → UpgradeTransitionFX NRE → 乞丐 Promote 中断"。
- 2.4.0 证据：LogOutput.log 零 `pool not found`/`failed to find parent`；PoolManager/BiomeObjectPools 类存在，
  但 InitPools 方法体不可读（IL2CPP），无法静态确认 bug 是否还存在。
- 决策：不移植池修复（移植"修复不存在 bug"的 patch 有引入副作用风险——force InitPools 可能打断 2.4.0 正常池流程）。
  集成冒烟测试时专门验证乞丐拾取场景；复现 NRE 再补迁移，不复现则永久放弃该 patch。
- 例外：Patch_BeggarCamp 的"生成间隔 90 秒"是功能改动不是 bug 修复，正常迁移（M2 组）。

## 决策 2（2026-08-12）：Mono 侧维持 UMM 现状，IL2CPP 侧独立 BepInEx 工程
- 双架构同步成本高、收益低（Mono 官方已绝版，无发布价值）。
- Mono 版继续 UMM+Harmony1.2 维护（GOG 2.1.0 自用）；IL2CPP 版（本工程）面向 Steam 2.4.0 发布。
- 功能对齐基准：Mono 版 20 个 Patch 文件（2026-08-12 状态）。

## 约定
- worker 迁移笔记：notes-economy.md / notes-roles.md / notes-world.md（各组"待决策清单"合并处）
- 集成编译：三组交付后统一 dotnet build（bin/obj 清理独立输出目录）
- 发布版本号：2.4.0（对齐目标游戏版本）
