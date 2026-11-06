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

    /// <summary>CanvasGroup double: 243 root group exposed through Menu.get__canvasGroup.
    /// The public interactable setter is the only API the production patch may use.</summary>
    public class CanvasGroup
    {
        private static long _nextPointer = 0x5000;

        public IntPtr Pointer { get; }
        public bool ThrowOnSet;
        private bool _interactable = true;

        public CanvasGroup() : this(new IntPtr(System.Threading.Interlocked.Increment(ref _nextPointer))) { }
        public CanvasGroup(IntPtr pointer) { Pointer = pointer; }

        public bool interactable
        {
            get => _interactable;
            set
            {
                if (ThrowOnSet) throw new InvalidOperationException("CanvasGroup.interactable set failed");
                _interactable = value;
            }
        }
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

/// <summary>Map double: State enum mirrors MapTimelineMenu.cs:645-657 verbatim.</summary>
public class MapTimelineMenu
{
    public enum State { Closing, Closed, ShowingMapOnly, ShowingWithTimeline, Opening }
    public State CurrentState { get; set; } = State.Closed;
}

/// <summary>Menu double: IsMenuShown / Hide / HideOne / interactable follow the actual
/// Windows 2.4.3 PE (native-menu-review/native-facts.json): the setter at RVA 0x60AB60
/// writes BOTH the internal field (+0x4B0) and the root CanvasGroup (+0x498), with the
/// idempotent guard on the internal field only; Hide at RVA 0x5FF8A0 synchronously sets the
/// active map targetDepth to 0 and bare-writes the internal field false, leaving the root
/// CanvasGroup untouched (0x5FFE1A). The ~0.6s close tail is modeled by Env.FinishMenuTail;
/// the tail's bare _interactable=false by ExitBareInteractableFalse.</summary>
public class Menu
{
    public enum HideFailure { None, BeforeDepth, AfterDepthBeforeInternal, AfterDepth }

    private static long _nextPointer = 0x1000;

    public static Menu Inst;
    public static bool InstExists;

    /// <summary>Stable native identity; production binds responsibility to this, never to the
    /// managed wrapper (IL2CPP can hand out replacement wrappers for the same native object).</summary>
    public IntPtr Pointer { get; }

    public bool ShowMainPanelStarted;
    public int targetDepth;
    public float Pts; // native pts stash (Menu.cs:710)
    public MapTimelineMenu ActiveMap;
    public bool CanOpen = true; // Menu.cs:33-43 refcount, flat bool here
    public int HideCalls, HideOneCalls, OnButtonCloseCalls;
    public int InteractableTrueWrites, InteractableFalseWrites; // effective setter writes only
    public UnityEngine.CanvasGroup _canvasGroup { get; set; } // native get__canvasGroup/set__canvasGroup
    public HideFailure HideFailureMode; // fault injection for the close retry contract
    private bool _interactable;

    public Menu() : this(new IntPtr(System.Threading.Interlocked.Increment(ref _nextPointer)),
        new UnityEngine.CanvasGroup()) { }
    public Menu(IntPtr pointer) : this(pointer, new UnityEngine.CanvasGroup()) { }
    public Menu(IntPtr pointer, UnityEngine.CanvasGroup canvasGroup)
    {
        Pointer = pointer;
        _canvasGroup = canvasGroup;
    }

    /// <summary>Convenience view for fixtures: the current native root group's interactable.</summary>
    public bool RootCanvasInteractable
    {
        get => _canvasGroup != null && _canvasGroup.interactable;
        set { if (_canvasGroup != null) _canvasGroup.interactable = value; }
    }

    public bool interactable
    {
        get => _interactable;
        set
        {
            if (_interactable == value) return; // Menu.cs:226 idempotent guard (internal field only)
            _interactable = value;
            if (_canvasGroup != null) _canvasGroup.interactable = value; // setter writes root group too
            if (value) InteractableTrueWrites++;
            else InteractableFalseWrites++;
        }
    }

    public static bool IsMenuShown
    {
        get
        {
            if (!InstExists || Inst == null) return false;
            return Inst.ShowMainPanelStarted || Inst.targetDepth > 0
                || (Inst.ActiveMap != null && Inst.ActiveMap.CurrentState != MapTimelineMenu.State.Closed);
        }
    }

    public void Hide()
    {
        HideCalls++;
        if (HideFailureMode == HideFailure.BeforeDepth)
            throw new InvalidOperationException("native Hide failed before targetDepth write");
        // Menu.Hide 0x5FF90D loads ActiveMap (+0x4B8), 0x5FF92E calls its targetDepth
        // setter (vtable+0x348) with 0.
        targetDepth = 0;
        if (HideFailureMode == HideFailure.AfterDepthBeforeInternal)
            throw new InvalidOperationException("native Hide failed after depth but before final internal write");
        _interactable = false; // Menu.Hide 0x5FFE1A: bare internal false, root CanvasGroup untouched
        if (HideFailureMode == HideFailure.AfterDepth)
            throw new InvalidOperationException("native Hide failed after targetDepth write");
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

    /// <summary>Fixture seed outside the setter (serializer-like); never counted. Both layers
    /// are explicit so a test can model a native bare flag write that left the root group raw.</summary>
    public void SetRaw(bool interactable, bool rootCanvasInteractable)
    {
        _interactable = interactable;
        RootCanvasInteractable = rootCanvasInteractable;
    }

    /// <summary>Menu.cs:986 exit path: bare field write, bypasses the counted setter.</summary>
    public void ExitBareInteractableFalse() => _interactable = false;
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
    /// <summary>true = TryShowMenu only arms the run signal (real 1-2 frame delay);
    /// PumpRunSignal models Game.Update processing it into _ShowMainPanel.</summary>
    public bool DeferredMenuOpen;
    private bool _pendingMenuOpen;
    private int _pendingPlayerId;
    private bool _pendingInteractable = true;

    /// <summary>Native Game.TryShowMenu body (Game.cs:2316-2358 → run signal → Menu state +
    /// _ShowMainPanel: started, depth :733, the :724 interactable write, and the solo-only
    /// timeScale freeze Menu.cs:709-712). DeferredMenuOpen parks the signal instead.</summary>
    public void TryShowMenu(int playerId, bool interactable = true)
    {
        NativeTryShowMenuCalls++;
        if (state == State.Loading) return; // Game.cs:2318
        Menu menu = Menu.Inst;
        if (menu == null || !menu.CanOpen) return; // Game.cs:2326
        if (DeferredMenuOpen)
        {
            _pendingMenuOpen = true;
            _pendingPlayerId = playerId;
            _pendingInteractable = interactable;
            return;
        }
        OpenMenuNow(playerId, interactable);
    }

    /// <summary>Game.Update processes the pending ShowMenu run signal.</summary>
    public void PumpRunSignal()
    {
        if (!_pendingMenuOpen) return;
        _pendingMenuOpen = false;
        OpenMenuNow(_pendingPlayerId, _pendingInteractable);
    }

    private void OpenMenuNow(int playerId, bool interactable)
    {
        state = State.Menu;
        Menu menu = Menu.Inst;
        if (menu == null) return;
        menu.ShowMainPanelStarted = true;
        menu.targetDepth = 1;               // Menu.cs:733
        menu.interactable = interactable;   // Menu.cs:724 counter-write (the gate must fight this)
        if (NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.IsClientPresent)
        {
            menu.Pts = UnityEngine.Time.timeScale; // Menu.cs:709
            UnityEngine.Time.timeScale = 0f;       // Menu.cs:712
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

// 复刻 CursorSystem 的 force 位语义（真实实现见 CursorSystem.cs:19-36）。
// 字段名与生产反射读取一致（公开替身可被 Public|NonPublic 的 GetField 命中）。
public class CursorSystem
{
    public static CursorSystem Inst;
    public static bool InstExists;
    public bool forceVisibleCursor;
    public void SetForceVisibleCursor(bool visible) => forceVisibleCursor = visible;
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
