# 武士燕返修复候选

2026-09-25 11:09 已安装到 E 盘独立测试副本，未启动游戏。

- 构建戳：`9.14.24-choreo-whitefix-20260925`
- DLL MD5：`8E994A98B65E77EA6368682E4C96C427`
- DLL SHA256：`EC86481FF07E7ECB9D1CC5541500F22357D29F41A4B194608EDFE7FACD18ABA1`

## 本次修复

1. 将燕返的启动资格与行程存续条件分开。行程中的撤退、充能、编队等原生行为标记不再截断两腿或停止伤害；死亡、抓取、失去世界权限等硬失效仍会收尾，保留每腿 1.2 秒与总计 3 秒的保护。没有反写撤退动画。
2. 将残影真正烘焙为白色。读取实际 Sprite 网格和 UV，在小姿态纹理中保留图集 alpha、把 RGB 置白；支持实机打包图集，并避免相邻素材串入、不同姿态串缓存。保留八槽、两秒淡出、脚点和排序。

## 验证

| 套件 | 通过 / 失败 |
|---|---:|
| samurai-motion | 216 / 0 |
| samurai-visuals | 31 / 0 |
| samurai-night-formation | 24 / 0 |
| archer-night-band | 32 / 0 |
| samurai-retreat | 43 / 0 |
| samurai-diagnostics | 9 / 0 |

合计 **355 / 0**；实际 IL2CPP 构建 **0 警告、0 错误**。临时恢复旧资格门、原色 Sprite 和错误缓存键时，相应回归按预期失败，恢复修复后通过。真实武士帧的 1312 个像素还与独立轮廓参考逐一比对通过。

GLM 5.3/max 的运动与最终视觉独立复审均为 **APPROVE**。审查是静态及证据复核，测试由实现代理与 Operator 实跑；没有把这些结果当作游戏画面验收。

## 安装与恢复

安装位置：
`E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll`

旧候选备份：同目录 `KingdomEnhancedMod.dll.20260925-110941.bak`（MD5 `97030311ED804CC2D703C5F5DEDB105A`）。如需回退，应先退出游戏，再用此备份替换 DLL。

安装前后 DLL 哈希回读一致，存档的文件清单、修改时间和内容指纹保持不变。原三份诊断器未改动并随候选保留；Steam 正式目录未写入。

## 下一步实机确认

启动 E 盘测试副本后，先核对日志构建戳，再看夜战武士是否出刀冲出、反斩回到出发点，以及白影是否清楚、脚点是否偏移。日志应有 `whiten-baked`，留意 `whiten-degraded` 和 `[SamuraiDash/choreo]` 异常收尾。

GPU 读回方向、夜间观感、首次烘焙帧耗时、撤退动画与联机尚未实测。残影仍使用游戏当前采样姿态，没有导入历史预制动作图集。

## 开发恢复入口

独立工作树：`C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix`

分支：`win/samurai-choreo-hard-validity`，基线 `67d39b9`。改动未 commit/push。

该树的 `docs/project-harness/tasks/samurai-slash-guard-20260925/` 保存设计审查、实现回执、最终审查、测试日志、源码哈希和安装回执。原交接文档顶部已加入此入口。原主树已有的 progress/checklist 合并冲突未修改。
