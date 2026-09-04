# runtime-hotfix-002 — 工匠卡顿、忍者商店与乞丐补员

## Goal

消除 Hammer 转职时的同步资源扫描卡顿，修复忍者商店只警告但未实际重试的问题，
并将每个乞丐帐篷的 `spawnInterval` 临时设为 1 秒、最多 5 个乞丐；考虑原生每轮固定约 5 秒扫描，
可观察补员周期约为 6 秒。

## In Scope

- `il2cpp/PatchRoles_Worker.cs`：缓存 Worker prefab，保留交替语义但移除每次转职全资源扫描。
- `il2cpp/PatchRoles_Castle.cs`：在 ShopPlanner 稳定初始化后确定性重试忍者商店队列。
  显式传左右 Side，并移除希腊全商店 `CreateItem` 接管，恢复原生池化产出。
- `il2cpp/PatchRoles_BeggarCamp.cs`：每个帐篷 `spawnInterval=1f`、`maxBeggars=5`（实际约 6 秒补员）。
- IL2CPP 构建、独立 reviewer、仅部署独立测试副本。

## Out of Scope

- 不修改 Mono 历史线。
- 不启动游戏、不写 Steam、不修改共享存档。
- 不在没有新实测证据前发布最终 zip。

## Verification

- 最新日志中的 Ninja queue NRE 有对应确定性修复。
- Hammer 路径不再调用 `Resources.LoadAll`。
- `BeggarCamp.spawnInterval=1f` 与 `maxBeggars=5` 均按每个实例设置，不替换原生 SlowUpdate。
- IL2CPP Debug 构建 0 warning/0 error；独立 reviewer approved。
- 游戏退出后部署，构建与测试副本 DLL SHA-256 一致。

## Exit Criteria

- 用户实测 Hammer 卡顿明显消失或决定取消交替。
- 每个帐篷能约每 6 秒补员，最多保留 5 个乞丐。
- Castle5 希腊世界忍者商店成功排队/出现，日志无新增 mod 异常。
