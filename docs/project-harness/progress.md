## 2026-08-12 — 2.1.0 两个 bug 修复（乞丐拾取 + 友好巨魔永久控制）

## 2026-08-13 — IL2CPP 迁移（Steam 2.4.0 发布线，用户拍板）

- 决策：发布受众 = Steam 正版玩家 → BepInEx 6 + Il2CppInterop 迁移（scope.md 更新为执行中）。
- M0 骨架：il2cpp/KingdomEnhancedMod.csproj + Plugin + ModConfig（BepInConfig 替代 UMM Settings），零错误部署验证。
- 三 worker 并行迁移：经济域（CurrencyBag/Banker/ShopPlanner/SidedShop）、角色域（Holder/Castle/Knight/Character/Worker/World/BeggarCamp）、
  世界战斗域（Mover/Construction/Level/Kingdom/EnemyManager/Artemis/HermesStaff/FriendlyTroll）——全部零错误零警告。
- 关键漂移：Mover"漂移"是 get_type_members.py 正则 bug 误报（unsafe 方法漏报），实际无漂移；其余漂移（BagCurrency.Reset→ResetVisuals、
  Wallet 多币种、ShopType 重排、Level.GenerateInternal+seed 等）已适配并记录待冒烟验证（notes-*.md 共 14 项）。
- Mono 侧池修复经 HotfixReviewer 抓 P0（syncID=119 跨biome冲突每帧 NRE）+P1（根因误判：真根因是读档恢复先于 InitPools）
  → 重写为 SpawnGO 池缺失兜底，部署 GOG 2.1.0。
- 实机验证：E:/QQ 2.4.0 加载 KingdomEnhancedMod v2.4.0 成功，零错误零异常。
- 发布包：release/KingdomEnhancedMod_v2.4.0_IL2CPP.zip（7.6MB，doorstop+BepInEx core+插件+配置+安装说明，开箱即用）。
- 待办：MigrationReviewer 交叉审核中；14 项待决策需游戏内冒烟验证。

- 乞丐拾取：根因链 扔金币→乞丐捡→Promote("Peasant")→UpgradeTransitionFX→Sparkles 池缺失
  （2.1.0 InitPools 只注册当前 biome 池资产）→NRE→拾取中断。修复：RegisterAllBiomePools
  全 biome 池去重补注册（Patch_PoolManager）。
- 友好巨魔：2.0.1 ShouldRevertToTroll 恒 false（原生永久），2.1.0 改为 `_expirationTime <= Time.time`
  （_duration=5f）——补 prefix 强制 false 实现永久控制（Patch_HermesStaff）。
- checklist feature-002/003 已登记；HotfixReviewer 交叉审核中。

## 2026-08-12 — 赫尔墨斯钱袋三件套（精细化改造第一项）

- 解锁：开局强制 `ChangeCurrencyBag(Hermes, 0/1)`（Patch_CurrencyBag，OnGameStartHandler postfix）。
- 扩容：`ChangeCurrencyBag` postfix 按类型设 `Player.wallet.TotalCapacity`（Hermes 2000 / Bag 1000，
  每局重设幂等——TotalCapacity 非持久字段）。
- UI：`BagCurrency.Reset` prefix 视觉堆叠上限 300→600；`CurrencyBag.Awake` postfix 整体放大 1.3x
  （金币堆子物体继承）。
- 机制澄清（防后人重踩）：游戏**没有"钱袋容器碰撞空间"**——容量是数字（Wallet.TotalCapacity），
  拾取靠金币×玩家物理碰撞重叠 + 点击 OverlapCircle，钱袋是 HUD 视觉对象。
- 待用户实测：钱包 2000 上限、堆叠 600、视觉放大效果。


# ohmymods — 进展

## 2026-08-12 — arch-002 收尾（命名对齐 + Probe 裁剪 + 文档同步）

- 命名对齐：Patch_Shop.cs → Patch_ShopPlanner.cs、Patch_Enemy.cs → Patch_EnemyManager.cs
  （Main.cs 注册名同步更新，maint-002/003 done）。
- Patch_Probe.cs 已删除，不再注册（maint-002 done）。
- build.bat 通配化（`for %%F in (Main.cs Patch_*.cs)`）+ 编译成功自动部署到 Mods/MyMod（maint-003 done）。
- 文档同步（Worker B）：architecture.md 模块清单按最终态重写（商店注册为 Prefix 全量替换）、
  domain-model.md 关闭 R3/R4 + 新增 D8（速度倍率 SetGoal 入口/地图幂等/银行家补员删除原因/Enabled 契约统一）、
  biome-asset-system.md / unit-spawning.md 的商店注册描述改 Prefix、unit-spawning.md 自洽方案标注废弃
  （指向 patch-patterns.md 坑10）、MOD开发文档.md 归档到 docs/legacy/。
