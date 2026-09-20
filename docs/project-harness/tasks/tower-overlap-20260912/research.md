# 空塔基与已建箭塔重叠：只读源码侦查

结论：已从operator提取的当前存档对象与实际资源资产交叉确认一个具体重合点：KEM_TowerSpot_156.6与Tower Knight_greece在x≈156.64各自独立存在。Knight特化塔根没有Tower组件，且tag=Untagged，故现有Tower扫描连allX都漏掉它。另有旧空KEM清理不比较普通已建Tower/native空Tower、以及中心距离不按现有塔宽度检测的确定机制。日志retired=0不能证明无重叠；近邻其他坐标仍须单独判定，详见第7节。

本次未操作PID34420，未运行/停止游戏，未部署，未修改canonical、游戏、存档或配置。仅写本报告。当前save坐标由主operator独立检查。

审计canonical：`C:/Users/ADMIN/projects/ohmymods/il2cpp/PatchWorld_TowerSpots.cs`，953行（本轮工具实际读取，而非任务描述中的945），SHA256 `F557C2243F9A4EED2266F08DBDBB4FD4F18E888F19C796CFE8D04042CEB01666`。

原生行为参考：`C:/Users/ADMIN/Documents/Codex/2026-08-13/wo/work/ohmymods-reference-backup-20260815/game-source/Assembly-CSharp-2.1.0/`。实际2.4 API使用E副本BepInEx/interop/Assembly-CSharp.dll经Cecil只读验签，未加载运行游戏代码。

## 1. 确定的Tower漏检

1. `OverlapsNativePlacement`第378行明确跳过tag==Tower。尽管日志avoid=[Farmhouse,WallFoundation,Tower,HermesShade]包含Tower，此标签实际没有进入矩形比较。
2. `RetireOverlappingGeneratedBases`第525行先调用该排除Tower的检查；第528–547行仅在`keptGenerated`中比较之前保留且满足可删条件的KEM空塔基。原生Tower和已建Tower都不在keptGenerated。因此旧KEM空塔基与已建塔重叠没有任何清理分支能命中，哪怕距离为0或画面完全交叠。
3. 全量Tower的`allX`只含x中心。第674行用`IsFree`，第652行阈值=`target*0.6`，第784–790行只比较绝对中心距离。它没有每个existing Tower的左右边界，也不会对旧对象运行IsFree清理。
4. `target`虽在第645–650行加上候选prefab总宽+0.25下限，但乘0.6后不能保证候选对既有任意尺寸结构无交叠。构造性反例（非实物测量）：候选宽10、target10.25、占用阈值6.15；既有Tower与候选中心差7时IsFree通过，而两个宽10物体仍重叠。已建Tower更宽时问题更大。

因此`native=19 generatedUnbuilt=6 occupied=31 retired=0`与用户看到空塔基撞已建箭塔完全相容。added1@176.7只证明这一个点通过当前门，不能证明它周围视觉无重叠，也不能推出它就是用户所指的对象。

## 2. sameObject、MinSpacing与视觉边界

- 原生Level.cs第706–729行逐tag检查，tag相同时对occupied调用`GetOverlapRegionSameObject`。该方法第833–840行即使UsePayableForSpacing=true，仍使用occupied ScatteredObject.MinSpacing。candidate使用普通GetOverlapRegion，故原生同类规则不是两侧一律MinSpacing，也不是简单固定中心距。
- 当前补丁第416行虽保留sameObject参数，但所有可见GetCombinedOverlapRegion调用都传false；不存在实际true调用。因此日志UsePayable=true时MinSpacing8不会进入这些矩形分支。不要把“日志打印8”当作“当前至少保持16中心距”。
- `GetVisualHalfWidth`第793行优先ScatteredObject.GetHalfWidth并合并Renderer.bounds。2.1 ScatteredObject.cs第139–156行的GetHalfWidth优先根Renderer，再Payable.playerPayDistance，再GAPS；它并不返回MinSpacing。当前child-renderer合并通常能增加可信宽度，但读取失败/零宽时不会自动获得MinSpacing兜底。
- `GetCombinedOverlapRegion`第438–446行合并Payable矩形与visual bounds，做的是横向投影，y统一50..150。这可保守地发现不同高度sprite水平覆盖；不等同于可购买建筑基座的真实形状。全部children renderer（含inactive）可能把装饰/光效一起纳入。
- 第530行清旧候选使用**prefab**，occupied previous才用实际对象；被清旧对象的历史scale、镜像、子节点offset不会进入candidate边界。应让清旧路径测actual spot。新生成路径还会第733–741行应用template.rotation/localScale和FixedTransform.Fix，预检prefab原姿态与最终对象不一定相同。2.1 FixedTransform.Fix第66–89行可按左右设置scale为±1并改y/z；不改x，但可镜像非中心对称sprite。
- 刚好边界相接的Rect.Overlaps不命中；如需要视觉间隙，应显式加小padding，避免把MinSpacing8强套所有塔阻断增密。

