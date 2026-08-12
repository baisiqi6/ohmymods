# Kingdom Enhanced Mod（王国增强 Mod）— IL2CPP 版

## 适用版本

- **Steam 正版** Kingdom Two Crowns: Call of Olympus **2.4.0**（IL2CPP）
- 不支持 GOG Mono 版（那是另一个分发包）

## 安装（开箱即用，三步）

1. 把本包解压到**游戏根目录**（包含 `KingdomTwoCrowns.exe` 的文件夹）
2. 启动游戏
3. 首次启动会黑屏几十秒——这是 BepInEx 在生成 interop 缓存，**属正常现象**，之后启动恢复正常速度

## 修改设置

编辑 `BepInEx\config\KingdomEnhancedMod.cfg`（用记事本打开），改完保存、重启游戏生效：

| 设置项 | 默认 | 说明 |
|---|---|---|
| Enabled | true | 总开关 |
| InfiniteMoney | false | 无限金币 |
| SpeedMultiplier | 2 | 君主移动速度倍率（1-5） |
| FastBuild | false | 快速建造（约 2 秒建成） |
| MapSizeMultiplier | 2 | 地图大小倍率（1-5） |
| EnemyCountMultiplier | 1 | 每波怪物数量倍率（1-5） |
| EnemyTimelineSpeed | 1 | 怪物时间线推进速度（1-5） |

## 卸载

删除游戏根目录的 `winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `BepInEx` 文件夹即可。

## 注意事项

- **联机/CO-OP 双方必须装相同 mod**，否则会不同步
- 改经济类设置后建议备份存档：`%USERPROFILE%\AppData\LocalLow\`
- 游戏更新后 mod 可能失效，请关注更新

## 功能一览

跨世界商店/角色通用、希腊世界忍者/狂战士自动生成、银行家全面增强（含跨岛共享存款）、赫尔墨斯钱袋扩容、神器权杖控 16 个且永久、Artemis 箭 20 次伤害、君主移速/地图/怪物倍率可调、乞丐变北欧平民、草地自动生成等。
