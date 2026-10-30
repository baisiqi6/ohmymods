# #313 盾卫蓝紫格挡晶光 — 源码交付

用户确认概念B“蓝紫折光”。基线 `release/v9.5.13@cb467d4c`，独立分支 `codex/shield-block-gem-fx-313`，[PR #314](https://github.com/baisiqi6/ohmymods/pull/314) 已创建；本轮不合并、不安装、不启动游戏，不修改正常配置/存档。

## 最终行为与写入责任

成功格挡显示一层与原人物完全同形的蓝紫晶光和3个贴身像素亮点，0.28自然秒退去，接收间隔0.45秒、峰值alpha0.62。重复事件不刷新起点或叠亮。精细/粗图集仍为原509帧，不AI重画脸型、装备或动作；所有原alpha、rect、pixel pivot、PPU、脚点和本体变换保持。

既有CopyLook继续独占本体显示状态。新leaf只写自有子renderer/新owned texture，不写Native/Own本体、Material属性或PropertyBlock，不新增Shader/Material/ParticleSystem、伤害hook、场景扫描、RPC或池角色。贴图每identity缓存一次，最多2槽、每槽最多509帧，仅逐出零引用槽；忙时只关本life FX，保留其它正在引用的光效。受击时只置状态，不创建GameObject/纹理。

原通用Pose.Trigger保持原样。`Runtime.TryBlock`唯一成功出口在原Pose调用之后发送一次异常封闭的`NotifySuccessfulBlock`；含自然秒窗口内免费格挡和破盾最后一击。AttachCarrier恢复旧PendingBreak只重播Break姿势，不制造新受击。暂停、serialization、隐藏、Suspend及同life失效均取消残光；恢复不补播。

## 核对与纠偏

- 正确Unity契约：`Sprite.pivot`读取像素值；`Sprite.Create`接受rect内归一化pivot。候选fake原样返回Create入参形成错误互证；修正fake后原候选直接出现pivot/PPU红例。产品按pixelPivot/rect.size创建，glint不以透明fullrect为可见人物尺寸。语义来源：[Sprite.pivot](https://docs.unity3d.com/cn/current/ScriptReference/Sprite-pivot.html)、[Sprite.Create](https://docs.unity3d.com/kr/2022.2/ScriptReference/Sprite.Create.html)。
- 独立只读审计两图全部1018帧alpha bbox，按可见包络1.1倍及3×3半径推导3个安全foot-local位置，再依source PPU量化。按Own.flip镜像、父链继承signed scale/hop，不再另乘worldFacing；新identity在bake时验证alpha包络，未知美术不套用旧坐标。
- 时钟以lastObserved拒绝途中回退，拒绝样本不写lastAccepted；正常新事件仍可恢复。颜色/透明度继承本体，透明时取消，不在恢复后补播。
- 部分启用故障先关闭自有层，normal/force hide均停用自有root，root deactivation成功才结清layer标志；Suspend先关FX再保留原Own setter行为。Remove在Release前Dispose FX，即使原native归还凭据暂留也不保留光效。
- 独立审查首轮2个P2以10项反例复现3红；修正及新事件生产者经独立增量复审APPROVE。相同10项cleanup合同旧3红→新全绿；真实damage/recovery旧21项2红→新23项全绿，另root+renderer双故障5项及312组合8项独立通过。`ActorVisuals.Clear`当前没有生产调用点，关闭责任通过原有Suspend/Remove/driver/life路径落实。

## 实际验证

| 验证 | 结果 |
|---|---|
| PC FX真实Actor/Runtime suite | 158主断言 + 83独立geometry/clock/cleanup合同，通过 |
| ANDROID host变体 | 157主断言 + 83合同，通过；仅原1个PC-only visibility断言按平台排除 |
| 既有相关回归 | runtime1144、integration48、visuals891、leap suite通过 |
| 语义红绿 | 真实fake单位修正后pivot红；最初geometry合同旧候选43红/当前全绿；真实恢复0,true旧姿势误闪1红/当前全绿 |
| 312+313组合host | 完整312 ingress/90 policy与当前FX，8断言通过：首击90→89有光，0.5自然秒第二击仍89且有光 |
| 完整实际interop源码编译 | Mac240/Win243 net6/net8各0W0E；Android0E/1既有unused Banner字段warning；NoDeploy且BepInExPluginsPath空，所有输出独立 |
| 公共源联合构建 | 合并312产品输入与313显示增量的私有Mac net8完整构建0W0E；非正常安装物 |
| 源图集与软件预览 | 两PNG hashes保持，当前生产palette方法映射原始RGBA，全部alpha逐像素一致；生产ValidSparkEnvelope对两张原图均true；同一clock输出101个采样点生成独立browser预览，未当作游戏截图 |

本机证据 `.local/tasks/shield-block-gem-fx-20261030/` 保留worker红绿与实际模型事件、geometry-api-audit几何/签名证据、Root源码纠偏红绿、root-build/root-tests、combined-312与preview。方案/纠偏经GLM5.3/max请求审查，公开事件核实glm-5.3；北京时间周六凌晨worker按协议使用OMP deepseek/deepseek-flash/max。worker报告与host全绿不代替Operator源码和独立审查。

## 交付边界

PC/Android Native视觉尚未验：interop签名、编译、host与软件像素预览不能证明游戏内光照、遮挡、镜头、分配/释放或性能。没有安装/运行游戏/触碰正常DLL或档案，也不拿public验证DLL覆盖现有累计版；后续正常累计安装需要保留312算式/时钟与其它已装内容，并按原规范备份。

无新保存schema或正式发版/tag/单独版本递增。Issue在PR未合并前开放；受管file侧已登记 `shield-block-gem-fx-313`，operation `e1202888-aaa3-4d25-b4d3-d2e4908aa466`、workspace `ohmymods`，旧120项保持。合并部署后沿同operation补record侧，不冒充managed done，不裸改JSON/DB。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.
