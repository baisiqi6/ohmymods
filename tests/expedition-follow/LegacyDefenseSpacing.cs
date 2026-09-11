using System;
using UnityEngine;
using HarmonyLib;
namespace KingdomEnhancedMod;
internal static class LegacyDefenseSpacing
{
private static readonly System.Collections.Generic.Dictionary<int,int> _moverIsArcher=new();
private static bool _loggedNightPull;
    internal static bool NightFollowerAnchorPrefix(Mover mover, GameObject goal,
        float speed, float offset, Mover.OffsetMode offsetMode)
    {
        try
        {
            if (!ModConfig.Enabled.Value || mover == null || goal == null) return true;
            if (offsetMode != Mover.OffsetMode.Formation) return true;

            // Fast path: cached is-archer verdict per mover instance (same
            // pattern as the is-knight cache above).
            int id = mover.GetInstanceID();
            if (!_moverIsArcher.TryGetValue(id, out int isArcher))
            {
                isArcher = mover.GetComponent<Archer>() != null ? 1 : 0;
                _moverIsArcher[id] = isArcher;
            }
            if (isArcher == 0) return true;

            Kingdom kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            if (kingdom == null || kingdom.isDaytime) return true;

            Archer archer = mover.GetComponent<Archer>();
            if (archer == null || archer._knight == null) return true;
            Knight knight = archer._knight;
            if (knight.gameObject == null) return true;
            float side = (float)knight.side;
            if (side == 0f) return true;

            float wall = kingdom.GetBorderSideIntact(knight.side);
            // Formation target = goal object x + offset (native multiplies the
            // offset by the goal's localScale.x facing sign, Mover.cs:161; the
            // plain sum is within 0.3 of that — close enough for the band test).
            float anchorX = goal.transform.position.x + offset;
            // 拉回量按骑士风格取（KnightStyle API，判空/查不到回落 4.2）：
            // 死地随从=弩手（射程 12，站深不打折、避开贴墙高抛）→ 6.5；
            // 普通弓随从（射程 8）→ 4.2，再深会把后排推出射程。
            float pullback = PatchRoles_KnightStyle.GetFollowerAnchorPullback(knight);
            if ((wall - anchorX) * side < pullback)
            {
                float newAnchor = wall - side * pullback;
                if (!_loggedNightPull)
                {
                    _loggedNightPull = true;
                    KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                        "[DefenseSpacing] night follower anchor pulled inside: knight@"
                        + goal.transform.position.x.ToString("F1")
                        + " anchor " + anchorX.ToString("F1")
                        + "->" + newAnchor.ToString("F1"));
                }
                // Float overload; this mover is an Archer, so the day-spread
                // prefix's is-knight cache passes it straight through (no
                // recursion), and it is night anyway.
                mover.SetGoal(newAnchor, speed);
                return false; // skip the native formation goal
            }
            return true; // anchor already deep enough inside — native follow
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(
                "[DefenseSpacing/night-anchor] " + e);
            return true;
        }
    }
}
