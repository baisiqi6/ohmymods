#if ANDROID
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace KingdomEnhancedMod;

// Issue #188：Android 公共绘制（本文件与 ImGuiCompat.cs 组成同一个 partial 类；原声明已改 partial，
// 旧 ANDROID 块已移出，PC 分支与 IntersectRect 原样保留）。
// 本 loader 构建 strip 了 GUIStyleState 背景 setter 与旧 GUIStyle.CalcSize 的 icall。
// 旧注释“native BeginGroup 一律不裁 GUI.matrix 绘制”改为以下具体组合证据：
//   Round1 outerGroup→matrix→Label：9 组 60,160 像素确认完整外溢；
//   Round2 matrix→innerGroup(q)→完整 intrinsic Label(-q)：保留图案，远侧外溢消失。
// 独审同时记录本组合边界并非像素全等：C/D 保留区各 1 列 80 像素差异（最大通道 delta≤35），
// E/F 各 1 行 80 像素（delta≤64），紧邻裁剪线外侧另有各 80 个弱非黑像素；因此不声称
// “像素完全一致 / 外侧全黑 / 唯一根因”。完整图片采用 Round2 已观测组合（本机仅观测过
// parent=1/0.6、fit=1 与正轴缩放；其他取值、放大、旋转/负 scale 不作已验承诺）。
// 1x1 纯色填色保留原独立语义与几何补偿（有独立纯色用途），不套内层 group。
internal static partial class ImGuiCompat
{
    // 两条绘制路径共用：一份 weak GUIContent 缓存与一份 borderless 样式。弱键不阻止纹理回收；
    // 纹理属于调用方，本类不 Destroy 外部纹理、不复制纹理、不做资源注册或默认尺寸。
    private static readonly ConditionalWeakTable<Texture2D, GUIContent> TextureContents = new();
    private static readonly ConditionalWeakTable<Texture2D, GUIContent>.CreateValueCallback CreateTextureContent =
        texture => new GUIContent { image = texture };
    private static GUIStyle borderlessStyle;

    /// <summary>
    /// 根 GUI 全图片入口：等价于显式四参入口 (rect, texture, Vector2.zero, rect)。
    /// 不能从未知外层 Group 猜测 origin/clip，嵌套调用方必须显式使用四参入口。
    /// </summary>
    internal static void DrawTexture(Rect rect, Texture2D texture) => DrawTexture(rect, texture, Vector2.zero, rect);

    /// <summary>
    /// 完整图片绘制：整张纹理铺满 dest（UV 自然 0..1），Q=IntersectRect(dest, clip) 只限定可见
    /// 区域，不把残余内容重新拉满 Q。O 为当前 group 局部 (0,0) 的绝对位置（当前 GUI.matrix
    /// 变换前）；dest/clip 均为当前局部坐标。null 纹理 / 非正 dest / 空交集不画；纹理实际尺寸
    /// 每次真实读取，非正时显式抛错（不猜 1 或 8、不缓存失效尺寸、不 catch 后重试）。
    /// </summary>
    internal static void DrawTexture(Rect localRect, Texture2D texture, Vector2 groupScreenOrigin, Rect localClipRect)
    {
        if (texture == null) return;
        if (localRect.width <= 0f || localRect.height <= 0f) return;
        Rect viewport = IntersectRect(localRect, localClipRect);
        if (viewport.width <= 0f || viewport.height <= 0f) return; // 空交集：无事可画
        int width = texture.width;   // 每次从纹理真实读取：不缓存、不默认 8/1
        int height = texture.height;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("ImGuiCompat.DrawTexture: texture size must be positive, got " + width + "x" + height);
        float sx = localRect.width / width;
        float sy = localRect.height / height;
        GUIContent content = TextureContents.GetValue(texture, CreateTextureContent);
        GUIStyle style = borderlessStyle ??= BuildBorderlessStyle();
        Matrix4x4 restore = GUI.matrix;
        GUI.matrix = restore * StretchMatrix(sx, sy, localRect.x, localRect.y, groupScreenOrigin); // 先 matrix、后 inner group（Round2 观测组合）
        try
        {
            float qx = (viewport.x - localRect.x) / sx;
            float qy = (viewport.y - localRect.y) / sy;
            float qw = viewport.width / sx;
            float qh = viewport.height / sy;
            GUI.BeginGroup(new Rect(qx, qy, qw, qh));
            try { GUI.Label(new Rect(-qx, -qy, width, height), content, style); } // 完整 intrinsic：不重缩到 Q
            finally { GUI.EndGroup(); } // 只有 BeginGroup 成功才配对 EndGroup
        }
        finally { GUI.matrix = restore; } // 先 EndGroup、后恢复矩阵
    }

    /// <summary>1x1 纯色交集填色（原语义与公式不变，免内层 group；裁剪已在产生处求交集）。</summary>
    internal static void DrawSolidTexture(Rect localRect, Texture2D texture, Vector2 groupScreenOrigin, Rect localClipRect)
    {
        Rect clipped = IntersectRect(localRect, localClipRect);
        if (clipped.width <= 0f || clipped.height <= 0f) return; // 空交集：无事可画
        GUIStyle style = borderlessStyle ??= BuildBorderlessStyle();
        GUIContent content = TextureContents.GetValue(texture, CreateTextureContent);
        Matrix4x4 restore = GUI.matrix;
        GUI.matrix = restore * StretchMatrix(clipped.width, clipped.height, clipped.x, clipped.y, groupScreenOrigin);
        try { GUI.Label(new Rect(0f, 0f, 1f, 1f), content, style); }
        finally { GUI.matrix = restore; }
    }

    /// <summary>两路径共用的（唯一）矩阵构造：m03/m13 = O + localXY - s*O（intrinsic=1 时即原纯色公式）。</summary>
    private static Matrix4x4 StretchMatrix(float sx, float sy, float localX, float localY, Vector2 groupScreenOrigin)
    {
        Matrix4x4 stretch = Matrix4x4.identity;
        stretch.m00 = sx;
        stretch.m11 = sy;
        stretch.m03 = groupScreenOrigin.x + localX - sx * groupScreenOrigin.x;
        stretch.m13 = groupScreenOrigin.y + localY - sy * groupScreenOrigin.y;
        return stretch;
    }

    private static GUIStyle BuildBorderlessStyle()
    {
        // 整体 RectOffset 赋值在 interop 下走缺失的 GUIStyle::AssignRectOffset_Injected 桥
        // （Issue #144）；与 ModPanelStyles 一致，经 getter 取回自有 RectOffset 后逐字段写全 0。
        GUIStyle style = new GUIStyle();
        ModPanelStyles.WriteOffset(style.border, 0, 0, 0, 0);
        ModPanelStyles.WriteOffset(style.padding, 0, 0, 0, 0);
        ModPanelStyles.WriteOffset(style.margin, 0, 0, 0, 0);
        ModPanelStyles.WriteOffset(style.overflow, 0, 0, 0, 0);
        style.wordWrap = false;
        return style;
    }
}
#endif
