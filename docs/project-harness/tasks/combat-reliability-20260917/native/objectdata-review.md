# ObjectData 原生保存入口的有界只读复核

目标是判断新 CombatTargetLifeMarker 是否需要写 HideFlags.DontSave；没有执行游戏、保存或注入。

- 实际 2.4 interop 的 IslandSaveData/ObjectData 静态初始化器提供 `ObjectData(Persistent,bool)` 的原生 method token **100678276**。现有只读 map-native.py 映射到 **RVA 0x73f3b0**。只对这个构造函数把解码上限从 2048 提高到 8192，并由下一个方法地址截断，输出为 objectdata-review-disassembly.txt；未修改 mapper 或游戏二进制。
- 从实际 GameAssembly.dll 的元数据引用槽读取编码值，再关联同版本 global-metadata.dat（v31）的 type definition / byvalTypeIndex，得到以下映射，原始值和 metadata hash 保存在 objectdata-review-metadata.json：`0x183D1C0E8 → IBehaviour`、`0x183CEF078 → IRPCable`、`0x183CEB778 → ComponentData`。
- 构造函数在 `0x18073F88B–0x18073F895` 调用取得一个数组，随后遍历，`0x18073F92A` 起使用上面的 IBehaviour 类型做接口分派；另一分支在 `0x18073FDFD–0x18073FE07` 取得数组，`0x18073FE98` 起使用 IRPCable 类型做接口分派，随后构造 ComponentData 写入列表。
- 两处数组获取调用共用原生地址 `0x180BFBFF0`，但使用不同 MethodInfo 引用（usage kind 6，索引 154905 / 154903）。本次没有恢复完整 methodSpec 泛型名称，因此不把 `GetComponents<…>` 的全符号恢复宣称为已完成。实际 2.4 的接口分派与 ComponentData DTO 结构已经核到；它们与本地 2.1 IslandSaveData.cs:1518/1530 的两个接口限定 GetComponents 路径一致。

结论：原生接口保存路径与一个不实现 IRPCable / Persistent.IBehaviour 的纯运行时 marker 分离；未见此构造函数遍历任意 MonoBehaviour 字段。结合原生 DTO 设计和 2.1 源码，移除不必要的 marker hideFlags 写入有明确依据。这里没有实际写档/重载证明，也不扩大为对全部原生保存方法的全审计。

另独立查询 [Unity 2022.3 HideFlags 文档](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/HideFlags.html)：DontSave 包含 DontSaveInEditor / DontSaveInBuild，文档描述这些位可能使对象经历 OnDisable/OnEnable。新 marker 对观察缺口采取 fail-closed，因此不必要的 flags 写入会给初始化引入额外生命周期风险。实际 2.4 特定组件 setter 的运行行为没有启动游戏验证。
