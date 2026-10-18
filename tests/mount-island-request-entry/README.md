# 坐骑岛首次请求入口与原生结果读取（#260 Worker A）

纯策略/adapter 测试直接链接生产文件：

- `il2cpp/Patch_MountIslandRequestEntry.cs`：入口动作、单槽 tracker、terminal OwnCurrent 读取门、
  native 结果 shape 纯判定（含独立 `il2cpp_type_is_byref` 事实）。
- `il2cpp/Patch_MountIslandFreshEvidence.cs`：既有 gate / advance classifier / route fence。
- `il2cpp/Patch_MountIslandAvailabilityPolicy.cs` / `Patch_MountIslandSplitPolicy.cs`：既有纯依赖。

```sh
DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  .local/toolchains/dotnet/dotnet run --project tests/mount-island-request-entry/Tests.csproj -c Debug
```

退出码 0 且 `checks=165 failures=0`。覆盖 0→1 单次 seal、合法 1→2 复用与单次 publish、同参数
restart/CompletedOnce 新代次、PC2 首见拒绝、stale N 对 N+1 同指针同参数零 reader 调用、foreign/
unreadable/skipped/throw/closed、PC0 失败后 PC1 重建、old-empty 仍拒 fresh、正常 1→2 yield 不读
flags，以及元数据 shape 的尺寸/类型/字段布局/byref 标志拒绝。

## Native reader 元数据 stub（无 Native）

直接编译**实际生产** `il2cpp/Patch_MountIslandNativeResult.cs`，用 host 元数据 stub 运行：

```sh
DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  .local/toolchains/dotnet/dotnet build tests/mount-island-request-entry/NativeResultStub/NativeResultStub.csproj -c Debug
```

每种模式单独进程（reader metadata 整会话 sticky，不能在同一进程切换形状）：

```sh
dll=tests/mount-island-request-entry/NativeResultStub/bin/Debug/net8.0/MountIslandNativeResultStub.dll
for mode in normal return-byref value-byref enum-base-byref enum-base-pointer enum-base-byref-kind; do
  .local/toolchains/dotnet/dotnet exec "$dll" "$mode" || exit 1
done
```

`normal` 必须读取 1 次且 flags=0x410；所有 byref/pointer 形状必须拒绝且 `field_get_value` 调用 0。
这不证明真实 ABI，只证明生产 shape 判定会拒绝 kind=GENERICINST/VALUETYPE 但独立 byref=true 的输入。

## Observer 行为 suite（host；默认当前生产源码）

`ObserverBehavior/ObserverBehavior.csproj` 默认编译 repo 相对实际
`../../../il2cpp/Patch_MountIslandFreshObserver.cs`；可选显式覆盖：
`-p:ObserverSource=<explicit-path>`（冻结旧源码只在对照时传入，不复制进公开 repo）。

公开 portable suite（每种模式独立进程，避免静态 Gate 跨场景串扰；当前生产全部应 exit 0）：

```sh
for mode in stale drift first-seen pc0-pc1 restart owner-drift; do
  DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    .local/toolchains/dotnet/dotnet run \
      --project tests/mount-island-request-entry/ObserverBehavior/ObserverBehavior.csproj \
      -c Debug -- "$mode" || exit 1
done
```

- `stale`：旧 attempt N 对 live N+1 同 routine/request；应在 flags 前拒绝，保留新 current。
- `drift`：own attempt 读 flags 期间新 seal 同形状请求；after-flags stamp 必须丢弃 flags、无 publish。
- `first-seen`：用 reflection 调实际私有 `Prefix`/`Postfix`/`Finalizer`，首见 PC1 seal → 1→2 yield
  保持 Started 且 0 次 flags → PC2 terminal 由正式 reader 提供 0x410 → 单次 pending CompletedOnce。
- `pc0-pc1`：正常 PC0→PC1 只 seal 一次。
- `restart`：同指针同参数第二次 PC1 必须新 generation。
- `owner-drift`：prefix 后 owner 漂移必须在读 flags 前失败，typed/native 读取均为 0。

冻结旧源对照（显式路径；不是公开默认，不依赖固定 `/Users/...`）：

```sh
for mode in stale drift first-seen pc0-pc1 restart owner-drift; do
  DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    .local/toolchains/dotnet/dotnet run \
      --project tests/mount-island-request-entry/ObserverBehavior/ObserverBehavior.csproj \
      -c Debug -p:ObserverSource=<path-to-frozen-old-observer.cs> -- "$mode"
done
```

旧源预期：`stale`/`drift`/`first-seen`/`restart`/`owner-drift` exit 1；`pc0-pc1` exit 0。
`first-seen` 旧源输出 `state=None generation=0 pending=False` 并断言失败，不是编译失败。此驱动只
stub 输入 API，不复制 Observer 决策控制流。

## 真实 interop 表面编译（不运行 Native）

```sh
.local/toolchains/dotnet/dotnet build tests/mount-island-request-entry/InteropCheck/InteropCheck.csproj -c Release
```

引用本机 `/Applications/ohmymods/arm64/BepInEx/{core,interop}` 的 `Il2CppInterop.Runtime.dll`、
`Assembly-CSharp.dll`、`Il2Cppmscorlib.dll`。路径可用显式属性覆写：
`-p:InteropDir=<interop-dir> -p:CoreDir=<core-dir>`；`NativeResultStub` 与 `ObserverBehavior` 完全不
引用这些 DLL。所有 host suite 都不加载 GameAssembly、不执行 Native；host != Native，不证明
`il2cpp_field_get_value` 的运行期复制语义、真实 Haglet 复用或原执行透明性；这些仍需另行批准的隔离
Native 运行。
