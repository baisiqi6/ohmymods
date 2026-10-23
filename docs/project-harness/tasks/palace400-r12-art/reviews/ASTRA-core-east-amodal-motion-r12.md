# Astra Core/East、补全资产与荷塘动作集中离线审查

日期：2026-10-23。Reviewer：用户指定Astra美术专项。

## 分组结论

- **Core 12件：changes_requested。5件需有限清边/柱材质修复，7件通过。**
- **East 12件：changes_requested。9件需局部材质/语义边界修复，3件通过。**
- **新增8件amodal：approved。**
- **4套荷塘动作、合计64时间帧：approved，限离线帧结构/固定锚区及身份保留。**
- **3件pilot最终B128-r2映射：approved。** 复用其主体与清边通过证据，不重新开结构scope。

批准范围不含Native比例、水位、视差率、相机覆盖、碰撞、遮挡排序、当地时钟消费或整幅重组。OMP正在修的西5件不在本报告内。

## 实际看图

实际view_image看了core/east/pilot的1x深底总览、core/east全部24件4x四底板、pilot3件最终4x四底板；新增8件amodal全部1x和4x四底板。荷塘查看4份`*-raw-b128-8x.png`和4份`*-three-backgrounds-1x.png`；另外读取两份GIF的全部8帧，在内存中按时间顺序拼成联系表实际查看，没有新写图。这里不声称经过Native播放。

读core review-notes、相关registry的所有权、moon-court/亭体范围及lotus-motion manifest。另直接比较64份styled帧：每个资产声明的固定patch在其16时间帧中均逐字节相同。数值检查仅辅助，仍以实图判断没有脱茎/破叶。

以下局部坐标均为asset-local左上0的人工观察包络，不能按整矩形清空。处于真实材质内的混色应修RGB，真实环境空隙才改alpha；禁止全图删绿/删蓝或将柱子统一削窄。

## Core：逐件结论

| id | 结论与证据 |
|---|---|
| ceremonial-paifang-columns | **changes_requested**：主牌楼柱旁沿竖向出现背景绿/白/蓝相间条块，尤其local x≈89–96、y≈99–161，以及侧柱x≈58–65、y≈99–140。对应raw自审已点名全图x5414..5418,y118..178一带。不是1px高光，需还原红木柱实宽并剔除柱侧背景；华表本体和石座保留。 |
| ceremonial-side-gatehall | **approved**：主殿、固定侧墙/石灯植栽与基脚完整，未证实的微小金饰混边仅记录，不据此返工。 |
| imperial-book-merchant | **changes_requested**：左金饰local x≈50–59,y≈3–24、右金饰x≈153–162,y≈4–14有明确天蓝包边；局部去天空/去混色，保留金饰高度与瓦缘。自有阳台植物不删除。 |
| imperial-ceramic-workshop | **approved**：主铺、附属后屋/烟囱、自己前墙与货车作为同组可读，前墙遮住自有后屋不当作外部遮挡缺口。 |
| lotus-moon-gate | **changes_requested**：左自有松树下方local x≈10–20,y≈55–81保留明显蓝色竖片，四底不随底色变化，不能当树干；沿真实树干/根石去除后景或补齐被遮本体。圆门孔已透底，墙体/自有松树其余保留。 |
| lotus-octagonal-pavilion | **changes_requested**：左外柱x≈18–24,y≈62–76及右外柱x≈79–85,y≈61–78有蓝青矩形段；顶刹x≈46–50,y≈6–24偏绿条状。重点修两柱材料和柱侧空气，金刹按真实金属ramp做有限处理；保留实际红木承力宽度、栏杆及石基。 |
| lotus-water-gallery | **changes_requested**：柱侧空气开口仍留下灰绿竖条，local x≈11–18、52–59、92–99、150–157，y≈48–77。需逐根区分柱体与后景残留，补真实红木/清实际空气；不是只挖几块大矩形就可宣称微缝已净。屋顶及基台不用重生成。 |
| palace-east-moon-court | **approved**：月墙、后亭、固定盆景/灯与栏杆作为整体成立；本次未看到足以证实后亭檐端被外部邻居切断的缺口，不因低分辨率檐边疑点另行打回。 |
| palace-east-wing-shops | **approved**：连排铺面、固定柜货及石脚完整，外侧1px颜色未形成明确断物。 |
| palace-west-moon-court | **approved**：前墙/盆景遮自有后亭可接受，后亭顶部及左右外形仍可读；不宣称后亭能独立从月墙后取出。 |
| palace-west-wing-court | **approved**：主殿/侧院与自有花树石景闭合，局部金饰边色不扩大成无依据返工。 |
| theatre-front-seats | **approved**：两侧固定座席组及腿脚可读，周边空气透底；前后景排序另验。 |

