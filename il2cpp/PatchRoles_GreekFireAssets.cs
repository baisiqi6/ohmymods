using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Restore missing native fire-attack data without granting a buff or changing shared resources.</summary>
internal static class PatchRoles_GreekFireAssets
{
    private const string BaseAssetName = "Archer_OakAndBirch_FireArrowAttack";
    private const string AssetFolder = "Data/ArrowData";
    private static ArrowAttack BaseFire, MappedFire;
    private static IntPtr WorldKey, BiomeKey;
    private static float NextLookupAt;
    private static int Repairs, LogCount;

    private static bool Live(Component c) => c != null && c.gameObject != null && c.gameObject.activeInHierarchy;

    private static void Log(string message)
    {
        if (LogCount >= 3) return;
        LogCount++;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[GreekFireAssets] " + message); }
        catch { }
    }

    // Call from the existing world-load pipeline on both peers; pointer checks below are a second guard.
    internal static void ResetWorld()
    {
        WorldKey = BiomeKey = IntPtr.Zero;
        MappedFire = null;
        NextLookupAt = 0;
        Repairs = LogCount = 0;
    }

    // Match native SpawnGO's prefab mapping; GetPoolFromPrefabAsset itself does no mapping.
    private static bool Ready(ArrowAttack attack, BiomeData biome)
    {
        if (attack == null || attack._arrowPrefab == null || biome == null) return false;
        GameObject original = attack._arrowPrefab.gameObject;
        GameObject effective = biome.GetAssetSwapForThis<GameObject>(original);
        if (effective == null) return false;
        Pool pool = Pool.GetPoolFromPrefabAsset(effective);
        return pool != null && (!NetworkBigBoss.IsOnline || pool.sync);
    }

    /// <summary>
    /// Main-thread, host and client safe. No pool registration, asset mutation, Buffable call or expiry write.
    /// Ensure is scoped to Greek followers; RestoreMissing also serves native client deserialization.
    /// Non-null instance fire data is never replaced.
    /// </summary>
    internal static bool Ensure(Archer archer)
    {
        try
        {
            if (!Live(archer)) return false;
            Knight knight = archer._knight;
            return Live(knight) && PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style)
                && style == 3 && RestoreMissing(archer);
        }
        catch { return false; }
    }

    // Native DeserializeFromData selects fire arrows after ConvertToSoldier/Hunter, but clients
    // need not have a _knight link yet. Prepare nullable native data before that selection;
    // this does not grant FireAttacks to an unrelated soldier or change its active attack.
    internal static bool RestoreMissing(Archer archer)
    {
        try
        {
            if (!ModConfig.Enabled.Value || !Live(archer)) return false;
            if (archer._damageable == null || archer._damageable.isDead || archer.Buffable == null ||
                archer.Buffable._applicableBuffs == null || !archer.Buffable._applicableBuffs.Contains(BuffType.FireAttacks)) return false;

            var managers = Managers.Inst;
            var world = managers != null ? managers.world : null;
            BiomeData biome = BiomeData.Current;
            if (world == null || biome == null) return false;
            if (WorldKey != world.Pointer || BiomeKey != biome.Pointer)
            {
                WorldKey = world.Pointer;
                BiomeKey = biome.Pointer;
                MappedFire = null;
                NextLookupAt = 0;
                Repairs = 0;
                LogCount = 0;
            }

            ArrowAttack current = archer._fireArrowAttack;
            if (current != null) return Ready(current, biome);

            // Asset discovery/mapping is shared by all recipients, never a per-archer scene scan.
            if (MappedFire == null)
            {
                if (Time.time < NextLookupAt) return false;
                NextLookupAt = Time.time + 30f;
                if (BaseFire == null)
                {
                    ArrowAttack found = null;
                    foreach (var asset in Resources.LoadAll<ArrowAttack>(AssetFolder))
                    {
                        if (asset == null || asset.name != BaseAssetName) continue;
                        if (found != null && found.Pointer != asset.Pointer)
                        {
                            Log("ambiguous base fire asset; leaving follower unchanged");
                            return false;
                        }
                        found = asset;
                    }
                    BaseFire = found;
                }
                if (BaseFire == null)
                {
                    Log("base fire asset missing; retry after 30 scaled seconds");
                    return false;
                }
                MappedFire = biome.GetAssetSwapForThis<ArrowAttack>(BaseFire);
            }
            if (!Ready(MappedFire, biome))
            {
                Log("mapped fire asset/prefab/effective pool not ready; no pool created");
                return false;
            }

            // Calls above can initialize native data. Recheck lifecycle, world and the null field before writing.
            if (!Live(archer) || archer._damageable == null || archer._damageable.isDead || archer.Buffable == null ||
                archer.Buffable._applicableBuffs == null || !archer.Buffable._applicableBuffs.Contains(BuffType.FireAttacks)) return false;
            if (!ModConfig.Enabled.Value || Managers.Inst == null || Managers.Inst.world == null ||
                Managers.Inst.world.Pointer != WorldKey || BiomeData.Current == null || BiomeData.Current.Pointer != BiomeKey)
                return false;
            if (archer._fireArrowAttack != null) return Ready(archer._fireArrowAttack, biome);
            archer._fireArrowAttack = MappedFire;
            Repairs++;
            Log("restored missing instance fire attack=" + MappedFire.name + " repairs=" + Repairs);
            return true;
        }
        catch (Exception e)
        {
            Log("dependency lookup failed; follower unchanged: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }
}
