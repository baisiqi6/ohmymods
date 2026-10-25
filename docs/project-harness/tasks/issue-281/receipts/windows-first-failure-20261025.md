# 2026-10-25 — Windows 2.4.3 首错现场（保存修复未完成）

用户手动启动正常独立 E: 玩家包，在下排左侧第一座坐骑岛确认后停于四选一，未点继续。DLL `10.9.39-win243-diag.281.1` / SHA256 `16a0ddb5d508997595019cc14d5bb6faa96908522977cd7cd3ca75f6ec2cc4e7`，启动路径、出生时间及版本均只读核验；完整 BepInEx/Player 日志、用户截图、出航前和首错后的25项存档/配置字节副本留在本机私有任务目录，未上传玩家内容。

## 已观察事实

- UI→原生提交为来源岛8、目标物理岛11，成功交接。随后来源岛保存出现 `save-missed-row → CaptureFault → sync-reject → _ShowSaveFailurePrompt`，不能把此弹窗解释为目标坐骑生成失败。
- 首个缺失 career 是 unit；begin/apply 均为 `active=1, persistRootMatch=1, reg=0, pinstance=0, pobj=1, should=0, sourceEligible=0`。`GetID not-observed`，全捕获 `readFaults=0 / incomplete=0 / truncated=none`。`life` 是 Mod 根生命周期计数；`missing=1` 是布尔首见标记，不是缺失总数。不能由15条career与11条捕获相减推算损失人数。
- Native `global-v35` 前后998959字节相同；火枪手archive/pair及备份未变。Hero/Knight主文件及其bak共4项改变，后bak逐字等于前主文件。Knight本轮最新15项身份/样式/类型/顺序不变；启动匹配的22条旧快照仍保留。未执行故障后的真实冷重读，不能声称已经证明权益恢复。
- 捕获器正常退出，未结束游戏、发输入、改DLL或恢复正常存档。向用户说明可结束该游戏并撤回此次未保存的航行；本记录不声称用户已经结束。

## 当前原生证据与未闭合项

Astra 使用已核对的2.4.3 PE/metadata静态复核：`GameAssembly` SHA256 `cb3f28d73766c9ffb035512161bea1ad82835b0684330ce12f5d83da365e2342`，metadata `b10a5c6e7ae16c5526c93d6c81cf5b821ef4e62ecbe4c8d7a23a7c8f93ed01c8`。

`Game._Run.MoveNext` VA `1805549AD` 对当前 `Managers.kingdom.boat` 根调用 `DontPersistInstance(true, excludes)`；递归注销子Persistent，不关闭GO，能够合法产生本轮状态组合。`ShouldPersist`原生公式与诊断一致。`OnEnable/DoPersistInstance`共址，不能按managed名字新设两个detour。CarryForward可见数量/类型等字段，尚无逐Archer GUID/NativeId与目的地实例的可靠契约。

本轮未采到该缺失root的船归属/登船状态，故“原生船子级排除与Mod active→必有来源行假设冲突”仍是有强依据的假设。后续仅补现有Begin/Apply内的主船指针、严格子级/self、Persistent.enabled与embarkee状态及unknown原因，不放宽CaptureFault，不改Reserved、不强开原生持久资格、不按位置猜身份。

公开记录只含脱敏结论；原始GUID、原生指针、玩家数据和私有模型会话不进入GitHub。新诊断准备/构建、审查、安装与实机分别留证；本轮Issue保持开放，PR #282仍Draft。

## 只读诊断增量，本机准备完成（未安装）

- 仅扩展 `MusketeerVoyageSaveDiagnostics` 的现有Begin/Apply快照：当前主船GO指针、严格后代/self、同根Persistent.enabled及同根Archer的登船状态；缺失、异常、根失配分开。不会把当前船归属或enabled/embarked作为原生清理已命中的证明。原CaptureFault、writer、Archive、注册集合和预算保持；没有新detour/每帧扫描/主动GetID。
- 源链接RED先复现字段缺失；首轮独审发现错root的embarked归属与Persistent原因折叠，增量分别RED再修，Astra实际源增量静态复核通过（不是运行计数背书）。Operator独立执行default **659**、diag **857**断言通过；target59与R2target19已包含在857中。host两配置0W0E。
- 实际2.4.0/2.4.3、default/diag四种私有interop编译均 **0W0E**；243保留既有PR250的私有alias验证层，不称其已合并。两default各 **10397**方法的signature/operand/CFG/EH normalized IL无增删变化，**38**份资源哈希相同、无诊断类型。只重跑本次相关验证；没有代验Android设备或Windows玩法。
- Windows最小冻结源 **281 cs + 1 MyPluginInfo = 282 Compile**、**162 refs / 38 assets**，仅换诊断源与版本；原安装冻结源/引用/资源保持。构建器纠正了活工作树SHA可能误记为冻结产物来源的问题，最终记录使用实际冻结source哈希。初始本机候选和失效脚本/审查原件保留，不安装。

最终本机候选 `10.9.39-win243-diag.281.2`：DLL SHA256 `a3d49aabd6652db244be21be301c522871c3a39dfce537d3fc34d6606f14d83c`（4527104 bytes）；冻结诊断源 `fc8e42910eddb2a7a3bb9159269ca87697735dac9b8d7dff1c797856b373a1f7`，构建log `3ed6077301dfca7c3f2104722756dc8b793570e2bc701ce6634f483272820618`，输入manifest `cbedb82d74d4c0c685d03a90431c25206b7daa7bb779c6ce7ab187772c67a180`。支持件及输入构建前后均核对；其余源码和资源未替换。**没有传送或安装该DLL，没有修改Windows受管清单，也没有恢复任何玩家数据。**

下一轮安装必须新核进程与备份、DLL和受管JSON成对更新并通过具体执行审查，不复用10/25单清单修复脚本或过期凭据。真实船归属采样、职业跨岛交接修复、登岛生成及保存退出冷重进仍未完成。

Operator本阶段接受与Git交付决策已由OMP实际 `zhipu-coding-plan/glm-5.3`（请求max，公开模型事件核验）审查通过；实现为实际 `deepseek/deepseek-flash`（请求max）。限本机诊断准备和Draft PR增量，不预付安装/合并关口。精确公开模型事件与原始日志SHA保存在私有任务证据，不上传私有推理或玩家内容。