## East：逐件结论

| id | 结论与最小实际修正 |
|---|---|
| well-quarter-bookshop | **changes_requested**：顶脊中央local x≈69–114,y≈9–12有连续绿色背景条，不是零星1px。只清这一条真实后景/去瓦缘混色，沿青瓦/脊线补材质；保留店前盆栽。 |
| well-quarter-house-shop | **changes_requested**：右外侧local x≈151–157,y≈43–101呈近白竖矩形，与周围灰泥/木柱质感脱节，旁边还留细绿边。已有补墙几何可以保留，但须补同面灰泥色簇、木框接触阴影及合理端面，不以平色条当最终外墙。 |
| well-quarter-draw-well | **approved**：井圈、横梁、绳桶与两立柱结构可读，未见可证明的大面积空气误填；细绿材质不按颜色自动删除。 |
| well-quarter-residential-gate | **changes_requested**：右门柱/墙local x≈57–68,y≈29–73是明显纯浅色补片，与左侧已有石/灰泥质感不同。保留完整几何，有限补面/侧光、灰泥纹理与柱脚衔接。 |
| nursery-trellis-court | **changes_requested**：后屋右斜檐local x≈253–272,y≈10–38是一整块深蓝平涂多边形，邻接屋面却有明确瓦列。作为补全应补同方向瓦列、檐木厚度/阴影，不需重做整座苗圃；自有棚架及盆栽保留。 |
| stonecarver-workyard | **changes_requested**：棚柱local x≈74–80、151–157、191–197，y≈5–67多段出现明显绿/白/蓝背景条，被当细柱保留；需逐柱还原木杆与支脚、清杆侧空气。石材、工作台和篷布完整可保留。 |
| reading-hall-gallery | **changes_requested**：左侧松树上方约local x≈43–51,y≈58–61有孤立红色短片悬在空气，且没有连到任何结构；清除此邻接碎片即可。其余书堂/附廊主体保留，不由这一小项扩大重画。 |
| academy-west-garden-gate | **approved**：完整双柱、门拱与底部铺石成立，门洞透底，当前不因未证实的个别绿色檐像素扩大scope。 |
| academy-main-hall | **approved**：主屋、台阶和固定两侧松岩是完整组合；自有石景对建筑底部遮挡合理。 |
| tower-west-covered-corridor | **changes_requested**：local x≈96–132,y≈66–82的补台基为两大片平色加直线，与左侧真实石台纹理直接断档；同时檐端约x≈1–14 / 128–140,y≈27–35是明显单色补片。保留几何，补同材质石面分层/灰缝与檐瓦纹理。已补整形不等于材质完成。 |
| tower-east-armillary-pavilion | **changes_requested**：local x≈94–108,y≈72–82的右台基是突兀平色灰块，与中央雕石/底面不匹配。仅补完整石面及与已有石基的层次连续，不动浑仪。左细柱下端接台位置可随同复看，但没有据此断言整亭悬空。 |
| shared-sky-stele-terrace | **changes_requested**：台阶最底与左脚local x≈3–101,y≈94–100仍有明显蓝水色边，尤其左脚底端；这是烘在物体上的固定冷蓝条，不是Native水位未知。按真实石材轮廓清掉外部水像素/修石边混色，保留完整最后踏面。 |

以上9件均有界；不要求其余3件再次生产。凡修复内容属于alpha内真实本体，应使用同材质明暗而不是加遮挡条。

## 新增8件amodal：全部通过

- `ferry-storeyard`：原被树断开的屋面现在连续，三组建筑/墙体、根基和固定货物齐全。
- `bridge-west-water-market`：两屋/棚与完整自有大树形成一个whole compound，内部遮挡合理，空气透底，无烘水。
- `bridge-east-water-market`：左小亭、主屋、右布棚与石基完整；列柱/棚间空隙透底。
- `well-quarter-pharmacy`：右墙/附铺和完整柱脚成立；附属物一起移动，不冒称独立。
- `pear-garden-theatre`：左右翼、主台、阶与完整外檐连贯，没有未补完半侧屋。
- `east-rock-pine-endcap`：全树冠、树根与完整岩脚闭合，无水/反射片。
- `lotus-stone-bridge-garden`：白石桥与后廊完整，桥孔及柱间空气透底；桥面/护栏结构可读，不用拉伸补隐藏部分。
- `imperial-garden-rear-pavilion`：完整前墙和自有内院石路/种植正确保留，月门里的固定内院不是应删的Native天空/水背景。

