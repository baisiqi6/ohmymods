using System;
using System.Collections.Generic;

namespace KingdomEnhancedMod
{
    // Explicit plain boundaries. No Unity, BepInEx, native call, real sidecar IO or save write.
    internal static class HostFixture
    {
        internal static bool BindOk = true;
        internal static bool OwnerKnown = true;
        internal static bool ThrowRead;
        internal static bool StructureReadable = true;
        internal static string Guid = "11111111-1111-1111-1111-111111111111";
        internal static string ReadReason = "missing";
        internal static int Slot;
        internal static int OwnerReign = 1;
        internal static int ReadCalls;
        internal static PalaceCourtArea.CourtAreaMetadataRead Metadata;
        internal static PalaceSavedIsland12Facts Structure = new PalaceSavedIsland12Facts
        {
            TileObjectCount = 11,
            BeachObjectCount = 2,
            BeachLeftOk = true,
            BeachRightOk = true,
            TileGridOk = true
        };

        internal static void Reset()
        {
            BindOk = true;
            OwnerKnown = true;
            ThrowRead = false;
            StructureReadable = true;
            Guid = "11111111-1111-1111-1111-111111111111";
            ReadReason = "missing";
            Slot = 0;
            OwnerReign = 1;
            ReadCalls = 0;
            Metadata = null;
            Structure = new PalaceSavedIsland12Facts
            {
                TileObjectCount = 11,
                BeachObjectCount = 2,
                BeachLeftOk = true,
                BeachRightOk = true,
                TileGridOk = true
            };
        }
    }

    internal sealed class Game
    {
        internal IntPtr Pointer;
        internal Game(IntPtr pointer) { Pointer = pointer; }
    }

    internal sealed class Managers
    {
        internal static Managers Inst;
        internal Game game;
        internal Managers(Game game) { this.game = game; }
    }

    internal sealed class GlobalSaveData
    {
        internal static GlobalSaveData loaded;
        internal IntPtr Pointer;
        internal GlobalSaveData(IntPtr pointer) { Pointer = pointer; }
    }

    internal sealed class CampaignSaveData
    {
        internal IntPtr Pointer;
        internal int reign;
        internal List<IslandSaveData> _islands = new List<IslandSaveData>();
        internal CampaignSaveData(IntPtr pointer) { Pointer = pointer; }
    }

    internal sealed class IslandSaveData
    {
        internal IntRange _levelEdges = new IntRange { min = -22, max = 22 };
    }

    internal struct IntRange
    {
        internal int min;
        internal int max;
    }

    internal sealed class PalaceSavedIsland12Facts
    {
        internal int TileObjectCount;
        internal bool TileGridOk;
        internal int BeachObjectCount;
        internal bool BeachLeftOk;
        internal bool BeachRightOk;
    }

    internal static class PalaceLand12Runtime
    {
        internal const int LandIndex = 12;
    }

    internal static class PalaceDedicatedEndpoints
    {
        internal const string BeachPrefabPath = "Palace/Beach";
        internal const float OwnedBeachLeftX = -14.6875f;
        internal const float OwnedBeachRightX = 14.6875f;
    }

    internal enum PalaceDestinationSupport
    {
        UnsupportedOrUnknown,
        Fresh,
        SupportedSaved
    }

    internal static class PalaceTripContext
    {
        internal static bool TryBinding(GlobalSaveData global, CampaignSaveData campaign,
            out string guid, out int slot, out string reason)
        {
            guid = HostFixture.Guid;
            slot = HostFixture.Slot;
            reason = "fixture binding";
            return HostFixture.BindOk;
        }
    }

    internal static class PalaceSaveBindingSource
    {
        internal static bool TryGetSemanticOwner(out string guid, out int slot, out int biome,
            out int reign, out int startIsland, out string reason)
        {
            guid = HostFixture.Guid;
            slot = HostFixture.Slot;
            biome = 5;
            reign = HostFixture.OwnerReign;
            startIsland = 1;
            reason = "fixture owner";
            return HostFixture.OwnerKnown;
        }
    }

    internal static class PalaceCourtArea
    {
        internal sealed class CourtAreaMetadataRead
        {
            internal string CampaignGuid = HostFixture.Guid;
            internal int CampaignSlot;
            internal int Reign = 1;
            internal int NativeEdgesMin = -22;
            internal int NativeEdgesMax = 22;
        }

        internal static CourtAreaMetadataRead ReadSidecar(out string reason)
        {
            HostFixture.ReadCalls++;
            reason = HostFixture.ReadReason;
            if (HostFixture.ThrowRead) throw new InvalidOperationException("fixture read failure");
            return HostFixture.Metadata;
        }
    }

    // Structural boundary of the extracted decision method. The real parser remains production-owned;
    // this host only feeds explicit facts so every acceptance/refusal gate can be exercised.
    internal static partial class PalaceDestinationAdmission
    {
        internal static bool TryReadSavedStructureFacts(IslandSaveData island,
            out PalaceSavedIsland12Facts facts, out string reason)
        {
            if (!HostFixture.StructureReadable)
            {
                facts = null;
                reason = "fixture-structure-fault";
                return false;
            }
            facts = HostFixture.Structure;
            reason = "fixture structure";
            return true;
        }
    }
}
