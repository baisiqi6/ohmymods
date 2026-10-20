# #272 地图生命周期与下一轮诊断

- base `release/v9.5.13` / `2b22aa5b21e2d8d96dd01b754b047836e8d166fe`；独立于#271，仅在下一隔离候选联合编入已审#270结果交接。
- 用户授权：继续补准异常诊断、修地图清理/重新登记及详情形状；只做必要修改。正常存档/配置/DLL和Steam不写。当前无游戏。
- A23实证：freshPlaying→permit0.119s/ARM/source8通过；真实resultUI10→physical11及land11生成；探针InvalidOperationException触发controller TERM143。确切throw原因未记录，旧route-check标签不定位它；native-clear后key11=9拒绝与该异常因果未证。

## 修复设计与边界

1. 地图字典：先回归复现owned11→10在clear降成9，再append拒9的自身路径。优先在实际清理写入处按同菜单/同字典代次保存的原值与预期值CAS归还：原本不存在的自有扩展键删除，原本存在的按捕获prior恢复；当前值漂移/foreign不接管。复用现有dict transaction，不新增扫描/全局账本。任何仍用于真实无UI焦点的fallback保留且不能伪称owned；禁止无条件把9视为我们的值。先核generation/prior目前责任，再实现最小方案；若当前native生命周期不足支持，先补单次写入诊断而非猜测覆盖。测试clear→reappend、A/B、prior、外来值、错菜单/字典、回滚失败。
2. 详情图：A/B独立Image/token已有per-slot数组，不应在批量prepare A→B时当用户切页、互相release。将per-slot贴图存续与pager焦点分开；保留exact owner/geometry/resource归还和foreign保护，不只删一行后放开分页。当前焦点从已登记tuple及当前menu焦点/状态确认。批量完成后实际读回两pair；直接world→A、A→B→A、OFF、Clear/owner变化作为针对性回归。尺寸/位置/素材/概览算法不改，不新增每帧全树扫描或native UpdateLand补偿。
3. 私有观察器：新A24只复制32项已核source模板，不复制A23凭据/运行/证据。重绑新case/window/PID边界；在post-ARM实际读取/Arrival子步骤标记stage；异常原message限长且单行、可选少量managed frames，保留sticky stop与原全部identity/数据/计时/8设施/marker成功约束。Arrival读取到的原始map/catalog在原校验前留complete=false诊断（不能当到达通过；读取本身若抛错就留stage/message）。Source/Arrival成功与诊断分别命名。不要改read bridge业务/授予/保存；unknown不改成成功。

## 执行

三个互不重叠slice：W的ExtensionIslandMap+现有dict纯helper/对应tests；W的MapMountIcons+对应tests；私有N24的诊断source/controller身份重绑。两product worker不改Main.cs/build.bat/版本号/Androidpin入口/彼此源码；诊断worker不改产品、N23原件或正常目录。Operator合并与完整构建，NoDeploy/BepInExPluginsPath空、输出各自独立；未变#270/route/生命周期证据按hash复用，新增只补受影响验证。

阶段集中审查仅批准静态实现/host/纯构建。代码审查、精确新候选Native数据边界审查后，再问人类就绪开窗；不借本次许可启动游戏、merge、安装。A23 case spent，保存/重进尚未通过，不能用其后像当合格cold父样本。

## Operator 对实现前条件的裁决
- 第一代prior存入同代Generation，幂等刷新不得重捕获自身值：采纳。
- no-extension降级路径一并修复/验证，不留下已知重登记边界：采纳，worker已明确。
- clear读回未知保留poison/责任、非焦点两pair必须读回：采纳。
- 对“在TryBuildDetailPageContext直接改为focusedLand焦点门”的具体建议不采纳：真实Rebuild/PlanExtensionDetailIsland在world状态为非焦点岛调用该共享几何方法（2140/2192附近）；这样会破坏合法预建并留下FailedFingerprint。独立UI审查已指出相同问题。替代实现为几何仅exact tuple/per-slot applied proof；交互点击/显示才核actual focus/state。去除ActiveSlot对多租约的错误限制，保持旧回调拒绝。CreateDetailPager当前还捕获world focus9/state0且为单例：worker需按既有两个slot持有，明确expected交互契约与actual观测区分，并在点击时重新核所有门。此替代履行同一风险控制，不扩授权。
- 本阶段只静态实现/host/NoDeploy纯构建，无Native/gate/merge/install授权。

## 静态追加核对（不扩大实机结论）
- dataLand=0 是 IslandSaveData.land，不是Campaign.LandMapData；ReadMap并不校验前者。实际冻结CrossWorldMountRuntime在placement/binding成功后立即TryWriteMarker，不等Save/探索，因此不能用“刚到未保存”放宽八marker门。保留精确stage/message/rawmap作区分，A23 throw仍Unknown。
- OFF/current11/13按Availability优先CurrentRecovery，不必然触发no-extension；slot历史及EnsureReady门成功时继续保留tuple。两岛都notReady/noUI时原生Greek ShowRoutine state0会直接查物理key，已有Init→LoadLands子链已证，但不能证明每次map复用先执行该链。需把这种条件性时序列为Native边界；不能假称必现回归，也不能靠hook存在宣称所有OFF情况安全。源码与host须保留明确拥有的fallback/foreign保护，增量审查核该分支。

## 代码审查增量（同阶段）
- 初审发现无账目降级、写后异常丢失责任、未发布回滚无读回和字典换代四条遗漏；同一worker按exact pending账目/独立readback修正，并补真实方法抽取hashlock故障测试。纯决策/源码接线与实际方法执行分计，不替代Native。
- UI复审要求把暂时focus/state不匹配改为只拒绝此次点击；hard owner/token/view/reign失效仍先清理。迟到A点击后无UpdateLand返回A，必须仍能翻页；该增量已审通过。
- 地图复审追加同一发布边界：Unity SetActive可能重入，必须在local visibility后核same pending generation与exact字典，再只做managed发布；拒绝后旧attempt仅CAS自身数组引用，不能覆盖Clear/新代或foreign数组。正在修正，不先接纳R2。
- 新A24诊断两个P2（getter标签、严格单行/长度）已关闭；total-node门仅补实际操作数消息，条件未放宽。候选仍待Root联合port/编译与新Native阶段审查，原case凭据不复用。

## 当前收口
- Root 集成并核完 reentrant tombstone/poison 责任：15方法hashlock/81项动态故障、357项纯决策与接线通过；UI194/4/3复用未变源码与实际Root结果。独立代码审查P1=0/P2=0，双TFM0W0E。39项旧RuntimeProbe失败按原样记录。
- 代码保留独立Draft PR；#271仍为交接依赖。新候选精确Source/Native边界另核，完整8→11/保存/冷读尚未通过，不合并安装。