本组色簇和材质与冻结B128-r2相容，细微生成纹理不构成返工。前5个已通过amodal按未变化证据复用，不在此重新判定。

## 荷塘动作：离线通过

四个资产各calm8+wind8时间帧，共64；并非每个资产64独立pose。允许相邻时间帧复用同一姿态。实际raw/B128对照与GIF时序联系表显示：花瓣/花苞与花托、茎保持连接，叶片自由边轻微变化但没有裂帧或脱落，花叶身份及粉/绿明暗色簇保留。

固定patch逐帧比较通过：open `[2,21,24,5]`；bud `[2,20,24,6]`；flat `[8,9,3,3]`；curl `[9,9,3,3]`（cell-local xywh）。这是对实际图像的额外检查，不仅信metadata。可辨变形集中在上部或自由叶缘，没有整张精灵一起漂浮的证据。

批准离线帧和固定锚区，不批准实际Native水接触高度、模式切换、时钟与暂停消费；也不把联系表查看说成已游戏播放。

## Pilot色盘映射：通过

三件最终1x/4x里，已清理轮廓没有被映射重新填回天空块；青瓦/朱木/金饰/灰石可辨，未见新材质断层。复用前次清边与主体通过结论，只关闭本次B128-r2映射增量。

## 实际输入SHA绑定

