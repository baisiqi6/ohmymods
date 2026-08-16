# special-tower-rebuild-018 — 特种箭塔安全重建

## 目标与首版范围

- 允许已经失去战略价值的特种箭塔付费重建回当前世界的原生六级普通箭塔，再由玩家携带目标隐士按原版流程重新专精。
- 首版只开放“稳定状态的 Ballista 弩箭塔 → 当前 biome 原生六级普通箭塔”。正常驻守或排队工匠不再阻断交互；回到普通塔后的第二次升级继续使用原版价格、隐士消耗、施工和存档链。
- 北境 Ballista 路线实际被 biome swap 为 `OilFireArcherTower`，因此首版在北境 fail closed；待火油库存、隐藏 GuardSlot 与投射物回收链单独通过后再开放。

## 原生证据与设计选择

1. 2.4 资源中的完成态 Ballista、Fire、Knight、OilFire、Berserker 等专精根均没有 `PayableUpgrade`；普通 Tower 3～6 才有原生付款/乘客升级图。因此不能只修改 `nextPrefab`，也不能在已注册实例上临时追加 RPC 组件。
2. 原生 `PayableUpgrade.Pay()` 已负责预留/同步 next NetID、实例化当前 biome swap、复制 `IUpgradeable` 数据、消耗乘客、统计与销毁旧根。首版只在 prefab/CRPC 枚举之前确定性增加原生 `PayableUpgrade`，实际交易完整复用该原生链。
3. 注入的 `PayableUpgrade` 同时实现 `IRPCable` 与 `Persistent.IBehaviour`。主客必须在 `PoolManager.InitPools` 和任何实例注册之前得到相同组件顺序；即使总开关关闭也保留组件布局，仅阻止选择/付款。
4. 重建付款 profile 从运行时原生六级塔读取，不硬编码价格；目标设为原生六级塔、无 passenger route、`statToIncrement=Null`，避免重建本身重复计入特种塔统计。玩家在重建后再次专精时仍由原版正常计费并使用隐士。
5. Ballista 即使没有当前 Worker，也可能持有池化 `_bolt`。最终付款前必须先确认该 bolt 有原生 pool，并以本地对象池语义回收后清引用；不得让它作为旧塔子物体被直接 Destroy。
6. `Ballista.OnDisable/OnDestroy` 只注销工作点；工匠的完整退出发生在旧塔由原生 `PayableUpgrade.Pay()` 销毁后，当前与排队工匠在下一次原生工作循环观察到 Unity-null 工作对象并执行各自清理。补丁不得直接清空 `_currentActors`，也不得调用被 publicize 的 native-private Worker 重置方法。

## 安全门禁

- 只在 Mod 启用、Game.Playing、Kingdom safe、当前 world/gameLayer、Persistent 与 CRPC header 已就绪时允许交互。
- Ballista 必须 active/enabled、无攻击目标、无卷扬音效、非施工中；正常驻守或排队工匠允许存在，并交给旧塔销毁后的原生 Worker 生命周期释放。
- 允许两种稳定状态：
  - Ready 且存在活动、可回收的池化 bolt；
  - Reloading 且 `_currentWork==0`、无 bolt。
- 原生 `CanSelect/CanPay` 仍在上述门禁通过后继续执行，以保留货币、王冠、技术、区域与全局 payable block。
- 离线与本地分屏只在原生最终 `CanPay=true`、付款玩家处于 `Completed` 且付款对象精确匹配时，于 `CanPay` Postfix 回收 bolt；失败把结果改为 false，让原生取消交易并在下一步退回悬浮金币。
- 回收成功后建立只绑定本次 payable、玩家、world、scene 和 frame 的一次性 token；随后的 `Pay` Prefix 只原子消费 token，不再执行任何可能失败的清理，然后完整进入原生 `PayableUpgrade.Pay()`。
- 在线首版整体 fail closed，不显示也不接受重建交互。原因是在线付款 RPC 在 `Pay()` 前已经批准，无法在该阶段安全地单端取消；未设计对称的批准前事务前不得开放。
- bolt 回收异常时，仅在对象仍活动且原 pool 仍可解析时恢复原引用；若发生部分回收，则保持引用为空并把旧塔归一为 `Reloading/currentWork=0`，由原生工匠重新装填，同时取消付款并退币。

