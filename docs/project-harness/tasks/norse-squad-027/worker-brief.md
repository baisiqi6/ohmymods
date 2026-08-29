# worker 任务书：norse-squad-027 北境骑士小队完整移植（五风格随机+真北境随从）

## 身份与边界

- 你是本仓库 worker。仓库 `C:/Users/ADMIN/projects/ohmymods`（IL2CPP 主线，.NET 8）。
- 允许修改：`il2cpp/PatchRoles_KnightStyle.cs`、新建 `il2cpp/PatchRoles_NorseSquad.cs`。
  禁止其他文件（PatchRoles_Castle 的池重注册接线由 Operator 加）、commit、push、部署、运行游戏。
- 日志前缀 `[NorseSquad]`/`[KnightStyle]`。ModConfig.Enabled 门控。中文注释。
- 编译验收：il2cpp/ 下 `C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug -p:BepInExPluginsPath=NONE`，0 error，删 NONEKingdomEnhancedMod。

## 需求（用户拍板：先加入随机池）

随机池从四风格扩为五风格（新增"北境/norse"）。抽中北境的骑士：本体穿 `knight_norselands`
控制器；其随从在入队时转化为**真北境弓箭手预制体 `Archer_norselands`**（带 NpcShieldUser
盾牌组件→原生近战/盾墙逻辑），并程序化装盾。存量北境骑士（读档）由巡检补转化。

## 侦查实锤（全部已验证，直接采用）

1. **近战钥匙**：`Archer.SetDesiredAttackMode` 在 `_npcShieldUser==null` 时早退
   （Archer.cs:770-773）——无该组件的弓箭手永远 Ranged。组件原生只在 Archer_norselands
   prefab 上。近战本体在 Archer.Attack()/TryMeeleDamage（DamageSource.Knight 伤害），
   盾墙编队门 CanJoinFormation 要求 `_npcShieldUser!=null && HasShield()`（1737）。
2. **入队盾门**：`IsAvailableForJob` 对 Knight 任务要求 `_npcShieldUser==null || HasShield()`
   （Archer.cs:745-749）——带盾组件无盾不能入队。**解法**：程序化装盾
   `NpcShieldUser.SetShieldEnabled(true, 0)`（NpcShieldUser.cs:69-118，无需掉落物）；
   时序坑已由 PatchRoles_Worker.TryEquipShieldAfterRegistration（418-469）踩平——等
   `BeginRegisteringRPCs`（postfix 先例 504-523）、Awake 补跑、regenWait 回填，照抄该模式。
3. **北境 prefab 获取**：PatchRoles_Character.cs（30-128）已有"查北境变体"机制——
   `Resources.Load<BiomeData>(biomePathStrings[NorselandsBiomeIndex])` →
   `swapData.prefabSwapPool` 匹配 original==holder.GetCharacterByTag("Archer").gameObject。
   复用/提取该逻辑拿 Archer_norselands（注意运行时校验 prefab 的 tag=="Archer"）。
4. **随从转化路径**：`Archer.AssignJob`/`SetKnight` prefix 检测"北境骑士+非北境随从"→
   Worker 交替转职同款窗口技巧（PatchRoles_Worker.cs:162-325：prefix 临时替换
   `tagCharacterPairs["Archer"]`=北境 prefab，调 `archer.GetComponent<Character>().Promote("Archer")`
   走 ReplaceBy→Pool.Spawn 池化同步路径换出北境随从并 despawn 旧对象，恢复映射）→
   对新 Archer 调 `SetKnight(该骑士)` 直连（绕过 FetchArchersForJob 的距离筛选）。禁止枚举
   `Knight._archers`（Il2Cpp HashSet 枚举器不可靠，knightstyle2 实锤）。
5. **翻牌治理现成**：北境随从 ConvertToSoldier 时若被希腊 swap 刷皮，KnightStyle 的
   ConvertTo postfix 重涂机制直接对冲——北境风格目标控制器 `archer_soldier_norselands`
   （2.4.0 资产实锤存在，含 attack/defend/getshield/retreat 全套近战 clip，其他风格族没有）。
6. **池**：角色池必须建（读档按 syncID 从池复活）。`PatchRoles_Castle.EnsurePoolForCharacter`
   是 internal/public 可调用（363-387）；新文件里做 `EnsureNorseArcherPool()`（幂等，
   Holder 就绪才动），Operator 会在 ReRegisterModPools 里加一行调用——你暴露
   `internal static void EnsureNorseArcherPool()` 即可，**不要自己改 PatchRoles_Castle**。
