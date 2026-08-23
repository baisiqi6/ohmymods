# Review 请求：crossbowman-021 弩手补丁（行为契约变更，需 reviewer 交叉审核）

## 你是谁

OMP reviewer（只读）。你审查代码与契约的一致性，不修改任何文件、不 commit、不部署。
你的产出：verdict（approve / must-fix / question）+ 逐条理由。must-fix 要给出具体行号与修法。

## 审查对象

- 实现：`il2cpp/PatchRoles_Crossbowman.cs`（唯一新增文件，约 770 行）
- 设计定稿：`docs/project-harness/tasks/crossbowman-021/design.md`
- worker 任务书（实现契约，含 Operator 裁决）：`docs/project-harness/tasks/crossbowman-021/worker-brief.md`
- 参考先例（不必全读，按需对照）：`il2cpp/PatchRoles_Worker.cs`（Promote 挂钩）、
  `il2cpp/PatchWorld_DefenseSpacing.cs`（World 协程宿主）、`il2cpp/PatchRoles_Castle.cs`
  （同步池注册）、`il2cpp/PatchPoolFix.cs`（InitPools 重建）、
  `il2cpp/PatchEconomy_BankAssistants.cs`（控制器解析）
- 游戏源码（2.1.0 参考，只读）：`game-source/Assembly-CSharp-2.1.0/Character.cs`
  （Promote ~504 行）、`Archer.cs`（ActiveArrowAttack/shootRange/IsAvailableForJob/ActivateBuff）、
  `ArrowAttack.cs`、`Arrow.cs`（hitDamage）、`Kingdom.cs`（FetchArchersForJob ~2749）

## 背景

2.4.0 IL2CPP（BepInEx 6 + HarmonyX + Il2CppInterop，.NET 8）。无 2.4.0 反编译，以 2.1.0
源码为逻辑参考 + interop 暴露成员为签名事实。编译已通过（0 error 0 warning）。

弩手 = 每第 4 个捡弓转职的弓箭手被打包成"换皮强化弓箭手"（deadlands 动画 + 射程 12 +
冷却 ×2 + 伤害 ×2 + 平直快弹独立弩矢 + 永不被骑士招募 + 读档每第 4 个重算 25% 守恒）。

## 重点审查清单（按风险排序）

1. **对象池污染路径**：标记/参数残留实例被池复用给普通弓箭手（promote 路径 Strip 兜底、
   读档 15s 窗口、非 promote 的池复用路径）是否全部闭环？有无 Apply 幂等性漏洞
   （间隔 ×2 是否可能叠加）？
2. **与原生 buff 兼容**：火矢 buff（ActivateBuff→_fireArrowAttack）期间巡检不得覆盖；
   buff 过期回 _arrowAttack 后巡检恢复克隆 SO——这个"仅当等于基础值才修"的判据有没有
   误伤面（例如其他原生代码合法地把 ActiveArrowAttack 设为别的值）？
3. **联机**：syncID 分配器（30130 起，跳过 30120-23/30130-31 保留段）在双端 Init 重建后
   是否确定一致？promote 计数双端独立推进的后果是否被设计接受（外观级分歧）？
4. **协程宿主**：World.OnLevelLoaded postfix + per-world 指针守卫；world 切换时旧协程
   退出、新协程启动；RecomputeOnLoad 只跑一次/世界。有没有双协程或永不退出的路径？
5. **骑士排除**：IsAvailableForJob postfix 对 GuardSlot/塔位 jobObject 无 Knight 组件的
   场景必须零影响（弩手可以上塔守城，这是设计）。
6. **弩矢池**：Init 清池后重注册的幂等与孤儿池清理；Pool.Spawn 兜底
   （PatchPoolFix.SpawnGO fallback）存在的前提下，未注册瞬间开火是否安全。
7. 通用：异常吞噬后状态半套的风险；DestroyImmediate 使用点是否安全；
   FindObjectsOfType 调用频率（每 5s 两处全场景扫描）在后期人口的性能是否可接受
   （对照现有 Patch 的同等做法）。

## 明确非目标（不要报）

- 皮肤不进存档、读档重算可能轮换具体个体（设计定稿已接受）
- SO 内部 Range≈36 站桩狙击行为（Operator 有意裁决）
- 联机客户端外观级分歧（已知并接受，权威端判伤害）
- 武士/骑士小队/错峰（后续任务）
