# #320 全向伤害保护：验证与边界

> 最终原生审计推翻了v2的一项假设：IsDamagedBy=false不代表长ReceiveDamage不扣HP。v3已删除该透传门并纠正host桩；当前实现以全来源已进入伤害请求为准。以下v2阶段证据保留历史，最终v3结果见文末；不能再用旧绿证明mask拒伤。

代码范围：PR#312原候选84a0ae8之后的全向/全来源防护增量，关联#311和#320。用户新规则替代旧的正面/普通来源白名单，不改AI追敌距离、原生移动方式、商店或已购身份算法。实现只改HeavyShieldBlockPolicy、HeavyShieldCombat、HeavyShieldRuntime三处产品代码；原native detour目标、反弹实现、schema5迁移和素材保持。

## 实现与独立复核

生产前缀按目标当前life建立保护，无source组件/方向/姿态要求；每life同一个自然秒窗口，首次和距上次扣耗至少1秒扣1，窗口内持续挡伤。已核原生前置条件保证零负伤害/原生无敌继续执行原函数。独立原生审计另发现Character鼓舞免伤，增加现有IsInspired()纯只读资格门，在原生回调之前透传且不耗盾。

保护不等于反弹凭据：只有本次真正Troll碰撞顶帧、source/target/kind/career/current life都匹配才标记Witness；范围、空源、异source、特殊怪保护不为其它帧领票。已提交policy结果不因后续持久/显示异常回退成原生承伤；持久拒绝仍暂停后续防护。未增加协程、HP镜像、无敌bit、周期扫描或新native hook。

独立代码审查：v1唯一P1为鼓舞漏门，已修；v2实际11文件SHA与清单匹配，APPROVE。Reviewer自行用相同fixture/stub/场景与固定旧3产品源重建，旧code精确6个行为红例、新code56断言全绿；鼓舞独立12条反例在v1失败、v2通过，覆盖精确到期/续期/回调次数/同一自然时间戳。

## v2阶段自动验证（历史记录）

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

## v2阶段证据身份（已由下方v3更正覆盖）

Root13组与上述四次NoDeploy编译全部发生在Operator应用v2并核验11文件SHA之后，均对应aggregate patch `5b4c9dfa8582978f14d1e2d28860b2832a370357099570a282fe396ab943359d`（原84a0ae8加v2增量），不是修鼓舞前的产物。元数据独立读回四个真实产品DLL和Root RuntimeTests DLL：都含Character.IsInspired调用、单参数Runtime.TryBlock/EvaluateHit且不含旧Hit结构，四产品275份嵌入资源逐名逐字节一致。输入映射与时序见本机verification-inputs-v2.json、artifact-audit/result.json。旧独立56断言仅作为old→v1红绿历史；最终v2证据为Root1656/新增60、worker v2及独立鼓舞12断言，不混记。

## v2累计主线核对（历史验证）

代码累计点`3af7ec60d3708205a3430211bbef318061b63aab`已包含主线`5ce82cfa9bd68d471748eef40b725f73ea2ed332`的#319保存修复，SharedBankNative.cs SHA256保持`6e12f472f037cbd6879bf064b1f3f03779e2fa30fa95b8e06c21a044c13231fe`；盾卫11文件逐项仍为review过的v2 SHA。累计树再次完整Mac240/Win243 net8/net6编译均0W0E，Cecil再读回四产物的v2方法特征和275资源与合并前一致；新产物adapter43、主线#319账户选择18检查/0失败。其它13组沿相同盾卫source的既有v2证据，不伪称重新跑过或实机通过。

主线更新只在本任务分支进行。两份任务记录冲突通过恢复已批准基线清单原字节、Coordinate create-files重放原#311 operation `81ec8cbe-7044-48ee-afbc-1dd4d57f6ab3`解决，input/before/after fingerprints均与原操作一致；121个基线item内容全部保持、总计122、status仍todo，未执行DB record半边。Markdown用正常三方union保留两边记录；EXharness validator通过0warnings。原#311 plan字节保留绑定，新范围明确由本#320 plan与用户新授权覆盖，未裸改JSON或伪造managed done。

## 最终v3：撤回错误的mask拒伤假设

两名Reviewer分别读取完整原生长入口，确认damagedBy mask不匹配的`0xb1d58c→0xb1d5dc`只跳过AddForce，后续`0xb1d724`仍可写HP、`0xb1d780`仍可置死亡。`IsDamagedBy`是攻击调用方查询，并非已进入长ReceiveDamage后的无伤保证。v2的提前透传与host桩Allowed扣血门均为错误假设，已在push前撤回；不能再引用v1/v2绿作为该边界的正确性证据。

v3只从产品Combat前缀删掉该单一条件，不扩大damagedBy mask、不加hook或补血。原生invulnerable与IsInspired、正伤害/当前付费life、Playing/世界/开关/挂载/自有视觉门保持；invulnerable getter另以原生`0xb1cbe0`直接读取`+0x100`核实。原生调用方若根本不发伤害请求自然零耗，一旦正伤害已到入口，不再用kind猜测免伤。

纠正host桩：Allowed只决定IsDamagedBy查询；它不再阻止长入口HP。精确红绿在同一纠正后的桩运行，非盾卫false-mask确实扣血的控制先通过，旧v2有效盾卫漏防失败；v3盾卫false-mask Ogre/未知与复合kind/空source仍保护且跨来源共用窗口，不领外层witness。独立Reviewer重新用固定v2与v3生产源重建，旧红、新9条独立断言绿，最终实现APPROVE；历史hold与原始日志保留。

最终aggregate against84a0ae8 patch SHA256 `f7bf67dddfd0ba530e861e6313cedbe7e5724022726ecf7179ae12fb51db7c91`，11文件manifest SHA256 `f22c4290021fa8ba9abc67f0fa4b4d6cf719633b0443502f7d8f9728f76b19ae`。Operator逐文件应用后，在真实累计树重新跑13组全部通过：runtime1663、全向67、原pause归属82/0，其他save/identity/maintenance/visuals/integration/banner等保持前表通过（本次重新执行，不复用v2绿）。Mac240/Win243 net8/net6四实际interop NoDeploy编译再次全部0W0E；fresh product adapter43已包含在本次13组。policy仍保留原net6测试EOL提示，非产品新warning。

Cecil重新核四产品和本次RuntimeTests五个实际程序集：Runtime.TryBlock仍有IsInspired查询、简化单参数policy保留，且真正的HeavyShieldCombat.TryBlock均不再含IsDamagedBy调用；四产品275资源与此前逐名逐字节相同。注意mask门位于Combat而非Runtime，不能只看Runtime调用缺失来冒充v3。对应证据v3-verified-inputs.json、v3-root-tests-results.json、v3-product-build-results.json、v3-artifact-audit/products.jsonl。所有产物在v3应用后生成；银行6e12源、harness122项与原operation fingerprints不变。#319账户18项是同一未变银行源码此前累计验证，未冒称本次重跑。

本轮仍未push前结束产品验证；最终GitHub commit回读另记。没有安装、实机、合并PR或发行新版，Windows native方法体/时序仍待实机，不以Mac控制流替代。
