// Compilation shells only: selected methods are copied unchanged from the current production file.
if (args.Length != 3) throw new ArgumentException("Usage: SourceExtractor <retreat|fleet|follow> <source.cs> <output-directory>");
string mode = args[0], source = File.ReadAllText(args[1]), output = args[2];
Directory.CreateDirectory(output);

int Find(string marker, int start = 0)
{
    int index = source.IndexOf(marker, start, StringComparison.Ordinal);
    return index >= 0 ? index : throw new InvalidOperationException("Production boundary missing: " + marker);
}
string Block(int start)
{
    int open = Find("{", start), depth = 0;
    for (int i = open; i < source.Length; i++)
    {
        if (source[i] == '{') depth++;
        if (source[i] == '}' && --depth == 0) return source[start..(i + 1)];
    }
    throw new InvalidOperationException("Unbalanced production block");
}
void Write(string file, string content) => File.WriteAllText(Path.Combine(output, file), content);

switch (mode)
{
    case "retreat":
    {
        string method = Block(Find("    internal static bool DayAssembleSpreadPrefix("));
        int attribute = Find("[HarmonyPatch(typeof(Mover), nameof(Mover.SetGoal), new[] { typeof(float), typeof(float) })]");
        int declaration = Find("public static class Mover_DefenseSpacing_DayAssemble_Spread_Patch", attribute);
        string hook = source[attribute..declaration] + Block(declaration);
        Write("ExtractedPrefix.cs", """
using System;
using UnityEngine;
using HarmonyLib;
namespace KingdomEnhancedMod;
internal static class PatchWorld_DefenseSpacing
{
    private static readonly System.Collections.Generic.Dictionary<int,int> _moverUnitType = new();
    private static bool _inSetGoalRedirect = false;
    public static int KnightBranches, ArcherBranches;
    public static float BranchGoal, BranchSpeed;
    private static bool KnightDayAssembleSpread(Mover mover, float goal, float speed)
    { KnightBranches++; BranchGoal = goal; BranchSpeed = speed; return true; }
    private static bool MirrorNightArcherGoal(Mover mover, float goal, float speed)
    { ArcherBranches++; BranchGoal = goal; BranchSpeed = speed; return true; }
""" + "\n" + method + "\n}\n" + hook + "\n");
        break;
    }
    case "follow":
        Write("ExtractedDefenseSpacing.cs", """
using System;
using UnityEngine;
namespace KingdomEnhancedMod;
internal static class PatchWorld_DefenseSpacing
{
""" + "\n" + Block(Find("    internal static bool NightFollowerAnchorPrefix(")) + "\n}\n");
        break;
    case "fleet":
    {
        int start = Find("    private static bool TryBuildCandidates(");
        string method = source[start..Find("    private static bool TryExpand(", start)];
        Write("CandidateSource.cs", """
using System;
using System.Collections.Generic;
using UnityEngine;
namespace KingdomEnhancedMod;
internal static class CandidateSource
{
    private const int MaxFleetBoats = 4;
    private static void LogFailureOnce(string s, Exception e) { }
    internal static bool Build(Player p, Formation f, Kingdom k, Transform root, Side side, List<FleetBoat> boats)
        => TryBuildCandidates(p, f, k, root, side, boats);
""" + "\n" + method + "\n}\n");
        string finalizer = Block(Find("        private static Exception Finalizer(Exception __exception, ActivationState __state)"));
        Write("FinalizerSource.cs", """
using System;
namespace KingdomEnhancedMod;
internal static class FinalizerSource
{
    private sealed class FormationProfile { internal Formation Formation; }
    private sealed class ActivationState { internal bool Expanded; internal FormationProfile Profile; }
    internal static int RestoreCalls;
    private static bool AllUnitsEmpty(Formation f) => f.members.Count == 0;
    private static void TryRestoreBaseline(FormationProfile p, bool ignored) { RestoreCalls++; }
    private static void LogFailureOnce(string s, Exception e) { }
    internal static Exception Run(Exception e, Formation f, bool expanded = true)
        => Finalizer(e, new ActivationState { Expanded = expanded, Profile = new FormationProfile { Formation = f } });
""" + "\n" + finalizer + "\n}\n");
        break;
    }
    default: throw new ArgumentException("Unknown extraction mode: " + mode);
}
