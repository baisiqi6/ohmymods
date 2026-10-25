# Mac 图层现场取样（2026-10-24）

用户已确认：宫廷入口与盾店槽位两问题都发生在Mac；原图白十字为截图组合键Shift误触坐骑技能，普通靠近为主复现。

- 本轮只用 release/v9.5.13@98259af＋私有只读Probe，A DLL `b5161fe4120e5eb8a4450a9863f0e175c8ea097c6211297754331b41c7ac7410`；没有使用PR287身份候选，也没有应用宫廷same-order候选或改任何材质/排序。
- 复用现有ARM64私有QA与可信source8的17文件检查点，宫廷入口x55.87与盾店x31.73分别经过；新case `issue286-render-20261024-01`，PID64515实际OS身份/私有Filer重定向/启动握手通过。
- 4个独立one-shot均取得：入口近处/远处、盾店远处/近处，日志共178行，无采样预算截断。每点有Unity frame/time/横向位置；OS截图另记PID与捕获时间，未称严格同帧，未用跨帧瞬态FX定因。
- 入口与原生ShopHammer参照共用PowerSprite2/Pow-Diffuse，入口order1、参照order0；共享stencil Comp8/Op0/Ref1。Greek店身order0与六个独立小盾order1分别确认；六小盾采样时enabled，共享stencil也为8/0/1。
- 借用sharedMaterial不等于实际MPB/实例材质/GPU pass已知，这些保持unknown；default8不能推出全部片元不受其它深度影响。没有根据读数直接换材质或改排序。
- source8检查点盾卫状态正常：phase=Staged、Unknown=false、KeyReady=true、StageFault=false、QuotaUnknown=false、左右各2/2，EconomyFault=false、无待领Bow。本轮不验证正常已访问岛11缺行、不验证新生成或购买；四名旧兵不是正常档已修的证据。
- 独立可视复核：本轮普通骑行图没有稳定复现用户原图的大圆弧缺片；盾店图显示角色实体覆盖店内图形，不能据此判断透明范围也异常裁切。用户所指具体小盾/柜龛/付款圆点仍待指认。B未执行，不能称A/B对照或修复验收。
- Cua纯Shift被工具输入格式拒绝、未发送；标准shift+Right150ms发送后未确认游戏技能实际触发，未把此项当通过或根因。
- 经真实UI逐项选择Save+Quit退出：controller return0/errors[]、无signal，elapsed783.8秒，OS身份/正常6对象/71物理游戏文件终态检查通过；正常存档/配置/DLL未改，无正常恢复/安装。诊断private后像18文件已归档，单次Native批准已撤销，没有残留游戏进程。

本机原始证据 `.local/tasks/heavy-shield-shop-20261024/mac-layer-observation/` 中的 `native-outcome.json`、`native-render-samples.txt`、`visual-native-review.md`、两张ordinary-near.png和私有后像；不提交玩家原始存档/日志/身份数据。私有Probe/prepare增量独立审查及GLM5.3/max Native窗口审查已留证。准备边界只精确采纳了正常目录新增Finder .DS_Store的6148字节前像，未忽略/删除该文件；Stage后原全量守卫继续执行。

结论：本轮完成Mac图层可复核取样，**未确认遮挡根因、未合并或安装图层修复**。后续先由用户指认本次目标槽位，再对齐同一目标的普通经过/离开画面；不凭好画面关闭#285/#286。
