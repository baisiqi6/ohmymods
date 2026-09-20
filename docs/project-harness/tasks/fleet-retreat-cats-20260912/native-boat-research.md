# 希腊小船仅接纳希腊骑士小队：只读设计核查

结论：可以按“舰队小船只分配 owner style3 的骑士及其原生随从”实施，但要在**乘员分配**层限制，不能只改旗帜阵型船槽，也不能在上船动画开始时才拒绝。`min(可用希腊小队数, 可用船数)`适合作为同一次召集的成功匹配上限；它不是现有原生代码已经执行的公式，也不应直接覆盖船所有权、主船容量或存档船数。

本次没有修改 canonical、游戏、存档或配置，没有运行游戏。只有本报告写入指定研究目录。

## 1. 对象必须分清

- “希腊小船”对应 **FleetBoat**，参考资源名 `Prefabs/Objects/Boat_Fleet_Greece`，由 `BiomeHolder.Inst.curBiomeAssets.fleetBoatPrefab`提供。原生 `MAX_FLEET_BOATS=4`，现有恢复代码也以4为上限。
- 主跨岛船是 **Boat**。`il2cpp/PatchWorld_BoatCapacity.cs`只临时调整主 Boat 的 OnEnable 容量：worker8、knight6、pikeman8、farmer3；Archer布局不改。它不是舰队小船的每船小队容量。
- FleetBoat.OnEnable（2.1参考第158行）用 `_numSquads`注册 Knight 乘员槽；参考默认 `_numSquads=1`。这是可序列化 prefab 字段，不能把默认值冒充本次实际2.4 prefab测量结果。若要严格“一船一队”，实际当前槽数应纳入验收。
- FleetBoat还有弩炮工人及跨岛时其他乘员。用户“只让希腊骑士小队登船”不应被推断为禁止弩炮工人、农夫和长矛兵登船；应仅约束 Knight小队通道，保留原生其他乘员用途，除非用户另行明确改变。

## 2. 原生招募、登船、下船与召回

### 小船加入旗帜，不等于骑士小队上船

`Player.ActivateFormation()`启用 formation、设玩家所在侧，然后 `StartRecruiting()`。现有 `PatchWorld_FleetBoatFormation.PlayerActivateFormationPatch`保存原来的一个FleetBoat槽，并根据 `TryBuildCandidates`得到的可用船数扩展船槽；postfix对候选调用 `boat.TryRecruit(formation)`。

候选要求：当前世界gameLayer下、active/enabled、同请求侧、船编号1..4不重复、未在其他formation、native状态允许加入、IsAccessible以及native CanJoinFormation通过。现有代码**没有按可用希腊骑士数截断**。原生 FleetBoat.CanJoinFormation只检查PlayerFormation及世界存在工人或骑士，不判骑士style。

因此若用户指“举旗最终带出几艘载队小船”，可在该次候选规划中增加小队匹配数；但仍需下面的乘员资格限制，否则被召来的一艘船仍可能自动吸收非希腊骑士。修改船槽本身不会改变乘员分配。

### 骑士分配由 EmbarkableRegistrar 完成

参考 `EmbarkableRegistrar.CalculateEmbarkeeAssignments`先填 `_tempUnitCache` 和 `_tempSlotCache`，调用JobAssigner.Compute，成本由 `CalculateEmbarkeeScore(int embarkeeIndex,int slotIndex)`计算；`ApplyAssignments(int[])`再次校分数后调用 `Embarkee.SetEmbarkableTarget`。

成本规则包含 CanEmbark、距离、EmbarkingPenalty、船Priority，已登船减2000、原目标及原槽匹配减4000；拒绝阈值是100000。槽缓存只纳入该类型允许登船的Embarkable。它是全局分配器，同时服务主Boat和FleetBoat，所以限制必须识别**目标Embarkable所属FleetBoat + 候选Knight**，不能全局拒绝所有非Greek Knight.CanEmbark。

小船在WaitingForSquad、Attacking、ReturningToBase、InFormation等状态切换Knight boarding permission。`CallPassengers()`及`OnBoatSummonBellRang()`又启用Knight/Pikeman/Farmer/Worker，并切WaitingForSailAway。`Boat.OnBoatSummonBellRang()`会先通知所有FleetBoat，再打开主Boat全部登船权限；这与玩家旗帜是不同的召集入口。