## 3. 其他标签、层级与施工

- 非Tower标签只来自prefab.AvoidOverlapWith，当前记录只有Farmhouse、WallFoundation、HermesShade。代码不会主动检查其他墙标签、其他建筑或ScaffoldingTower；不能假定所有已建墙仍叫WallFoundation，应以实际资产tag为准。
- 第393行要求目标处于Managers.level.transform下，第394–395行还要求与gameLayer同scene。此路径没有硬性要求所有对象都是gameLayer后代；一个当前同scene却不在level层级下的对象会被过滤。正常原生层级是否满足由实物数据决定，不能直接断言是本次原因。
- `IsSameHierarchy`第491–503行忽略同根、祖先或后代，防止自相交。若ignoreRoot的某个祖先意外带avoid tag，它的全部占位也会被跳过；标准独立建筑不受影响。
- `IsOnBoat`第921行按上四层祖先名字Contains("Boat")排除。名字策略存在误排/漏排可能，但本次没有证据涉及船或这种命名。
- **原生施工明确是独立遗漏边界**：ConstructionBuildingComponent.InitializeBuild第134–175行，NeedsMoreWork时把Scaffolding移到building.parent，building.SetActive(false)，并把Scaffolding.Building设为原建筑。Tags.cs第172行有ScaffoldingTower。活跃FindGameObjectsWithTag("Tower")无法看见该inactive building，现avoid列表也不含ScaffoldingTower。若load补放恰逢已有塔在建，当前占用扫描可漏其位置。Tower类注释“脚手架不污染扫描”在防重叠任务上不能当作安全保证。

## 4. 旧存档名字、重建与重复load

- IsNativeBase第323–336行和IsGeneratedBase第338–350行完全依赖`name.StartsWith("KEM_TowerSpot")`与level0；名字不再含标记的原MOD对象会进入native参考集，无法被作为owned KEM删除。不能为了修复扩大删除到所有level0。
- 2.1 IslandSaveData.ObjectData第1488行保存persistent.name，TryCreateOrFind第898–899行恢复localScale和name；所以普通CreateObject存/读档保留标记的源码依据成立。不能把普通reload“必丢名字”作为当前结论。
- 但2.1 PayableUpgrade.Pay第288–351行原位实例化nextPrefab并销毁原对象，不复制旧name；Tower.DestroyTower第27–49行再次实例化holder塔位，也不复制KEM标记。原生成塔位经购买再毁回空位可失去该名称来源。因此“无标记level0永远就是原关卡固定点”并非生命周期不变量，参考集中也可能包含后来的重建空位。
- 入口只有World.OnLevelLoaded postfix第945–952行，Schedule延迟5秒；第169行同world+layer guard确保同场景成功扫描后不再重复执行，操作主体不yield，同一主线程重复延时回调通常首个完成后被guard拦。
- load重建layer时会重跑，但原生reference样本因购买/摧毁而变化。MedianGap忽略<=0.5的gap（第865–869行），不会自动修复重合native参考点。对固定参考集的同网格幂等不等于所有建筑生命周期都绝对幂等。
- 第228行refGos<2直接返回，在旧KEM清理之前；因此只剩0或1个原生空塔基的岛，即便已知旧KEM与塔重叠也不会执行清理。清理无需估计密度，应与此参考数量门分离。
- 同一world成功扫描后，后续建塔/升级导致宽度增长不会触发第二次检查。仅修两处生成/清旧检测可解决扫描时已经存在的built footprint；不能承诺此后任意升级都不会出现新交叠。是否预留首建/升级宽度或加升级后延迟收敛，应由具体scope明确决定。

## 5. 清理付款/施工保护：实际API可用

当前CanRetireGeneratedBase第580–594行只有Tower.level0、Persistent及SemiStatic header；没有检查正在交钱或施工。level0不是“玩家尚无投入”的充分判据。

原生没有Payable.pricePaid/coinsPaid进度字段。Payable.Price/price是价格，_coinIndicatorsP1/P2是显示器，不是实际投币数。

