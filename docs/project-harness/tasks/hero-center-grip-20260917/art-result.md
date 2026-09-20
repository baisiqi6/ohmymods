# 英雄自然垂臂、中心握把、弦朝上横持候选

2026-09-17 下午内置素材 worker 有界完成并 freeze。用户最新“横持弓弦朝上”已覆盖首稿的相反方向。仅写 `artifacts/hero-archer/20260917-center-grip/` 与本文，未覆盖生产 PNG、代码、游戏、存档或配置。

候选：`artifacts/hero-archer/20260917-center-grip/HeroArcherAtlas.png`。

- SHA256：`cea4a64924e64dd7195ec6ac3998cda2e48bb426480ada4b8c26af381b6bbf78`。
- 冻结输入 before.png：`43896f9f932ebb9d67b3d3375568e16e4721fd269f954fed83697a787744915b`；完成检查时生产 PNG 仍为同一输入 hash。
- 仅 0..21 站/走/跑：肩 `(35,18-L)`、肘 `(36,21-L)`、手 `(36或37,24-L)`，L 为保留的原生躯干升降。前臂近垂直，步相仅 1px 横摆。
- 采用原金弓完整笔画像素，围绕原中央握把 `(41,20)` 刚体反向90度旋转，再移到手：`(x,y) → (handX-(y-20), handY+(x-41))`。因此握把严格映射至手掌像素，绝不通过改抓弓下段实现。
- 弓弦在中央握把上方，两侧金色弧面朝下，无搭箭或拉弦动作。完整宽19px、握点左右各9px，弓梢轴近水平（原形状一像素高差保留）。完整武器层最低 y25，脚线 y30；未缩放、未剪弓尖。每侧至少22个源弓像素在最终画面仍可见，双端原弓尖颜色与坐标精确保留。

层序按 root 明确补充：横弓以前景方式自然覆盖下腹/腰带/大腿上缘少量像素，使两侧弓臂和中心握把可读。没有重画底层身体或腿姿势；**不宣称最终身体区域每个像素都保持**。每帧核心/原腿掩码中有19像素被前景覆盖。`body-underlay.png` 保留底层躯干/腿原色，`foreground-overlay.png` 为持弓与手臂层，两图 alpha composite 精确重建最终图。`work-masks.json` 的 `body_occluded` 列出每个遮挡坐标、原身体RGBA、源弓坐标/RGBA和最终RGBA；来源均通过刚体逆变换逐项校验。

全部31帧头脸、已缩头轮廓、颈围巾patch、最终下腿/脚底严格保持；底层躯干核心与完整腿脚笔画保持。22与30保留原胸前中位持弓，作为收举衔接，不加插帧；23..29战斗原像素完全保持，因此22..30共9帧均像素精确不变。31槽/384×128/48×32、末格空、PPU32、pivot(31,2)、整体 .9、动态围巾anchors、native Animator采样代码均保持。

验证及交付：

- `python artifacts/hero-archer/20260917-center-grip/build_carry.py` 可复现候选及预览。
- `python artifacts/hero-archer/20260917-center-grip/validate_preview.py`：31/31通过，alpha仅0/255、调色板仅源色、掩码范围、底层身体精确、下腿/脚底精确、手臂连续、手=中央grip、90度完整坐标映射、弦朝上/弧面朝下、左右9px、每侧可见像素、双端弓尖、无裁剪/触地及遮挡来源均验证；`receipt.json` 逐帧记录。
- `preview-relaxed-only.png`：0/10/17，上旧下新，只有非战斗动作；`preview.png` 另含25作战斗不变审核。
- `grip-detail-annotated.png`：最终持弓和独立武器层放大，绿色框为手与中央弓把共点，显示源坐标映射与弦朝上说明。
- `stand-comparison.gif`、`walk-comparison.gif`、`run-comparison.gif`、`prepare-shoot-comparison.gif`、`transition-comparison.gif`；`walk-contact.png`、`run-contact.png`、`transition-contact.png`、`contact-before-after.png`。最终日常预览、握点图、走跑contact与完整过渡contact已目视核验。所有预览固定48×32格原点，不按bbox逐帧重排。

离线预览使用名义6/8fps，不模拟实际native Animator速度和动态围巾网格，不替代实机验收。root负责独立复核、集成、构建与后续安装。worker未启动、安装、提交或发布。
