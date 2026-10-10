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

尚未实机证明 writer factory 实际调用线程与 owner path 捕获时机、Harmony 注入、Task.FromResult 失败传播、sync boxed Return 4B 回写、GC 下委托生命周期和 Windows 行为。交付为 draft PR；共享 Mac 原生窗口、存档备份、安装及游戏实测由主 operator 统一，未通过隔离 gate 前不合并或安装。测试/构建/审查私有回执位于本机 Issue198 同期保存 task；不将玩家原始存档、对象 ID 或 provider 私有思考写入 GitHub。
