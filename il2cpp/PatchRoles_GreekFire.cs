using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Greek squad fire: native timed buff and RPCs, 8 seconds per 15-second cast.</summary>
internal static class PatchRoles_GreekFire
{
    private const float Duration = 8f, Cooldown = 15f, PollInterval = .25f;
    private sealed class OwnerState
    {
        internal Knight Knight;
        internal int Id;
        internal float NextTryAt, NextCheck;
        internal bool Casting;
    }
    private static readonly Dictionary<int, OwnerState> States = new();
    private static readonly HashSet<string> Logged = new();
    private static BuffData Original, Clone;
    private static float NextAssetTry;
    private static int EnemyLayer = -1, CastLogs;

    private static void Info(string message) => KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[GreekFire] " + message);
    private static void Once(string key, string message)
    {
        if (Logged.Add(key)) KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[GreekFire] " + message);
    }
    private static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;
    private static bool Live(Component c) => c != null && c.gameObject != null && c.gameObject.activeInHierarchy;
    private static bool Enabled() => ModConfig.Enabled.Value && NetworkBigBoss.HasWorldAuth && Time.timeScale > 0;
    private static bool Eligible(Knight k) => Enabled() && Live(k) && k.enabled &&
        PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) && style == 3 &&
        k._character != null && !k._character.inert && !k._character.grabbed && !k._harmless &&
        k._damageable != null && !k._damageable.isDead &&
        k._embarkee != null && !k._embarkee.IsEmbarked;
    private static bool Follower(Knight k, Archer a) => Live(a) && Same(a._knight, k) &&
        a._damageable != null && !a._damageable.isDead && a._character != null &&
        !a._character.inert && !a._character.grabbed && a._embarkee != null && !a._embarkee.IsEmbarked;
    private static bool Current(OwnerState s) => States.TryGetValue(s.Id, out var current) && ReferenceEquals(current, s);
    private static bool CastingValid(OwnerState s) => Current(s) && s.Casting && Eligible(s.Knight);
    private static bool BuffReady(Buffable b) => Live(b) && b.enabled && b._owner != null &&
        b._applicableBuffs != null && b._applicableBuffs.Contains(BuffType.FireAttacks) && b._activeBuffsExpirations != null;
    // Native receivers send their RPC under HasWorldAuth even offline: require registration in both modes.
    private static bool KnightReady(Knight k) => k.parentHeaderRef != null && k._knightBuffIndex >= 0 &&
        k._knightBuffEndIndex >= 0 && BuffReady(k.Buffable);
    private static bool ArcherReady(Archer a)
    {
        if (a.parentHeaderRef == null || a._archerBuffIndex < 0 || a._archerBuffEndIndex < 0 || !BuffReady(a.Buffable)) return false;
        return PatchRoles_GreekFireAssets.Ensure(a);
    }

    private static bool Target(GameObject target, float x, float range, DamageSource source)
    {
        if (target == null || !target.activeInHierarchy || range <= 0
            || !float.IsFinite(range) || !float.IsFinite(x) || target.transform == null
            || !float.IsFinite(target.transform.position.x)) return false;
        if (EnemyLayer < 0) EnemyLayer = LayerMask.NameToLayer("Enemies");
        if (EnemyLayer < 0 || target.layer != EnemyLayer || Mathf.Abs(target.transform.position.x - x) > range) return false;
        var d = target.GetComponent<Damageable>();
        return d != null && d.enabled && !d.isDead && !(d.invulnerable && d.ignoredWhenInvulnerable) && d.IsDamagedBy(source);
    }
    private static bool HasEnemy(Knight k, Archer[] archers)
    {
        var scanner = k._enemyScanner;
        if (scanner != null && Target(scanner.GetClosest(), k.transform.position.x,
            Mathf.Max(scanner.range, scanner.rangeBehind), DamageSource.Knight)) return true;
        foreach (var a in archers)
        {
            if (!Follower(k, a) || a.ActiveArrowAttack == null) continue;
            float range = a.ActiveArrowAttack.Range;
            if (Target(a._shootingTarget, a.transform.position.x, range, DamageSource.Arrow)) return true;
            // Cached target can be stale/dead while the same follower's native scanner buffer
            // holds another shootable enemy: bounded GetAll fallback, never only GetClosest.
            // The array is not retained past this call; Target re-measures from the Archer.
            Scanner followerScanner = a._enemyScanner;
            if (followerScanner == null) continue;
            int count = followerScanner.GetAll(out Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<GameObject> results);
            // Native calls can change membership or the active attack; only current
            // owned followers and their current scanner/range supply trigger evidence.
            if (!Eligible(k) || !Follower(k, a) || a._enemyScanner == null
                || a._enemyScanner.Pointer != followerScanner.Pointer
                || a.ActiveArrowAttack == null) continue;
            range = a.ActiveArrowAttack.Range;
            if (results == null || count <= 0) continue;
            int limit = Math.Min(64, Math.Min(count, results.Length));
            for (int i = 0; i < limit; i++)
                if (Target(results[i], a.transform.position.x, range, DamageSource.Arrow)) return true;
        }
        return false;
    }

    private static bool TryGetClone()
    {
        if (Clone != null) return true;
        if (Time.time < NextAssetTry) return false;
        NextAssetTry = Time.time + 5f;
        try
        {
            BuffDataStorage.Init();
            var assets = Resources.LoadAll<BuffData>("Data/BuffData");
            BuffData chosen = null;
            int candidates = 0;
            foreach (var asset in assets)
            {
                if (asset == null || asset.BuffType != BuffType.FireAttacks) continue;
                var registered = BuffDataStorage.GetBuffData(asset.ID);
                if (!Same(registered, asset)) continue;
                candidates++;
                if (chosen == null || asset.ID < chosen.ID) chosen = asset;
            }
            if (chosen == null) { Once("asset-missing", "registered FireAttacks data unavailable; retry in 5s"); return false; }
            // Keep the original RPC ID and all visual data. Never mutate the shared asset/storage.
            var clone = UnityEngine.Object.Instantiate((UnityEngine.Object)chosen).Cast<BuffData>();
            clone.EffectDuration = Duration;
            Original = chosen;
            Clone = clone;
            Info("asset=" + Original.name + " id=" + Original.ID + " originalDuration=" + Original.EffectDuration +
                " cloneDuration=" + Clone.EffectDuration + " candidates=" + candidates);
            return true;
        }
        catch (Exception e) { Once("asset-error", "asset preparation failed; retry in 5s: " + e); return false; }
    }

    // consumed is set before the native call: a partial native failure must not cause repeated RPCs.
    private static bool Apply(Buffable b, ref bool consumed)
    {
        if (!BuffReady(b)) return false;
        float end = Time.time + Duration;
        if (b._activeBuffsExpirations.TryGetValue(BuffType.FireAttacks, out float oldEnd) && oldEnd >= end)
        {
            consumed = true;
            return true;
        }
        consumed = true;
        b.ActivateBuff(Clone); // First activation receives 8s; native coroutine and end RPC own expiration.
        return b._activeBuffsExpirations.ContainsKey(BuffType.FireAttacks);
    }

    internal static void OnKnightUpdate(Knight knight)
    {
        try
        {
            if (!Enabled() || !Live(knight)) return;
            int id = knight.gameObject.GetInstanceID();
            if (!States.TryGetValue(id, out var state) || !Same(state.Knight, knight))
            {
                if (!Eligible(knight)) return;
                state = new OwnerState { Knight = knight, Id = id };
                States[id] = state;
            }
            float now = Time.time;
            if (state.Casting || now < state.NextTryAt || now < state.NextCheck) return;
            state.NextCheck = now + PollInterval;
            if (!Eligible(knight) || !KnightReady(knight)) return;
            // Default 3-second shared scene cache; no poll-time allocation until a cast is possible.
            var archers = UnitScanCache.GetArchers();
            if (!HasEnemy(knight, archers) || !TryGetClone()) return;
            var followers = new List<Archer>();
            foreach (var a in archers) if (Follower(knight, a)) followers.Add(a);
            float previousCooldown = state.NextTryAt;
            state.Casting = true;
            state.NextTryAt = now + Cooldown;
            bool consumed = false;
            int recipients = 0;
            try
            {
                if (CastingValid(state) && KnightReady(knight) && Apply(knight.Buffable, ref consumed)) recipients++;
                foreach (var a in followers)
                {
                    if (!CastingValid(state)) break;
                    // Membership snapshot is fixed, but departures/death/native callback replacements are rechecked.
                    if (!Follower(knight, a) || !ArcherReady(a)) continue;
                    PatchRoles_Crossbowman.RestoreSquadCrossbowPackage(a);
                    if (!CastingValid(state)) break;
                    if (Follower(knight, a) && ArcherReady(a) && Apply(a.Buffable, ref consumed)) recipients++;
                }
                if (consumed && CastLogs < 3)
                {
                    CastLogs++;
                    Info("cast owner#" + id + " duration=8s cooldown=15s recipients=" + recipients + " buffID=" + Clone.ID);
                }
            }
            finally
            {
                if (Current(state))
                {
                    state.Casting = false;
                    if (!consumed) state.NextTryAt = previousCooldown;
                }
            }
        }
        catch (Exception e) { Once("update", "cast check failed: " + e); }
    }

    internal static void OnKnightDisabled(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return;
        int id = knight.gameObject.GetInstanceID();
        if (States.TryGetValue(id, out var state) && Same(state.Knight, knight)) States.Remove(id);
        // Leave shared FireAttacks to native Buffable cleanup; it may belong to the original artifact.
    }
}

[HarmonyPatch(typeof(Knight), "Update")]
internal static class Knight_Update_GreekFire_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance) => PatchRoles_GreekFire.OnKnightUpdate(__instance);
}

[HarmonyPatch(typeof(Knight), "OnDisable")]
internal static class Knight_OnDisable_GreekFire_Patch
{
    // Release our state before native lifecycle callbacks can reuse the pooled actor.
    [HarmonyPrefix]
    private static void Prefix(Knight __instance)
    {
        try { PatchRoles_GreekFire.OnKnightDisabled(__instance); }
        catch (Exception) { }
    }
}
