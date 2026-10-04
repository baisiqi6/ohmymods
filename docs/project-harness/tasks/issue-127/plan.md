# Issue127 — Android 坐骑技能冷却调用级适配
- GitHub: https://github.com/baisiqi6/ohmymods/issues/127
- Owner/actor: mac-codex-ohmymods-android-operator
- Session: codex-android-steed-cooldown-20261004
- Branch: codex/android-steed-cooldown
- Coding host: /Users/Admin/.codex/worktrees/android-construction/ohmymods
- Source baseline: 0107c8ced9080065b846e06d2160815d7459e631 (PR124 merge)
- Dependency: issue-122 normaldoneClosed before implementation
- Plan approvals: OMP zhipu-coding-plan/glm-5.3 max no fallback, session01a105da-03b6-7707-a7d4-0704c00c93c8 APPROVE_PLAN_C3 then APPROVE_PLAN_C3_R1; previous repeated-cleanup condition2 explicitly superseded, single cleanup adopted.
- Workflow authority: metadata create/planapprove/dependency/accept only at registration; not implementation/install/device acceptance. Exact candidate installation and delivery require their actual artifact gates. Code task closes after review+necessaryvalidation+PRmerge, gameplay remains separate.

范围：Android单块兼容，源码基线PR124正常merge0107c8ced9080065b846e06d2160815d7459e631，PC和Mono完全不改。自然当前Playing玩家马Horse Regular P1 Greece(Clone)，playerSteedAbilities=0/scene total0；不制造RoyalHorse身份或实际技能验收。证据D/steed-d-query/device-natural-r1，冷却详细本体/协程见既有steed-cooldown-recon及D2；Glide尾虚调经actualmetadata slot9与loadedInteropClass29_2 SizeOf0x138/VirtualInvokeData16模型静态解析为Glide.Deactivate ptr2594e10，本体46insns直接cooldown/next读写0；handler选择IL未核、非运行时布局测量、间接callee未展开。SetAnimation slot10本体82insns直接cooldown/next读写0。都不是整个native传递闭包。

## 语义及最小写面
原PC模块输入CD倍率0.2..1，SummonGhost排除；PC长期instanceID Native/LastApplied缓存与SettingChanged扫场未证明原生来源/生命周期且不需要移植，Android使用真实消费者当次借用。这是相对进入时当前_cooldown乘倍率，稳态字段还回当前来源，不追溯现有_nextActivationTime、不会Prefab原生值猜测。Enabled默认真；倍率默认1零写/零marker/零借用state分配。
恰4native hook：SteedAbility.Activate、BuffUnitsSteedAbility.Activate、GlideMovementSteedAbility.Activate、SpeedBoostSteedAbility.Deactivate，各Prefix+Finalizer，不追加协程/OnEnable/newdriver。已核四消费者本体，仅Buff/Speed协程state0在StartCoroutine当次首yield前消费一次cooldown；Glide不足体力time+3路径不消费cooldown，必须保持3s。上述最小查询无新directwriter；如未来产生新writer证据则消费集合重审，不自我宣布全native闭包。
沿PC既有全体ability范围，除SummonGhost，无新本机rider/world/scene限制、无无依据guard。仅Enabled且m!=1且非null时借用；Ghost只在base hook一次真正interop TryCast<SummonGhostSteedAbility>判定（而非托管is）；Buff/Glide/Speed三种由目标类型确定，不重复TryCast。

