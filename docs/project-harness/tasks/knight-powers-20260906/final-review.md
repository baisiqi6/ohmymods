四项增量已逐一核对完毕（只读，未跑任何命令）。

# 复核结论：三项修复与一项优化均已正确落地，无剩余具体阻断。

**1. Mover goalMode→Off 残留修复 — 正确。** `PatchRoles_DeadlandsPowers.cs:669-675`：goalMode 变化时，若 `_moveSpeed` 仍精确等于 `__state.MoveSpeed × 1.5`（即 prefix 写入后原生未改动的临时值，对应 Mover.cs:145-151 目标对象销毁路径），恢复为 `__state.MoveSpeed`；若为回调写入的不同值（不相等）则保留不污染。测试 32（Program.cs:32）恰好覆盖两个分支：未写速度→还原 4，回调写 9→保留 9。语义与原生路径吻合。

**2. 动画外部改速 rebase — 正确。** `PatchRoles_DeadlandsPowers.cs:311-320`：`if (o.Boosting && speed != o.BoostedSpeed) o.Boosting = false;` 强制下次触发以当前外部正速重建 `OriginalSpeed/BoostedSpeed`；speed 仍等于 BoostedSpeed 时沿用旧保存值。测试 33（Program.cs:33）验证了 1.25f 外部速度被 boost 到 2.5f、OnDisable 恢复回 1.25f 而非更早的旧原速。原第 2 项发现（陈旧 BoostedSpeed 覆盖）已消除。

**3. RegisterWalletOwner 写表优化 — 符合描述。** `PatchRoles_MedievalNorsePowers.cs:143-145`：仅当反查表中该钱包指针的 owner id 与当前不同才写字典；`knight._originalWallet` 原生读取保留用于检测钱包替换。每帧互操作读仍在（这是检测替换所必需的），字典写已消除。

**4. 测试现状**：Program.cs 现有 16 个用例（原 14 + 新增 32「消失目标不残留缩放」、33「重复触发 rebase 外部正速」），新用例直接针对本轮两项修复，断言分支选取正确。注意 `results.txt` 仍停留在 14/14 的旧一轮结果，运行新用例后应更新。

**残余项**：无新增具体阻断。此前第 3 项（巡检 World 指针复用边缘）按约定保留为运行时观察项，Unity 销毁对象比较已存在。Interop 审计（21 目标可达、176 项无 stub）与当前构建成功为操作员声明，本次未重复核验。

**判定：PASS，可交付。**
