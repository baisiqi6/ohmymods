# 视觉 worker 回执 — 残影真白剪影烘焙（packed 修正版）2026-09-25

任务：只改 `il2cpp/SamuraiDashVisuals.cs` + `tests/samurai-visuals/{Program.cs,Stubs.cs}`，实现按 packed 审查
（`packed-design-review.md`）修正的 CPU 逐三角形白化路径。未编译主工程（Operator 统一编译）、未动其他
worker 文件（`PatchRoles_SamuraiPowerDash.cs`/`tests/samurai-motion/*` 的改动非本 worker 所写、未被触碰）。

## 1. 实现（对应审查最小约束 1–6）

| 审查约束 | 落地 |
|---|---|
| 1 `WhiteKey` 加 source 身份；`Bakeable` 改几何校验，删 packed/rotation 拒绝与 textureRect 读取 | 键=`Sprite.GetInstanceID()+纹理指针+rect+pivot+PPU+extrude`；旧 `Bakeable` 整体删除，改为 `BakePose` 内一次性几何校验（vertices/triangles/uv 非空、长度一致、三角形索引界内、非有限顶点整 sprite 降级） |
| 2 单路径光栅化 | 输出 `rect` 取整尺寸 RGBA32；顶点像素=`vertex*PPU+pivot`；像素中心重心坐标；UV 线性插值→最近邻 `(int)(u*W)` 采样图集 alpha（越界 clamp）；覆盖内 `(255,255,255,源alpha)`，覆盖外 `(255,255,255,0)`；源零写入 |
| 3 图集 alpha 缓存 | CPU `isReadable` → `GetPixels32()` 一次抽 alpha `byte[]`；不可读 → `RenderTexture.GetTemporary`+`Graphics.Blit`+`Texture2D.ReadPixels` 一次（`finally` 恢复 `RenderTexture.active` + `ReleaseTemporary` + 销毁 staging 贴图）；键=指针+宽高+纹理名 hash（指针复用防线）；LRU ≤4 张且 ≤16MiB 字节预算 |
| 4 姿态条目自持 | `WhiteSpriteEntry{Sprite,Texture2D}`，淘汰/`ClearAll` 时同生共死；`WhiteTextureEntry`/`Users`/`ReleaseWhiteTexture` 引用计数链与全图白纹理整体删除 |
| 5 降级日志 | `DegradeWhiten` 一次性（按 reason 限频）保留，新增 `invalid-rect-or-ppu`/`missing-or-mismatched-geometry`/`non-finite-geometry`/`geometry-read-failed`/`triangle-index-out-of-range`/`rasterize-exception:*`/`invalid-atlas-size`/`atlas-alpha-unavailable` |
| 6 非目标 | 不写源 sprite/纹理/材质/属性块；无自定义 shader、无新 Harmony 钩子、不动动画与游戏资源 |

新增：白化**成功**一次性诊断 `whiten-baked`（源 sprite 名/rect 尺寸/输出尺寸/PPU/pivot/packed/raster=uv-triangles），
与 `whiten-degraded ... (NOT white)` 明确区分；残留旧 `LogShaderFallback` 三级链、8 槽、2s 淡出、alpha 阶梯、
`order+2/+3` 全部保持。

缓存语义：姿态 soft cap 64（在飞 ghost 引用的条目不淘汰；本轮刚烘焙条目由 `TrimWhiteSprites(entry)` 显式保护，
绝不返回已销毁对象；引用释放后下一次 `Whitened` 收敛；>2× 时一条一次性 `whiten-cache-overflow` 日志——见 §5 可达性说明）。
图集 LRU 可无条件淘汰（纯 alpha 字节，姿态贴图已自持）。

## 2. 真实 native 数据 vs 推断（务必区分）