部分投币状态属于Player：Player.cs第1554–1600行处理Transaction、Completed；第1644行将实体货币加入`_floatingCurrency`；第1656–1661行满额后设置`_completingPayable`并等待货币动画；第1593行才调用TransactionComplete。CancelTransaction第1729–1747行会先清interactingPlayer再进入Cancelling，DropFloatingCurrency第1750–1759行随后才归还货币。因此只检查interactingPlayer为空仍可能赶在退款完成前删对象。

实际E2.4 interop均已确认public可访问：

| 类型 | 字段/属性 | 用途 |
| --- | --- | --- |
| Payable | interactingPlayer, selectedByP1, selectedByP2, PlayerSelecting, forceBlockPayment, nextObjectNetId | 保守保护被选中、交互或等待完成的对象；nextObjectNetId可作辅助，不能把price当进度 |
| Player | selectedPayable, _completingPayable, _payState, _floatingCurrency | 两玩家付款关联与飞行货币状态 |
| Player.PayState | None0, Holding1, Transaction2, Completed3, StateKeyDown4, StateKeyHoldDetected5, StateKeyHold6, Cancelling7 | 实际2.4枚举与旧参考有新增成员，勿照抄旧数字 |
| ConstructionBuildingComponent | NeedsMoreWork, _hasStarted, _currentBuildPoints, _buildPoints, _scaffolding | 工程状态；对于删除“未买空底”保守排除此组件更简单 |
| Scaffolding | Building | 从活跃脚手架找回inactive建筑占位 |

建议删除候选要求：严格KEM标记 + level0 + 当前world/layer + SemiStatic/Persistent身份；必须是预期PayableUpgrade，且无任何选中/互动；两玩家selectedPayable/_completingPayable均不关联；存在ConstructionBuildingComponent/Scaffolding的对象保守不删。异常/未知身份都保留。所有条件在实际Deregister前再次验证。不要通过强制CancelTransaction或归还金币来扩大清理范围。

## 6. 最小修复合同

1. 独立结构占位快照用于新生成及旧KEM清理；必须覆盖未带Tower标签的特化塔，不能只修Tower标签比较。当前希腊实物支持复用已有Tower扫描，额外WorkableBuilding与Scaffolding各扫描一次并去重GO（第7节资产证据）；检查实际对象的combined横向足迹，跳过自身/同根与船。新生成candidate应按预期最终变换测量；旧清理candidate用actual spot。
2. 所有Tower都可作为障碍，只有严格eligible且未付费/未施工的KEM空塔基可被删除；不能删原生空位或建成塔。
3. 施工脚手架作为占位来源，保护Building；去重同一建筑引用，不把脚手架当待删除KEM。
4. 避免直接把native sameObject MinSpacing8套成统一16间距。新规则只需阻止真实combined足迹覆盖并加小padding，保留原有密度网格用于候选规划。
5. 清理脱离“至少两个原生参考点”门；每次成功load只需一次，同时记录具体blocker name/tag/x/min/max与被清对象身份，便于把下一次画面反馈对应到实物。

必要回归：旧KEM空底对built Tower重叠时清旧、新点拒绝；同位置native空底不删；合法近邻保留；已投部分币/满额等待/退款中保留；施工项目保留并阻挡新点；旧KEM actual scale/偏移；无参考点仍可安全清旧；旧world/权限变化不写；普通重复load不增生。未定位实物前，不宣称这些回归已经在游戏中通过。


## 7. 实物补证：已定位同坐标Knight特化塔，及最小类型扫描集合

operator提供当前campaign1/land9，并提取`same-position-objects.json`。本次已独立读取该JSON，确认：

| 对象 | x | netID | prefabPath | 持久化父级 |
| --- | ---: | ---: | --- | --- |
| Tower Knight_greece(Clone) | 156.63999938964844 | 1591 | Prefabs/Buildings and Interactive/greece/Tower Knight_greece | parentObject空；Level/GameLayer/ |
| KEM_TowerSpot_156.6 | 156.63995361328125 | 1613 | Prefabs/Buildings and Interactive/greece/Tower0_greece | parentObject空；Level/GameLayer/ |

两者x差约0.00004578；不是子物体同一根的正常重复记录。Knight保存有TowerKnight、WorkableBuilding与ConstructionBuildingComponent数据；后者CurrentBuildPoints=20。KEM保存PayableUpgrade cooldown0及独立CRPCStamp。这定位了一个真实重合实例，而非仅推测几何风险。

随后用UnityPy只读resources.assets中的GameObject根、component PPtr、MonoBehaviour标准header，并用globalgamemanagers.assets的MonoScript.m_ClassName解引用组件类型。没有解码图片、加载引擎或写游戏资源。

