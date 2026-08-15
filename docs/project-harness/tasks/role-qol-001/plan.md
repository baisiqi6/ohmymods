# role-qol-001 — 狂战士招募序列与隐士防绑架

## 目标

- 工匠每成功使用 6 个普通 `BerserkerTool` 招募时，前 5 个生成普通狂战士，第 6 个生成长柄斧狂战士队长，然后循环。
- 所有隐士不再被怪物选作战利品或抓走，但不获得伤害免疫，也不改变其他 NPC/物品的抓取规则。

## 边界与约束

- 仅 IL2CPP 发布线；Mono 保持冻结。
- 狂战士序列只计算 world-authority 下，带 `Worker` 组件的 active `Character` 使用未拾取、active
  普通 `BerserkerTool` 后实际返回匹配角色的 `Character.Promote(DroppableTool, IUnitController)`。
- 禁止依赖私有 `Worker.TryPickupBerserkerTool` Harmony hook：实机已证明原生内部调用绕过其
  IL2CPP runtime-invoke thunk，导致整个序列 no-op；公开 Promote 是锤子交替已实机证明命中的稳定入口。
- 购买、未拾取/失败转职、读档/对象池生成、`BerserkerLeaderTool` 原生升级不计数。
- 序列保存在当前游戏进程中，换岛继续；完整退出游戏后重置，不写 `PlayerPrefs`，避免跨存档持久污染。
- 第 6 次只在同步转职调用栈临时替换 Holder 映射，Postfix/Finalizer 必须恢复。
- 隐士只覆盖敌人拾取判定；不修改 Damageable、移动、乘骑、建筑升级或存档状态。
- 不触碰 Steam 目录和共享存档；游戏运行期间不覆盖独立副本 DLL。

## 验收

1. 独立 reviewer 静态批准两个补丁。
2. IL2CPP Debug 构建 0 warning / 0 error。
3. 独立副本连续招募 6 个狂战士，日志与实物顺序为普通×5、队长×1；第 7 个回到普通。
4. 换岛后序号继续；完整退出再启动后从第 1 个普通开始。
5. 贪婪靠近落地隐士时无法抓取，日志出现一次 `Prevented an enemy from kidnapping a hermit`；金币、宝石、狗、猫等原生抓取不受影响。
6. 无相关 `Error` / `Exception`，构建产物与独立副本 DLL 哈希一致。

## 当前交接

- 用户实测招募大量普通狂战士但未出现队长；同一运行日志里普通/队长池均注册成功，却没有任何
  `Berserker recruitment slot`，高置信证明私有 `TryPickupBerserkerTool` hook 未命中。
- 正在将来源判别、计数和第六次临时映射迁移到已被 Hammer 路径实机证明命中的公开
  `Character.Promote(DroppableTool, IUnitController)`。实现已完成并获独立 reviewer 静态 APPROVED；
  Debug 构建 0 warning/0 error，合并三槽灌木后的 DLL SHA-256=
  `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。后续包含全部候选改动的
  DLL SHA-256=`6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`，已仅部署独立副本且
  构建/部署哈希一致。用户复测确认狂战士序列没有问题，公开 Promote 修复的游戏内门禁通过；
  换岛延续/完整退出重置未单独留证。当前组合任务只剩隐士防绑架实测，仍不修改 Steam/共享存档，
  不提前重打 zip。
