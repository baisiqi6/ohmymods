# Astra 最终局部抛光与离线组合审查

日期：2026-10-23。Reviewer：用户指定Astra美术专项。

## 分项结论

1. **Core5/East9，合计14项有限修正：approved，全部关闭。** 原已通过资产证据继续复用。
2. **当前完整组合：changes_requested，仅一个重复出现的布局阻断——部分远景宫阙台基悬在天空。** 不要求重画已通过的单体资产，不扩大为Native参数验收。

## 实际看图与输入

已实际view_image看`review/polish-fourteen-overview-1x.png`、polish-core五件4x四底板、polish-east九件4x四底板；组合的11区总板、district-01至11逐区1x、400u-offline-context全卷和四张solo层图。solo整幅缩览用于确认层非空，几何判断来自逐区实际1x。

读当前layout与scope，核对D224街线、预览262水线和分层角色；现场计算14成图SHA并逐个匹配registry。没有修改`offlineReviewApproved`或任何registry。

## 14项关闭记录

| 对象 | 修后实际观察 |
|---|---|
| ceremonial-paifang-columns | 真实朱柱已恢复到其承力位置并接石脚，旧柱旁绿白蓝条块清掉，未把错误邻接列涂成替代柱。 |
| imperial-book-merchant | 双金饰天空蓝包边已变为自有金属边，瓦脊衔接完整。 |
| lotus-moon-gate | 左松干原蓝竖片已替为连续树干，枝、根和门体空间仍成立。 |
| lotus-octagonal-pavilion | 外柱恢复红木材质、柱侧空气透底，顶刹恢复金属色，无削坏亭体。 |
| lotus-water-gallery | 原灰绿条带消除，第三真柱面已恢复，梁柱/低栏/台基连接仍可读。 |
| well-quarter-bookshop | 脊线长绿色带已清除，青瓦脊线连续。 |
| well-quarter-house-shop | 右端原平白条已有灰泥明暗及端面/柱脚衔接，完成度与同体接近。 |
| well-quarter-residential-gate | 右补墙面已有受控材质色簇与立体边，非原纯白占位条。 |
| nursery-trellis-court | 右斜檐补出同方向瓦列与檐端，原纯深蓝大块消失，棚架/花木未破坏。 |
| stonecarver-workyard | 指定棚柱变为完整木杆，两端与篷布/地面关系可读，旧背景条块未保留。 |
| reading-hall-gallery | 左树上方孤立红短片删净，不再悬空。 |
| tower-west-covered-corridor | 右台基补有真实石材/灰缝层次，檐端纹理与主瓦衔接，原平块断层消除。 |
| tower-east-armillary-pavilion | 右台基改为与中央石基相容的结构纹理，整体承托可读。 |
| shared-sky-stele-terrace | 最底和左脚原蓝水边已处理为石本体边界，踏面/侧石仍完整。 |

上述均限上轮指出的包络；不因其它未改像素的零星边色重新打开整件结构审查。14件可被采入资产注册，但不能由此推出当前组合已通过。

## 当前组合唯一阻断：远景宫阙台基在天空中露空

这不是Native地高未知造成的判断空缺：在当前明确的离线预览里，部分本应落在城后地形/树后的完整宫阙，**整条水平石台基清楚暴露，台基下仍直接是天空色**。它们没有东端浮山的悬岩承托，视觉上成为新的浮空宫殿，破坏“都城为主、天庭仅东端点缀”的布局语义。

可直接复核的代表位置（X为设计全卷逻辑像素，Y为当前预览像素，均为近似观察包络）：

| 位置 | 当前证据 |
|---|---|
| 第2区左侧，X≈1365–1460、Y≈114–188 | `far-palace-4`，`rear-palace-long-gallery`，supportX=1420；台基约Y158，下面到低墙上方仍有可见天空空隙。 |
| 第4区客栈左侧，X≈3635–3730、Y≈112–205 | `far-palace-10`，`rear-palace-long-gallery`，supportX=3700；在树间露出整块水平台基，下面悬空尤其明显。 |
| 第10区中部、石作棚左上，X≈10875–10970、Y≈114–183 | `far-palace-29`，`rear-palace-residential-garden`，supportX=10920；小院楼基在棚/树之间露出，下面是连续天空。 |

布局中这些far实例共用`supportOffsetYUnits=2.0625`，按D224计算台基约在Y158；图像证实不能靠统一抬高完整宫阙就形成可信远景。这三个是同类问题的代表，不只修这三个点后忽略其它露底实例。

**最小修正**：只调整有露底的远景实例位置/遮挡，使石台基落在可信的后方地形、庭墙或树冠遮挡下；可以局部降低、横移到现有前/中景遮挡后，或补同组可信承托。完整宫阙资产PNG不改，不拉长楼体，不用透明渐隐抹台基，也不为了遮错位铺一条无语义色带。调整后仍保留天际线屋顶层次即可，不必强求每个远景整院都露全。

