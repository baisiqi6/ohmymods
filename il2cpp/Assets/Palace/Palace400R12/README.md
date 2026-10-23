# 诸岛共天、同入天朝 — R12 400u 生产素材

本包用于当前完整 IL2CPP Mod 的独立副本接入。`readyForIsolatedIntegration` 仅代表生产素材及离线组合审查完成；`nativeAccepted=false`。实际收费入口、存档、场景切换和坐骑恢复由主线唯一消费者实现，包内不写游戏状态。

## 内容

- 54 件完整建筑、院落、桥、船及两端山石实体；保留原“唐朝建筑”三层塔。
- 13 件独立背景：6 组宫阙、6 组树木、1 座东方云山。
- 5 件路岸模块：砖石步道、砌石岸墙、两种完整下水阶梯、园墙。
- 10 件玩家人物 atlas，共 294 个时间帧；4 件荷花/荷叶 atlas，共 64 个时间帧。
- 400 游戏单位，11 个区域，452 个布局实例；横向长度来自新增设计，未横拉建筑。

`registry.json` 和 `layout.json` 必须作为一组采用，全部文件由 `SHA256.json` 精确覆盖。`palette.json` 是冻结 B128-r2 环境色盘，人物保留原作色盘。PNG 使用 Point、二值 alpha、透明 RGB0，每边不超过 2048。天空、水面、反射、天气、过场云雾由原游戏环境及主线过场提供，未烘入 PNG。

## 坐标与锚点

本包像素坐标为画布左上角原点、Y 向下，`localOrigin=[0,0]`。设计 PPU=32，街面参考 Y=224。世界 X 范围 `[-200,200]`，宫门中轴 X=0，对应设计像素 6420。旧原图的 source/master Y 仅为来源记录，不能推算 Native 地面。

对每个 placement，设其 `supportLocalPixel=(sx,sy)`：

```
worldX = supportWorldX + (localX-sx)/32
worldY = measuredNativeReferencePlaneY + supportOffsetYUnits + (sy-localY)/32
```

`referencePlane` 明确选择 Native 地面或水面。每个 PNG 的 `masterId/masterRect` 保持唯一画布映射；运输裁片与动画帧按显式 rect 回映，不按 opaque bbox 居中。

宫门 `return-to-source` 使用独立交互锚 `[218,176]`，含义是中央门扇底缘前的门槛中心。它与建筑 support `[218,178]` 分开定义，世界 Y 为地面 +0.0625u。该点只标明返程门位；实际触发范围、按键和玩家可达性待 Native 校准。来源岛入场为每次 10 金币，返程不再次收费。

舞女通过最终戏台 local deck `[257,123]` 变换，台面距该建筑 support `[257,140]` 为 17/32u。不要重用旧脚线或旧版 `.675` 等偏移。

## 层序、动作与完整性

`drawOrderHints` 是相对语义，不能当成已测 Native Z。岸墙在两端山石和人物之后，船与荷塘在岸前。4 个背景运动组非空；`backgroundMotionRates=null`，由主线原生镜头/视差实测后设置。

每个实体是固定复合体，可整件移动；固有侧厢、树木、院墙等成员没有独立补全声明。13 件背景可独立移动展示；远景台基已经按离线布局降低或置于可信前景之后。真实旋转/视差揭露范围仍须在 Native 验证。

动画一律读取显式 `frames[].sourceRect`、`durationMs` 和 pivot。舞女 26 个时间曝光复用 21 个物理格，不允许用 `index % columns` 推格。人物已经是逻辑像素 atlas，不再除以 8。人物来源 PPU 未知的条目仍为 null，不能把设计 PPU 当成原生成人人体标尺。

四天王使用已审全身、手臂及法器休闲动作；守卫有行走/停步/致意/转身；舞女有舞蹈/行礼。未提供守卫战斗、天王行走或居民搬货等新玩法动画。荷塘两档动作服从原引擎本地时钟、暂停和天气，固定茎脚/叶脉挂点，不另开独立世界时间。

## r2 局部修正（2026-10-23）

r2 是 r1 的仅元数据修订：`far-palace-34` 内收至 X196（designX 12692），`forest-74` 内收至 X198.8125（designX 12782）并因回到 `[-200,200]` 行走域内而移除 `allowOutsideWalkDomain`/`placementScope`；两实例的 district 11、Y、`supportLocalPixel`、`drawRole`、`motionGroup` 及其余 450 个 placement 不变，86 个 PNG、`registry.json`、`palette.json` 与帧数据逐字节保持。layout SHA 与 `offline-approval.json` 已同步重钉，旧 r1 bootstrap 仍拒绝 r2 index；`nativeAccepted=false`，右端 Native 实机复查仍未完成。

## 验证范围

离线已核验完整文件哈希、358 个时间帧直接 RGBA 回读、固定 pivot、二值透明、11 区组合、4 层 solo，以及 54 实体和 13 背景的整件平移回读。独立图审与布局审批绑定 `offline-approval.json` 的资产和 layout SHA。

接入后仍需在完整 Mod 隔离副本验证：真实地面/水面差、岸阶接口、成年 BODY 与可见脚底、遮挡、实际相机/旋转曝光域、背景视差、昼夜天气、人物与荷塘运动，以及付费进入→保存退出→新进程宫廷读档→返程、金币与坐骑携带。此包不宣称这些 Native 项已通过。

仓库验证入口：

```
python3 tools/palace400/validate_pack.py il2cpp/Assets/Palace/Palace400R12 --require-offline-ready
python3 -m unittest discover -s tools/palace400 -p 'test_*.py'
```
