# crown-stealer-speed regression

将真实生产文件 `il2cpp/PatchRoles_CrownStealer.cs` 链接进 net8 console stub（Stubs.cs 提供 HarmonyLib / UnityEngine.Vector2 / ModConfig / 插件日志面）的直调回归。

覆盖（Prefix 直调，不模拟 Unity 生命周期）：

- 开启：walkSpeed / runSpeed / jumpSpeed / chargeSpeed / wallJumpForce.x / chargeJumpForce.x 恰好 ×0.75（含正负号；字面量期望值可同时排除复乘 0.5625 类错误）；
- 开启：两个 Vector2 的 y 逐字节保留；attackRange / damage / jumpCooldown / `_mover` 等非目标成员不变；
- 关闭：六个量逐字节不变；
- 关闭状态下已缩放实例不被还原（锁住“无热切换恢复路径、需重进游戏”的既定边界）；
- 空实例调用不抛异常、不写错误日志。

运行（仓库根目录；`-p:BepInExPluginsPath=` 显式置空，避免触发主工程 CopyOutput）：

```sh
C:/Users/ADMIN/dotnet8/dotnet.exe run -c Release -p:BepInExPluginsPath= --project tests/crown-stealer-speed/Regression.csproj
```

限制：stub 只证明托管逻辑与门控；“Awake 每实例一次、池复生只走 OnEnable、实机 Harmony detour 生效、真实池观感”需要实机/日志验证，本套件不声称覆盖。六个成员名与 2.4 interop 的一致性由 operator 的 actual-interop 编译闸门把关。