- 剩余：Mover.Update 双 postfix 合并、Main.OnGUI 反射缓存（Worker A）。

## 2026-08-12 — GOG 2.1.0 迁移完成（Mono 最后版本）

- **注入方案**：UMM 21.0.32 自带 winhttp（旧 UnityDoorstop）不识别 Unity 2022.3.51f1 →
  改用 BepInEx 5.4.23.3 的 winhttp（x86）+ `[General] target_assembly=` 格式配置指向
  UnityModManager.dll（详见 runbook "注入方案"）。
- **API 差异修复（4 处）**：
  1. `Pool.syncID` int→short（Patch_Castle 显式转换）
  2. `EquipShield NRE`：NpcShieldUser.Awake 在 HasWorldAuth 未就绪时提前 return → regenWait
     为 null → 装备前反射补初始化
  3. `Worker.OnTriggerEnter2D` 新增 npcShieldUser==null 早退 → 希腊工人无法拾取
     BerserkerTool → 狂战士商店卡死；OnEnable 补组件+回填字段（EnsurePickupCapability）
  4. 其余 21 项 patch 目标 2.1.0 验证全部存在，零 not found
- 2.1.0 反编译源码入库 `game-source/Assembly-CSharp-2.1.0/`。

## 2026-08-12 — 架构交叉审查 + P0/P1 修复

- ArchReviewer（kimi K3）审查结论：**无需框架级升级**（单 DLL + Patch 类 + harness 骨架对 19 patch 规模合适）。
- **P0 修复**：① Patch_Mover 速度倍率写错字段（_moveSpeed 被 _goalSpeed Lerp 覆盖，从不生效）→ 改 patch SetGoal/SetGoalSpeed/SetGoalNoHaglet 入口缩放 speed 参数，幂等无累积；② Patch_Kingdom 地图倍率非幂等（Init+每岛加载指数放大 4→8→16→32）→ 基准值缓存幂等设置。
- **P1 修复**：③ Main.Enabled 契约统一（Patch_PoolManager/SidedShop/WorkerScale 补检查）；④ 银行家"5 个"补员删除——Banker.Awake 硬编码 NetID 903 唯一，克隆无法注册网络且与去重自相矛盾（每 120 帧 Instantiate/Destroy 刷屏）；共享银行增强保留。
- Info.json GameVersion 1.1.4→2.0.1、Version→1.1.0。
- 剩余 arch-002：Probe 裁剪、命名对齐、文档同步、双 postfix 合并、build.bat 通配化+部署脚本化、OnGUI 反射缓存。

## 2026-08-12 — kingdom-mod skill 迁入

- 原 `.omp/skills/kingdom-mod/`（6 文件）全部迁入 `docs/project-harness/game-logic-map/`。
- 链接改为相对路径；功能清单更新到当前状态（狂战士 hack 已退役、Patch_Mover 确认为速度倍率、新增坑 11/12）。
- 原 skill 已删除；`maint-001` 核实完成（Patch_Mover 是玩家速度倍率，保留）。

## 2026-08-12 — harness 实例化

### 已完成（核心功能全部就绪）
- 狂战士/忍者：希腊世界商店原生生成（槽位劫持 12/13），hack 退役。
- 北境形象：Worker/Peasant 的 tagCharacterPairs 替换 + sync 池注册。
- 北境工匠出生带盾（SetShieldEnabled，绕过无盾牌商店的缺口）。
- 单位缩放：y 轴守护机制（OnEnable 登记 + Mover.Update postfix 恢复），
  北境工匠 1.175 / 北境居民 1.125 / 希腊工匠 1.075 / 狂战士 1.2 / 鹿 0.55 / 小动物 1.8。
- 性能清理：删除每帧 FindObjectsOfType 兜底（ScaleAllWorkers），零每帧扫描。
- 地图扩展、希腊猫生成。

### 验证状态
- 每次改动后 build.bat 编译通过（csc.exe，C# 5）。
- 游戏内实测：盾牌可见 ✓、缩放生效 ✓（多轮调参 1.3→1.175 / 1.2→1.125）。
- 待测：清理后的完整回归（狂战士/忍者购买、读档恢复、缩放一致性）。

### 风险
- R1：存档携带 localScale.y（Serializer 写完整 transform）——卸载 mod 后旧档尺寸可能不符。
- R2：狂战士（Berserker）无缩放登记，转化后回 1.0（当前意图）。
- R3：Patch_Mover.cs 旧方案遗留待清理。
- R4：Patch_Probe.cs 调试日志待裁剪。

### 下一步（按 checklist）
1. maint-001 ✅（Patch_Mover 已核实为玩家速度倍率并修复，见 D8）。
2. maint-002 ✅（Patch_Probe.cs 已删除，arch-002）。
3. maint-003 ✅（build.bat 通配化 + 自动部署，arch-002）。
4. 完整回归测试。
