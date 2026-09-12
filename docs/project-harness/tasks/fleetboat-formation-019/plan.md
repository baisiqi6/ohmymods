# fleetboat-formation-019 — 城墙旗帜动态小船编队

## 目标

- 玩家在城墙外支付两枚金币举旗时，不再固定只招募一艘小船，而是招募当前旗帜侧全部可用的 `FleetBoat`，数量自然为0～4。
- 小船归属以2.4原生 `FleetBoat.Side` 为准；原生在一侧目标失效后迁移Side时，本补丁不改Side，只读取迁移后的结果。
- 所有小船继续使用原生招募、载员、跟随、攻击、返航、存档、RPC与PositionSync逻辑。

## 原生证据

1. `PayableBorderBanner.OnPayHandler`调用`Player.ActivateFormation()`；玩家的`Formation`负责招募并定位编队单位。
2. 2.4 `resources.assets`中Player Formation的原始`unitTypes`为：
   `FleetBoat, Gap, Gap, Archer×4, Gap, Pikeman×4`，只有一个小船槽。
3. 同一资源的`UnitSpacing[FleetBoat]=0`、`UnitSpacing[Gap]=0.34375`、`maxRecruitDistance=0`。直接复制船槽会重叠。
4. `FleetBoat.Side`、`_prevSide`和`NeedsToRefreshSide`属于2.4原生迁移链；`FleetBoatSaveData`只保存BoatNumber、状态和目标，不保存本补丁数据。
5. `Formation.RegisterUnit`从数组尾部选择匹配空槽；`FleetBoat.InFormation`使用`Formation.GetXPosForUnit`持续跟随各自槽位。

## 实现契约

1. Activate Prefix首先不受Enabled或authority限制：若命中旧profile且formation inactive、units全空，先做纯本地baseline恢复；恢复完成后才检查Mod Enabled、world-authority、Game.Playing及当前Managers/Kingdom/World，决定是否重新扩展。客户端保留原阵列并继续只接收原生FleetBoat状态与PositionSync。
2. `Player.ActivateFormation` Prefix只处理尚未enabled、`unitTypes/units`非空且等长、所有units为空的PlayerFormation。每个Formation按instanceID+native Pointer首次深拷贝原始`unitTypes`与`UnitSpacing`，绝不把扩展结果二次捕获为baseline。
3. Prefix以玩家位置计算requestedSide，再从当前`Kingdom.FleetBoats`筛选：Pointer唯一、当前world/gameLayer、active/enabled、`Side==requestedSide`、无Formation、BoatNumber在1～4且唯一、FSM状态允许加入、`CanJoinFormation(PlayerFormation, requestedSide)`与IsAccessible均通过。任何身份异常或数量大于4时fail closed并保留原版布局。
4. 在原唯一FleetBoat槽位置原序展开N个FleetBoat槽；N=0时把原槽改为一个Gap。其他原槽逐项保持顺序。构造新的`Il2CppStructArray<UnitTypes>`和等长空`Il2CppReferenceArray<IFormationUnit>`，全部构造成功后才原子赋值。
5. N=1保留原生FleetBoat spacing=0；N≥2时克隆`UnitSpacing`并只把FleetBoat项绝对设为1.0。船位因此为0/1/.../(N-1)；四船最后一艘相对第一艘3格，后续兵线相对原版最多约后移4格，为船体留出空间。N=0会因Gap令后续兵线约后移0.34375，接受这一轻微布局变化以阻止跨侧招船。
6. 原方法返回后核对`formation.side==requestedSide`，再按BoatNumber稳定顺序逐只重验并调用公开原生`TryRecruit(formation)`。原生RecruitRoutine首个动作是等待一帧，因此显式招募先完成。逐船异常局部捕获并限流记录，不中断剩余清理。
7. Activate patch使用Finalizer及本地try/finally：若原方法或Postfix异常且尚无船成功注册、units全空，恢复baseline `unitTypes/UnitSpacing`与fresh units；若已有任一成功注册则绝不覆盖非空units，保留profile交给正常Deactivate清理。无论逐船招募是否异常，finally都把所有仍为空的本轮FleetBoat槽（包括原槽对应位置）立即改成Gap，避免裸露空槽被原生5秒Recruit跨侧填充。
8. `Formation.UnregisterUnit` Prefix记录本轮reserved槽，Postfix在原生确实清空该槽后立即把FleetBoat改Gap；Harmony命中记录一次canary。0.5秒轻量协调器只遍历活动profile的最多4个reserved索引作竞态兜底，不扫描场景、不停止原生RecruitRoutine。
9. `Formation.OnDisable` Postfix在原生逐个Unregister完成、units全空后幂等恢复原始`unitTypes`、`UnitSpacing`和等长全空units。由于Unity OnDisable native hook仍需实机canary，协调器和下次Activate Prefix也会在inactive+units全空时先恢复再重建。
10. Disabled或失权时不招募、不热缩活动阵列；仅允许在formation inactive且units全空时做纯本地baseline恢复。场景切换时，同一Formation仍有效则先恢复后移除profile；已销毁/Pointer失效才直接丢记录。
11. 两名玩家分别保存profile与激活快照。同侧第二面旗继续由原生PayableBorderBanner阻止；异侧可分别招募，先完成的TryRecruit会令对应船`HasFormation=true`，另一侧重验后不会重复使用。

