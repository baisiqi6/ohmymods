# 放松携弓与头部微缩候选

已完成候选素材，未覆盖生产 PNG、代码、游戏文件、存档或配置。仅写新 artifact 目录与本报告。

- 候选：`artifacts/hero-archer/20260917-relaxed-carry/HeroArcherAtlas.png`
- SHA256：`5377cf462ffd98796a35fb1e8fc88af66292c31d807d89a86fe8f8be3a9154f6`
- 0..21 站/走/跑采用腰侧斜垂携弓；手腕按步相 0、+1、+1、0、-1、-1 像素微摆，原金弓像素以整数 nearest 旋转及原调色板组成。根手臂自然向下。弓层最终再提 1px，携弓最低可见像素 y28，脚底像素 y29 / 地面边界 y30。
- 22/30 保留原胸前中位持弓（仅缩头），作为休息与原斜上战斗动作间的抬弓/收弓节点。23..29 保留原斜上弓、拉弓和释放像素，只有头部编辑。
- 所有 31 帧从兜帽内部去一列、一行：原宽约 11px、高 9px 的头部变为约 10×8，保持脸像素与颈下边界；原跑步前倾及 native torso lift 0/1/2px 均保留。

`build_carry.py` 是可复现的局部编辑脚本；`validate_preview.py` 导出证据与固定坐标预览。`receipt.json` 包含每帧修改计数、携弓最低点和保护区域结果；`work-masks.json` 保留显式编辑/脚步保护掩码。

验证 31/31 通过：384×128、8×4、48×32、31有效帧/最后一格空、alpha仅0/255、色彩仅来自原图；原生腿部所有笔画掩码像素保持，躯干核心 x30..35/y18..25 保持，完整颈围巾4行patch保持，授权区域外像素保持，23..29头部以外完全保持。PPU32、pivot(31,2)、整体0.9由现有代码继续使用，没有改动。

目视核验了 `preview.png`、`walk-contact.png`、`run-contact.png`、`transition-contact.png`。放大总对照为 `contact-before-after.png`；固定坐标GIF为 `stand-comparison.gif`、`walk-comparison.gif`、`run-comparison.gif`、`prepare-shoot-comparison.gif`，以及完整0→22..29→30→0的 `transition-comparison.gif`。所有预览固定48×32格原点，保留原生躯干升降，不按每帧bbox重排。

预览是离线主体、名义6/8fps对比，不模拟实际native Animator状态速度、0.9运行时采样或双动态围巾网格。实机观感仍由root集成后验收，本报告不声称已实机通过。