**真实（来自 2.4 串行化资产，只读）**，任务目录 `native-charge-fixture.json` / `native-bamboo-assets.json`：
- `knight_charge_bamboo_0`：`rect=(0,0,41,32)`、`pivot=(14.349999755620956, 0)`、`PPU=32`、`packed=true`、
  `rotation=0`、`texture_rect=(1468,170,38.98708724975586,25.923879623413086)`、图集 2048²、59 条 bamboo 全 packed；
- `vertices`（7×2 局部单位）、`triangles`（15 索引）为串行化网格实数据；
- `alphaBottomUp`（32 行 × 41 字节）= sharedassets0 该 sprite 区域的实际 alpha 通道。

**推断（非 runtime 捕获，已在夹具与测试中标注）**：`uvInferred` 由串行化 `atlas.uvTransform` 推导，不是运行时
抓取。测试把它当既定输入使用；自洽性已核（`uv=(tileOrigin+vertexPixel)/2048`，如 V6 顶点像素 (0,0) →
uv6=(1468/2048,170/2048)，与夹具逐位一致）。

数据完整性：测试内嵌的 32×41 hex alpha tile、7 vertices、7 uv、pivot 已用脚本逐项与任务 JSON 比对**全等**
（发现并修正 1 处手抄错误：第 30 行多 2 字节），防止夹具自洽掩盖抄写错误。

## 3. 测试与计数

`C:/Users/ADMIN/dotnet8/dotnet.exe build -c Release --no-incremental` → **0 错误**（仅 1 条既有 `Shader.name` 桩警告，
改前即存在）；`... run -c Release` → **RESULT 29 passed, 0 failed**（17 条既有基线 + 12 条新增）。

新增 12 条（全部先红后绿主体，见 §4）：
1. 非打包 quad golden（48 像素逐像素 RGB255/源 alpha、尺寸、pixel pivot/PPU、源未改、单次读回/上传/apply、成功日志）
2. **真实 packed 夹具**：输出 41×32、native pivot/PPU、网格包络==`ceil(texture_rect)`（39×26）、
   内部像素 alpha==native tile、几何外像素 alpha==0（含邻居 bleed 角 (40,31)：tile=255 而输出=0）、
   1312 个图集像素逐一未改、成功日志含 `knight_charge_bamboo_0/packed=True`、图集缓存 1 条
3. 姿态切换冻结（旧 ghost 保留旧剪影与其贴图，新姿态各采各的图集区域）
4. 跨 owner 共享 / 无逐帧重烘（1 次烘焙、1 次读回、缓存 1 条、6 帧后计数不变）
5. 八槽阶梯/排序 + 单次烘焙（alpha .55→.0928125、order+2/+3、9 renderer 上限不破）
6. 不可读图集 GPU 读回（alpha 逐像素、RT 取还销毁、active 恢复、单次 blit、staging 销毁计数）
7. GPU 读回失败（回退原 sprite + `atlas-alpha-unavailable NOT white`、finally 还 RT/恢复 active/销毁 staging、
   负结果缓存不逐帧重试、源未改）
8. 位移夹具（图集 (30,40) 起 5×5 实心块 → 输出 rect 像素 0..4，证明无 y 翻转/偏移正确）
9. 旋转 90° 合成夹具（图集竖条 → 输出第 3 行横条，UV 驱动即正立）
10. **键碰撞**（同 rect/pivot/PPU 两个姿态 → 2 条缓存、各自 alpha 图案不串帧）
11. 缓存上限/在飞保护/新对象不被自己 trim 销毁/释放后收敛（100 次烘焙期间在飞剪影与其贴图存活；反向时钟使新条目成 LRU 仍存活）
12. `ClearAll` 同生共死释放 + 两缓存清空

桩能力（新增）：`Sprite.vertices/triangles/uv`、`Texture2D` 像素/Apply/ReadPixels（按左下原点语义）、
`RenderTexture` GetTemporary/ReleaseTemporary/active、`Graphics.Blit`（GPU 侧拷贝绕过可读门）、
`Texture2D.ReadPixelsThrows`、销毁计数与 `Texture2D.ResetCounters` 等。

