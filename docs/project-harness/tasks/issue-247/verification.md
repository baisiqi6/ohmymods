# Issue247：2.4.0 钱袋验证记录

## 需求与范围

玩家报告来自游戏2.4.0懒人包/公开Mod10.8.35；本任务将钱袋外观扩展至全部世界，并独立核验实际容量。Windows2.4.3兼容见Issue248，结果不交叉替代。代码基线release/v9.5.13@1f9c6753；与公开10.8.35的钱袋行为对照。

## 已完成的代码与自动检查

- 钱袋root/container明确使用EnabledAllWorlds，其他调用仍默认GreekOnly。沿用2倍袋、原金币世界尺寸、每玩家相机和隐藏状态保护；总开关恢复、加载/池复用/换袋不累乘。
- 币只有父缩放差异与incoming匹配两重证明才纠正，单次完成即移除注册表；跨容器不复用旧Original，外部后写无新proof不重放。身份暂不可读由原有Tick有界重试，不新增扫描/driver。
- Worker真实red/green；独立Reviewer原两红例与身份故障红例重编译后复测通过。最终scope88/0、scale adapters24/0、生产viewport adapter49项、viewport policy20项；git diff检查通过。
- 全插件：Mac2.4.0引用的NET6与NET8、Windows2.4.0实际interop+公开包core的NET6构建均0 warning/0 error。Windows使用既有2.4.0互操作产物，此轮尚未重新启动生成。构建禁用自动部署。
- 独立代码review：R3 approved；只证明本轮代码与自动检查，不表示真实窗口、收币/溢出/存读档通过。

## 精确2.4.0原生静态证据

玩家日志记载2.4.0r23488、构建a436110bb9、Unity6000.0.61f1。用于本轮静态分析的独立本体BuildInfo匹配这些标识；尚未取得玩家文件SHA256，不能称其文件逐字节相同。

- GameAssembly SHA256：`cd8c2b822b12f5416e73234d6d1052efb499fe324aceff6264e0c87c6f8edfc1`。
- metadata SHA256：`e837a826dd2036e37a3a1dc07a90847afd250dd3c609471b709b1fa2e3af8d78`。
- LibCpp2IL以精确版本重新解析；没有使用2.4.3地址。TotalCapacity偏移0x54；TryToGrabCurrency在VA0x18080FB51、SuckCurrency在0x18080F5D5、SetCurrency在0x18080E9B8读取容量。
- SpawnCurrency在0x1809B8DCB已SetParent(container,true)，之后算当前列表count<300并于0x1809B8E74调ResetVisuals，然后加入列表。ResetVisuals本体与正常后续未见直接重写localScale/父级；不排除范围外间接写入。
- Show/Hide对应Tween核对为SpriteRenderer.DOFade，未见直接DOScale/root scale恢复，没有依据新增持续显示补偿。
- 已检查ApplyData等方法未见直接写TotalCapacity；只限已检查方法。自然掉币链BagCurrency.OnTriggerEnter2D→CurrencyBag.CurrencyFell独立存在，300/600堆叠参数不是数值容量验收。

原始游戏/interop/玩家日志/工具与汇编只保存在本地任务证据，不提交仓库；这些是原生静态分析，interop不是原方法实现，汇编不称为原始C#源码。

## 待完成

2.4.0隔离实机：公开包基线和候选的自然尺度钱袋、实际所属钱包/保留容量/收币量、首次掉币/溢出、池复用及保存重进。仍无实际安装/游戏验收；正常游玩版不改。PR保持Draft、不合并/发布，Issue保持开放。


## 当前主线复核与交付（2026-10-17，Mac）

按用户明确授权推进代码合并与正常包安装，实际容量/外观玩家验收单列，不把未完成原生验收作为无限保留Draft的理由。本轮接入当前 `release/v9.5.13`（3b340713）；其相对f60只有3份Android文档，il2cpp/android/tests树完全相等，已有构建以完整实际source hash一致性重绑。钱包两份生产源码仍与原审查版本逐字节相同。

独立审查抓到Android共享源pin遗漏：实际重建Probe后AdapterTests为1262通过/3失败，仅GreekScaleScope.cs及其Program.cs/Stubs.cs三项。只更新这三处已审输入SHA，保留其它全部pin与断言；再重建Probe 0错误（1条既有CS0169 warning），AdapterTests 1265/0。共享Scope的现有Android角色入口仍为GreekOnly，未把PC钱包patch或全世界角色开关引入Android。

本轮实际通过：scope88/0、adapters24/0、viewport geometry49、viewport policy20、ANDROID scope88/0；Mac完整net6/net8、Windows2.4.0真实引用net6以及正常私有累计net6/net8均0W0E。首轮host隔离CLI误卷旧obj生成文件导致CS0579，尚未运行测试即停；修正仅验证命令的附加排除属性，未改项目/断言/GenerateAssemblyInfo或清理旧文件。每套实际Compile item化与源hash核对后在独立输出重跑，以上为修正后真实结果，旧失败日志保留。

正常包候选保留39资源、271编译源和161引用，与此前已装的#253/#257/#258及宫廷/盾卫/地图累计内容保全；预先限定只有GreekScaleScope与CurrencyBag owner允许语义变更，其余类型/方法snapshot精确一致。私有metadata SourceLink沿用旧primary标记，不能用它冒充本轮源码来源；实际来源以输入清单、PDB文档checksum及本次提交核验。

边界：此PR是全世界钱袋视觉范围修复，不改变容量数值/币种业务；实际收币、掉币、上限、读档与各世界外观均待玩家原生验证。金币单次继承纠正成功后释放owner，不跨off/on重放外部coin写入；root/container关闭时保留外部轴，但重新启用仍会重断言该persistent request的Desired，这是既有契约，不宣称任意第三方root修改跨重开永久保留。首次对象identity读取失败、尚未注册的请求不承诺自动重试。

安装按独立精确关口记录：备份当前包/存档/配置后只改DLL及SHA256SUMS一行，不热替换、不启动游戏、不覆盖共享进度。Windows2.4.3/Android设备/联机未由本轮验证。Issue247的实际容量待验范围不冒填完成；旧受管元数据阻断未因本轮裸改或跳过。
