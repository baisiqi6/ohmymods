# #270 扩展坐骑岛出航结果交接

- 基线：`release/v9.5.13` / `2b22aa5b21e2d8d96dd01b754b047836e8d166fe`。
- 范围：Greek 扩展地图的成功选择由 UI 域转为 physical 域；不直接改 Campaign、不放宽测试拒绝条件、不修详情贴图或设施恢复。
- 当前状态：产品实现与 Operator 窄集成、最终双TFM0W0E、22/230 host验证及独立代码审查已完成；新隔离候选准备中，Native未验、未合并、未安装。

## 已复核的首次偏离

Mac IL2CPP 2.4.0 的实际隔离 A21：Stage1 Confirm 成功接管，第一岛 `landResult=11`，而 fresh `focusedLand=10`。随后原生 `Campaign.CurrentLand` 及对应原始字段均为10，隔离守卫主动停止 owned 子进程；不能把窗口关闭称为已证原生崩溃。

当前 ARM64 原生 `<BaseShowRoutine>d__76.MoveNext`（token `0x0600245e`、RVA `0x732298`）在 `0x732458` 读取 `focusedLand`，构造 Nullable 返回值并在 `0x73248c` 写 iterator 内联 `result`。Greek/Menu 转发不映射，最终 Campaign setter 直接写入。旧补丁只写 `landResult`，没有履行这条真实返回契约。

全158758个 MethodDefs 中153317个非零地址条目，仅该方法映射到 `0x732298`。这证明当前 Mac 二进制未见此入口地址折叠，不代替其它构建和平台的证据。主 interop SHA256 `d6b249a4c5d7c833bec733d172df9a6083e87e5fc203c466d9195f0b7bc7041f`；ARM64 slice SHA256 `9815ae67da7d013be597ac630d611a3da77a94819885830222aef38e1fd00ef0`。

## 修复约束与顺序

1. 独立审查精确结果边界和 ABI；UI focus 始终保持 UI 语义，不能将 physical13 放进 UI0..11。
2. Worker 在本任务工作树实现具体 iterator 完成处的一次交接，绑定 owner/show/campaign/登记代次和成功选择；取消、旧回调、重复完成不得升级或二次映射。
3. 只补此变化的 host 正反例和纯编译，再进行独立代码审查。ABI/真实生命周期另由隔离 Native 验证，host 不冒充。
4. 新候选与新运行凭据准备好后，再交人类自然出航8→11；目标设施、Save+Quit 和同实际后像冷读分开留证。第二岛13若只做 host 校验必须明确未实机。

## API 边界

实际类型为 `Routine.Return<Il2CppSystem.Nullable<int>>`。iterator getter 会把内联 Return 装箱成副本；仅改 `iterator.result.value` 不会写回原结果。setter 通过 unbox + `class_value_size` + cpblk 写回内联值。泛型 ctor 参数存在 wrapper/value ABI 歧义，不凭 C# 签名采用。采用实际 metadata 校验后的内联 int 单次4字节写入：具体 iterator 正常完成后，只有同一次有效确认票且已有 hasValue=true、value=expectedUI，才写捕获的 physical；不碰其余字节。布局不符则在 Hide 前禁确认，不制造 Nullable/Return 值。禁止额外 pop 或 detour 共享泛型 getter。

补核推翻了“None 可作取消回退”的假设：当前 Greek 和 Game 出航链直接调用 Nullable.Value，空值会进入 ThrowInvalidOperationException_NoValue。因此不合成 None、不写 hasValue；取消/无票/旧show/失配原样，不宣称这些未实测路径已通过。原生 Confirm 的 Stage1 也仅写 landResult 后 Hide，没有可复用的 UI→physical 转换。

## 未完成

产品实现、最终构建/测试和代码审查已完成，见 receipts/root-verification.md。新候选实机、合并和正常版安装均未完成。A21 未生成11/13、未完成目标保存重进，不能作为目标冷读父档；正常存档与正常 DLL 保持。