## 4. 红验证（均先红后恢复绿，同一命令 `run -c Release`）

| 红项 | 手法 | 结果 |
|---|---|---|
| A 键身份 | 注释掉 `SpriteId = sprite.GetInstanceID();` | 27/2：键碰撞 + 姿态切换两条**精确失败** → 证明审查头号缺陷被测试覆盖 |
| B 淘汰保护 | `TrimWhiteSprites(entry)` → `TrimWhiteSprites(null)` | 28/1：`fresh bake survives its own trim` 失败（该测试用反向时钟把新条目变 LRU，缺保护即被自己销毁） |
| C 主路径 | `renderer.sprite = source.sprite;` 临时退回原赋值 | 17/12：**12 条新增全红、17 条基线全绿** → 证明新用例绑定真实烘焙路径，恢复后 29/0 |

## 5. 资源上限与可达性（不把软上限当硬上限）

- 姿态贴图：`rect` 尺寸（native 41×32×4≈5.2KB）× soft cap 64 ≈ 335KB；条目自持 Sprite+Texture2D，淘汰即同毁。
- 图集 alpha：2048² = 4MiB/张，LRU ≤4 张且 ≤16MiB 字节预算，淘汰即丢 `byte[]`（无在飞依赖）。
- soft cap 超限只可能因活跃 renderer 持有：8 槽+body=9 引用/owner；**可证明在实际参数下不可达 64**——残影
  生命周期 2s、采样间隔 40ms，同一时刻被 pin 的不同姿态上限 ≈50（已写入测试注释）。故 `whiten-cache-overflow`
  （>2×）是安全阀，本组测试不可达、未单测；代码保留、日志一次性，绝不销毁在飞对象、绝不返回已销毁 sprite。
- `ClearAll` 与源对象场景：源纹理销毁/指针复用由 `AtlasKey`（指针+尺寸+名 hash）与条目生命周期兜住；
  姿态条目与图集条目无跨 owner 共享写者，无新全局设施。

## 6. 待实机验证（不宣称屏幕可见）

1. 桩不渲染任何像素：**测试只证明烘焙数据与所有权正确，不证明屏幕可见白影**。
2. 实机需看 `whiten-baked` 日志（确认真实 knight sprite 走到 UV 光栅化主路径、打印的 rect/pivot/packed 与
   预期相符）与`[SamuraiDash/…]`异常行；若出现 `whiten-degraded`（如 `missing-or-mismatched-geometry`）需回传。
3. 关键未知：真实游戏 knight sprite 的运行时 `vertices/uv` 是否与夹具的 `uvInferred` 一致（夹具 uv 为推导值）；
   实机日志 + 目视 41×32 脚点/位置不偏移才算通过。
4. 夜间观感（alpha .55→.10 是否偏暗）、密集齐射帧耗时（首次烘焙为一次性 CPU 停顿，2048² 抽 alpha 约 4MB）、
   联机主客一致性仍待验。
5. 主工程编译由 Operator 执行；本 worker 未编译主工程。

## Operator最终补记（优先于上面的未验/计数描述）
- 2.4 interop的Sprite.vertices实际是Vector2[]，worker桩错误为Vector3[]。Operator通过真实主工程CS0029发现，生产与桩/夹具统一为Vector2，实际主构建0W0E。
- 上文“采样率证明>64不可达”错误：多个owner各自持有姿态，不能把单owner采样上限当全局上限。Operator新增130个owner各持不同姿态用例，证明超限仍不销毁在飞Sprite/Texture、overflow只一条，释放后收敛<=64。
- Operator额外对真实native charge 1312个像素与独立凸多边形+固定atlas offset期望逐一比对通过（含边界，不仅margin>=1区域）。最终visual 31/0见operator-visuals.log，旧29/0仅worker交付时状态。
