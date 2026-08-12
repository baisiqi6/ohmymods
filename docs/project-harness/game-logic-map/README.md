# 游戏业务逻辑地图（原 kingdom-mod skill）

Kingdom Two Crowns: Call of Olympus 反编译代码业务逻辑地图——商店、兵种、生物群系、城堡升级全链路，
为 mod 开发（UMM + Harmony 1.2）提供**已验证**的调用路径和 patch 模式。

> 迁移来源：`E:/.../.omp/skills/kingdom-mod/`（2026-08-12 迁入本 harness，原 skill 已删除）。

## 游戏基本信息

- 游戏：Kingdom Two Crowns: Call of Olympus
- 引擎：Unity (Mono)
- Mod 框架：UnityModManager (UMM) + Harmony v1.2（`HarmonyInstance`，不是 HarmonyLib v2）
- 编译器：.NET Framework 4.7.2，`csc.exe`（C# 5 语法上限）
- 反编译源码：`E:/.../自制mod/Assembly-CSharp/`，1418 个 .cs 文件，约 28.5 万行（只读参考，不进仓库）
- Mod 工程：本仓库根目录（Main.cs + Patch_*.cs + build.bat）

## 生物群系索引（BiomeIndex）

| 索引 | 名称 | 独有机制 |
|------|------|----------|
| 0 | Europe（欧洲/默认） | Pike 商店 |
| 1 | Shogun（幕府/日本） | Ninja 商店 |
| 2 | Dead Lands | ChangeRuler |
| 3 | Norse Lands（北境） | BerserkerTool 商店、ShieldShop |
| 5 | Call of Olympus（希腊） | ChangeItem、Pike 商店（DLC） |

> `BiomeHolder.Inst.BiomeIndex` 或 `CampaignSaveData.current.BiomeIndex` 获取当前生物群系。
> biome 3 和 5 是 DLC：`BiomeHolder.IsBiomePaidDlc(3)` / `(5)` 返回 true。

## 子文档索引（按需阅读）

| 文档 | 内容 | 何时读 |
|------|------|--------|
| [shop-system.md](shop-system.md) | 商店系统全链路 + **槽位复用方案**（跨世界加商店的核心机制） | 改任何与商店相关的逻辑 |
| [castle-upgrade.md](castle-upgrade.md) | 城堡升级链路：两条入队路径的陷阱与 biome 白名单 | 让某生物群系出现额外商店 |
| [unit-spawning.md](unit-spawning.md) | 兵种产生机制：拾取白名单（Katana/BerserkerTool/盾牌）→ Promote 转化 | 添加/修改兵种生成 |
| [biome-asset-system.md](biome-asset-system.md) | 生物群系资源系统 + 探测盲区教训 + 实例身份自注册机制 | 跨生物群系共享资源 |
| [patch-patterns.md](patch-patterns.md) | 已验证 patch 模式、已知坑（含 sync 池/残留槽位/LoadAll）、功能清单 | 写新 patch 前先查 |

## 关键类索引（高频使用）

### 管理器入口
- `SingletonMonoBehaviour<Managers>.Inst` — 全局管理器单例
  - `.kingdom` → Kingdom 实例（`.castle`、`.border[Side]`、`.campfirePosition`、`.Berserkers`）
  - `.shopPlanner` → ShopPlanner 实例
  - `.director` → Director 实例
  - `.payables` → Payables 管理器

### 核心类
- `BiomeHolder.Inst` — 当前生物群系（`.BiomeIndex`、`.curBiomeAssets`、`.biomePathStrings`）
- `ShopPlanner` — 商店规划器（见 shop-system.md）
- `Castle` — 城堡（见 castle-upgrade.md）
- `Holder` — 角色 prefab 容器（`.tagCharacterPairs`）
- `CampaignSaveData.current` — 存档数据（`.BiomeIndex`）

### 枚举
- `PayableShop.ShopType` — 商店类型（Bow=0 ... Total=15）
- `PayableShop.UnsidedShopType` — 无方向商店（Pike, Ninja, ChangeRuler, Workshop, ShieldShop, ChangeItem）
- `Castle.Level` — 城堡等级（Castle1 ... Castle7）
- `Side` — 方向（Left, Right）
- `TechnologyAge` — 科技时代（None, Wood, Stone, Iron）
