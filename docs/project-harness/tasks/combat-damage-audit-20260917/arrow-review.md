# 弓箭 / 火焰 AoE / 英雄三箭只读审查

范围：当前安装 0b1ad4e1 对应的 PatchArcher_Options、PatchArcher_Impact、PatchArcher_GreekImpact、HeroArcherCombat，以及相关账本、既有测试和本地原生 2.1 参考。未改生产、测试、配置、存档或安装；未启动游戏、增加 hook 或日志。本轮没有实际 2.4 命中耗时/扣血追踪，不把视觉命中认定为 ReceiveDamage 成功。

## 发现

**P2，条件性跨生命漏 AoE：目标去重仅记 Damageable 指针。** `PatchArcher_GreekImpact.cs:116,1233–1234,1276–1279` 没有目标生命代次，目标死亡/回池也不会清掉该 volley 的记录。可直接从代码推导的序列是：同齐射箭 A 对敌人 X 产生邻接 AoE → X 回池再作为同一个对象启用 → 同齐射仍在途的箭 B 在新 X 邻近命中 → X 通过 alive/current 检查，但旧指针命中账本，新生命少一次 AoE。不是要求同一生命在同齐射重复吃 AoE。本地 2.1 `Damageable.cs:141–150` 显示禁用时重置 HP/dead，池复用可保留对象；是否在真实 2.4 同齐射飞行窗口内遇到这种复用，未实测。现有 `tests/greek-impact/LedgerTests.cs:78` 验的是**箭自身**回池，不覆盖受伤目标的新生命。建议后续仅在有可靠目标生命证据时区分该记录，不能用时间/位置猜测，也不应直接取消合法的同齐射去重。

**性能关注，不能据静态代码判定卡顿根因：命中 AoE 没有跨全部箭的每帧查询额度。** `GreekImpact.cs:1200–1257` 每个合格命中进行一次 NonAlloc OverlapCircle，最多读 64 个 collider；对候选逐一重新验证当前箭、来源、world，随后 ClosestPoint、GetComponentInParent、免疫等检查，单齐射最多 32 个目标。共用查询缓冲且命中结束清空，不是每帧全场物理扫描；但大量原生箭同帧命中仍可集中执行许多次查询。1280 lease / 256 volley 是并存容量，不是每帧吞吐帽；FX 的 4/帧上限也不会限制伤害查询。

**性能关注：视觉本身有明确的持续 native 调用成本。** `PatchArcher_Impact.cs:53–60,341–356,644–702` 最多 16 个特效 × 3 层 × 36 顶点；所有层存活时每帧最多更新 1728 个顶点、调用 3456 次 PerlinNoise 和 3456 次 Round，以及 48 次 mesh.vertices 上传、48 次 RecalculateBounds，另有材质/变换写入。网格、材质与顶点数组会复用，初次建槽才集中分配（482–527）；上限是同时 16、每帧新增 4、每秒新增 24。这里和 AoE 都值得实测比较，不能直接把卡顿归因于“死后多判几次”。

## 已确认的规则与边界，不列作缺陷

- 英雄三箭是期望数量，额外两箭仍与普通散射共享预算：每发最多 2、每帧 8、每个整数秒桶 40、同时额外箭 64（`Options.cs:123–129,274–303,455–468`）。池满或预算用完会少发额外箭，原生主箭保持；这是主动退化，不是已经命中的箭被去重吞伤。整数秒桶不是任意滑动一秒窗口。额外箭不再次调用 FireArrowInternal，不会递归三裂。
- AoE 的 64 collider / 32 目标会在密集重叠时省略范围内后续对象；多 collider 也占查询缓冲。`GreekImpact.cs:1217–1234` 有计数，既有 FullBuffer 测试明确预期此上限。没有“范围内无限目标都必定扣血”的保证。
- 直接命中者不重复吃该箭的邻接 AoE，同一齐射同一目标最多一次 AoE，火免疫/死亡/无敌/非本世界/友军过滤均属现有规则（1287–1308）。每支箭的普通直接伤害没有被该 volley 账本合并；只有火箭同一次 HitObject 事务的重复 TryDamage 被 TxSubstituted 挡住（1000–1057）。
- 来源弓手在箭落地前死亡、禁用或回池，会失去该箭附加 AoE 资格（293–300,366–376,652–675,962）。原生直接命中路径继续；失去有效 lease 的火箭走回原生 DOT。箭租约超时、容量不足、切 world/关闭功能也可能撤销附加效果。这些是当前保守资格策略，不能称主箭伤害全丢。
- 箭生命、shot、HitObject 事务均有独立 epoch，OnEnable 退休旧箭，Archer.OnEnable 失效旧来源，换 world 清账（433–444,487–517,652–675,831–838）。未见普通不同目标指针被错误互相去重；上面的目标新生命例外仍存在。
- AoE 在调用 ReceiveDamage **之前**登记目标；调用抛异常会结束本次 burst，后续目标不处理且该目标不重试（1234–1244）。这是防重入/部分成功重复结算的保守取舍，有异常时可少算；没有异常现场，不能判定它解释用户感受。计数 `StatDamage` 是调用正常返回次数，不是实际减少 HP 数。
- 特效只要求原生 `_hasHit` 接受命中，EndHit 即使无 AoE 目标仍可返回 true（1073–1093；Impact.cs:881–884）。地面、免疫目标等都可能有视觉而没有实际扣血，视觉不是伤害回执。

## 死后重复伤害与分配

本地 **2.1** `Damageable.ReceiveDamage` 在 active/authority 后先调用 OnPreReceiveDamage（236–245），没有入口 isDead 早退；普通 useHitPoints 且 HP=0 跳过 OnReceiveDamage，`!isDead` 挡重复 OnDeath（255–288）。所以“死后多打几次一定重复掉钱”没有证据，但“完全没有额外回调/开销”也不成立。掉钱订阅及实际 2.4 方法由 root 的原生审查补证，本报告不外推成 2.4 定论。当前自定义 AoE 已在调用前排除 dead，直接箭沿原生路径。

维护不是全场逐帧扫描：GreekImpact 每 Tick 至多 24 lease + 6 volley 槽，额外箭维护最多 64，染色回执最多 128。来源 OnEnable 会扫固定 256+1280 槽，属于生命周期成本。不能称整个路径“零分配”：GreekImpact 的 lease/volley/32目标数组按槽惰性建立复用；Options.cs:502–505 在没有可复用 ExtraLease 时 new，553–562 又会移除已退休项，因此稳态持续射击仍可产生小托管对象。异常路径还有日志字符串；IL2CPP wrapper 的实际 GC 分配未测。

代码快照 SHA256：Options `F07E2E54BB6D2E53490C90A7610C51B58A21A5C7B22FD81C2F2E2EB08100A55A`；Impact `5E1A2FDD64094D37CC43871DE94E24334E6B6FAAC4E1C139BE0FAD4B0313D4F4`；GreekImpact `DBF1E4D1254F4674D79DC550ACB92B2FB695BC27A93E3BA649F157BA7FCBE892`；HeroArcherCombat `D3C0B76631069A2F481736DE08CB99379469D9DBB6DBA75CB51D898077067FE8`。
