# 火枪手职业HUD与阈值补货

用户要求左上角职业显示和自动补货加入火枪手。基线2C7FE54FCC319976E539EF55A464DAFF74AEF08E8CECE2B9473461B7BF6A0B48，保留刚完成的英雄放松持弓和其他修复。

HUD所有原有效世界沿用原开关，新增独立火枪手人数，从普通弓箭手分类剥离，避免同一角色重复计数；单机职业身份有效，联机边界沿用当前限制。补货仍希腊authority金库，新增独立默认off/目标15阈值（沿既有范围），需要火铳铺开关与真实店铺可用。目标覆盖=活火枪手+已买可用枪+已预留订单；普通弓店不能重复统计火枪职业和火枪工具。手动4/自动8，复用税收官队列与最终金库扣款，不能伪造Player已支付状态绕过手动购买凭据。

worker工作日14–18按用户规则内置。HUD worker只PopulationCounts/Hud及测试；经济worker负责补货服务/计数/火枪店适配/API及针对测试。root统一ModConfig/ModPanel配置接线、构建、审查、DLL审计、闭游戏本机安装。禁止自动启动、提交发布、用户存档/配置编辑、运行时替换DLL。

预计契约：补货服务role8=火枪手（0..7旧索引保持），ModConfig.AutoRestockMusketeersEnabled/Target由root增加；HUD内部新MusketeerRole追加，KnightRole独立调整需查消费者。Identity.IsUnit已可按Archer判定；已绑定单位/枪的读取必须同world、alive和Ready/Unresolved明确，未知不当0触发消费。自定义商店只允许显式自动采购入口，付款/生成/失败不重复执行，最终独立复核经济边界。

14:57用户追加纠正英雄日常姿势：斜垂仍像端弓，希望手自然下垂持弓或背弓。Root采用贴身近竖直持弓，斜上仅瞄准；HUD已冻结后同worker新角色仅在artifacts/hero-archer/20260917-vertical-carry生成候选，不修改经济代码。保留前轮缩头、22..30战斗/中位、腿与围巾，弓尖不碰地且不裁短假达标。与本候选一并集成，资源审计只允许HeroArcherAtlas改变。
