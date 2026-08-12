# ohmymods — 操作手册（Runbook）

## 路径约定

| 项 | 路径 |
|---|---|
| 本仓库 | `C:/Users/ADMIN/Projects/ohmymods` |
| 游戏根目录 | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Call.of.Olympus-P2P` |
| 游戏反编译源码（只读参考） | `E:/.../自制mod/Assembly-CSharp/`（1418 文件，28.5 万行，不进本仓库） |
| mod 安装目录 | `E:/.../Mods/MyMod/` |
| UMM | `E:/.../KingdomTwoCrowns_Data/Managed/UnityModManager/` |

## 编译

```bash
cd C:/Users/ADMIN/Projects/ohmymods
build.bat
# 产物：MyMod.dll（编译到仓库根），手动拷贝到 E:/.../Mods/MyMod/MyMod.dll
```

build.bat 内部：Framework 4.7.2 csc.exe + 引用游戏 Managed DLL（UnityEngine*/Assembly-CSharp/
UnityModManager/0Harmony-1.2）+ 全部 Patch_*.cs。

注意：**csc 是 C# 5 编译器**——不能用字符串插值、null 条件运算符等新语法。

## 安装 / 更新

1. 编译 → 拷贝 `MyMod.dll` 到 `E:/.../Mods/MyMod/`。
2. 游戏必须通过 UMM 启动（doorstop_config.ini 指向 UnityModManager.dll）。
3. 启动游戏，UMM 菜单里确认 MyMod 启用（Main.Enabled）。

## 日志

- Unity Player.log：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Player.log`
- mod 日志前缀 `[MyMod]`，探测日志前缀 `[PROBE-SCENE]`。
- 检查 patch 是否挂上：搜 `[MyMod] Patched ...`（Worker.OnEnable / Mover.Update / Kingdom.OnLevelLoaded 等）。

## 常见调参入口

| 参数 | 位置 |
|---|---|
| 北境工匠缩放 1.175 | `Patch_Worker.cs` ApplyWorkerScale + OnEnable_Postfix 登记 |
| 希腊工匠缩放 1.075 | 同上（非北境分支） |
| 北境居民缩放 1.125 | `Patch_WorkerScale` Peasant_OnEnable_Postfix |
| 狂战士缩放 1.2 | `Patch_WorkerScale` WarriorPeasant_OnEnable_Postfix |
| 鹿 0.55 / 小动物 1.8 | `Patch_WorkerScale` Deer/Critter OnEnable |
| 猫生成 | `Patch_Kingdom.SpawnCatsInGreece` |
| 地图扩展倍率 | `Main.mapSizeMultiplier` |

## 规则（踩过的坑）

1. **hook 点必须用 OnEnable**——Worker/Peasant 没有 Start 方法；池复用只触发 OnEnable。
2. **缩放只动 y**——x 是朝向符号，动它会改变移动速度（Mover.cs:405）。
3. **Holder 替换必须配 sync 池**（EnsurePoolForCharacter），否则 Pool.Spawn 崩/联机 desync。
4. **Resources.Load 按名字找不到子目录资源**——必须 LoadAll 扫。
5. 反编译源码是参考，别往里写代码——它不可编译。
