using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

// A single existing smith services the bounded paid-career roster. Runtime owns all native
// identity, threat observations, movement and durable writes; the policy alone owns work time.
internal static class HeavyShieldRepair
{
    private static readonly HeavyShieldMaintenancePolicy Policy = new();
    private static readonly List<HeavyShieldMaintenanceSubject> Subjects = new(4);
    private static HeavyShieldRepairSite _site;
    private static HeavyShieldRuntime.Entry _active;
    private static HeavyShieldRepairCompletion _completion;
    private static bool _siteValid, _advancing, _completing, _loggedCompletion;

    internal static void Tick(long world, IReadOnlyList<HeavyShieldRuntime.Entry> entries, bool advancing)
    {
        // A native validation call can reenter managed code. An issued completion is one
        // synchronous transaction, not an opportunity to observe or issue another job.
        if (_completing) return;
        _advancing = advancing;
        _active = null;
        Subjects.Clear();
        bool siteValid = TryReadSite(world, out var site);
        bool relocated = _siteValid && siteValid && _site != site;
        _site = site;
        _siteValid = siteValid;

        bool rosterValid = entries != null && entries.Count <= 4;
        if (rosterValid)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) { Subjects.Add(default); continue; }
                var career = entry.Career;
                bool valid = career.World == world && HeavyShieldRuntime.IsMaintenanceEligible(entry);
                bool atShop = valid && siteValid && TryReadPosition(entry, out float x, out float y)
                    && AtShop(x, y, site);
                Subjects.Add(new(career.Receipt, career.Life, entry.Policy.Durability, valid,
                    HeavyShieldBanner.IsAssigned(career.Receipt, career.Life), atShop,
                    !entry.ThreatKnown || entry.Threatened, entry.LastThreatAt));
            }
        }

        // Even paused/save frames observe real invalidation. A moved site discards ordinary
        // work once; neither missing observations nor relocation release an unknown result.
        Policy.Tick(Time.time, advancing, world, site.Life,
            rosterValid && siteValid && !relocated, Subjects);
        if (!rosterValid) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry != null && entry.Career.Receipt == Policy.ActiveReceipt && entry.Career.Life == Policy.ActiveLife)
            { _active = entry; break; }
        }
        if (!Policy.TryTakeCompletion(out var completion)) return;
        Complete(completion);
    }

    internal static bool IsQueued(Guid receipt, long life) => Policy.IsQueued(receipt, life);

    internal static bool IsUncertainCareer(in HeavyShieldCareerHandle career) =>
        Policy.Phase is (HeavyShieldMaintenancePhase.AwaitingResult or HeavyShieldMaintenancePhase.HeldUnknown) &&
        Policy.ActiveReceipt == career.Receipt && Policy.ActiveLife == career.Life;

    // Only Runtime's confirmed native life-end proof may release this exact career's
    // uncertain transaction. Disable, feature toggles and failed reads are not proof.
    internal static void EndConfirmedCareer(in HeavyShieldCareerHandle career)
    {
        if (IsUncertainCareer(career)) Reset();
    }

    internal static bool TryGetOrder(in HeavyShieldCareerHandle career, out HeavyShieldRepairOrder order)
    {
        order = default;
        var phase = Policy.Phase;
        if (!_advancing || !_siteValid || _active == null || _active.Career != career ||
            Policy.ActiveReceipt != career.Receipt || Policy.ActiveLife != career.Life ||
            phase is not (HeavyShieldMaintenancePhase.Travelling or HeavyShieldMaintenancePhase.Repairing or
                HeavyShieldMaintenancePhase.CompletionDue) ||
            !HeavyShieldRuntime.IsMaintenanceEligible(_active) || !_active.ThreatKnown || _active.Threatened ||
            !HasCurrentObservation(_active) ||
            HeavyShieldBanner.IsAssigned(career.Receipt, career.Life) ||
            !TryReadSite(career.World, out var site) || site != _site ||
            !TryReadPosition(_active, out float x, out float y)) return false;

        bool working = phase != HeavyShieldMaintenancePhase.Travelling;
        if (working && !AtShop(x, y, site)) return false;
        // The repair point is shop x + .55; the existing smith is to its left. Final hand/pose
        // alignment remains native visual calibration, with no extra actor or tool created.
        int facing = working || Math.Abs(site.X - x) <= .20f ? -1 : site.X > x ? 1 : -1;
        order = new(site.X, facing, working);
        return true;
    }

    internal static bool IsCompleting(in HeavyShieldRepairCompletion completion) =>
        _completing && Policy.Phase == HeavyShieldMaintenancePhase.AwaitingResult && _completion == completion;

    internal static void Reset()
    {
        Policy.Reset();
        Subjects.Clear();
        _active = null;
        _site = default;
        _completion = default;
        _siteValid = _advancing = _completing = _loggedCompletion = false;
    }

    private static void Complete(in HeavyShieldRepairCompletion completion)
    {
        bool accepted = false, uncertain = true;
        string fault = null;
        _completion = completion;
        _completing = true;
        try
        {
            accepted = HeavyShieldRuntime.TryCompleteRepair(completion, _site, out uncertain);
            // This proof must precede Resolve: an Idle job cannot subsequently be put into
            // HeldUnknown. Runtime freezes the exact entry if this readback is uncertain.
            if (accepted && !uncertain && !HeavyShieldRuntime.IsRepairSettled(completion))
            { accepted = false; uncertain = true; fault = "post-commit state unproved"; }
        }
        catch (Exception ex)
        {
            accepted = false;
            uncertain = true;
            fault = "completion exception: " + ex.GetType().Name;
        }
        finally
        {
            try
            {
                bool resolved = Policy.ResolveCompletion(completion.Serial, accepted, uncertain);
                // Both operations below are managed policy operations with no native callback
                // between accepting the full-value proof and dropping its historical fence.
                if (resolved && accepted && !uncertain)
                {
                    if (!Policy.ConfirmSettled(completion.Receipt, completion.Life, HeavyShieldDurabilityMigration.Maximum))
                        fault = "accepted completion could not be settled";
                }
                else if (!resolved) fault = "completion owner changed before resolution";
            }
            finally
            {
                _completing = false;
                _completion = default;
                _active = null;
            }
        }
        if (uncertain || fault != null) LogCompletion(fault ?? "completion held unknown; owner reset required");
    }

    private static bool TryReadSite(long world, out HeavyShieldRepairSite site)
    {
        site = default;
        try
        {
            return world > 0 && HeavyShieldShopShell.TryGetRepairSite(out site) && site.World == world &&
                site.Life > 0 && site.Root != IntPtr.Zero && float.IsFinite(site.X) && float.IsFinite(site.Y);
        }
        catch { site = default; return false; }
    }

    private static bool TryReadPosition(HeavyShieldRuntime.Entry entry, out float x, out float y)
    {
        x = y = 0;
        try
        {
            if (entry.Carrier == null) return false;
            var position = entry.Carrier.transform.position;
            x = position.x; y = position.y;
            return float.IsFinite(x) && float.IsFinite(y);
        }
        catch { return false; }
    }

    private static bool AtShop(float x, float y, in HeavyShieldRepairSite site) =>
        Math.Abs(x - site.X) <= .20f && Math.Abs(y - site.Y) <= .50f;

    private static bool HasCurrentObservation(HeavyShieldRuntime.Entry entry)
    {
        foreach (var subject in Subjects)
            if (subject.Receipt == entry.Career.Receipt && subject.Life == entry.Career.Life)
                return subject.Valid && !subject.InBanner && !subject.Threatened &&
                    subject.Durability == entry.Policy.Durability && subject.LastThreatAt == entry.LastThreatAt;
        return false;
    }

    private static void LogCompletion(string message)
    {
        if (_loggedCompletion) return;
        _loggedCompletion = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldRepair] " + message); } catch { }
    }
}
