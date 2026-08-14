# ninja-placement-002 — 忍者商店左右队列修复

## 目标

修复 IL2CPP 忍者商店队列中 `NinjaLeft` 被错误记录为右侧的问题，并修复当前存档里已经存在的旧队列条目；不改变原生商店占地、科技年龄或排斥区规则。

## 证据

- `LogOutput.log` 已记录 `NinjaLeft`、`NinjaRight` 成功入队，无队列 NRE。
- `Player.log` 反复进入 `AttemptPlaceShop`，但两者搜索起点始终相同且位于右边界。
- 狂战士商店已出现，说明 ShopPlanner、跨 biome prefab 与对象池主链路可用。

## 实施

1. 新入队使用显式 `Nullable<Side>(Left/Right)`，不依赖 native helper 往返包装。
2. ShopPlanner 初始化后规范化现有忍者队列中的 `shopSide`，并重新触发规划。
3. 独立 reviewer 检查 IL2CPP 字段、队列幂等性和网络 authority 边界。
4. Debug 构建；仅在游戏退出后部署独立测试副本并比对 SHA-256。

## 验收

- 构建 0 error；reviewer 批准。
- 新日志中 `NinjaLeft` 从左边界搜索，`NinjaRight` 从右边界搜索。
- 至少左店可在合格空地建成；若右店仍失败，保留原生队列并继续诊断 Stone 科技覆盖、占地或排斥区。
- 不触碰 Steam 正式目录和共享存档。
