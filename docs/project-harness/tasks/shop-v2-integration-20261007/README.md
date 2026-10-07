# 三家商店V2外观与职业idle

关联Issue #176/#177/#180。用户认可游戏尺寸静态稿并要求补休闲动作后一起实装；不是新增购买玩法。代码基线release/v9.5.13 a8eed4c3，分支codex/shops-v2-idle-20261007。

三店店体、商人、槽位分别分层，商人各8帧小幅职业idle（手/弓、锤臂、枪管/握姿往返，含长休息），脸和支撑脚固定；不是完整锻造或擦枪工序。英雄128x80/盾卫128x72/火枪176x80 @32PPU，人物22/24/22px。Root点在图片顶坐标(64,78)/(64,70)/(64,78)，Unity底pivot为(64/W,2/H)，足边world y0有独立回归。

只用现有创建/Tick/Clear：英雄左右真实名额投影为2木框；希腊盾卫4石龛区分base/extra与claims，Readyfalse或Unknown不画空位，单件待领库存独立；火枪三槽沿用原真实枪对象和挂点，不把库存烘进店体。非Greek盾店旧路径保留；Greek画布4世界单位宽、选址和主排他halfWidth2。菜单/事务未清保持原绑定，成功清理后释放自有子层。旧Hero/Musket四帧整图writer与旧banner Tick退出，不改钱包、身份、持久化、战斗或补货算法。

三个新embedded atlas：ShopV2Hero.png(512x400,20cells)、ShopV2Shield.png(512x504,26cells+2padding)、ShopV2Musket.png(704x240,10cells+2padding)。八merchant cells2..9，rear0/front1。Hero双侧各五状态；Shield四龛各available/locked/occupied/unknown。所有available cells透明，其他语义状态非空。不可变cache，Sprite Point/Clamp/FullRect；native共享material/layer/order，子层localZ递进。新增decl在共同EmbeddedAssets.props，Mac wrapper/官方项目同输入。

生产PNG SHA256：

- Hero b38d89080c6b694c518b4936d8f0ee84f18d34c45b078a80042cd818d48d0831
- Shield 0b35bf2bca4252bd87b6c5000bb7cbd061a6f5ab2d22c5b90e44468aa9702a08
- Musket 3897c27934fb08dd63163ba05717a13bb473f0d10c743b71c9b488ee263ede27

验证：新纯政策/足边579断言通过，既有Hero67、Musket45、盾店真实付款/适配245通过。盾店stub只借位/时序，不证明像素；原始PNG由独立direct检查证明bodycopy、头脚固定、motion、state盒/透明/padding，代码review另独立44资源检查。真实ARM64 interop全量0W0E。原生Greek shop使用Pow-Diffuse/PowerSprite2，Transparent队列但zWrite=1/LEqual，支持同sortingOrder内localZ职责；实际player/FX/相机遮挡未由源码证明。

两个产前缺陷已按来源修正：worker把图片顶rootY当Unity底pivot，改(H-rootY)/H并补真实足边回归；动作manifest的保护框值为x0,x1,y0,y1，字段改rect_xx_yy避免后续读成xyxy。计数直接取quota，不再加ShopOccupied；外观只读不反写。

安装由主会话唯一normal owner从合并tip累积构建并保留已批准Palace入口输入；不包含私有整景/诊断。代码/资源审查完成不等于已安装或玩家艺术接受，换岛/读档/菜单/库存支付与实际比例遮挡另记实测。当前只完成PR候选，未装机。独立原始证据在本机同名.local/tasks任务。

版本：本次为已有3商店美术/表现整组优化，候选不另发正式标签、不更改版本常量；正式发布时按VERSIONING相对最近公开包只计一次，不按动画内部返修累计。

