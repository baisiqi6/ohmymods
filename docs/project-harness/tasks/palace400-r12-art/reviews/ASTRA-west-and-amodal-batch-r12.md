# Astra 西区成图、补全资产与矮墙集中审查

日期：2026-10-23。Reviewer：用户指定Astra美术专项。

## 分组结论

- **西区13件：changes_requested，5件需有限修正，另外8件通过离线图审。** 不是整批重生成。
- **5件真实补全资产：approved，限完整whole compound离线alpha/结构/风格。**
- **普通矮墙增量：approved。** 原报告的平色占位风格问题关闭；前4个地岸不重审。
- 不批准Native ground/water/adult/视差、实际排序、交互或整包上线。未知运行时参数不是本轮打回理由。

## 实际看图与证据

实际view_image查看`work/styled/west/review/overview-1x-dark.png`及13件各自`*-4x-four-backgrounds.png`；5件amodal各自1x与4x四底板；当前`work/ground/review/low-garden-wall-plain-4x-four-backgrounds.png`。针对吊架额外看`entities-west/review-local/hoist-both-braces-source-18x-grid.png`并读local-review-note，区分清掉的后仓蓝瓦与仍需补全的木撑。

已读两组registry中的所有权/排除对象/补全边界，现场计算资产SHA并核对13件styled与5件amodal均匹配registry。`ferry-storeyard`已降draft，不属于13件通过范围，本报告不将其恢复。

## 西区13件逐项结果

| id | 结论 | 证据/边界 |
|---|---|---|
| west-two-storey-merchants | changes_requested | 主体完整，左主脊饰有明显天空蓝残块，见下。 |
| west-courtyard-gatehouse | changes_requested | 门脚补全可接受；下层右檐仍带局部叶状绿色块，见下。 |
| west-drygoods-pavilion | approved | 两铺面、高亭与根脚闭合，主体屋脊未被裁断。 |
| cargo-timber-hoist | changes_requested | 后仓蓝片已清掉；下短斜撑仍自由终止，未成为完整支撑关系，见下。 |
| cargo-dock-store | approved | 组合屋顶、布棚、柱脚、桶货完整，未见明显外部天空块。 |
| cargo-pack-yard | approved | 作为固定货物/架子小组合可读；框架间负空间透底，不能拆成独立完成子件。 |
| wood-bamboo-workshop | approved | 主屋、右棚与固定木料工具成组，檐与柱脚连续。 |
| market-pottery-house | changes_requested | 两脊饰及右主檐存在连续亮天蓝残边，见下。 |
| food-market-alley-gate | approved | 门洞真实透底，两侧门柱与屋顶完整；不是已实现的游戏入口。 |
| drygoods-food-row | approved | 三铺固定组合完整，货物保留不当空气挖掉，少量边缘色差不扩大返工。 |
| courier-inn | changes_requested | 主要结构完成；左主屋斜脊及金饰天空蓝包边仍显著，见下。 |
| courier-delivery-yard | approved | 实际是货车/包裹小组，未将整段院墙宣称可移动；前轮轮廓与必要空气孔可读，隐藏后轮不作独立资产通过。 |
| courier-neighborhood-gate | approved | 双门、屋顶、两门脚完整，邻接花盆已排除，当前可用作整组门体。 |

### 五处有限修正（坐标为asset-local、左上0，均为人工检查包络）

1. **west-two-storey-merchants，x≈51–58、y≈7–17**：左主脊金饰上方/右旁的浅天蓝块在深底和岩色底都突出，不是金属本体。只去掉外部天空并修复真实金饰混色边；保留金饰高度和屋脊厚度。固定阳台盆栽不在此项删除范围。
2. **west-courtyard-gatehouse，x≈75–87、y≈65–78**：下层右檐/木檐之间仍见一块绿色叶状覆层；不是微小1px瓦面高光。metadata排除了周边树木，这块不应随门楼移动。仅补这小块被遮住的青瓦/檐木，按相邻可见层次延续；不能只挖透明洞导致瓦檐缺一块。门脚及已通过的上层窗墙不重做。
3. **cargo-timber-hoist，短斜撑从约(12,74)向(31,49)延伸，在(28–33,47–53)附近结束**：当前四底板显示其上端仍停在空气里，没有接到主长斜撑、上横臂或另一个可读节点。实看source网格也能见短木条向背景树丛消失；去掉蓝瓦是正确修正，但不等于被环境遮住的木撑上端已经补完整。最小处理是仅补短撑缺失的延续/明确连接节点，接到合理的现有承力构件，保持原粗细与木纹；若设计决定它只是非承力支出杆，须画出清楚完整端面并修正其支撑声明，不能仍将不明截断作为完成撑杆。无需重生成整架、货捆或绳索。此项不要求把刚清掉的蓝片放回来。
4. **market-pottery-house，x≈35–44、y≈8–25；x≈101–109、y≈8–27；x≈116–129、y≈31–42**：左右脊饰附近及右主檐尖残留明显浅蓝亮带，四底中颜色固定。按先前pilot相同做法只处理真实天空/混色边，保留金饰、瓦面和屋檐体积，不能整圈腐蚀或全图按蓝色删除。
5. **courier-inn，x≈62–84、y≈3–43，另核右脊饰x≈210–221、y≈3–18**：左主斜脊与金饰有连续天蓝描边，右金饰也有小块相同残边。它超过零星材质1px差异，深底缩览已能读成剪贴白蓝边。只局部去混色/收紧真实外缘；不动主屋比例、阳台植栽和右侧固定茶桌区。

