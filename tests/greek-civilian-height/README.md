# greek-civilian-height

平民身高纠正回归（2026-10-03，跟进 PR #102）。此前 +5% 被加在共享常量
`WarriorPeasant_OnEnable_Patch.NorseCivilianScaleY` 上（`0.70 × 1.05 × 32/18`），
北欧模型跟着一起变高；现恢复常量为 `0.70 × 32/18`（站高 0.70），+5% 只作用于
当前 Greek 世界的普通 `Peasant`：`GreekScaleScope.NativeScale(...).y × 1.05f`，
并排除带 `WarriorPeasant` 组件的改名北欧模型。三个既有北欧入口
（`Character.Promote` 替换路径、`WarriorPeasant.OnEnable`、`Peasant_norselands.OnEnable`）
继续共用同一常量。

覆盖：

- **精确 source extraction**（避免为单个常量镜像一份生产实现）：常量表达式恰好是
  `0.70f * 32f / 18f`；`NorseCivilianScaleY` 的引用数固定（Worker 3 = 声明 + 2 使用；
  Promote 1）；`0.70f` 只出现在声明处；`1.05f` 只出现一次且位于普通居民分支
  （`NativeScale(...).y * 1.05f`）；普通分支用 `GetComponent<WarriorPeasant>() == null`
  排除改名北欧模型；没有 `NorseCivilianScaleY *` 之类的二次累乘；Promote 路径无旧值/独立魔数。
- **数值**：`0.70×32/18 ≈ 1.244444` 且不等于旧的 0.735 站高；普通居民 +5% 是相对乘法
  （1.0 → 1.05、1.2 → 1.26）。
- **真实 `GreekScaleScope.cs` 行为**：只写 Y（X 朝向符号与 Z 保留）、重复 apply 同一值不累乘、
  非 Greek 作用域不写、回到当前 Greek 后恰好应用一次。

两个真实 `OnEnable` 入口、repeated enable、Mover 复位、OFF/非 Greek restore 的行为契约见
`tests/native-scale-timing`。本套件不模拟 Unity 渲染、不启动游戏；实机观感由用户验收。

```sh
dotnet run -c Release --project tests/greek-civilian-height/GreekCivilianHeightTests.csproj
```

源根默认从当前目录/程序目录向上查找 `il2cpp/PatchRoles_Worker.cs`；也可用
`GREEK_CIVILIAN_SOURCE_ROOT=<repo-root>` 覆盖。
