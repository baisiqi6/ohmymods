# Astra 清边、虹桥、地岸与天庭点景集中增量审查

日期：2026-10-23。Reviewer：用户指定 Astra 美术专项。

## 分项结论

| 项目 | 结论 |
|---|---|
| 3个pilot有限清边增量 | **approved**；上轮指定背景条块/金刹蓝边问题关闭，原主体结构与compound结论继续有效。 |
| timber-rainbow-bridge | **approved**；离线完整compound的alpha、结构与风格通过。 |
| street-stone-paving | **approved**，限离线模块。 |
| quay-vertical-brick-wall | **approved**，限离线模块。 |
| quay-descent-stairs-narrow | **approved**，限离线模块。 |
| quay-descent-stairs-wide | **approved**，限离线模块。 |
| low-garden-wall-plain | **changes_requested**，仅材质/明暗风格需有限修整；结构/alpha本身成立。 |
| east-celestial-vista | **approved**，限离线完整远端点景。 |

前12个背景与B128-r2色盘沿用已有通过结论，不重审R11布局。不批准Native水差、相机、视差、人体比例、运行时通路或排序。

## 实际看图与辅助文件

已实际view_image查看三pilot各自1x/4x四底板，以及`previews/review-r2/`三份`targeted-r1-r2-delta-4x.png`；虹桥1x与4x四底板；ground/review下五份4x四底板；`background-r2/review/east-celestial-vista-4x-three-backgrounds.png`。

已读取pilot edge-revision-r2 receipt、bridge metadata、ground registry与build_ground.py的几何/材质生成段、background-r2 registry最后的点景记录。receipt只是范围辅助，结论来自实际图像。

## 1. 三pilot指定污染点复审

- 粮仓：上层外柱旁浅蓝竖条已清除，下层右屋脊绿色带与卷脊旁叶色大块已去掉。右脊仍有金饰卷翘，屋面斜线连续，未因清边大面积削掉瓦体；四底1x不再有原绿色条带的剪贴观感。
- 宫门：上层左右外柱旁矩形天空条块已透出真实底色；指定主屋面斜脊与侧阙内斜脊不再保留原浅蓝/绿色宽带。朱柱、上层栏杆端头、主檐和金饰存在，未出现清边后断柱或悬檐。零星边像素不扩为重新整圈返工。
- 唐塔：金刹右侧亮天蓝突块消失，金刹顶端与主体高度仍完整；上层右檐尖和铃旁蓝片清除，铃有连接，三道檐体积与石基没有被削坏。

本次关闭的是`ASTRA-pilot-entities-and-style.md`中指定污染点。原compound/隐藏子件限制仍有效；这里不声称货车、石狮等已可独立移动，也不替代后续统一色盘处理后的成品核验。

## 2. 完整木虹桥

378×86样本在1x可辨平缓拱形、红栏、连续桥面、拱下木梁/交叉撑和两座完整石桥台。4x下桥孔、栏杆间与木撑间均能透出不同底色，无烘入水面或原背景天空。两端基座石面、边缘和底脚没有被裁断，也未见原先局部桥体需要靠遮挡才能成立的情况。

结构风格与同批红木/灰石资产相容，没有因均匀缩小产生大面积断杆或不自然横扁。桥台承托为视觉候选；仍只把整个桥作为一个compound批准，不把两桥台/栏杆/木撑各自当独立完工子件。

## 3. 五个地岸模块

- **铺地128×10**：浅石面和前沿层次连续，纹理来自明确石材区域，当前可作为铺地模块。边缘是模块边界，不能因其矩形就误判为抠图残块。
- **三道砌墙128×24**：三层石面与灰缝清楚，正面承托完整；没有原水色渐隐或隐藏结构被竖向拉长的视觉迹象。重复规则属于模块施工几何，不能外推为实际运行时接缝已验。
- **窄阶46×33**：两侧边石与各级踏面完整，最底没有原先烘水蓝条；四底下轮廓清楚。近灰石高光不是水的证明，不要求全局去掉冷灰。
- **宽阶87×37**：右侧边石已完整保留，最右没有独立蓝水列；左外缘与底部闭合。宽窄两组来源细节稍软，和当前同批源图材质相容，不因此无限重画台阶。

