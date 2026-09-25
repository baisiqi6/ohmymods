# 发布交接说明：武士燕返与商店选址修复

日期：2026-09-26。供原会话接续整理源码、版本账目和发布包；本会话未提交、推送或发布。

**当前状态：累计修复已安装到 E 盘测试副本。用户重新游玩后反馈“目前没啥问题了”。** 本次再次核对已安装 DLL 哈希与候选一致，最新游戏日志也出现了对应 build 横幅。这是当前单机游玩的整体反馈；联机、池化复生及高密度长时间压力场景未据此宣称全部验收。

## 一、可直接用于更新说明的内容

### 武士燕返修复

- 修复燕返被原生撤退、充能或编队状态中途打断的问题，武士可以正常完成出刀冲出、转身反斩和回到出发点的两段动作。
- 稳定往返目标：第一段以出发点为基准向前冲刺 7 格，第二段返回本次出发点，减少目标被其他移动指令覆盖造成的短冲、远退和距离紊乱。
- 修复回程结束后仍保持出刀/收刀姿势的问题，让动作正常收势并衔接待机或行走。
- 移除燕返之外额外触发的追随从回撤突进，避免技能结束后又冲到大后方；普通归队仍保留。
- 修复白色残影不明显的问题，正确生成保留角色轮廓的白色定格残影；去掉本 MOD 额外启用的连续白色拖尾，只保留白色残影效果。

### 火铳铺与英雄驿站选址修复

- 修复城墙内明明有空地，商店却因搜索范围过小而无法生成的问题。
- 商店改为在整片完整城墙内寻找合适位置，取消只搜索领地中点左右各 30 单位的限制，优先选择靠近领地中部的位置。
- 改善空隙检查，避免固定采样步长跳过足够容纳商店的空位。
- 继续检查完整店铺及枪架占地和游戏原生建筑避让条件；已有商店保持原位。火铳铺与共用选址逻辑的英雄驿站同时受益。

## 二、发布时应准确保留的行为边界

- **7 格是正常燕返的出程目标，回程目标是原出发点。** 仍通过原生移动执行，有到点容差及每腿 1.2 秒、总计 3 秒的保护；遇到阻挡、死亡或其他硬失效时会中止收尾。不要写成任何情况下都强制移动精确 7 格或传送回原位。
- 燕返原有伤害扫描、无敌窗口与硬失效清理保留。此次主要修复行程存续、动画收势、额外回撤突进和视觉表现。
- 白影采用当前角色姿态的真实 Sprite 网格/UV，保留 alpha 并将 RGB 烘焙为白色，兼容实际打包图集。当前仍为八槽、两秒淡出；没有引入新的历史动作图集。
- 商店搜索范围为左右 **intact 城墙边界**之间，完整占地必须落在范围内。火铳铺连侧枪架宽度仍为 5.5 单位。
- 原生 `OverlapsAnyExclusions(..., false)` 仍作最终判断。新算法只用预留边界划分候选，不把商人、坐骑等可忽略对象的范围直接当作不可用地块扣除，也没有取消树木或建筑避让规则。
- 商店仍保留原有创建重试、单机/功能开关/暂停条件、地面参照、付款和枪架逻辑；不会主动搬迁已经生成的店铺。

## 三、当前安装版本与源码入口

| 项目 | 当前值 |
|---|---|
| 构建标记 | `9.14.24-choreo-shopland-20260925` |
| DLL MD5 | `94CC219A57909EA19D4F366F84758DB6` |
| DLL SHA256 | `9E56BF4BB873DC68F7D65A5492D52196D76CB5D8E9F2A7E6231C512F636411C4` |
| 安装时间 | 2026-09-25 23:55，北京时间 |
| 独立工作树 | `C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix` |
| 分支 | `win/samurai-choreo-hard-validity` |
| 基线 HEAD | `67d39b9edb50a9aee3f536a00f7683a0c809a04e` |
| Git 状态 | 修复仍为工作树中未提交的改动，不能仅 checkout 此分支就认为已取得修复 |

构建产物：
`C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix/il2cpp/bin/Debug/KingdomEnhancedMod.dll`

已安装位置：
`E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll`

旧 DLL（8E994A98）备份：上述 DLL 路径后追加 `.20260925-235547.bak`。安装前后存档和配置哈希一致；安装过程未启动游戏、未写 Steam 正式目录。

**94CC219A 是本次完整累计候选。** 8E994A98 是前一轮只修行程中断与白影的旧版本；DED188D7 是随后武士收势/回程/去拖尾候选，其全部修复已包含在 94CC219A 内。不要从旧报告的候选路径或旧工作树重新打包覆盖当前修复。

