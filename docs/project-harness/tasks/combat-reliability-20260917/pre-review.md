# 隔离实现预审（未冻结，非最终通过）

审查对象为 operator/combat-reliability-20260917 下 life/work 与 fx/work；先读 operator-notes.md、三份 prompt.md 和 canonical plan。未改实现、运行测试或游戏。实现可能继续变化，以下结论只对应本次读取快照。

| 文件 | SHA256 |
|---|---|
| life/CombatDamage.cs | 753FD3C310C466EF4FD3E9DED6DC223257B1595F3269FDC5CE18B5A1E7D1319C |
| life/CombatTargetLife.cs | B5788E2EA42CA0017B0C58A0B3DDF8F834176A2175648804BFE872F60A00AC09 |
| life/PatchArcher_GreekImpact.cs | 91E583DFC827AE0AF884D6470169FABF644248D338DBD03B0FA0F1F3317FB50B |
| fx/PatchArcher_Impact.cs | 497B7366DCC985990A8E160494A8261DF21FB175B79A4E3970F229D604FC0B99 |

## 需修复后再审

1. **P2：marker 禁用造成未被观察的生命周期缺口。** `CombatTargetLife.cs:61–76,143–151`：组件单独禁用时保留 Life；之后 GO 完整停用/启用期间，这个 disabled Behaviour 不会像启用组件一样收到成对消息。重新启用 marker 仍 EnsureLife 旧号码，或者 TryResolve 在 marker 仍 disabled 时也返回旧号码。因此原先同对象新生命误去重仍可发生。反复切换 marker 不能直接作为新 life；对丢失观察的区间应明确不可信，不能推断没有 GO 复用。`GameObjectActive` 的读取异常也被当作 false，从而清 Life 并在后续启用发新号码：这会把未知状态误作新生命证据。

   测试驱动也需要对应真实语义：combat-damage/Stubs/UnityStubs.cs:101 与 greek-impact/Stubs/UnityStubs.cs:164 对所有组件直接发 GO 启停消息，没有按 Behaviour.enabled 区分，可能掩盖该缺口。应覆盖 marker 单独禁用 → 其间 GO 完整回池 → marker 重启，以及 active 读取失败，不以 stub 强行派发实际不会发生的回调代替证明。

2. **P2：AoE 第二阶段没有重新核对完整目标资格。** `GreekImpact.cs:1283–1297` 只核箭/来源、距离和 token，没有重新执行 TryResolveTarget。较早目标的 ReceiveDamage 回调若把后续目标移至别的 world、变成 FriendlyTroll、设置火免疫，或禁用 Enemy/collider，GO 身份和 token 仍可相同，Submit 的基础门也不会拒绝所有这些情况。应在提交前重新解析原 collider，重跑原目标过滤，并确认解析得到的 Damageable 和快照 token 都吻合。

3. **P2：尚未提交的目标提前消耗 volley 去重名额。** `GreekImpact.cs:1271` 在快照阶段就写入持久 volley 账本。若第一个目标回调把第二个目标移出半径，第二个在 1287 跳过，但仍被记成该 life 已处理；同齐射后续箭无法对恢复有效的该目标提交 AoE。阶段一应使用局部快照去重；最终资格复核通过、即将调用 Submit 时才消耗 volley 记录。Faulted 保留记录且不重试的原则不变。需要“被回调移动而未提交，恢复后同齐射下一箭仍可一次伤害”回归。

两阶段快照方案本身可以防止同一次查询的旧 collider 在回调内池复用后攻击新生命；本轮没有要求撤回该方案。上述问题已及时发 root 转交原 worker。

## 当前未见阻断的部分及验证边界

- CombatDamage 的同步入口没有 invulnerable 提前返回，保留原生 OnPreReceiveDamage / 盾牌 / 无敌顺序；只读 authority/active/enabled/dead，最终调用原生三参数 ReceiveDamage，捕获 Faulted 且不重试。Submitted 仅表示正常返回，不保证扣 HP。未新增 Damageable.OnDisable/Reset 或 getter detour、队列、HP 写入。
- marker 初始化与初次 OnEnable 共用 EnsureLife，正常首次 AddComponent 路径幂等；全局序号避免销毁后重加同号；只给自身组件 DontSave，未发现修改目标 GO flags、Persistent、原生 SaveData 或 HP。实际 IL2CPP 注入消息与 native 存档行为仍待 root 验证，不能用编译代替运行证明。
- FX 静态差异未见改变随机调用顺序、三层颜色/尺寸/寿命公式或资源。noise 以原始计算输入 Time.time*5 缓存，共享 36 对值；量化保持先除、ties-even round、再乘的 float 顺序。dirty 强制首上传，量化未变时仍更新颜色/scale/生命周期；Release 清缓存引用。局部保守 bounds 半幅 .66、Z 厚度 .22 的推导与当前常量一致。未改原生伤害路径。
- FX 测试源码包含旧顶点公式逐位比较、颜色/scale 比较、负数/half ties、同 time 与 time 回退、调用数和 bounds 采样；本 reviewer 未执行，不宣称通过或实际 FPS 提升。官方 [Mathf.Round](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mathf.Round.html) 确认 ties-even；[Mesh.SetVertices](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Mesh.SetVertices.html) 文档不能单独证明实际 2.4 的每次底层上传成本，性能结论限于减少显式调用。
- bullet 仍在实现，本预审不对其半成品下结论。后续需审完整结果查询、触发器语义、最近目标选择、池记录回收与同步回调重入，以及大 dt/寿命/范围端点。

当前状态：**life 三项待修；FX 源码预审未见阻断；测试、实际 2.4 编译、整合候选及最终独立审核均待后续证据。** 本报告不涉及旧 blocked Hero/Crossbow 诊断。

## bullet 后续快照预审

root 请求增加审核 `bullet/work/il2cpp/MusketeerCombat.cs`，读到 SHA256 `9D29DFF7CB1110BD0558B5DB7DC6BC17E8D29594E389340FCEE33E0D23421DB3`。

**P2：同步伤害回调递归 Tick 会重复推进仍在途的旧弹。** Tick:713–780 有 frameLease 截止和 i>=count 防护，却没有重入门。反例：B 位于 index0 尚未命中，A 位于 index1 命中；A 的 Submit（758）回调递归 Tick(dt)，内层推进 B 一次；回到外层 i=0，B 的旧 lease 仍小于等于 frameLease，再推进一次 dt。结果是同次外层推进内 B 移动/老化两次，可能提前命中；frameLease 只挡新租用记录，不能挡旧记录。应增加 try/finally 保护的重入门或等价的一次推进机制，并回归 callback Tick、callback Clear+发新弹以及 callback 只换 world/失权的情况（当前 EnsureWorldScope 仅位于 716 入口）。已发 root 转交原 worker。

其余可见改动方向符合计划：消费前快照 ShooterRoot，RemoveAt 后不读归还记录；大 dt 的扫掠端点仍受剩余射程与寿命限制；数组饱和才转可扩容 List，遍历全部返回项，不假设排序，filter.useTriggers 沿 queriesHitTriggers，API 故障保守消费且有限日志。尚未用 root 测试结果或最终冻结 hash 验收这些结论。

root 后续通报 FX perf 8 与实际 interop 0W0E；主 archer-impact 测试工程递归包含 actual-interop/Stubs 导致 CS0101 正在修接线。该通报不替代最终日志/候选复核，也不撤销以上 life/bullet 阻断。
