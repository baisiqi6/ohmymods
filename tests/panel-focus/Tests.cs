// Tests.cs: behavior regression for il2cpp/PatchUI_PanelFocus.cs (linked unmodified).
// Run with: dotnet run --project tests/panel-focus
// Every scenario drives the production Tick / Prefix / InstallInputGate /
// ShouldRunNativeTryShowMenu around faithful copies of the vanilla Game.Update pump,
// CheckForPause, TryShowMenu and Menu depth flows, so assertions are about observable
// timeScale / menu-depth / input-body behavior, never about production internals.
using System;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("engage_freezes_and_close_restores_timescale", EngageAndRestoreFormula);
        Run("zero_timescale_never_engages", ZeroTimeScaleNeverEngages);
        Run("network_gates_never_engage", NetworkGatesNeverEngage);
        Run("open_edge_state_gate_never_touches_native_overlay", OpenEdgeStateGate);
        Run("open_edge_closes_overlay_with_a_single_hide", OpenEdgeSingleHide);
        Run("engage_waits_for_menu_tail_then_freezes", EngageWaitsForMenuTail);
        Run("esc_window_game_update_runs_first", EscWindowGameUpdateFirst);
        Run("esc_window_panel_update_runs_first", EscWindowPanelUpdateFirst);
        Run("f5_and_fault_close_do_not_suppress_the_menu", NonEscCloseDoesNotSuppress);
        Run("disengage_writes_only_in_playable_zero_state", DisengageWriteBranches);
        Run("native_ts_overwrite_during_engage_never_rezeroes", NativeOverwriteNoReengage);
        Run("master_toggle_off_still_pauses_and_restores", MasterToggleOffStillWorks);
        Run("input_gate_covers_both_local_players", InputGateCoversBothPlayers);
        Run("input_gate_resolution_fails_closed", InputGateResolveFailsClosed);
        Run("prefix_semantics_follow_panel_visibility_only", PrefixSemantics);

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "ALL PASS (" + _passed + " scenarios)"
            : _failed + " FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------- scenarios

    private static void EngageAndRestoreFormula()
    {
        Env env = new Env();
        env.InstallGate();
        UnityEngine.Time.timeScale = 1.25f;
        env.OpenPanel();
        True(PatchUI_PanelFocus.Engaged, "opening the panel in a playable solo world engages");
        Eq(0f, UnityEngine.Time.timeScale, "engaged freezes timeScale");
        Eq(1, Lines("input gate installed on Player.IControllable_ReceiveInput").Count,
            "install line logged exactly once (O-3)");
        var engagedLines = Lines("[PanelFocus] engaged");
        Eq(1, engagedLines.Count, "exactly one engage line per engage");
        True(engagedLines[0].Contains("IControllable_ReceiveInput"),
            "the engage line carries the resolved hook name");
        env.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "closing clears Engaged");
        Eq(1.25f, UnityEngine.Time.timeScale, "closing restores the exact stashed timeScale");
        Eq(0, env.Menu.HideCalls, "no native overlay behind: the open edge never calls Hide");
    }

    private static void ZeroTimeScaleNeverEngages()
    {
        Env env = new Env();
        UnityEngine.Time.timeScale = 0f; // somebody else already paused the world
        env.OpenPanel();
        True(!PatchUI_PanelFocus.Engaged, "ts==0 must never engage");
        Eq(0f, UnityEngine.Time.timeScale, "no write while ts==0");
        env.ClosePanelByF5();
        Eq(0f, UnityEngine.Time.timeScale, "closing writes nothing when never engaged");
    }

    private static void NetworkGatesNeverEngage()
    {
        Env host = new Env();
        NetworkBigBoss.IsClientPresent = true;
        host.OpenPanel();
        True(!PatchUI_PanelFocus.Engaged, "host with a client present mirrors native: no pause");
        Eq(1f, UnityEngine.Time.timeScale, "host-with-client timeScale untouched");
        Eq(0, host.Menu.HideCalls, "no overlay shown: no Hide on the open edge");

        Env client = new Env(Game.State.NetworkClientPlaying);
        NetworkBigBoss.HasWorldAuth = false;
        client.OpenPanel();
        True(!PatchUI_PanelFocus.Engaged, "client machines never pause");
        Eq(1f, UnityEngine.Time.timeScale, "client timeScale untouched");
    }

    private static void OpenEdgeStateGate()
    {
        // (state, mainSceneActive, menuInstanceExists); the native overlay is fully shown in
        // every row: front-end / SailingAway / coop-prompt states must not touch it.
        var rows = new (Game.State state, bool mainScene, bool instExists)[]
        {
            (Game.State.SailingAway, true, true),
            (Game.State.Loading, true, true),
            (Game.State.WaitingForP2, true, true),
            (Game.State.Playing, false, true),
            (Game.State.Menu, true, false),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            Env env = new Env(rows[i].state);
            ProgramDirector.MainSceneActive = rows[i].mainScene;
            Menu.InstExists = rows[i].instExists;
            env.Menu.ShowMainPanelStarted = true;
            env.Menu.targetDepth = 2;
            env.OpenPanel();
            Eq(0, env.Menu.HideCalls, "row " + i + ": the gate must block Hide");
            Eq(2, env.Menu.targetDepth, "row " + i + ": native depth untouched");
            Eq(0, env.Menu.OnButtonCloseCalls, "row " + i + ": OnButtonClose never called");
            Eq(0, env.Menu.HideOneCalls, "row " + i + ": HideOne never called");
            True(!PatchUI_PanelFocus.Engaged, "row " + i + ": never engages either");
        }
    }

    private static void OpenEdgeSingleHide()
    {
        // Menu + map stacked (Menu.cs:913-929): started, depth 2, map Opening.
        Env stacked = new Env(Game.State.Menu);
        stacked.Menu.ShowMainPanelStarted = true;
        stacked.Menu.targetDepth = 2;
        stacked.Menu.ActiveMap = new MapTimeline { CurrentState = MapTimeline.State.Opening };
        stacked.OpenPanel();
        Eq(1, stacked.Menu.HideCalls, "exactly one Hide on the open edge");
        Eq(0, stacked.Menu.OnButtonCloseCalls, "OnButtonClose must never be called");
        Eq(0, stacked.Menu.HideOneCalls, "HideOne must never be called");
        Eq(0, stacked.Menu.targetDepth, "Hide writes the absolute 0");
        True(stacked.Menu.targetDepth >= 0, "targetDepth never goes negative");

        // Pure map mid-close (Closing): the same single-Hide path.
        Env closing = new Env(Game.State.Playing);
        closing.Menu.ActiveMap = new MapTimeline { CurrentState = MapTimeline.State.Closing };
        closing.OpenPanel();
        Eq(1, closing.Menu.HideCalls, "closing map: single Hide path");
        Eq(0, closing.Menu.OnButtonCloseCalls, "closing map: OnButtonClose never called");
        Eq(0, closing.Menu.HideOneCalls, "closing map: HideOne never called");
        Eq(0, closing.Menu.targetDepth, "closing map: depth stays at 0");
        True(!PatchUI_PanelFocus.Engaged, "map still shown: engage waits");
        closing.Menu.ActiveMap.CurrentState = MapTimeline.State.Closed;
        PatchUI_PanelFocus.Tick();
        True(PatchUI_PanelFocus.Engaged, "engage fires once the map coroutine exits");
    }

    private static void EngageWaitsForMenuTail()
    {
        Env env = new Env(Game.State.Playing);
        env.InstallGate();
        StubRewired esc = new StubRewired();

        // ESC first: the native menu opens (state=Menu, native freezes ts at 1).
        esc.Press(5);
        NativeGameLoop.Update(env, esc);
        Eq(1, env.Game.NativeTryShowMenuCalls, "setup: the native menu opened");
        True(env.Game.state == Game.State.Menu, "setup: native menu state");
        Eq(0f, UnityEngine.Time.timeScale, "setup: native solo freeze");

        // F5 with the menu behind: one Hide, engage waits for the ~0.6s tail.
        Env.PanelUpdate(env, escDown: false, f5Down: true);
        True(ModPanel.IsShown, "setup: panel open");
        Eq(1, env.Menu.HideCalls, "the open edge closed the menu behind");
        True(env.Menu.ShowMainPanelStarted, "the menu tail keeps started true");
        True(!PatchUI_PanelFocus.Engaged, "IsMenuShown not yet exited: no engage");
        Eq(0f, UnityEngine.Time.timeScale, "ts still the native 0 during the tail");

        // Tail ends: native restores ts, the game returns to playing; the panel freezes.
        Env.FinishMenuTail(env);
        PatchUI_PanelFocus.Tick();
        True(PatchUI_PanelFocus.Engaged, "engage fires after the tail");
        Eq(0f, UnityEngine.Time.timeScale, "panel freeze after the tail");

        env.ClosePanelByF5();
        // 恢复值即 _pts 的可观测证明：stash 捕获的是原生恢复后的 1，不是尾巴期的 0。
        Eq(1f, UnityEngine.Time.timeScale, "closing restores the native value, not the tail 0");
    }

    private static void EscWindowGameUpdateFirst()
    {
        Env env = new Env(Game.State.Playing);
        env.InstallGate();
        ModPanel.IsShown = true;
        PatchUI_PanelFocus.Tick();
        StubRewired esc = new StubRewired();

        // Frame N, Game.Update first: the same Esc press reaches TryShowMenu while the panel
        // is still shown (CheckForPause reads rewired directly, never through the input gate).
        esc.Press(5);
        NativeGameLoop.Update(env, esc);
        Eq(0, env.Game.NativeTryShowMenuCalls, "game-first: the press must not open the menu");
        Eq(1, env.Game.BlockedTryShowMenuCalls, "blocked by the panel flag");
        esc.EndFrame();

        // Same frame later: ModPanel.Update closes the panel and arms the window.
        Env.PanelUpdate(env, escDown: true, f5Down: false);
        True(!ModPanel.IsShown, "esc closed the panel");

        // Frame N+1, still inside the 0.25s window: a real press stays blocked.
        esc.Press(5);
        NativeGameLoop.Update(env, esc);
        Eq(0, env.Game.NativeTryShowMenuCalls, "game-first: still blocked inside the window");
        esc.EndFrame();

        // Past the window: the menu opens normally.
        Env.Advance(0.26f);
        esc.Press(5);
        NativeGameLoop.Update(env, esc);
        Eq(1, env.Game.NativeTryShowMenuCalls, "game-first: after the window the menu opens");
        True(env.Game.state == Game.State.Menu, "the native menu really opened");
    }

    private static void EscWindowPanelUpdateFirst()
    {
        Env env = new Env(Game.State.Playing);
        env.InstallGate();
        ModPanel.IsShown = true;
        PatchUI_PanelFocus.Tick();
        StubRewired esc = new StubRewired();

        // Frame N, ModPanel.Update first: the closing press shuts the panel and arms the
        // window before Game.Update sees the very same Esc.
        esc.Press(5);
        Env.PanelUpdate(env, escDown: true, f5Down: false);
        NativeGameLoop.Update(env, esc);
        Eq(0, env.Game.NativeTryShowMenuCalls, "panel-first: the closing press must not open the menu");
        Eq(1, env.Game.BlockedTryShowMenuCalls, "blocked by the window, not the panel flag");
        esc.EndFrame();

        Env.Advance(0.26f);
        esc.Press(5);
        NativeGameLoop.Update(env, esc);
        Eq(1, env.Game.NativeTryShowMenuCalls, "panel-first: after the window the menu opens");
    }

    private static void NonEscCloseDoesNotSuppress()
    {
        Env f5Close = new Env(Game.State.Playing);
        ModPanel.IsShown = true;
        PatchUI_PanelFocus.Tick();
        Env.PanelUpdate(f5Close, escDown: false, f5Down: true); // F5 toggle close
        StubRewired esc = new StubRewired();
        esc.Press(5);
        NativeGameLoop.Update(f5Close, esc);
        Eq(1, f5Close.Game.NativeTryShowMenuCalls, "F5 close arms no window: the menu opens at once");

        Env faultClose = new Env(Game.State.Playing);
        ModPanel.IsShown = true;
        PatchUI_PanelFocus.Tick();
        ModPanel.IsShown = false; // OnGUI fault close: never passes through Update
        PatchUI_PanelFocus.Tick();
        esc.EndFrame();
        esc.Press(5);
        NativeGameLoop.Update(faultClose, esc);
        Eq(1, faultClose.Game.NativeTryShowMenuCalls, "fault close also arms no window");
    }

    private static void DisengageWriteBranches()
    {
        // a) Engaged with ts still 0, state leaves the playable set: clear only, no write.
        Env left = new Env(Game.State.Playing);
        left.OpenPanel();
        True(PatchUI_PanelFocus.Engaged, "a: setup engaged");
        left.Game.state = Game.State.Loss;
        left.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "a: flag cleared");
        Eq(0f, UnityEngine.Time.timeScale, "a: no write when the state left playable");

        // b) game==null (context torn down): clear only, no write.
        Env gone = new Env(Game.State.Playing);
        gone.OpenPanel();
        Managers.Inst.game = null;
        gone.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "b: flag cleared");
        Eq(0f, UnityEngine.Time.timeScale, "b: no write on game==null");

        // c) Engaged but native already moved ts: clear only, native wins.
        Env native = new Env(Game.State.Playing);
        native.OpenPanel();
        UnityEngine.Time.timeScale = 0.4f; // e.g. the crown-loss slow-motion tween
        native.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "c: flag cleared");
        Eq(0.4f, UnityEngine.Time.timeScale, "c: native value wins, never overwritten");
    }

    // operator 修订回归：Engaged 期间原生改写时标（皇冠失落慢动作只认原生 IsMenuShown）。
    // 无 !Engaged 守卫时，下一个 Tick 的 Engage 条件 ts>0 重新为真 → _pts 被覆写成慢动作值
    // 且 ts 被重新置 0；有守卫时原生值让位（不重新冻结），关面板走"不写"分支由原生收敛。
    private static void NativeOverwriteNoReengage()
    {
        Env env = new Env(Game.State.Playing);
        env.OpenPanel();
        True(PatchUI_PanelFocus.Engaged, "setup engaged at ts=1");
        Eq(0f, UnityEngine.Time.timeScale, "setup frozen");

        UnityEngine.Time.timeScale = 0.4f; // crown-loss tween writes over our 0
        Env.PanelUpdate(env, escDown: false, f5Down: false); // panel stays open, Tick re-evaluates
        Eq(0.4f, UnityEngine.Time.timeScale, "native value wins: no re-zero while engaged");
        True(PatchUI_PanelFocus.Engaged, "still engaged (no disengage trigger)");

        env.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "flag cleared on close");
        Eq(0.4f, UnityEngine.Time.timeScale, "native value kept on close (original pts=1 abandoned to native)");
    }

    private static void MasterToggleOffStillWorks()
    {
        Env env = new Env(Game.State.Playing);
        ModConfig.Enabled.Value = false; // P1-2: the panel is infrastructure, not gameplay
        env.InstallGate();
        env.OpenPanel();
        True(PatchUI_PanelFocus.Engaged, "engage is not gated by ModConfig.Enabled");
        Eq(0f, UnityEngine.Time.timeScale, "frozen despite the master toggle off");
        StubRewired pad = new StubRewired();
        NativeGameLoop.Update(env, pad);
        Eq(0, env.P1.InputTicks, "the input gate still blocks with the master toggle off");
        env.ClosePanelByF5();
        Eq(1f, UnityEngine.Time.timeScale, "restore still happens with the master toggle off");
        NativeGameLoop.Update(env, pad);
        Eq(1, env.P1.InputTicks, "input flows again after the close");
    }

    private static void InputGateCoversBothPlayers()
    {
        Env env = new Env(Game.State.Playing);
        env.Game._secondaryControllable = env.P2; // Game.ResetInput local pair
        env.InstallGate();
        StubRewired pad = new StubRewired();

        ModPanel.IsShown = true;
        PatchUI_PanelFocus.Tick();
        NativeGameLoop.Update(env, pad);
        Eq(0, env.P1.InputTicks, "player one body skipped while shown");
        Eq(0, env.P2.InputTicks, "player two body skipped through the same wired method");
        Eq(1, env.P1.GateSkips, "player one skip counted");
        Eq(1, env.P2.GateSkips, "player two skip counted");
        Eq(1, PanelFocusHarness.PatchedPrefixes.Count, "exactly one method is wired");

        ModPanel.IsShown = false;
        PatchUI_PanelFocus.Tick();
        NativeGameLoop.Update(env, pad);
        Eq(1, env.P1.InputTicks, "player one input flows after the close");
        Eq(1, env.P2.InputTicks, "player two input flows after the close");
        Eq(0, env.Game.BlockedTryShowMenuCalls, "plain input never touches the menu path");
    }

    private static void InputGateResolveFailsClosed()
    {
        Env env = new Env(Game.State.Playing);
        PatchUI_PanelFocus.InstallInputGate(new HarmonyLib.Harmony(),
            typeof(PlayerWithoutInterfaceImplementation));
        Eq(0, PanelFocusHarness.PatchedPrefixes.Count, "nothing wired when resolution fails");
        Eq(1, Lines("input gate target not found").Count, "resolution failure logged once as ERROR");
        True(Lines("input gate target not found")[0].StartsWith("ERROR ", StringComparison.Ordinal),
            "the failure line is an ERROR, not a silent skip");
        env.OpenPanel();
        True(PatchUI_PanelFocus.Engaged, "the pause state machine is unaffected by a missing gate");

        // A later install with the real type still wires and works.
        env.InstallGate();
        Eq(1, PanelFocusHarness.PatchedPrefixes.Count, "a later correct install still wires");
        StubRewired pad = new StubRewired();
        NativeGameLoop.Update(env, pad);
        Eq(0, env.P1.InputTicks, "the late gate blocks input while shown");
    }

    private static void PrefixSemantics()
    {
        ModPanel.IsShown = false;
        True(PatchUI_PanelFocus.Prefix(), "hidden panel lets the native input body run");
        ModPanel.IsShown = true;
        True(!PatchUI_PanelFocus.Prefix(), "shown panel skips the native input body");
        ModPanel.IsShown = false;
        True(PatchUI_PanelFocus.Prefix(), "closing restores the native input body");
    }

    // -------------------------------------------------------------- helpers

    private static System.Collections.Generic.List<string> Lines(string needle)
    {
        var hits = new System.Collections.Generic.List<string>();
        foreach (string line in KingdomEnhancedPlugin.Instance.LogSource.Lines)
            if (line.Contains(needle))
                hits.Add(line);
        return hits;
    }

    private static void Run(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message);
        }
    }

    private static void Eq<T>(T expected, T actual, string what)
    {
        if (!Equals(expected, actual))
            throw new Exception(what + ": expected " + expected + ", got " + actual);
    }

    private static void True(bool condition, string what)
    {
        if (!condition)
            throw new Exception(what);
    }
}