## 四、原会话需要接收的改动

以下均相对于上述独立工作树根目录：

| 范围 | 文件 |
|---|---|
| 武士往返、收势、清理及普通归队 | `il2cpp/PatchRoles_SamuraiPowerDash.cs` |
| 白色姿态残影 | `il2cpp/SamuraiDashVisuals.cs` |
| 两店共用全领地选址、英雄店接线 | `il2cpp/HeroShop.cs` |
| 火铳铺选址接线与状态文案 | `il2cpp/MusketeerShop.cs` |
| 测试构建标记 | `il2cpp/KingdomEnhancedPlugin.cs` |
| 武士回归 | `tests/samurai-motion/Program.cs`、`Stubs.cs`；`tests/samurai-visuals/Program.cs`、`Stubs.cs` |
| 商店选址回归 | `tests/hero-shop/Program.cs` |
| 项目记录 | `docs/project-harness/progress.md`、`domain-model.md`、`game-logic-map/patch-patterns.md`，以及下述两个任务目录 |

另有三份来自前序诊断的既有未跟踪文件，随当前测试 DLL 保留，但不是本轮新增功能：
`il2cpp/PatchDiag_PayShop.cs`、`il2cpp/PatchDiag_ShopQueue.cs`、`il2cpp/PatchDiag_StuckSamurai.cs`。
原会话应明确它们在正式发布包中的处理方式，避免直接 `git add .` 将诊断器、日志或 DLL 备份一起提交。若移除诊断器并重建，正式 DLL 的哈希会变化，不能继续沿用本报告的候选哈希作为正式包校验值。

原主树 `C:/Users/ADMIN/projects/ohmymods` 中既有其他工作及 progress/checklist 合并冲突未被本会话清理；请从上述独立工作树提取并核对改动，不要 reset/clean 原主树，也不要从旧树直接覆盖当前源码。

## 五、验证与证据

| 验证组 | 结果 |
|---|---|
| 武士最终回归 | motion 189、visuals 31、night-formation 24、archer-night-band 32、retreat 43、diagnostics 9，合计 328/0 |
| 商店相关检查 | HeroShop Core 67、Musketeer shop 18、side-rack 60、production wiring 35，合计 180 项通过 |
| 实际游戏 2.4 接口与完整 IL2CPP 构建 | 通过，0 警告、0 错误 |
| 独立审查 | 武士运动/视觉、第二轮收势/距离及商店最终实现均有 GLM 5.3/max APPROVE 记录 |
| 累计候选保留检查 | 商店构建前后，受保护的武士运动/视觉及运动测试源码 SHA256 保持一致 |
| 实机反馈 | 用户先确认真实往返与白影；安装完整累计候选后，于 2026-09-26 反馈目前没有明显问题 |

328 是武士最终套件结果；180 是商店相关检查，不能再把第一轮历史 355 项重复相加。musketeer-shop 独立测试项目存在 4 条既有 stub CS0649 警告，与完整产品构建 0W0E 分开记录。

证据目录（工作树根下）：

- `docs/project-harness/tasks/samurai-slash-guard-20260925/`：行程硬失效与白影实现、审查及第一轮验证。
- 同目录 `live-feedback/`：PowerSlash/Land 实际控制器证据、第二轮实现、328 项回归、最终复审和旧候选备份。
- `docs/project-harness/tasks/shop-territory-placement-20260925/`：选址设计/验收、`candidate.json`、`events.md`、`receipts/implementation-review.md`、测试日志。
- 商店任务 `receipts/install.json`：94CC219A 安装、旧 DLL 备份及存档/配置不变回执。

只读 EXharness 校验另有 15 条历史 schema 错误，来自旧任务缺 `handoff`/`verification` 等字段；本次没有修改 checklist，也未声称全项目 harness 校验通过。

## 六、发布接续提示

1. 先核对上述工作树的未提交 diff，将武士与商店这两组修复完整纳入发布源码，保留既有其他功能。
2. 按仓库 `VERSIONING.md` 和原会话的完整待发布批次统一定版本。武士同一功能的白影、收势和距离返修要去重，不能每轮候选重复计数；此处 build 标记不是新正式版本或已发布标签。
3. 正式版本号/日志标记调整或诊断器处理后，重新构建和核对正式包、测试与哈希；本次证据不替代正式发布包自身的验证。
4. 发布说明可采用本文第一节。保留联机、池化复生和密集场景压力测试未专项验收的边界；用户当前单机反馈良好，不必继续写成完全未游玩。

本次交接仅提供发布材料，没有向原会话自动发送消息，也没有执行 commit、push、tag 或发布。
