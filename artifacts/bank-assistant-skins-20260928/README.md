# 税收助手新增四套皮肤：用户已选择的概念素材

2026-09-28，用户认可 A/B/C/D 四套，用作税收助手从四名扩展到八名的外观方向。四名原生风格助手保留，新增四种原创外观；原生银行家单独计算。当前只有概念图与设计记录，尚未制作可运行的连续动画，也未扩容或安装。

- A：东方账房 — concepts/a-treasury-clerk.png
- B：沙漠商人 — concepts/b-caravan-treasurer.png
- C：皇家女管家 — concepts/c-royal-steward.png
- D：矮人金库管家 — concepts/d-dwarf-vaultkeeper.png

每张图是同一造型的站立、小跑、数钱三个示意姿势，不是三帧可循环动画；不透明背景、文字标签与放大的展示像素仍需制作阶段处理。请勿直接整图嵌入游戏当图集。

完整设计：[八助手扩展说明](../../docs/project-harness/bank-assistant-eight-skins-20260928.md)。生成方式：内置 image_gen；完整原始提示词见 prompts.json。四张图从侧聊输出逐字节复制，SHA256、尺寸与原生风格参考来源见 manifest.json。原生参考图不是新增原创皮肤，也不作为新增运行资源。

本目录是侧聊受用户明确授权新增的资料，不修改主会话正在维护的生产源码、Git index、受管计划/checklist 或安装版本。正式开工由主会话在既有协作流程中登记和审查。


## 已接入候选

2026-09-28 issue89：原概念图保持归档；animation-candidates四张32帧RGBA原图已逐字节复制为il2cpp/Assets/BankAssistant{Clerk,Caravan,Steward,Vaultkeeper}.png并嵌入候选。production-metadata.json记录最终整数rect、逐帧pivot、PPU与来源SHA，连续动作装配已验证。当前实现和E盘安装已完成，游戏并排站高和动作仍待实测；上文“只有概念/未安装”是最初归档时状态。


## 2026-09-29 当前生产素材

上面的animation-candidates接入记录为旧版本。本次改用用户批准的32像素保形图集：账房来自pixel-style-pilot/trio32/clerk-complete，其他三人来自pixel-style-pilot/identity-motion-v2；trio32中被否决的后三人未采用。production-metadata.json现记录整格rect、固定rig pivot（Y=3，X=16/20）、统一PPU 47.6279069767，以及实测opaque bbox、源SHA和整周期步幅。每人32格、4种动作；运行期走跑相位按速度/步幅推进，时长字段仅供创作预览。候选已安装，实际游戏步态待验；详见docs/project-harness/tasks/issue-89/receipts/pixel-gait-final-20260929/acceptance.md。
