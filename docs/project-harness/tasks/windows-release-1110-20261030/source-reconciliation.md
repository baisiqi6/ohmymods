# Windows 11.1.0 源码收敛（2026-10-30）

本轮只做未来主线收敛，不是 Windows 发行物构建。正式 Windows 包由另一 recipe worker 从已实玩历史 commit 闭包生成；本文件记录把该闭包所需的未合并源码增量接回 `il2cpp` 主线的结果。

## 基线与来源

| 用途 | 来源 |
|---|---|
| 目标主线 | `7eed434`（`codex/win243-source-reconcile-20261030`） |
| 2.4.0/2.4.3 保存协程 alias | `3774d8b`（PR #250；父提交核对 diff） |
| 火枪手留守/登船降级、航行诊断、菜单 gate | PR #282 head `d7694e3`，实际功能提交 `5e79c08`、`98a3603`、`d3da7c9` |
| Release/Optimize/NoDeploy/依赖根参数化 | PR #276 `ec627c2` |

三方合成使用 `git merge-file`：base 为 PR #282 / #276 的 merge-base，ours 为 `7eed434`，theirs 为来源 head。产品文件自动合并无冲突；测试 `tests/musketeer-runtime/UnityStubs.cs` 的 `enabled` 归属冲突按主线结构调整（`enabled` 留在 `Behaviour`，只补 `ThrowOnEnabledSet` 注入）。

## 保存 alias

四个文件仅替换协程状态机类型绑定，保留 Palace 责任链与最新保存逻辑：

| 文件 | 2.4.0（默认） | 2.4.3（`KTC_GAME_243`） |
|---|---|---|
| `CoinCourierPersistence.Hooks.cs` | Save 89 / Campaign 91 / Challenge 94 | Save 92 / Campaign 94 / Challenge 97 |
| `CoinCourierPersistence.cs` | Campaign 91 | Campaign 94 |
| `HeroNativeRights.cs` | Save 89 / Campaign 91 / Challenge 94 | Save 92 / Campaign 94 / Challenge 97 |
| `SharedBankNative.cs` | Save 89 | Save 92 |

当前主线已把 Challenge 钩子移出 `#if !ANDROID`，因此 alias 不再沿用 PR #250 的 `!ANDROID` 条件。Android 2.4.0 interop 实测含 91/94 类型，但使用 alias 右侧简单名无法经 `global using Il2Cpp;` 解析，Android 分支显式写 `Il2Cpp.GlobalSaveData.*`。Android probe 编译通过（0 error，1 条既有 `HeavyShieldBanner._nextActivation` CS0169）。

## 火枪手与界面

- `Patch_MusketeerBoardingPolicy.cs`（新增）与 `MusketeerVoyageSaveDiagnostics.cs`（新增）按 PR #282 原样接入；前者继续 `#if !ANDROID`，后者 `#if VOYAGE_SAVE_DIAGNOSTICS && !ANDROID`。
- `MusketeerArchive.cs`：保留 baseline 与补偿 grant 最后 witness 的淘汰保护，8 项上限内 fail-closed。
- `MusketeerRuntime.cs` / `MusketeerVisuals.cs`：接入登船降级 strict 归还；Visuals 保留主线 Android renderer/ICall 分支。
- `MusketeerPersistence.cs`：只新增诊断观察点，保留主线 Android bank producer/consumer bridge。
- `PatchUI_PanelFocus.cs`：接入 owned menu gate/root CanvasGroup 原值归还；没有菜单其它行为改动。
- `ModPanel.cs` 只读确认：主线已含 `MountIslandGemRewards.Tick/Clear` 与长按购买帮助文字三处，未修改。

## 构建属性

- `ProjectSettings.shared.props`：`GameInteropVersion`（2.4.0 默认 / 2.4.3）、`KTC_GAME_243`、CoreCompile 前校验；`Release` 配置 `Optimize=true`。
- `KingdomEnhancedMod.csproj`：加入 `Release` 配置、`KTCModDevLibsDir` 参数化、Release 引用组、`NoDeploy` 守卫；合并重复的 Audio/UI/Interop.Common/MonoMod 引用；版本数值与入口/注册未改。

## 验证

| 项目 | 结果 |
|---|---|
| Mac ARM64 2.4.0 Release 全源 | 0 warning / 0 error |
| Windows 2.4.0 Release 全源 | 0 warning / 0 error |
| Windows 2.4.3 Release 全源 | 0 warning / 0 error |
| Windows 2.4.3 + `VOYAGE_SAVE_DIAGNOSTICS` | 0 warning / 0 error |
| Android probe Release | 0 error（1 条既有 CS0169） |
| musketeer-identity | 728 assertions（诊断构建 926） |
| musketeer-native-save-pipeline | 258 assertions |
| musketeer-legacy-compensation | 507 checks |
| musketeer-runtime | 137 cases |
| panel-focus | 37 scenarios |
| mount-island-gems / musketeer-shop | 78 / 45 |

边界：以上是编译与移植回归，不是新的游戏实机验收。玩家证据仍来自 Windows `.281.3` 历史闭包；首次出航的 2 个 `EmbarkArchers HashSet` 异常未复现，保留残余，不声称已修复。

独立审查澄清：CoinCourierPersistence.Hooks 仅改 Campaign/Challenge 类型别名；没有 Save 别名。主线标准 Release 的 SDK 不定义 DEBUG；正式 Windows 历史闭包仍显式保留已实玩 B 的 DEBUG/TRACE，两者不能混称同一构建。