### 唯一需修：low-garden-wall-plain 128×44

**问题是材质风格，不是alpha或完整性。** 四底板上这面墙明显呈纯色几何示意块：

- asset-local **x≈9–118、y≈12–32**的主墙面为大块几乎单一颜色，缺少同批院墙已有的灰泥细微色簇、瓦下投影和靠柱/墙脚的接触明暗。
- **x≈5–122、y≈4–10**的帽瓦主要是重复斜杠色带，面/侧厚度与瓦层叠压读感不足；与已通过屋瓦和院墙相比更像平面描边。
- 两侧柱约**x≈3–8 / 119–124、y≈8–38**和柱脚也是均匀矩形色块，未与有石材纹理的地岸模块形成相同完成度。

**最小修法**：保留128×44、完整两柱、墙脚、帽瓦轮廓和所有注册坐标；只补一轮材质明暗。使用声明清楚、无植物/门窗混入的灰泥/瓦/石材质参考，给墙面少量受控色簇，瓦下和靠柱接触阴影，帽瓦的面/侧层次，柱脚石面变化；再走既定B128-r2一次。无需增加花窗、装饰或新建筑，也不要求换尺寸、重生成整套模块。主墙仍可以朴素，但不能以“plain”保留与整组素材明显不同的占位图完成度。

修后只复审这一块的1x/4x与新SHA，其余四地岸模块和本报告其他通过项可复用。

## 4. 东端天庭点景

166×117完整浮山、宫阙和悬垂根石轮廓成立，顶部楼阁/松枝、左右小挑檐、底部尖岩没有裁断。白、青、深底下能看出真正透底，未见带入整面天空/白云或白雾底。少量独立悬石属于浮山整体语义，不按地面建筑悬空缺陷处理。

作为远端点景，其细节比近景建筑柔和可以接受；本体依然是一个完整compound，内部楼阁/悬石不获得独立ready资格。最终远景位置、相机可见域和视差率仍由主Operator验证。

## 实际输入SHA

- `work/entities/samples/cargo-granary.png`：`a773c64465df2c343ecf3951a242fbf22c2f192321b1edb44b4cb2cd647f398d`
- `work/entities/samples/central-palace-gate.png`：`a70985be80c7f86c7b59070d49fa8a867bfe665093787af80a21c03cadc6ea7c`
- `work/entities/samples/tang-tower-original.png`：`5081aa36165e1b7674999f07fe6bd70d19d945c5ad31993c0c183c997c302ae2`
- `work/bridge/timber-rainbow-bridge.png`：`0e5d9d69ad4d9b1d97f64c58e5d920404d2aa75c4649d41653d94fb8a5490b65`
- `work/ground/assets/street-stone-paving.png`：`013accb2301ec96350a422f0bfa110645a1cbccfe03b77bf0ce8ecea7200e4f2`
- `work/ground/assets/quay-vertical-brick-wall.png`：`485cbfa5ee3b0559dbc007b0061fe63ed42f43d56a7c1d5e3ebe63433ab452b1`
- `work/ground/assets/quay-descent-stairs-narrow.png`：`bd330347e2857f5bb94eb23ad41d9a6b9952b90b8cc56f4c69d416816c34bf09`
- `work/ground/assets/quay-descent-stairs-wide.png`：`8d2ea48b98f1a3bbb461d30b5295a919b9033c59167d972334e32072f180a673`
- `work/ground/assets/low-garden-wall-plain.png`：`4b4ec1f6535845b5731d32281e7d1e8438a60c81efe329fad46a12ce2933ab5d`
- `work/background-r2/assets/east-celestial-vista.png`：`5c539e2d26b4022c9b0fd6fabb2f499649c2153f1a71e7e74b21443202c302c2`

本轮只新写本报告，未改图片、脚本、其他报告或游戏，未再派agent。
