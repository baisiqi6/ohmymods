using System;

namespace KingdomEnhancedMod
{
    // Explicit plain boundaries. No Unity, BepInEx, native call, real journal IO or save write.
    internal enum PalacePaidPreparationOutcome
    {
        WaitingForOwner,
        Attempted
    }

    internal enum PalacePaidStage { Unknown, SourceCaptured, Resumable, Returned, DepartureClaimed }
    internal enum PalacePaidStoreReadStatus { Valid, Held, Missing }

    internal static class HostFixture
    {
        internal static bool OwnerReady = true;
        internal static bool ThrowOwner;
        internal static int OwnerId = 1;
        internal static int TryOwnerCalls;

        internal static bool EnsureStoreOk = true;
        internal static string StoreReason = "fixture-store";
        internal static int EnsureStoreCalls;
        internal static PalacePaidJournalStore Store;
        internal static int StoreReadCalls;
        internal static bool ThrowRead;
        internal static bool HasEnvelope = true;
        internal static PalacePaidStoreReadStatus ReadStatus = PalacePaidStoreReadStatus.Valid;
        internal static string ReadError = "";
        internal static int ReadOwnerId = 1;
        internal static int ReadSourceLand = 1;
        internal static PalacePaidStage ReadStage = PalacePaidStage.Resumable;
        internal static string ReadTripId = "11111111-1111-1111-1111-111111111111";

        internal static bool CanRestore;
        internal static int CanRestoreCalls;
        internal static bool CanRestoreLastValid;
        internal static bool CanRestoreLastSameOwner;
        internal static bool BuildColdOk = true;
        internal static bool ThrowCold;
        internal static string BuildColdReason = "fixture-cold";
        internal static int ColdBuilt;
        internal static PalacePaidStage ColdStage = PalacePaidStage.DepartureClaimed;
        internal static bool ExactPayerOk = true;
        internal static bool Transient;
        internal static int BindCalls;
        internal static bool ThrowBind;
        internal static bool PreparedHandleAvailable;
        internal static bool ColdArrivalOk = true;
        internal static int ColdArrivalCalls;
        internal static bool ColdSawMarkerSet;
        internal static int Logs;

        internal static bool MarkerSet;
        internal static IntPtr MarkerPointer;
        internal static int EventSequence;
        internal static HostScene CurrentScene;

        internal static void Reset()
        {
            OwnerReady = true; ThrowOwner = false; OwnerId = 1; TryOwnerCalls = 0;
            EnsureStoreOk = true; StoreReason = "fixture-store"; EnsureStoreCalls = 0;
            Store = null; StoreReadCalls = 0; ThrowRead = false; HasEnvelope = true;
            ReadStatus = PalacePaidStoreReadStatus.Valid; ReadError = ""; ReadOwnerId = 1;
            ReadSourceLand = 1; ReadStage = PalacePaidStage.Resumable;
            ReadTripId = "11111111-1111-1111-1111-111111111111";
            CanRestore = false; CanRestoreCalls = 0; CanRestoreLastValid = false; CanRestoreLastSameOwner = false;
            BuildColdOk = true; ThrowCold = false; BuildColdReason = "fixture-cold"; ColdBuilt = 0;
            ColdStage = PalacePaidStage.DepartureClaimed;
            ExactPayerOk = true; Transient = false; BindCalls = 0; ThrowBind = false;
            PreparedHandleAvailable = false; ColdArrivalOk = true; ColdArrivalCalls = 0; ColdSawMarkerSet = false;
            Logs = 0; MarkerSet = false; MarkerPointer = IntPtr.Zero; EventSequence = 0; CurrentScene = null;
            PalaceIslandTravel.HasActiveJourney = false;
            PalacePaidHandle.Engine.HostReset();
            PalacePaidHandle.Engine.HostInstalled = true;
        }

        internal static PalacePaidStoreReadResult NewReadResult()
        {
            if (!HasEnvelope) return new PalacePaidStoreReadResult { Status = ReadStatus, Error = ReadError };
            return new PalacePaidStoreReadResult
            {
                Status = ReadStatus,
                Error = ReadError,
                Envelope = new PalacePaidEnvelope
                {
                    Current = new PalacePaidRecord
                    {
                        TripId = Guid.Parse(ReadTripId),
                        Owner = new PalacePaidScope(ReadOwnerId),
                        SourceLand = ReadSourceLand,
                        Stage = ReadStage
                    }
                }
            };
        }
    }

    internal sealed class PalacePaidScope
    {
        internal readonly int Id;
        internal PalacePaidScope(int id) { Id = id; }
        internal bool ContentEquals(PalacePaidScope other) => other != null && other.Id == Id;
    }

    internal sealed class PalacePaidJournalStore
    {
        internal PalacePaidStoreReadResult Read(PalacePaidScope owner)
        {
            HostFixture.StoreReadCalls++;
            if (HostFixture.ThrowRead) throw new InvalidOperationException("fixture-read-fault");
            return HostFixture.NewReadResult();
        }
    }

    internal sealed class PalacePaidStoreReadResult
    {
        internal PalacePaidStoreReadStatus Status;
        internal string Error = "";
        internal PalacePaidEnvelope Envelope;
    }

    internal sealed class PalacePaidEnvelope
    {
        internal PalacePaidRecord Current;
    }

    internal sealed class PalacePaidRecord
    {
        internal Guid TripId;
        internal PalacePaidScope Owner;
        internal int SourceLand;
        internal PalacePaidStage Stage;
    }

    internal sealed class Game
    {
        internal IntPtr Pointer;
        internal State state;
        internal int currentLand;
        internal enum State { Intro, Playing, Menu }
    }

