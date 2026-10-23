# Astra 远景台基落位增量复审

日期：2026-10-23。Reviewer：用户指定Astra美术专项。

**结论：approved。上一轮唯一离线组合阻断已关闭，当前400u offline composition design通过。**

仅复审远景宫阙实例落位。`ASTRA-final-polish-and-composition-r12.md`已批准的14项局部修正及其他未变资产/布局观察继续有效；不重新展开单件风格审查。

## 实际看图与范围

实际view_image逐张看更新后的`district-01-1x.png`至`district-11-1x.png`，并看`far-palace-skyline-solo.png`、`rear-courtyard-architecture-solo.png`、`mid-courtyard-forest-solo.png`、`east-celestial-vista-solo.png`及`400u-offline-context.png`。

逐区图用于判断实际遮挡和承托；solo用于确认层仍存在，不把solo透明背景中的独立图块当组合悬空缺陷。读取当前layout确认仍为452 placements，其中35个far实例；其预览台基Y分布为158、166、174、182、190、198、206、214、224，没有继续对所有完整宫阙统一使用同一抬高值。

## 原问题关闭证据

| 原代表位置 | 修后实际观察 |
|---|---|
| far-palace-4，X≈1420，第2区左侧 | 台基已降到预览Y224，落在低墙/街后；原Y158下方天空空隙消失。 |
| far-palace-10，X≈3700，第4区客栈左侧 | 小院降到预览Y224，处于自有可见落地关系与树/前屋之间；不再有空中水平石台基。 |
| far-palace-29，X≈10920，第10区石作棚左上 | 台基降到预览Y224，建筑下部由石作/植栽前景组织承托与遮挡；原棚上天空中的悬空小院消失。 |

已扫其余全部区段，不只检查三个代表。未再见同类“普通都城宫阙整条石台基悬在天空、下面仍露空”的明确剩余阻断。仍较高的远景下部被当前可信的屋体、树簇或院墙遮挡；放低到间隙里的小院也有可读落位。没有通过新增无语义遮挡条、拉伸建筑或透明渐隐掩盖问题。

远/后庭/中林三层保持非空；宫门主轴、唐塔、前岸与两端石景沿用已有效结论。明确悬浮的天庭点景仍仅在东端，具有完整岩根，不与此次普通远景露底问题混淆。

## 批准边界

通过的是当前静态离线400u组合设计与本次落位修正。Native实际parallax、相机域/宽高比/旋转reveal、ground/water、人物BODY与脚底、动态遮挡排序仍待主Operator实测；相对运动未来可能重新暴露台基，需要届时验证。本报告不将其宣布通过。

新增门返程锚[218,176]是元数据变化，未改图，不在本次美术增量中作运行语义验收。

无需为此次原唯一阻断继续改图或重审未变PNG；可推进后续受管集成与实测。

## 实际SHA绑定

- `layout-instance-overrides.json`：`8eb93415c2af571329cc6f6e44d2437b311150011bc5d2a0cf3abcd3f0086b1c`
- `delivery/palace400-r12-production-candidate/layout.json`：`5078d7c2091fbfee916ce1c553fe410b0ae1aad2fe0874c01c9d94fa00686f8a`
- `delivery/palace400-r12-production-candidate/registry.json`：`169da36f870c886c2b5c224ec5c19ba66722bd6481dec866dc46efc585c06b27`
- `review/composition/400u-offline-context.png`：`02634b21be973345a174d2ee89be7198b8a256107a0c5a923d255e7378a8977a`
- `review/composition/all-layers-transparent.png`：`0d0ef11d192cbc80c1dfe198585c6200ad1b8907d226db333ca21817b6ba49a9`
- `review/composition/eleven-districts-1x.png`：`bba9dfad1db7975a3fd5636b11e123e13a72d9ec630638cfa976b480bb059692`

本轮仅新写本报告，未修改图片、layout、registry、其他报告或游戏，未再派agent。
