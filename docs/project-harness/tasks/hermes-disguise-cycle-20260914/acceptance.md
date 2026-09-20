# 友好巨魔伪装与头饰轮换验收

2026-09-14本机7D3D2926/build7.6.5-hermes-disguise-cycle-20260914：友好巨魔戴原生面具/44款新增头饰免新主动选敌及反制追击，伤害不变/已起手允许完成；默认30%保留，命中后44款顺序循环，独立本机全局游标原子落盘。48项目通过（48保护/26轮换/62头饰整合及interop）、0W0E/2671API/2281无关方法保持/独立review通过；闭游戏备份C6B71AA6安装，save57E54166/configA3E3A0B8保持，未启动/提交/发布。真实战斗/AOE/头饰观感/保存读档/换岛/联机仍待。

## 当前行为与明确边界

FriendlyTrollDisguise在Mod总开关开启且有主机权限、当前world/scene/gameLayer、存活时才判断保护。原生_maskIndex 0～5且mask有效显示、实际位于当前troll子层级可保护；自有头饰通过HasDisguiseHeadwear校验同世代pointer、当前world、有效token/Sealed/HostEnabled/VisualApplied及真实IsApplied，44款均包含周年帽。未抽中新头饰但仍戴原生面具的转换巨魔也受保护。资格现算无缓存，不写生命/无敌/伤害/MaskIndex，不动碰撞。

既有5%反制怪IsPursuitTarget与PriorityTargetPatch注入循环均排除保护对象。两个TargetCacher nearest Postfix仅在原生结果为保护FriendlyTroll时按现有对应列表、原条件/忽略谓词、严格距离<range及原列表同距先后重选；列表不修改，无新delegate/RPC/全场扫描。无保护的原生结果原样保留。已有起手/已发射攻击允许完成，用户已获告知，不取消既有缓存攻击。

实际2.4最近优先/低优先原生入口0x7a1820/464B和0x7a16a0/384B，各same_slots1；只新增这两处长方法后缀。实际resources.assets的Troll_friendly组件有14项，不含Character，Squid独立公民抓取链不适用；未另加章鱼钩子，证据friendly-prefab-components.json。

概率配置沿用默认30%，RollChoice仅命中后调用HermesHeadwearCycle；既有收据重复Init早返回，读档/未命中不推进。顺序为5世界各6款、周年帽7款、周年面具7款，0～43循环；已有选择稳定。游标文件BepInEx/config/KingdomEnhancedMod/ModSave/hermes-headwear-cycle.v1.json在首次发放时创建，本机主机各存档共用，未改原生存档或迁移既有Hermes收据。
游标只读取≤4KiB固定schema文件，仅确证缺失异常初始化0；其他访问/损坏/未知版本拒绝，不重置覆盖。唯一temp+Flush(true)+按读取时存在状态Move或Replace+bak（实际扩展名.bak）；成功落盘才发放，进程内锁串行。外部并发多进程写无事务保证，突然进程中断可能消耗尚未显示的编号，手工删除文件会从0重新开始，均不宣称绝对无遗漏。

## 回归和审查证据

两名实现worker均本机OMP deepseek/deepseek-v4-flash thinking=max，native会话resolvedModelIsFallback=false。初始6分钟deadline结束后恢复原会话完成review整改，最终均exit0；没有改用内置实现worker。内置archer_reviewer只读最终复核无阻断。其提出FileInfo.Exists错误折叠会误初始化已修，Operator提出native mask池归属边界也已补IsChildOf并回归。
48个实际测试/接口编译项目均通过，包括48保护、26游标、62头饰整合；后者保留原55保存/网络/生命周期用例，新增7条覆盖44头饰、重复Init/未命中不推进、IO失败不改变native参数、隐藏/关闭/authority及复用资格。游标测试都用测试临时目录，不写真实配置。
初始递归发现还包含worker的一次性Cecil导出工具，该工具缺少task-specific CecilPath参数时编译失败；已把它移出canonical测试目录到operator research-tools，保留发现日志。这是调查工具，非第49套回归。48个实际回归项目首次均通过，没有为此修改任何生产逻辑。
完整clean snapshot构建0W0E；2671个可达Unity接口路径无unstripping失败stub。对比C6B71AA6，2281个旧方法体保持，仅Plugin.Init、Config.Init、Panel.DrawControls、Hermes.RollChoice与Friendly的追击资格/注入Prefix共6处改变。唯一Hermes编译器闭包DisplayClass104_0→105_0因新增方法顺序改变，精确名称归一后其方法体与网络AppendBinding逻辑保持；不是广泛忽略所有闭包。

## 安装与待实测

DLL 7D3D2926FCC3E5EADA491771617F737C2C246899CED181A741B9E8EDCDCC5E7E
路径 E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll
备份 E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-hermes-disguise-cycle-20260914-194240-140.bak
存档 57E54166C813EEA47B85AC44DC47D779A7AAA5D41B73651B1F00AED33C4E2AAB
配置 A3E3A0B876BB5D421B681FFF5919FCE100BBAAEC043F083A36933F3114EA5439
游戏关闭时安装，save/config前后哈希相同；未启动游戏、未提交/公开发布。新候选说明TXT保留希腊火矢半径0.25、直径0.5与小怪受击宽0.375（约1.33倍）对比及此前所有功能说明。
真实攻击/范围伤害、面具/周年帽观感、跨岛/实际保存读档、真正两机一致性及性能尚未验证。双方需同候选版本。旧骑士附加档与头饰相关待实测项仍保留doing。