### 随从是骑士带上来的 stowaways

参考 `Knight.OnEmbarkComplete()`启动 `EmbarkArchers(Embarkable)`，逐个原生 `_archers`成员间隔0.2秒 `archer.Embarkee.SetEmbarkableTarget(embarkable,-1)`。slot=-1对应stowaway，不能给每个弓手另占一个Knight槽，也不能简单用全局Archer风格筛选替代真实队籍。

FleetBoat记录上船骑士的 `numArchers`到 `_squadArcherCount`，`HasEnoughUnitsToDepart`比较已登船Knight与已分配Knight、stowaway数量与 `_squadArcherCount`，并等待弩炮工人条件。若在**已分配以后**只阻止某个骑士/随从上船，等待条件可能永远不满足。这是必须优先在分配之前筛选的原因。

船回到Idle时 `OnIdleCallback()`调用 `DisembarkAll()`；骑士离船也有原生 `DisembarkArchers()`链。应继续使用原生Disembark/target/slot流程，不直接改Transform、人数、IsEmbarked或船状态来“修正”乘员。

弱点攻击是第三条船调用链：`FleetBoat.SendOutToAttack(weakPoint)`进入WaitingForSquad；弱点销毁/移动后进入ReturningToBase。仅限制举旗路径不能覆盖弱点攻击及主船铃铛路径。

## 3. 最小可靠实施设计

1. **统一资格函数**：候选是当前世界活跃、存活、身份有效的Knight，`PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight,out style)`成功且 `style==3`；沿用原生CanEmbark、已有登船/目标/任务规则。以owner风格为准，不根据贴图名称或弓手effectiveStyle判断。style尚未恢复时不猜，等原有恢复完成后再分配。
2. **分配成本门**：优先在 `EmbarkableRegistrar.CalculateEmbarkeeScore`的候选/目标索引对应对象上，对“FleetBoat目标的非Greek Knight”返回拒绝成本，其余保持原生分数。这能让JobAssigner为合法队伍找替代槽，覆盖多个开启登船权限的入口。实际native评分入口已核实且在Assembly-CSharp方法表中地址唯一，见第6节；仍需实际构建与注册验证。
3. **分配提交边界**：最终SetEmbarkableTarget前需再次验证style/目标仍有效，防计算后生命周期变化。但不能只在SetTarget prefix默默return而保留已占用的计划；必须让原生下一轮重新分配，避免未分配队伍被当成成功。通常主线程同次同步Compute→Apply可减少竞态，保留原生链比重写完整分配器稳。
4. **匹配数量**：一次召集构造同侧/当前场景/可用状态的FleetBoat集合B，以及该召集允许且未被其他船或任务占用的Greek Knight集合K。预定船槽上限为 `min(|K|, |B|)`，每队/船最多匹配一次；成功数量以真实分配/登船结果为准，不能把预计数量直接写成已登船数量。若存在其他限制形成不可达配对，min只是上限，不保证全部成功。
5. **零队伍**：允许结果0，不能为了保留“原生一船槽”而错误召一艘。现有FleetBoatFormation已能将未占用保留槽改gap，但需要检查zero-candidate分支是否仍让native recruitment抢入默认一船槽；不要只截postfix候选数组而让prefix/native先招走。
6. **计数语义**：已经载有合格Greek队伍的候选船，可作为已有匹配单独算一次；主Boat已分配队伍、其他player formation/船已占用的队伍不能再次算可用。是否允许跨侧调船应遵守现有同侧策略，不能用全世界总船数承诺当前侧min匹配。
7. **随从限制**：正常靠合格Greek knight的原生stowaway流程携队；若加Archer防御性资格，只检查其真实owner/本次合法船队关联，不能因客户端 `_knight`未同步而阻断native收包。不要改变独立工人/其他类型船员规则。

## 4. 主客与存档边界

