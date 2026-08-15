# ohmymods — 操作手册（Runbook）

## 路径约定

| 项 | 路径 |
|---|---|
| 本仓库 | `C:/Users/ADMIN/Projects/ohmymods` |
| **IL2CPP 开发环境** | `E:/QQ/QQ下载文件/Kingdom Two Crowns (1)/Kingdom Two Crowns` |
| **IL2CPP 独立测试副本（唯一实测目标）** | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091` |
| **Steam 正式版（禁止写入）** | `D:/Steam/steamapps/common/Kingdom Two Crowns` |
| Mono 自用环境 | `E:/Kingdom Two Crowns/`（GOG 2.1.0 x86） |
| 旧目标游戏（2.0.1 x64，已弃用） | `E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Call.of.Olympus-P2P` |
| 2.1.0 逻辑说明书（只读） | `game-source/Assembly-CSharp-2.1.0/` |
| 共享存档（禁止自动修改） | `%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35` |

> 版本差异记录：2.0.1→2.1.0 仅发现 `Pool.syncID` int→short（Patch_Castle 已加显式转换）。
> 切换目标游戏只需改 build.bat 的 `GAME_DIR`。
| UMM | `E:/.../KingdomTwoCrowns_Data/Managed/UnityModManager/` |

## 编译与验证

### IL2CPP 发布线

```powershell
cd C:/Users/ADMIN/Projects/ohmymods/il2cpp
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug
```

产物为 `il2cpp/bin/Debug/KingdomEnhancedMod.dll`。Debug 配置默认可自动复制到开发环境；
只做编译验证时应覆盖/清空 `BepInExPluginsPath`，部署则只复制到独立测试副本。
日志在 `<测试副本>/BepInEx/LogOutput.log`。首次启动可能只生成 interop；退出后第二次启动再确认插件加载。

### Mono 自用线

```bash
cd C:/Users/ADMIN/Projects/ohmymods
build.bat
# 产物：MyMod.dll（编译到仓库根），手动拷贝到 E:/.../Mods/MyMod/MyMod.dll
```

build.bat 内部：Framework 4.7.2 csc.exe + 引用游戏 Managed DLL（UnityEngine*/Assembly-CSharp/
UnityModManager/0Harmony-1.2）+ 全部 Patch_*.cs。

注意：**csc 是 C# 5 编译器**——不能用字符串插值、null 条件运算符等新语法。

## [Mono-only] 安装 / 更新

1. 编译 → 拷贝 `MyMod.dll` 到 `E:/Kingdom Two Crowns/Mods/MyMod/`。
2. 游戏必须通过 UMM 启动（doorstop 注入，见下方"注入方案"）。
3. 启动游戏，UMM 菜单（Ctrl+F10）确认 MyMod 启用（Main.Enabled）。

## [Mono-only] 注入方案（GOG 2.1.0 x86，2026-08-12 踩坑记录）

```
游戏根/
├── winhttp.dll            ← BepInEx 5.4.23.3 的 winhttp（x86，22016 字节），
│                             从 BepInEx_win_x86_5.4.23.3.zip 提取（E:/mod-dev/ 有备份）
└── doorstop_config.ini    ← 必须用 BepInEx [General] 格式（target_assembly=），
                             不是 UMM 旧 [UnityDoorstop] targetAssembly= 格式！
```

```ini
[General]
enabled = true
target_assembly = E:\Kingdom Two Crowns\KingdomTwoCrowns_Data\Managed\UnityModManager\UnityModManager.dll
```

**为什么不能用 UMM 21.0.32 自带的 winhttp**：它打包的 UnityDoorstop 版本不识别 Unity 2022.3.51f1
（静默放弃，UMM 不加载）。BepInEx 5.4.23.3 的 doorstop 兼容。BepInEx 完整包只需 winhttp.dll +
doorstop_config.ini 两个文件（不需要 BepInEx 目录）；BepInEx 目录仅用于验证 doorstop 是否工作
（LogOutput.log 生成即证明注入成功）。

**验证注入是否成功**：Player.log 开头应有 `[Manager] Reading file ... Info.json` 与全部
`[MyMod] Patched XXX` 日志。

## 日志

- Unity Player.log：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Player.log`
- mod 日志前缀 `[MyMod]`（Patch_Probe 探测日志已随 maint-002 删除）。
- IL2CPP 插件日志应出现 `KingdomEnhancedMod` 版本与 patch 激活信息，且无 Error/Exception。
- 检查 patch 是否挂上：搜 `[MyMod] Patched ...`（Worker.OnEnable / Mover.Update / Kingdom.OnLevelLoaded 等）。

## 常见调参入口

| 参数 | 位置 |
|---|---|
| 北境工匠缩放 1.175 | `Patch_Worker.cs` ApplyWorkerScale + OnEnable_Postfix 登记 |
| 希腊工匠缩放 1.075 | 同上（非北境分支） |
| 北境居民缩放 1.125 | `Patch_WorkerScale` Peasant_OnEnable_Postfix |
| 希腊北境外观居民缩放 1.125 | `Patch_WorkerScale` Peasant/WarriorPeasant OnEnable + `Patch_Character` Promote |
| 鹿 0.55 / 小动物 1.8 | `Patch_WorkerScale` Deer/Critter OnEnable |
| 猫生成 | `Patch_Kingdom.SpawnCatsInGreece` |
| 地图扩展倍率 | `Main.mapSizeMultiplier` |

## 规则（踩过的坑）

1. **本地池复用初始化用 OnEnable**——Worker/Peasant 没有 Start；但 RPC 必须等网络注册完成。
2. **缩放只动 y**——x 是朝向符号，动它会改变移动速度（Mover.cs:405）。
3. **Holder 替换必须配 sync 池**（EnsurePoolForCharacter），否则 Pool.Spawn 崩/联机 desync。
4. **Resources.Load 按名字找不到子目录资源**——必须 LoadAll 扫。
5. 反编译源码是参考，别往里写代码——它不可编译。
6. 实机只在独立测试副本进行；测试前备份共享存档，禁止把 Steam 目录当测试环境。