以上修后只补审5件相应局部及1x成图，不再重审8件已通过者。其他零星绿色/冷灰边像素没有据此自动判为背景；没有使用颜色阈值给整批下结论。

## 五件补全资产

| id | 离线实际观察 |
|---|---|
| west-rock-pine-endcap | 松、柳、根与整座岩体闭合，底部与两端均有完整轮廓；无水面/反射烘入，树间空隙透底，无面状雾。 |
| ferry-covered-boat | 船首尾与船底完整，棚顶/立柱/固定舱门存在，棚内开口透底；没有外部水块或倒影。只批准整船，固定舱体不是可拆子件。 |
| cargo-freighter | 完整首尾船体、固定货堆和弧形舱棚清楚；水线以下是实际船体形状，不是烘入水色。货物与舱后面不宣称单独补全。 |
| food-tea-kitchen | 左墙/柱脚与前廊承托成立，完整二层、布棚、桌椅及烟囱保留；烟囱无烘烟，外轮廓无周边建筑缺口。局部开廊为空间，不误当缺墙。 |
| courier-empty-stable | 长屋顶、三空厩、后槽/横栏、支柱与基座完整，未出现后屋半截或用周边墙假装自身结构；未烘入马匹/人物。 |

五者B128-r2后的材质可读，未见应阻断本阶段的等比缩小失真或断梁。局部细节柔和/色阶不作无限返工理由。whole compound可以后续注册，隐藏子件依旧不独立ready。

## 普通矮墙增量

当前128×44的主墙面已改为低对比不规则灰泥色簇，帽瓦有瓦面/阴影，柱脚和底部石材不再是先前均一矩形占位。四底下alpha、两柱、帽瓦与根部轮廓完整。未见需要继续打回的显著镜像花纹或高对比重复纹理；可以通过，不要求再增加装饰。

## 实际输入SHA

- `work/styled/west/assets/west-two-storey-merchants.png`：`dbe8a322cf44270fb7b98c5925082ae979c827c51d55e0132fc2b0bf81ddb121`
- `work/styled/west/assets/west-courtyard-gatehouse.png`：`4918941aac8c27f5f1e7f09e1f9d37258cbc202df751d6ec5dd4fdd5670a4930`
- `work/styled/west/assets/west-drygoods-pavilion.png`：`12b60d7aa2563ea17d689190d04372ee91bf5cda24ec740a7c975d2a3facc17a`
- `work/styled/west/assets/cargo-timber-hoist.png`：`9551d03f7a7f51587891677d8f166fe5c08f3df3990959a2866c0aeee3f0f801`
- `work/styled/west/assets/cargo-dock-store.png`：`6a759ee53305abe00cf9991433f8c94fb71249d7ced494a6ad2d201af923c6b3`
- `work/styled/west/assets/cargo-pack-yard.png`：`cc78c2db7388776e6313e0d34ea52698741199e97fd1e2b33407f1a444606b43`
- `work/styled/west/assets/wood-bamboo-workshop.png`：`0c3a624336658e4dd824d348444d1c8b1e8ab6c5b98806bf04fc9a2ef4bb5718`
- `work/styled/west/assets/market-pottery-house.png`：`186bac2bd0a9534d52cfd4410a9e18c228ca2f1772f08a1a4fbfe4d1885afe46`
- `work/styled/west/assets/food-market-alley-gate.png`：`004e66338343847eb81c7482562d036ba6dd417ebb73c6362dd353abe66f3eb2`
- `work/styled/west/assets/drygoods-food-row.png`：`5537dcb9d0e169b459919d2f65bbd0012aa816036b2921a983758a7a849a961b`
- `work/styled/west/assets/courier-inn.png`：`cf3a5821c3b44aab166853f3e56416713a18302ab3b1375a635785c77ce94af6`
- `work/styled/west/assets/courier-delivery-yard.png`：`36a48ece3d7dd0cde87f7bb17bbcd67dc36f112a1e31314f649efa079feff6ce`
- `work/styled/west/assets/courier-neighborhood-gate.png`：`7780d10b1048634147ac2665e159be0294bcf3b10eb068928e30af961289aa74`
- `work/amodal/assets/west-rock-pine-endcap.png`：`3d8c96e5d4ebd718ef1c6ba0c5093b715bb2cd197f077d43cbab6b6070050410`
- `work/amodal/assets/ferry-covered-boat.png`：`0a48841d6a7b7034223ace4f75c655f8fae38a0cc1d20ed041e3608f4759ded4`
- `work/amodal/assets/cargo-freighter.png`：`47f77d590cfcd15aae3f8eef4166320e8300f177e04178336c955ef4bca8b9ab`
- `work/amodal/assets/food-tea-kitchen.png`：`13df578efe740371505a612191bdbf8acde19c3151cf43d63707699bc6f2f643`
- `work/amodal/assets/courier-empty-stable.png`：`89de4ac9cec367492e90eb44d7ffcd46a5fec2f712676d2eaff9d411ddfb4c66`
- `work/ground/assets/low-garden-wall-plain.png`：`78cdca109379ae7bf3e92b350456f650bbe4aaf7be954d935d01a7442adc02c8`
- `work/entities-west/assets/cargo-timber-hoist.png`：`df2644ce8ea38b7566e53d181177ef4f61770db9dc986139719df4b3d306118e`

本轮仅新写本报告，未修改图片、脚本、registry、其他报告或游戏，未再派agent。
