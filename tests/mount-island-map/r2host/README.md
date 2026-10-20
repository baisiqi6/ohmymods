# r2host — 真实生产方法体的动态 fault-injection host

执行 `il2cpp/ExtensionIslandMap.cs` 的**真实方法体**（非镜像）：`HostedMethods.cs`
由抽取脚本逐字生成，运行时对产品文件再抽取同签名做 sha256 比对（hashlock），
不一致即全部 FAIL（零执行证据）。

## 再生成（产品方法变更后必须重跑，repo 根）

```
python3 tests/mount-island-map/r2host/extract-hosted.py
```

当前锁定 15 个方法：SlotIndexOf / SameMenu / MenuId / CaptureLookupPriors /
AttachLookupLedger / LookupDictStillCurrent / LookupCommitGuard /
CanPoisonAppendAttempt / RemoveTombstoneForPendingGeneration / RemoveTombstone /
RestoreReplacedArraysIfStillOurs / WriteLookup / FixLookupOnDict /
DegradeLookupForRevoke / ReleaseLookup。

## 运行（输出目录隔离时）

```
dotnet build tests/mount-island-map/r2host -p:BaseOutputPath=<out>/bin/ -p:BaseIntermediateOutputPath=<out>/obj/
KEM_SOURCE_ROOT=<worktree 根> KEM_HOSTED_ROOT=<worktree 根> <out>/bin/Debug/net8.0/MountIslandMapR2Host
```

- `KEM_SOURCE_ROOT`：产品 `il2cpp/ExtensionIslandMap.cs` 所在根（默认向上搜索）。
- `KEM_HOSTED_ROOT`：本目录（拷贝区）所在根，默认同 KEM_SOURCE_ROOT；反向验证时
  两者可指向不同源以证明 hashlock 拒绝旧产品。
- 任何 bin/obj 不提交（输出到 slice 目录）。
