using System;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 自有地图图标数据（用户 2026-10-02 批准补齐 Santa Reindeer(3) / Spookyhorse(4) / Rainbow Pony(38)）。
    ///
    /// - 只覆盖原生**确证缺失**的 steed 类型；其余缺源仍走原生缺口日志，绝不替换成别的变体；
    /// - 表由 `tools/sync_custom_icons.py` 从 `custom-icons/ready.json`（root 实测 PNG/rect/ui）生成，
    ///   纯数值（无 UnityEngine 类型）⇒ 离线测试可直接链接；UI 层负责转 Rect/Vector2；
    /// - 每 type 两个状态帧（normal / locked），rect 为 **Unity bottom-left 像素矩形**（atlas 内），
    ///   不做任何离线缩放/重画；最终 UI 尺寸由 RectTransform 决定（= UiWidth/UiHeight）。
    /// </summary>
    internal sealed class MapCustomIconDef
    {
        internal readonly int IconType;        // UIMapIcon/MapIconType：Steed = 1
        internal readonly int Selection;       // typeSelection：3 / 4 / 38
        internal readonly string ResourceName; // EmbeddedResource LogicalName
        internal readonly string DisplayName;
        internal readonly int SheetWidth;
        internal readonly int SheetHeight;
        internal readonly float NormalX, NormalY, NormalW, NormalH;
        internal readonly float LockedX, LockedY, LockedW, LockedH;
        internal readonly float UiWidth, UiHeight;
        internal readonly float PivotX, PivotY;   // 归一化（默认 0.5, 0 = 原生地图图标脚点锚）
        internal readonly float PixelsPerUnit;
        internal readonly string Sha256;

        internal MapCustomIconDef(int iconType, int selection, string resourceName, string displayName,
            int sheetWidth, int sheetHeight,
            float normalX, float normalY, float normalW, float normalH,
            float lockedX, float lockedY, float lockedW, float lockedH,
            float uiWidth, float uiHeight, float pivotX, float pivotY, float pixelsPerUnit, string sha256)
        {
            IconType = iconType;
            Selection = selection;
            ResourceName = resourceName;
            DisplayName = displayName;
            SheetWidth = sheetWidth;
            SheetHeight = sheetHeight;
            NormalX = normalX;
            NormalY = normalY;
            NormalW = normalW;
            NormalH = normalH;
            LockedX = lockedX;
            LockedY = lockedY;
            LockedW = lockedW;
            LockedH = lockedH;
            UiWidth = uiWidth;
            UiHeight = uiHeight;
            PivotX = pivotX;
            PivotY = pivotY;
            PixelsPerUnit = pixelsPerUnit;
            Sha256 = sha256;
        }

        internal bool Valid =>
            SheetWidth > 0 && SheetHeight > 0 &&
            NormalW > 0f && NormalH > 0f && LockedW > 0f && LockedH > 0f &&
            NormalX >= 0f && NormalY >= 0f && LockedX >= 0f && LockedY >= 0f &&
            NormalX + NormalW <= SheetWidth + 0.01f && LockedX + LockedW <= SheetWidth + 0.01f &&
            NormalY + NormalH <= SheetHeight + 0.01f && LockedY + LockedH <= SheetHeight + 0.01f &&
            UiWidth > 0f && UiHeight > 0f;
    }

    internal static class MapCustomIconCatalog
    {
        internal const int SteedIconType = 1;
        /// <summary>克隆模板：原生确证存在的 Steed UIMapIcon（Map_Icon_Steed_Stag, type=5）。</summary>
        internal const int TemplateSelection = 5;

        // ==== BEGIN GENERATED (tools/sync_custom_icons.py) ====
        internal static readonly MapCustomIconDef[] Defs =
        {
            new MapCustomIconDef(1, 3, "KingdomEnhancedMod.KEM_MapSantaReindeer.png", "Santa Reindeer", 1774, 887, 372f, 122f, 450f, 616f, 1106f, 122f, 450f, 616f, 24f, 32f, 0.5f, 0f, 100f, "debd5d87908761130a40444b42e1288c8c23dedf76b15b3f084e2e27cf789172"),
            new MapCustomIconDef(1, 4, "KingdomEnhancedMod.KEM_MapSpookyhorse.png", "Spookyhorse", 1774, 887, 93f, 177f, 702f, 435f, 951f, 177f, 702f, 435f, 40f, 25f, 0.5f, 0f, 100f, "dcf9b143b9e5b2eb950e0159c5f4f161ce1a2fc45a0e954512b642bfc1a1b808"),
            new MapCustomIconDef(1, 38, "KingdomEnhancedMod.KEM_MapRainbowPony.png", "Rainbow Pony", 1774, 887, 193f, 207f, 630f, 409f, 971f, 207f, 630f, 409f, 40f, 26f, 0.5f, 0f, 100f, "65090951f3b29f2f9b54351c15a3907c5cf014dc9a2f9fe9d2418a7eaac97979"),
        };
        // ==== END GENERATED ====

        internal static int Count => Defs.Length;

        /// <summary>是否属于用户批准的自有补齐范围（**仅** steed 1 + type 3/4/38）。</summary>
        internal static bool IsCustomSteed(int iconType, int selection)
        {
            if (iconType != SteedIconType) return false;
            return selection == 3 || selection == 4 || selection == 38;
        }

        internal static bool TryGet(int iconType, int selection, out MapCustomIconDef def)
        {
            for (int i = 0; i < Defs.Length; i++)
            {
                MapCustomIconDef candidate = Defs[i];
                if (candidate.IconType == iconType && candidate.Selection == selection)
                {
                    def = candidate;
                    return true;
                }
            }
            def = null;
            return false;
        }
    }
}
