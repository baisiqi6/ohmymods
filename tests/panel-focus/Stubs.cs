// Stubs.cs: minimal Unity / game / Harmony / sibling-mod doubles for the panel-focus
// behavior tests. The production file is linked UNMODIFIED (see the csproj); only surfaces
// the production file and the native simulation actually touch are modeled here. Nothing in
// this file reimplements the production algorithm.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(params object[] args) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    public class HarmonyMethod
    {
        public MethodInfo Method;
        public HarmonyMethod(MethodInfo method) { Method = method; }
    }

    /// <summary>Test-side Harmony stand-in: records manual installs, no weaving.</summary>
    public class Harmony
    {
        public void Patch(MethodInfo original, HarmonyMethod prefix = null)
            => PanelFocusHarness.PatchedPrefixes[original] = prefix;
    }
}

namespace UnityEngine
{
    public static class Time
    {
        public static float time;
        public static float deltaTime;
        public static float unscaledTime;
        public static float timeScale = 1f;
    }

    public static class Mathf
    {
        public static bool Approximately(float a, float b)
            => Math.Abs(a - b) <= Math.Max(1e-06f * Math.Max(Math.Abs(a), Math.Abs(b)), 1e-06f * 8f);
    }
}

// ---------------------------------------------------------------------------
// Game assembly doubles (global namespace, like the interop assembly).
// ---------------------------------------------------------------------------

/// <summary>Rewired player double: GetButtonDown is true until EndFrame (native per-frame edge).</summary>
public sealed class StubRewired
{
    private int _downMask;
    public void Press(int action) => _downMask |= 1 << action;
    public bool GetButtonDown(int action) => (_downMask & (1 << action)) != 0;
    public void EndFrame() => _downMask = 0;
}

public class MapTimeline
{
    public enum State { Closed, Opening, Open, Closing }
    public State CurrentState = State.Closed;
}

/// <summary>Menu double: IsMenuShown / Hide / HideOne follow Menu.cs:207-213 / 664-675.
/// The ~0.6s close tail (started staying true after Hide) is modeled by FinishMenuTail.</summary>
public class Menu
{
    public static Menu Inst;
    public static bool InstExists;

    public bool ShowMainPanelStarted;
    public int targetDepth;
    public float Pts; // native pts stash (Menu.cs:710)
    public MapTimeline ActiveMap;
    public int HideCalls, HideOneCalls, OnButtonCloseCalls;

    public static bool IsMenuShown
    {
        get
        {
            if (!InstExists || Inst == null) return false;
            return Inst.ShowMainPanelStarted || Inst.targetDepth > 0
                || (Inst.ActiveMap != null && Inst.ActiveMap.CurrentState != MapTimeline.State.Closed);
        }
    }

    public void Hide()
    {
        HideCalls++;
        targetDepth = 0;
    }

    public void HideOne()
    {
        HideOneCalls++;
        targetDepth--;
    }

    public void OnButtonClose()
    {
        OnButtonCloseCalls++;
        HideOne();
    }
}

public class Game
{
    public enum State
    {
        Loading = 0,
        Intro = 1,
        Playing = 2,
        Menu = 4,
        Loss = 8,
        GreedDefeat = 16,
        SailingAway = 32,
        NetworkClientPlaying = 64,
        NetworkClientEnding = 128,
        WaitingForP2 = 2048,
        SelectingP2Appearance = 4096,
    }

    public State state = State.Playing;
    public Player _currentControllable;
    public Player _secondaryControllable;
    public int NativeTryShowMenuCalls;
    public int BlockedTryShowMenuCalls;

    /// <summary>Native Game.TryShowMenu body (Game.cs:2316 -> run signal -> Menu state +
    /// _ShowMainPanel: started, depth, and the solo-only timeScale freeze Menu.cs:709-712).</summary>
    public void TryShowMenu(int playerId, bool interactable = true)
    {
        NativeTryShowMenuCalls++;
        state = State.Menu;
        Menu menu = Menu.Inst;
        if (menu == null) return;
        menu.ShowMainPanelStarted = true;
        menu.targetDepth = 1;
        if (NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.IsClientPresent)
        {
            menu.Pts = UnityEngine.Time.timeScale;
            UnityEngine.Time.timeScale = 0f;
        }
    }
}

/// <summary>Player double: the explicit interface implementation keeps the interop method
/// name IControllable_ReceiveInput; the body counter is the observable native effect.</summary>
public class Player
{
    public int InputTicks; // native body executions (movement/sprint/pay entry)
    public int GateSkips;  // prefix blocks observed by the harness

    public void IControllable_ReceiveInput(StubRewired rewiredPlayer)
    {
        InputTicks++;
    }
}

/// <summary>Stand-in with no interface-shaped member: drives the fail-closed resolution path.</summary>
public class PlayerWithoutInterfaceImplementation
{
    public void UpdatePayState(bool a, bool b, bool c) { }
}

public class Managers
{
    public static Managers Inst;
    public Game game;
}

public static class ProgramDirector
{
    public static bool MainSceneActive = true;
    public static bool IsMainSceneActive() => MainSceneActive;
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsClientPresent = false;
}

// ---------------------------------------------------------------------------
// Sibling mod API doubles (namespace KingdomEnhancedMod; merged with the linked
// production file, which is the only place these are consumed from).
// ---------------------------------------------------------------------------
namespace KingdomEnhancedMod
{
    public class ConfigEntry<T>
    {
        public T Value;
    }

    public static class ModConfig
    {
        public static ConfigEntry<bool> Enabled = new ConfigEntry<bool> { Value = true };
    }

    /// <summary>Root panel flag double (production deliberately never reads ModConfig here).</summary>
    public static class ModPanel
    {
        public static bool IsShown;
    }

    public class ManualLogSource
    {
        public readonly List<string> Lines = new List<string>();
        public void LogInfo(string message) => Lines.Add("INFO " + message);
        public void LogWarning(string message) => Lines.Add("WARN " + message);
        public void LogError(string message) => Lines.Add("ERROR " + message);
    }

    public class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();
        public ManualLogSource LogSource = new ManualLogSource();
    }
}
