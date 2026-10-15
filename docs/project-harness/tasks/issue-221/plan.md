# Android 神器参数共享移植设计与实施计划

Goal: 保留Android0.0.29成果，新增PC现有神器增强：权杖转化上限32、扫描范围原生×2、转化不自动恢复敌对，以及Artemis单箭伤害次数规则（当前原生公式确认后决定是否可承诺20）。同一批默认关闭/下次冷启3个显式目标，复用两个PC共享源和现有中文金色面板，不新增driver/周期扫描/身份账本。

Baseline release/v9.5.13 d355d93bec8f46ca8c056e801d3a9bb1dbbd5439；AndroidProbe0.0.29/64源/30prefs/default51目标。PC 243源码与当前W matched。身份/弓弩侦查保留，仅编译type边不是精确file闭包；不引用22个d<=2前沿为完整最小组。PR204职业保存待Main原生关口，不提前移入/修改其PC职责。Main已接手canonical窗口，Root本批零canonical mutation，待窗口交回后正常登记认领/验收。用户已授权自主按风险调整顺序。

Design: shared il2cpp/PatchDivine_HermesStaff.cs与PatchDivine_Artemis.cs加最小ANDROID别名/真实enable gate，只ANDROID排除PC法杖ApplyCooldownProfile、固定30秒常量、SettingChanged场景扫描和CanActivate/TriggerItemAbility两个包装（Android已由PatchDivine_StaffCooldown两个调用期hook独占基础冷却）；PC预处理逻辑严格保持。保留PC32/max与×2原始值机制，不重写算法。ANDROID三个入口默认OFF则不读native对象；enabled同时限定现有offline/当前world scope，原生参数机制复用。Awake×2现有Dictionary原始值证据须审计：强引用key不会被GC回收，不能沿原注释承诺回收；若需修错误产生处须先最小diagnostic区分，不借新OnDestroy短detour/周期清扫兜底。暂不改此算法，记录PC已有局限并以同一共享实现复用；审查若认为阻塞，则暂停该子项而不假称修复。

Config/UI: 真实MelonPreferences_Entry<bool> DivineParametersEnabled=false，旧30键逐值保持。单一保存路径ToggleDivineParameters调用Save一次。玩家页共享Step显示“神器参数增强 / 下次开启或下次关闭”，中文帮助精确权杖32、范围2倍、保持转化与神器箭规则，说明冷启生效及不含头饰/友好巨魔其他行为；不覆盖原CD控件。不复制样式/座标或另造浮窗。

Registration: Operator显式registerHermesStaff.Awake postfix、FriendlyTroll.ShouldRevertToTroll prefix、ArtemisArrow.DamageAffectedEnemies(GameObject) prefix，具体签名/long唯一native安全通过再启用。整组false不register，不创建Range字典项/扫描。无需新注入类型或池：仅原有staff/troll/arrow；不生成跨世界对象/扩充Holder/sync池。默认51/ON最多54，源共享2+Android原64=66。Scope限制维持按实际shared/noRPC能力，不升级MP验收。

Alternatives: A推荐本组共享最小ANDROID分责，已有CD继续唯一负责，保留PC算法。B直接链接PC完整Hermes会固定写30秒并重复倍率、加全场扫描，拒绝。C重写Android神器算法/新轮询，也拒绝。一次有界worker slice负责两个共享源的必要条件编译和生产源direct-host实质测试（defaultOFF无native触碰；ON32与×2幂等/原CD字段不变、永久revert与Artemis字段写零；异常不会新增scan），不写Main/config/UI/project/Git/GH/SSH/设备；Root统一接线。实际行为测试stub只在测试工程，产品绝不假cfg/身份替身。

