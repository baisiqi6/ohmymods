# #320 盾卫全向、全来源共用耐久保护

用户于2026-11-02明确选择：所有方向、所有种类攻击都共用每秒最多一点损耗。此规则替代#311原“背击/范围/特殊来源不保护”的范围，沿未合并PR#312与独立分支codex/heavy-shield-combat-311累积。

## 当前行为契约

- 90耐久。每名当前life的盾卫首次受击扣1，之后距上次实际扣耗至少1自然秒才再次扣1；窗口内继续拦伤，不扣、不改stamp。不是整秒桶，不因换来源/姿态/重复挂载/维修重置。
- 公共Damageable.ReceiveDamage前缀按目标的付费身份、当前世界/life、Playing、开关、挂载及自有视觉所有权保护；取消方向、source组件、攻击类别白名单与GuardReady动画门。空source、失活source、相同x、背击、触手/范围、箭/火等在原生实际伤害入口一视同仁。
- 原生零/负伤害、invulnerable、Character.IsInspired免伤，以及非盾卫/无效life/过期职业等仍透传原函数，不凭空扣盾。攻击种类mask不作为长伤害入口的拒伤门；原生该分支只影响AddForce，仍会扣HP。鼓舞判断复用当前原生公开bool IsInspired()，不执行回调来探测。
- 防护结果与反弹凭据分开：只有当前Troll碰撞顶帧且本次source/target/kind/career/life精确匹配时才给Witness；保持原生pause归属/方向检查。Portal、箭发射者和其它来源不能借票反弹。
- Policy提交后，即使持久记录或显示/可选反馈失败，本次仍挡下；持久拒绝按既有机制暂停后续防护，不捏造购买/身份状态。第90次仍挡下后按原破盾退职，不复活。
- schema5比例迁移、维修等待/工作、购买记录、AI迎敌距离保持；不拦正常回收/卸载。坏/倒退时钟维持#311的逐击扣耗降级，不能宣称异常时钟下仍有自然秒保证。

## 实现与验证

根因在公共前缀把伤害保护绑定到Troll/Arrow入口票据与Runtime动画/方向，独立攻击绕过后打在Archer载体。修正统一入口，不逐个敌人补偿、不加HP镜像/无敌标志/扫描/协程。删除policy旧Hit方向/来源数据与ArrowFront判断，迁移真实消费方（包括save/identity测试），反弹保留独立Impact/Witness数据。

先在固定旧head84a0ae8上加入source-linked用例，编译成功后6场景漏伤红；实现后绿。原生鼓舞缺口另留v2红例并修正。GPT-6.1 Sol在私有staging交付，Operator逐文件验证哈希后应用；独立审查/原生入口核对/实际interop编译分别留证。具体结果与平台边界见verification.md。

本任务不正式发版或单独递增版本，不创建或安装懒人包。代码、合并、安装和玩家实机分别记录。Issue在PR未合并前保持开放。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.
