using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Only purchased, enabled heroes are excluded. IsHero cannot be used here: native tower
// assignment itself temporarily disables the visual/combat hero eligibility.
internal static class HeroArcherTowerPolicy
{
    private const int MaxTrackedReleases = 8;
    private const int MaxAttempts = 3;
    private sealed class Release
    {
        internal Archer Owner;
        internal IntPtr Actor, Root, Slot;
        internal int Attempts;
        internal float RetryAt;
        internal bool Busy;
    }
    private static readonly Dictionary<int, Release> Releases = new();
    private static readonly List<int> Expired = new(MaxTrackedReleases);
    private static bool _failureLogged;

    internal static bool IsProtected(Archer archer)
    {
        try { return HeroArcherRuntime.Enabled && archer != null && HeroRecruitment.IsPurchased(archer); }
        catch { return false; }
    }

    internal static bool AllowJob(Archer archer, GameObject job)
    {
        try
        {
            if (!IsProtected(archer) || job == null) return true;
            // Native AssignJob gives Knight precedence; preserve that exact non-tower route.
            if (job.GetComponent<Knight>() != null) return true;
            return job.GetComponent<GuardSlot>() == null;
        }
        catch { return true; } // Unknown jobs remain the native caller's responsibility.
    }

    internal static bool AllowSlot(Archer archer, GuardSlot slot)
    {
        try { return slot == null || !IsProtected(archer); }
        catch { return true; }
    }

    // Called at the beginning of the existing Archer observation bridge. No scene search,
    // field clearing, teleport or global distribution suppression. Native ExitGuardSlot owns
    // renderer, collision, movement, scanner, slot and wallet restoration.
    internal static void Observe(Archer archer)
    {
        try
        {
            if (!HeroArcherRuntime.Enabled) { Releases.Clear(); return; }
            if (archer == null || archer.gameObject == null) return;
            int id = archer.gameObject.GetInstanceID();
            if (id == 0) return;
            if (!IsProtected(archer)) { Releases.Remove(id); return; }
            GuardSlot slot = archer._guardSlot;
            if (slot == null && !archer.inGuardSlot) { Releases.Remove(id); return; }
            // A stale actor-side pointer is not authority to evict somebody else's occupant.
            if (slot != null && slot.archer != null && slot.archer.Pointer != archer.Pointer) return;
            IntPtr slotKey = slot != null ? slot.Pointer : IntPtr.Zero;
            if (!Releases.TryGetValue(id, out var release)
                || release.Actor != archer.Pointer || release.Root != archer.gameObject.Pointer || release.Slot != slotKey)
            {
                if (Releases.Count >= MaxTrackedReleases)
                {
                    Expired.Clear();
                    foreach (var pair in Releases)
                        if (!pair.Value.Busy && !IsProtected(pair.Value.Owner)) Expired.Add(pair.Key);
                    foreach (int expired in Expired) Releases.Remove(expired);
                }
                if (!Releases.ContainsKey(id) && Releases.Count >= MaxTrackedReleases) return;
                release = new() { Owner = archer, Actor = archer.Pointer, Root = archer.gameObject.Pointer, Slot = slotKey };
                Releases[id] = release;
            }
            if (release.Busy || release.Attempts >= MaxAttempts || Time.unscaledTime < release.RetryAt) return;
            release.Busy = true; release.Attempts++; release.RetryAt = Time.unscaledTime + 1f;
            try
            {
                // ExitArcher can synchronously ask Kingdom to distribute again. The job and
                // assignment gates below exclude the hero throughout that nested operation;
                // normal archers may refill the slot, and existing cleanup suppression remains.
                archer.ExitGuardSlot();
                if (archer._guardSlot == null && !archer.inGuardSlot
                    && (slot == null || slot.archer == null || slot.archer.Pointer != archer.Pointer))
                    Releases.Remove(id);
            }
            finally { release.Busy = false; }
        }
        catch (Exception error)
        {
            if (_failureLogged) return;
            _failureLogged = true;
            try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeroTower] native release deferred: " + error.GetType().Name); }
            catch { }
        }
    }

    // Actual 2.4 non-getter bodies were checked before use. AssignJob contains inlined slot
    // assignment, so the standalone SetGuardSlot gate alone cannot cover normal distribution.
    [HarmonyPatch(typeof(Archer), nameof(Archer.IsAvailableForJob), new[] { typeof(GameObject) })]
    internal static class AvailabilityPatch
    {
        [HarmonyPostfix] private static void After(Archer __instance, GameObject __0, ref bool __result)
        { if (__result && !AllowJob(__instance, __0)) __result = false; }
    }

    [HarmonyPatch(typeof(Archer), nameof(Archer.AssignJob), new[] { typeof(GameObject) })]
    internal static class AssignmentPatch
    {
        [HarmonyPrefix] private static bool Before(Archer __instance, GameObject __0) => AllowJob(__instance, __0);
    }

    [HarmonyPatch(typeof(Archer), nameof(Archer.SetGuardSlot), new[] { typeof(GuardSlot) })]
    internal static class SetSlotPatch
    {
        [HarmonyPrefix] private static bool Before(Archer __instance, GuardSlot __0) => AllowSlot(__instance, __0);
    }

    [HarmonyPatch(typeof(Archer), "EnterGuardSlot", new[] { typeof(GuardSlot) })]
    internal static class EnterSlotPatch
    {
        [HarmonyPrefix] private static bool Before(Archer __instance, GuardSlot __0) => AllowSlot(__instance, __0);
    }
}
