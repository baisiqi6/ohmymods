using System;

namespace Fixture.Model
{
    // Minimal boundary model for the extracted UpdatePaymentGates slice. Every member used by
    // the extracted method exists here; no production logic is duplicated or translated.
    internal enum HeavyShieldPurchaseKind : byte { Mold, ExtraLeft, ExtraRight, ShieldLeft, ShieldRight }

    internal sealed class Player
    {
        internal readonly int Id;
        internal Player(int id) { Id = id; }
    }

    internal sealed class Payment
    {
        internal HeavyShieldPurchaseKind Kind { get; }
        internal bool Busy;
        internal Payment(HeavyShieldPurchaseKind kind) { Kind = kind; }
    }

    internal sealed class Point
    {
        internal readonly int Id;
        internal readonly Payment Payment;
        private Payable _payable;
        internal Payable Payable
        {
            get { Box.NotePayableRead(Id); return _payable; }
            set { _payable = value; }
        }
        internal Player Payer;
        internal bool Disabled;
        internal Point(int id, HeavyShieldPurchaseKind kind) { Id = id; Payment = new Payment(kind); }
        internal bool? SnapshotValue => _payable == null ? (bool?)null : _payable.Value;
    }

    internal sealed class Payable
    {
        internal readonly int PointId;
        internal bool Value;
        internal bool forceBlockPayment
        {
            get => Value;
            set => Box.Write(this, value);
        }
        internal Payable(int pointId) { PointId = pointId; }
    }

    internal static class HeavyShieldIdentity
    {
        internal static bool CanReservePurchase(HeavyShieldPurchaseKind kind) => Box.CanReserve(kind);
    }

    internal static class HeavyShieldRuntime
    {
        internal static bool CarrierPreflightReady => Box.CarrierReady;
    }

    internal static class IslandSaveData
    {
        internal static bool isSavingGame => Box.Saving;
    }

    // Countable read-only boundary stubs. No stub allocates on the measured path and none
    // dispatches Mod/business callbacks except CanPay, which is a real business boundary.
    internal static class Box
    {
        internal const int Capacity = 64;

        internal struct WriteEntry { internal int PointId; internal bool Value; }
        internal struct ReserveEntry { internal HeavyShieldPurchaseKind Kind; }
        internal struct CanPayEntry { internal int Index; internal long Life; internal int PlayerId; }
        internal struct Mutation
        {
            internal byte Kind; // 0 none, 1 bow=false, 2 bow=true, 3 busy=true, 4 busy=false
            internal int Arg;
            internal Mutation(byte kind, int arg) { Kind = kind; Arg = arg; }
        }

        // Scenario configuration (preallocated, replaced only outside measured loops).
        internal static bool BowValue;
        internal static bool CarrierReady;
        internal static bool Saving;
        internal static bool[] Quota = new bool[5];
        internal static bool[] CanPayAllow = new bool[8];
        internal static Mutation[] CanPayMutations = new Mutation[8];
        internal static int ThrowBowAt, ThrowReserveAt, ThrowCanPayAt, ThrowSetAt;
        internal static Point[] Points;

        // Counters and fixed-capacity call logs.
        internal static int BowCalls, AnyBusyCalls, ReserveCalls, CanPayCalls, SetCalls, PayableReads;
        internal static WriteEntry[] Writes = new WriteEntry[Capacity];
        internal static int WriteCount;
        internal static WriteEntry[] Attempts = new WriteEntry[Capacity];
        internal static int AttemptCount;
        internal static ReserveEntry[] Reserves = new ReserveEntry[Capacity];
        internal static int ReserveCount;
        internal static CanPayEntry[] CanPays = new CanPayEntry[Capacity];
        internal static int CanPayCount;
        internal static int[] PayableReadIds = new int[Capacity];
        internal static int PayableReadCount;

        private static bool _bowThrown, _reserveThrown, _canPayThrown, _setThrown;

