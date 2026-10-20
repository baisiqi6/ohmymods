# Issue #270 worker receipt（r2，集中审查修正后）

- 角色：有界 implementation worker；本轮只改本任务允许的新文件与测试。
- 工作树：`/Users/Admin/.codex/worktrees/mount-map-result-handoff-20261019/ohmymods`
- Branch：`codex/mount-map-result-handoff-20261019`；基线 `2b22aa5b21e2d8d96dd01b754b047836e8d166fe`。
- 未运行游戏/VM；未安装 DLL、未写存档/配置/Steam；未 commit/push/merge；未改 Android。
- `il2cpp/ExtensionIslandMap.cs` 的 Root 集成改动（out-handle + 显式 hide + `Clear(menu)`）已存在，worker 未编辑/回退；`docs/project-harness/tasks/issue-270/plan.md` 未改。

## 受控 diff（worker-owned）

新增：

- `il2cpp/PatchExtensionIsland_MapResult.cs`
- `il2cpp/ExtensionIslandMapResultPlan.cs`
- `tests/mount-island-map-result/MapResultTests.csproj`
- `tests/mount-island-map-result/Program.cs`
- `docs/project-harness/tasks/issue-270/receipts/worker.md`（本文件）

仅本机构建/日志产物（未提交、非产品源码）：`.local/issue270-worker/build/`、`.local/issue270-worker/logs/`。

## 本轮审查修正

1. **值类型 offset 归一化（P1）**
   - `TryResolveLayout` 保留 `iterator.result` 的 reference-type 对象坐标；`Return.value`、`Nullable.hasValue/value` 的 raw metadata offset 作为 boxed 坐标送入事实。
   - 生产 helper `ExtensionIslandMapResultPlan.TryNormalizeLayout` 用实测 Return box object_unbox header 与 `il2cpp_class_instance_size(Nullable<int>) - valueSize` 的 class ABI header 交叉校验并归一化：raw `16/16/20` + Return header `16` + Nullable class size `24` → payload `0/0/4`。要求 class instance size `>=8`（宽有符号减法前检查）、class size = header + 8、header `>=2*IntPtr.Size` 且指针对齐、Return box bounds、raw/payload bounds。
   - 不调用 `il2cpp_value_box`、不构造/装箱 Nullable/Return；`NullableClassInstanceSize/NullableClassHeaderDisplacement` 是 metadata/class ABI 事实，不冒充 boxed Nullable 观测。boxed Return 的 class 必须是精确闭合类，inline/Return boxed 字节与 `field_get_value` 字节必须一致。
2. **TryArm 最后关闭门（P1）**
   - 新增 `Menu hide` 参数与 `out object confirmationHandle`。
   - arm 前重查：exact active show/owner/iterator、`hide.ActiveMap == menu`、`ExtensionIslandMap.IsActiveMenu(menu)`、`menu.CurrentState` 允许值（`>=2` 的已知 `ShowingMapOnly/ShowingWithTimeline/Opening`；`Closing0/Closed1`/未知拒绝）、实际 Hide target 必须是 `Menu.Inst` 或 `menu._menu`、当前 campaign 与 slot world token。
   - 最后用 `IsShowStillActive` 再核 show 代次/iterator state/未 armed，防止 getter 同步回调换 show 后给旧 show 立票。
   - 任一 getter 未知/异常 → 返回 false，调用者 consume 且不 Hide。
3. **exact per-invocation handle（P2）**
   - 删除全局 `_hideTicket`；`TryArm` 返回 exact `Ticket` handle，Root 在 Confirm 栈帧保存并传给 `AfterHide(menu, handle)`/`Abort(menu, handle)`。
   - 两者只处理该 handle，且要求 `show.Ticket == handle`、menu 身份一致、未 revoked/consumed/committed；旧同 menu 新 show、旧 foreign menu、同步已提交后 Abort 均不会碰新票或假回滚。
4. **删除未验证标量身份**：移除 `_nextShowId`、`Show.Id/Generation/OwnerPointer`、`Ticket.Ui/Physical`、plan 的 `Campaign/TokenId/ShowId/ShowGeneration`；实际 show/ticket 绑定以对象引用、menu/iterator pointer、campaign pointer、world token 引用为准。
5. **提交路径不变**：只在 exact ticket + native hasValue 恰为 1 + value == expected UI 时，对 inline `Return` 的 value 做一次 4 字节对齐 int 写；不写 hasValue/其它字节，不构造 Nullable/Return，不 pop/peek，不合成 None，不改 Campaign。布局失败永久禁用本进程确认特性并只记一次可见日志。

## 构建证据（no-deploy）

Wrapper（本工作树）：

- `.local/issue270-worker/build/Issue270Overlay-net8.0.csproj`
- `.local/issue270-worker/build/Issue270Overlay-net6.0.csproj`
- 161 个 `<Reference>` 从 `native-a21/worker/run-source/build/Overlay-net8.0.csproj` 提取；Compile 精确指向本工作树 `il2cpp/*.cs`（276 个）+ 自带 `MyPluginInfo.cs`（共 277 个 Compile）；资源走 `il2cpp/EmbeddedAssets.props`（38 个 EmbeddedResource）。
- `NoDeploy=true`，未传 `BepInExPluginsPath`，无复制到游戏目录的 target。

命令与结果：