## 明确不支持

- Fire/Greek Fire：拥有火罐库存、PayableComponent、活动 projectile 和 FSM；首版不丢弃或补偿库存。
- OilFire：拥有隐藏 archer/GuardSlot、worker、flask/projectile 与多 RPC；首版不作为重建源。
- Knight/Berserker：`TowerKnight.Start` 会在塔根同级创建独立 `PayableShield`，单位/商店还通过 PersistentLink 关联；首版不制造孤儿盾牌店或迁移驻军。
- Baker/Mead：属于商店型塔，不在本轮战斗塔重建范围。
- 不退款、不直接 special→special、不绕过隐士、不改现有塔图的普通升级路线。

## 存档与卸载兼容边界

- 启用本候选后保存的未重建 Ballista 会新增一份原生 `PayableUpgradeData` cooldown；再次由本 Mod 读档时 prefab 布局一致。
- 完全卸载 Mod 后，原版 prefab 不再有这份新增组件，旧存档可能记录一条“缺少 PayableUpgrade component data”并忽略该附加数据。首版实测必须覆盖保存→退出→读档，玩家说明不得把“完全卸载后零日志”作为保证。

## 验收门禁

1. IL2CPP Debug 禁部署构建 0 warning / 0 error，diff-check 通过。
2. 加载希腊岛时只出现一次 Ready 摘要，价格等于运行时原生六级塔；无 unknown pool、duplicate syncID、RPC/component registration 异常。
3. 和平期正常有当前与排队工匠的弩箭塔也可付款重建为同位置、同朝向的当前世界六级普通箭塔，数量不重复；装填 bolt 被池化回收且无残留投射物。
4. 重建后的普通塔可携骑士/Fire/Ballista等原生允许隐士再次专精；隐士只在第二次原生升级时消耗，重建本身不消耗隐士。
5. 当前与排队工匠各实测一次：旧塔销毁后 1～2 帧内原生工作引用、动画、z 与伤害 mask 恢复，工匠不消失、不复制、可重新接活；攻击目标、施工中、非安全期、Mod Disabled 时仍不可重建。
6. 保存未重建的注入塔与已经重建的普通塔，退出后再次读档均正常；跨岛和本地分屏完成一次。在线首版应保持无交互、无组件错位、NetID/RPC错误或重复塔，不能把“在线不可用”误报为已支持。
7. 北境 OilFire、Fire、Knight/Berserker、Baker/Mead 均保持不可重建，直到各自 teardown 子任务完成。

## 当前状态

- 原生资源图、付款链、持久化与 Ballista bolt 生命周期已完成只读核对。
- 交互修复已落在 `il2cpp/PatchWorld_SpecialTowerRebuild.cs`：移除工匠数量阻断，新增付款前 bolt 事务、失败退币和有界阻断原因诊断。
- worker 禁部署构建 0 warning / 0 error，独立 reviewer `APPROVED`。当前源码 SHA-256=`C7FF31FFD1E6D025D63CCD615AB582D9B2A3A7E88C57C784B42374B461CA3F78`；禁部署 Debug DLL 为196,608 bytes、SHA-256=`113BE01ED8F8ABAAD52571DCEF74829A14CB7B7B4F5210191AE8F804CF0D6696`。
- 修复尚未从干净提交重建并部署；当前 E 盘独立副本与现有 ZIP 仍是上一候选。部署后仍需工匠释放、bolt、再次专精、保存往返、跨岛与本地分屏实测；在线首版明确 fail closed。

## 回滚

- 回滚只移除该补丁并从干净提交重建/部署独立副本。
- 若已用本候选保存未重建 Ballista，回滚后原版可能忽略一条额外 PayableUpgradeData；不得通过直接编辑压缩存档掩盖问题。
