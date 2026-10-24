# Astra 背景结构与B128-r2增量审查

- 日期：2026-10-23
- Reviewer：用户指定 Astra 美术专项。
- **① B128-r2环境色盘：approved，可冻结为本轮环境资产生产基线。**
- **② 12个背景语义alpha/结构：approved，限离线完整compound/tree-cluster资产。**
- 无本轮必须修正的结构或色盘阻断项。不是最终Native覆盖、视差、接地或整包生产完成认证。

## 实际看图与增量范围

实际view_image查看五张板：`three-buildings-two-flowering-r1-r2-1x.png`、`two-flowering-r1-r2-4x.png`、`three-buildings-r1-r2-detail-4x.png`，以及background的`palace-six-clean-to-logical-three-backgrounds.png`、`forest-six-clean-to-logical-three-backgrounds.png`。已读`README-r2.md`、palette-r2字段和background README。

本次只比较r1→r2色盘增量与12个背景完整轮廓。R11布局、已通过的建筑主体结构不重审；三个pilot旧清边问题仍由其专项修复和独立增量验收，本报告不关闭那些问题。背景两张大板仍显示r1颜色，因此其两簇米褐花色仅用于结构观察，花瓣颜色结论以r2专用板为准。

## ① 色盘冻结判断

- `flowering-pine`的上部大花冠及根部小花，从r1米褐/桃灰恢复为明确粉红层次；4x下浅粉高光、中粉及深粉仍形成花簇明暗，未变成单一红块。`nursery-mixed`中间花树同样恢复，左右绿色/蓝绿色树冠保持区分。
- ROI来源坐标已声明：flowering-pine逻辑图[50,6,5,4]与[103,33,6,6]。实际看到的恢复覆盖相应花冠区域；不需要通过文字标签才能辨认粉花。
- 粮仓瓦线、朱柱、米色棚布和货堆、宫门灰砖与木梁、唐塔青瓦/红白构件与金刹，在1x及4x对照中没有形成新的明显色偏或材质混淆。不能因极小差异比例断言所有像素相同，但本轮实看未见需要打回的局部建筑漂移。
- 已读取r2表并核对包含128项；其记录以4个粉色替换4个近似浅天空色，金灰6色保留。冻结依据是实图恢复与建筑稳定性，不是128这个数字。
- **可以采用当前b128r2作为环境生产基线。** 只从未tone的原始二值RGBA走一次指定处理；人物不使用该环境色盘。旧r1不覆盖，后续新材质如暴露新问题再作有界增量，不把本批准解释为任意未来资产都自动合格。

## ② 12个背景离线结构判断

| 对象 | 实图判断 |
|---|---|
| rear-palace-twin-wing-court | 顶部金饰、主檐、两翼与完整前阶/墙基均保留；建筑与自带后树组成一个院落整体，没有触边断檐。 |
| rear-palace-garden-gate | 高低后楼、前门和围墙构成完整组合；前墙根与门阶有承托，墙上圆窗里的固有内容不应误挖为全透孔。 |
| rear-palace-moon-arch-court | 三段门庭、两个月洞和中间院景完整；月洞内是自有庭院内容，属于compound的纵深，保留不透明植物/墙景合理。 |
| rear-palace-main-hall-court | 两端侧墙/后树、正殿屋脊和台阶闭合；未见矩形天空片或面状雾带。 |
| rear-palace-long-gallery | 非对称高楼与长廊完整，长屋脊、檐尖、台阶和墙基没有被格线裁切。 |
| rear-palace-residential-garden | 多层院落、侧树与外墙保持一个完整包络，底部石脚闭合，隐藏后楼没有被声称独立完成。 |
| rear-forest-pine | 上下松冠、斜干、根石与枝间负空间清晰；深底没有整体白色外圈或地面雾片。 |
| rear-forest-willow | 下垂柳条与枝间孔洞透出三种底色，树冠顶和最长侧枝未硬切，树根有支撑。细柳叶的像素疏密变化可接受。 |
| rear-forest-bamboo | 顶部叶尖、侧枝、主竹秆和根石完整，竹秆间空隙不带统一雾底；没有横向截平顶部。 |
| rear-forest-mixed-grove | 交叠双树、地被和根石形成完整簇，颜色/枝干层次可辨；不把被遮挡的单树作为独立可移动子件批准。 |
| rear-forest-flowering-pine | 花树与松树完整相依，根石闭合，枝间孔洞和上下轮廓正常；粉花恢复按r2专用板通过。 |
| rear-forest-nursery-mixed | 三株混合成熟树的整体包络、根部地被和枝间负空间完整；它是混合树簇，不是苗床/幼苗资产。粉花恢复按r2板通过。 |

