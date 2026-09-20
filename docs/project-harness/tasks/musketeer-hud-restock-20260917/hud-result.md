# HUD worker result

2026-09-17，北京时间下午内置 worker 有界实现完成。仅改动 `il2cpp/PopulationCounts.cs`、`il2cpp/PopulationHud.cs`、`tests/population-hud/Program.cs`、`tests/population-hud/Stubs.cs` 及本文。

- 旧职业索引 0..7 保持，火枪手追加为 8，`RoleCount=9`，内部骑士哨兵改为 9。全仓检索未发现骑士哨兵外部消费者。
- 名册重建时仅为原 Archer 分类缓存 Archer 组件。每秒 Sample 在有效身份、活动对象、本场景/本世界 layer 和存活检查之后调用既有 `MusketeerIdentity.IsUnit(Archer)`，把当前采样的一人计入普通弓箭手或火枪手之一，不修改缓存的原职业分类。因此 AddCharacter 早于 Bind、延迟读档身份确认、身份解除都能不重建名册更新。
- 不依赖火铳铺开关；沿用 IsUnit 已确认、当前世界、离线 authority 契约。普通弩兵/英雄 Archer 沿旧规则，骑士与其他组件优先级不变。客户端保持原不可用提示。未新增 hook、身份事件订阅、逐帧扫描或场景查询。
- HUD 新增“火枪手”行，骑士、风格与弹药纵坐标改由职业数组行数推导。1280×720 基准下火枪手 y156、骑士 y182、风格 y202/222/242、弹药 y268；完整未知骑士标签场景矩形不重叠。

验证：`C:/Users/ADMIN/dotnet8/dotnet.exe run --project tests/population-hud/Regression.csproj -c Release` 成功，39 passed / 0 failed（原 32 项保留、新增 7 项）。覆盖稳定采样无新增名册/组件扫描、绑定→解除独占计数、确认延迟、关店/暂停、死亡/池复用、换世界/外层/场景拒绝、online/client 限制、旧职业+骑士优先级及完整标签布局。Identity 在该测试工程中使用既有查询契约的 double；真实 Identity 生命周期由其专属回归负责，不能把这些 HUD stub 测试宣称为真实 Unity/联机验证。

`git diff --check` 对上述四个代码文件无空白错误（仅仓库 LF→CRLF 提示）。主程序集实际 interop 构建、独立复核、部署由 root 统一处理；本 worker 未修改配置/存档/游戏，未构建主项目、安装、启动、提交或发布。实机画面与保存读档显示仍待验收。