## 明确不改

- 不修改FleetBoat.Side、BoatNumber、FSM、Mover、载员容量、攻击、返航、持久化数据、NetID、RPC或对象池。
- 不新增小船、不删除小船、不修改四艘所有权恢复与泊位归位补丁。
- 不改变旗帜价格、士兵槽位种类或原生五秒补员协程。

## 验收门禁

1. IL2CPP Debug禁部署构建0 warning/0 error，diff-check通过；无游戏进程时才部署独立副本。
2. 只解锁1/2/4艘时，举对应侧旗分别最多招1/2/4艘；N=0不从另一侧拉船。
3. 正常两侧各2艘时只招旗帜侧2艘；原生Side迁移后同侧4艘可全部加入。
4. 两船间距约1；四船位置按槽位错开，后续兵线最多约后移4格；靠FormationBlocker/城墙边不叠船、不挤进墙体。
5. 单船异常Disable/离队后`Formation.UnregisterUnit` canary命中，空槽立即变Gap；协调器无跨侧误招。
6. 解除旗帜后所有船走原版ReturningToBase，formation恢复原始一船槽和原UnitSpacing；关闭Mod后下一次举旗恢复原版单船。
7. 分屏同侧不能重复举旗，异侧各自招各自船；在活动编队状态下存档退出再读档时按原生进入非活动编队，且无残留profile、扩展数组或孤立`_currentFormation`；换岛、死亡新君主同样完成清理。
8. 联机验证客户端FleetBoat仍不注册进Kingdom列表、只由authority招募，位置/状态同步一致；无unknown pool、duplicate syncID、RPC或Formation异常。

## 当前状态

- 2.1逻辑、2.4 interop签名与Player Formation资源数组已完成只读核对。
- 计划与实际代码均已获独立reviewer批准；IL2CPP禁部署Debug构建0 warning/0 error，源码
  SHA-256=`8550F056A982A7FAD570EBBC77929F65C99957F1F86993BC9D5D19DF66CEFCDF`，DLL
  SHA-256=`3595BEB72A7CD30871FD778F7F7FCCFBD6ED6AF36C9181AC1BFF634DBD54B3F3`（192,000 bytes）。
- 游戏进程为0后，该DLL已只部署到E盘独立测试副本，并保留部署前备份；当前仍为测试候选，等待
  验证1/2/4船、N=0、单船离队、分屏/联机以及
  `UnregisterUnit`/`Formation.OnDisable`原生Hook实机命中；未完成这些门禁前不标记稳定完成。
