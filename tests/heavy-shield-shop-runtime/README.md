# 宝石盾卫店铺生产付款回归

Tests.csproj 直接编译生产 HeavyShieldShopShell、HeavyShieldShopPayment、PatchRoles_HeavyShieldArt、公共 Contracts 和 HeroShop 纯规则。Stubs.cs 仅替代 Unity/IL2CPP/native API 与 A/C 协作接口，不初始化游戏。生产付款核心、Started/OnPay adapter、取消/延迟清理、Pool.Spawn 发行路径和 paid Bow 借字段 marker 都真实执行。

运行（cwd 为仓库；禁部署、独立产物路径）：

```powershell
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/heavy-shield-shop-runtime/Tests.csproj -p:BepInExPluginsPath= -p:BaseIntermediateOutputPath=obj/worker-b/ -p:OutputPath=bin/worker-b/
C:/Users/ADMIN/dotnet8/dotnet.exe tests/heavy-shield-shop-runtime/bin/worker-b/Tests.dll
python tests/heavy-shield-shop-shell/test_contract.py
C:/Users/ADMIN/dotnet8/dotnet.exe build tests/heavy-shield-shop-runtime/ArtRegression.csproj -p:BepInExPluginsPath= -p:BaseIntermediateOutputPath=obj/worker-b/art/ -p:OutputPath=bin/worker-b/art/
C:/Users/ADMIN/dotnet8/dotnet.exe tests/heavy-shield-shop-runtime/bin/worker-b/art/ArtRegression.dll
python tests/heavy-shield-shop-runtime/run_atlas_regression.py
```

覆盖：4Gem铸模、左右各2Gem扩位、左右6Coin盾具、native header五点、CanPay只读、首次identity/carrier门、Started冻结、同票CanPay、不同payer/payable、混币/错额、重复/重入、native浮币未清时不可切阶段、部分Gem/Coin取消、Drop延迟或抛错、双人争同货位、native null/unknown发行、cached-active Bow不被标记/借字段、拒绝与unknown区分、没有补钱包或重复取消、Completed关闭延期、单点hide不清全店、Menu保留、paid/claimed工具占位与不重锚、paid视觉/physics原生disable归还、普通池复用。

艺术测试替身仅从PNG头读真实尺寸，像素数组不模拟Unity解码；真实RGBA/alpha/切片/合成由现有Pillow verifier的682断言验证，纯布局沿用现有1190断言。wrapper仅将该已有verifier输出改到本目录 evidence，未修改原验证器。

边界：未运行实际IL2CPP invoker/Unity/游戏；native钱包实物、原生选择距离/布局观感、跨岛/存档roundtrip需最终实机验证。实际2.4显式退款未获证，因此拒绝/未知完整付款保留Unknown，占位且不重复发货/补钱。实际Spawn抛错且未返回可确认GO时不能隔空回收未知对象。

## R2 失败启动回归

运行产物另用 `-p:BaseIntermediateOutputPath=obj/worker-b-r2/ -p:OutputPath=bin/worker-b-r2/`，再显式执行 `bin/worker-b-r2/Tests.dll`。原107断言保留，新增109断言，总216通过；shell guard增加1项，总5通过。

reserve=false/throw/contract mismatch 或广告之后容量达到4096时，真实生产Started仅冻结本地失败观察并关闭模块新收费。后续更晚frame driver Tick重新核对payer/payable/shoplife/world/kingdom与Transaction/selected点，才至多一次调用native CancelTransaction + DropFloatingCurrency。币在Started返回之后才追加亦会进入此取消；callback中取消次数为0。Drop正常返回、浮币Clear、completing清除、native状态settled全部满足才清本地失败观察；未确证A lease永不调用A ConfirmUnpaidCancellation。

foreign点/payer/world/life、已经Completed、Drop抛错保留Unknown，不取消别人的交易或重发/补钱包。Drop未Clear、Cancelling未settled保持观察并留住店铺，重复Tick不重发取消。菜单/关闭/保存helper不能绕开下一driver tick；收尾后模块fault仍保持，关闭重建或重新开开关不会解锁。恢复策略为明确诊断后重新加载，A自身Unknown/已付权益仍须按A的恢复边界处理，不生成新GUID或自动重试来恢复收费。
