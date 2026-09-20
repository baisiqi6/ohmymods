# 鹿 slice 冻结版独立复核

2026-09-17，复核隔离 `deer/work`，非 canonical 整合候选。源码 SHA256：

- MusketeerRuntime.cs：`a6a91d90dc432c1256e528107820f06d71a2df8bf7e777702d1bbe1dbb3fc46e`
- MusketeerCombat.cs：`1e164ed3058884ae3bff614c9d56c16f76fe02aa13290e3cc48e51d50a2d8564`

## 结论

**鹿 slice 有界代码复核通过，无剩余已知阻断。** 原初审的发射 life、死亡、根 body 与地面裁剪问题已修订；最终即时身份门和日志建议也已逐项读回。此结论不覆盖尚未冻结的 banner 或整包候选，也不是实机验证。

### 已闭环：即时身份释放

`MusketeerCombat.cs:503–513` 的 IsDeerHuntApplicable 调用 IsArmedMusketeer；该 helper (`MusketeerRuntime.cs:222–225`) 仅读 runtime Applied/ClaimLost/PendingDestroy。MatchesBindingLease 也只比较 runtime 表。身份可以在回调内 Release，但包要到下一次 Reconcile 才失效；同帧另一颗旧弹仍可能通过鹿门。现 `DeerHuntTests.cs:617–624` 清名单之后先调用 Runtime.Tick，未覆盖该顺序。

最终 `MusketeerCombat.cs:401–409` 的共用 IsUsableSource 已检查 archer.enabled 和 Runtime.IsMusketeer（实时 Identity.IsUnit）。测试第 617–628 行清身份后不先调用 Runtime.Tick，断言旧包仍 Applied、scanner 立即拒绝、旧弹跳过鹿仍命中敌人，之后才维护归还。测试 SHA256 `8cfceb21b2a734a9ad3a74813206a43a16df976602d94d962e8b39c31551d912`，root 重跑日志 99 passed / 0 failed。该反例闭环。

### 已闭环：两处日志建议

- 第 1910 行现为 attempted damage submission，不再断言 ReceiveDamage 一定已调用或 HP 必减，实际伤害调用链保持。
- 第 1875、1901 行现于两个日志入口先检查预算，耗尽后不再读取额外 bounds/state 或构造格式化日志字符串。

## 已核闭环及测试边界

- BindingLease 在成功 Apply 和既有 Archer.OnEnable prefix 更新，生产 Clear 不归零；发射快照 ShooterLease，鹿碰撞比较，记录释放清零。敌人分支不检查鹿租约。
- source 死亡、Damageable 禁用、Character inert/grabbed、活动状态及当前 world 均有门。普通鹿依靠 Deer 根及自有 Damageable，排除 Steed/Hind/石化，白天在发射和命中复核。
- 鹿只有合法根 Collider2D 的有限非空 bounds 才能发射；近垂直退化或背面拒射，敌方方向保持水平。斜向步段先按地面交点裁剪，再做物理查询；最近有效命中仍经原始数组到完整 List 路径，段长改用欧氏长度。
- 保留原生 DamageSource.Arrow、伤害 2 和同步 ReceiveDamage 管线；未增加手动掉币、HP 写入、全场扫描或新 hook。扫描器与原先验 AND，归还仍使用原 CAS。
- 已读取 root 的 `revision-runtime.log`（99 passed / 0 failed）及测试工程生产链接；这些是受控 stub 行为回归。该工程使用真实 CombatDamage、Runtime、Combat、Visuals，但 Identity、物理与原生对象为替身。lease 测试覆盖 Strip/Reapply；生产 OnEnable 接线另有源码核对及已有视觉测试，不能代替 Unity 实机 lifecycle 验证。
- actual2.4 interop 编译日志为接口可编译证据，不证明真实逃跑鹿命中、原生掉币次数、举旗姿势、联机或性能。整包 DLL、资源审计待 root 整合后另核。
