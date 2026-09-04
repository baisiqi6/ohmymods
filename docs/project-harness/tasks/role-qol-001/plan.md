# role-qol-001 — 狂战士招募序列、隐士防绑架与隐士缩放

## 目标

- 工匠每成功使用 6 个普通 `BerserkerTool` 招募时，前 5 个生成普通狂战士，第 6 个生成长柄斧狂战士队长，然后循环。
- 所有隐士不再被怪物选作战利品或抓走，但不获得伤害免疫，也不改变其他 NPC/物品的抓取规则。
- 将当前版本中的酿酒师隐士（内部类型为 Baker）绝对缩放为 1.15 倍；只改变纵向统一缩放目标，保留朝向与能力。
- 将吹笛解锁、用于马厩升级的马厩隐士（内部类型为 Horse）绝对缩放为 1.10 倍；不误用 Horn 类型。
- 将号角隐士（内部类型为 Horn）绝对缩放为 1.15 倍；与Horse分别判别。
- 将希腊弩箭塔隐士（内部类型为 Ballista）绝对缩放为 1.20 倍；将骑士塔隐士
  （内部类型为 Knight）绝对缩放为 1.05 倍。
- 将恢复的希腊火焰塔隐士（内部类型为 Fire）绝对缩放为1.25倍；不改变其Passenger/Roaming、
  登船、建筑升级或存档状态。

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
- 酿酒师缩放只按 HermitType.Baker 判别，在 OnEnable/对象池复用时把 localScale.y 设为 1.15，并交给现有缩放注册器维持；真正 OnDestroy 时精确注销该 Mover 的 instanceID，避免同进程 ID 复用污染其他单位。不得按名称扫描资源、不得累乘、不得修改 x/z 或其他隐士。
- 马厩隐士只按`HermitType.Horse`判别并绝对设置y=1.10；号角隐士只按`HermitType.Horn`判别并绝对设置y=1.15；弩箭塔、骑士塔与火焰塔隐士分别只按`HermitType.Ballista`/`HermitType.Knight`/`HermitType.Fire`判别并绝对设置y=1.20/1.05/1.25。它们与Baker共用OnEnable登记/OnDestroy注销生命周期，其他隐士保持原样。
- 不触碰 Steam 目录和共享存档；游戏运行期间不覆盖独立副本 DLL。

## 验收

1. 独立 reviewer 静态批准两个补丁。
2. IL2CPP Debug 构建 0 warning / 0 error。
3. 独立副本连续招募 6 个狂战士，日志与实物顺序为普通×5、队长×1；第 7 个回到普通。
4. 换岛后序号继续；完整退出再启动后从第 1 个普通开始。
5. 贪婪靠近落地隐士时无法抓取，日志出现一次 `Prevented an enemy from kidnapping a hermit`；金币、宝石、狗、猫等原生抓取不受影响。
6. 无相关 `Error` / `Exception`，构建产物与独立副本 DLL 哈希一致。
7. 酿酒师生成、解锁、上下坐骑或对象池复用后保持 1.15 倍；左右朝向正常，其他隐士尺寸不变。
8. 马厩隐士生成、解锁、上下坐骑或对象池复用后保持1.10倍；号角隐士保持1.15倍；弩箭塔/骑士塔隐士分别保持1.20/1.05倍，其他隐士尺寸不变。
9. 火焰塔隐士上下坐骑、放下、读档和对象复用后保持1.25倍，x朝向正常；不改变其所有权与换岛规则。

## 当前交接

- 用户实测招募大量普通狂战士但未出现队长；同一运行日志里普通/队长池均注册成功，却没有任何
  `Berserker recruitment slot`，高置信证明私有 `TryPickupBerserkerTool` hook 未命中。
- 正在将来源判别、计数和第六次临时映射迁移到已被 Hammer 路径实机证明命中的公开
  `Character.Promote(DroppableTool, IUnitController)`。实现已完成并获独立 reviewer 静态 APPROVED；
  Debug 构建 0 warning/0 error，合并三槽灌木后的 DLL SHA-256=
  `88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`。后续包含全部候选改动的
  DLL SHA-256=`6E0C474B9D665CB2649F00071C2D02C09B44A0DACF3E49057D462E3D9EAE5AE0`，已仅部署独立副本且
  构建/部署哈希一致。用户复测确认狂战士序列没有问题，公开 Promote 修复的游戏内门禁通过；
  换岛延续/完整退出重置未单独留证。酿酒师 1.15 倍缩放与真实销毁注销已实现并获独立 reviewer
  静态 APPROVED；Debug 构建 0 warning/0 error，DLL SHA-256=
  `C4003C445EAC67037C1BD295BBAD7E21B8A68E00C3DA900037E26F0BF8C683E0`。游戏退出后已部署独立副本，
  部署哈希与构建一致；隐士防绑架和缩放都待实机。仍不修改 Steam/共享存档，不提前重打 zip。
- 后续新增 `HermitType.Horse` 马厩隐士绝对 y=1.10；独立 reviewer 静态 APPROVED。源码提交
  `82333a1` 推送后，从干净提交 Debug 构建 0 warning/0 error；确认游戏退出后只部署独立测试副本，
  构建/部署 DLL SHA-256=`BAF335AF932260819F01AAC3F9C93D4B3C4E1F22FF0FDA58075A8DE339E435D6`。
  尚未打包或实机，不把部署自动视为观感验收。
- 2026-08-16 新增 `HermitType.Ballista` 弩箭塔隐士绝对 y=1.20 与 `HermitType.Knight`
  骑士塔隐士绝对 y=1.05；沿用现有 ScaleRegistry 与 OnDestroy 注销，只写 y、保留 x/z。
  独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error。用户退出后已只部署独立测试副本，
  构建/部署 DLL SHA-256 均为 `8571E740D8CD4C94E5552D13B7CD1AC5D3124FF863733191257A864B4E92FB94`；
  尚未打包或取得观感验收。
- 2026-08-16新增`HermitType.Fire`绝对y=1.25并对称加入OnDestroy注销；独立reviewer最终APPROVED，
  Debug构建0 warning/0 error，DLL SHA-256=`7A75716A8748A09497314C7DAE32B1B760B81A1416520C7509C5BD958E691208`。
  游戏仍运行，尚未部署或打包；实机需观察乘骑时原生单位缩放被注册器恢复、放下与跨岛后的比例。