下一次只需看修正后的相关区段/完整总览与新layout/合卷SHA。未来Native相对视差是否再次揭露这些台基属于后续验证，不能在本次离线图上先保留明显悬空。

## 组合中已成立的部分

- **完整性与密度**：11区具有渡口、仓货、茶食、客栈驿递、仪道、宫门、梨园荷苑、虹桥水市、居民、花木石作、书院唐塔的不同内容。局部留白可作为步行/分区间隔，不要求用更多重复物填满。
- **主轴与地标**：宫门仍处x6420/world0的视觉中心，三门体量明确；三层唐塔保留，东端浮山完整露出且只在末端作为点景。木桥和白石桥各自身份明确，没有主地标复制。
- **道路与岸台**：逐区当前前沿石岸连贯，未见上次那种直接跳高的拼缝；桥前主步道继续存在。未将诊断水面当生产纹理。
- **两端**：西松柳岩体、东松岩及基础完整；岩体覆盖岸墙端口，未看到岸墙从岩体前方硬穿出的明显断面。
- **船与岸**：两船当前在岸墙前显示完整船体，不再被岸墙切掉下半船；仅是当前预览层序成立，不是Native浮力/水位验收。
- **人物与舞台**：第6区入口人物和第7区舞台人物可见，演员脚落在当前画出的街/台面附近，舞台前座席仍留出通路空间。没有用这些静态图证明成年BODY比例、动作脚底、穿行遮挡或碰撞已验。
- **背景层**：far-palace、rear-courtyard和mid-forest三层均非空，solo图可证；问题是部分far实例的承托/遮挡关系，不是缺素材。无需重做已经通过的13个背景单体。

## 实际SHA绑定

- `work/polish-core/assets/ceremonial-paifang-columns.png`：`7b3b16b50797204bfb80b652a24c116444d534f60a1a2569f553f7506e3b91fc`
- `work/polish-core/assets/imperial-book-merchant.png`：`95160878a899f6cdadfb955e124417d4bededd4e5f54428d5d53debd226ca211`
- `work/polish-core/assets/lotus-moon-gate.png`：`8cd2e39cc0469a6cfb25e71e2ea8d29d06c79dbd5d411d57ef7f410fca686bf3`
- `work/polish-core/assets/lotus-octagonal-pavilion.png`：`2521f916170a1f67f5a4007bdbf2f9b8f10aa238ed0856f1cb2e9a4c5d3f32d5`
- `work/polish-core/assets/lotus-water-gallery.png`：`1631bfb46ae4372cf9a68623d4ad199ae072e43a0092fc2c41527791f609327e`
- `work/polish-east/assets/well-quarter-bookshop.png`：`ff3220f211693342d22b341680e44e142031d1d23f993c3b2b889a50a7797150`
- `work/polish-east/assets/well-quarter-house-shop.png`：`a8513a94a0fdad526d8eb7a23c4e238d9118c4d8d148ed0c32df477dba5946c7`
- `work/polish-east/assets/well-quarter-residential-gate.png`：`cd5d7d6d3f70253087030b6c3f911f236323a285ab2b448683d588762dd2ca0d`
- `work/polish-east/assets/nursery-trellis-court.png`：`963b3d00b9488c389313d6414bba943d591404713efcc0c632b2bee7be37711e`
- `work/polish-east/assets/stonecarver-workyard.png`：`0ef35507a787017c21577ec0c1ffeb7297e683a61f1643ad95d74224e75ad584`
- `work/polish-east/assets/reading-hall-gallery.png`：`1000ec5b3b67e7d4838488f933bc1a5a3441746c4f7347d8f8aa89cc58cc6ee9`
- `work/polish-east/assets/tower-west-covered-corridor.png`：`b1689eacf9c9559d5fca2c717271a076db4779a426fa486da22b4afb188d6112`
- `work/polish-east/assets/tower-east-armillary-pavilion.png`：`0af0470bf9f72ac9e428fe5f6770e5ad913b6d2f3a12b0f7b47a1e7a93641ab2`
- `work/polish-east/assets/shared-sky-stele-terrace.png`：`850703f45a58c5035bc77486cf70607b340253c7f00931fbf10229887af60f2c`
- `delivery/palace400-r12-production-candidate/registry.json`：`26daca90cfc86621a1755695726cd62ad4f1526e36c5da2cdcbbd783094a1df9`
- `delivery/palace400-r12-production-candidate/layout.json`：`c32a90be0561ae9d51f76336e02b3cb71deb3633b7117785a421e88e2b64827c`
- `review/composition/400u-offline-context.png`：`7eb9c70250e33fe1089ee765dd509f4183703340bffe2e36a30164eeb0635e40`
- `review/composition/all-layers-transparent.png`：`dbd5e70d1f44b6be4b2f4e8886ee26e4b5f814883e4c88897d31af4f9117027a`
- `review/composition/eleven-districts-1x.png`：`aceb3f6dfceac9a80259e43353ecd9866fd4c743207f6c9b3b312ee18a1a1a97`

本轮唯一写本报告，未修改素材、layout、registry、其他报告或游戏；未再派agent。
