# 中世纪随从散射与淡金箭候选验收

2026-09-14本机C6B71AA6/build7.6.5-medieval-scatter-20260914：散射限当前中世纪style0骑士随从攻击敌人，打猎/其他类型单发；F5中世纪随从散射总箭1～3，只有额外箭淡金实例色，池复用恢复，6byte版本初始化由主机决定。44项目通过（88combat/16policy/44tint及实际interop）、0W0E/2651API/2246无关方法保持/独立review通过。闭游戏备份1D63533A后安装，save57E54166/configA3E3A0B8保持；未启动/提交/发布。主客需同版本，真实颜色/回收/狩猎/联机及旧身份档验证仍待。

## 最终实现

资格模块MedievalScatterPolicy只读实际射击时Archer、当前_knight、已解析style0、_shootingTarget与Enemies层，拒绝野生动物/友好巨魔/其他骑士风格/独立弩手/Norse特种弓手/近战或失效角色。没有按当前世界皮肤猜类型；任何世界可按开关启用。旧开关键与默认off保留，总箭数在运行时、配置和UI统一限制1～3，原始箭不删改。

ScatterArrowTint只在额外箭生成时调SpriteRenderer.color。白色基准约变为(1,.948,.792)，alpha不变；无新增贴图、材质实例、renderer、粒子或光源。自有回执最多128条，沿现有Tick清理，无新扫描器；关闭只阻止之后新增，已射箭保留颜色到回收。OnEnable无条件先尝试恢复原色，身份固定GO指针/InstanceID、Arrow与renderer指针；外部颜色改变时CAS不覆盖。未知身份及写失败保留待恢复回执并退避，初次染色失败后只恢复，不能重新染色导致主客状态分叉。常量表扫描与少量实例读写仍有开销，本轮没有性能实测，不宣称零开销。

softsim注册与发射顺序不变：额外箭成功染色后在原perfect字节后追加SCT1和版本1，总6字节。Arrow.ReceiveInitialise Prefix仅严格识别自有6byte载荷，先锁定接管再ReadByte逐字节消费；任何部分消费后的异常不再运行原生。非fireArrow才调用PerfectShot，客户端本地Scatter=false也显示主机金箭，master/world边界保留；原生1byte及未知载荷完全交原生。无新RPC或OnEnable发送。新钩子实际2.4原生0x4c93b0、336B、same_slots1；已反汇编确认原生严格长度1与火矢早退分支。主客双方必须安装同候选版本；尚未实际联机验证。

## 验证与审查

两个实现worker均本机OMP deepseek/deepseek-v4-flash、thinking=max，原生会话事件确认resolvedModelIsFallback=false，见worker-identities.json。内置archer_reviewer只读审查；首轮7项生命周期/异常消费/配置边界问题由原OMP修正，最终直接复查canonical无阻断，过时上限注释也已同步。

干净源快照构建0警告0错误，源码哈希与canonical一致。44项目最后全部通过，包含88项真实散射/射速模块整合（13项新增资格/主箭池复用/主客载荷场景）、16项资格、44项色彩生命周期与协议、73项希腊范围火矢与45项旧火焰视觉回归，以及实际2.4接口编译门。
全量初轮两套旧银行测试因上一候选新增FireTowerRestockCapacity而缺少Compile依赖，补入真实模块及必要测试接口后重跑通过；原失败与重跑均保留。生产银行代码本轮没有改变。
2651条可达Unity接口调用未发现unstripping失败stub。对比安装前1D63533A，2246个已有方法保持，仅Plugin.Init、ModConfig.Init、ModPanel.DrawControls、PatchArcher_Options的VolleyCount/OnArrowSpawnedByAttack/OnArrowEnable/Tick共7处按预期改变；原射速定时、希腊火矢、身份持久化/网络与旧银行/菜单实现保持。

## 本机交付与待实测

DLL：C6B71AA694980B2958B87E768C469E75D030F5F55C2616A407E9EE36F342674F
路径：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll
备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-medieval-scatter-20260914-181752-350.bak
当前存档：57E54166C813EEA47B85AC44DC47D779A7AAA5D41B73651B1F00AED33C4E2AAB
当前配置：A3E3A0B876BB5D421B681FFF5919FCE100BBAAEC043F083A36933F3114EA5439

游戏关闭时原子备份安装，存档/配置前后哈希相同，没有启动游戏、提交或公开发布。候选ZIP与更新说明TXT保留，TXT包含希腊火矢半径0.25、直径0.5与普通贪婪小怪受击宽0.375（约1.33倍）对比。
实际淡金色的昼夜观感、真正敌人/狩猎目标、反复池复用与普通主箭恢复、换岛以及两台游戏显示尚未验证。骑士附加档真实保存读档/原版重存等上一任务验证同样未完成。任务保留doing，不把代码回归当实机结果。