- 原生EmbarkableRegistrar分配仅在HasWorldAuth进行。style筛选/匹配决策也应仅主机执行；客户端接受原生Embarkee/FleetBoat同步，不本地重复分配或因本地style未就绪拒绝收包。
- `Embarkee`网络数据包含IsEmbarked、目标船NetID/CRPCType、slotID；客户端Deserialize解析目标并调用EmbarkProcess。FleetBoat网络数据包含state/boatNumber；不要引入随机slot/syncID或跳过原生注册。
- FleetBoat保存BoatNumber、CurrentState、TargetWeakPoint。`CampaignSaveData.PopulateCarryForward`保存船总数，active有船时取FleetBoats.Count，否则取standby。现有FleetBoatRecovery以quest所有权/捕获carry恢复船，active与standby**不相加**。Greek可用队数下降不能减少这些所有权字段，否则船会永久丢失。
- `PatchWorld_FleetBoatRecovery`只处理四项任务带来的船所有权和泊位，不是小队容量限制点。`FleetBoatFormation`保存每个运行时formation基线，空船槽封gap、disable后恢复，不应借新逻辑破坏其旧世界/生命周期归还机制。
- 对旧档已经载着非Greek队伍的船，不能在反序列化中直接踢人/清字段。建议保留现有航程乘员至安全Idle/原生下船，再限制未来重新分配；若用户要求立即清退，需要一个明确的安全港迁移步骤。否则中途拒绝会造成等待人数/槽状态/跨岛携带不一致。

## 5. 异世界贴图与动画：已知事实而非猜测

- 参考FleetBoat登船过程主要是原对象挂到EmbarkeeParent并Tween到预设座位；下船重新挂回world.gameLayer。没有按骑士风格验证动画控制器，也没有原生“非Greek必坏”分支。
- Knight.OnEmbarkStart使用通用站立动画、stationary、无敌及PositionSync开关；船自身帆桨动画由自己的AnimationSync处理。只读代码不能证明某种非Greek骑士动画必然不兼容，因此不能把用户担心作为已证实bug。
- 本项目KnightStyle本来就是共享Knight实体换controller/scale。随从可能因Norse实体/有效风格策略保留北境战斗动画，owner style3并不自动代表每个Archer都用希腊原生prefab。此前实物也已出现Greek风格小队的随从实际上是Norse实体；限制owner style3不是全部视觉兼容问题的充分保证。
- 骑士和Archer登船有预设位置/缩放，混编规模若超过 `_archerPositions.Length`会退回boardingArea随机位置。不能在未验座位布局前承诺任意大小小队都无重叠。应测试Greek knight+实际当前followers的登船、射击、下船和读档表现，而不是只看owner标签。

## 6. 实际E副本2.4 API核查

Cecil只读确认以下均存在且public：

- FleetBoat.CallPassengers、OnBoatSummonBellRang、CanJoinFormation(FormationType,Side)、TryRecruit(Formation)、`_embarkable`、`_squadArcherCount`、GetSerializationData、DeserializeFromData。
- EmbarkableRegistrar.CalculateEmbarkeeScore(int,int)、ApplyAssignments(Il2CppStructArray<int>)、`_tempUnitCache : List<Embarkee>`、`_tempSlotCache : List<EmbarkeeSlot>`、GetAssignedUnits、GetAssignedUnitCount<T>。
- EmbarkeeSlot.Embarkable、UnitType、Occupant、IsOccupied、ID。
- Embarkee.EmbarkableTarget、IsStowaway、CanEmbark、SetEmbarkableTarget(Embarkable,int)、Embark、Disembark(bool)、`_owner`及网络序列化方法。
- Embarkable.ToggleBoardingPermission<T>及非泛型Type重载、Embark/EmbarkProcess/Disembark、StowawayCount、`_owner`。

重要版本差异：2.1的FleetBoat.EmbarkProcess/DisembarkProcess **不是实际2.4相同入口**。实际2.4是 `PrepareEmbarkProcess(Embarkee) : DG.Tweening.Tween` 与 `PrepareDisembarkProcess(Embarkee) : Tween`，且TryGetEmbarkPosition/TryGetDisembarkPosition仍存在。实施不能照抄旧2.1钩子名。优先在注册器分配层做限制可避开这段版本不同的动画流程。

本报告尚未离线读取本轮船prefab布局，因此 `_numSquads=1`仍是2.1参考默认值；拒绝成本100000已由实际2.4 native评分机器码确认。API存在与单程序集地址唯一不等于运行时detour已验证。

## 推荐向用户描述

“可以把希腊舰队小船的骑士乘员限定为希腊小队，实际召集数按当前可用小队和可用船匹配，最多取两者较小值。主跨岛船和小船弩炮工人不受这条骑士资格限制。异世界骑士目前没有代码证据显示一定登船坏动画，不过希腊小队也可能含北境实体随从，所以会专门验登船、船上攻击和下船表现。”


