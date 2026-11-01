# Issue 318 — Windows 保存失败诊断候选

用户授权：先交独立诊断懒人包供玩家复现；确认根因后再修复、提 PR。不得损坏或猜测恢复存档和购买记录。本阶段不创建修复 PR、不合并或更新公开发行资产。

Issue：https://github.com/baisiqi6/ohmymods/issues/318
Owner：Mac Max / Codex；分支 `codex/windows-save-diagnostics-318`，集成起点 `cb467d4c1a23474f9678794bb42c3f12cfd4b5cf`。

## 事实与待证

玩家材料中主档原生挑战目录 5 项、英雄文档 6 项；提供日志存在英雄身份不可用和原生保存失败弹窗栈。发行版存在英雄/银行 Prepare 拒绝后未输出具体原因的分支。磁盘检查点不能代替失败当次的实时输入，根因尚未确认。配对备份与主档字节不匹配，不自动覆盖恢复。

## 范围与契约

- 从公开 Windows 11.1.0 source-recipe 的 287 源文件、38 资源、162 Windows 2.4.3 引用重建。源 ZIP SHA256 `bbe134e5f9f12d2a583a2c81f672616b5794a7c72d1455019419b6ad9439eca7`；不使用主会话性能 WIP。
- 在既有 HeroNativeRights、SharedBankNative、MusketeerNativeSave 入口加入条件诊断；新 leaf 只做有界、去标识、立即落盘的日志。无新原生 hook、池/RPC、存档迁移或保护绕过。原始短路顺序、getter 次数、Prepare 调用、回调次数及返回结果保持。
- `SAVE_FAILURE_DIAGNOSTICS` 关闭时原有业务 IL 必须等价；开启时差异仅为声明的诊断方法与 build 标识。异步 gate 在 `first=false` 时仍执行原始 Prepare；scope 只包 Prepare，不跨回调。
- 专用日志不输出 GUID、原始档案、路径、金额或异常消息；单锁、IO 失败熔断、有界容量/去重。资料收集工具须包含该日志。
- 完整私有懒人包来自已核对干净全量 ZIP，独立目录，不内置个人数据；游戏体和未改运行时保持。启动前退出游戏、备份原生 Release 与 Mod 配置，首次只从校验后的备份导入旧 Mod 配置；不覆盖已有目标，不写或恢复原生存档。以后备份并使用诊断目录的最新配置。BepInEx.cfg 不继承、不编辑，专用日志独立刷盘。

## 分工与验证

北京时间周末时段由 OMP DeepSeek Flash max 分别实现日志和 PS5.1 启动器；Operator 统一注册、构建、包装并审查。决策审查 GLM 5.3 max 的 C1–C8 已并入任务契约。

1. 独立重建公开基线，比较 IL 与资源；记录真实构建输入。
2. 在副本和合成边界测试日志、目录计数诊断、原始 gate 行为与回调次数；独立源审。
3. Windows PowerShell 5.1 合成目录验证备份、首次导入、重复启动、错误中断和 Unicode；不启动游戏、不写真实用户数据。
4. 编译独立候选，核对变更范围、38 资源与固定运行时；组装完整懒人包、读回逐项哈希。
5. 交用户转交玩家复现；根据首拒原因与当次计数追踪状态产生处，再进入根因修复与 PR 阶段。

构建、自动测试、源审、包装与玩家原生实测分别记账，不以构建成功宣称保存问题已修复。原始证据及候选过程仅保存在本机 `.local/tasks/player-win243-save-failure-20261031/`，不公开玩家存档、原始日志或游戏体。
