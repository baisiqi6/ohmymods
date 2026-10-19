# #270 Operator 最终源码复核（2026-10-19）

当前基线 `2b22aa5b21e2d8d96dd01b754b047836e8d166fe`，只涉及扩展地图确认结果交接。worker.md 保留旧轮次的历史哈希/21测试；本回执及 root-builds.json、root-tests.txt 对应最终源码。

## 通过范围

- 首次偏离已由 A21 日志与当前 ARM64 原生确认：辅助 landResult=11 不是实际出航返回；BaseShowRoutine 从 focusedLand 读取 UI10，旧链最终写 Campaign10，隔离守卫终止 owned PID。不是已证原生 crash。
- 专属 Server.GetReturnValue 原生 `0x273c930` 直接读 iterator+0x40，与 `0x73248c` 写入同 cell；具体 BaseShow.MoveNext 在当前158758个 MethodDefs 中只有一个非零映射到该地址。证据范围为当前 Mac 2.4.0，不推及全部 generic 地址或其它平台。
- 完成边界仅消费 exact owner/show/iterator/campaign/token/成功确认票，对原有 hasValue=1、expectedUI 值做一次对齐4字节写入；UI focus、Campaign、hasValue 不写。无票/取消/失配原样，不合成 None，不声称这些未实测路径已安全取消。
- 修正 boxed metadata offsets、Hide 前的 active map/state/真实 Hide target 重查、每次调用自己的 handle；元数据形状门先于 result getter，box/class/range/size 门先于 unsafe 字节读取。布局不符在 Hide 前禁用扩展确认，只记一次可见日志。
- Root 最终独立构建 net6/net8：两者 exit0、0 warnings / 0 errors，NoDeploy=true 且 BepInExPluginsPath 为空。161 references、277 Compile（当前276产品源+私有PluginInfo）、38资源。不是私有出航恢复包的构建配置。
- Root 测试22 passed / 0 failed / 230 assertions；直接链接生产 plan，不加载游戏/Unity/IL2CPP。本机 .NET10 runtime 以 DOTNET_ROLL_FORWARD=Major 运行 net8测试。最后日志接线一行变更后已重编两框架；纯 helper/tests 未变，复用已通过测试。
- 独立内置 Reviewer 复核最终 helper1d5994f0 后 approved，0未关闭P1/P2。决策与worker实际模型事件见 model-evidence.json；不复用旧运行许可。

## 最终代码与产物

- `il2cpp/ExtensionIslandMap.cs` SHA256 `f5647580f77333d897e75cd699e069c5d99a2d37ada5f3a256b083f237fb0219`。
- `il2cpp/PatchExtensionIsland_MapResult.cs` SHA256 `1d5994f090dbadd382923b720b07805fc1de69c5e213190bc52a8f0fd2a57454`。
- `il2cpp/ExtensionIslandMapResultPlan.cs` SHA256 `b84b84b9abc003e69432d15a897c7a6dd3a02ca145367f8965b40d4ae14e4b17`。
- `tests/mount-island-map-result/Program.cs` SHA256 `2fbffaba2d9a31c1a3ed04f8aa8ea11cbf6813899322c1e1795c651c23e0a0be`。
- net6.0 DLL SHA256 `da4183e427a4292d14a84d9c3969986d24bd0fa48dbb29b69c8bed5b7c7ec6b8`。
- net8.0 DLL SHA256 `7dc7ab1b5d812a0d1bda5e3a93fcf5bb0481b793a4a721a4282aee27273c6145`。

独审报告本机 `/Users/Admin/projects/ohmymods/.local/tasks/mount32-fresh-validation-20261019/issue270/code-review-independent.md`，SHA256 `f310494507db4309e5b86769dddabc1a22ffe3e77fed78fa353c28c70828c932`；原生cell报告 `/Users/Admin/projects/ohmymods/.local/tasks/mount32-fresh-validation-20261019/issue270/native-result-cell-readonly/report.md`，SHA256 `756897e4e21d6411d33f22d6ab444d3b097502ec3e16dec93e9712f7c9e31319`。

## 待验边界

没有启动新游戏或写正常存档/配置/DLL，没有合并安装。Native metadata实际值、hook安装/完成时序、第一岛实际出航及八设施、Save+Quit和同完整实际后像冷重进仍待新隔离案例；第二岛13仅host校验。详情页岛形问题独立未修。Android/Windows未构建或实机，本任务不改其pin。

私有出航候选沿用A21冻结显式清单，只叠本次差异及两个新文件，实际279 Compile/161refs/39资源；与本回执纯主线构建分开。旧pure275仅作基线引用，不是已批准cold候选。A21 land10后像不能用作目标冷读父档。
