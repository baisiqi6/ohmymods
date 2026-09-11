using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>At most five bounded snapshots, using arrays already fetched by the existing style patrol.</summary>
internal static class SquadRosterSnapshot
{
    private struct Count
    {
        internal Knight Owner;
        internal int Style, Slots, Capacity, Live, Far;
    }
    private static readonly Dictionary<IntPtr, Count> Counts = new(64);
    private static readonly int[] Owners = new int[5], Slots = new int[5], Capacity = new int[5],
        Live = new int[5], Mismatches = new int[5], Far = new int[5];
    private static float NextAt;
    private static int Samples;
    private static bool ErrorLogged;

    internal static void Observe(Knight[] knights, Archer[] archers)
    {
        if (Samples >= 5 || !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth || Time.timeScale <= 0 ||
            Time.time < NextAt || knights == null || archers == null) return;
        NextAt = Time.time + 120f;
        Samples++;
        long started = Stopwatch.GetTimestamp();
        try
        {
            Counts.Clear();
            Array.Clear(Owners, 0, 5); Array.Clear(Slots, 0, 5); Array.Clear(Capacity, 0, 5);
            Array.Clear(Live, 0, 5); Array.Clear(Mismatches, 0, 5); Array.Clear(Far, 0, 5);
            // Bounded CPU even if a player creates an unusually large army. Truncation is explicit.
            for (int i = 0; i < knights.Length && i < 64; i++)
            {
                var k = knights[i];
                if (k == null || k.gameObject == null || !k.gameObject.activeInHierarchy ||
                    !PatchRoles_KnightStyle.TryGetResolvedStyleIndex(k, out int style) || style < 0 || style >= 5) continue;
                Counts[k.Pointer] = new Count { Owner = k, Style = style, Slots = k.numArchers, Capacity = k._maxArchers };
            }
            for (int i = 0; i < archers.Length && i < 1024; i++)
            {
                var a = archers[i];
                if (a == null || a.gameObject == null || !a.gameObject.activeInHierarchy ||
                    a._damageable == null || a._damageable.isDead) continue;
                var k = a._knight;
                if (k == null || !Counts.TryGetValue(k.Pointer, out var count)) continue;
                count.Live++;
                if (Mathf.Abs(a.transform.position.x - k.transform.position.x) > 10f) count.Far++;
                Counts[k.Pointer] = count;
            }
            var detail = new System.Text.StringBuilder();
            int details = 0;
            foreach (var entry in Counts.Values)
            {
                int s = entry.Style;
                Owners[s]++; Slots[s] += entry.Slots; Capacity[s] += entry.Capacity;
                Live[s] += entry.Live; Far[s] += entry.Far;
                if (entry.Slots != entry.Live)
                {
                    Mismatches[s]++;
                    if (details++ < 3) detail.Append(" owner#").Append(entry.Owner.GetInstanceID())
                        .Append("(s").Append(s).Append(" roster=").Append(entry.Slots).Append(" alive=").Append(entry.Live).Append(')');
                }
            }
            var summary = new System.Text.StringBuilder("[SquadRoster] cached<=3s");
            for (int s = 0; s < 5; s++)
                summary.Append(" s").Append(s).Append("{knights=").Append(Owners[s]).Append(" roster=")
                    .Append(Slots[s]).Append('/').Append(Capacity[s]).Append(" alive=").Append(Live[s])
                    .Append(" mismatch=").Append(Mismatches[s]).Append(" far10=").Append(Far[s]).Append('}');
            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            summary.Append(detail).Append(" truncated=").Append(knights.Length > 64 || archers.Length > 1024)
                .Append(" readMs=").Append(ms.ToString("F2")).Append(" sample=").Append(Samples).Append("/5");
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(summary.ToString());
        }
        catch (Exception e)
        {
            if (!ErrorLogged)
            {
                ErrorLogged = true;
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[SquadRoster] read failed: " + e.GetType().Name); }
                catch { }
            }
        }
        finally { Counts.Clear(); }
    }
}
