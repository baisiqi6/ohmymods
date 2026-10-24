// ohmymods — palace visible nodes / R6 figure layer：无 Prefix/hook、只读观察的 Root 挂接助手。
// Root：1) 人物创建原始 sorting 赋值处使用 FigureOrderOffset；2) container.SetActive(true) 后调用一次 ObserveOwnedFigures。
using System;
using System.Collections.Generic;
using System.Globalization;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// FigureOrderOffset 是唯一进入排序链的量（环境变量未开时维持 -1）。
/// ObserveOwnedFigures 只读：不写场景、不扫全场景、不做 fallback 猜测、无静态状态。
/// </summary>
internal static class PalaceFigureVisuals
{
    private const string GuardianPrefix = "palace-r6-guardian-";
    private const string ResidentPrefix = "palace-r6-resident-";

    /// <summary>R2：PALACE_FIGURE_LAYER_R2=1 且 land12 runtime 启用时 figure order=0；否则 -1。</summary>
    internal static int FigureOrderOffset =>
        PalaceRuntimeProfile.Capability("PALACE_FIGURE_LAYER_R2") && PalaceLand12Runtime.Enabled ? 0 : -1;

    /// <summary>
    /// 只迭代 Root 传入的本 Content 自有 renderer 列表，按七人前缀筛选。
    /// 仅 PALACE_FIGURE_OBSERVE=1 且 land12 runtime 启用时输出；字段读取失败记 unknown，不吞同 figure 其余信息。
    /// </summary>
    internal static void ObserveOwnedFigures(IEnumerable<SpriteRenderer> ownRenderers)
    {
        if (!PalaceRuntimeProfile.Capability("PALACE_FIGURE_OBSERVE") || !PalaceLand12Runtime.Enabled) return;
        if (ownRenderers == null) { Emit("[FigureVisual] ownRenderers=null"); return; }

        Camera camera;
        try { camera = Camera.main; }
        catch (Exception) { camera = null; }
        string cameraText = camera == null
            ? "camera=unknown"
            : "camera=" + Safe(() => camera.name) + " ptr=" + Ptr(camera);

        foreach (SpriteRenderer renderer in ownRenderers)
        {
            string name = renderer == null ? null : Safe(() => renderer.name);
            if (name == null || !IsObservedFigure(name)) continue; // 名字读不到或非七人：跳过，不猜
            try
            {
                Bounds bounds = default(Bounds);
                Vector3 world = default(Vector3), local = default(Vector3);
                bool boundsOk = TryRead(() => renderer.bounds, out bounds);
                bool worldOk = TryRead(() => renderer.transform.position, out world);
                bool localOk = TryRead(() => renderer.transform.localPosition, out local);
                Sprite sprite = null;
                try { sprite = renderer.sprite; } catch (Exception) { }

                Emit("[FigureVisual] " + name
                    + " activeSelf=" + Safe(() => renderer.gameObject.activeSelf)
                    + " inHierarchy=" + Safe(() => renderer.gameObject.activeInHierarchy)
                    + " enabled=" + Safe(() => renderer.enabled)
                    + " sprite=" + (sprite == null ? "unknown" : Safe(() => sprite.name))
                    + " rect=" + (sprite == null ? "unknown" : Safe(() => RectText(sprite.rect)))
                    + " ppu=" + (sprite == null ? "unknown" : Safe(() => Fmt(sprite.pixelsPerUnit)))
                    + " bounds=" + (boundsOk ? "center" + VectorText(bounds.center) + "size" + VectorText(bounds.size) : "unknown")
                    + " wpos=" + (worldOk ? VectorText(world) : "unknown")
                    + " lpos=" + (localOk ? VectorText(local) : "unknown")
                    + " layer=" + Safe(() => renderer.sortingLayerID)
                    + " order=" + Safe(() => renderer.sortingOrder)
                    + " mat=" + Safe(() => renderer.sharedMaterial == null ? "null" : renderer.sharedMaterial.name)
                    + " shader=" + Safe(() => renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null
                        ? "null" : renderer.sharedMaterial.shader.name)
                    + " " + cameraText
                    + " vpCenter=" + (camera != null && boundsOk
                        ? Safe(() => VectorText(camera.WorldToViewportPoint(bounds.center))) : "unknown")
                    + " vpFootAnchor=" + (camera != null && worldOk
                        ? Safe(() => VectorText(camera.WorldToViewportPoint(world))) : "unknown"));
            }
            catch (Exception e)
            {
                Emit("[FigureVisual] " + name + " unknown:" + e.GetType().Name); // 单 figure 兜底，循环继续
            }
        }
    }

    private static bool IsObservedFigure(string name)
    {
        return name.StartsWith(GuardianPrefix, StringComparison.Ordinal)
            || name.StartsWith(ResidentPrefix, StringComparison.Ordinal);
    }

    private static void Emit(string line)
    {
        try { PalaceCourtVisuals.Log(line); } catch (Exception) { }
    }

    /// <summary>读取失败 -> unknown；不抛出、不影响同 figure 其它字段。</summary>
    private static string Safe(Func<object> read)
    {
        try
        {
            object value = read();
            return value == null ? "null" : value.ToString();
        }
        catch (Exception) { return "unknown"; }
    }

    private static bool TryRead<T>(Func<T> read, out T value)
    {
        try { value = read(); return true; }
        catch (Exception) { value = default(T); return false; }
    }

    private static string Ptr(Il2CppObjectBase o)
    {
        try { return "0x" + o.Pointer.ToInt64().ToString("x"); }
        catch (Exception) { return "unknown"; }
    }

    private static string Fmt(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static string VectorText(Vector3 v) => "(" + Fmt(v.x) + "," + Fmt(v.y) + "," + Fmt(v.z) + ")";

    private static string RectText(Rect r) => "(x" + Fmt(r.x) + ",y" + Fmt(r.y) + ",w" + Fmt(r.width) + ",h" + Fmt(r.height) + ")";
}