白、青、黑底的实际观察未见遗留面状绿雾、明显白边、四边触切、悬浮根基或邻格对象残片。12份logical-raw另做alpha集合检查，均为0/255；这只是辅助证据，不取代上述轮廓观察。单个很细的叶尖/亮边不构成本轮返工理由。

这些资产可作为far/mid候选以**完整compound或完整树簇**独立重排。自带小树/后楼、交叠树冠/岩石仍归同组；隐藏部分未补全，不能把它们逐子件拆开移动。README对此限制表述明确，本轮没有发现与图像矛盾的独立性声明。

## 后续边界

本报告允许继续把r2用于这批环境结构。背景目前r1派生成图不因此自动成为r2成图，生产消费应保留相同alpha/尺寸，从logical-raw一次处理并留绑定。最终层间reveal、far/mid实际排序、覆盖所有宽高比、运行时视差率和视觉接地仍需主Operator在Native验证。本轮没有把这些后置未知作为打回理由，也没有声称已通过。

## 实际输入SHA绑定

- `work/style/r2/palette-r2.json`：`f9a2bf767906fff2e7ca30a6de040dc5d7f7a589561b294bd89cd37ff6d87841`
- `work/style/r2/review/three-buildings-two-flowering-r1-r2-1x.png`：`74ac4693d3d6ecdcebc2f962e3d2ae78890891c97defcacb72b94f48e2c968a6`
- `work/style/r2/review/two-flowering-r1-r2-4x.png`：`2fd57966b1b0a8b4360a73039917e88b97d15b17f6523fae7acb063116e9c4f2`
- `work/style/r2/review/three-buildings-r1-r2-detail-4x.png`：`ab7d21aef9d5815ff854a5037403f6820a2abbd3c9b797da48dfe6c477e5da4c`
- `work/background/review/palace-six-clean-to-logical-three-backgrounds.png`：`6d3f31c571f05041e43dcd226b72d75f26fda68fe8f9e44efcd96f206025bbb4`
- `work/background/review/forest-six-clean-to-logical-three-backgrounds.png`：`b1934f416967d40260fe84612f7db5be858b9cc1e912a116a401fbf61db15ff0`
- `work/background/logical-raw/rear-forest-bamboo.png`：`e2871bc1afb7653c6661d857a357d0d46d9ded5ab50af1bbe0c69892f71ad127`
- `work/background/logical-raw/rear-forest-flowering-pine.png`：`c4529bf542be43aa4d6186fc3d222a0b4f21267784fe44fe71682fc902d43fba`
- `work/background/logical-raw/rear-forest-mixed-grove.png`：`fe2c219bef6e1db1cc54ac65aff5bb1a5169a0f3c49c4f0f91dc2d67299485df`
- `work/background/logical-raw/rear-forest-nursery-mixed.png`：`e2a21c8afcdcb2baa8172bdc3cbdcfe3f7d79eb4ae00d1d7909aa57043488dce`
- `work/background/logical-raw/rear-forest-pine.png`：`9820a45607d53f9fbb182664491b0ddf038a6a1f632e8b18a6551fba71373317`
- `work/background/logical-raw/rear-forest-willow.png`：`a87ae2212cce32be41965feb0bf7960cf5830aa1b48395dce17b275970beeb3c`
- `work/background/logical-raw/rear-palace-garden-gate.png`：`f0a280bf6e57e5fbe0ea6221f5445175d941c0094af6c060ceedb0433840331a`
- `work/background/logical-raw/rear-palace-long-gallery.png`：`a2ae84996659d6a37a766e76b787448bfcf9f0c81bb72b978c5a57c56a6e70db`
- `work/background/logical-raw/rear-palace-main-hall-court.png`：`a5d1c2f5e11cc4229790601ed3b5d38875be9c96a633ae86bd9654b6b2430ff9`
- `work/background/logical-raw/rear-palace-moon-arch-court.png`：`9e7757e46339bbbf6e05d8af998fe4e4529f476db6eb1ecfbb39eb7044906a75`
- `work/background/logical-raw/rear-palace-residential-garden.png`：`8adbfdf370ce616c5565c1883778e1776aa358a10e80ad01bc4ffca2a2445511`
- `work/background/logical-raw/rear-palace-twin-wing-court.png`：`97fafbbc547f0c3292a93c181a993ff49a426549a2310d60e2a37e90bf82b49a`

本轮仅新写本报告，未修改图片、palette、脚本、其他报告或游戏，未再派agent。
