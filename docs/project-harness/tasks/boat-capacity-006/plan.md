# boat-capacity-006 — 主船原生兵种扩容

## 目标

- 仅调整大船（主船）：独立弓箭手保持 4，工匠改为 8，骑士/侍从小队改为 6，长矛兵/重装步兵改为 8，农民保持 3。
- 小船（奥林匹斯 `FleetBoat`）全部保持原生配置。
- 按用户最终取舍，取消狂战士与忍者登船；不保留适配器、额外 RPC、跨岛 sidecar 或相关半成品。

## 安全边界

- 只在 `Boat.OnEnable` 原生注册槽位之前写入四个已有容量字段，继续复用原生登船、航行、换岛计数和下船链。
- 独立弓箭手容量仍由主船原生四个站位决定，不修改位置数组。
- 不 patch、不引用 `FleetBoat`，不改变小船容量或可登船类型。
- `ModConfig.Enabled=false` 时不写容量，保持原生值；已注册槽位不会在运行中反向重建，开关效果以下次主船启用为边界。
- 仅 IL2CPP 2.4.0；Mono、Steam 正式目录、共享存档与当前正式 zip 不在本任务修改范围。

## 验收

1. 静态确认补丁目标精确为 `Boat.OnEnable`，且只写 `maxWorkers=8`、`maxKnights=6`、`maxPikemen=8`、`maxFarmers=3`。
2. 独立弓箭手位置数组、小船及狂战士/忍者代码均无改动。
3. IL2CPP Debug 构建 0 warning / 0 error，独立 reviewer、`git diff --check` 与 checklist validator 通过。
4. 后续独立副本实测大船各原生兵种上限与超额拒绝；无实机证据前任务保持 doing。

## 当前状态

- 用户已取消新增狂战士/忍者乘客，相关半成品已全部移除。四个原生容量字段的最小补丁已完成，
  独立 reviewer 静态 APPROVED，IL2CPP Debug 构建 0 warning / 0 error；尚未部署和实机。