| resources GO pathID | 根名称 | 原始tag数值 | 特化组件 | 同GO WorkableBuilding | 同GO ConstructionBuildingComponent | 同GO Tower |
| ---: | --- | ---: | --- | --- | --- | --- |
| 38415 | Tower Knight_greece | 0 (Untagged) | TowerKnight | 有 | 有 | 无 |
| 29254 | Tower Ballista_greece | 0 (Untagged) | Ballista | 有 | 有 | 无 |
| 20797 | Tower_upgrade_Fire_greece | 0 (Untagged) | FireTower | 有 | 有 | 无 |
| 20280 | Tower_upgrade_Bread_greece | 20140 | Baker | 有 | 有 | 无 |
| 20019/19702/20160/20290/22929/23781 | Tower1..6_greece | 20018 | 普通Tower | 有 | 有 | 有 |
| 19822 | Tower0_greece | 20018 | 空基底Tower | 无 | 无 | 有 |

Knight/Ballista/Fire根另挂PayableBlocker；Bread挂PayableShop。实际类名是Baker，未发现Bakery类型。实际2.4类型继承：Ballista、FireTower、OilFireArcherTower都继承Workable，TowerKnight/Baker继承MonoBehaviour；因此从继承本身不能推导WorkableBuilding覆盖，但以上同根资产证据证明本轮希腊四类确实可由单次WorkableBuilding扫描抓到。名含greece的塔根扫描未发现OilFireArcherTower；本轮Fire用FireTower。不要将此结论宣称为所有DLC/未来资产的完整覆盖。

**建议最小扫描集合**：复用普通Tower已扫描集合，加一次当前scene/layer的WorkableBuilding根快照，以及一次活跃Scaffolding快照通过Building关联补施工本体；按GO身份去重。本次没有发现需要额外四到五次special类型Find才能覆盖的希腊实物。其他avoidTags也可每轮快照一次，避免每个候选重复Find。即使快照含built/特殊塔，它们也只作为障碍，不可进入删除候选。

## 8. prefab bounds风险与实际sprite尺寸

`TryGetVisualBounds`第459–480行对任意Renderer设置found，即使bounds零宽；若部分bounds位于0、部分位于实际坐标，union可被错误拉向world原点。若整个prefab的Renderer.bounds在离线/未激活资产上无有效值，方法返回false，随后退回Payable窄框。`GetVisualHalfWidth`也不能从MinSpacing推定可见宽度。因此不能依赖“存在Renderer”保证预检得到有效footprint。

最小可用候选边界来源是同biome已落地的原生空基底template（本次19个参考）有效renderer bounds，按root相对偏移平移到候选x，并保留Payable矩形；清旧必须测actual KEM spot。如果要从prefab直接算，则用SpriteRenderer.sprite.bounds局部边界结合最终child/root变换，而非猜测prefab世界bounds。先拒NaN/Infinity及无效零宽，再合并；无法得到可信边界时跳新点比当成零宽更稳。

另读取上述五个根SpriteRenderer的sprite元数据（未解码图像）：

| GO | sprite pathID | sprite名 | rect / PixelsToUnits / pivot | 未缩放根sprite宽 |
| --- | ---: | --- | --- | ---: |
| Tower0_greece | 9089 | tower_00_greece | 96×136 / 32 / (0.5,0) | 3.0 |
| Tower Knight_greece | 10027 | tower_knight_greece | 同上 | 3.0 |
| Tower Ballista_greece | 10243 | tower_ballista_greece | 同上 | 3.0 |
| Tower_upgrade_Fire_greece | 10672 | tower_fire_greece | 同上 | 3.0 |
| Tower_upgrade_Bread_greece | 11054 | tower_winery_greece | 同上 | 3.0 |

这支持同x实例确实重叠，但operator另报的126.58对123.24、136.60对133.26（相差约3.34）不能仅凭根sprite数据断言视觉交叠；可能只是近邻，须看实际scale、children或要求的padding。尤其MinSpacing8是原生布局余量，**不是实测sprite半宽8**；源码第69–73行注释对两者的混淆不能作为测量证据。

证据哈希：resources.assets `B80394A7961425EA6CF7C55E7493FF648EA558FBC46EC575FEBF35569F2C195D`；globalgamemanagers.assets `34A4EC5517CF292832E08A2D8E161902017739AC35FCC07F04C318443D9A5664`；same-position-objects.json `3DB681771E6305C9D00EDEA6476D33A3F67D16FBD4D76756823F326EAD4F9E88`。
