# Issue #198：火铳职业与原生存档同期保存

范围：IL2CPP 2.4.0，基线 `9ea703f8`，关联 [Issue #198](https://github.com/baisiqi6/ohmymods/issues/198)。本次不改盾卫、宫廷、双坐骑岛、Mono 或正常运行环境。实现由 Codex operator 统筹、OMP DeepSeek Flash max worker 实现 helper/native adapter，独立 reviewer 与 GLM 5.3 max 决策审查分别留证。

## 错误产生处

旧机制在岛屿捕获成功后独立写职业附加档；完整原生文件由后续异步 global save 写出。前一次成功不能证明后一次接受了对应对象行。现存 17 份记录与原生对象无法匹配，现有历史证据不足以确认第一次分离由哪一次写入造成。

## 当前实现

- 沿用 IslandSaveData.Save/GetID、加载及生成入口，捕获同一次原生保存的对象身份。职业 GUID、岛屿 context/epoch 和精确 fingerprint 存入原生 Prefs 自定义键，随游戏正文一起序列化；捕获时不再单独落盘职业档。
- 在实际异步文件 writer factory 与无 yield 的同步 writer MoveNext 之前验证出站字节包含已捕获职业 checkpoint 和原生对象行。不改写出站字节；缺行、丢键、歧义或已确认 capture fault 返回失败，不以错误正文覆盖存档。
- 写入租约等待实际 Task 终态；未返回的 factory/同步临界期为 Busy。只有没有 Task 见证的异常/空返回保留 UnknownWriter；不以超时推断写入已经结束。
- 接受的回调只转发一次原结果。备份从接受后实读文件派生；异步观察仅核对自己的冻结内容，不用 FIFO 推断 writer 归属；内容相同的保存具有相同的配对结果。无法核对或观察容量已满时明确降级，不声称备份成功。
- 配对容器同时保存原生压缩字节、文件标识与摘要，职业档由其中实际原生 Prefs 解码。恢复必须匹配当前实际原生字节和摘要；不按人数生成单位、不将旧世界覆盖到现存世界、不覆盖损坏或未来版本数据。
- 原生 key 存在时以它为权威；key 缺失时才尝试严格同次配对及旧附加档迁移。旧附加档 mirror 只在接受且核对真实文件后写出；mirror 不可写不破坏原生正文中的职业权益。
- 保存拒绝接口不可用时在付款前拒绝新购买并在 F5 显示原因。读取失败、身份不明时仍保留原记录，不能靠清空记录解锁。

## 记录含义

每个 context 包含原生文件、campaign 槽、challenge 与 land；epoch 区分同一上下文的生成历史。GUID 在枪→单位→掉落枪之间转移，不能把这些状态各计作一次购买。死亡/回收释放运行时绑定，保留身份权益；当前没有完整持久化的死亡事件账本。职业记录数不是存活人数，也不能据此补兵。旧 17 份记录尚无可核对的同次备份，本次修复不声称已恢复这些单位。

## 验证与交付边界

纯编码、严格解析、配对和 adapter 的生产源码链接测试，及真实 Identity/Persistence→Stage→writer→配对→冷读的联结测试分别验证。旧 identity/restock/defense fixture 使用显式 storage substitute，只证明原有生命周期，不代替 native 同期保存验收。

可复跑：

```sh
dotnet run --project tests/musketeer-synchronized-save/MusketeerSynchronizedSaveTests.csproj -c Release -f net8.0
dotnet run --project tests/musketeer-native-save-adapter/MusketeerNativeSaveAdapterTests.csproj -c Release -f net8.0
dotnet run --project tests/musketeer-native-save-pipeline/MusketeerNativeSavePipelineTests.csproj -c Release
dotnet run --project tests/musketeer-identity/Tests.csproj -c Release
```

截至 2026-10-10，独立审查通过纯 helper 487 断言、durable adapter 37 用例及追加反例后 47 用例；真实六生产源串联 33 断言通过，旧 identity/restock/defense 分别 343/72/127 通过。固定源码与实际 interop 引用的 Mac net6、net8 Release 全 Mod 构建均 0 warning / 0 error（本机运行 host 为 net8；net6 只有编译证据）。最后注释清理已独立确认行为源码逐字等价。

2026-10-10 交付时尚未实机证明 writer factory 实际调用线程与 owner path 捕获时机、Harmony 注入、Task.FromResult 失败传播、sync boxed Return 4B 回写、GC 下委托生命周期和 Windows 行为。交付为 draft PR；共享 Mac 原生窗口、存档备份、安装及游戏实测由主 operator 统一，未通过隔离 gate 前不合并或安装。测试/构建/审查私有回执位于本机 Issue198 同期保存 task；不将玩家原始存档、对象 ID 或 provider 私有思考写入 GitHub。

## 2026-10-12：Mac 原生返回值读取修复，完整验收仍待

按当前 `release/v9.5.13 @ d355d93b` 与 PR head `03b9f78d` 整合为 `0aceb55a`；未带入未合并的 #202/#207。当前游戏为 IL2CPP 2.4.0 ARM64，GameAssembly SHA-256 `738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`，Interop Assembly-CSharp SHA-256 `d6b249a4c5d7c833bec733d172df9a6083e87e5fc203c466d9195f0b7bc7041f`。Interop 是包装与映射证据，不是原方法实现。

真实“保存＋退出”触达同步 `d10.MoveNext`，保存输入与实际磁盘字节一致，但生成的 `Return<SaveLoadResult>.value` getter 返回异常数值。两路独立静态核查与后续原生负对照确认：getter 将 `(fieldAddress, true, false)` 传入当前 Runtime 的 `PointerToValueGeneric<T>`，该路径连续装箱两次，读到枚举类指针的低 32 位。原同步成功判定因此不能证明配对晋升；先前构建及 host 通过结论没有覆盖这个桥接缺陷。

仅在本 PR 替换同步成功判定和拒绝结果 readback 两处读取。`ReadSyncReturnCode` 从实际 box 解析字段，核对闭式类、字段非静态、枚举类型和双方四字节宽度；每个用于原生转换的句柄先检查非零，最后调用 `il2cpp_field_get_value` 并保活 wrapper。保留原生 32 位结果与既有只接受 `72` 的晋升条件，未知或失败结果不晋升。原 setter、写盘门、异步流程、身份、租约及全局 Runtime DLL 均未改。

验证分别记录：

- Host 逻辑回归：保留坏 getter 的两个旧读点时出现五处失败；替换后 adapter 39 用例、pipeline 33 断言、synchronized-save 487 断言、runtime 112 断言通过。空 fieldType 守卫撤去时另出现三处失败，恢复后通过。两项 finalizer 变体只能证明类不匹配不晋升；具体宽度及空 type 守卫由直接 reader 用例覆盖。这些 fake 不证明原生 ABI。
- 当前引用的全 Mod 与私有探针 net6/net8 四种编译均 0 warning / 0 error；独立 exact-source 与 GLM 5.3/max 审查通过。
- 隔离原生盒样本：setter 写入 `72/136/392`，新 reader 分别准确读回；旧 getter 三次都等于枚举类指针低 32 位。实际 Return 与 enum size/align 均为 4，字段从 boxed 基址偏移 16、flags 6。`Task.FromResult(136/392)` 实际 completed 且 Result 正确。这只是字段及 Task API 局部验证，没有当作真实 writer 的失败传播或同期保存通过。

私有 v2 探针 DLL SHA-256 `4497381b1263a06c46e2df90dfe1199618d08bbc2d564bfa78a8c9bbb15ecdbf`，生产保存源码 SHA-256 `080bb7f232ce487cafd46d5b0570626361cbe31f44d3727b5ed0655ac253f9cd`；完整材料、模型与原生行号回执在 `.local/tasks/pr204-native-validation-20261011/`。v2-smoke-i 到 600 秒自动关闭，正常运行目录、游戏文件、共享存档、偏好与日志均恢复到该轮新鲜备份；正常 DLL 仍为 `9ef503f2...`，未安装候选。

自动菜单输入没有进入新战役，已请求玩家仅协助操作隔离窗口。新候选的真实 writer 结果与配对、两笔合法付费的 Unit/Rack 保存、失败拒绝及下一次合法保存、callback/pending GC、退出进程冷读与无探针生产候选仍未验；实际 Busy、Windows 及旧 17 条恢复也未证明。PR 保持 Draft，完整原生关口通过前不合并。


## 2026-10-13：Mac 付费样本与原生拒绝路径验证

验证生产基线为 `c1f69dd2`，当前 2.4.0 ARM64 游戏及 Interop 哈希与上一节相同。无探针候选已由玩家真实购买 Unit 1 与 Rack 1，保存退出后冷启动，两份付费身份均绑定到新原生对象；后续原生日志显示敌人拾取工具；最终两条身份记录不能证明枪仍在架上，Bep1500 的原始具体原因仍未确定。该无探针结果与本次私有故障验收分别留证。

最后隔离窗口 `v4-paid-writer-primed-20261013` 使用同一 pristine 付费样本，生产保存代码 SHA256 为 `080bb7f232ce487cafd46d5b0570626361cbe31f44d3727b5ed0655ac253f9cd`，私有 net6 探针 DLL SHA256 为 `d7439a96edd2575e7de9381ef966b7634667709de329f0e230b14f105408224a`。私有命令先调用当前原生 `IslandSaveData.SerializeObjects` 的真实场景采集入口，使用经核对的空 Nullable 包装；checkpoint 与 proof 摘要真实变化，owner 与保护集合保持，磁盘未写。未注入异步保存随后真实 Task/result/callback 返回 72、回调一次、输入与磁盘一致，证明后续故障试验具有合法缓存前置。

- 同步：一次损坏 gzip 注入到实际 writer 后，正文未运行，boxed reader 与 Haglet 返回 136，文件 SHA、长度、mtime 与完整目录逐文件见证均不变；下一次未注入同步 writer 正文运行并返回 72，输入与磁盘 SHA 一致，租约终态 0。
- 异步：一次同类注入令实际 writer 正文跳过、真实 Task 返回 136，原始回调恰一次收到 136，目录见证不变；下一次合法 writer 正文运行、Task 与原始回调返回 72，回调恰一次、输入与磁盘一致。最后文件 SHA 为 `da4157130f6365fc31c8f0c3dabbe873f7155246929ed2d397ccc7f9732d6242`、305894 字节，配对容器字节与实际文件一致。
- 一次托管与原生 GC 调用发生在原始回调仍未完成、生产 observation 强根存在时；调用返回后回调仍未完成，随后合法 Task/回调各返回 72。未证明对象确被回收，GC 时 writer factory 尚未返回，不能称覆盖了 pending writer lease。

异步 Task 完成后，生产租约会保留已完成 Task 的强根，直到下一次同路径 writer 在 `TryEnterPath` 读取 `IsCompleted` 并回收。本次下一同步入口真实回收该租约，拒绝及后续合法同步保存的租约均为 0；最终异步保存留下已完成 Task 的惰性强根，不将它写成尚未完成或即时清零。无周期清理、超时释放或人工清租约。

此前私有 seq26/28 直接运行 global serializer，遗漏完整场景采集；未注入调用被 `NoMatchingSnapshot` 拒绝，因此不能作为合法保存恢复失败或生产缺陷证据。原有失败保留，本次修正仅限本机验收工具，未改生产补丁或加入 PR。严格首次 Playing 快照也已观察到两份身份在周期更新前绑定；此快照不表示持续架上库存。

窗口正常退出（732.17 秒，超过 600 秒目标但未延长 1800 秒硬上限），内外恢复及最终只读复核的正常目录、游戏、存档、偏好和日志五项均一致，没有游戏进程；正常 DLL 保持 `9ef503f2`，未安装候选。私有完整回执位于 `.local/tasks/pr204-native-validation-20261011/v4/`，不发布玩家存档、对象 ID 或编码档案。

Mac 必要安全分支已验证；最终独立结果审查与合并裁决另行留证。实际 Busy、Windows、旧 17 条权益恢复及设备端配对恢复接受正例仍未验证，Bep1500 的原始具体原因仍 unknown；这些边界不被 Mac 结果代替。历史材料中的 pending GC、首次 Playing 及全部平台覆盖不再作为额外的 Mac 合并硬门，不无痕改写早前“尚未验”记录。
