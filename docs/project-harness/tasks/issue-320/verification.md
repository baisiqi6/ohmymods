# #320 全向伤害保护：验证与边界

代码范围：PR#312原候选84a0ae8之后的全向/全来源防护增量，关联#311和#320。用户新规则替代旧的正面/普通来源白名单，不改AI追敌距离、原生移动方式、商店或已购身份算法。实现只改HeavyShieldBlockPolicy、HeavyShieldCombat、HeavyShieldRuntime三处产品代码；原native detour目标、反弹实现、schema5迁移和素材保持。

## 实现与独立复核

生产前缀按目标当前life建立保护，无source组件/方向/姿态要求；每life同一个自然秒窗口，首次和距上次扣耗至少1秒扣1，窗口内持续挡伤。Would-damage前置条件保证零负伤害/原生无敌/不接受该伤害种类继续执行原函数。独立原生审计另发现Character鼓舞免伤，增加现有IsInspired()纯只读资格门，在原生回调之前透传且不耗盾。

保护不等于反弹凭据：只有本次真正Troll碰撞顶帧、source/target/kind/career/current life都匹配才标记Witness；范围、空源、异source、特殊怪保护不为其它帧领票。已提交policy结果不因后续持久/显示异常回退成原生承伤；持久拒绝仍暂停后续防护。未增加协程、HP镜像、无敌bit、周期扫描或新native hook。

独立代码审查：v1唯一P1为鼓舞漏门，已修；v2实际11文件SHA与清单匹配，APPROVE。Reviewer自行用相同fixture/stub/场景与固定旧3产品源重建，旧code精确6个行为红例、新code56断言全绿；鼓舞独立12条反例在v1失败、v2通过，覆盖精确到期/续期/回调次数/同一自然时间戳。

## 自动验证

| 验证 | 实际结果 |
|---|---|
| Runtime真实source-linked生产入口 | 1656断言通过；新增全向60，原pause归属82条/0失败，普通Troll反弹/生命周期/维修保留 |
| Policy | 8场景通过，精确1秒、窗口不滑动、life独立、重复挂载/维修不重置、90次破盾与退职 |
| Save | v1-v4比例142、schema5 68、native payload30、codec/policy594检查通过 |
| Identity | native-boundary136、真实identity/persistence兼容328检查/0失败 |
| Maintenance | 36 tests /2260 checks通过 |
| Visuals | 891断言通过 |
| Integration / Leap / Promotion / Quota | 48 / ALL PASS /27 /93通过 |
| Banner policy / platform / actual-product adapter | 20场景279 /49 /43通过 |
| Android条件host | flipY合同6通过，不代表设备实测 |
| Mac240 / Win243实际interop | 完整产品net8/net6四NoDeploy编译均0W0E |

Root实际树13组相关回归通过。测试采用各工程原net6/net8声明，host实际运行于SDK8 runtime、net6用Major roll-forward；policy的既有NETSDK1138 EOL warnings留录，不为消warning升级TFM。Root首轮adapter漏传产品DLL参数导致驱动失败，原始日志保留；真实产品编译后只重跑adapter并明确绝对DLL，43通过。没有改产品迁就验证入口，也未删除失败记录。

## 当前原生静态入口证据

独立重新校验Mac240 ARM64 slice、metadata与两平台实际interop输入SHA。Portal.OnAttack按OverlapArea范围查询，传Ogre(0x20=32)到短ReceiveDamage；metadata精确定位长重载token0x06000f69、slot11、vtableStart4672、methodDef3944，结合当前原生vtable base0x138与stride16确认+0x1e8是该长入口。短/fatal/延迟持续伤害均经此槽，未见先写HP再调用的旁路；长入口在当前Mac地址唯一。

Character.IsInspired()原生只读同一Time.time与_inspiredUntil条件，无写入/回调/RPC，与OnPreDamageInspired把ref damage清零的条件相符；两平台interop都有公开无参bool查询。未发现明确对攻击目标直接DestroyNow/HP setter致死的旁路。网络RecvHP/反序列化及正常回收不拦，沿用功能原单机边界。

原生方法体证据来自Mac240，Windows243仅另核interop签名/枚举和编译；不以同名包装宣称Windows native时序已验证。Host stub未模拟真实OverlapArea或游戏物理，不能叫玩家实战已通过。

## 交付边界

本轮未启动游戏、未替换正常DLL，未改玩家存档/配置/购买记录，未重打懒人包或创建Release。游戏内普通怪扑击/反弹距离、触手门/火焰/箭/背击实战、跨平台native时序仍待对应实机。坏/倒退自然时钟维持此前逐击扣耗降级，不宣称异常时间源下仍有秒限保证。Schema5迁移来自原#311，回退旧schema4 DLL需配对备份，不能用旧DLL直接读新附加记录。

原始代码、红绿、逐文件散列、独立审查、原生指令与构建记录保留在本机.local/tasks/shield-player-feedback-20261102；不公开原始玩家资料。Issue在PR合并前保持开放，合并/安装/实机分开记录。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.

## 最终v2证据身份

Root13组与上述四次NoDeploy编译全部发生在Operator应用v2并核验11文件SHA之后，均对应aggregate patch `5b4c9dfa8582978f14d1e2d28860b2832a370357099570a282fe396ab943359d`（原84a0ae8加v2增量），不是修鼓舞前的产物。元数据独立读回四个真实产品DLL和Root RuntimeTests DLL：都含Character.IsInspired调用、单参数Runtime.TryBlock/EvaluateHit且不含旧Hit结构，四产品275份嵌入资源逐名逐字节一致。输入映射与时序见本机verification-inputs-v2.json、artifact-audit/result.json。旧独立56断言仅作为old→v1红绿历史；最终v2证据为Root1656/新增60、worker v2及独立鼓舞12断言，不混记。
