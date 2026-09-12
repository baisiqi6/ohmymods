# log-hygiene-004 — 面板字体与运行日志降噪

## Goal

消除 IL2CPP 设置面板打开后由静态 `Zpix` 字体触发的持续 TextCore/IMGUI 报错，
并降低已经成功、但会在正常加载流程中反复输出的 mod 信息日志。

## In Scope

- `il2cpp/ModPanel.cs`：移除 `Resources.LoadAll<Font>("")` 与静态 `Zpix` 作为 IMGUI 字体的方案；
  采用不会触发字体转换报错的确定性回退，并保持面板可操作。
- `il2cpp/PatchEconomy_CurrencyBag.cs`：容量保障日志改为限流或 Debug，不改变容量逻辑。
- `il2cpp/PatchRoles_Castle.cs`：旧存档左右商店队列仍保持幂等规范化，仅降低重复成功日志噪音。
- IL2CPP Debug 构建、静态审查、仅部署独立测试副本。

## Out of Scope

- 不屏蔽 PlayFab/证书、原生商店选址、原生 UI BestFit 或场景卸载音频警告。
- 不改变忍者、钱包、商店、盾牌等既有行为契约。
- 不修改 Mono 历史线、Steam 正式目录、共享存档或当前发布 zip。

## Verification

- 源码不再全量扫描 Font，也不把 `Zpix` 注入 IMGUI skin。
- 面板代码编译通过，且无新增资源/运行时依赖。
- 重复成功日志被限流或降为 Debug，业务写入仍每次幂等执行。
- IL2CPP Debug 构建 0 warning/0 error；独立 reviewer approved。
- 游戏未运行时，构建产物与独立测试副本 DLL SHA-256 一致。

## Exit Criteria

- 新一轮实机打开/操作面板后，不再出现成千上万条 `Unable to find/load font ... Zpix`。
- 新日志中旧版忍者 ThrowingStar/Smokebomb 异常不再出现。
- PlayFab、原生选址等非 mod 噪音继续如实保留，便于诊断。
