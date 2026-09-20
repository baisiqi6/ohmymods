# Defense follow-up — 2026-09-17

本轮补齐独立复核指出的三个缺口：最后一次转职及读档完成后缺少分配事件、共享三秒缓存漏本次普通弓手占位、无墙原生回退被错误拒绝。源码与离线回归完成；未部署、未启动/操作游戏、未读写用户存档或配置、未进行 git 操作。完整插件构建由 root 负责。本报告更新旧 `defense-result.md` 中对应结论；旧报告不作为本轮实现描述。

## 改动

- `MusketeerIdentity.IslandState` 新增三个仅会话字段：`DefenseAttempts`、`DefenseBindFrame`、`DefenseWorld`。成功绑定 Archer 后登记同 state 的待处理事件，并保存绑定 `slot.World`，多次绑定合并。既有 `Tick` 在更新可变 `state.World` 前核对事件自己的 world，不符即取消；同存档context复用旧state也不会把旧事件带入新world。之后仅在 state Ready、转职 finalizer 已退出、至少下一帧且 Playing 时消费；菜单不消费重试预算，关闭配置则消费事件且不调用原生。每个合并批次最多三次尝试；平常帧仅检查整数/状态，不扫描、不调用原生。字段不加入任何 archive DTO/schema。
- `MusketeerDefense.RedistributeAfterBindings` 在捕获/分配之外发起一次 `Kingdom.DistributeFreeArchers`。原生方法完整运行，原有 prefix/postfix 才修正 marked subset；不加 `SetGuardSide` 短方法 hook。原生抛异常遗留的捕获在下一帧重试前失效；同步重入返回等待，不嵌套调用原生。重入阻止或原生调用抛异常才消耗至多三次尝试预算，此后等待其他自然分配事件，避免无限重试。若原生正常返回，事件即消费；Begin/End内部捕获的策略异常、不具资格、override及缺census等本轮跳过不会触发这套重试。
- `CensusOccupiedSlots` 改为读取刚完成的原生分配列表 `kingdom._availableArchersCache.Count` / `[i]`，不再读 `UnitScanCache`、不遍历原生 HashSet。列表缺失则本轮不写。普通单位在原生本轮刚出生并占 0/1 槽时可立即被读到；MOD不写普通单位。原生额外分配本身仍依原生规则处理普通单位。
- `SplitOpen` 保留 `HasOverrideGuardPosition()`，再用原生 `GetGuardPosition(Left/Right)` 的 Key/Value 判断两个目标分别仍属原侧且位置有限；号角/战役重定向继续让位。删去“两边 intactWall 都非空”的假设，缺墙使用原生营火/最小疆域回退。单侧安全状态仍不影响 marked subset 自身均衡。
- 原有 online/authority/current kingdom/world、登船/待登船、tower/guard slot、knight、formation、死亡/隐藏/玩家控制/高度等门保持。`MusketeerIdentity.cs` 仅事件字段与 Bind/Tick 接线有改动。`Character.DropItem` hook 撤除与 `Droppable.Drop` 显式 `__0` 来源转职移交保持。

## 证据

在 `C:/Users/ADMIN/projects/ohmymods` 执行，均返回 exit 0：

```text
C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/musketeer-defense/Tests.csproj
PASS 128 assertions (production MusketeerDefense + hook bodies)

C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/musketeer-defense/Integration.csproj
PASS 118 integration assertions (real Defense + Identity + Persistence + Archive; modeled native OnEnable ordering)

C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/musketeer-identity/Tests.csproj
PASS 273 assertions (real musketeer archive/identity/persistence)

C:/Users/ADMIN/dotnet8/dotnet.exe build tests/musketeer-defense/Interop.csproj
Build succeeded. 0 warnings / 0 errors.
```

Interop 工程引用 E 盘实际 2.4 interop，仅只读。`InteropStubs.NativeIndexWitness.At` 显式声明 `Il2CppSystem.Collections.Generic.List<Archer> list = kingdom._availableArchersCache`，使用 Count 和索引器编译通过，验证真实字段具体类型及索引访问，不只是替身里宣称。`GetGuardPosition` 的 Key/Value API 同样由真实 interop 编译验证。

原生逻辑证据仍分级：2.1 只读参考 `Kingdom.cs:2646–2671` 每次 Clear/Add/Sort 当前 available 列表；`Kingdom.cs:780` 的 GetGuardPosition 缺墙回退为 campfirePosition + minKingdomExtents * side。2.4 字段类型/API 经真实编译核对，但本 worker 未逐条反汇编 2.4 方法体，不把旧版参考称为 2.4 方法体实证。

## 新回归覆盖

- 原生同店出生分配发生在身份标记之前：联合工程编译真实 Defense、Identity、Persistence、Archive；每次执行真实 `OnGunPickupBegin` → 未标记后继 OnEnable 的原生分配替身 → 真实 `OnGunPickupEnd` 绑定 → finalizer → 下一 Tick。连续四次，每次完成后两侧差不超过 1，最后 2/2，无需额外昼夜/购买事件。
- 转职作用域未退出时跨帧 Tick 也不重新调用原生；嵌套补分配被拦；原生抛异常恰好三次后停止，遗留捕获不会吃掉后续重试。
- 真实 `CaptureLoadRow` 恢复四名 Left 身份，Ready=false 时不会补分配；真实 `LoadCapture.End(true)` 设置 Ready 后下一 Tick 只发一次原生分配并完成 2/2。测试通过反射注入 LoadCapture 内部 state，避免创建任何用户档或真实磁盘档；本场景未请求 archive commit。
- 同一暂停批次四个绑定合并；Menu十帧不分配也不消耗预算；恢复只一次变为2/2，后续反复暂停/恢复零原生调用、零额外守位写。
- 新普通 Archer 不在旧共享缓存但在当前 native list，MOD mover 避让它本次深度0；普通 Archer 零MOD写入；本轮不读缓存；缺 native list 则不猜槽。
- 左墙缺失、两墙缺失仍使用有效原生回退；horn/campaign侧别重定向、非有限目标不做split；online/关闭/换world事件门保持。
- 补充review后的world反例：不调用Setup、不清Islands、不换GlobalSaveData/CampaignSaveData或同一个IslandState，只更换gameLayer与kingdom；旧事件在新kingdom分配前取消，随后新world自己的两个绑定仍能合并一次分配为1/1。该测试在world修复前真实exit 1，失败点为 `same-context world switch cancels old binding event before invoking new kingdom`；修复后联合断言118通过。此前只用Setup换state的测试不足，此项专门补足。

## 范围与限制

修改限 `il2cpp/MusketeerDefense.cs`、`il2cpp/MusketeerIdentity.cs` 的事件接线、`tests/musketeer-defense/**`、身份测试 `Stubs.cs` 的新Defense方法替身，以及本报告。没有改其他存档/drop/商店/视觉/骑士slice代码。

原生 OnEnable、x排序与写字段由测试替身按已知时序建模；Identity的转职、绑定、Tick、load完成与Defense策略执行的是生产代码。离线通过不能证明真实 detour 命中或实机步行动作、号角/战役内容、跨岛与联机验收已完成。读档/四次购买的真实2/2、夜间守位表现仍待实机；完整插件0W0E及独立review由root统一记录。