```sh
DOT=/Users/Admin/projects/ohmymods/.local/toolchains/dotnet10/dotnet
$DOT build .local/issue270-worker/build/Issue270Overlay-net8.0.csproj -c Release
$DOT build .local/issue270-worker/build/Issue270Overlay-net6.0.csproj -c Release
```

- net8.0：**0 warnings / 0 errors**
- net6.0：**0 warnings / 0 errors**
- 两次均包含 Root 现有 `ExtensionIslandMap.cs` 集成调用，签名匹配：
  `TryArm(MapTimelineMenuGreece, Menu, int, int, object, IntPtr, out object)`，
  `AfterHide(MapTimelineMenuGreece, object)`，`Abort(MapTimelineMenuGreece, object)`，`Clear(MapTimelineMenuGreece)`。

产品 SHA256：

- net8 产品：`40c5db742437bcb144fa1d6711c2c2b4232757d33d2701c3d49b2811aab1c718`
  `.local/issue270-worker/build/out-Issue270Overlay-net8.0/Release/net8.0/KingdomEnhancedMod.dll`
- net6 产品：`ff3d4b4565577314c37c96c9be5aebf42818f5712cc4c7eb6f88414013d87c82`
  `.local/issue270-worker/build/out-Issue270Overlay-net6.0/Release/net6.0/KingdomEnhancedMod.dll`

源码 SHA256：

- `il2cpp/PatchExtensionIsland_MapResult.cs`：`24cf41bf81c3bf68dda35f66c10dba2760f13715f6ea0c47e7acc2260090399e`
- `il2cpp/ExtensionIslandMapResultPlan.cs`：`d3ff41ca5dbdbd6ae08f1b6c636ecf4df07e0de347a63981cccb97a5ab0e230f`
- `tests/mount-island-map-result/Program.cs`：`6ba91ff0d3f4cb6ff1731840109cf302b432e9779b02cdf76f875eab8bc408d2`
- `tests/mount-island-map-result/MapResultTests.csproj`：`4f5356262c74e7c724683de607ffd8d1be35cbd763f623fd77499ff8def16d0f`

## 自动化测试

命令：

```sh
$DOT build tests/mount-island-map-result/MapResultTests.csproj -c Release
DOTNET_ROLL_FORWARD=Major $DOT tests/mount-island-map-result/bin/Release/net8.0/MapResultTests.dll
```

- 测试构建：**0 warnings / 0 errors**
- 运行：**21 passed / 0 failed / 224 assertions**，exit 0。
- 说明：本机只装了 .NET 10 runtime，net8 测试二进制通过 `DOTNET_ROLL_FORWARD=Major` 在 dotnet10 runtime 运行；测试纯 managed，不加载游戏/Unity/IL2CPP。
- 覆盖：A10→11、B11→13、A 重复 false 保持 11（不 A11→B13）、wrong expected UI、None、非规范 hasValue、无票/foreign、menu/iterator/show/campaign/token/arm 失配、新 show 旧迟到、revoke/nativeClear、Hide 早退/异常/同步完成后 Abort、raw 16/16/20 + Return header16 + Nullable class size24 → payload0/0/4、布局 size/offset/type/越界/对齐/引用/boxed 失败、等值写仍为 Committed、byte buffer 只改 4 字节。
- 测试直接编译 `ExtensionIslandMapResultPlan.cs` 与 `Patch_MountIslandSplitPolicy.cs`，不复制决策算法；runtime adapter 的 native getter/class ABI 只做编译与逻辑审查，未在 game 中执行。

## 未完成 / Native 边界

- **未运行游戏、未安装、未 Native 实测**。实际 IL2CPP metadata 的 raw offset/header、hook 安装、Postfix 与 Haglet pop 时序、A10→11 真实出航均未验证。
- 第一轮 Native 必须确认：布局硬门通过（预期 raw 16/16/20 + Return header16 + Nullable class size24）、`MapTimelineMenu._BaseShowRoutine_d__76.MoveNext` detour 安装、A 成功出航；B11→13 目前仅 host 校验，不能宣称实机通过。
- 若实际布局不是 8B/0/0/4 或 boxed header 不同，`EnsureLayout` 会永久禁用确认特性并记一次可见日志；这是 fail-closed，不是已验收。
- Android `OhMyMods.AndroidProbe.csproj` 是 explicit list，不包含 `ExtensionIslandMap`/新 hook；`grep` 未发现相关 pin；本轮未构建/未验证 Android。
- 未证明普通取消路径安全；不把 None/无票称为原生正常 cancel。

## 最小 Operator 集成片段（Root 现有文件已包含；worker 未编辑）

```csharp
// ClearLands postfix 入口（Root 现有）
ExtensionIslandMapResult.Clear(menu);

// Confirm Stage1 Succeed：landResult/audio getters 之后、Hide 之前
Menu menuInst = Menu.InstExists ? Menu.Inst : null;
Menu hide = menuInst != null ? menuInst : menu._menu;
if (hide == null || !ExtensionIslandMapResult.TryArm(
        menu, hide, landIndex, resultLand, slot.WorldToken, slot.CampaignPointer,
        out object confirmationHandle)) return true;
try
{
    if (menuInst != null) menuInst.menuBackoutAllowed = true;
    hide.Hide();
    ExtensionIslandMapResult.AfterHide(menu, confirmationHandle);
}
catch
{
    ExtensionIslandMapResult.Abort(menu, confirmationHandle);
    throw;
}
```
