# 英雄日常竖拿弓素材候选

2026-09-17 下午内置素材 worker 已完成并 freeze。仅写 `artifacts/hero-archer/20260917-vertical-carry/` 和本文；未覆盖生产 PNG、运行时代码、配置、存档或游戏文件。

候选 `artifacts/hero-archer/20260917-vertical-carry/HeroArcherAtlas.png`，SHA256 `43896f9f932ebb9d67b3d3375568e16e4721fd269f954fed83697a787744915b`。输入生产图及本目录 before.png 均为 `5377cf462ffd98796a35fb1e8fc88af66292c31d807d89a86fe8f8be3a9154f6`。

仅 0..21 站/走/跑帧改变手臂与携弓区域：肩 y18 至手 y24（随原 native torso lift 同步），水平差最多 3px，手臂垂落体侧。金弓保持源完整上下弓梢及 19px 高度，仅整数平移，弓梢轴线约偏离竖直 3°；直弦无拉弦手、无搭箭。静态弓底 y28，抬身帧同步提高，脚线仍 y30。轻摆为 0/+1/0/0/-1/0px；跑步 slot20 为保留前倾兜帽与完整弓尖间隙，用 0 代替 -1px。无通过裁剪下端满足地面间距。

22..30 中位、战斗抬弓、拉弓、放箭与回收原像素逐帧保持。全部 31 帧已缩头、脸、颈围巾 patch、躯干核心和原腿脚笔画保持。原 native 动作采样、整体 .9、PPU32、pivot(31,2) 均未改。

已完成：

- `build_carry.py` 可复现候选及首稿；`validate_preview.py` 31/31 帧像素验证通过，结果 `receipt.json`，显式区域 `work-masks.json`。额外验证原金弓颜色/笔画完整映射、全武器坐标纯平移、上下弓尖保留（仅允许原脚步自然遮挡）、完整武器层底端≤28，无新增颜色，alpha仅0/255，384×128 / 48×32 / 31槽 / 末格空。
- `preview-relaxed-only.png`：仅非战斗 0/10/17，上旧下新，适合向用户说明日常姿势。
- `preview.png`：0/10/17/25 上旧下新，保留战斗不变的审核对照。
- `walk-comparison.gif`、`run-comparison.gif`、`stand-comparison.gif`、`prepare-shoot-comparison.gif`、`transition-comparison.gif`；固定格原点，不以 bbox 重新对齐。
- `walk-contact.png`、`run-contact.png`、`transition-contact.png`、全31帧 `contact-before-after.png`。已目视核验首稿、最终日常三列、走跑 contact 与完整过渡 contact；最终 slot20 弓尖保留修正也已目视核对。

离线主体预览使用名义6/8fps，不模拟原生 Animator 实际状态速度及动态围巾网格，不能替代实机验收。root 负责候选集成与后续构建。worker 未启动、安装、提交或发布。
