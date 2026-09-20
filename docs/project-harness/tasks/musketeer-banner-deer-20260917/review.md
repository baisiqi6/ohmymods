# 独立初稿复核（修订中，非最终通过）

2026-09-17。范围仅本轮举旗火枪后排及白天普通鹿狩猎。只读两个隔离 worker 源码、原生证据与计划；未改生产、启动游戏或操作存档。root 正在修订及运行测试，本记录不代表冻结候选验收。

## 待闭环问题

1. **P1：招募事务必须覆盖部分完成和部分写入。** 初稿 `banner/work/il2cpp/PatchWorld_FleetBoatFormation.cs:513–561` 仅恢复 types；真实 TryRecruit 先 RegisterUnit，后 ConvertToSoldier，异常可留下成员。另第 543 行临时 types 写入发生在内部 try/finally 之前，中途写入失败不归还。需要从首次写入开始保护，验证成员及 actor formation 最终一致，失败仅归还本事务成员，并保留无法完成的清理责任。替换数组、切世界及他人 occupant 不得被旧事务清除。
2. **P1：功能关闭与失效成员清理。** 初稿关闭只停止 TopUp，已招入后排仍在。修订须使用原生离队链清理确证自有成员，同时保持原 4 弓、4 重步、船及仍有成员的数组。死亡、池复用、同指针新 life、换 formation 必须区别处理。
3. **P1：鹿命中不能只检查当前 GO 对应的新角色。** 初稿弹道只检查鹿；修订中已添加当前 Archer/IsArmed/formation 等门，但 `ResolveSourceArcher` 重新解析当前对象不构成发射 life 证明。旧弹发出后同 GO 回池并重新武装，会再次通过。`IsUsableSource` 的 active/world 也不等于 Damageable 未死；IsArmed 本身不读死亡。需要发射时的明确身份/life 凭据及命中时比较；保留敌方弹道既有语义。测试须含死亡但 GO 仍 active、包尚 Applied，以及同 GO 新 life 再武装。
4. **P1：向下弹必须先裁地面再查询。** 初稿扫描完整段后才做 below-ground 终止，会扫到地面之后的对象。修订中已见交点裁剪，仍待冻结测试验证大 dt、合法地上先命中、地下目标、射程预算及左右朝向。
5. **P2：鹿瞄准证据不足不得 flat 回退。** 初稿 GetComponentInChildren 可能选物理脚圈，缺失/异常则 flat 发射。修订中已改只读鹿根 Collider2D、有限非空 bounds 和失败拒射。最终须核实际根 body 匹配及测试；此结论不证明游戏内命中率。

## 验证要求与已确认边界

- 初稿 banner 测试只链接新策略模块、stub 掉真实数组 owner；不能证明真实 TryDirected/TopUp/finally/cleanup。root 已要求直接编译生产 owner 的事务回归。
- 原生 2.4 四个入口已独立只读映射并解码，地址/脚本在 `native/addresses.json`、`native/entry-disassembly.txt`、`native/inspect-formation.ps1`。RegisterUnit → ConvertToSoldier → currentFormation 的中间失败窗口有实际调用顺序依据。唯一入口及较长地址跨度不等于实机 detour 验证。
- 采用实际 PlayerFormation `[11,5,5,0,0,0,0,5,3,3,3,3]`。prepend Gap 与 startOffset 补偿的布局方向成立；仍需两侧所有原位、0–4 船、原始弓位隔离和撤员测试。UpdatePhalanx 按尾部 Pikemen 实例移动，清理不能凭旧 index 盲删。
- 鹿继续原生 ReceiveDamage 与原生死亡掉钱，不应手动掉币。固定初始方向不是跟踪；几何计算及 stub 测试不能代表逃跑鹿实机命中、举旗队形观感或联机验证。

## 本次读取标识

初稿 banner owner SHA256 `03bf05688fdee67eb7daeb8f5a5b89d8296cad3061e736905f23545eb29cea91`；策略模块 `8fd212f860b0907ce92483c7fb086e8e570471042973523ae6d9347e504e2967`。初稿 Runtime `b3863a1a49df4e1410bf8c26267a8d6e9c9f0252a324c66e3efb640dd0f03f1c`。deer Combat 在复核期间持续修订，观察到中间 SHA `8a530935b4eb997974baddd79c45e6ab68c564282b618fecf4064c5511425f72`，随后地面裁剪继续变化；上述旧稿与修订观察已分别标记，不能作为最终冻结 hash。

**结论：等待修订闭环及冻结候选复核；当前不通过交付。**

## 后续修订预审

鹿 slice 最终小修已独立读回，无剩余已知阻断，精确源码与边界见 `deer-review.md`。这不使 banner 或整包自动通过。

banner 中间版 owner SHA256 `f249e2c8561485040fd2c4f8f44a5e74070481020fd4dc8f354361484a615b29` 仍在修改，发现以下必须在冻结前闭环的顺序问题，已即时告知 root：

- **P1，861–887：** 该版先在 try 写 temporary / BeginDirected，然后 finally 恢复类型并 EndDirected，之后才实际调用 Archer.TryRecruit。此时真实 prefix 已拒绝自家火枪，后排也已恢复 Gap。原生 TryRecruit 必须位于 temporary 和 bypass 的同一保护窗口内；e2e stub 必须模拟真实 RegisterUnit 按 type 从尾部找座位及招募 prefix。
- **P1，恢复失败窗口：** 若 Gap→Archer 成功而 finally 写回失败，pending receipt 仅保证稍后重试，不能阻止其间普通 Archer native Recruit 填入已开放后排。初稿 prefix 只挡火枪身份，普通弓仍放行。需要在定向窗口及尚未恢复类型的 dirty 窗口禁止其他招募，或同等窄保护，并测试持续写回失败之后普通弓的真实招募路径。否则普通弓可能占后排，维护只移除数组引用后还留下其 currentFormation。

以上是中间源码发现，不冒称最终修订仍存在；待 worker 冻结后重核。
