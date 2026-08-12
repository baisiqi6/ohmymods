# 历史：最初 DLL 直改 mod（legacy，2024-10）

## 背景

mod 开发的第一阶段：用 dnSpy **直接编辑游戏 DLL**（`自制mod/Assembly-CSharp.dll`，3.6MB，
2024-10-17）改数值，替换游戏 `Managed/Assembly-CSharp.dll` 生效。

**已废弃**：游戏升级（当前 2.0.1）后 DLL 失效；直改 DLL 无法维护/追溯，且 dnSpy 保存会
重写整个模块（与官方 DLL 有 300 万+ 字节差异，全是元数据重排）。现方案为
UnityModManager + Harmony patch（本仓库源码）。

> 原始说明文档：`E:/.../自制mod/王国：两位君主mod（测试中慎下）.docx`（2024-10-16）。

## 修改清单（原版数值改动，全部可查证）

### 通用
| 项 | 修改 |
|---|---|
| 骑士上船 | 最多 10 队 |
| 工匠 | 最多 6 人 |
| 投石手 | 最多 24 人 |
| 农民 | 最多 10 人 |
| 乞丐帐篷刷新 | 90 秒，上限 20 人 |
| 长矛兵攻击距离 | +3 |
| 投石车燃烧弹 | 购买一次给 10 颗弹药 |
| 公民房屋刷新 | 10 秒 |

### 希腊
| 项 | 修改 |
|---|---|
| 神器弓箭 | 数量 50 发 |
| 弓箭地面留存 | 50 秒 |
| 单发弓箭伤害次数 | 20 次 |
| 神器权杖控制数量 | 16 个 |
| 权杖控制时间 | 永久 |

### 北欧
| 项 | 修改 |
|---|---|
| 猫生成数量 | +15 |

## 参考价值

1. **需求档案**：这份清单是用户最初想要的数值调整全集。
2. **迁移状态（2026-08-12 决定）**：数值全部迁移到 Harmony patch 实现（checklist backlog-001~004），
   **DLL 保持现状不动**（用户决定：不好改回去，重复无害——设置型条目重复设同样值无副作用；
   注意"猫生成+15"若实现为加法会叠加，实现时按设置型处理）。
3. **状态对照**（2026-08-12 逐项核实 2.0.1 源码）：
   - **已对齐（无需实现）**：骑士 10 队（Boat.maxKnights=10）、工匠 6 人（Boat.maxWorkers=6）、
     农民 10 人（Boat.maxFarmers=10）、**弓手 24 人**（Boat.cs:70 `RegisterUnitSlots<Archer>(_archerPositions.Length + 20)`，
     若 prefab 有 4 个弓手位则原生就是 24）、乞丐上限 20（BeggarCamp.maxBeggars=20）、
     长矛 +3（Pikeman.cs:287 判定已含 `+3f`）、燃烧弹 10 颗（Catapult.cs:132 `queuedOilBarrels += 10`）、
     弓 50 发（ArtemisBow._arrowsToFire=50）、留地 50 秒（ArtemisArrow._maxTTL=50f）、
     房屋 10 秒（CitizenHousePayable._cooldownOfSpawning=10f）、猫 +15（core-009 已覆盖）。
   - **已实现（2026-08-12，Patch_BeggarCamp.cs / Patch_Artemis.cs / Patch_HermesStaff.cs）**：
     - 乞丐刷新 90 秒：`spawnInterval` 设 209f，保留官方 `-119f` 公式（209-119=90 秒）；
       直接设 90 会得到负等待（立即刷怪风暴），已避开。
     - 单发弓伤害 20 次：`_maxHitsPerArrow` 设 0f → 上限 0+20=恰好 20 次
       （原版 2+20=22；设 20 会变 40，已避开）。
     - 权杖控 16 个：`_maximumConvertedTrolls` 8→16（有效上限 16+8=24）。
     - **控制永久：2.0.1 原生已永久**（`FriendlyTroll.ShouldRevertToTroll()` 反编译源码即
       `return false`，`_expirationTime` 只赋值从未读取），不实现。
4. **关键源码位置**（已定位）：
   - 权杖控制：`HermesStaff._maximumConvertedTrolls = 8`（控 16 = 改 16）；
     `FriendlyTroll._duration` + `ShouldRevertToTroll()`（控制永久 = 不 revert）。
   - 房屋刷新：`CitizenHousePayable._cooldownOfSpawning = 10f`。
5. **决策记录**：DLL 直改方案失败的原因（升级失效/不可维护）是选择 Harmony 的论据之一。
   当前游戏 `Managed/Assembly-CSharp.dll`（md5 c69d7a4f）与修改版（md5 2eceabf）完全不同——
   游戏当前未在运行修改版数值，迁移到 Harmony 是让数值生效的唯一可控途径。

## 相关路径

- 修改版 DLL：`E:/.../自制mod/Assembly-CSharp.dll`（保留原处，不入 git）
- 修改版 DLL 的反编译源码：`E:/.../自制mod/Assembly-CSharp/`（无注释版，与顶层版同逻辑，
  但 RVA 元数据来自 dnSpy 重写后的 DLL——两种源码均已被本项目 `game-source/` 带注释版覆盖）
