# release-hardening-001 — IL2CPP 发布阻断修复与交接整顿

## Goal

消除独立测试副本中 `EquipShieldIfNorselands` 触发的 `NpcShieldUser.SetShieldEnabled` 空引用，
收紧锤子交替转职的状态边界，并恢复可审计的 harness、IL2CPP 构建和发布打包流程。

## In Scope

- `il2cpp/PatchRoles_Worker.cs`：盾牌异常与交替转职。
- `docs/project-harness/`、`AGENTS.md`：当前架构、状态、验收和交接边界。
- `pack-il2cpp.bat` 与发布包：根目录直装结构、必需 runtime、DLL 哈希与清单。
- IL2CPP 编译；只允许部署到明确列出的独立测试副本。Mono 为冻结历史线，不是本任务门禁。

## Out of Scope

- 不启动游戏，不代替用户执行游戏内行为测试。
- 不修改 Steam 正式版目录。
- 不修改或恢复共享存档。
- 不把“编译成功”或“DLL 已复制”表述为游戏内验收通过。

## Authority Boundaries

- 可修改主仓库文件。
- 可读取开发环境和独立测试副本日志、配置与二进制。
- 可将验证过的 DLL 复制到
  `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/` 的插件目录。
- 禁止写入 `D:/Steam/steamapps/common/Kingdom Two Crowns`。
- 发布 zip 只在静态审查和 IL2CPP 构建通过后生成；仍需用户实机验收才能发布。

## Steps

1. 保存当前 Git、DLL 哈希、zip 清单和最新独立副本日志作为基线。
2. 修复盾牌对象所有权/初始化问题；让交替状态仅在有效转职路径推进，避免污染全局 Worker 映射。
3. 独立 reviewer 核对 Harmony hook、对象池、分屏/场景状态及回归风险。
4. 将旧 checklist 迁移到 EXharness 当前 schema，并登记未完成的发布门禁。
5. 统一 Mono 自用线与 IL2CPP 发布线的 scope、architecture、domain model 和 runbook。
6. 修正打包脚本，执行 IL2CPP 构建，核对产物哈希和 zip 目录结构。
7. 仅部署独立副本，输出用户实测矩阵；等待实机结果后才能 closeout。

## Verification

- EXharness checklist validator 通过。
- IL2CPP `dotnet8 build -c Debug` 退出码 0、零警告。
- reviewer 给出结构化 verdict。
- 独立副本 DLL 与构建产物 SHA-256 一致。
- zip 内 DLL 与构建产物 SHA-256 一致，根目录结构和 runtime 清单通过。
- 用户实测前任务保持 `doing`，不得标记 `done`。

## Exit Criteria

- 所有静态、构建、打包与部署证据已记录。
- Steam 目录未改动。
- 剩余游戏内测试被拆成明确步骤，包含盾牌、交替转职、容量、分屏和北境世界。