## 7. 追加：闲船时序、预留与实际native地址

“评分拒绝非Greek + FleetBoat.CanJoinFormation必须已有assigned/embarked Greek”这两个postfix不足以支持闲船初次召集：Idle不允许Knight boarding；TryRecruit先RegisterUnit加入formation，再进入InFormation，OnChangeState才开放Knight boarding并让registrar分配。若CanJoin预先要求已经有队伍，会形成循环前置条件，初次闲船全被拒绝。

最小可靠路径是沿现有ActivateFormation候选规划加入短生命周期的一对一预留。先选当前世界同侧、原生可用且没有其他有效占用的Greek Knight与船；已载合格队伍的船保留已有配对。被预留船可以通过CanJoin门，加入原生formation后再由原生registrar登船。对预留船的Knight槽，评分只接纳预留的该队；所有FleetBoat槽仍拒非Greek。不要绕过原生100000拒绝结果而强行把不可达/CanEmbark=false配对改成可达。

必须同时避免预留队在实际登船前被PlayerFormation招成陆队。实际2.4 Knight.CanJoinFormation在inert/side/HasFormation后新增了 `Embarkee.EmbarkableTarget == null || !Embarkee.IsEmbarked`：已经登船的原生会排除，只有目标但还在走向船的仍可被陆队招走。因此可以在该方法的PlayerFormation结果上，仅排除仍有效的未完成船队预留；登船完成后原生规则承担此排除。不能全局禁止Greek加入陆队，也不能影响shieldwall。

原生StartRecruiting不在同一栈直接Recruit。2.1 RecruitRoutine先yield Wait.ForUpdate，恢复后才Recruit，然后等待5秒再循环；实际2.4协程MoveNext的state0同样先写state1并返回，state1恢复才call Formation.Recruit。预留必须跨帧持续，不能ActivateFormation postfix结束就全部清除。现有postfix TryRecruit船本身发生在原生首轮Recruit之前，但随后尚未上船的Greek仍需防抢。

预留身份必须绑定world、formation及船/骑士实际实例，不能仅靠可复用的native pointer。profile关闭、失去authority、世界变化、formation disable、船招募失败/离队、角色失效/死亡、过期未完成时应归还未完成预留。已经完成的真实登船关系由原生维护，不应因计划过期清除乘员。评分只锁船的一侧仍不能保证该Knight不会被主Boat或别的开放登船船槽分走：遇到更高优先级原生调用（例如主船铃铛），应显式取消旧旗帜预留并收敛空船数量，或在有效预留期间实施一致的双向配对门；不要静默修改主Boat一般容量/风格规则。

实际E副本2.4 GameAssembly.dll只读核查如下。sameSlots是Assembly-CSharp方法表中指向同地址的计数，以下均为1；不是全进程符号唯一性声明。

| 入口 | Method token (decimal) | RVA | 关键证据 |
| --- | ---: | --- | --- |
| EmbarkableRegistrar.CalculateEmbarkeeScore | 100668382 | 0x4f7eb0 | VA 0x1804f810c有mov eax,186A0h，拒绝成本100000 |
| FleetBoat.CanJoinFormation | 100669277 | 0x51fbb0 | 非共享短函数 |
| FleetBoat.TryRecruit | 100669278 | 0x522970 | 非共享短函数 |
| FleetBoat.OnChangeStateCallback | 100669250 | 0x520690 | 非共享短函数 |
| Knight.CanJoinFormation | 100671814 | 0x5b1120 | 在目标非空时要求IsEmbarked=false |
| Formation.StartRecruiting | 100669514 | 0x5324d0 | 调routine.Start接口 |
| Formation.Recruit | 100669531 | 0x5311d0 | 由恢复后的协程调用 |
| Formation._RecruitRoutine_d__37.MoveNext | 100669567 | 0x53f670 | state0先yield；state1在VA 0x18053f718 call Recruit |

为排除字段猜测，getter机器码交叉确认：Knight.get_Embarkee RVA0x5b7970读取+0x1d0；Embarkee.get_EmbarkableTarget RVA0x4b9240读取+0x28；Embarkee.get_IsEmbarked RVA0x4fd7b0读取+0x34。以上地址只用于本次实际副本审计，不能硬编码为跨版本运行时入口。
