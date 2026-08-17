# Kingdom Enhanced Mod（王国增强 Mod）— IL2CPP 版 V2.0

## 适用版本

- **Steam 正式版** Kingdom Two Crowns: Call of Olympus **2.4.0**（IL2CPP）
- 不支持 GOG Mono 版（那是另一个分发包）

## V2.0 更新速览

V2.0 是第二个正式大版本，重点：

- **银行助手系统**：一名主银行家 + 四名跨世界外观助手；助手连续顺滑收币、
  途经顺手吸收、积压自动增援，金币只记入主国库一次。
- **Cerberus 四支亡灵小队**：4 骑士 + 16 弓箭手；希腊队推进 + 边界驻守 + 60 秒消亡
  （不再冲锋自爆），北境队跟随君主 30 秒消亡；冷却 22.5 秒。
- **特种箭塔重建**：弩箭塔付费重建回六级普通塔，再携目标隐士重新专精（在线暂不开放）。
- **城墙旗帜**：一次带走当前侧全部小船（最多 4 艘，错开停靠）。
- **死亡换君主后自动恢复奥林匹斯小船**（按神像交付次数，最多 4 艘）。
- **忍者多载体伏击、狂战士六次进阶、隐士防绑架、主船扩容、友好巨魔平衡**转为正式能力。
- **修复**：箭塔重建交互不出现、银行助手逐枚卡顿、希腊小队冲锋自杀、航行换岛偶发闪退、
  设置面板日志暴涨、高人口岛屿卡顿等。

完整更新记录见发布包内 `MOD_UPDATE_AND_FIX_LOG_ZH.txt`。

## 安装

1. 把本包解压到**游戏根目录**（包含 `KingdomTwoCrowns.exe` 的文件夹）
2. 启动游戏。首次启动可能只生成 BepInEx interop 缓存，期间会黑屏几十秒
3. 如果首次没有加载插件，请退出游戏并第二次启动；在 `BepInEx\LogOutput.log` 中确认出现
   `Loading [KingdomEnhancedMod 2.0.0]`，且没有相关 `Error` 或 `Exception`

安装前请先备份存档；首次安装或大版本更新时，建议同时备份游戏目录。
从 V1 升级直接覆盖解压即可。

## 修改设置

**游戏内面板（推荐）**：游戏中按 **Ctrl+F10** 或 **F5** 呼出设置面板，可查看并调整无限金币、移速、快速建造、地图大小、怪物倍率和总开关。设置会立即保存，但按各自触发时点生效；地图大小只影响之后生成的地图。

**配置文件**：`BepInEx\config\KingdomEnhancedMod.cfg`（用记事本打开），改完保存、重启游戏生效：

| 设置项 | 默认 | 说明 |
|---|---|---|
| Enabled | true | 总开关 |
| InfiniteMoney | false | 无限金币 |
| SpeedMultiplier | 2 | 君主移动速度倍率（1-5x） |
| FastBuild | false | 快速建造（约 2 秒建成） |
| MapSizeMultiplier | 2 | 地图大小倍率（1-5x） |
| EnemyCountMultiplier | 1 | 每波怪物数量倍率（1-5x） |
| EnemyTimelineSpeed | 1 | 怪物时间线推进速度（1-5x） |

## 卸载

卸载前先备份存档。若 BepInEx 还承载其他 mod，只删除
`BepInEx\plugins\KingdomEnhancedMod\` 和 `BepInEx\config\KingdomEnhancedMod.cfg`；
不要删除共享的 loader/runtime。只有确认没有其他 BepInEx mod 时，才移除根目录 loader 文件和整个 `BepInEx`。

## 注意事项

- 单人 IL2CPP 端到端验证是主要环境；双人分屏与在线联机属于发布后反馈观察项，不承诺已完整覆盖
- 尝试联机/CO-OP 时，双方必须安装完全相同版本的 mod，否则可能不同步
- 在线模式下特种箭塔重建暂不可用
- 测试或卸载前备份共享单文件存档：
  `%USERPROFILE%\AppData\LocalLow\noio\KingdomTwoCrowns\Release\global-v35`
- 游戏更新后 mod 可能失效，请关注更新
