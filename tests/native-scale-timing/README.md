# native-scale-timing

平民身高纠正（2026-10-03）的入口行为回归：直接编译真实 `il2cpp/GreekScaleScope.cs` 与
`il2cpp/PatchRoles_Worker.cs`，用托管边界替身（Stubs.cs）驱动两个真实 `OnEnable` postfix
与 `Mover.Update` postfix。六个场景中三个在纠正前必然失败：普通 Greek 居民未 +5%、
共享 Norse 常量被误加 +5%、非单位原生 Y 未做相对 +5%；其余三个是护栏：
带 `WarriorPeasant` 组件的改名模型不得进入普通 +5%、非 Greek 世界与 MOD 关闭时不加高
且作用域退出后还原。

覆盖：

- `Peasant.OnEnable` 普通分支：当前 Greek 世界 `NativeScale(...).y × 1.05`；重复 enable 不叠乘；
  原生 Y 复位后由既有 `Mover.Update` postfix 修复；x（朝向符号）/z 不动。
- `Peasant_norselands` 与 `WarriorPeasant.OnEnable` 两个入口共用 `0.70×32/18` 常量。
- 非单位原生 Y：1.2 → 1.26，显式 restore 回 1.2。
- 非 Greek：进入/离开作用域各只写一次，离开还原原生 Y。

不模拟 Unity 渲染、不启动游戏；实机观感由用户验收。

```sh
dotnet run -c Release --project tests/native-scale-timing/Tests.csproj
```
