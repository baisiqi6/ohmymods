结论前先说明证据边界：本审查为静态审查 + 夹具数学校验（本环境无执行工具，31/0 与 0W0E 以 operator 日志为准，未复跑）。

## 独立校验（非复述 operator 结论）

- **夹具自洽性**：NativeUv×2048 与顶点像素坐标逐一还原为恒等映射（v6=(0,0)↔texel(1468,170)=textureRect 左下角；v0=(28,26)↔(1496,196) 等 7 点全对），即 UV 推导与几何/图集偏移一致，夹具非空转。解码 base64 参考首行得 `ff@7、ff@18`，与三角形 (6,5,3)∪(1,3,0) 在 y=0.5 的覆盖区间 [0,~24] 内取 tile 行 0 的两个 ff 位置吻合——独立参考与重心插值实现确属两条推导路径。
- **alpha 方向/UV/旋转**：输出下标 `pixels[y*w+x]`、图集下标 `sy*W+sx`、GetPixels32/ReadPixels 均左下原点，方向一致；旋转打包靠 uv 逐顶点插值自然消化（RotatedSprite 用例锁定）。最近邻用角点原点 `floor(u*W)`，对本夹具是精确恒等；一般情形与 GPU 纹素中心约定差 ≤0.5 texel，属观感级偏差（见 P3-2）。
- **缓存键**：WhiteKey 含 `GetInstanceID`+纹理指针+rect/pivot/PPU/extrude，同 rect 不同姿态不串（测试锁定）；AtlasKey 指针+尺寸+名哈希防指针复用。无反例。
- **资源生命周期**：BakePose 全部失败路径销毁 white；Whitened 失败不留孤儿纹理且负结果缓存（无逐帧重试，测试锁定）；AlphaFromGpu 的 active 恢复/临时 RT 归还/staging 销毁全在 finally，失败路径同样覆盖（测试双路径锁定）；单张超预算图集会被自己刚触发的 Trim 淘汰，账目 `WhiteAtlasBytes` 加减对称。
- **in-flight 保护**：Trim 只淘汰“非 protect 且未被任何 enabled 渲染器引用”的条目；`Owners[id]` 在 Emit 前已写入，WhiteReferenced 能看到本 owner；刚烘焙条目经 `protect` 免于自灭；Pose 赋值发生在 Whitened 返回后、下一次 Whitened 之前，主线程串行无窗口。反时钟 LRU 用例专门验证 protect。未找到可摧毁活体残影的序列。
- **枚举安全**：Trim/WhiteReferenced 的嵌套遍历均为只读；Owners 变异只发生在 Tick 循环外的 Retire 阶段。
- **接口与桩**：桩复刻了关键真实语义（Sprite.Create 归一化 pivot↔像素往返、GetPixels32 返回副本、ReadPixels 左下原点下标、Destroy 计数）；已知桩-真差异（Destroy 同步 vs 延迟、Blit 定向、运行期 uv）均已在待实测清单，不构成 diff 引入缺陷。

## Findings（全部 P3，不阻塞）

1. **P3-1 健壮性** `SamuraiDashVisuals.cs:Whitened`——`new WhiteKey(source)` 在 try 块外读取 `rect/pivot/PPU/extrude/GetInstanceID`；濒死 sprite 的 interop 读失败会逃逸到 Begin/Tick 的粗粒度 handler（整 owner 退役或 5s RetryAt），而非本姿态降级回原色。最小修正：键构造移入 try，失败按 null 返回（后续负缓存路径已就绪）。现网风险低（SourceReady 已门 sprite 非空）。
2. **P3-2 保真注记** 光栅采样为角点原点截断（半纹素偏移）且姿态贴图无 mipmap——与原生渲染相比 alpha 边缘最多 ~0.5px 偏移/缩小时略硬。不要求改，归入实机观感验收。
3. **P3-3 内存小注** `white.Apply(false,false)` 保留 CPU 副本（~5KB/姿态，≤64 条 ≈330KB）；`makeNoLongerReadable:true` 可省，可选。
4. **P3-4 日志小疵** `DegradeWhiten("atlas-alpha-unavailable", null)` 输出 `sprite=null`，图集级失败被记成无 sprite，易误导排障；可把 `source` 透传进 AtlasAlpha。纯日志。
5. **P3-5 性能注记** 病态多姿态场景下逐次 Trim 为 O(条目×owner) 的渲染器 interop 读（130-owner 用例即此形态）；真实姿态跨 owner 共享，条目数远低于 64，不接受改。

未发现 P1/P2。范围纪律核过：源 sprite/纹理/材质/属性块零写入（仅读），无新素材/无新 shader（仅 Shader.Find 既有三级链，前审已批），运动文件未触碰。

## 裁决

**APPROVE**（附上述 5 条 P3，均不要求本次返工；P3-1 可与后续任意改动脉冲合并处理）。

## 实机待验（独立保留，不因本 APPROVE 消除）

1. 真实运行期 `sprite.uv/vertices/triangles` 与序列化推导值的吻合（fixture 的 uv 为 uvTransform 推导，非运行期捕获）。
2. 不可读图集走 GPU 回读：Blit 方向、压缩纹理 alpha 保真、首烘卡顿幅度。
3. 屏幕可见性：8 槽白剪影/2s 淡出/姿态各异/flip 与排序层级观感。
4. 真实 LogOutput.log：`whiten-baked` 一条（packed=True），正常游玩无 `whiten-degraded`/`whiten-cache-overflow`。
5. 联机边界未验（本 diff 无同步面，但不因此宣称联机安全）。
