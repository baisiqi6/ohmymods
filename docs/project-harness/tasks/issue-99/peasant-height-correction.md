# Issue #99 居民身高纠正（跟进 PR #102）

2026-10-03。PR #102 的 `9171254` 把“当前站高 +5%”加在共享常量
`WarriorPeasant_OnEnable_Patch.NorseCivilianScaleY` 上（`0.70 × 1.05 × 32/18`），
使三个北欧入口（`Character.Promote` 替换路径、`WarriorPeasant.OnEnable`、
`Peasant_norselands.OnEnable`）连同北欧模型一起变高；玩家要求的是当前希腊世界的
普通 `Peasant`。本纠正恢复共享常量，并把 +5% 移到普通居民入口。

## 纠正内容

- 共享常量恢复 `0.70 × 32/18 ≈ 1.244444`；三个既有北欧入口继续共用，不改转职与
  注册路径。
- `Peasant.OnEnable` 既有的 postfix 内新增普通分支：当前 Greek 作用域内按
  `GreekScaleScope.NativeScale(transform).y × 1.05f` 应用，并明确排除带
  `WarriorPeasant` 组件的改名北欧模型。
- 只写 Y：x 朝向符号与 z 保持；重复 enable 不叠乘；原生 Y 复位后由既有
  `Mover.Update` postfix 修复；MOD 关闭或非 Greek 世界不写、并在作用域退出时还原。
- 无新增 Harmony hook（在既有入口内加分支）、无配置/存档/版本改动；PR #102 的
  CoinCourier 视觉/身高与其余修复不受影响。

## 文件清单

| 文件 | 变化 |
|---|---|
| `il2cpp/PatchRoles_Worker.cs` | 常量恢复 0.70×32/18；普通 Peasant 分支 +5%（组件排除改名北欧模型） |
| `tests/greek-civilian-height/` | 契约更新：常量表达式、+5% 分支结构、数值与 scope 语义 |
| `tests/native-scale-timing/` | 新增：两个真实 `OnEnable` 入口与 `Mover.Update` postfix 的 6 场景行为回归 |

## 验证

| 套件 | 结果 |
|---|---|
| `tests/native-scale-timing` | 6 cases / 19 checks ALL PASS；纠正前同套件 5/6 场景失败（普通未 +5、北欧误 +5、非单位原生 Y 未相对 +5 等） |
| `tests/greek-civilian-height` | 23 checks ALL PASS；纠正前 source 契约失败 |
| `tests/greek-scale-scope` | 68 passed / 0 failed |
| `tests/greek-scale-adapters` | 10 passed / 0 failed |

ARM64 实机参考集（161 个 interop reference assemblies）纯构建 0 warning / 0 error；
构建产物经只读 IL 校验：`NorseCivilianScaleY = 1.2444444`、`Peasant_OnEnable_Postfix`
存在且调用 `GreekScaleScope.NativeScale` 并加载 `1.05f`。构建不执行部署。

## 状态

模拟回归与编译证据如上；实机观感与实玩验收由用户完成，不以测试或编译替代实机。
