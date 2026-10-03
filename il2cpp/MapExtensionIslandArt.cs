using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 扩展岛岸线窄 art 服务（唯一新增自有 PNG：EmbeddedResource
    /// KingdomEnhancedMod.KEM_MapExtensionIsland.png，Root 提供、原字节不可变）。
    ///
    /// 冷启动一次像素准备（业务算法在 C#，不在测试/工具里）：
    /// 1) 读入源 alpha，取**最大 4-连通岛面**（排除零星外点/噪点）；
    /// 2) 用该岛面 bbox 的实际宽高比，等比 nearest 缩放到目标逻辑宽（默认 224，不固定 2.4、不 sx!=sy）；
    /// 3) 6 档灰调 + binary alpha（同一 clean alpha 生成 outline，1px 外环、同画布/pivot/PPU、
    ///    透明 padding 不截边）；另存岸内 mask 供布局终检；
    /// 4) 不逐帧 decode/扫描；PNG 原 bytes 只读不写；不改 native tint/reveal/color。
    ///
    /// 租约（owner/代际身份，不只是“共享 sprite 相等”）：
    /// - 每个被借 Image 保存 exact Image 引用 + 原 sprite 快照 + 本租约写入的 sprite + **exact owner 身份**；
    /// - 重复 bind：同 Image 同 owner 只更新应用目标；同 Image 换 owner 建立新租约（不覆盖原快照）；
    /// - 释放按 owner 范围：旧 owner 清理不得撤新 owner 的图；归还仅在「Image 仍存活 且 仍等于本租约
    ///   写入的 sprite」时恢复，外部换 sprite / dead wrapper / 读写异常 → 保留恢复责任并跳过破坏性写入；
    /// - 只有归还完成后才 Release 自有 Sprite/Texture；绝不 Destroy 原生 Sprite。
    /// </summary>
    internal static class MapExtensionIslandArt
    {
        internal const string ResourceName = "KingdomEnhancedMod.KEM_MapExtensionIsland.png";
        private const long MaxResourceBytes = 8L * 1024 * 1024;
        private const float PixelsPerUnit = 32f;
        internal const int TargetLogicalWidth = 224;
        internal const int ToneLevels = 6;
        internal const byte AlphaThreshold = 24;

        /// <summary>
        /// 6 档灰度调色板（nearest 映射，不用 floor 压暗）：
        /// native 8723 主材统计 = 227 plateau / 245 浅岸 / 189 cliff / 255 高亮（Root 只读统计），
        /// 这三（四）个主调必须保持；另补 2 个新暗 cliff 档（116/155）承载新 PNG 的暗部细节。
        /// 映射取最近档：254→255、227→227、245→245、189→189（native 锚点无损）。
        /// </summary>
        internal static readonly byte[] TonePalette = { 116, 155, 189, 227, 245, 255 };

        /// <summary>最近档映射（|v−p| 最小；平手取较暗档，确定性）。</summary>
        private static int NearestToneIndex(int gray)
        {
            int best = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < TonePalette.Length; i++)
            {
                int distance = Math.Abs(gray - TonePalette[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        private static byte NearestTone(int gray) => TonePalette[NearestToneIndex(gray)];

        // ------------------------------------------------------------------ 像素准备（纯函数，可离线测试）

        internal sealed class ShorePrep
        {
            internal int SrcWidth;
            internal int SrcHeight;
            internal int BboxX;
            internal int BboxY;
            internal int BboxWidth;
            internal int BboxHeight;
            internal float Aspect;          // bboxWidth / bboxHeight（真实测出，不假定 2.4）
            internal int Width;             // 缩放后画布宽（含 padding）
            internal int Height;
            internal Color32[] Shore;       // 6 档灰 + binary alpha
            internal Color32[] Outline;     // 同 clean alpha 的 1px 外环
            internal bool[] Mask;           // 岸内（binary alpha>0）
            internal int IslandPixels;      // 最大连通岛面像素数
            internal int ComponentCount;
            internal int GrayMin;
            internal int GrayMax;
            internal int LevelsUsed;
        }

        /// <summary>
        /// 冷启动像素准备：最大连通岛面 → bbox 比例 → 等比 nearest → 6 档灰 + binary alpha → outline/mask。
        /// 纯函数：不触碰 Unity 对象；PNG 字节只读。
        /// </summary>
        internal static ShorePrep BuildPrep(Color32[] src, int width, int height, int targetWidth)
        {
            if (src == null || width <= 0 || height <= 0 || src.Length < width * height) return null;
            if (targetWidth < 8) targetWidth = 8;

            int total = width * height;
            var alpha = new bool[total];
            for (int i = 0; i < total; i++) alpha[i] = src[i].a > AlphaThreshold;

            // 最大 4-连通岛面（两遍 BFS；显式栈避免递归）
            var label = new int[total];
            int components = 0;
            int bestLabel = 0;
            int bestCount = 0;
            var queue = new int[total];
            for (int start = 0; start < total; start++)
            {
                if (!alpha[start] || label[start] != 0) continue;
                components++;
                int head = 0;
                int tail = 0;
                queue[tail++] = start;
                label[start] = components;
                int count = 0;
                while (head < tail)
                {
                    int idx = queue[head++];
                    count++;
                    int x = idx % width;
                    int y = idx / width;
                    if (x > 0) TryPush(alpha, label, queue, ref tail, idx - 1, components);
                    if (x < width - 1) TryPush(alpha, label, queue, ref tail, idx + 1, components);
                    if (y > 0) TryPush(alpha, label, queue, ref tail, idx - width, components);
                    if (y < height - 1) TryPush(alpha, label, queue, ref tail, idx + width, components);
                }
                if (count > bestCount) { bestCount = count; bestLabel = components; }
            }
            if (bestLabel == 0) return null;

            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (label[y * width + x] != bestLabel) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            int bboxW = maxX - minX + 1;
            int bboxH = maxY - minY + 1;
            if (bboxW <= 0 || bboxH <= 0) return null;

            float scale = targetWidth / (float)bboxW;                 // 等比：宽高同倍率
            int outH = Math.Max(1, (int)Math.Round(bboxH * scale));
            int pad = 2;                                              // 透明 padding：外环不截边
            int outW = targetWidth + pad * 2;
            outH += pad * 2;

            var shore = new Color32[outW * outH];
            var mask = new bool[outW * outH];
            int grayMin = 255;
            int grayMax = 0;
            var used = new bool[ToneLevels];
            int islandPixels = 0;
            for (int oy = 0; oy < outH - pad * 2; oy++)
            {
                for (int ox = 0; ox < outW - pad * 2; ox++)
                {
                    int sx = minX + (int)Math.Floor((ox + 0.5f) / scale);   // nearest
                    int sy = minY + (int)Math.Floor((oy + 0.5f) / scale);
                    if (sx > maxX) sx = maxX;
                    if (sy > maxY) sy = maxY;
                    if (label[sy * width + sx] != bestLabel) continue;
                    Color32 pixel = src[sy * width + sx];
                    int gray = (int)(0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b);
                    if (gray < grayMin) grayMin = gray;
                    if (gray > grayMax) grayMax = gray;
                    int level = NearestToneIndex(gray);
                    used[level] = true;
                    byte tone = TonePalette[level];
                    int dst = (oy + pad) * outW + (ox + pad);
                    shore[dst] = new Color32(tone, tone, tone, 255);      // binary alpha：形状内完全不透明
                    mask[dst] = true;
                    islandPixels++;
                }
            }
            if (islandPixels == 0) return null;

            var outline = new Color32[outW * outH];
            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    int idx = y * outW + x;
                    if (mask[idx]) continue;
                    int neighbour = -1;
                    if (x > 0 && mask[idx - 1]) neighbour = idx - 1;
                    else if (x < outW - 1 && mask[idx + 1]) neighbour = idx + 1;
                    else if (y > 0 && mask[idx - outW]) neighbour = idx - outW;
                    else if (y < outH - 1 && mask[idx + outW]) neighbour = idx + outW;
                    if (neighbour < 0) continue;
                    outline[idx] = new Color32(255, 255, 255, 255);       // native 7953 白色描边语义（1 logical px 外环）
                }
            }

            int levelsUsed = 0;
            for (int i = 0; i < ToneLevels; i++) if (used[i]) levelsUsed++;

            return new ShorePrep
            {
                SrcWidth = width,
                SrcHeight = height,
                BboxX = minX,
                BboxY = minY,
                BboxWidth = bboxW,
                BboxHeight = bboxH,
                Aspect = bboxW / (float)bboxH,
                Width = outW,
                Height = outH,
                Shore = shore,
                Outline = outline,
                Mask = mask,
                IslandPixels = islandPixels,
                ComponentCount = components,
                GrayMin = grayMin,
                GrayMax = grayMax,
                LevelsUsed = levelsUsed,
            };
        }

        private static void TryPush(bool[] alpha, int[] label, int[] queue, ref int tail, int idx, int tag)
        {
            if (!alpha[idx] || label[idx] != 0) return;
            label[idx] = tag;
            queue[tail++] = idx;
        }

        // ------------------------------------------------------------------ 运行时（Unity）

        private static Texture2D _shoreTex;
        private static Texture2D _outlineTex;
        private static Sprite _shore;
        private static Sprite _outline;
        private static bool _attempted;
        private static ShorePrep _prep;

        internal static bool Ready { get { return _shore != null && _outline != null; } }

        internal static Sprite Shore { get { return _shore; } }

        internal static Sprite Outline { get { return _outline; } }

        /// <summary>冷启动一次：解码 PNG → BuildPrep → 建 shore/outline Sprite（同画布/pivot/PPU）。</summary>
        internal static bool TryEnsure()
        {
            if (Ready) return true;
            if (_attempted) return false;
            _attempted = true;
            Texture2D rawTex = null;
            int rawTextureWidth = 0;
            int rawTextureHeight = 0;
            Texture2D shoreTex = null;
            Texture2D outlineTex = null;
            Sprite shore = null;
            Sprite outline = null;
            try
            {
                byte[] bytes = ReadResource();
                if (bytes == null) return false;

                rawTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(rawTex, bytes, false))
                {
                    Warn("shore LoadImage failed");
                    return false;
                }
                rawTextureWidth = rawTex.width;
                rawTextureHeight = rawTex.height;
                Color32[] rawPixels;
                try { rawPixels = rawTex.GetPixels32(); }
                finally { DestroyQuietly(rawTex); rawTex = null; }     // raw 只用于取像素，立即回收
                ShorePrep prep = BuildPrep(rawPixels, rawTextureWidth, rawTextureHeight, TargetLogicalWidth);
                if (prep == null)
                {
                    Warn("shore prep failed " + rawTextureWidth + "x" + rawTextureHeight);
                    return false;
                }

                shoreTex = new Texture2D(prep.Width, prep.Height, TextureFormat.RGBA32, false);
                shoreTex.SetPixels32(prep.Shore);
                ApplyTexturePolicy(shoreTex);
                shoreTex.Apply(false, false);

                outlineTex = new Texture2D(prep.Width, prep.Height, TextureFormat.RGBA32, false);
                outlineTex.SetPixels32(prep.Outline);
                ApplyTexturePolicy(outlineTex);
                outlineTex.Apply(false, false);

                var rect = new Rect(0f, 0f, prep.Width, prep.Height);
                var pivot = new Vector2(0.5f, 0.5f);
                shore = Sprite.Create(shoreTex, rect, pivot, PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                outline = Sprite.Create(outlineTex, rect, pivot, PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                if (shore == null || outline == null)
                {
                    Warn("shore Sprite.Create failed");
                    return false;
                }

                _shoreTex = shoreTex;
                _outlineTex = outlineTex;
                _shore = shore;
                _outline = outline;
                _prep = prep;
                Info("shore ready canvas=" + prep.Width + "x" + prep.Height + " bbox=" + prep.BboxWidth + "x" +
                     prep.BboxHeight + " aspect=" + prep.Aspect.ToString("0.###") +
                     " levels=" + prep.LevelsUsed + " island=" + prep.IslandPixels +
                     " comps=" + prep.ComponentCount);
                return true;
            }
            catch (Exception e)
            {
                Warn("shore decode failed: " + e.GetType().Name);
                return false;
            }
            finally
            {
                DestroyQuietly(rawTex);          // 任何路径都不得遗留 raw 临时贴图
                if (!Ready)
                {
                    DestroyQuietly(outline);
                    DestroyQuietly(shore);
                    DestroyQuietly(outlineTex);
                    DestroyQuietly(shoreTex);
                }
            }
        }

        /// <summary>供布局终检：岸内 mask 与画布/bbox 事实（准备完成前返回 null）。</summary>
        internal static ShorePrep Prep { get { return _prep; } }

        internal static bool IsInsideShore(float canvasX, float canvasY)   // 画布左下角原点、像素单位
        {
            ShorePrep prep = _prep;
            if (prep == null) return false;
            int x = (int)canvasX;
            int y = (int)canvasY;
            if (x < 0 || y < 0 || x >= prep.Width || y >= prep.Height) return false;
            return prep.Mask[y * prep.Width + x];
        }

        /// <summary>归还全部租约 → 再销毁自有 Sprite/Texture（顺序固定；原生 Sprite 永不 Destroy）。</summary>
        internal static void Reset()
        {
            ReleaseAllBorrowed();
            if (PendingRestores > 0)
            {
                // 仍有 Image 需要恢复（外部替换/写异常）：绝不销毁仍被使用的自有资源；下次 Reset 重试
                Warn("reset deferred: " + PendingRestores + " restore(s) pending; shore cache kept");
                return;
            }
            DestroyOwnCache();
        }

        /// <summary>
        /// 没有未完成责任（无租约、无 pending；dead wrapper 先 prune）时才销毁自有缓存；
        /// 否则保留——绝不销毁可能仍被 Image 引用的 Sprite/Texture（多 owner 场景下的 scope 化收尾）。
        /// </summary>
        internal static bool DestroyCacheIfUnused()
        {
            PruneDeadLeases();
            if (Leases.Count > 0 || PendingRestores > 0) return false;
            DestroyOwnCache();
            return true;
        }

        private static void DestroyOwnCache()
        {
            DestroyQuietly(_shore);
            DestroyQuietly(_outline);
            _shore = null;
            _outline = null;
            DestroyQuietly(_shoreTex);
            DestroyQuietly(_outlineTex);
            _shoreTex = null;
            _outlineTex = null;
            _prep = null;
            _attempted = false;
        }

        // ------------------------------------------------------------------ owner/代际租约

        internal sealed class BorrowedImage
        {
            internal Image Image;
            internal Sprite OriginalSprite;
            internal Sprite AppliedSprite;
            internal object Owner;               // exact owner 身份（menu/map/campaign/scene 由调用方给同一引用）
            internal int Generation;             // 同一 Image 的代际门：只有最新租约可写/可撤
            internal bool Superseded;            // 被更新 owner 接管：责任由新租约继承，旧租约注销
            internal bool WriteFailed;           // 归还写失败：责任保留（与"外部已替换"区分）
            internal bool RestorePending = true; // 归还责任：只有成功恢复/确认无需求才清零
            /// <summary>本模块可能写入过、但未确认的 sprite（写后抛+读回 unknown；接管被拒时记入旧租约）。
            /// 归还时 current 落在其中视同“我方持有”，必须恢复原值，绝不误判为外部替换。</summary>
            internal readonly List<Sprite> HeldSprites = new List<Sprite>(4);
            internal readonly List<object> RetiredOwners = new List<object>(4);   // 有界：已被接管过的 owner

            internal bool Alive { get { return Image != null; } }

            /// <summary>读回当前 sprite；读异常 = unknown（调用方必须保留归还责任，不得当“外部已替换”）。</summary>
            internal bool TryReadCurrent(out Sprite current)
            {
                current = null;
                if (Image == null) return false;
                try { current = Image.sprite; return true; }
                catch (Exception) { return false; }
            }

            /// <summary>记录“可能已写入 own 但无法确认”（setter 写后抛 + 读回 unknown）：保留原值与恢复责任。</summary>
            internal void NotePossibleWrite(Sprite own)
            {
                WriteFailed = true;
                RestorePending = true;
                if (CompareNativeSprite(own, AppliedSprite) == IdentityMatch.Same) return;
                for (int i = 0; i < HeldSprites.Count; i++)
                {
                    if (CompareNativeSprite(HeldSprites[i], own) == IdentityMatch.Same) return;
                }
                if (HeldSprites.Count < 4) HeldSprites.Add(own);
            }

            /// <summary>current 与“我方持有”集合（Applied/可能写入值）的关系；Unknown = 无法裁决（必须保留责任）。</summary>
            internal IdentityMatch CompareHeld(Sprite current)
            {
                IdentityMatch applied = CompareNativeSprite(current, AppliedSprite);
                if (applied == IdentityMatch.Same || applied == IdentityMatch.Unknown) return applied;
                for (int i = 0; i < HeldSprites.Count; i++)
                {
                    IdentityMatch match = CompareNativeSprite(HeldSprites[i], current);
                    if (match == IdentityMatch.Same || match == IdentityMatch.Unknown) return match;
                }
                return IdentityMatch.Different;
            }

            internal bool Apply(Sprite own)
            {
                if (Image == null || own == null) return false;
                try
                {
                    Image.sprite = own;
                    AppliedSprite = own;
                    return true;
                }
                catch (Exception) { return false; }
            }

            /// <summary>
            /// 归还：读故障 / 无原值快照 = unknown → 保留责任（绝不当“外部已替换”注销）；
            /// 仅确证仍持有本租约写入值时才写回原值；确证外部已替换（读到不同的值）才收尾。
            /// </summary>
            internal bool Release()
            {
                if (Image == null) { RestorePending = false; return true; }
                if (Superseded) { RestorePending = false; return true; }        // 责任已由新租约继承
                if (OriginalSprite == null) { WriteFailed = false; RestorePending = true; return false; }  // 读不到原值：不写、留责任
                if (!TryReadCurrent(out Sprite current)) { WriteFailed = false; RestorePending = true; return false; }  // unknown：留责任
                IdentityMatch held = CompareHeld(current);
                if (held == IdentityMatch.Unknown) { WriteFailed = false; RestorePending = true; return false; }  // 无法裁决：留责任
                if (held == IdentityMatch.Different) { RestorePending = false; WriteFailed = false; return true; }  // 确证外部已替换：收尾、不覆盖
                try
                {
                    Image.sprite = OriginalSprite;
                    AppliedSprite = null;
                    RestorePending = false;
                    WriteFailed = false;
                    return true;
                }
                catch (Exception) { WriteFailed = true; RestorePending = true; return false; }
            }
        }

        private static readonly List<BorrowedImage> Leases = new List<BorrowedImage>(8);
        private static int _generation;

        internal static int LeaseCount { get { return Leases.Count; } }

        internal enum IdentityMatch
        {
            Same = 0,
            Different = 1,
            Unknown = 2,
        }

        /// <summary>
        /// 原生 Unity 身份（项目既有方式：nativePointer + InstanceID；绝不把 managed wrapper 引用当身份）。
        /// 两条身份都读不到 = Unknown（调用方 fail-closed，不建新租约）。
        /// </summary>
        internal static IdentityMatch CompareNativeImage(Image a, Image b)
        {
            if (a == null || b == null) return IdentityMatch.Different;
            try
            {
                IntPtr pa = a.Pointer;
                IntPtr pb = b.Pointer;
                if (pa != IntPtr.Zero && pb != IntPtr.Zero)
                {
                    return pa == pb ? IdentityMatch.Same : IdentityMatch.Different;
                }
            }
            catch (Exception) { return IdentityMatch.Unknown; }
            try
            {
                int ia = a.GetInstanceID();
                int ib = b.GetInstanceID();
                if (ia != 0 && ib != 0) return ia == ib ? IdentityMatch.Same : IdentityMatch.Different;
                return IdentityMatch.Unknown;
            }
            catch (Exception) { return IdentityMatch.Unknown; }
        }

        /// <summary>
        /// 原生 Sprite 身份（同 Image：Pointer + InstanceID）。Image.sprite getter 可能返回“同 native、
        /// 不同 managed wrapper”的新实例（Il2CppObjectPool），sprite 比较必须按 native 身份，
        /// 否则会把仍由本模块写入/持有的 sprite 误判为“外部已替换”。
        /// </summary>
        internal static IdentityMatch CompareNativeSprite(Sprite a, Sprite b)
        {
            if (a == null || b == null) return IdentityMatch.Different;
            try
            {
                IntPtr pa = a.Pointer;
                IntPtr pb = b.Pointer;
                if (pa != IntPtr.Zero && pb != IntPtr.Zero)
                {
                    return pa == pb ? IdentityMatch.Same : IdentityMatch.Different;
                }
            }
            catch (Exception) { return IdentityMatch.Unknown; }
            try
            {
                int ia = a.GetInstanceID();
                int ib = b.GetInstanceID();
                if (ia != 0 && ib != 0) return ia == ib ? IdentityMatch.Same : IdentityMatch.Different;
                return IdentityMatch.Unknown;
            }
            catch (Exception) { return IdentityMatch.Unknown; }
        }

        /// <summary>prune：Unity 已销毁（== null）的 Image 无恢复责任，直接注销；live wrapper 一律保留。</summary>
        private static void PruneDeadLeases()
        {
            for (int i = Leases.Count - 1; i >= 0; i--)
            {
                BorrowedImage lease = Leases[i];
                if (lease.Image == null)
                {
                    lease.RestorePending = false;
                    Leases.RemoveAt(i);
                }
            }
        }

        /// <summary>O(1) 绑定校验：该 Image（按原生身份）当前仍由本租约写入 own（sprite 按原生身份比较，
        /// getter 返回同 native 新 wrapper 也算命中）；读故障/未绑定 → false。</summary>
        internal static bool IsApplied(Image image, Sprite own)
        {
            if (image == null || own == null) return false;
            for (int i = 0; i < Leases.Count; i++)
            {
                BorrowedImage lease = Leases[i];
                if (CompareNativeImage(lease.Image, image) != IdentityMatch.Same) continue;
                if (CompareNativeSprite(lease.AppliedSprite, own) != IdentityMatch.Same) return false;
                return lease.TryReadCurrent(out Sprite current) &&
                    CompareNativeSprite(current, own) == IdentityMatch.Same;
            }
            return false;
        }

        internal static BorrowedImage TryBorrow(Image image, Sprite own, object owner)
        {
            if (image == null || own == null) return null;
            PruneDeadLeases();

            Sprite trueOriginal = null;                 // 同一原生 Image 的**首个**快照才是原值
            bool hasTrueOriginal = false;
            BorrowedImage latest = null;
            var matching = new List<BorrowedImage>(4);
            for (int i = 0; i < Leases.Count; i++)
            {
                BorrowedImage existing = Leases[i];
                IdentityMatch match = CompareNativeImage(existing.Image, image);
                if (match == IdentityMatch.Unknown)
                {
                    // 无法排除“同一原生 Image”：fail-closed 拒绝本次绑定
                    // （尤其不得把本模块写入的 sprite 当成第二份原值快照）。
                    if (!existing.Superseded) return null;
                    continue;
                }
                if (match != IdentityMatch.Same) continue;
                matching.Add(existing);
                if (!hasTrueOriginal) { trueOriginal = existing.OriginalSprite; hasTrueOriginal = true; }
                if (latest == null || existing.Generation > latest.Generation) latest = existing;
            }

            if (latest != null && ReferenceEquals(latest.Owner, owner))
            {
                if (latest.Apply(own)) return latest;     // 同 owner 重 bind：只更新应用目标
                // 写失败不谎报成功；读回同步记账（setter 若“写后抛”，Image 实际已是新值）
                if (latest.TryReadCurrent(out Sprite after)) latest.AppliedSprite = after;
                else latest.NotePossibleWrite(own);       // 写后抛 + 读回 unknown：登记“可能已写入”责任
                return null;
            }
            if (latest != null)
            {
                for (int i = 0; i < latest.RetiredOwners.Count; i++)
                {
                    if (ReferenceEquals(latest.RetiredOwners[i], owner)) return null;   // 旧 owner 迟到 bind：代际门拒绝
                }
            }

            Sprite original;
            if (hasTrueOriginal) original = trueOriginal;
            else if (!TryReadSprite(image, out original)) return null;   // 读不到原值：不建成功租约、不写

            // 事务式接管：先写（不改变任何 lease 状态）；失败时读回判定——未生效保留一切，未知保留责任
            bool writeOk = TryApply(image, own);
            if (!writeOk)
            {
                Sprite current;
                if (!TryReadSprite(image, out current))
                {
                    // setter 写后抛 + 读回 unknown：无法裁决是否已写入 own。
                    // 不能把“证明不了写成功”当作“证明未写入” —— 保留原值与“可能已写入”的责任。
                    return KeepUnknownWrite(latest, image, original, own, owner);
                }
                IdentityMatch written = CompareNativeSprite(current, own);
                if (written == IdentityMatch.Unknown)
                {
                    return KeepUnknownWrite(latest, image, original, own, owner);
                }
                if (written == IdentityMatch.Different)
                {
                    return null;   // 写前抛（未生效）：旧租约的 snapshot/hold/pending 原样保留
                }
                // “写后抛”且读回确证 = own：写入已生效 —— 责任必须由本租约承担（否则下轮把自己的 sprite 捕成原值）
            }

            // 确认写入后才提交接管：注销同原生 Image 的旧租约（有界记录被接管过的 owner）
            var retired = new List<object>(4);
            for (int i = matching.Count - 1; i >= 0; i--)
            {
                BorrowedImage old = matching[i];
                if (retired.Count < 4 && !ReferenceEquals(old.Owner, owner) && !ContainsRef(retired, old.Owner))
                {
                    retired.Add(old.Owner);
                }
                old.Superseded = true;
                old.RestorePending = false;         // 责任由新租约继承原值
                Leases.Remove(old);
            }
            var lease = new BorrowedImage
            {
                Image = image,
                OriginalSprite = original,          // 同一 Image 只认首个快照（不把 own sprite 当原值）
                Owner = owner,
                Generation = ++_generation,
                AppliedSprite = own,
            };
            if (!writeOk) { lease.WriteFailed = true; lease.RestorePending = true; }   // 写异常但已生效：责任保留
            if (latest != null)
            {
                for (int i = 0; i < latest.RetiredOwners.Count && lease.RetiredOwners.Count < 4; i++)
                {
                    if (!ContainsRef(lease.RetiredOwners, latest.RetiredOwners[i])) lease.RetiredOwners.Add(latest.RetiredOwners[i]);
                }
            }
            for (int i = 0; i < retired.Count; i++) lease.RetiredOwners.Add(retired[i]);
            Leases.Add(lease);
            return writeOk ? lease : null;          // 写异常：不谎报成功（状态已正确收敛到本租约）
        }

        private static bool TryApply(Image image, Sprite own)
        {
            try { image.sprite = own; return true; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 写后抛 + 读回 unknown 的责任保留：
        /// - 已有旧租约（含接管场景）：旧租约保持 snapshot/hold，登记“可能写入”值；
        /// - 首绑无旧租约：新建租约承担原值与恢复责任（绝不因为“证明不了写成功”就丢责任）。
        /// 两种情况都不谎报成功（返回 null）。
        /// </summary>
        private static BorrowedImage KeepUnknownWrite(BorrowedImage latest, Image image, Sprite original,
            Sprite own, object owner)
        {
            if (latest != null)
            {
                latest.NotePossibleWrite(own);
                return null;
            }
            var lease = new BorrowedImage
            {
                Image = image,
                OriginalSprite = original,
                Owner = owner,
                Generation = ++_generation,
                AppliedSprite = own,
                WriteFailed = true,
                RestorePending = true,
            };
            Leases.Add(lease);
            return null;
        }

        /// <summary>owner 范围释放：只归还属于该 exact owner 的租约（旧 owner cleanup 不动新 owner 的图）。</summary>
        internal static void ReleaseOwner(object owner)
        {
            for (int i = Leases.Count - 1; i >= 0; i--)
            {
                BorrowedImage lease = Leases[i];
                if (!ReferenceEquals(lease.Owner, owner)) continue;
                LeaseReleaseAttempt(lease, i);
            }
        }

        /// <summary>归还单个 Image 的所有租约（instance 换代）；按原生身份匹配（同 native 不同 wrapper alias
        /// 也能释放）；Unknown 保留责任、绝不冒险注销；保留资源本身。</summary>
        internal static void ReleaseBorrowed(Image image)
        {
            if (image == null) return;
            for (int i = Leases.Count - 1; i >= 0; i--)
            {
                IdentityMatch match = CompareNativeImage(Leases[i].Image, image);
                if (match != IdentityMatch.Same) continue;
                LeaseReleaseAttempt(Leases[i], i);
            }
        }

        internal static void ReleaseAllBorrowed()
        {
            for (int i = Leases.Count - 1; i >= 0; i--) LeaseReleaseAttempt(Leases[i], i);
        }

        /// <summary>
        /// 该 exact owner 是否仍有未完成归还责任（租约仍未成功释放/仍待恢复）：调用方（MapMountIcons 的
        /// shore 收尾）据此**保留 captured cleanup 身份**做有界重试，而不是丢掉责任依赖后续全局 Reset。
        /// 只读、有界（Leases 数量小）；ReleaseOwner 之后仍留在列表里的即未完成。
        /// </summary>
        internal static bool HasOutstanding(object owner)
        {
            if (owner == null) return false;
            for (int i = 0; i < Leases.Count; i++)
            {
                if (ReferenceEquals(Leases[i].Owner, owner)) return true;
            }
            return false;
        }

        private static void LeaseReleaseAttempt(BorrowedImage lease, int index)
        {
            if (lease.Release())
            {
                Leases.RemoveAt(index);
                return;
            }
            if (!lease.Alive || lease.Superseded)
            {
                lease.RestorePending = false;      // dead/已被接管：注销，无遗留责任
                Leases.RemoveAt(index);
                return;
            }
            if (lease.WriteFailed)
            {
                return;                            // 写失败：保留责任，下次重试（资源不得销毁）
            }
            // 其余：读不到原值 → 保留责任（不写、不丢）
        }

        internal static int PendingRestores
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Leases.Count; i++) if (Leases[i].RestorePending) count++;
                return count;
            }
        }

        private static bool ContainsRef(List<object> list, object item)
        {
            for (int i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], item)) return true;
            return false;
        }

        private static bool TryReadSprite(Image image, out Sprite sprite)
        {
            sprite = null;
            try { sprite = image.sprite; return true; }
            catch (Exception) { return false; }     // 读异常：不建成功租约，避免把未知原值当 null 覆写
        }

        // ------------------------------------------------------------------ 内部

        internal static byte[] ReadResourceBytes() => ReadResource();

        private static void ApplyTexturePolicy(Texture2D tex)
        {
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 0;
        }

        private static byte[] ReadResource()
        {
            try
            {
                Assembly assembly = typeof(MapExtensionIslandArt).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        Warn("shore resource missing: " + ResourceName);
                        return null;
                    }
                    long length = stream.Length;
                    if (length <= 0 || length > MaxResourceBytes)
                    {
                        Warn("shore resource size rejected: " + length + " bytes");
                        return null;
                    }
                    byte[] bytes = new byte[(int)length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int step = stream.Read(bytes, read, bytes.Length - read);
                        if (step <= 0) break;
                        read += step;
                    }
                    if (read != bytes.Length)
                    {
                        Warn("shore resource truncated " + read + "/" + bytes.Length);
                        return null;
                    }
                    return bytes;
                }
            }
            catch (Exception e)
            {
                Warn("shore resource read failed: " + e.GetType().Name);
                return null;
            }
        }

        private static void DestroyQuietly(UnityEngine.Object obj)
        {
            if (obj == null) return;
            try { UnityEngine.Object.Destroy(obj); }
            catch (Exception) { }
        }

        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static void Warn(string message)
        {
            try
            {
                if (!Warned.Add(message) || Warned.Count > 32) return;
                MapIconSources.Log.Warn("[MapExtensionIslandArt] " + message);
            }
            catch (Exception) { }
        }

        private static void Info(string message)
        {
            try { MapIconSources.Log.Info("[MapExtensionIslandArt] " + message); }
            catch (Exception) { }
        }
    }
}
