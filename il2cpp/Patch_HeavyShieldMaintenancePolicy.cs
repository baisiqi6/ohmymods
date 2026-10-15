using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod;

internal readonly record struct HeavyShieldMaintenanceSubject(Guid Receipt, long Life, int Durability, bool Valid, bool InBanner, bool AtShop, bool Threatened, double LastThreatAt);
internal readonly record struct HeavyShieldRepairCompletion(long Serial, Guid Receipt, long Life, long World, long ShopLife, int ExpectedDurability);
internal enum HeavyShieldMaintenancePhase { Idle, Travelling, Repairing, CompletionDue, AwaitingResult, HeldUnknown }

internal sealed class HeavyShieldMaintenancePolicy
{
    internal const double QuietSeconds = 6d;
    internal const double WorkSeconds = 8d;
    private readonly record struct Job(Guid Receipt, long Life, int Durability, double LastThreatAt);
    private readonly List<Job> _queue = new(4);
    private readonly Dictionary<Guid, Job> _accepted = new();
    private readonly HashSet<Guid> _interrupted = new();
    private readonly List<HeavyShieldMaintenanceSubject> _additions = new(4);
    private long _world, _shopLife, _serialCounter;
    private double _lastTime = double.NaN;
    private bool _previousPlaying, _latestPlaying;
    private HeavyShieldRepairCompletion _completion;

    internal HeavyShieldMaintenancePhase Phase { get; private set; }
    internal Guid ActiveReceipt => Phase == HeavyShieldMaintenancePhase.Idle ? Guid.Empty : _queue[0].Receipt;
    internal long ActiveLife => Phase == HeavyShieldMaintenancePhase.Idle ? 0 : _queue[0].Life;
    internal double WorkedSeconds { get; private set; }

    internal bool IsQueued(Guid receipt, long life)
    {
        foreach (var job in _queue)
            if (job.Receipt == receipt && job.Life == life) return true;
        return false;
    }

    // The adapter owns native identity, threat detection, movement and mutation. These bounded
    // snapshots only decide scheduling; no completion value is itself a native identity proof.
    internal void Tick(double gameTime, bool playing, long world, long shopLife, bool siteValid,
        IReadOnlyList<HeavyShieldMaintenanceSubject> subjects)
    {
        _latestPlaying = playing;
        if (Phase is HeavyShieldMaintenancePhase.AwaitingResult or HeavyShieldMaintenancePhase.HeldUnknown)
        {
            // An ordinary observation cannot unlock an already-issued mutation transaction.
            _previousPlaying = false;
            return;
        }

        if (!double.IsFinite(gameTime) || gameTime < 0 || world <= 0 || shopLife <= 0 ||
            !siteValid || !ValidRoster(subjects))
        {
            ClearJobs();
            _previousPlaying = _latestPlaying = false;
            _lastTime = double.NaN;
            return;
        }

        if (_world != world || _shopLife != shopLife)
        {
            ClearJobs();
            if (_world != world) _accepted.Clear();
            _world = world;
            _shopLife = shopLife;
            _previousPlaying = false;
        }
        if (!double.IsNaN(_lastTime) && gameTime < _lastTime)
        {
            ClearJobs();
            _lastTime = gameTime;
            _previousPlaying = _latestPlaying = false;
            return;
        }

        double delta = _previousPlaying && playing ? gameTime - _lastTime : 0;
        _lastTime = gameTime;
        _previousPlaying = playing;

        // Absence alone is not evidence that an accepted mutation has reached the next snapshot.
        foreach (var subject in subjects)
            if (subject.Valid && subject.Durability >= 0 && subject.Durability <= HeavyShieldDurabilityMigration.Maximum &&
                _accepted.TryGetValue(subject.Receipt, out var accepted) &&
                (accepted.Life != subject.Life || accepted.Durability != subject.Durability))
                _accepted.Remove(subject.Receipt);

        _interrupted.Clear();
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            var job = _queue[i];
            int index = Find(subjects, job.Receipt, job.Life);
            if (index >= 0 && (subjects[index].Durability != job.Durability || subjects[index].LastThreatAt != job.LastThreatAt))
                _interrupted.Add(job.Receipt);
            if (index >= 0 && Eligible(subjects[index], gameTime) && !_interrupted.Contains(job.Receipt))
                continue;
            _queue.RemoveAt(i);
            if (i == 0) ClearActive();
        }

        if (_queue.Count > 0 && Phase != HeavyShieldMaintenancePhase.Idle &&
            !subjects[Find(subjects, _queue[0].Receipt, _queue[0].Life)].AtShop)
        {
            Phase = HeavyShieldMaintenancePhase.Travelling;
            WorkedSeconds = 0;
        }
        if (!playing) return;

