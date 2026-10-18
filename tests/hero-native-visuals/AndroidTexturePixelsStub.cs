#if ANDROID
namespace KingdomEnhancedMod
{
    /// <summary>Test-only stand-in for the device PNG reader; the desktop tests already decode into
    /// Texture2D.Pixels, so the Android branch sees the same pixels without native image APIs.</summary>
    internal static class AndroidTexturePixels
    {
        internal static UnityEngine.Color32[] ReadOwnedPng(UnityEngine.Texture2D texture, int width, int height)
        {
            if (texture == null) throw new System.InvalidOperationException("texture required");
            return texture.GetPixels32();
        }
    }
}
#endif
