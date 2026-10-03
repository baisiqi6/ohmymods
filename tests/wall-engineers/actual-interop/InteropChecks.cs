using System;
using System.Reflection;
using Il2CppSystem.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

// Compile shells for the plugin bootstrap and global switch (the real files are not part of this
// interop slice). Production code reads ModConfig.Enabled.Value; the shell exposes the same shape.
internal sealed class ConfigEntryShell<T>
{
    public T Value => throw new NotSupportedException();
}

internal static class ModConfig
{
    public static ConfigEntryShell<bool> Enabled => throw new NotSupportedException();
}

internal sealed class Logger
{
    public void LogWarning(string message) { }
    public void LogInfo(string message) { }
}

internal sealed class KingdomEnhancedPlugin
{
    public static KingdomEnhancedPlugin Instance { get; set; }
    public Logger LogSource { get; } = new Logger();
}

/// <summary>
/// Reflection report over the actual 2.4 interop assemblies for every member the wall-engineer
/// slice assumes. Name strings only: this compiles even when an assumption is wrong and reports
/// the exact member. The primary signature gate is the direct production compile in this project.
/// </summary>
internal static class InteropChecks
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static int Run(Action<string> report)
    {
        int missing = 0;

        // Load boundary.
        missing += Method(report, typeof(IslandSaveData), "TryPopObjectsToScene", Type.EmptyTypes);
        missing += Method(report, typeof(CampaignSaveData), "ApplyToScene", Type.EmptyTypes);
        missing += Member(report, typeof(CampaignSaveData), "CurrentIsland");
        missing += Member(report, typeof(CampaignSaveData), "current");
        missing += Member(report, typeof(IslandSaveData), "poppingObjectsToScene");

        // Kingdom roster / walls / bookkeeping hook targets.
        missing += Member(report, typeof(Kingdom), "Workers");
        missing += Member(report, typeof(Kingdom), "outerWall");
        missing += Method(report, typeof(Kingdom), "AddWork", new[] { typeof(Workable) });
        missing += Method(report, typeof(Kingdom), "RemoveWork", new[] { typeof(Workable) });
        missing += Method(report, typeof(Kingdom), "CheckWork", new[] { typeof(Workable) });
        missing += Method(report, typeof(Kingdom), "ReassignWork", new[] { typeof(Workable) });
        missing += Method(report, typeof(Kingdom), "IsWithinWalls", new[] { typeof(float) });
        missing += Method(report, typeof(Kingdom), "AddWorker", new[] { typeof(Worker) });
        missing += Method(report, typeof(Kingdom), "RemoveWorker", new[] { typeof(Worker) });
        missing += Method(report, typeof(Kingdom), "AddWall", new[] { typeof(Wall) });
        missing += Method(report, typeof(Kingdom), "DestroyWall", new[] { typeof(Wall) });
        missing += Method(report, typeof(Kingdom), "ReplaceWall", new[] { typeof(Wall) });
        missing += Method(report, typeof(Kingdom), "OnDisable", Type.EmptyTypes);
        missing += Method(report, typeof(Kingdom), "CalculateBordersNow", Type.EmptyTypes);
        missing += Member(report, typeof(Side), "Left");
        missing += Member(report, typeof(Side), "Right");

        // Worker surfaces.
        missing += Method(report, typeof(Worker), "ShouldFlee", Type.EmptyTypes);
        missing += Method(report, typeof(Worker), "OnDisable", Type.EmptyTypes);
        missing += Method(report, typeof(Worker), "ResetWorkState", new[] { typeof(bool) });
        missing += Member(report, typeof(Worker), "behaviour");
        missing += Member(report, typeof(Worker), "_currentWork");
        missing += Member(report, typeof(Worker), "_queuedWork");
        missing += Member(report, typeof(Worker), "_enemyScanner");

        // Wall surfaces.
        missing += Member(report, typeof(Wall), "_workableBuilding");
        missing += Member(report, typeof(Wall), "_damageable");
        missing += Member(report, typeof(Wall), "isIntact");
        missing += Method(report, typeof(Wall), "GetTotalMaxHitPoints", Type.EmptyTypes);
        missing += Method(report, typeof(Wall), "UpdateGraphics", Type.EmptyTypes);
        missing += Method(report, typeof(Wall), "OnDisable", Type.EmptyTypes);
        missing += Method(report, typeof(Wall), "HandleOnBecameUnSafe", Type.EmptyTypes);
        missing += Method(report, typeof(Wall), "HandleOnReceiveDamage", new[] { typeof(int), typeof(GameObject), typeof(DamageSource) });
        missing += Member(report, typeof(WorkableBuilding), "UnderConstruction");

        // Damageable.
        missing += Member(report, typeof(Damageable), "initialHitPoints");
        missing += Member(report, typeof(Damageable), "hitPoints");
        missing += Member(report, typeof(Damageable), "isDead");
        missing += Member(report, typeof(Damageable), "useHitPoints");

        // Haglet behaviour (repo-proven Cast pattern).
        missing += Member(report, typeof(Coatsink.Common.Haglet), "latestGoto");
        missing += Member(report, typeof(Coatsink.Common.Haglet), "started");

        // G3 scanner geometry / query.
        missing += Method(report, typeof(Scanner), "ComputeCorners", new[] { typeof(Transform), typeof(float), typeof(float), typeof(float), typeof(bool) });
        missing += Member(report, typeof(Scanner), "_pointA");
        missing += Member(report, typeof(Scanner), "_pointB");
        missing += Member(report, typeof(Scanner), "_contactFilter");
        missing += Member(report, typeof(Scanner), "_lastRefresh");
        missing += Member(report, typeof(Scanner), "observer");
        missing += Member(report, typeof(Scanner), "range");
        missing += Member(report, typeof(Scanner), "rangeBehind");
        missing += Member(report, typeof(Scanner), "_height");
        missing += Method(report, typeof(Physics2D), "OverlapArea",
            new[] { typeof(Vector2), typeof(Vector2), typeof(ContactFilter2D), typeof(List<Collider2D>) });

        // Context / transport.
        missing += Member(report, typeof(Managers), "Inst");
        missing += Member(report, typeof(Managers), "kingdom");
        missing += Member(report, typeof(Managers), "world");
        missing += Member(report, typeof(Managers), "game");
        missing += Member(report, typeof(World), "gameLayer");
        missing += Member(report, typeof(Game), "state");
        missing += Member(report, typeof(Game), "State");
        missing += Member(report, typeof(NetworkBigBoss), "IsOnline");
        missing += Member(report, typeof(NetworkBigBoss), "HasWorldAuth");

        return missing;
    }

    private static int Method(Action<string> report, Type type, string name, Type[] args)
    {
        try
        {
            MethodInfo method = type.GetMethod(name, Any, null, args, null);
            return Report(report, type, name, method != null);
        }
        catch (Exception ex)
        {
            report?.Invoke("[ERR ] " + type.Name + "." + name + ": " + ex.GetType().Name);
            return 1;
        }
    }

    private static int Member(Action<string> report, Type type, string name)
    {
        try
        {
            bool found = type.GetProperty(name, Any) != null || type.GetField(name, Any) != null
                || type.GetNestedType(name, Any) != null;
            return Report(report, type, name, found);
        }
        catch (Exception ex)
        {
            report?.Invoke("[ERR ] " + type.Name + "." + name + ": " + ex.GetType().Name);
            return 1;
        }
    }

    private static int Report(Action<string> report, Type type, string name, bool found)
    {
        report?.Invoke((found ? "[ OK ] " : "[MISS] ") + type.Name + "." + name);
        return found ? 0 : 1;
    }
}