- `work/styled/core/assets/ceremonial-paifang-columns.png`：`568abacac9cca8564e3d6c371b6fbbdb30a9fdc37806345f21dead455e0bca08`
- `work/styled/core/assets/ceremonial-side-gatehall.png`：`f26f5bbb92bbdefd405e39f6676e08c77fb2d5e73180f6dd17d87b2022987401`
- `work/styled/core/assets/imperial-book-merchant.png`：`67cd8127b6ecc3ec725b3690b22ca6fa16ab67d691a09a1fbea2fe97a382b596`
- `work/styled/core/assets/imperial-ceramic-workshop.png`：`e54892c880adf91f6d21f10aec080fc6d6e7a404264bfea14a2c883e5546a041`
- `work/styled/core/assets/lotus-moon-gate.png`：`41e7d1cee795b8a06cb7b28b0aae1978b59ca894e5aeff7d85cac7262f157f96`
- `work/styled/core/assets/lotus-octagonal-pavilion.png`：`dad308606a02a2ab83850a18d6fbcf2cad00d3cbda60f0fb92fd3267a36423b6`
- `work/styled/core/assets/lotus-water-gallery.png`：`50ca664302f191f69b301299f1036e6b8e6855dfc32e2ef87ca058d70b44d50a`
- `work/styled/core/assets/palace-east-moon-court.png`：`8707b0210f9e028da87f46dc1624f1a975b61fc750cd6e30ea046ac5d670456d`
- `work/styled/core/assets/palace-east-wing-shops.png`：`7aeb66d16a1efff50500e24c7062d02639b531012df8aca628de09053c2bd8bc`
- `work/styled/core/assets/palace-west-moon-court.png`：`faeb7cc6d5b327feacc1f54cfb66efe5006a5d21cadb3c2512b2435fca399e71`
- `work/styled/core/assets/palace-west-wing-court.png`：`50b49ce56d4b1f01a60519de7263f63cec93226c783f655751e4ca6e925206af`
- `work/styled/core/assets/theatre-front-seats.png`：`10ec01206f3ff1ca9d8875c0e38207a8bfcc0c2fcc3e03fbcfea2b5ae4f357d2`
- `work/styled/east/assets/well-quarter-bookshop.png`：`7eb6b6069e1a9838182d5356a6f6ab5acbf084cb297b4ad318e888b27506e1f5`
- `work/styled/east/assets/well-quarter-house-shop.png`：`0c293d3ab1583c80eb26ad696eab7f84255d8e5e6d341cb3e54b159aa478989f`
- `work/styled/east/assets/well-quarter-draw-well.png`：`b96be78b5eb05315d319330c105507b022dbe0e7dcaca6ca208fe9beee3024e9`
- `work/styled/east/assets/well-quarter-residential-gate.png`：`825249a3c5cd3c97417611c147b4e0dbfb209626c9cb124e4f448dc1e54cc64b`
- `work/styled/east/assets/nursery-trellis-court.png`：`dd4764b78f4464666afb650c374e71e366b9e90123df59bed3ce84e55ea22c89`
- `work/styled/east/assets/stonecarver-workyard.png`：`d66777dd930aebc3dc43882e917bf9621a3ba1dff60c2d865880569ee0c535d3`
- `work/styled/east/assets/reading-hall-gallery.png`：`1169cfc9553aa95cb5eaa67131e73625ea17637ba6d74f286451f84c0b0a685c`
- `work/styled/east/assets/academy-west-garden-gate.png`：`652683482a62989690420289ef3eabbf9464c0be92a358869723503a340a6c39`
- `work/styled/east/assets/academy-main-hall.png`：`7dbe3261cb19e945be1601ce9075a417ddcdb528184b96340e647c87feacaf24`
- `work/styled/east/assets/tower-west-covered-corridor.png`：`4a72c844860052d204519553ac0dbf4558f658fbc0864dee1b9a474c4d12e559`
- `work/styled/east/assets/tower-east-armillary-pavilion.png`：`baf9bffa6019fa88160e288e12573660906306bace47f646df5c243b3ea74f32`
- `work/styled/east/assets/shared-sky-stele-terrace.png`：`6e157a60a39f85dc6b64b6db23a10612475601981963ff512eb016546d4a12cd`
- `work/styled/pilot/assets/cargo-granary.png`：`49ba0389c3839d92235d3c789eacbcbbee45e3a7127db1cb781e9aa0cbce9c70`
- `work/styled/pilot/assets/central-palace-gate.png`：`81e76d439b6bd331b77190cbb3a7f96385f8660403955329d0c97a15bd3baf64`
- `work/styled/pilot/assets/tang-tower-original.png`：`df2b11a737d73f578247859ab1c52099242df4c1d1ecaea0bbf9fabeb19957d2`
- `work/amodal/assets/ferry-storeyard.png`：`5c87b6a096fe31dbce731082aa84cf76fa97b8434f817bbaaaacc79ca3376587`
- `work/amodal/assets/bridge-west-water-market.png`：`9cb1dec909f74fe37ef471956127f34dfb05ad5b595586b9a329fc8e7dd55e6a`
- `work/amodal/assets/bridge-east-water-market.png`：`8cb8af5f51035ed1fe1a05fd1a41cdf5790a4f1ecf394e4407bc9b8065c382b9`
- `work/amodal/assets/well-quarter-pharmacy.png`：`5dc2b0801b3b7781a2e0a1abceaf3bb9d5976ce79e1817dc3f08da4a0cd33c24`
- `work/amodal/assets/pear-garden-theatre.png`：`fd8713bbd937261a1aac43b5bd8beaeefd6f4c662bf316a806c306921f80475f`
- `work/amodal/assets/lotus-stone-bridge-garden.png`：`f1d01080045e73768fce3e201d04f376b4fe7c899247fe967672f2070900046b`
- `work/amodal/assets/imperial-garden-rear-pavilion.png`：`7837983194c0168322855284b4d91bea53862cd176edcb1dc81f176c814abebe`
- `work/amodal/assets/east-rock-pine-endcap.png`：`2830b3e5fd5f29054fafe12f6471e20270c85a5251e08618711d162d1c206aea`
- `work/lotus-motion/atlases/lotus-flower-open.png`：`5060b823fa235d1eb66ae03cd210e6c53b624d3e12cad91275a168d57a2cd7c3`
- `work/lotus-motion/atlases/lotus-flower-bud.png`：`0b62194a40b628350f3b1e13e8e74f9c8064635dd65156267a09bf32f9ae5a94`
- `work/lotus-motion/atlases/lotus-leaf-flat.png`：`dc4488736502189208fe9497818f1ba3a7c72017a50b5b045434fcbe99e6051a`
- `work/lotus-motion/atlases/lotus-leaf-curl.png`：`99b17073038a12faf79ceaa2e87eb7989ae3fc36c490beb5dd16237faffb67fc`
- `work/lotus-motion/manifest.proposal.json`：`350e83879d36508648cc3405250fe46243234eff8d70ca8c909c65eae5b57dd7`
- `work/lotus-motion/review/preview-calm.gif`：`bba25fe224b86b33f64cf8e5105282186c4c5dade7535c37c6ac6f734c6c6f27`
- `work/lotus-motion/review/preview-wind.gif`：`7a69d84a0f8eef31d70a435697c1dec549bec8cfbcfeb641db38c23cb0be3952`

本轮唯一写入本报告，未改任何图片、源码、其他报告或游戏，未再派agent。
