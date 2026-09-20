# 税收官自动采购四职业道具

用户已确认：四职业Worker/Archer/Ninja/Berserker独立目标；从主城金库扣店铺实际费用；人数+待领工具+转职/在途合计；商店满货则等待，即使目标人数尚不足。用户明确关注性能，不新增全场扫描。

原生名册初始化仅枚举Kingdom._characters一次，按Worker/Archer/Ninja/Berserker组件分类；之后Kingdom.AddCharacter/RemoveCharacter与Damageable.OnDeath维护增量计数。Ninja含白天_isFisher外观，Pikeman排除；Archer包含随从/塔岗。ShopPlanner._placedShops初始化商店缓存，AddItem/SetPlacedShop/工具OnPickedUp与OnDisabled标脏。稳定期无角色/商店注册表重复枚举，不额外扫描DroppableRegistrar/场景/Physics；只刷新脏商店库存。转职同步创建新角色再移除旧体，视觉FX期间新体已入名册；pending Add延迟检查Pool.Spawn后parent。

税收官借用已有4个visual-only actor，最多2个同时采购，统一role/shop/assistant预留避免超买。现有收币已立即入国库，CarriedCoins只表现不能作第二份资金。借用前原生归还旧coin claim并清理已入账携币状态；reservation阻止coin派发与idlepatrol抢回actor；pool/reset/world/auth/config失效归还或取消。未加Banker/Wallet/Persistent到助手，不复制903角色。

动画复用Banker.DropOff .2秒一枚：原币prefab+DroppableCurrency.MoveTo(target,true)，该原生入口SetFake禁物理/拾取并移动后销毁；不得产生可捡的免费币。资金只在最终合法购买的同步调用前扣除，之前预留是调度计数，不跨帧动余额。取消或资金不足时没有扣款。Native TransactionComplete保留实际商店Pay/出货/统计/注册逻辑；once开始购买后未知异常不盲目退款/重试造成复制，记录并封锁该shop自动采购到world重载，必要时人工复核。

UI新增第五页自动补货，四职业独立开关(默认false)/目标(默认15，范围1..200)/缓存现有、库存、采购与状态。不是修改现有人口，不越过商店容量/施工/付款/授权/缺库存入口。未生成商店等待现有ShopPlanner，不建店不直接Spawn职业。

实施：root负责资金API/助手reservation/面板集成；本机ZCode worker负责单一新采购服务文件，独立ZCode reviewer交叉核验。只IL2CPP，当前E6DE8F6CB为回滚基线；先隔离编译/决策与事务测试/API审计，再游戏退出时备份部署、受控暂停启动。不开启自动消费真实存档以假造实战，实际动作待用户启用；公开5.0不变，无commit/push授权。
