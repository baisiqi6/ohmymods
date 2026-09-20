# 原生入口与方案只读预审

当前是实现前/实现中的边界审查，不是最终候选通过结论。没有运行游戏、写存档、安装或修改生产代码。

## 实际 2.4 入口

`inspect-formation.ps1` 从实际 interop 类型初始化器取 method token，经既有 mmap-only mapper 关联 GameAssembly.dll，Iced 解码到下一 distinct method entry。结果及输入 hash 见 addresses.json、entry-disassembly.txt、input-hashes.json。

| 方法 | token | RVA | Assembly-CSharp 同地址槽数 | 到下一不同入口的跨度 |
|---|---:|---:|---:|---:|
| Formation.RegisterUnit | 100669516 | 0x531b10 | 1 | 640 bytes |
| Formation.Recruit | 100669531 | 0x5311d0 | 1 | 2272 bytes |
| Archer.TryRecruit | 100663874 | 0x4b80b0 | 1 | 560 bytes |
| Player.ActivateFormation（已有 hook） | 100675592 | 0x68fc00 | 1 | 224 bytes |

这些不是短 getter 或该程序集 method table 内的折叠共享入口；跨度可能含对齐，不声称等于精确函数长度。最终仍应审实际新增 hook 集合、参数及安装结果。未建议拦截 GetFormationUnitTypes 或其他短 getter。

实际 TryRecruit 在 VA 0x1804B8223 调用已映射的 RegisterUnit，成功后才执行后续状态转换，至 0x1804B82AF 才写 formation 字段。2.1 参考明确中间是 ConvertToSoldier / shield 设置。因此仅把 RegisterUnit 返回当整个入队成功不够：转换或后续回调失败时，可能已有占槽而演员尚未完成入队，必须有最终事务清理。

## 编队实现验收边界

- 只修改 FleetBoatFormation 同一数组 owner 的合成布局；四个火枪保留槽位放在原弓手后方（较低 index），左右镜像验证。原 RegisterUnit 从数组尾部找空匹配槽，不能只加四个 Archer 类型槽就认为不会抢原四弓槽。
- 空保留槽保持合法 Gap，定向招募只开放自己的目标槽；临时 type 更改用 finally/CAS 还原，不把普通弓手招进火枪区。无新 UnitTypes 枚举值、无第二 owner 写数组。
- 失败/异常只退出本次新增的成员，不清原 4+4 或船；最终 TryRecruit 状态/结果、当前 world 与演员 life 必须核对。Unregister 会调用 UpdatePhalanx，postfix 不按未经复核的旧 index 封掉别人的槽。只有所有 units 为空时才恢复/缩短原布局。
- 0..4 船与 0/1/4/超额火枪组合、原弓槽空缺、重复 Recruit/Activate、收旗、死亡、回池、开关/切 world、异常路径须覆盖。候选从已购身份名册取，不生成兵或枪，不扩联机，不混盾墙图腾。
- ConvertToSoldier/Hunter 会换 controller 并 force SetDesiredAttackMode；需要组合测试 native 招募/离队顺序与真实 Musketeer runtime，验证身份、枪械 clone、视觉和既有回执兼容，而非仅用 stub 设置 _currentFormation。

## 猎鹿与几何方案

- 白天、Deer 根组件及其自身 Damageable、来源当前有效且非 formation/knight 等事实，在缓存过滤之外仍需发射/命中终检。保留旧 scanner predicate 的 AND 与 CAS 归还，读取异常拒绝。普通敌方分支不加白天限制，兔子/其他小动物不受伤也不吸弹；原生 ReceiveDamage 负责死亡和掉钱。
- root 的实际资产证据表明现有枪口平线高于已缩放鹿的 body。仅对本发已确认 Deer，在平线不交实际有效 body bounds 时确定一次朝 body 中心的直线方向，是可接受的有界方案；不改变敌方水平弹道、不抬枪、不扩大 hitbox，不后续追踪目标。
- 新方向必须有限、非零并单位化；Travelled/range 与命中距离使用欧氏线段长度。现代码部分使用 Abs(to.x-from.x)，不能沿用作斜线 hit.distance 上限，否则会重新漏伤。
- 保持完整 dt、剩余寿命/射程钳制、最近有效障碍与 Crusher 消费语义；先扫有效路径再终止。测试鹿左右两侧、flipX/父朝向、近零 dx、无效 bounds、途中目标移动、兔鹿重叠、鹿前敌人。近距离可能不是“微”向下，应准确描述固定方向，不暗加未经要求的角度帽导致又打不到。

实际游戏的编队站位、注入 hook 执行、鹿 collider 和掉钱效果仍待实机验证；本报告不扩展旧 blocked Hero/Crossbow 诊断。
