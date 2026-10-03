# greek-civilian-height

Greek 世界平民（`Peasant_norselands`）站高 +5% 的针对性回归。共享常量
`WarriorPeasant_OnEnable_Patch.NorseCivilianScaleY` 从 `0.70 × 32/18` 提高到
`0.70 × 1.05 × 32/18`（站高 0.735），三个入口（`Character.Promote` 替换路径、
`WarriorPeasant.OnEnable`、`Peasant_norselands.OnEnable`）必须继续共用同一常量。

覆盖：

- **精确 source extraction**（避免为单个常量镜像一份生产实现）：常量表达式恰好是
  `0.70f * 1.05f * 32f / 18f`；两个文件里 `NorseCivilianScaleY` 的引用数固定
  （Worker 3 = 声明 + 2 使用；Promote 1）；0.70/1.05 只在声明处出现；没有
  `NorseCivilianScaleY *` 之类的二次累乘；Promote 路径无旧值/独立魔数。
- **数值**：`0.70×1.05×32/18` 与 `0.735×32/18` 在 float 舍入内一致且高于旧 0.70 目标。
- **真实 `GreekScaleScope.cs` 行为**：只写 Y（X 朝向符号与 Z 保留）、重复 apply 同一值不累乘、
  非 Greek 作用域不写、回到当前 Greek 后恰好应用一次。

不模拟 Unity 渲染、不启动游戏；实机观感由用户验收。

```sh
dotnet run -c Release --project tests/greek-civilian-height/GreekCivilianHeightTests.csproj
```

源根默认从当前目录/程序目录向上查找 `il2cpp/PatchRoles_Worker.cs`；也可用
`GREEK_CIVILIAN_SOURCE_ROOT=<repo-root>` 覆盖。