Verification: 在真实Android interop/SDK10构建0W0E；既有Adapter所有变体及新增真实entry保存/无allPatch扫描/sourcepin、typed3targets/关闭注册0；PC完整243源码语义/资源与原baseline零差异（条件编译导致PC无差异）。独立review先审分责/Dictionary风险/原生公式，再实现。原生静态method metadata、FDE/PT_LOAD/地址折叠及调用链只针对三个selected入口与必要直接reader（目前未有安全事实），短共用方法不强行detour；不据旧注释当事实。

Delivery: GH Issue对齐本范围和真实baseline后独立codex/android-divine-parameters worktree；普通源候选可在canonicalMain忙时离线准备，但不虚称normalaccepted；申请窗口后正常登记/planapprove/accept，保持169/order/11plans/local119/root7。所有代码审查与必要检查通过后owncommit/push/PR、fresh exacthead文件/CI读回normalmerge；私有APK/备份/注册ON-OFF有限验证另经安全gate，未经native目标审与新备份不安装、不启动。任何短共用native不可安全接则做完整reader分析/选原生产生处其他入口，不能靠managedmethod唯一。最终Issue与normal只按实际scope交齐合并/receipt收尾，天然转化/实际20命中/重复Awake/池/读档换岛/phoneMP/long保持pending。不正式发版/tag，不写Steam或Mac正常游玩环境。

Checklist:
- [x] 恢复基线/用户授权/逻辑地图/根因规范；与Main确认共享保存职责与窗口交回。
- [ ] 独立计划及GLM5.3/max决策审；字典与当前native式不能预付。
- [ ] GH Issue/独立branch/worktree，只产品候选离线实现；normal后续窗口认领。
- [ ] Worker先测试再最小共享条件代码；Root真实配置/UI/project/Probe。
- [ ] 真引用编译+生产共享host+PC严格语义+原生具名静态+必要审查。
- [ ] 正常受管/私有有限安装验证/ownGit交付与收尾。


## 首轮审查后修正契约（以本节替代前述Awake/范围2倍/永久字段写方案）

已确认两风险：现OptionalQoLScope不包含offline，Friendly prefix缺instance；PC OriginalAbilityRanges强key字典不会自动GC。原计划Awake永久写在scope失效后不归还。不能以defaultOFF或scope早return解决这两个责任问题。

本批收缩为权杖单次转化上限32+条件禁止恢复敌对+Artemis单次原生结算max字段借0，不移植范围2倍；不引入原强keyDictionary，不顺带改PC历史算法。权杖冷却/范围/Scanner保持原先责任。ANDROID不编入Hermes的ApplyCooldownProfile、OriginalCooldown常量、SettingChanged扫描、Awake/CanActivate/Trigger包装、OriginalAbilityRanges/AbilityRangeMultiplier；原EffectiveMaxConvertedTrolls32常量复用。PC非ANDROID预处理仍完整原文无语义变化。

新真实门由一个shared Android-only PatchDivine_Parameters.Scope(Component)集中：真实ModConfig.Enabled/DivineParametersEnabled、!NetworkBigBoss.IsOnline、NetworkBigBoss.HasWorldAuth、既有OptionalQoLScope.IsCurrent(instance)与OptionalQoLScope.IsActive（只验证当前BiomeHolder.BiomeIndex>=0，不限定希腊；与PC所有world保持一致）。仅已有native神器实例可入门，不生成/跨world导入对象。这不是引用旧门就称offline，所有条件显式，native调用期对象当前实例归属。没有新增场景scan/静态账本；null/configdefaultOFF在任何native对象读前退出。Friendly prefixANDROID有FriendlyTroll __instance参数，PC仍原refbool签名。

权杖32改为HermesStaff._StartAbilityRoutine_d__17.MoveNext调用期借 _maximumConvertedTrolls，和原AndroidCD同一target但两个独立字段责任：prefix仅state0、记录真实进入值→写32；finalizer按同receiver+现字段==32归还original，不重算CD、不写range/scanner或已排程时间。原生调用异常原样传播，写失败只归还已写的本字段，不加重试/镜像。仅当当前nativebody证实截断只在该state0同步调用内读取时才安装；跨yield保存常量上限的语义须用当前reader证据解释，而不是猜测。

