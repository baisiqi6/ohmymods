# Kingdom Enhanced Mod（王国增强 Mod）— IL2CPP 版

## 适用版本

- **Steam 正版** Kingdom Two Crowns: Call of Olympus **2.4.0**（IL2CPP）
- 不支持 GOG Mono 版（那是另一个分发包）

## 安装

1. 把本包解压到**游戏根目录**（包含 `KingdomTwoCrowns.exe` 的文件夹）
2. 启动游戏。首次启动可能只生成 BepInEx interop 缓存，期间会黑屏几十秒
3. 如果首次没有加载插件，请退出游戏并第二次启动；在 `BepInEx\LogOutput.log` 中确认出现
   `KingdomEnhancedMod` 版本/加载信息，且没有相关 `Error` 或 `Exception`

安装前请先备份存档；首次安装或大版本更新时，建议同时备份游戏目录。

## 修改设置

**游戏内面板（推荐）**：游戏中按 **Ctrl+F10** 或 **F5** 呼出设置面板，可查看并调整无限金币、移速、快速建造、地图大小、怪物倍率和总开关。设置会立即保存，但按各自触发时点生效；地图大小只影响之后生成的地图。

**配置文件**：`BepInEx\config\KingdomEnhancedMod.cfg`（用记事本打开），改完保存、重启游戏生效：

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

卸载前先备份存档。若 BepInEx 还承载其他 mod，只删除
`BepInEx\plugins\KingdomEnhancedMod\` 和 `BepInEx\config\KingdomEnhancedMod.cfg`；
不要删除共享的 loader/runtime。只有确认没有其他 BepInEx mod 时，才移除根目录 loader 文件和整个 `BepInEx`。

## 注意事项

- 本次首发主要完成了单人 IL2CPP 端到端验证；双人分屏与在线联机属于发布后反馈观察项，不承诺已完整覆盖
- 尝试联机/CO-OP 时，双方必须安装完全相同版本的 mod，否则可能不同步
- 测试或卸载前备份共享单文件存档：
  `%USERPROFILE%\AppData\LocalLow\noio\KingdomTwoCrowns\Release\global-v35`
- 游戏更新后 mod 可能失效，请关注更新

## 功能一览

跨世界商店/角色通用、希腊世界可通过原生商店招募忍者/狂战士、银行家扫描与移动增强、赫尔墨斯钱袋扩容及 UI 调整、神器权杖控制上限 16 个且不会按原版 5 秒恢复、Artemis 箭 20 次伤害、君主移速/地图/怪物倍率可调、乞丐变北欧平民、草地自动生成等。

## 下一候选版（已构建并部署独立测试副本，尚待实机）

- 工匠每成功完成 6 次普通狂战士工具转职，第 1–5 次生成普通狂战士，第 6 次生成长柄斧队长。
- 阻止怪物新抓取隐士，但不提供伤害免疫，也不主动救回已经被抓住的隐士。
- 补齐希腊忍者的飞镖与烟遁运行支持，修复战斗中断和昼夜状态卡死；
  城墙外每个成熟宽灌木提供左、中、右三个错开的伏击位置，最多供三名忍者分别蹲守。
- 夜行忍者 y=1.1、白天忍者 y=1.0；希腊银行家 y=1.075。
- 修复设置面板引发的大量字体报错，并降低钱包与商店成功提示的重复刷屏。
- 玩家版完整更新记录见 `MOD_UPDATE_AND_FIX_LOG_ZH.txt`。
- 以上改动尚未包含在当前发布 zip；独立副本验证通过后才会重新打包。
