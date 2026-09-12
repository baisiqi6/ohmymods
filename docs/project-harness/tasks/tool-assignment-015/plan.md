# tool-assignment-015 — 稀疏工具分配性能优化

## 目标

- 优化“居民远多于待拾取工具”时原生每约3秒运行一次的工具分配尖峰。
- 将高人口、工具稀疏场景从接近居民数平方的矩阵，收敛为以实际可分配工具数为主的小矩阵。
- 完整保留原生资格、距离、危险区、城墙、目标粘性、状态切换成本、一工具一人和认领语义。

## 已核对事实

- 2.1逻辑中，`DroppableRegistrar.ReassignRoutine`每约3秒调用`ReassignClaimers`。
- 原算法用注册居民数作为矩阵一维，即使工具很少，也会在高人口岛构造和求解很大的分配问题。
- 2.4 interop公开了`ReassignClaimers`、`CalculateCarrierScore`及居民/工具注册列表；但原方法源层为private，原生内部调用可能绕过公开包装器。
- `JobAssigner.Compute`还服务农田、工作、捕鱼等系统，禁止为了工具优化而全局patch它。

## 第一阶段：只读运行时探针（已通过）

- Harmony仅观察`DroppableRegistrar.ReassignClaimers`，Prefix/Postfix始终放行原方法，不写目标、不写claim、不替换算法。
- 每个Registrar实例最多记录前4次命中，包含居民数、工具数、相邻调用间隔与原方法耗时；之后零日志、零额外矩阵。
- 探针读取异常只限流记录一次并继续原版，不得改变原异常传播或分配行为。
- 实机日志已连续命中：高人口岛为582个carrier、7～8个droppable；后三次间隔3016/2995毫秒，
  原方法分别耗时10.352/9.678/9.051毫秒。公开wrapper真实命中，允许进入第二阶段。

## 第二阶段：稀疏反向分配（已实现，待实机）

- 只拦工具分配入口，不patch全局`JobAssigner.Compute`、通用Scanner、AI状态机或Animator。
- Disabled时完全走原版；Enabled但非world-authority时不写目标；只处理当前Manager、Kingdom和scene身份一致的Registrar。
- 使用补丁私有独立`JobAssigner`，不复用或改写`Kingdom._jobAssigner`。
- 逐个工具用原生`CalculateCarrierScore(carrierIndex, originalDroppableIndex)`筛出至少有一个分数小于10000的可分配工具；该方法是唯一评分来源。
- 仅当居民数不少于128且可分配工具数不超过居民数四分之一时启用稀疏路径，其他情况同轮原版。
- 将可分配工具作为矩阵行、居民作为列，保留原注册顺序保证确定性；完整求出一工具一人结果后再进入写阶段。
- 写入分两步：先让所有当前目标与期望不同的居民通过自身接口清空目标，再让所有具有期望目标的居民（包括目标未变者）通过自身接口重新断言目标和claim。
- 禁止直接写`friendlyClaimer`或额外调用`TryFriendlyClaim`。计算或应用异常时清理静态上下文并让同轮原版重建。
- 只记录一条原版基线摘要与一条替换摘要，不按3秒持续刷屏。
- 当前实现先以原生评分构建`rawDroppables × carriers`分数缓存，再让私有solver只求解
  `eligibleDroppables × carriers`矩阵；因此评分资格不变，同时避免原生接近`carriers × carriers`的求解规模。

## 安全边界

- 仅IL2CPP 2.4发布线；不修改Mono，不碰Steam正式目录或共享存档。
- 游戏运行时不替换DLL；第一阶段只构建、提交和推送，等待退出后再部署独立测试副本。
- 第一阶段没有行为优化，已由真实日志而不是编译结果证明命中。
- 第二阶段必须重新进行独立静态审查、构建和实机门禁，不能因本计划存在就宣称已优化。
- 一次切岛闪退的Windows WER为`coreclr.dll / 0xc00000fd`（栈溢出），发生在卸载阶段；当时探针已停止且
  日志无探针异常，现有证据不足以归因。第二阶段切岛必须把是否复现作为门禁，复现即回退调查。

## 验证

1. 第一阶段Debug构建、部署与日志命中已通过，原版基线约9～10毫秒/3秒。
2. 第二阶段Debug禁部署构建必须0 warning/0 error，diff-check与harness validator通过。
3. 游戏退出后仅部署独立测试副本；高人口岛应只出现一条`[ToolAssignment] sparse replacement active`摘要，
   且耗时低于原版基线，不出现replacement failure。
4. 第二阶段实机必须覆盖：单工具只分配一人、同一居民不领两件工具、危险/墙外资格不变、既有目标不抖、拾取后claim清理、Mod关闭即时回原版、切岛/失权/联机。

## 退出条件

- 第二阶段的实现、部署与发布仍需完整门禁；当前保持`doing`，不得把静态构建当作运行时通过。
