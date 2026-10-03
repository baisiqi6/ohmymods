# Issue #99 玩家反馈修复切片（2026-10-03）

关联 #56 / #27 / #87，保留这些更大功能 Issue 的归属与未验状态。本切片是在玩家实玩反馈后
从累计开发线中剥离出的独立修复，清洁 PR 只含以下范围，不带累计开发中的银行/英雄/探索岛/
重盾等其它未提交修改。

## 范围

1. **CoinCourier 同进程切档后不可见**：原生 view 根/渲染器/共享图集被回收（fake-null）而
   托管缓存仍存活时，同 world 只做纯视觉替换：原地重建显示对象与图集，配送相位、已消费
   动作、访问计划、已送数量、目标、冷却、钱袋全部保留，绝不走整场清理。替换失败按约 0.5s
   节奏有界重试并保留旧句柄作"本世界已初始化"标记，绝不逐帧重建；确证坏素材保持
   fail-closed；共享图集换代后，既有 view 在动画帧号不变时也必须重绑。
   同帧去重只在已提交切图仍可用时生效；`Sprite.Create` 失败不得留下"半就绪"图集。
2. **中世纪风格预览误用希腊帧纠正**：`KnightStylePreview0` 原与 `KnightStylePreview3`
   同图；现改为锁定原生 `knight_idle` clip 1144 → Sprite 8060 灰盔帧，只补透明画布、
   不拉伸。其余四张预览图逐字节保持。
3. **哥布林身高 +5%**：自带视觉节点 X 保持 0.625（含 ±镜像），Y 0.625→0.65625，Z 恒 1；
   只缩本模块自有视觉子节点，脚点/位置/物理/经济路径不动。
4. **当前 Greek 世界平民站高 +5%**：`NorseCivilianScaleY` 0.70→0.735（0.70×1.05×32/18）。
   转职 Promote、`WarriorPeasant.OnEnable`、`Peasant_norselands.OnEnable` 三个既有入口
   继续共用该常量；只写 Y、只限当前 Greek 作用域、重复应用不累乘，其它 world 与职业保持。
5. **夜间/受阻 Wait 原地动作**：驻留与受阻时由单一时钟连续播放 Idle 2s → Leisure 2.4s
   循环；夜间不发起闲走、受阻不猜地面位置，安全站位/暂停/保存/配送与真实威胁处理优先级
   不变；暂停（delta=0）不推进。

## 文件清单

| 文件 | 变化 |
|---|---|
| `il2cpp/CoinCourierRuntime.cs` | 纯视觉替换/有界重试/状态保留；原地休闲循环 |
| `il2cpp/CoinCourierVisuals.cs` | 图集回收恢复、同帧去重修正、贴图可用性门、材质 fake-null 重取；Y +5% |
| `il2cpp/PatchRoles_Worker.cs` | 仅共享常量 `NorseCivilianScaleY` |
| `il2cpp/Assets/KnightStylePreview0.png` | 锁定原生 knight_idle 帧 + 透明补边 |
| `tests/coin-courier-runtime-bridge/Program.cs` | 夜间/受阻新契约断言 |
| `tests/coin-courier-visuals/Program.cs` | +5% 身高期望值 |
| `tests/coin-courier-visual-lifecycle/` | 新增：视觉生命周期回归 |
| `tests/greek-civilian-height/` | 新增：平民站高回归 |

## 非目标

银行家活动范围与账本（独立 Issue）、存档 schema、其它职业缩放、联机行为、安装/发布；
不修改游戏正式副本、不操作玩家数据。

## 状态

模拟回归与 ARM 编译证据见 `validation.md`；实玩验证由用户完成，合并后 Issue 保持验收状态，
不以编译或模拟测试替代实玩。
