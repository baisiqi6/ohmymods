# ninja-runtime-003 — 希腊忍者运行时修复、草丛伏击与缩放

## 目标

- 修复跨世界迁移后忍者因 `ThrowingStar` / `Smokebomb` 对象池缺失而中断攻击与昼夜状态机的问题。
- 复用原生 `Ninja.GetHidingSpot`、占用与昼夜逻辑，让希腊世界城墙外的成熟草丛可作为忍者伏击点；
  每个宽灌木生成 3 个左右错开的独立锚点，允许最多 3 名忍者分散蹲守。
- 忍者夜行攻击形态 y 缩放为 1.1；白天钓鱼形态恢复 1.0。
- 仅在希腊世界将银行家 y 缩放为 1.075；全部沿用 `ScaleRegistryHolder`，不改 x 朝向。

## 已确认根因

- 最新独立副本 `Player.log` 反复记录 `Pool not found for ThrowingStar`，随后 `Ninja.ThrowStar()` 空引用。
- 同一日志记录 `Pool not found for Smokebomb`，随后 `Ninja.SmokebombRoutine()` 空引用并向上中断 `Ninja.Behaviour`。
- 原生伏击点不是按“竹子”名称查找，而是读取 `Kingdom.GetHidingSpotList(side)`；希腊 `Grass` 本身不带 `HidingSpot`，应只在实际生成的成熟 thicket 实例上幂等补组件，不能给每片 Grass 建点。

## 边界与约束

- 仅 IL2CPP 发布线；Mono 冻结。
- 两个依赖池必须在忍者使用前、固定顺序、两端确定性注册；手里剑按原生网络对象语义处理，烟雾按本地视觉池处理。
- 不用 `ThrowStar` / `SmokebombRoutine` 热路径临时建池，不做每帧资源扫描。
- 不重写 `Ninja.Behaviour`，不强制昼夜切换；修复异常后由原状态机恢复攻击、隐身退出和钓鱼形态。
- 草丛补点只限 Greece 的实际 thicket；每个 thicket 使用 3 个命名子锚点而不是共享一个坐标。
  每个 `HidingSpot` 仍保持单人占用，原生 `GetHidingSpot` 继续负责城墙外过滤；thicket 池禁用时
  三个锚点都必须注销并通知各自忍者，复用时清除旧占用并仅补回缺失注册。
- 不改变忍者隐藏期间的原生 `DamageSource`、敌人目标选择或攻击数值。
- 不触碰 Steam 和共享存档；仅在游戏退出时覆盖独立测试副本 DLL。

## 验收

1. 独立 reviewer 静态批准池注册、草丛伏击和两个缩放补丁。
2. IL2CPP Debug 构建 0 warning / 0 error。
3. 独立副本日志显示 `ThrowingStar`、`Smokebomb` 池各成功注册且无同名重复/SyncID 冲突。
4. 忍者连续完成多轮手里剑、近战与烟雾撤退，无 `Pool not found`、`Ninja.ThrowStar` NRE 或 `SmokebombRoutine` NRE。
5. 夜晚同一成熟灌木最多可让 3 名忍者占用三个错开的独立伏击点；同一锚点不被多人共享，
   草丛消失时三名忍者都能解绑，池复用不重复注册、不保留旧占用。
6. 天亮后忍者恢复钓鱼形态与正常可攻击状态；夜行形态 y=1.1、钓鱼形态 y=1.0。
7. 希腊银行家 y=1.075，其他世界银行家不受影响，所有缩放只改 y。
8. 构建产物与独立副本 DLL SHA-256 一致；不提前重打发布 zip。

## 当前交接

- 根因、对象池、草丛伏击与缩放实现均已完成；最终独立 reviewer 静态 APPROVED，Debug 构建 0 warning / 0 error。
- 三槽灌木与狂战士公开 Promote 修复合并候选已仅部署独立测试副本，构建/部署 SHA-256=`88CE41D4D27C21F0B7BDB1D90A1286F9A0FAF1964225338E8487F7FD90B3821F`；等待用户实测，不启动游戏、不重打发布 zip。
