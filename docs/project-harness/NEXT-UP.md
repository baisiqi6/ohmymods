# NEXT-UP — V2.x 开发接力总览（会话压缩后的入口文档）

> 更新：2026-08-25。V3.0.0 已发布（弩手/骑士四风格/昼夜布阵/蛇缰绳/平衡）。
> 新会话从 AGENTS.md 被引到本文件；读完本文 + 对应任务的 design.md 即可开工。

## 当前状态

- **V2.1.0 已发布**（GitHub Releases：v1.0 存档 / v2.0.0 / v2.1.0=Latest；仓库已转 PUBLIC）。
  最新功能：骑士小队夜战紧凑列队（rank 压缩）。QQ 群双渠道分发。
- E 盘测试副本（`E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091`）
  与最新代码同步；性能探针（夜战门控版）已部署，**等用户守一夜血月拿数据**。
- 分支 `agent/post-release-candidate` 持续推送；打包脚本
  `C:/Users/ADMIN/AppData/Local/Temp/kem-ghostleash/package_v2.py`（临时目录，会话压缩后需重建，
  模板见 git 历史与本文件附录）。

## V2.x 任务队列（用户确认的顺序）

| 序 | 任务 | 设计文档 | 状态 |
|---|---|---|---|
| 序 | 任务 | 设计文档 | 状态 |
|---|---|---|---|
| ① | 骑士小队（大骑士+2小骑士+4弓） | tasks/knight-squad-023/design.md | 草案，开工前需用户确认 5 条规则 |
| ② | 武士骑士战斗特化+突进斩 | tasks/samurai-knight-022/design.md | 设计定稿（幕府外观已由随机风格交付，改战斗特化） |
| ③ | 北境盾兵小队（prefab级完整引入） | 新建 | 已立项（2026-08-24），跨生物群系替换基建 |
| ④ | 夜战齐射错峰 | tasks/staggered-volley-024/design.md | DefensePerf 数据已到手（arrows=39 avg6.5-10ms max56-287ms），可决策 |

V3.0.0 已交付：弩手（含死地随从弩手化/弩矢平直拖尾）、骑士四风格全套、昼夜布阵（白天踱步/夜间紧凑/墙外三因三治）、蛇缰绳墙+60、巨魔5%/法杖11.25s。

## 本阶段沉淀的核心技术事实（勿重新踩坑）

1. **2.4.0 死代码**：`Archer.GetWallTargetPos`、`Knight.GetTargetPos`、`Director.Update` 全都
   钩不住（AOT 内联/重构）。**可行模式**：World.OnLevelLoaded postfix 启动协程
   （`WrapToIl2Cpp`）+ 直接读写字段（`_guardDepth`/`rank`/`_fireJarsActiveNum` 等 interop
   全暴露）+ 幂等守卫。先证路径再写补丁（与坑 24 同源教训）。
2. **没有 2.4.0 反编译**：侦查工具 = interop 二进制 grep（`grep -c -a -o "类名" Assembly-CSharp.dll`）
   + pwsh 反射脚本（模板在本文件附录）+ 存档解压 grep（gzip）+
   resources.assets 路径扫描（P2P 副本 `E:/Kingdom.Two.Crowns.Call.of.Olympus/...-P2P/`）。
3. **跨世界皮肤 = BiomeSwapData 动画换皮**（`banker_deadlands`/`archer_deadlands` 是控制器不是
   prefab）；完整角色变体才是 prefab（`Knight_norselands`/`Samurai`）。
4. **原生无任何自动/免费冲锋**（穷尽证明）；白光 = `character.Inspire()`；
   六神像全局统一（Archer/Worker/Knight/Farmer/Time/Pike），无分世界变体。
5. 协作规范：worker=OMP `deepseek-v4-flash` thinking=max；reviewer=OMP `kimi-code/k3`
   （当月配额可能耗尽→回落 GLM subagent）；经济/行为契约改动必须 reviewer。
6. 发布纪律：命名规范见 runbook（文件名=Mod版本）；构建戳在 KingdomEnhancedPlugin.cs
   （`build=`）；E 盘部署需游戏退出（哨兵模式等待进程）；ZIP 不进 git，走 GitHub Releases。

## 附录：打包脚本要点（临时脚本丢失后重建用）

- OUT：`release/KingdomEnhancedMod_v{ModVersion}_IL2CPP.zip`；
- 内容：E 盘游戏根的 `.doorstop_version/doorstop_config.ini/winhttp.dll/dotnet//BepInEx core+unity-libs/
  config(仅cfg)/plugins(仅新DLL)` + 根级 `MOD_*.txt` 三份 + INSTALL.md(=release-notes-il2cpp.md)
  + BUILD-MANIFEST.txt（ModVersion/GitCommit/DllSHA256/GeneratedUtc）；
- 打包前 `git commit` 保证 manifest dirty=false；
- 泄漏检查：无 .bak/LogOutput/cache/interop/SKIDROW 混入；
- 发布：`gh release create v{版本} --target agent/post-release-candidate --latest`，
  更新 v2.x 中文群公告 txt。
