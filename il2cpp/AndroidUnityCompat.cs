// Shared UnityEngine visibility/time boundary for features whose PC path uses members that the
// Android 2.4 player did not ship: Renderer.forceRenderingOff (no metadata method, no ICall),
// Renderer.Get/SetPropertyBlock (no matching native registration) and Time.unscaledTimeAsDouble.
// The PC branch keeps the original expressions verbatim; every Android deviation is explicit.
using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class AndroidUnityCompat
{
    /// <summary>
    /// PC original: <c>r != null &amp;&amp; r.enabled &amp;&amp; !r.forceRenderingOff</c>.
    /// Android 2.4: no <c>Renderer.get_forceRenderingOff_Injected</c> ICall; <c>Renderer.enabled</c>
    /// is the only available pure-render visibility primitive, so it alone decides.
    /// </summary>
    internal static bool WouldRender(Renderer r)
    {
#if ANDROID
        return r != null && r.enabled;
#else
        return r != null && r.enabled && !r.forceRenderingOff;
#endif
    }

    /// <summary>
    /// PC original: <c>r != null &amp;&amp; r.forceRenderingOff</c>.
    /// Android 2.4: the shared <c>enabled</c> field is the hide signal (identity/parent/life are
    /// proven by the caller). It cannot distinguish our write from another system's write.
    /// </summary>
    internal static bool IsHidden(Renderer r)
    {
#if ANDROID
        return r != null && !r.enabled;
#else
        return r != null && r.forceRenderingOff;
#endif
    }

    /// <summary>
    /// PC original: <c>r.forceRenderingOff = true</c>.
    /// Android 2.4: <c>r.enabled = false</c>; the write is read back and an unconfirmed write throws
    /// so the existing caller catch rolls back instead of treating it as success.
    /// </summary>
    internal static void Hide(Renderer r)
    {
#if ANDROID
        r.enabled = false;
        if (r.enabled) throw new InvalidOperationException("android renderer hide write unconfirmed");
#else
        r.forceRenderingOff = true;
#endif
    }

    /// <summary>
    /// PC original: <c>r.forceRenderingOff = false</c>.
    /// Android 2.4: <c>r.enabled = true</c>; same read-back confirmation as <see cref="Hide"/>.
    /// </summary>
    internal static void Show(Renderer r)
    {
#if ANDROID
        r.enabled = true;
        if (!r.enabled) throw new InvalidOperationException("android renderer show write unconfirmed");
#else
        r.forceRenderingOff = false;
#endif
    }

    /// <summary>
    /// PC original: <c>r != null &amp;&amp; r.enabled &amp;&amp; r.forceRenderingOff</c> (our hide plus the
    /// renderer still being enabled). Android 2.4: <c>!r.enabled</c>; identity, parent and lifetime
    /// are proven by the caller, so this is only the shared hide field.
    /// </summary>
    internal static bool HideProof(Renderer r)
    {
#if ANDROID
        return r != null && !r.enabled;
#else
        return r != null && r.enabled && r.forceRenderingOff;
#endif
    }

    /// <summary>
    /// PC original: <c>r != null &amp;&amp; r.enabled &amp;&amp; (!r.forceRenderingOff || ourHide)</c>.
    /// Android 2.4: <c>r != null &amp;&amp; (r.enabled || ourHide)</c>; <c>ourHide</c> means our recorded
    /// write set the shared field, so the renderer is considered intentionally hidden by us.
    /// </summary>
    internal static bool NativeVisible(Renderer r, bool ourHide)
    {
#if ANDROID
        return r != null && (r.enabled || ourHide);
#else
        return r != null && r.enabled && (!r.forceRenderingOff || ourHide);
#endif
    }

    /// <summary>
    /// PC original: <c>Time.unscaledTimeAsDouble</c>.
    /// Android 2.4: <c>Time.get_unscaledTimeAsDouble</c> is absent; <c>(double)Time.unscaledTime</c>
    /// is the retained float second clock (precision and wrap differ at long play sessions).
    /// </summary>
    internal static double UnscaledSeconds()
    {
#if ANDROID
        return (double)Time.unscaledTime;
#else
        return Time.unscaledTimeAsDouble;
#endif
    }
}
