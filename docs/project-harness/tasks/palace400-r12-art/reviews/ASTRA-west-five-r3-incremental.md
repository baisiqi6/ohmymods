# Astra 西5件r3局部增量复审

日期：2026-10-23。Reviewer：用户指定Astra美术专项。

**结论：approved，五件指定修正全部通过离线增量审查。** `ASTRA-west-and-amodal-batch-r12.md`中的五项changes_requested由本报告关闭，原已通过8件西区、amodal与荷塘等结论不重审、不改写。

## 实际看图

实际view_image查看`work/styled/west-r3/review/overview-1x-dark.png`及以下五件各自4x四底板：west-two-storey-merchants、west-courtyard-gatehouse、cargo-timber-hoist、market-pottery-house、courier-inn。读`work/west-finite-fix/repairs.json`核对变化包络，但没有以receipt替代看图。

## 五项增量结果

| 资产 | 原检查包络/修后实图 | 结论 |
|---|---|---|
| west-two-storey-merchants | local约[50,7,60,18]左金饰：原浅蓝天空块改为完整金属卷角，底端接屋脊，1x不再突出天空蓝块。 | approved |
| west-courtyard-gatehouse | local约[75,65,88,79]右下檐：原叶状绿块已由自身成对檐材质补回，可辨青瓦及下方檐木连续，未挖成透明缺口，也没有补成明显单色盖片。 | approved |
| cargo-timber-hoist | 新增连接约[27,24,51,54]：原短撑上端现在确实延伸并接入上承力梁，四底下连接处无透底裂口；杆有亮/暗两面，原有下段、主斜撑、竖桅、吊绳和货捆仍完整，旧后屋蓝瓦没有恢复。 | approved |
| market-pottery-house | local约[33,7,129,41]的有限金饰/檐边：原大块浅蓝包边不再保留，双金饰及右主檐端有完整可读轮廓，没有为清色削掉整片瓦体。 | approved |
| courier-inn | local约[63,4,219,35]：左右金饰转为自有金属色，左斜脊接入瓦面，原天空蓝剪贴描边显著消除；主体、右附屋和固定前景无须重开。 | approved |

金饰局部在4x比源图更简练、吊架新增杆段较原细杆有厚度变化，但在最终1x属于可读的构件，不形成新的缺口、悬断或材质阻断。残留个别细边/色阶不扩成无限整圈重画。这里批准的是本轮有限修正及成图效果，不据此宣布其他未改source像素已逐点重新验收。

## 实际输入SHA

- `work/styled/west-r3/assets/west-two-storey-merchants.png`：`6e5bdc76ef749ad2059284592cf29132703f31c46084157f0c8be8df911fac07`
- `work/styled/west-r3/assets/west-courtyard-gatehouse.png`：`de46374d876ad098d7ef62909ceb8c5ea23bbd4787410f72524438bec6484612`
- `work/styled/west-r3/assets/cargo-timber-hoist.png`：`7bb3b8729bcc5ec3a29e5e279d7748b73dc134e99bb9795785996e17c5f9168f`
- `work/styled/west-r3/assets/market-pottery-house.png`：`7eb0423eb7df64a7a8a7a324d2ae6c602a726e15e23097254e90a14dd0cae4d9`
- `work/styled/west-r3/assets/courier-inn.png`：`df3e3d0617208ef82b34e947d3424e0a7085d44c729131a8deb2dffafe78c039`

## 范围

五件可进入后续整幅重组与注册。whole compound和子件未独立amodal完成的原有限制继续有效。未批准Native接地、水位、相机、视差、交互或最终组合验收；后续Core5/East9修正另作增量。

本轮仅新写本报告，未修改图片、脚本、registry、其他报告或游戏，未再派agent。
