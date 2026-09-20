# 最终独立只读审查

**结论：本轮有界 PASS，无剩余阻断项。** 适用于最终候选 `284B78A0DC4D17D83D40712E157DF1E21FB20EB2E15D981D0371488016740C1F`，基线为 `0B1AD4E149A0BF1BBAD90DE62A574A87F68B33E2A607CDBB7B46122EF695E55D`。此前 76bb 候选及 pre-review 中的未修快照不作为交付结论。

## 修订闭环

- marker 单独禁用或 active 读取失败现在进入观察不可信状态，TryResolve 拒绝发可信 token；重新开组件不擅自发新 life，直到启用 marker 实际观察到 GO 停用/启用才恢复。首次 AddComponent / 初始化幂等；marker 销毁后新建使用不同全局序号。没有用时间、位置或 HP 猜测生命。
- 已删除不必要的 hideFlags 写入，避免设置 DontSave 自己触发启停消息而污染观察状态。marker 不实现 IRPCable / Persistent.IBehaviour，不注册 SaveData、不改目标 GO 或旧组件。实际 2.4 原生 ObjectData 构造函数的接口分派与 ComponentData 路径已独立核对，详见 [native/objectdata-review.md](native/objectdata-review.md)；两个泛型 methodSpec 全名未恢复的边界仍保留。
- 非 Unity helper EnsureLife / GameObjectActive 均带 HideFromIl2Cpp，候选 DLL 也独立核到；暴露的 Unity 消息只有 void OnEnable / OnDisable / OnDestroy。没有 Update 或新原生 detour，特别没有碰同址的 Damageable.OnDisable / Reset。
- 注册每进程最多尝试一次；失败后不在每个 AoE 目标接触点反复注册，单次注册错误日志，后续降级日志有频控。原生直伤继续，未知 marker 不退回纯指针去重。
- AoE 使用最多 64 项本次查询快照与局部去重；提交前重新核对完整目标资格、同一 Damageable、同一 life 和距离。只有即将提交时才写 volley 账本，Faulted 不重试。仍保留每 volley 32 个提交目标、半径 .25、额外 1、直接目标排除及同 life 同齐射一次。
- 最后发现的“已有 30 目标，stage A/B，A 回调使 B 无效，C 被剩余额度提前截断”已修：快照不再按剩余提交额度截断，提交阶段才守 32 上限。新增回归确认 C 可取得第 32 个名额。旧 collider 在同一 burst 回调内被回池重用，不会命中新 life；后续箭的新 burst 可以。
- 弹丸 Tick 使用 try/finally 重入门；消费前快照 source，归还记录后不再读旧字段；long lease 截止不推进回调中新租记录。Submit 后重查世界、playing/权限/开关，回调改变上下文不会让外层继续结算旧弹。递归 Tick、异常、清理/复用、世界切换等回归对应此前阻断。

## 行为与性能边界

CombatDamage 同步调用原生三参数 ReceiveDamage，没有提前过滤 invulnerable 来取代原生 OnPreReceiveDamage/护盾顺序，不写 HP、不新建伤害队列。Skipped / Submitted / Faulted 不是 HP 增减回执；Submitted 仅代表调用正常返回。单目标异常保留消费记录、不重试，后续独立目标仍可处理。

火枪保留 2 伤害、既有射速/射程/过滤和 Crusher 免疫消费。常态仍走复用数组，饱和后使用可扩容完整 List；全量遍历选最近有效命中，保持 queriesHitTriggers/layer 语义，未以另一固定上限替代 256 截断。List 首次扩容可以分配，极端密度的查询总量不承诺硬上限或零分配。完整 dt 的端点仍受剩余射程和寿命约束，暂停语义保留。

FX 以实际 noise 输入时间缓存 36 对 Perlin，未改作者三层、随机取样顺序、颜色/尺寸/寿命公式或 PNG。保留 float 运算顺序及 ties-even 量化，量化未变跳过上传但继续颜色/scale/寿命更新，首建/复用强制上传，使用覆盖局部顶点的 bounds。优化证据是公式与显式调用量比较，不是实际 FPS 测量。

## 独立核对的证据

- 当前生产文件对 source-before.json：严格 4 项修改（Plugin、GreekImpact、Impact、MusketeerCombat）+ 2 项新增（CombatDamage、CombatTargetLife）。逐文件重新 hash 与最终 source-delta.json 全部一致。侧架、长按弹药购买、其他玩法和资源保持。
- 使用 Mono.Cecil 独立读取精确 before.dll / candidate DLL，比较所有方法指令、locals、InitLocals、MaxStackSize、异常处理边界：**3603 保持、18 改变、35 新增、7 移除/替换**，与最终 receipt 的具体方法集合一致。HarmonyPatch 类型集不变，全部 **8 张嵌入 PNG 字节一致**，程序集版本 9.0.0.0。marker 无持久化接口，helper 的 HideFromIl2Cpp 属性存在。
- 读取 root 最终测试日志与直接链接工程：26 combat-damage、88 GreekImpact、85 hero-GreekImpact、77 bullet、54 FX、54 hero-FX、8 FX performance，合计 **392 项通过**（跨套件含重叠覆盖，不表示 392 个互不重复的游戏场景）。bullet 与 AoE 测试都链接真实 CombatDamage 生产文件。root 的测试工程排除嵌套 interop、重复 FX 工程同步等接线修订未改变生产行为。
- 完整实际 2.4 重构建以及 bullet/FX interop 日志为 **0 warnings / 0 errors**。此 reviewer 未重复运行这些套件；已审相关回归源码、日志和精确产物。

## 尚未实测

没有启动游戏、写存档、安装、Git 变更或发布。真实 IL2CPP marker 注入及池复用消息、List overload 的实际 AOT 运行、高密度战斗的扣血/盾牌/主客机表现和帧耗时仍需游戏验证。编译、模拟 native 边界和静态调用量均不能替代这些实测，也不能宣称从此所有漏伤或卡顿已消除。保留原 64 查询结果 / 32 AoE 目标和额外箭预算等既有规则。本轮不重审旧完整火枪附加档，也不涉及此前 blocked Hero/Crossbow 诊断。
