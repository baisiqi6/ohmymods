# Knight context follow-up — 2026-09-17

有界内置 worker 完成。用户存档、游戏、配置、安装、提交、发布均未操作；主工程最终构建由 root 统一。本报告补充并覆盖旧 knight-result.md 的 unresolved 运行时遗漏和“未运行测试”状态。

## 改动

- `KnightIdentityRuntime.cs`：成功 Load 的 context 判定持续约束加载后的 TryResolve、PrimeExisting、Save 与 seed flush。未确认历史不会生成 GUID，不调用 Prime 的 legacy 风格计算回调，不迁移风格，不写快照。既有 `PatchRoles_KnightStyle.ApplyKnightStyle` 在 TryResolve=false 时立即返回，实际控制器/缩放设置位于该门之后；本 worker 无须修改该文件。
- Begin 不再写会话 binding；成功的最外层 End 才提交。异常与原生 false 均保留先前 binding。当前 load 新恢复的收据标记 scope，失败时撤销并隔离该 life；之前已有收据不动。嵌套加载的新收据向父 scope 归属，外层失败可撤销。中途存档不提交。
- 唯一新增 Harmony 类型：`KingdomEnhancedMod.KnightIdentityGenerationPatch`，目标 `CampaignSaveData.ApplyToScene`，Prefix/Finalizer。复用 HeroRecruitment 已验证原生事实：前后相同 campaign/island/world/context，且 `isNew && playTimeDays == 0`；失败、world 改变、已游玩、重复调用不建新代。成功生成建立私有随机 epoch 与 kind2 空基线，旧 epoch 原样保留。指针仅作本次调用/重复调用 guard，不进入永久 context 或指纹。不新建 RPC，不写原生存档。
- generation 期间禁止 Load 迁移与分配；成功基线提交后开放正常人口持久化。归档只读/保存失败仍 fail closed。没有上述可靠信号的同上下文失配继续视为 unresolved，绝不凭日期、NetID、位置猜新代。
- `KnightIdentityLoadSeed.cs`：旧 pending 批次也服从 unresolved/加载门；绑定已换 epoch 的批次丢弃，不得把旧批次写回新代。
- `KnightIdentityContext.cs`：任一所属 epoch 存在历史即阻止未匹配人口重新播种；匹配必须同时符合 snapshot.Kind 与该 hash 配方。
- `KnightIdentityArchive.cs`：序列化结果先 Parse 验证可读再写。合法 v1 未知根字段 `contexts` 与 v2 重名、或 v1 已有 256 根字段升级后超限时，保守拒写，主档/备份逐字节保留，不删除未知数据。

## 回归与构建

命令均从 `C:/Users/ADMIN/projects/ohmymods` 使用 `C:/Users/ADMIN/dotnet8/dotnet.exe`；执行 `run --project tests/<directory>/<project>.csproj`。

| 项目 | 结果 |
| --- | --- |
| knight-identity-runtime / KnightIdentityRuntimeTests | 37 cases，0 failed |
| knight-identity-archive / KnightIdentityArchiveTests | 40 cases，0 failed |
| knight-stable-context / KnightStableContextTests | 16 cases，0 failed |
| knight-load-seed / KnightLoadSeedTests | 34 cases，0 failed |
| knight-identity-network / KnightIdentityNetworkTests | 48 cases，0 failed |
| knight-identity-integration / KnightIdentityIntegrationTests | 9 assertions passed |

Runtime 新覆盖：unresolved 后 Prime/TryResolve/Poll/Flush/Save 无新 GUID/style/写入；failed load 旧 binding 保持与本次 receipt 回滚；false load；真正新生成历史保留与重复/失败/world 改变保护；新代保存读回同 GUID/style；原生旧快照回退；未来/损坏归档；残留 seed 批不穿透 unresolved 或替换 epoch。原 22 人无原生 Save 的 seed 回归也通过。

旧测试 stub 的 component.data 曾直接嵌入 JSON 字符串而未转义，导致新 Normalized 指纹无法解析；三份 stub 已修为 JsonSerializer.Serialize 字符串，并补 generation 的真实 API 表面。runtime/load-seed 的编译警告仅既有 stub 未赋值字段，未隐瞒。

真实 2.4 interop 验证：`dotnet.exe build tests/knight-load-seed/interop/InteropCompile.csproj --no-restore` 最终 0 warnings / 0 errors，输出仅测试目录 `bin/Debug/KnightIdentityLoadSeedInterop.dll`。该只引用真实 2.4 程序集、无复制安装目标的测试工程从过时 net6.0 对齐主线 net8.0；首次无 assets 与临时 TargetFramework 命令覆盖失败已通过项目对齐+还原解决。所有新调用均在该构建实际解析，主工程未由本 worker 构建。

## 限制

- 本次是代码/原生 API 面及模拟边界回归；新局、旧局和跨岛实机未执行，不能标记实机验收完成。
- **当前用户旧 v1 附加档如果没有精确全岛 JSON 匹配，旧 GUID/style 不会自动恢复。** 这次保证不再擅自重种旧人口，而不是恢复全部旧类型。v1 的三个时钟已参与旧 hash，不能从 hash 逆推出时钟或原人物对应关系。不得以本回归推断用户旧档可迁移。
- 正常候选 v2 快照仅忽略三个已验证顶层时钟；人物、钱包、建筑等仍参与。未证实的新生成同样保持 unresolved。
- generation 的空基线仅确认新代，实际人口仍由正常 Save/既有 seed 路径持久化；在未保存人口前退出不会凭空恢复本次未落盘的风格。

root 负责 AGENTS/progress/checklist 等公共文档、最终源码冻结构建、DLL diff 审计与下一步实机安排。

## 14:16 reviewer 复核后的补修

Reviewer 发现此前仅已恢复收据记录 load 责任，fresh owner 与成功内层 seed 在外层失败时仍可能逃逸。已补最小父事务归属：

- OnEnable 与首次建立 Entry 都记录当前 load scope，成功内层把 owner 责任转给父层；父层异常/false 时连尚无收据的 fresh owner 一并隔离。之前已有、未被本次初始化的旧 world 身份保留。
- SeedBatch 单独记录 TransactionScopeId。内层成功只转父责任；最外层成功才归零可 Flush，任一祖先失败拒绝其全部后代 pending 批。
- 内层 context binding/claim 延后到最外层成功统一提交；失败不留下子上下文绑定。Load End 加幂等 guard。
- generation 证据现同时冻结 World 与其 active gameLayer.Pointer；仅替换 gameLayer、不换 World 同样拒绝确认。原报告“同 world”的精确含义以这里为准。
- 新增四个单层/嵌套 × exception/false 用例，验证 Prime 回调零调用、TryResolve=false、无新收据、Flush/Save 无字节改动、旧 world receipt/binding 保留；成功嵌套用例新增父提交前后 inner binding 的精确 epoch 断言。generation 用例增加同 World 换 layer 与 inactive layer。
- 最终重跑 runtime **37/0**、load-seed **34/0**，真实 2.4 interop **0 warnings / 0 errors**。此前 archive40/context16/network48/integration9不受这组变更影响；合计175 cases + 9集成断言。未操作游戏/存档/配置/安装。
