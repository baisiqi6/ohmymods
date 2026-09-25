# 独立对抗设计审查 — SamuraiDashVisuals packed-sprite 白剪影烘焙

## 裁决：CHANGES_REQUESTED（CPU 逐三角形烘焙方向批准；按下列必改约束修订后即 APPROVE）

依据：通读 `il2cpp/SamuraiDashVisuals.cs` 全文（701 行）+ `native-bamboo-assets.json`（59 条 bamboo sprite 全部 `packed:true`，`rotation:0`，无 pivot 字段）。

---

## 三问

**1. 是否正确？** 方向正确，设计有一处致命遗漏。

- 根因确认成立：`Bakeable` 对 `sprite.packed` 直接 return false → `DegradeWhiten("packed-or-rotated")` → 主功能在 bamboo 岛死。证据链（UnityPy m_RenderDataMap + textureRect≠rect + 旧日志）充分。
- 逐三角形光栅化是唯一无串帧风险的方案：tight 打包允许相邻 sprite 的网格占据本 sprite 原 rect 内的透明角（`knight_charge_bamboo_0` rect 41×32 → textureRect 38.99×25.92，仅裁 2px，角部极可能有邻居），任何"rect 区域整体拷贝"方案都会把邻居像素烘进剪影。UV 插值天然消化 rotation/位移，无需 `packingRotation` 分支。
- **致命遗漏——缓存键碰撞**：`WhiteKey` 取 `sprite.rect`，而 59 条 bamboo pose 的 rect 全部是 `(0,0,41,32)`（证据 JSON 可证），只靠 textureRect 区分。当前键下不同姿态共享同一 key：姿态 A 烘焙成功后，姿态 B 命中 A 的缓存 → **残影八格全是同一姿态**。键必须加 `sprite.GetInstanceID()`（persistent asset，稳定）或 textureRect 四元组。此条不修，方案上线即错。

**2. 是否最优？** 基本最优，附一次结构简化机会。

- 替代方案逐一否决：① rect-on-atlas `Sprite.Create`（textureRect+textureRectOffset 定位）= 邻居串帧 + tight 时属性可能抛；② GPU 白化 = 需自定义 shader（非目标）——Sprites/Default 顶点色是乘法恒等（根因 D 已实证），现有任何内置 shader 无法 RGB→255；③ `_Overlay` 材料路径 = recipe A/B 已实测败因；④ GPU DrawMeshNow+小 RT 读回 = 需自建 Mesh+正交投影矩阵，活动部件多于 ~40 行确定性 CPU 光栅化，且无法脱离 GPU 做夹具测试。CPU 路径胜出。
- **结构简化（必做）**：几何路径对非打包 sprite 同样成立（FullRect 即四角 quad，uv==rect）。统一单路径，删除 `Bakeable` 的 packed 拒绝与 rect 一致性分支，校验改为几何有效性（vertices/triangles 非空、`uv.Length==vertices.Length`、rect≥1、PPU>0）。旧 `TryWhiteTexture` 全图白化路径随之整体废弃。
- **内存账（当前实现的隐藏硬伤，新设计顺带修复）**：`TryWhiteTexture` 建 `new Texture2D(source.width, source.height)` — 对 packed sprite 的 source 是 **2048×2048 atlas**，RGBA32 单份 16MB，`MaxWhiteTextures=16` 上限 256MB。新设计每姿态 41×32×4≈5KB，必须明确：姿态纹理由 `WhiteSpriteEntry` 自持（Sprite+Texture2D 同生共死），atlas 侧只缓存 **alpha 单通道 byte[]**（2048²=4MB/张，LRU ≤4 张 ≤16MB），`Users` 引用计数机制整体删除。

**3. 是否引入新 bug？** 三个具体风险点 + 对 prompt 内两项既有风险说法的核实修正：

