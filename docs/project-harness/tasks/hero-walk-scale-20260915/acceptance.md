# 英雄步态与0.9候选验收

2026-09-15 本机 FAE6FBDD / 7.6.5-hero-walk-scale-20260915：用户实测英雄移动像滑行且要求整体0.9。修正goal=false错误Idle路径，优先原生Animator Speed（暂停为0）、读取失败/非有限才回退指令速度，abs左右均可；ModPanel既有Update直接Sync并沿用frame去重，未认定原LateUpdate失效。自有body/双cloth/肩锚XY绝对0.9，保留flip与-.05/.002置前，不改actor/碰撞/战斗/金箭/素材。缩放失败整组撤销；sprite赋值成功后每VisualState最多16首见帧日志[HeroArcherMotion]。40新检查、13281动画、65生命周期/68布料逻辑/33布料显示/29深度通过，普通与真实2.4构建0W0E、2626旧方法保持/8授权修改+1私有MotionOf签名替换、独立review通过。闭游戏备份758B5990安装，save/config不变，未启动/提交/发布；现场滑行是否完全解决、0.9观感仍待实测，特殊动作/联机仍未完成。

SHA256：`FAE6FBDDCEF13DCE3D84120989087CB2972528097325EABF7FD987BC3BC50674`。

Worker ZCode0.16.5，session sess_b1a9328f-9392-4700-b5b5-87440235e853；公开模型事件GLM-5.3/bigmodel，配置reasoningLevel=max，运行强度无独立回显。仅Read/Edit/Write，测试Operator运行。Operator补充原生Animator Speed优先/失败归还/日志后置，再经独立hero_archer_review最终通过。

40项测试链接真实HeroArcherMotion与HeroArcherAnimation纯逻辑，不是完整Unity视觉胶水回归；完整胶水通过真实2.4编译与只读审查。Mover语义依据2.1参考源码；2.4原生Archer/Norse控制器TOS确认Speed/Prepare名字，实际帧运行与步态仍待现场。保留原有像素素材及已授权英雄优先场景覆盖规则，未新增特殊动作或联机功能。
