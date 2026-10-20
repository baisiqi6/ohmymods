using System;

namespace KingdomEnhancedMod
{
    /// <summary>One read-only departure preimage. Source.CurrentLand is the real source, never the target.</summary>
    internal sealed class MountIslandDeparturePreimage
    {
        internal readonly long Epoch;
        internal readonly object Show, Confirmation, Token;
        internal readonly MountIslandOwnerIdentity Source;
        internal readonly int TargetLand;
        internal readonly MountIslandHistoryView History;

        internal MountIslandDeparturePreimage(long epoch, object show, object confirmation, object token,
            MountIslandOwnerIdentity source, int targetLand, MountIslandHistoryView history)
        {
            Epoch = epoch; Show = show; Confirmation = confirmation; Token = token;
            Source = source; TargetLand = targetLand; History = history;
        }
    }

    /// <summary>Bound once to the real target request and its issued gate generation.</summary>
    internal sealed class MountIslandTransitionProof
    {
        private readonly MountIslandDeparturePreimage _departure;
        private readonly MountIslandOwnerIdentity _target;
        private readonly ulong _routine;
        private readonly long _generation;

        internal MountIslandTransitionProof(MountIslandDeparturePreimage departure,
            MountIslandOwnerIdentity target, ulong routine, long generation)
        {
            _departure = departure; _target = target; _routine = routine; _generation = generation;
        }

        internal long Epoch => _departure.Epoch;
        internal int SourceLand => _departure.Source.CurrentLand;

        internal bool Matches(MountIslandOwnerIdentity owner, ulong routine, long generation, int land)
            => _departure != null && _departure.TargetLand == land && _target.CurrentLand == land
                && routine != 0UL && routine == _routine && generation == _generation
                && MountIslandOwnerIdentity.Matches(_target, owner);

        internal bool AllowsHistory(MountIslandHistoryView current)
            => _departure != null && _departure.History.ReadsOk && !_departure.History.AnyPositive
                && current.ReadsOk && !current.NonMapPositive
                && !double.IsNaN(current.MapDays) && !double.IsInfinity(current.MapDays) && current.MapDays >= 0d
                && (_departure.History.SlotPointer == 0UL
                    || _departure.History.SlotPointer == current.SlotPointer);
    }

    /// <summary>One show, one pending transition, one take. No retries or persisted permission.</summary>
    internal sealed class MountIslandTransitionHistory
    {
        private long _epoch;
        private object _show;
        private MountIslandDeparturePreimage _pending;

        internal void Begin(object show)
        {
            _epoch++; _show = show; _pending = null;
        }

        internal void Cancel(object show)
        {
            if (!ReferenceEquals(_show, show)) return;
            _epoch++; _show = null; _pending = null;
        }

        internal MountIslandDeparturePreimage Capture(object show, object confirmation, object token,
            MountIslandOwnerIdentity source, int target, MountIslandHistoryView history)
        {
            if (show == null || confirmation == null || token == null || !ReferenceEquals(show, _show)
                || !source.IsComplete || source.CurrentLand == target
                || !MountIslandSplitPolicy.IsExtensionLand(target)) return null;
            return new MountIslandDeparturePreimage(_epoch, show, confirmation, token, source, target, history);
        }

        internal bool Publish(MountIslandDeparturePreimage pre, object confirmation, object token,
            MountIslandOwnerIdentity source, bool committedAndCurrent)
        {
            if (pre == null || pre.Epoch != _epoch || !ReferenceEquals(_show, pre.Show)) return false;
            // A failed/dirty capture closes only its own show; an old completion cannot clear a new one.
            if (!committedAndCurrent || !ReferenceEquals(pre.Confirmation, confirmation)
                || !ReferenceEquals(pre.Token, token) || !MountIslandOwnerIdentity.Matches(pre.Source, source)
                || !pre.History.ReadsOk || pre.History.AnyPositive)
            {
                Cancel(pre.Show);
                return false;
            }
            _pending = pre;
            return true;
        }

        internal MountIslandDeparturePreimage TakePending()
        {
            MountIslandDeparturePreimage pre = _pending;
            _pending = null;
            return pre;
        }

        internal bool TryBindTaken(MountIslandDeparturePreimage pre, MountIslandOwnerIdentity target, int land, ulong routine, long generation,
            MountIslandHistoryView current, out MountIslandTransitionProof proof)
        {
            proof = null;
            if (pre == null || pre.Epoch != _epoch || !ReferenceEquals(pre.Show, _show)
                || !target.IsComplete || target.CurrentLand != land || pre.TargetLand != land
                || routine == 0UL || generation < 0) return false;
            MountIslandOwnerIdentity source = pre.Source;
            if (source.CurrentLand == target.CurrentLand
                || source.GlobalPointer != target.GlobalPointer
                || source.SelectedCampaignIndex != target.SelectedCampaignIndex
                || source.CampaignPointer != target.CampaignPointer || source.ReignIndex != target.ReignIndex
                || source.LandDataPointer != target.LandDataPointer || source.IslandsPointer != target.IslandsPointer
                || source.HolderInstancePointer != target.HolderInstancePointer
                || source.HolderBiomeIndex != target.HolderBiomeIndex) return false;
            var bound = new MountIslandTransitionProof(pre, target, routine, generation);
            if (!bound.AllowsHistory(current)) return false;
            proof = bound;
            return true;
        }
    }

    /// <summary>Explicit policy operand, minted only after this generation frame's live recheck.</summary>
    internal readonly struct MountIslandCurrentTransitionTime
    {
        private readonly object _frame;
        private readonly MountIslandFrameTicket _ticket;
        private readonly MountIslandFreshGate _gate;

        internal MountIslandCurrentTransitionTime(object frame, MountIslandFrameTicket ticket, MountIslandFreshGate gate)
        { _frame = frame; _ticket = ticket; _gate = gate; }

        internal bool IsCurrent => _frame != null && _ticket != null && _gate != null
            && _ticket.Usable && _ticket.TransitionProof != null && _ticket.BelongsToFrame(_frame)
            && _ticket.IssuedGeneration == _gate.Generation;
    }
}