## 调用级状态
每个非默认consumer Prefix捕获同一native ability reference、Pointer、original_cooldown、applied=current*m为本次__state，先记录归还凭据再set；Finalizer在成功/原生异常路径归还。保持原生Exception原样返回，不抑制，不改__runOriginal/result。仅一个finalizer，不加postfix重复cleanup。前缀自身getter/setter异常仅一次End清理已借状态，并finally清空__state后放原方法继续；Finalizer不得再归还或重试。成功和原生异常仅Finalizer一次End。End内归还setter失败只日志，不重试/补偿；Buff owner在finally无条件归还。没有retry/fallback读值/扫描。
仅Buff借用窗口static IntPtr owner，本次__state保存previousOwner；base Prefix只有__instance.Pointer==owner才不再借用（包括m=1总开关发生变化也仍不叠乘）；跨对象Base仍正常借用。Buff结束无论字段归还成功都finally还previousOwner；窗口外ownerZero。不是持久identity/cache/dict/通用递归保护。不要假设没有跨对象nativecallback。
归还采用 exact float数值等值 current==applied 条件归还（非原子CAS、非bit equality/无epsilon；不覆盖不同值外部写，不能识别写了同值的外部来源）：已知consumer无直接cooldownwrite但未知其它mods/native回调可写，CAS仅避免覆盖期间真实外部变更，不用epsilon猜身份/原生值。不新增instanceID/跨scene重建/lease。native能力reference失效的归还失败日志诚实，不二次补偿或向其它对象写。
例10*.5：普通base schedule5/稳态10；Buff内base不二乘，normal rewrite5、earlyreturn留下5；两者exception finalizer也还10+owner。跨对象Base在Buff内独立乘；改变外部原值后下次取新值；已排程Next不回写。另20*.2→4，m1零修改、Ghost零修改。

## 配置和UI
Worker Android专用PatchRide_SteedCooldown.cs（实际消费者差异的薄适配，算法不复制PC缓存）；MobilePlayerConfig单SteedCooldownMultiplier MelonPreferences float entry default1（总10条），复用现有SanitizeMultiplier并传min/max .2/1（敌人两项仍1/5），load finite Clamp .2..1，非finite warning1/default1仅加载边界，UI cycle 1→.8→.6→.4→.2→1，载入中间值选下一较低20%档：index=MathF.Ceiling(m*5f)-1，index<=0回1f，否则index/5f；切换仅entry+日志+Save一次，无原生重写/eventscan。
Player保持Speed98/Stamina176/Hold254，新增Cooldown332，Back410，PanelHeight Player484。Home484/World562/Population376/球48visual72squarehit保持。文案说明next ability call/current cooldown；不用“原生Prefab恒倍率”或让用户确认工程步骤。
Operator统一 Probe显式4target注册、原生fullmetadataMethodPointer唯一证据、0.0.14/marker；不自动扫描。为免base/glide计数重复，先加4cooldownpatch但不日志，随后既有PatchStamina计数base/glide得到final2prefix/1postfix/1finalizer；Buff和Speed在末尾只记录一次。总18唯一reportedtarget（旧16+新增Buff/Speed2），默认所有target准确shape。

## 验证和分工
Worker只Patch/配置/UI/layout/csproj/tests/README；Operator只entry注册/精确metadata/合同及Issue正常生命周期。先GLM契约+测试范围审查、GitHub新Issue对齐、normaltask create/planapprove/dependency122doneClosed/accept、复用本chat干净且当前候选已合并的 /Users/Admin/.codex/worktrees/android-construction/ohmymods，从0107c8新建codex/android-steed-cooldown分支后实施（不重置或删除现有tree，不重复建checkout）。当前只草案，无产品动笔。
测试须实际生产Patch源在host最小typed stubs下执行嵌套早退/异常/跨对象borrow/factor1/Ghost/CAS externalchange，不重写镜像算法、不伪造Unity帧或宣称native证明；断言native消费侧拿到的值、异常对象identity、实际setter次数（不能只有终值），无同步嵌套Buff→另一Buff→回调旧Base的事实时不加通用stack，不宣称marker消除全部未知回调。真实AndroidInterop完整build0W0E；实际产物metadata/IL连线与旧冻结、全部既有host回归。默认/加载.5/边界invalid/单次save/geometries测试。
独立审查精确源码+产物，GLM exactAPK安装/交付关口。APK原资产/签名审计main唯一、备份旧DLL/prefs/native档，不发行，不phone/PCdeploy。装机默认1仅主Mod/18unique正确counts/无ERROR，真实UI倍率持久化和旧flags保持，不强行激活当前无能力马、不直接写钱包/存档/技能字段。自然实际技能未触发明示未验；静态/host不能称游戏技能完成。后续可独立私有fixture需新具体契约和安装门，不自动扩大本块。
只代码PR，base release/v9.5.13 attach对应，审查验证+正常merge后close代码scope/canonical；phone/有技能坐骑/MP/池复用/跨岛真实玩法单独待验。继续下一依赖块，不为关单造玩法结论。
