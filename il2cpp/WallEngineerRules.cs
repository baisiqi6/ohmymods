using System;

namespace KingdomEnhancedMod;

/// <summary>Why the wall-engineer effect is not currently shown as applied.</summary>
internal enum WallEngineerUnavailableReason
{
    None = 0,
    Disabled,
    NoAuthority,
    Loading,
    NoOuterWall,
    NativeEvidenceUnavailable,
    ApplicationFailed,
    NetworkUnsupported,
}

/// <summary>
/// Frozen read-only snapshot for the F5 panel / day HUD. Pure memory: reading it never touches a
/// native object and never writes. <see cref="AppliedMultiplier"/> is 0 unless <see cref="Ready"/>.
/// </summary>
internal readonly struct WallEngineerStatus
{
    internal WallEngineerStatus(
        bool enabled,
        bool authorityAvailable,
        bool ready,
        int appliedMultiplier,
        int eligibleWallCount,
        long version,
        WallEngineerUnavailableReason reason)
    {
        Enabled = enabled;
        AuthorityAvailable = authorityAvailable;
        Ready = ready;
        AppliedMultiplier = appliedMultiplier;
        EligibleWallCount = eligibleWallCount;
        Version = version;
        Reason = reason;
    }

    internal bool Enabled { get; }
    internal bool AuthorityAvailable { get; }
    internal bool Ready { get; }
    internal int AppliedMultiplier { get; }
    internal int EligibleWallCount { get; }
    internal long Version { get; }
    internal WallEngineerUnavailableReason Reason { get; }
}

/// <summary>
/// Pure decision math for the wall-engineer feature. No Unity / IL2CPP types: every rule here is
/// exercised directly by tests/wall-engineers. The runtime owns every native read/write.
/// </summary>
internal static class WallEngineerRules
{
    /// <summary>User-confirmed cap: initial hit points become at most 3x the native value.</summary>
    internal const int MaxMultiplier = 3;

    /// <summary>Every ten living kingdom workers add one multiplier tier, counting busy workers.</summary>
    internal const int WorkersPerMultiplierTier = 10;

    /// <summary>Native Wall job slots per side: at most two engineers actually repair one wall.</summary>
    internal const int MaxEngineersPerWall = 2;

    /// <summary>Left outermost + right outermost; one side alone can still be ready.</summary>
    internal const int MaxEligibleWalls = 2;

    /// <summary>2 walls x 2 native job slots: a hard cap on granted combat-repair exceptions.</summary>
    internal const int MaxQualifiedEngineers = 4;

    internal const float MaintainIntervalSeconds = 0.5f;
    internal const float FaultRetrySeconds = 2f;
    internal const float FaultLogIntervalSeconds = 5f;

    /// <summary>M = min(3, 1 + floor(N / 10)); N is clamped at zero.</summary>
    internal static int MultiplierForWorkers(int workers)
    {
        if (workers <= 0) return 1;
        int multiplier = 1 + workers / WorkersPerMultiplierTier;
        return multiplier >= MaxMultiplier ? MaxMultiplier : multiplier;
    }

    /// <summary>B * M checked: overflow fails closed instead of saturating and reporting success.</summary>
    internal static bool TryAppliedInitial(int baseInitial, int multiplier, out int applied)
    {
        applied = 0;
        if (baseInitial < 0 || multiplier < 1) return false;
        long scaled = (long)baseInitial * multiplier;
        if (scaled > int.MaxValue) return false;
        applied = (int)scaled;
        return true;
    }

    /// <summary>
    /// Current HP is preserved across boundary changes: raising the cap never heals, lowering or
    /// restoring immediately clamps, negative values can never survive.
    /// </summary>
    internal static int ClampedHp(int hp, int maxHitPoints)
    {
        if (maxHitPoints < 0) maxHitPoints = 0;
        if (hp < 0) return 0;
        return hp > maxHitPoints ? maxHitPoints : hp;
    }

