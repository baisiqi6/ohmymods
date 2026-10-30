# 宝石盾卫格挡与续战（Issue #311）

## 用户确认与基线

2026-10-30 用户确认：补原版骑士式格挡反弹并稍强；盾牌上限90；每名盾卫扣耐久后至少隔1自然秒才再扣1，游戏加速不影响。间隔内其它有效格挡仍挡伤、有反弹。继续先Issue再PR。本方案不调整背面/范围/特殊敌人覆盖，不把全伤害免疫作为平衡机制。

源基线release/v9.5.13@cb467d4c；本任务分支codex/heavy-shield-combat-311。正常已装Mac输入另为c0d7aa08的冻结累积版，不把public release DLL当作等价安装物。本轮只实现、验证、审查和提交PR；正常安装/玩家体验单独记录。

## 最小设计

- 耐久policy拥有每life自然时钟的last-charge，首次合格命中扣1；后续间隔<1s仍Blocked且不扣，>=1s扣1。按半开区间边界测试；空过不积攒扣耗额度。不是0/1秒整点桶，不允许0.99和1.01相邻扣。
- 生产使用既有AndroidUnityCompat.UnscaledSeconds（unscaled自然秒），测试直接提供明确时点；不使用日夜时间、Time.time或DateTime，不新增clock服务。异常/非finite/倒退样本回落旧按次扣1且不写timer，不能变成无限免费格挡。暂停时入口原Playing门保留；窗口不持久化、不因重复attach/维修复位。
- save schema5：v1上限3，v2/3上限4，v4上限40，v5上限90；先按原版本验证，再向上取整等比例迁移一次。20/40→45/90，0→0，不复活，不改购买/归属/unknown/原生fingerprint和Prefs键。维修满值和磨损分档使用90的统一比例；原6游戏秒脱战/8游戏秒工作保持。
- 反弹由攻击者Troll的原生碰撞负责；证据表明ApplyCollisionDamage普通Archer分支会在ReceiveDamage后创建PauseMotion。只在已有该方法Postfix的正常返回点，消费本次真实成功格挡的凭据，复核同source/target life/career/world/方向。异常、未格挡、嵌套外层凭据、背击、箭发射者、特殊怪均不能借用。
- 使用原生本次新创建且已经首tick等待中的Troll._PauseMotion_d__114，只改velocityAfter：必须与入口旧pause不同、精确owner同Troll、state1、duration1/15s，源/目标仍同life。按实例knightBounceForce逐分量乘localScale再×1.2，验证X确实离盾卫而去，Y符号保留。确定性基准+20%，不额外消耗随机数，不宣称必然超过原版随机上限。复用原生绝对速度恢复，不再起协程、不写刚体/坐标、不额外伤害。

## 实施与验收

- [x] A：worker在任务staging给policy/schema/migration/维修接线的精确diff与相关测试；Operator在本树整合既有核心源。无Main/build.bat变更。Android显式消费者仅补新leaf的Compile项，不改pin。
- [x] B：worker在独立staging给一次性格挡凭据、原生pause附加反弹leaf、已有collision hook的精确diff与host边界回归。Operator统一整合，不引入新的detour/registry。
- [x] A红灯：90初始、同刻100hit仅扣1、.999/1边界、多actor独立、自然秒与timeScale无关、非法/倒退不会无限免扣、90th charged仍挡而后破、生命期Reset与重复attachment。
- [x] 迁移与维修：v1..5原范围验证，含quarantine/unknown/购买；序列化重读不二次放大，原broken0不复活，repair满90与wear68/46/23分界。更新现有相关literal断言，不把历史v4fixture误改为v5。
- [x] B红灯：只有当前同impact成功格挡+正常返回+新pause/state1/life证明才修改一次，重复apply/exception/foreign pause/wrongstate/复用life/箭/背击/特殊怪不发反弹；同秒不扣耐久的合法格挡仍可反弹。向量离开目标、无附加伤害/第二协程、重复不会把已有速度再乘。
- [x] Operator复跑policy/save/identity/runtime/maintenance/visual与本轮相关链；完成实际Mac240与可用Win243 interop、net6/net8 no-install编译。原始base失败独列，不以host声明游戏体验通过。
- [x] 独立read-only审查固定diff/原生接入责任；增量复审只针对新改点。A与B最终均APPROVE。源码交付关联#311，PR创建、合并、安装状态以progress与GitHub实际状态为准。

## 证据与权限

本机任务证据在.primary .local/tasks/heavy-shield-combat-feedback-20261030；原生只读报告hash d027e952…，ABI/指令与实际interop独立记录。方案审查GLM5.3/max，worker按派发时北京时间路由；两slice各自staging、无commit/push/install/游戏权限。旧Mono源码只读且不作为当前版本证明。未写正常存档，不以安装目录年龄清理数据。
