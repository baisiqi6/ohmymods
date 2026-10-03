using System;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 自有地图图标资产（user-approved 3/4/38 补齐）。**只在原生源确证缺失时**由
    /// MapIconSources.Resolve 调用；其余缺源仍走原生缺口日志。
    ///
    /// 设计约束（r6）：
    /// - 3 张 PNG 为 EmbeddedResource（LogicalName = KingdomEnhancedMod.KEM_Map*.png），
    ///   读取/解码复用 B 源同款 API：GetManifestResourceStream → ImageConversion.LoadImage →
    ///   Texture2D(Point/Clamp) → Sprite.Create（每张 atlas 两个 Sprite：normal / locked）；
    /// - 源对象 = **clone 原生 Steed 模板**（MapCustomIconCatalog.TemplateSelection=5，确证存在）
    ///   到自有隐藏根 `KEM_MapIconSources`（该名字已在 CollectNativeBoxes 的排除表内），
    ///   只改 clone 的 sprites/typeSelection/icon/rect —— 原生模板与全局状态一律不写、不销毁；
    /// - 状态由原生 `UIMapIcon.UpdateIcon(reign, land)` 驱动（normal/locked 两帧），
    ///   不新增任何 Harmony hook；
    /// - 缓存恰好 3 source + 3 texture + 6 sprite；Reset 只销毁自有对象（先由调用方销毁 Views）。
    /// </summary>
    internal static class MapCustomIconAssets
    {
        /// <summary>自有隐藏源根名（CollectNativeBoxes/IsUnderOwnHolder 已排除该名，不会被当成原生遮挡）。</summary>
        internal const string SourceRootName = "KEM_MapIconSources";
        private const long MaxResourceBytes = 4L * 1024 * 1024;

        private static readonly UIMapIcon[] Sources = new UIMapIcon[8];
        private static readonly bool[] Attempted = new bool[8];
        private static Texture2D[] Textures = new Texture2D[8];
        private static Sprite[] Sprites = new Sprite[8 * 2];
        private static GameObject _root;

        internal static int CachedSourceCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Sources.Length; i++) if (Sources[i] != null) n++;
                return n;
            }
        }

        /// <summary>取自有源（缺失/失败返回 null；同一 (iconType,selection) 只构建一次）。</summary>
        internal static UIMapIcon Resolve(int iconType, int selection)
        {
            if (!MapCustomIconCatalog.TryGet(iconType, selection, out MapCustomIconDef def)) return null;
            int slot = Slot(selection);
            if (slot < 0) return null;
            if (Sources[slot] != null) return Sources[slot];
            if (Attempted[slot]) return null;
            Attempted[slot] = true;

            UIMapIcon built = Build(def, slot);
            Sources[slot] = built;
            return built;
        }

        /// <summary>
        /// 归还自有 source/assets（调用方必须**先**销毁使用它们的 Views）。
        /// 只销毁自有对象：原生模板从不持有、从不销毁；不注册 DontDestroyOnLoad。
        /// </summary>
        internal static void Reset()
        {
            for (int i = 0; i < Sources.Length; i++)
            {
                if (Sources[i] != null)
                {
                    try
                    {
                        GameObject go = Sources[i].gameObject;
                        if (go != null) UnityEngine.Object.Destroy(go);
                    }
                    catch (Exception) { }
                    Sources[i] = null;
                }
                Attempted[i] = false;
            }
            for (int i = 0; i < Sprites.Length; i++)
            {
                if (Sprites[i] == null) continue;
                try { UnityEngine.Object.Destroy(Sprites[i]); } catch (Exception) { }
                Sprites[i] = null;
            }
            for (int i = 0; i < Textures.Length; i++)
            {
                if (Textures[i] == null) continue;
                try { UnityEngine.Object.Destroy(Textures[i]); } catch (Exception) { }
                Textures[i] = null;
            }
            if (_root != null)
            {
                try { UnityEngine.Object.Destroy(_root); } catch (Exception) { }
                _root = null;
            }
        }

        // ------------------------------------------------------------------ build

        private static UIMapIcon Build(MapCustomIconDef def, int slot)
        {
            if (!def.Valid)
            {
                Warn("custom icon def invalid type=" + def.Selection);
                return null;
            }

            // 模板：只走原生 Resolve 分支（不递归进自有 fallback），且只读。
            UIMapIcon template = MapIconSources.ResolveNativeOnly(MapCustomIconCatalog.SteedIconType,
                MapCustomIconCatalog.TemplateSelection);
            if (template == null)
            {
                Warn("custom icon template missing (steed type " + MapCustomIconCatalog.TemplateSelection +
                    "); types 3/4/38 stay absent");
                return null;
            }

            UIMapIcon source = null;
            try
            {
                if (!TryDecodeSheet(def, slot, out Sprite normal, out Sprite locked, out Texture2D texture))
                {
                    return null;
                }

                if (_root == null)
                {
                    _root = new GameObject(SourceRootName);
                    _root.SetActive(false);
                }

                source = UnityEngine.Object.Instantiate(template, _root.transform, false);
                if (source == null)
                {
                    Warn("custom icon clone failed type=" + def.Selection);
                    DestroyOwn(texture, normal, locked);
                    return null;
                }
                source.gameObject.name = SourceRootName + "_" + def.Selection;

                // 只改 clone：sprites[normal, locked] + typeSelection + Image + RectTransform。
                var frames = new Il2CppReferenceArray<Sprite>(2);
                frames[0] = normal;
                frames[1] = locked;
                source.sprites = frames;
                source.typeSelection = def.Selection;

                Image image = source.icon;
                if (image != null)
                {
                    image.sprite = normal;
                    image.raycastTarget = false;      // 与既有 spawn 约定一致：绝不拦截原生选岛点击
                }
                try
                {
                    Text text = source.text;
                    if (text != null) text.raycastTarget = false;
                }
                catch (Exception) { }

                RectTransform rect = source.gameObject.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(def.UiWidth, def.UiHeight);   // UI 尺寸由 RectTransform 决定
                    rect.localScale = Vector3.one;
                }
                source.gameObject.SetActive(false);

                MapIconSources.Log.Info("custom map icon ready type=" + def.Selection +
                    " (" + def.DisplayName + ") " + def.SheetWidth + "x" + def.SheetHeight +
                    " ui=" + def.UiWidth + "x" + def.UiHeight);
                return source;
            }
            catch (Exception e)
            {
                Warn("custom icon build failed type=" + def.Selection + ": " + e.GetType().Name);
                if (source != null)
                {
                    try { UnityEngine.Object.Destroy(source.gameObject); } catch (Exception) { }
                }
                DestroyOwn(Textures[slot], Sprites[slot * 2], Sprites[slot * 2 + 1]);
                Textures[slot] = null;
                Sprites[slot * 2] = null;
                Sprites[slot * 2 + 1] = null;
                return null;
            }
        }

        /// <summary>
        /// 资源字节 → 纹理 + 两帧 Sprite（复用 B 源 loader 同款 API 与校验：
        /// 尺寸必须等于 manifest 声明、两帧都必须有可见像素、Point/Clamp、无 mipmap）。
        /// 与资源读取分离，便于测试链接注入字节。
        /// </summary>
        internal static bool TryDecodeSheet(MapCustomIconDef def, int slot,
            out Sprite normal, out Sprite locked, out Texture2D texture)
        {
            normal = null;
            locked = null;
            texture = null;
            Texture2D created = null;
            Sprite first = null;
            Sprite second = null;
            try
            {
                byte[] bytes = ReadResource(def.ResourceName);
                if (bytes == null) return false;

                created = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(created, bytes, false))
                {
                    Warn("custom icon LoadImage failed type=" + def.Selection);
                    DestroyQuietly(created);
                    return false;
                }
                if (created.width != def.SheetWidth || created.height != def.SheetHeight)
                {
                    Warn("custom icon size mismatch type=" + def.Selection + " " + created.width + "x" +
                        created.height + " != " + def.SheetWidth + "x" + def.SheetHeight);
                    DestroyQuietly(created);
                    return false;
                }
                Color32[] pixels = created.GetPixels32();
                var rectNormal = new Rect(def.NormalX, def.NormalY, def.NormalW, def.NormalH);
                var rectLocked = new Rect(def.LockedX, def.LockedY, def.LockedW, def.LockedH);
                if (!RectHasContent(pixels, created.width, created.height, rectNormal) ||
                    !RectHasContent(pixels, created.width, created.height, rectLocked))
                {
                    Warn("custom icon frame has no visible pixels type=" + def.Selection);
                    DestroyQuietly(created);
                    return false;
                }

                created.filterMode = FilterMode.Point;
                created.wrapMode = TextureWrapMode.Clamp;
                created.anisoLevel = 0;

                var pivot = new Vector2(def.PivotX, def.PivotY);
                first = Sprite.Create(created, rectNormal, pivot, def.PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                second = Sprite.Create(created, rectLocked, pivot, def.PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                if (first == null || second == null)
                {
                    Warn("custom icon Sprite.Create failed type=" + def.Selection);
                    DestroyQuietly(first);
                    DestroyQuietly(second);
                    DestroyQuietly(created);
                    return false;
                }

                texture = created;
                normal = first;
                locked = second;
                Textures[slot] = created;
                Sprites[slot * 2] = first;
                Sprites[slot * 2 + 1] = second;
                return true;
            }
            catch (Exception e)
            {
                Warn("custom icon decode failed type=" + def.Selection + ": " + e.GetType().Name);
                DestroyQuietly(first);
                DestroyQuietly(second);
                DestroyQuietly(created);
                return false;
            }
        }

        private static byte[] ReadResource(string logicalName)
        {
            try
            {
                Assembly assembly = typeof(MapCustomIconAssets).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(logicalName))
                {
                    if (stream == null)
                    {
                        Warn("custom icon resource missing: " + logicalName +
                            " (asset not embedded yet)");
                        return null;
                    }
                    long length = stream.Length;
                    if (length <= 0 || length > MaxResourceBytes)
                    {
                        Warn("custom icon resource size rejected: " + logicalName + " " + length + " bytes");
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
                        Warn("custom icon resource truncated: " + logicalName + " " + read + "/" + bytes.Length);
                        return null;
                    }
                    return bytes;
                }
            }
            catch (Exception e)
            {
                Warn("custom icon resource read failed: " + logicalName + " " + e.GetType().Name);
                return null;
            }
        }

        private static bool RectHasContent(Color32[] pixels, int width, int height, Rect rect)
        {
            if (pixels == null) return false;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.x), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.y), 0, height - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(rect.x + rect.width), 0, width);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(rect.y + rect.height), 0, height);
            for (int y = y0; y < y1; y++)
            {
                int row = y * width;
                for (int x = x0; x < x1; x++)
                {
                    if (pixels[row + x].a > 8) return true;
                }
            }
            return false;
        }

        private static int Slot(int selection)
        {
            switch (selection)
            {
                case 3: return 0;
                case 4: return 1;
                case 38: return 2;
                default: return -1;
            }
        }

        private static void DestroyOwn(Texture2D texture, Sprite a, Sprite b)
        {
            DestroyQuietly(a);
            DestroyQuietly(b);
            DestroyQuietly(texture);
        }

        private static void DestroyQuietly(UnityEngine.Object obj)
        {
            if (obj == null) return;
            try { UnityEngine.Object.Destroy(obj); } catch (Exception) { }
        }

        private static void Warn(string message) => MapIconSources.Log.Warn(message);
    }
}
