# 稳定头饰配额与低比例调查验收

2026-09-14本机63105375/build7.6.5-hermes-quota-20260914：用户明确将头饰独立随机30%改稳定配额，累计每10新转换3顶（4/7/10），v2 credit文件只读迁移v1 next8，已有actor不重分配；新增有界被动[HermesHeadwearDiag]用于显示调查。51项目/26quota+36真整合+16diag+62core+27visual、0W0E/2715API/2287旧方法保持/review通过。闭游戏备份7D3D后安装，save57E54166/configAD2CB236及v1游标保持，v2未预写；未启动/提交/发布。头饰显示根因仍未确认，真实10只3顶/外观/联机待验。

用户最初报头饰不显示，随后明确补充为偶尔可见、两轮同化可能只有一顶，再明确选择稳定30%配额。旧7D3D的实际运行日志没有HermesHeadwear警告，约50个friendly-active并非全都是可用于计算概率的新转换样本。该次进程创建游标并推进8款，只能证明实际分配过，不能证明全显示或全没显示。资源只读检查Head挂点/active/scale及body/maskPrefab alpha均正常，body材质Pow-TrollFriendly、maskPrefab材质Pow-Diffuse-Snow；差异仍是调查线索，未擅改材质。用户退出游戏之后才安装，未中途替DLL。

## 完成的改动

HermesHeadwearCycle.TryAssign用整数credit累计chance，30%从0时第4、7、10只发放，含未发放的一次也持久化credit。core RollChoice去掉随机门；已有token收据重复Init/读档早返回。v2文件固定schema2、nextChoice与credit，首次只读v1带入款式；v1不改、不删，v2坏时不回退。严格4KiB有界读、明确缺失异常才能新建、按读取时存在选择Move/Replace+bak、Flush(true)后返回、进程内锁。失败那只封存ChoiceNone保留原生，后续新转换继续credit；不会让同一只读档补计。保留配置键，面板改配额表述。此配额指新增MOD头饰，原本有面具的单位可另行保留，因此可见总比例不是强制恰好30%。

HermesHeadwearDiagnostics只在现有Decision、Apply、首Tick及撤除事件输出：8头饰+4未命中样本、每样本2快照+1撤除，另最多4无法归因撤除。字段按项异常降级，仅读sharedMaterial不触发材质实例，记录自身/body/template/native mask及Head的sprite/alpha/material/shader/active/position/scale/bounds/layer/order。VisualState.DiagnosticTickReported防逐帧诊断读；不新增Unity组件/钩子/RPC/驱动/场景扫描，不改颜色材质或伤害。原OMP按review把同一GOid/双指针复用的Removed样本与新Decision分开，Find逆序选最新。它是有限事件诊断，不能作为永久业务identity或屏幕可见性证明。

## 验证

两个worker均本机OMP deepseek/deepseek-v4-flash thinking=max，native模型事件fallback=false。诊断初轮4分钟仅接口调查，恢复同会话产出，再恢复修复review；quota一次完成，没有内置实施worker。内置archer_reviewer最终只读无阻断。
51个回归/接口编译项目最终全部通过，包含26实际quota、36真实core+cycle整合断言（从旧v1 next8迁移、3只后重启、第4/7/10、重复Init及关闭/client/0、native参数保持）、16诊断、62头饰核心、27视觉。诊断初轮13通过1格式断言失败，原OMP修正断言并增加相同native身份复用回归后16通过；初轮结果保留。生产分支接口编译通过，stub项目单条既有CS0649警告不混同生产构建；完整生产构建0W0E。
2715条可达Unity接口无unstripping失败stub。旧7D3D的2287个已有方法保持，仅quota模块替换/删除相应旧API、core配额与诊断接线、visual诊断接线和面板配置/版本日志改变。源哈希与干净构建快照一致，散射/火矢/伤害/保护/骑士档/银行方法保留。

## 交付与待实测

DLL 63105375E9CEB0478C679CAEFE5D31E573BE78DC6F4F3F68298D4E987407A56E
备份 E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-hermes-quota-20260914-205640-373.bak
save 57E54166C813EEA47B85AC44DC47D779A7AAA5D41B73651B1F00AED33C4E2AAB
config AD2CB23670AF6AD858D68E779B075F49A15E225B0668D46919E811A7689EB1CF
安装时v1/v2存在状态和哈希见protected-cycles.json（v1保持、v2尚不存在）。代码首次实际转化才迁移并创建v2。本轮未启动游戏、不覆盖存档配置、不提交发布。更新说明含希腊火矢半径0.25、直径0.5与普通小怪受击宽0.375的保留对比。

稳定配额已实现，但实际10只3顶体验、显示/消失原因、真正重启/换岛/联机仍待观察；不能把诊断就绪称为头饰显示故障修好。下一次游玩累计转换约10只后读取新日志，确认decision有无头饰及apply/first-tick/removed，再针对证据修视觉。旧有关骑士附加档和武士等实际验证仍待。

后续排队：旁支用户授权原创英雄弓手美术准备（来源01a09f0e-3e93-7690-a059-02ef606b98f4），当前修复验证妥善收尾后只读导出弓手动作/元数据与最近邻参考，做原创站立稿供选择。不得把普通希腊女性弓手冒充未使用新英雄，不提前定玩法/招募/数值/性别。原消息已记录在本任务plan.md；此次未开始美术，不夸称完成。