Artemis DamageAffectedEnemies调用期借 _maxHitsPerArrow=0，新增ANDROID前缀out Borrow和finalizer，native原生完整函数执行后按现值==0归还真实original。不留永久字段改写，scope失效自然下一调用原生。当前原生reader若在方法return后访问或写本字段则先审责任点再实现，不能预付20命中；代码层只声称结算字段取0直到当前公式证实。

两个borrow只存当次对象引用/原值/是否已写，不静态缓存/不新通用framework/不二次identity仓库；被调方法实例稳定、原state0native直接链与字段写入者须具名审。FriendlyShouldRevert不写任何字段，只本次scope有效返回false；OFF/online/失效走原方法，中文注明“启用时转化不自动恢复敌对”，不是无条件永远永久。

最终newtargets仅Friendly和Artemis2个，HermesRoutine复用原target，default51/ON53；5 handlers到3 targets（Hermes prefix/finalizer、Friendly prefix、Artemis prefix/finalizer）。UI中文3项，explicit不含range2或头饰/友好巨魔额外战斗，不把未移植子项称本批完成。群组冷启flag真实31prefs；native未知时不安装。该修正待同reviewer复审与GLM新契约审；原首轮完整计划保留作为被推翻方案，当前生效contract以本节为准。

术语校正：本计划早期使用CAS作简称，实施契约实际只是读取当前字段与applied作等值比较后归还，不是原子CompareExchange，不宣称跨线程原子性。原生执行仍同步同调用线程。


## 当前原生证据后的Artemis修正（替代本批写0方案；PC线留原逻辑）
metadata31.1/so77487143证明Artemis._maxHitsPerArrow Single/offset0x20；原函数0x2315b28/FDE2296B在0x2315e2c加载字段、e30与计数s8比较、e34 b.ge跳过候选，初始s8=1并每成功候选+1。未见旧注释声称的“字段+20f”，不采用其作为当前公式依据。ANDROID前缀需借真实字段20f，finalizer按当前==20f条件归还原值；PC前缀写0保持原有IL，PC问题交主线单独当前native核验，不在此借Android证据擅自修PC。UI只“神弓当次结算命中上限20”，天然实际单箭一生伤害/碰撞、有效目标数不足与穿透行为全部pending，不承诺固定20实际命中。字段读取在本同步函数候选循环，当前完整函数未写0x20，越过下游scanner/伤害逻辑保持native算法。下一安全判定仍需具体直接callee是否同字段读写/重入证据，未见的不伪造为通过。


## 精确命中分支与开关语义澄清（最新生效）
独审核本函数发现计数s8在0x2315c2c先清0，只有直接hit支路成立且调用伤害后cac设1；fe8候选成功+1。函数60a4–6178另有数组循环6168伤害调用，不经过max0x20门。因此不承诺“整个当次结算上限20”，只承诺本字段控制的候选循环max20，其他原生伤害分支保持。UI帮助“神弓范围候选最多20；其它原生命中规则不改”，不是固定总20/每生命20；自然真实玩法仍未验。原“s8无条件初始1”被此本函数证据纠正，native-findings同步记录而非继续当依据。

flag既决定启动是否注册，也在每个实际handler入口读取；不用新的启动状态镜像。默认false不注册，开启后重启完成注册；在已注册会话关闭则下一调用真实flag门返回原生（本次借据仍正常finalizer归还）。UI帮助准确“开启后重启，关闭从下次调用生效”，不宣称所有修改都只能下次启动生效。重新启用已注册会话可能即刻生效，重启并非每次必要，但按最初安装情形提示，注册形状仅下一冷启才改变。已转化Troll在停止阻止恢复后按原生时限判断，未造反向即时变敌人方法。
