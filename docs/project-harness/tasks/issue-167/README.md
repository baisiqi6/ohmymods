# Issue #167：宝石盾卫首次接入的已访问岛登记

用户靠近店没有付款圆点。日志确认5个原生付款点和Owner/CRPC预检完成；原生CanSelect依赖CanPay。只读原生存档中当前land8、已访问0/4/8，合法v2盾卫表仅有land0且无Mold/扩位/claims。把提取文档输入生产Identity/Persistence后，current8无记录→Unknown→Mold/Shield报价均false。该分支足以解释阻断；没有实时F5或付款gate快照，不能称唯一现场原因。

错误产生于首次接入时只登记当时岛：已有KEY但新当前岛没有row被隔离，Unknown又让正常Save不能补登记。本改动在成功原生生命周期边界建立未登记已访问岛的baseline，身份/付款predicate和schema保持。仅允许全权威副本和live过程均无已记录付费证据的lineage；不宣称能观察崩溃前丢失的历史。

## 状态责任

Staged有两个协议：原有ExactSaveCapture；首次接入Enrollment（Load前缀冻结，成功Pop finalizer提交；既有WasNew generation成功边界处理其它已访问岛）。两者共用原有PrefsPrepare copy/readback/srzEntries和版本屏障。初始化不写native key、Prepared、不强制保存、不新增Tick补行。

冻结只包含slot==land、已游玩、完成生成且missing的真实成员；≤128。已知行/GUID/其他战役不改，placeholder不预占。所有冻结成员在commit前仍须同Pointer/Slot且唯一；非当前岛hash复核，当前对象因Pop消费objects只用前缀hash。固定world/global/prefs/campaign/challenge/owner generation和key一致，全组clone验证后一次提交。失败与不需要登记分开；登记失败时Exact当前岛/新生岛也不得开放首购，下一次正确生命周期才能完成。任何Known当前hash不符、已付款记录/过程证据、较新未Prepared stage继续保护。

新增baseline经过下一次原生正常保存准备后才允许初次4Gem铸模。原有付款、库存占位、转职、角色/工具对象池均未改变；不退款、不猜测重发。没有移除旧守卫；缺岛分类保留用于真正未知或已付lineage，初始化责任在明确事件交接。

## 验证与边界

最终源码SHA256：Persistence `d079a657b081c2739af3a31d9c732c6b948545c68c4f6c7f03b13552f6f06e09`；Enrollment `35d8f0b47ee23ff9e8a3e231e705906ebd299f753c71f5e38f634a85d3480834`。

独审最终APPROVE。曾要求修订的失败fallthrough、非当前成员/world换代、形成candidate前key/context变化、256KiB字节容量均已用固定producer反例验证。容量在ApplyRows后用完整TrySerialize预检，失败不写Staged/版本、正常Prepare仍能处理原合法文档。公开104/0，私有实际权益文档入口96/0；原有identity136、R2 38/0、R3 27/0、durability92/0均通过。旧源正向期望与中间候选反例均有RED结果，未把mock当实机。

真实ARM引用完整构建两份均0警告0错误：公线基线c219531 + 本修复Release；玩家372c宫殿候选 + 仅本修复Debug。安装候选 `e573425bb691ae6b34680019f2a61c7f58f65de45789716e1ecc6f2fd6d93701`。与玩家372c旧DLL的保全审计确认仅5个既有H方法改变/19个H方法新增，6842其他既有方法、非H类型/字段/方法元数据与布局、338Harmony目标、35PNG名字及字节保持；归一化仅忽略无语义debug NOP/短跳编码。该组合保留未合并宫殿入口，不能称完整公线head安装。

实现worker为本机OMP DeepSeek Flash，max请求，原生模型事件核对provider deepseek/model deepseek-flash；独立内置reviewer多轮复审，Operator核对编译和安装输入。冷安装尚待正常退出游戏，不修改游戏存档/配置。测试使用synthetic native边界；私有实际文档只保存在.local，公开夹具保留结构并使用合成GUID/hash。

真实2.4 ARM编译可证明API接线可编译；静态原生Callgraph和scope回归可证明所选分支/guards，不能替代游戏里Pop/生成/保存顺序与JsonUtility稳定性。玩家付款圆点、4Gem付款后出兵、跨岛/读档/联机尚待实机。若仍无圆点，继续读取现有F5盾卫状态分流Carrier/ToolBow，不放宽付款门。

本次独立修复按中等体量patch+3登记为下一正式版本候选条目，同一Issue多轮修订只计一次；此轮无正式tag/发布，不改变10.9.38本机候选号。快购Issue168由ZCode另线处理。