- 光栅化细节：退化三角形（面积 ε 跳过）、非有限顶点（整 sprite 降级）、`floor(u*W)` 后 clamp 到 atlas 界内、y 方向不做翻转（GetPixels32/SetPixels32 同为左下原点，一致即对——但必须靠位移夹具实测证明，不能靠推理）。
- atlas 指针复用：alpha 缓存键 = (Pointer, width, height)，建议再加 name hash；sharedassets0 永久资产会话内不卸载，风险低但零成本防。
- 逐帧边界：`sprite.vertices/uv` 经 interop 每次调用都分配托管副本 — 仅在烘焙时读一次，`Pose` 每帧只走缓存命中路径（现状已如此，保持）。
- **修正 prompt 中"Trim 可淘汰刚创建 sprite 返回已 Destroy 对象"的说法**：不成立。`TrimWhiteCaches(entry, texture)` 的 protect 参数 + `TryEvict*` 的 `ReferenceEquals(protect)` 跳过已保护新条目；`WhiteReferenced` 只数 enabled 渲染器，dict 与销毁保持一致（销毁必先移除条目）。真实缺陷是另外三个：键碰撞（上）、全图 16MB 纹理缓存（上）、上限软失效（下）。
- **上限裁定（prompt 的 or 二选一）**：取软上限。缓存满且全部在飞时允许超过 64 继续插入（不跳过缓存——跳过即泄漏或重复烘焙抖动），Trim 延后到在飞引用释放后的下一次 `Whitened` 收敛；姿态纹理 5KB/张，最坏 120 武士×9 槽 ≈ 6MB 有界瞬态，可接受；超 2× 上限时一条一次性日志。绝不销毁在飞对象、绝不返回销毁 sprite（维持现状不变量）。

## 交 worker 的最小约束

1. `WhiteKey` 增加 `sprite.GetInstanceID()`；`Bakeable` 重写为几何校验，删除 packed/rotation 拒绝与 textureRect 读取。
2. 单路径光栅化：输出 rect 原尺寸 RGBA32，像素中心重心坐标，`像素 = vertex*PPU + pivot`，插值 uv 最近邻取 atlas alpha，覆盖内 RGB=255/A=源值，覆盖外 A=0；源零写入。
3. atlas alpha 缓存：CPU `isReadable` 优先 `GetPixels32` 一次，否则沿用现有 `TryWhiteGpu` 的 Blit/ReadPixels+finally 归还模式一次；键含尺寸+name，LRU ≤4。
4. `WhiteSpriteEntry` 自持 Sprite+Texture2D；删除 `WhiteTextureEntry`/`Users`/`ReleaseWhiteTexture` 引用计数链；`DestroyWhiteCache`/软上限 Trim 语义按上文。
5. `DegradeWhiten` 一次性日志语义保留，新增 geometry 类降级原因。
6. 非目标遵守确认：不写源、无自定义 shader/依赖、不动动画与游戏资源。

## 测试（全部必带）

- **真实 packed 夹具**：先补提取 `knight_charge_bamboo_0` 的 `m_Pivot`、vertices/uvs/triangles（当前 JSON 缺这三项，无它们夹具不真实）。
- 位移：atlas 已知坐标放 5×5 实心块，断言输出落点 = 预期 rect 空间位置（证 pivot/y 取向数学）。
- 透明外轮廓：几何外 alpha==0；裁边量与 textureRect−rect 差一致。
- rotation=90 合成夹具：UV 驱动输出仍正立。
- 缓存饱和：64+1 全在飞 → 无销毁对象返回、无重复烘焙错姿态；释放后下次烘焙收敛 ≤64。
- 资源销毁红测：渲染器持有期间强制 Trim，断言 `renderer.sprite` 存活；`ClearAll` 后全毁、无孤儿。
- 非打包 golden 回归：新路径输出与旧 rect 拷贝路径逐像素一致。
- 键碰撞红测：同 rect 不同 textureRect 两姿态 → 两缓存条目、互不串帧（防回归本审查头号缺陷）。
