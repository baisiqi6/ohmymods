# Issue #99 验证记录（清洁 PR 候选）

范围与文件清单见 `plan.md`。以下为提交候选自身的可复现验证；实玩验收由用户完成，
模拟回归与编译不能替代实玩。

## 回归套件（net8.0，无 Unity、无游戏）

| 套件 | 结果 | 覆盖 |
|---|---|---|
| `tests/coin-courier-visual-lifecycle` | 11 passed / 0 failed | 注入原生回收（纹理/切图/整树/渲染器失效）：原地恢复、行为状态保留、失败有界重试、坏素材闭锁、正常零抖动 |
| `tests/coin-courier-visuals` | 23 passed / 0 failed | 动作表与仓库内作者 manifest 逐项核对、渲染/镜像/幂等、+5% 身高（0.625 / 0.65625 / Z1）、传送 FX 与泄漏检查 |
| `tests/greek-civilian-height` | 19 checks ALL PASS | 共享常量精确表达式 `0.70f*1.05f*32f/18f`、三入口共用、只写 Y、作用域与不累乘 |
| `tests/coin-courier-runtime-bridge` | 203 checks ALL PASS | 行为回归；同套件基线为 197，本切片净增 6 条断言，覆盖夜间/受阻原地 Idle→Leisure 新契约 |

命令（仓库根，.NET 8 SDK）：

```sh
dotnet run -c Release --project tests/coin-courier-visual-lifecycle/CoinCourierVisualLifecycleTests.csproj
dotnet run -c Release --project tests/coin-courier-visuals/Regression.csproj
dotnet run -c Release --project tests/greek-civilian-height/GreekCivilianHeightTests.csproj
dotnet run -c Release --project tests/coin-courier-runtime-bridge/CoinCourierRuntimeBridgeTests.csproj
```

## 编译

- ARM64 实机参考集（161 个 interop reference assemblies）纯构建：本 PR 源码 0 warning /
  0 error；未修改基线以同构同参构建亦 0/0，未发现需要带入本 PR 的基线级平台差异。
- 构建不执行部署：无运行 DLL 复制、无游戏目录写入。

## 范围与资源审计

- 代码/资源差异 = 6 个修改文件 + 9 个新增文件（2 个新测试目录）；另外本目录计划与验证
  文档 2 个。无存档、GameAssembly、interop 二进制、配置或玩家数据进入提交范围。
- 唯一资源变化为 `KnightStylePreview0.png`（替换为原生 `knight_idle` clip 1144 →
  Sprite 8060 灰盔帧的透明补边画布，不拉伸）；其余 4 张预览图及全部其它 PNG 逐字节保持。
- 新套件 README 使用通用 `dotnet run` 命令，不含本机路径、账号、日志或其它私有信息。

## 实玩状态

可见性恢复、身高与动作观感、夜间/受阻休闲循环、跨存档切换均**未实机验证**；合并后
Issue 保持待用户验收，测试通过不等于玩法验收。