        internal static void ResetCountersOnly()
        {
            BowCalls = AnyBusyCalls = ReserveCalls = CanPayCalls = SetCalls = PayableReads = 0;
            WriteCount = AttemptCount = ReserveCount = CanPayCount = PayableReadCount = 0;
            _bowThrown = _reserveThrown = _canPayThrown = _setThrown = false;
        }

        internal static void ResetRun()
        {
            ResetCountersOnly();
            BowValue = false; CarrierReady = false; Saving = false;
            Array.Clear(Quota, 0, Quota.Length);
            Array.Clear(CanPayAllow, 0, CanPayAllow.Length);
            Array.Clear(CanPayMutations, 0, CanPayMutations.Length);
            ThrowBowAt = ThrowReserveAt = ThrowCanPayAt = ThrowSetAt = 0;
            Points = null;
        }

        internal static bool BowUsable()
        {
            BowCalls++;
            if (ThrowBowAt > 0 && BowCalls == ThrowBowAt && !_bowThrown)
            {
                _bowThrown = true;
                throw new InvalidOperationException("first-bow-call");
            }
            return BowValue;
        }

        internal static bool AnyBusy(Point[] points)
        {
            AnyBusyCalls++;
            if (points != null)
                foreach (var point in points)
                    if (point != null && point.Payment.Busy) return true;
            return false;
        }

        internal static bool CanReserve(HeavyShieldPurchaseKind kind)
        {
            ReserveCalls++;
            if (ReserveCount < Capacity) Reserves[ReserveCount++] = new ReserveEntry { Kind = kind };
            if (ThrowReserveAt > 0 && ReserveCalls == ThrowReserveAt && !_reserveThrown)
            {
                _reserveThrown = true;
                throw new InvalidOperationException("first-reserve-call");
            }
            return Quota[(int)kind];
        }

        internal static bool CanPay(int index, long life, Player player)
        {
            CanPayCalls++;
            if (CanPayCount < Capacity)
                CanPays[CanPayCount++] = new CanPayEntry { Index = index, Life = life, PlayerId = player == null ? -1 : player.Id };
            if (ThrowCanPayAt > 0 && CanPayCalls == ThrowCanPayAt && !_canPayThrown)
            {
                _canPayThrown = true;
                throw new InvalidOperationException("first-canpay-call");
            }
            if (index >= 0 && index < CanPayMutations.Length)
            {
                var mutation = CanPayMutations[index];
                switch (mutation.Kind)
                {
                    case 1: BowValue = false; break;
                    case 2: BowValue = true; break;
                    case 3:
                        if (Points != null && mutation.Arg >= 0 && mutation.Arg < Points.Length && Points[mutation.Arg] != null)
                            Points[mutation.Arg].Payment.Busy = true;
                        break;
                    case 4:
                        if (Points != null && mutation.Arg >= 0 && mutation.Arg < Points.Length && Points[mutation.Arg] != null)
                            Points[mutation.Arg].Payment.Busy = false;
                        break;
                    case 5:
                        BowValue = false;
                        if (Points != null && mutation.Arg >= 0 && mutation.Arg < Points.Length && Points[mutation.Arg] != null)
                            Points[mutation.Arg].Payment.Busy = false;
                        break;
                }
            }
            return index >= 0 && index < CanPayAllow.Length && CanPayAllow[index];
        }

        internal static void Write(Payable payable, bool value)
        {
            SetCalls++;
            if (AttemptCount < Capacity)
                Attempts[AttemptCount++] = new WriteEntry { PointId = payable.PointId, Value = value };
            if (ThrowSetAt > 0 && SetCalls == ThrowSetAt && !_setThrown)
            {
                _setThrown = true;
                throw new InvalidOperationException("first-setter-call");
            }
            payable.Value = value;
            if (WriteCount < Capacity)
                Writes[WriteCount++] = new WriteEntry { PointId = payable.PointId, Value = value };
        }

        internal static void NotePayableRead(int pointId)
        {
            PayableReads++;
            if (PayableReadCount < Capacity) PayableReadIds[PayableReadCount++] = pointId;
        }
    }
}
