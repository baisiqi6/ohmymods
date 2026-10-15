# Issue206 / PR207 当前主线兼容验证（2026-10-15）

此前已实装的字体日志修复与保存/角色热路径优化，现吸收release `47265e69cbdbeb087ef91cd4c5e962ed871802ab` 完成公开源码收尾。保留201/202/222/228/233/236/238已合入内容；progress追加冲突两边保留。

新增兼容仅5文件：Android显式链接真实CanonicalHashBuffer与HeavyShieldHookQuery；HeavyShieldPersistence末项在ANDROID保持原managed字典比较，PC保持NativeKeyReader，所有前置guards/catch不变；两个legacy宿主补HashBuffer，save-hash宿主补真实LegacyCompensation类型依赖。75条FrozenSources全保持，不引入Android NativeKeyReader或新注册/设置。

worker实际回归并经独审：AndroidD/R均0W0E，对两真实DLL各完整Adapter1264/0；公共PC net8/net6均0W0E；legacy-archive21、legacy-native-gate18场景405断言；font-policy61、font-regression21、save-hash47、hook-query92、native-key真实reader/fakeadapter宿主ALL PASS；shield identity136+92、save30+76、native-save-adapter41、pipeline33、hero fingerprint9。net6 native adapter仅编译且有1个NETSDK1138 EOL提示，未冒称net6执行。

五文件按before/after SHA精确移植。worker组合与当前W之差仅已合入PR202/222的地图输入（8改、11增），性能/保存/对应测试输入不变；本轮另对最终W重跑公共PC net8/net6、AndroidD/R及实际DLL完整Adapter，通过。未启动游戏/修改正常DLL，不声称Windows、原生保存性能或Android设备通过。后续玩家问题另Issue/PR。

证据：`.local/tasks/release-consolidation-20261015/workers/perf207-compat/independent-review.md`及`pr207/`实际构建、应用SHA和输入差异记录。已知失败/报告归因修正保留，未以替身或放宽oracle修产品。