        _additions.Clear();
        foreach (var subject in subjects)
        {
            if (!Eligible(subject, gameTime) || IsQueued(subject.Receipt, subject.Life) ||
                _interrupted.Contains(subject.Receipt) || _accepted.ContainsKey(subject.Receipt))
                continue;
            _additions.Add(subject);
        }
        _additions.Sort((left, right) => left.Receipt.CompareTo(right.Receipt));
        foreach (var subject in _additions)
            _queue.Add(new Job(subject.Receipt, subject.Life, subject.Durability, subject.LastThreatAt));

        if (_queue.Count == 0) return;
        var head = _queue[0];
        var current = subjects[Find(subjects, head.Receipt, head.Life)];
        if (Phase == HeavyShieldMaintenancePhase.Idle)
        {
            Phase = current.AtShop ? HeavyShieldMaintenancePhase.Repairing : HeavyShieldMaintenancePhase.Travelling;
            return;
        }
        if (!current.AtShop)
        {
            Phase = HeavyShieldMaintenancePhase.Travelling;
            WorkedSeconds = 0;
            return;
        }
        if (Phase == HeavyShieldMaintenancePhase.Travelling)
        {
            Phase = HeavyShieldMaintenancePhase.Repairing;
            WorkedSeconds = 0;
            return;
        }
        if (Phase == HeavyShieldMaintenancePhase.Repairing)
        {
            WorkedSeconds += Math.Min(delta, WorkSeconds - WorkedSeconds);
            if (WorkedSeconds >= WorkSeconds) Phase = HeavyShieldMaintenancePhase.CompletionDue;
        }
    }

    internal bool TryTakeCompletion(out HeavyShieldRepairCompletion completion)
    {
        completion = default;
        if (!_latestPlaying || Phase != HeavyShieldMaintenancePhase.CompletionDue) return false;
        if (_serialCounter == long.MaxValue)
        {
            // Exhaustion is permanent for this policy instance, including across Reset.
            Phase = HeavyShieldMaintenancePhase.HeldUnknown;
            return false;
        }
        var job = _queue[0];
        _completion = new HeavyShieldRepairCompletion(++_serialCounter, job.Receipt, job.Life,
            _world, _shopLife, job.Durability);
        Phase = HeavyShieldMaintenancePhase.AwaitingResult;
        completion = _completion;
        return true;
    }

    internal bool ResolveCompletion(long serial, bool accepted, bool uncertain)
    {
        if (Phase != HeavyShieldMaintenancePhase.AwaitingResult || serial != _completion.Serial)
            return false;
        if (uncertain)
        {
            Phase = HeavyShieldMaintenancePhase.HeldUnknown;
            return true;
        }
        if (accepted) _accepted[_queue[0].Receipt] = _queue[0];
        _queue.RemoveAt(0);
        ClearActive();
        _previousPlaying = false;
        return true;
    }

    internal void Reset()
    {
        ClearJobs();
        _accepted.Clear();
        _world = _shopLife = 0;
        _lastTime = double.NaN;
        _previousPlaying = _latestPlaying = false;
        // Keep _serialCounter: an old completion must never resolve a new owner/world's job.
    }

    private void ClearActive()
    {
        Phase = HeavyShieldMaintenancePhase.Idle;
        WorkedSeconds = 0;
        _completion = default;
    }

    private void ClearJobs()
    {
        _queue.Clear();
        ClearActive();
    }

    private static bool Eligible(in HeavyShieldMaintenanceSubject subject, double gameTime) =>
        subject.Valid && subject.Durability > 0 && subject.Durability < HeavyShieldDurabilityMigration.Maximum &&
        !subject.InBanner && !subject.Threatened && gameTime - subject.LastThreatAt >= QuietSeconds;

    private static bool ValidRoster(IReadOnlyList<HeavyShieldMaintenanceSubject> subjects)
    {
        if (subjects == null || subjects.Count > 4) return false;
        for (int i = 0; i < subjects.Count; i++)
        {
            var subject = subjects[i];
            if (subject.Receipt == Guid.Empty || subject.Life <= 0 ||
                !double.IsFinite(subject.LastThreatAt) || subject.LastThreatAt < 0) return false;
            for (int j = 0; j < i; j++)
                if (subjects[j].Receipt == subject.Receipt) return false;
        }
        return true;
    }

    private static int Find(IReadOnlyList<HeavyShieldMaintenanceSubject> subjects, Guid receipt, long life)
    {
        for (int i = 0; i < subjects.Count; i++)
            if (subjects[i].Receipt == receipt && subjects[i].Life == life) return i;
        return -1;
    }
}
