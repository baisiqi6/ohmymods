#if ANDROID
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// Test-only ownership seam for the Android body-box host. The production supplier
    /// (BankAssistantAtlasVisuals._textures) is exercised by tests/bank-assistant-atlas; this
    /// double only lets FX fixtures declare themselves owned without linking the whole atlas
    /// module. Instance-ID matching mirrors the production contract but is not native ABI proof.
    /// The AndroidTexturePixels double is reused from tests/musketeer-runtime (linked by the csproj).
    /// </summary>
    internal static class BankAssistantAtlasVisuals
    {
        private static readonly HashSet<int> Owned = new();

        internal static void MarkOwned(Texture2D texture)
        {
            if (texture != null) Owned.Add(texture.GetInstanceID());
        }

        internal static void ClearOwned() => Owned.Clear();

        internal static bool OwnsTexture(Texture2D texture)
        {
            if (texture == null) return false;
            return Owned.Contains(texture.GetInstanceID());
        }
    }
}
#endif
