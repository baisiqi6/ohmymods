# 英雄地面岗位与骑士式商店旗帜

2026-09-15 英雄驿站地面/旗帜候选 a6b5d90b / build=8.0.0-hero-shop-flags-ground-20260915：已购启用英雄不接箭塔岗位，已在塔位通过原生Exit退出；普通补位、骑士任务和关闭恢复原生保留。用户拒绝文字牌，现改空挂点/原生投币→完整金弓红旗占位→确认死亡破旗，Reserved保留完整旗。旧装饰旗转为真实占位旗，无文字/运行字体；破旗仅会话反馈不影响持久购买。109购买状态/31塔/81效果/31商店回归、完整和实际interop构建0W0E、独立2.4原生/像素/代码review通过；2665旧方法保持/10集成修改、原英雄2PNG保持。未安装/启动/发布，E仍80522bf1正式8.0。跨岛名额范围未答、运输桥和实机未完成；最新候选在operator hero-shop-20260915/candidate-ground-flags。

原生2.4 PayableShield：Empty可付/无旗；Available是付款后待领取盾牌；Taken完整旗展开；Broken破旗可付。set_status RVA0x678190，Pay0x6776E0，MakeAvailable0x676EC0，OnShieldTaken0x6776D0，OnKnightDestroyed0x677320，CanPay0x676B20。当前立即训练不会模拟一个不存在的待领取阶段。

新增塔门的native证据见tests/hero-tower-policy/native-audit.json：IsAvailableForJob/AssignJob/SetGuardSlot/EnterGuardSlot均独址长方法。AssignJob含内联SetGuardSlot写入，不能只钩单个setter。同步ExitGuardSlot补员时仍拒英雄，普通补员保留。

旗帜每0.25秒读一次只读状态，2个renderer、一张80x160图集和20缓存Sprite；0.6秒展开，完整旗轻摆，Reserved静态完整旗。旧文字脚本已禁用，文字草稿仅历史素材，不属于候选资源。

已独立复核旗片顺序、透明度0/255、静态状态、资源清理和状态权威。未实机验证购买、死亡、撤塔或屏幕观感。

候选目录：`C:\Users\ADMIN\Documents\Codex\2026-09-05\ohmymods-operator-2\hero-shop-20260915\candidate-ground-flags`。旧70d53395候选保留为历史。任务仍doing；下一步接收跨岛范围选择并完成运输接续，再安装测试。