7. **读档重招募扰动**：读档后 Knight.Update 会重新 FetchArchersForJob——已转化的北境随从
   若无盾会被盾门拦出队 → 装盾必须幂等重跑（World 巡检每轮：北境骑士的随从中
   `_npcShieldUser!=null && !HasShield()` → SetShieldEnabled(true,0)，复用 Worker 的时序安全模式）。
8. **KnightStyle 扩展点**：StyleNames/控制器解析/体型表（北境 1.0）/
   GetFollowerAnchorPullback（北境→4.2 默认）/BuildAvailablePool/确定性哈希取模自动随池长
   变化（%5）——注意哈希取模池长从 4 变 5 会让存量骑士换脸一次（可接受，注释写明；
   若想避免可改用 %20 映射五风格，Operator 无偏好，你选简单的）。
9. **弩手互斥**：弩手（CrossbowmanMarker）永不入队（IsAvailableForJob 排除），北境转化
   prefix 也要防御跳过弩手。死地随从弩手化包与北境转化互斥（风格不同天然不冲突）。
10. **降级链**：北境随从降级走 tagCharacterPairs["Peasant"]（希腊已被指到 Peasant_norselands，
    闭环）；Archer→Peasant Demote 原生处理，无需干预。
11. 2.4.0 interop 签名全部验证存在：SetKnight/FetchArchersForJob/IsAvailableForJob/
    ReplaceBy/ConvertToSoldier/TryPickUpShield/SetShieldEnabled/tagCharacterPairs/
    soldierAnimator/_npcShieldUser 等（侦查报告计数齐全）。

## 实现规格

### A. KnightStyle 扩展（PatchRoles_KnightStyle.cs）

- StyleNames 加 "norse"；控制器解析加 `knight_norselands` + `archer_soldier_norselands`
  （第五套）；体型表加 1.0；风格索引常量 NorseStyleIndex；随从缩放默认 1.0。
- 死地随从弩手化的 Apply/Restore 分支对北境风格天然走"普通士兵"路径（确认不误触）。

### B. 北境随从转化+装盾+巡检（PatchRoles_NorseSquad.cs 新文件）

1. `ResolveNorseArcherPrefab()`：第 3 条机制，缓存 static。
2. `[HarmonyPatch(typeof(Archer), nameof(Archer.AssignJob))]` prefix：jobObject 带 Knight
   且该骑士风格==norse 且本随从当前非北境（判 `_npcShieldUser==null` 即非北境 prefab）且
   非弩手 → 窗口技巧转化（第 4 条）→ 对新随从 SetKnight 直连 → 返回 false 跳过原生
   AssignJob（我们已直连）。旧对象 Promote 内部自然 despawn。
   转化失败（prefab 未解析/异常）→ 放行原生（北境骑士带普通随从，降级可接受，LogError）。
3. `EquipShieldSafely(Archer)`：复刻 Worker.TryEquipShieldAfterRegistration 模式（同步等待
   不可用时延迟一帧协程或 World 巡检兜底——你按 Worker 先例选实现，注意本文件无现成协程
   宿主时可搭 World.OnLevelLoaded 宿主或借用 KnightStyle 的巡检节奏）。
4. World 巡检（新协程或挂 KnightStyle 现有 5s 巡检尾部——若挂 KnightStyle 文件加一个
   internal 钩子调用）：北境骑士的随从 → 无盾装盾（幂等）+ 非北境随从（读档后骑士重新
   Fetch 拉来的普通随从）触发第 2 条同款转化。
5. `EnsureNorseArcherPool()`：Holder 就绪 + prefab 解析后 EnsurePoolForCharacter 风格注册
   （注意 syncID 分配走 Castle 现有分配器——EnsurePoolForCharacter 内部已处理；确认其
   对非希腊 biome 无门控，若有 biome 门控就在调用处绕过或提出来，报告里说明）。

### C. 明确不做

- 不做北境盾商店/掉落物（程序化装盾已满足）；不做独立获取途径（后续任务）；
  不改 PatchRoles_Castle/Holder（接线 Operator 做）；不动北境工人既有系统。

## 自查清单（汇报逐项确认）

1. 编译 0 error（贴尾部）。
2. 五风格随机分布均匀（哈希取模池长 5）+ 存量换脸一次已注释。
3. 转化链：prefix 窗口技巧 → Promote 池化 → SetKnight 直连 → 装盾 → 重涂北境皮，
   全链幂等（重复 prefix 不再转化）。
4. 读档场景：巡检能给北境骑士的普通随从补转化+装盾。
5. 弩手/死地随从与北境转化互斥。
6. 失败路径全部降级放行原生（北境骑士带普通随从不炸）。
7. 池注册幂等 + syncID 不与银行/幽灵/弩矢段冲突（走 Castle 分配器）。
8. 汇报：行数、关键决策、不确定点（列出不猜）。