    /// <summary>
    /// A wall holds combat-repair eligibility only when its build finished, it is still intact and
    /// it is actually missing HPs. A ruined outermost wall is never replaced by the next inner one.
    /// </summary>
    internal static bool IsWartimeTarget(bool underConstruction, bool intact, bool damaged)
        => !underConstruction && intact && damaged;

    /// <summary>
    /// Collider bounds vs the wall's inner half-space. True when any part of the bounds lies on the
    /// kingdom side of the wall plane: covers the straddle case and fully-inside enemies, not just
    /// the exact crossing tick. <paramref name="wallIsLeftOfKingdom"/> means the interior is +x.
    /// </summary>
    internal static bool BoundsTouchInnerHalfSpace(float boundsMinX, float boundsMaxX, float wallX, bool wallIsLeftOfKingdom)
    {
        if (boundsMinX > boundsMaxX)
        {
            float swap = boundsMinX;
            boundsMinX = boundsMaxX;
            boundsMaxX = swap;
        }
        return wallIsLeftOfKingdom ? boundsMaxX > wallX : boundsMinX < wallX;
    }

    /// <summary>
    /// A worker is inboard of its own wall when it is inside the kingdom and still on the interior
    /// side of the wall plane. The worker must never be granted an exception past its wall.
    /// </summary>
    internal static bool WorkerInboard(bool insideKingdom, float workerX, float wallX, bool wallIsLeftOfKingdom)
    {
        if (!insideKingdom) return false;
        return wallIsLeftOfKingdom ? workerX > wallX : workerX < wallX;
    }

    /// <summary>
    /// Native formation target of one engineer on the wall: wall.x + GetJobOffset(worker) * wall
    /// local scale sign. Must be strictly on the kingdom side of the wall plane to allow the
    /// combat-repair flee exception; unknown orientation keeps the native flee.
    /// </summary>
    internal static bool NativeTargetInboard(float targetX, float wallX, bool wallIsLeftOfKingdom)
        => wallIsLeftOfKingdom ? targetX > wallX : targetX < wallX;

    internal static void NormalizeRect(float ax, float ay, float bx, float by,
        out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = ax < bx ? ax : bx;
        maxX = ax < bx ? bx : ax;
        minY = ay < by ? ay : by;
        maxY = ay < by ? by : ay;
    }

    /// <summary>The queried rectangle must cross the wall plane to be usable for this wall.</summary>
    internal static bool Straddles(float minX, float maxX, float planeX) => minX < planeX && maxX > planeX;

    /// <summary>Any part of [minX,maxX] on the kingdom side of the wall plane.</summary>
    internal static bool InnerIntersectionNonEmpty(float minX, float maxX, float planeX, bool wallIsLeftOfKingdom)
        => wallIsLeftOfKingdom ? maxX > planeX : minX < planeX;

    /// <summary>
    /// Containment without epsilon: the current reach clipped to the wall's inner half-space must be
    /// non-empty and fully inside the rectangle that was physically queried this epoch.
    /// </summary>
    internal static bool CoveredByQuery(float rMinX, float rMinY, float rMaxX, float rMaxY,
        float qMinX, float qMinY, float qMaxX, float qMaxY, float planeX, bool wallIsLeftOfKingdom)
    {
        if (rMaxX <= rMinX || rMaxY <= rMinY) return false;
        if (!InnerIntersectionNonEmpty(rMinX, rMaxX, planeX, wallIsLeftOfKingdom)) return false;
        float innerMinX = wallIsLeftOfKingdom ? (rMinX > planeX ? rMinX : planeX) : rMinX;
        float innerMaxX = wallIsLeftOfKingdom ? rMaxX : (rMaxX < planeX ? rMaxX : planeX);
        return innerMinX >= qMinX && innerMaxX <= qMaxX && rMinY >= qMinY && rMaxY <= qMaxY;
    }
}
