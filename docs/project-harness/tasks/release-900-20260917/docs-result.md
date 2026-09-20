# 9.0.0 玩家文档 worker 交付

五份玩家文档已完成并冻结：

- `release-notes-il2cpp.md`
- `release/MOD_USER_GUIDE_ZH.txt`
- `release/MOD_UPDATE_AND_FIX_LOG_ZH.txt`
- `release/MOD_CAPABILITIES_AND_ROADMAP_ZH.txt`
- 新 `release/MOD_V9.0.0版本更新说明.txt`

说明统一版本/build=9.0.0、标签v9.0.0、完整包名`KingdomEnhancedMod_v9.0.0_IL2CPP.zip`。Markdown release notes 与新独立TXT正文一致，仅标题格式不同。所有旧独立`MOD_V*版本更新说明.txt`未写入；更新汇总中的旧8.0正文完整放入明确标注的历史段，不当作当前规则。

核对来源：当前ModConfig/ModPanel、HeroArcherRuntime/Combat/Movement、MusketeerRuntime/Combat/Shop、火枪HUD/自动补货交付、税收助手TRIP_TARGET与断流等待、英雄保存/商店/最新持弓图集记录、骑士context/epoch修订、保存静态核对、人口诊断记录。未写生产代码、版本、git、游戏或用户数据。

当前说明覆盖：英雄8币驿站、当前岛每侧1席占至确认死亡、不再开关自动选角、不上塔、移速/射速1.5与射程2、对敌3箭及范围去重、最新弦上横持；火枪手动4币与地面伤害2/射程1.5/慢装填/两侧守位/仅单机；独立火枪HUD与第9项自动补货、希腊单机8币/默认off15/1..200、活人+可用枪+订单；每趟20枚/4.2秒续收；骑士失配历史不重种；Nullable掉落旧拦截撤除；人口日志不冒充修复。

限制在五份当前文档中均明确：英雄/火枪仅单机，英雄走跑阈值抖动、乞丐穿地、弩手缩放未解决；原生存档/附加记录精确匹配、纯原版重存和历史未绑定风险、跨岛未完整验证；火枪本会话损失证明不持久化，重载unbound可暂停补货；18名已保存身份静态核对不等于退出重载实测。保留已授权作者像素火焰及半径.25/总宽.5、普通Greed受击宽.375高.65625参照。

检查结果：五份UTF-8文件均无替换字符/零字节，版本与必要数值、单机和已知问题文字存在；当前段未残留英雄射程1.25或免费自动选择、仅8项补货说法。全文件命中的1.25仅为保留的农舍猫比例及明确历史段；旧八项的表述明确接“合计9项”。隐私检查未发现私人绝对路径、账号标识、文件hash或内部worker/policy说明。公开文本仅保留玩家通用安装目录模板。

冻结SHA256：

| 文件 | SHA256 |
|---|---|
| release-notes-il2cpp.md | E3A13E1A7AAD4BDFAD82D62BF5797A14DD8C01CB66A69D956963A5BB692A71E9 |
| release/MOD_USER_GUIDE_ZH.txt | FBDBF91B059D7663CCB36AF5EF7D43A07FC3A59FD7B78C82E4455CB315D2CD99 |
| release/MOD_UPDATE_AND_FIX_LOG_ZH.txt | C69FC4984CB6223354E0320827DAD94326EE14BA96CBEEA408212BC745444785 |
| release/MOD_CAPABILITIES_AND_ROADMAP_ZH.txt | 3AABAAC805835A4549F4C08FD1A3871B6E5D624B2373BFAB8FD44EADABD49ED7 |
| release/MOD_V9.0.0版本更新说明.txt | 15298EA874C866F323537119B23DDFDDBCC2B6A2B5A07879FFDC9BDF35C42895 |

发布状态、完整构建/包内容与远端资产检查由root另行完成；文档冻结不等于已经发布或全部玩法实测完成。