    internal sealed class Kingdom
    {
        internal IntPtr Pointer;
        internal Player playerOne;
    }

    internal sealed class Player { }

    internal sealed class PayableComponent { }

    internal sealed class Managers
    {
        internal Game game;
        internal Kingdom kingdom;
        internal Managers(Game game, Kingdom kingdom) { this.game = game; this.kingdom = kingdom; }
    }

    internal sealed partial class PalacePaidHandle
    {
        internal PalacePaidStage Stage { get; set; }

        internal static partial class Engine
        {
            internal static bool HostInstalled { get => _installed; set => _installed = value; }
            internal static IntPtr HostPreparedGame => _preparedGame;
            internal static PalacePaidScope HostPreparedOwner => _preparedOwner;
            internal static PalacePaidStoreReadResult HostPrepared => _prepared;
            internal static string HostPrepareFault { get => _prepareFault; set => _prepareFault = value; }
            internal static PalacePaidHandle HostHandle { get => _handle; set => _handle = value; }

            internal static void HostReset()
            {
                _hookOwner = null; _installed = false; _installAttempted = false; _store = null;
                _preparedOwner = null; _preparedGame = IntPtr.Zero; _prepared = null;
                _prepareFault = "not-prepared"; _directory = ""; _handle = null; _captureSequence = 0;
            }

            private static bool TryOwner(Game game, Kingdom kingdom, out PalacePaidScope owner, out string reason)
            {
                HostFixture.TryOwnerCalls++;
                if (HostFixture.ThrowOwner) throw new InvalidOperationException("fixture-owner-fault");
                owner = new PalacePaidScope(HostFixture.OwnerId);
                reason = HostFixture.OwnerReady ? "fixture-owner" : "fixture-owner-unavailable";
                return HostFixture.OwnerReady;
            }

            private static bool EnsureStore(out PalacePaidJournalStore store, out string reason)
            {
                HostFixture.EnsureStoreCalls++;
                if (HostFixture.Store == null) HostFixture.Store = new PalacePaidJournalStore();
                store = HostFixture.Store;
                reason = HostFixture.StoreReason;
                return HostFixture.EnsureStoreOk;
            }

            private static bool CanRestoreSourceAtPlaying(bool hasHandle, bool hasTransient, bool validRead,
                bool sameOwner, int actualLand, int recordSource, PalacePaidStage stage)
            {
                HostFixture.CanRestoreCalls++;
                HostFixture.CanRestoreLastValid = validRead;
                HostFixture.CanRestoreLastSameOwner = sameOwner;
                return HostFixture.CanRestore;
            }

            private static bool TryBuildColdHandle(PalacePaidScope owner, Guid id, int sourceLand,
                PalacePaidJournalStore store, PalacePaidStoreReadResult read,
                out PalacePaidHandle handle, out string reason)
            {
                if (HostFixture.ThrowCold) throw new InvalidOperationException("fixture-cold-fault");
                HostFixture.ColdBuilt++;
                reason = HostFixture.BuildColdReason;
                if (!HostFixture.BuildColdOk) { handle = null; return false; }
                handle = new PalacePaidHandle { Stage = HostFixture.ColdStage };
                return true;
            }

            private static bool HasTransientResponsibility() => HostFixture.Transient;

            private static bool ExactPayer(Kingdom kingdom, Player payer) => HostFixture.ExactPayerOk;

#nullable enable
            private static void BindLive(PalacePaidHandle handle, Game game, Kingdom kingdom, Player payer, object? payable)
#nullable restore
            {
                HostFixture.BindCalls++;
                if (HostFixture.ThrowBind) throw new InvalidOperationException("fixture-bind-fault");
            }
        }
    }

    internal static partial class PalacePaidResponsibility
    {
        internal static bool TryGetPreparedHandle(Game game, Kingdom kingdom, Player payer,
            out PalacePaidHandle handle, out string reason)
        {
            handle = PalacePaidHandle.Engine.HostHandle;
            reason = "fixture-prepared-handle";
            return HostFixture.PreparedHandleAvailable && handle != null;
        }

        internal static bool ObserveExactColdArrival(PalacePaidHandle handle, Game game, out string reason)
        {
            HostFixture.ColdArrivalCalls++;
            HostFixture.ColdSawMarkerSet = HostFixture.CurrentScene != null
                && HostFixture.CurrentScene.PaidPreparationMarker == game.Pointer;
            reason = "fixture-cold-arrival";
            return HostFixture.ColdArrivalOk;
        }
    }

    internal sealed partial class HostScene
    {
        private IntPtr _paidPreparationGameValue;
        internal IntPtr _paidPreparationGame
        {
            get => _paidPreparationGameValue;
            set
            {
                _paidPreparationGameValue = value;
                if (value == IntPtr.Zero)
                {
                    HostFixture.MarkerSet = false;
                    HostFixture.MarkerPointer = IntPtr.Zero;
                }
                else
                {
                    HostFixture.MarkerSet = true;
                    HostFixture.MarkerPointer = value;
                    HostFixture.EventSequence++;
                }
            }
        }

        internal int TickCalls;
        internal int PostPaidPrepareReached;
        internal int WaitCalls;
        internal string LastWaitReason = "";
        internal IntPtr PaidPreparationMarker => _paidPreparationGame;

        internal void WaitFor(string reason)
        {
            WaitCalls++;
            LastWaitReason = reason;
        }
    }

    internal static class PalaceLand12Runtime
    {
        internal const int LandIndex = 12;
    }

    internal static class PalaceIslandTravel
    {
        internal static bool HasActiveJourney;
    }

    internal static class PalaceVisibleEntry
    {
        internal static void Log(string message) { HostFixture.Logs++; }
    }
}
