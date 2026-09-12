using System;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class SamuraiRetreatSpeed
{
    private static bool Logged;
    // Change only the native defensive GoToWall call; never persist a multiplied actor stat.
    internal static void Adjust(Knight knight, Mover mover, ref float speed)
    {
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || knight == null ||
            knight.gameObject == null || !knight.gameObject.activeInHierarchy || mover == null ||
            knight._mover == null || knight._mover.Pointer != mover.Pointer ||
            knight._damageable == null || knight._damageable.isDead ||
            knight._fsm == null || knight._fsm.Current != Knight.State.GoToWall || !knight.isRetreating ||
            !PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style) || style != 2 ||
            knight.isCharging || knight._shouldCharge || knight._beingControlled || knight.ShouldPlayerControl() ||
            knight.GetFormation() != null || knight.helPuzzlePillar != null) return;
        var character = knight._character;
        if (character == null || character.inert || character.grabbed || character.isStationary) return;
        var embarkee = knight._embarkee;
        if (embarkee != null && (embarkee.IsEmbarked || embarkee.IsTargetingEmbarkable || embarkee.EmbarkableTarget != null)) return;
        float baseline = knight._retreatSpeed;
        if (!float.IsFinite(speed) || !float.IsFinite(baseline) || baseline <= 0 ||
            !Mathf.Approximately(speed, baseline)) return;
        float adjusted = baseline * 3f;
        if (!float.IsFinite(adjusted)) return;
        speed = adjusted;
        if (!Logged)
        {
            Logged = true;
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[SamuraiRetreat] native defensive retreat speed x3 applied"); }
            catch { }
        }
    }
}
