# #286 宝石盾卫商店槽位与购买就绪

基线 release/v9.5.13@98259af；独立分支 codex/heavy-shield-shop-20261024，复用已合并且 clean 的盾卫工作树。宫廷 #285 候选独立保留。正常数据、Steam、Windows #281 诊断 DLL 不动。

## 已确认事实

- 正常路径的 Claims/席位按 (campaign GUID, challenge, land) 保存，盾模与左右加席权益按 campaign 共享。未发现把所有岛的兵力合并占当前岛名额的正常路径。
- #252 的首次生成缺口仍在：BeginNativeGeneration 冻结 placeholder 的 land0；End 在岛屿 slot>0 时可能因 land0 历史行而提前返回，或给新岛建立错误 land0 session，随后 enrollment 拒绝。旧岛计数可残留显示，购买被 Unknown/真实当前岛门关闭。
- ShopShell 的进程级 _economyFault 有置 true、无恢复；campaign Prepared 门也会跨岛关闭报价。这些是独立保护，未取得用户现场拒绝原因前不能直接清锁或绕过保存确认。
- Greek ShopV2 主体/状态层同 native order 并按 localZ 分层；独立六个购买/扩展小盾使用 front.order+1 且与 front 近共面。四龛状态层、六小盾、原生投币圆点、待领实物盾是不同 renderer，用户具体所指尚待确认；不能直接把宫廷候选套在它们上面。

## 本阶段范围

1. 先用实际 source-linked identity suite 复现 #252：slot>0/raw-land0，含已有付费 land0 行及未付首次 enrollment；红测必须在当前源码失败。
2. 修生成身份来源：同 owner 下唯一岛 pointer→slot，prefix 冻结 global/campaign/owner generation/current slot/challenge/world/island/virgin 事实；正常 finalizer 在任何 session/quota 改动前重验。只用这一明确生成凭据，允许当前新岛 land0 或正常化后的本 slot；不写 native land，不把普通 Load/已访问岛/显式 Save 的 field-slot 不符放宽。
3. Enrollment 仅接受该已验证的 virgin-generation 当前成员证明；其它岛仍要求真实 slot==land/visited/fingerprint。旧岛 claims、付费权益、Prepared、hash 与 native key 保护保持。新岛空席与返岛原席应独立，旧 paid row 不能被错误 fresh覆盖。
4. 沿现有商店 Tick/生成/载入边界补必要的有界拒绝原因：复用已经得到的状态结果，不额外调用可能初始化/写 key 的 getter；同状态不重复刷屏，预算按 shop life 限制。用于区分 Unknown/Prepared/Carrier/Pool/故障锁等；不让诊断更改收费判定。
5. 槽位先确认具体 renderer；只做定向读取/既有 runtime Create 复现，确认前不猜 z 或强制前景。_economyFault 保持保守关闭，只有实际付款故障证据才另审恢复范围。

验证：生成同物件 raw0→slot 与 raw0保留、A→B→A/付费与未付、重复/表外 pointer、owner/world/slot/challenge 变化、失败/重复 finalizer、非 virgin 拒绝、真实 Prepare、保存重进；identity/enrollment 既有套件、受影响 shop runtime、三套 NoDeploy 编译。只补受影响验证。Android 共享源与测试触及前集中协调一次；独立重建 probe/AdapterTests不当设备通过。Native、安装另记，不把 managed 通过当用户现场已解决。

当前用户平台、购买症状及槽位族的文本澄清尚待回复；代码上确证的 #252 可先推进，用户本次“无法购买”是否同因仍待现场。当前为未提交的生成身份候选，未安装。

## 本轮审查与执行边界

- 阶段方案由本机 OMP `zhipu-coding-plan/glm-5.3` / max 集中审查；实际模型与会话留本机 `heavy-shield-shop-20261024/model-evidence.json`。同阶段只补增量，不把读日志/截图当新审批。
- 首轮 worker 为 OMP `deepseek/deepseek-flash` / max，新增真实 source-linked 21 checks 在主线有8 failures，候选为0；已有 enrollment105、R2 38/R3 27、durability228、snapshot30通过。首轮候选未被接收：独立审查查到 native wrapper 引用比较、paid 分支 currentLand 门及先 TickBinding 后核身份的问题，正在一次集中修订并补冷读档/返岛用例。最终结果以修订后回执为准。
- 原生只读审计确认当前 Mac GameAssembly/metadata 哈希，但仍未确定 `land0→slot` 的实际写入者与时序；旧保存里 land11 是后来结果，不能当首次 Save 自动规范的证明。普通 Load/Save 的严格门不放宽，不写 native land。
- 槽位分四族（店身/状态、六个小盾、原生投币圆点、待领实物），尚缺用户分类/现场；PowerSprite2 的深度写入使 order+1 并非必然前景。尚不修改渲染或套用宫廷 #285 未证候选。
- 已向 Android 专项集中同步共享源范围一次；其独立 AVD/构建可继续，不占用其资源、不要求普通进展 ACK。当前改动未触 Android FrozenSources 明确 pin 列表；最终 probe/host 验证另记，不能作设备通过。

## 当前结果

生成身份候选及本阶段增量已通过独立审查，Root实际复跑626项主机检查、三profile NoDeploy/资源回读、Android probe/1265 host检查；具体产物和边界见 `receipts/validation.md`。旧paid A→B→A有效用例复用；原始各轮失败不删改。当前为Draft范围，未合并/安装/Native实测。当前Mac活动campaign0/currentLand11缺盾卫行是已访问旧档边界，不因新生成修复自动补空登记。槽位修复尚待exact renderer及本次平台确认；不会凭这轮编译合并或宣称用户已经可购买。

## Mac 现场增量

用户明确两图层都是Mac，白十字为截图Shift误触；已完成可信source8私有case四点取样并通过UI自然退出（return0/errors[]），正常数据未变、批准已撤销。入口大圆弧未稳定复现，盾店角色实体遮挡与透明区裁切尚需目标指认；没有因此改材质/排序或安装。本轮不验证PR287生成/正常岛11旧账恢复。见 `receipts/mac-layer-observation.md`。
