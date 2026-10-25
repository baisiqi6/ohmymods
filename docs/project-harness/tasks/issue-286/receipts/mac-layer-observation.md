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

## 2026-10-25 离线增量复核（不启动游戏）

- 用户红框已精确对应六个独立小盾；入口与24条A/B小盾快照的shared stencil Comp/Op/Ref均8/0/1、Pow-Diffuse、mask None、layer0且无SortingGroup链。旧“尚缺这三个stencil字段”的说法已过时；把已Always的Comp改Always没有区分力。
- 当前Mac匹配shader blob与Metal fragment按offset表核对；在已采shared keyword未被实际draw改变的条件下，普通候选片元执行_MainTex alpha×vertex alpha的discard。不能泛称透明texel一律写深度，也不能把shader名字或shared material当作该像素的GPU证据。固定ZWrite/ZTest等缓存解析仍是静态线索。
- 发现旧probe读错混合属性：ShaderLab用`DstBlend`（无下划线，默认10），查询`_DstBlend`的not-present不证明混合值为0；`_ZWrite/_ZTest`不存在也不证明固定pass关闭。下一次取证应修正读取名称，不能据此改材质。
- 重新核源PNG：入口192×192、alpha0/255=16865/19999、透明RGB全0且可见纯黑0；小盾19×30、alpha0/255=103/467，未烘入黑弧。只排除这些源像素假设，运行时纹理/MPB仍未知。
- A/B源码仅marker order差异，但A未稳定复现且截图位置/时段不同，B画面正常不足以定因。仍需失败像素同帧有效pass/variant、正确Blend/MPB纹理alpha与depth/stencil前后写入者；不批准猜测order/Z/material或逐帧覆写。默认图层源码未采纳、正常包未更新。
- 详细只读计算、shader/PNG/截图和源码哈希留本机`offline-20261025/layer-audit.md`，原始玩家资料不提交。Issue #285/#286保留开放。

### 关键遮挡者的剩余盲点与平台边界

追加源读发现：旧DescribeMaterial的keywords只覆盖入口/reference/V2四层；six-marker与玩家树MaterialBrief不记keywords/material pointer/多slot，steed.SpriteFX.Renderer还被remember去重，首次NearDone与64节点cap实际截断会漏瞬态/第二slot。不能把它们归到已知普通片元变体；下一次失败帧应窄采six-marker slot0、steed SpriteFX及实际DarkQueen/Puff等遮挡renderer的slot/keywords/MPB与正确DstBlend。

另纠正一份旧1521项地址表的平台归属：它由Windows PE白名单脚本生成，不是Mac完整表，其“不含SpriteRendererFX”不是Mac实现缺失证据。Root已直接用当前Mac v31 metadata核六个SpriteRendererFX方法token（RefreshShared/Instanced/SetInstanced/Flash/Glow），不依赖该表；但native实现地址/实际writer/调用时机仍未绑定。缓存interop的整体生成来源缺回执，此六token的身份匹配不能扩称全缓存绑定或GPU因果。
