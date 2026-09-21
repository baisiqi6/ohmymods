using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Single-axis policy. Coordinates are in the owning camera's viewport.</summary>
internal static class CurrencyBagViewportPolicy
{
    internal static bool TryGetShift(float minX, float maxX, int pixelLength, out float shift)
    {
        shift = 0f;
        if (!float.IsFinite(minX) || !float.IsFinite(maxX) || maxX < minX || pixelLength <= 0) return false;
        // Keep the user-approved legacy layout exactly whenever it already fits.
        if (minX >= 0f && maxX <= 1f) return false;
        double width = (double)maxX - minX;
        double correction;
        if (width >= 1d)
            correction = 0.5d - ((double)minX + maxX) * 0.5d;
        else
        {
            // At most eight camera pixels and one percent; never make the available
            // interval narrower than the bag merely to preserve padding.
            double margin = Math.Min(Math.Min(8d / pixelLength, 0.01d), (1d - width) * 0.5d);
            correction = minX < 0f ? margin - minX : 1d - margin - maxX;
        }
        shift = (float)correction;
        if (!float.IsFinite(shift)) { shift = 0f; return false; }
        return shift != 0f;
    }
}

internal static class CurrencyBagViewport
{
    /// <summary>Corrects X always; Y only for a currently visible, active bag.</summary>
    internal static void KeepVisible(CurrencyBag bag)
    {
        try
        {
            if (bag == null || bag.CachedInterfaceCam == null) return;
            Camera camera = bag.CachedInterfaceCam.InterfaceCam;
            if (camera == null || !camera.orthographic || camera.pixelWidth <= 0
                || !float.IsFinite(camera.orthographicSize) || camera.orthographicSize <= 0f) return;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            bool any = false;
            if (!Include(bag._front, camera, ref minX, ref maxX, ref minY, ref maxY, ref any)
                || !Include(bag._back, camera, ref minX, ref maxX, ref minY, ref maxY, ref any)
                || !Include(bag._closed, camera, ref minX, ref maxX, ref minY, ref maxY, ref any)
                || !any) return;
            bool moveX = CurrencyBagViewportPolicy.TryGetShift(minX, maxX, camera.pixelWidth, out float shiftX);
            float shiftY = 0f;
            bool moveY = CanCorrectVertical(bag)
                && CurrencyBagViewportPolicy.TryGetShift(minY, maxY, camera.pixelHeight, out shiftY);
            if (!moveX && !moveY) return;
            Vector3 position = bag.transform.position;
            Vector3 viewport = camera.WorldToViewportPoint(position);
            if (!Finite(viewport) || viewport.z <= 0f) return;
            Vector3 origin = camera.ViewportToWorldPoint(viewport);
            viewport.x += shiftX;
            viewport.y += shiftY;
            Vector3 corrected = camera.ViewportToWorldPoint(viewport);
            if (!Finite(origin) || !Finite(corrected)) return;
            // Translation follows this bag's camera axes and preserves viewport depth.
            // Fade state, alpha, scale and the native show/hide target remain untouched.
            Vector3 target = position + (corrected - origin);
            if (Finite(target)) bag.transform.position = target;
        }
        catch (Exception)
        {
            // A missing/destroyed camera or renderer must leave the legacy position intact.
        }
    }

    private static bool CanCorrectVertical(CurrencyBag bag)
    {
        if (!bag.enabled || bag.gameObject == null || !bag.gameObject.activeInHierarchy
            || bag.player == null || bag.player.gameObject == null || !bag.player.gameObject.activeInHierarchy
            || CurrencyBag.DebugHideCurrencyBag) return false;
        CurrencyBag.FadeState state = bag.CurrentFadeState;
        if (state == CurrencyBag.FadeState.FadingIn || state == CurrencyBag.FadeState.Opening
            || state == CurrencyBag.FadeState.Open) return true;
        // StartShow may only queue a coroutine, or may be declined. Its postfix
        // has the same strict state gate; a still-Hidden bag must remain hidden.
        return false;
    }

    private static bool Include(SpriteRenderer renderer, Camera camera, ref float minX, ref float maxX, ref float minY, ref float maxY, ref bool any)
    {
        if (renderer == null || renderer.sprite == null) return true;
        Bounds bounds = renderer.bounds;
        Vector3 min = bounds.min, max = bounds.max;
        if (!Finite(min) || !Finite(max) || min.x > max.x || min.y > max.y || min.z > max.z) return false;
        // Disabled renderers can report empty world-origin bounds; they are not bag geometry.
        if (max.x <= min.x || max.y <= min.y) return true;
        // Closed/open and faded sprites all contribute: visibility changes must not move the bag.
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            Vector3 projected = camera.WorldToViewportPoint(corner);
            if (!Finite(projected) || projected.z <= 0f) return false;
            minX = Math.Min(minX, projected.x);
            maxX = Math.Max(maxX, projected.x);
            minY = Math.Min(minY, projected.y);
            maxY = Math.Max(maxY, projected.y);
        }
        any = true;
        return true;
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
