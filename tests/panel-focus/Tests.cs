// Tests.cs: behavior regression for il2cpp/PatchUI_PanelFocus.cs (linked unmodified).
// Run with: dotnet run -c Debug (from tests/panel-focus)
// Every scenario drives the production Tick / Prefix / InstallInputGate around faithful
// copies of the vanilla Game.Update pump, CheckForPause, TryShowMenu, Menu._ShowMainPanel
// (:724 interactable write, :733 depth, :709-712 solo freeze), Menu.Update back-out tail
// (:2729-2736) and Menu close-tail flows, so assertions are about observable timeScale /
// menu-depth / interactable / input-body behavior, never about production internals.
using System;
using KingdomEnhancedMod;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("open_edge_requests_native_menu_and_gate_wins_same_frame", OpenEdgeRequestsMenu);
        Run("non_gate_states_never_touch_menu_full_path", NonGateStatesNeverTouchMenu);
        Run("open_edge_menu_plus_map_pulls_map_once", OpenEdgeMenuPlusMap);
        Run("open_edge_pure_map_insurance", OpenEdgePureMapInsurance);
        Run("open_edge_closing_map_is_left_alone", OpenEdgeClosingMapLeftAlone);
        Run("gate_counters_showmain_true_both_script_orders", GateCountersBothOrders);
        Run("close_ours_hides_once_without_restore", CloseOursHidesWithoutRestore);
        Run("close_user_menu_esc_layers_down_both_orders", CloseUserMenuBothOrders);
        Run("fallback_engage_after_rejected_menu_and_restores", FallbackEngageAfterRejection);
        Run("attempt_window_let_pass_then_menu_claims_ownership", AttemptWindowClaim);
        Run("stale_attempt_never_claims_external_menu", StaleAttemptNeverClaims);
        Run("zero_timescale_never_engages", ZeroTimeScaleNeverEngages);
        Run("network_hosts_and_clients_gate_without_freeze", NetworkGateWithoutFreeze);
        Run("fallback_esc_closes_panel_and_menu_opens_both_orders", FallbackEscBothOrders);
        Run("disengage_writes_only_in_playable_zero_state", DisengageWriteBranches);
        Run("native_ts_overwrite_during_engage_never_rezeroes", NativeOverwriteNoReengage);
        Run("master_toggle_off_still_opens_menu_and_pauses", MasterToggleOffStillWorks);
        Run("input_gate_covers_both_local_players", InputGateCoversBothPlayers);
        Run("input_gate_resolution_fails_closed", InputGateResolveFailsClosed);
        Run("prefix_semantics_follow_panel_visibility_only", PrefixSemantics);
        Run("cursor_forced_globally_and_restored", CursorForcedGlobally);

        Console.WriteLine();
        Console.WriteLine(_failed == 0
            ? "ALL PASS (" + _passed + " scenarios)"
            : _failed + " FAILED, " + _passed + " passed");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------- scenarios

    private static void OpenEdgeRequestsMenu()
    {
        Env env = new Env(Game.State.Playing);
        env.InstallGate();
        env.OpenPanel();
        Eq(1, env.Game.NativeTryShowMenuCalls, "open edge requests the native menu exactly once");
        True(env.Game.state == Game.State.Menu, "the native menu open flips the game state");
        Eq(0f, UnityEngine.Time.timeScale, "solo: the native menu froze the clock (Menu.cs:712)");
        Eq(1, env.Menu.InteractableTrueWrites, ":724 wrote true once during the open");
        Eq(false, env.Menu.interactable, "the per-frame gate wins within the same Tick");
        Eq(1, env.Menu.InteractableFalseWrites, "exactly one effective false write so far");
        Eq(0, env.Menu.HideCalls + env.Menu.HideOneCalls, "nothing is hidden in the plain open");
        True(!PatchUI_PanelFocus.Engaged, "menu mode (state=Menu): the fallback never engages");
        // The request is edge-driven: further frames never re-request.
        Env.Advance(0.1f);
        Env.PanelUpdate(env, escDown: false, f5Down: false);
        Env.PanelUpdate(env, escDown: false, f5Down: false);
        Eq(1, env.Game.NativeTryShowMenuCalls, "still exactly one request across frames");
        Eq(1, env.Menu.InteractableFalseWrites, "the idempotent setter keeps the write count at one");
        Eq(1, Lines("open edge: native menu open requested").Count, "request line logged once");
        Eq(1, Lines("menu gate active").Count, "gate line logged once per session");
    }

    private static void NonGateStatesNeverTouchMenu()
    {
        // (state, mainSceneActive, menuInstanceExists); the native menu is fully shown in
        // every row: front-end / SailingAway / loading / waiting states must never touch it
        // through the whole open -> per-frame -> close path (P0-1).
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
            env.Menu.SetRaw(true);
            env.OpenPanel();
            Env.PanelUpdate(env, escDown: false, f5Down: false);
            Env.PanelUpdate(env, escDown: false, f5Down: false);
            env.ClosePanelByF5();
            Eq(0, env.Game.NativeTryShowMenuCalls, "row " + i + ": no TryShowMenu inside the open edge");
            Eq(0, env.Menu.HideCalls, "row " + i + ": close path never Hides");
            Eq(0, env.Menu.HideOneCalls, "row " + i + ": never HideOnes");
            Eq(0, env.Menu.OnButtonCloseCalls, "row " + i + ": OnButtonClose never called");
            Eq(0, env.Menu.InteractableTrueWrites + env.Menu.InteractableFalseWrites,
                "row " + i + ": zero interactable writes in both directions");
            Eq(true, env.Menu.interactable, "row " + i + ": the menu stays exactly as it was");
            Eq(2, env.Menu.targetDepth, "row " + i + ": native depth untouched");
            True(!PatchUI_PanelFocus.Engaged, "row " + i + ": never engages either");
        }
    }

    private static void OpenEdgeMenuPlusMap()
    {
        // Menu + map stacked (menu depth 1 + map push, MapTimelineMenu.cs:363-364).
        Env env = new Env(Game.State.Menu);
        env.Menu.ShowMainPanelStarted = true;
        env.Menu.targetDepth = 2;
        env.Menu.SetRaw(true);
        env.Menu.ActiveMap = new MapTimelineMenu { CurrentState = MapTimelineMenu.State.ShowingMapOnly };
        env.OpenPanel();
        Eq(1, env.Menu.HideOneCalls, "menu+map: exactly one HideOne on the open edge");
        Eq(0, env.Menu.HideCalls, "menu+map: no absolute Hide");
        Eq(1, env.Menu.targetDepth, "2->1: the map retreats by depth, the menu stays behind");
        Eq(0, env.Game.NativeTryShowMenuCalls, "menu already shown: no TryShowMenu");
        Eq(false, env.Menu.interactable, "the menu left behind is gated");
        Eq(0, env.Menu.OnButtonCloseCalls, "OnButtonClose never called");
        // The map coroutine finishes its retreat; ESC then closes the panel (user ownership).
        env.Menu.ActiveMap.CurrentState = MapTimelineMenu.State.Closed;
        StubRewired esc = new StubRewired();
        esc.Press(5);
        Env.PanelUpdate(env, escDown: true, f5Down: false);
        Eq(1, env.Menu.InteractableTrueWrites, "user menu: exactly one restore write on close");
        Eq(0, env.Menu.HideCalls, "user menu: never Hidden");
        NativeMenuUpdate.Update(env, esc); // same-frame Menu.Update: restored + ESC
        Eq(2, env.Menu.HideOneCalls, "the same ESC lays the menu down exactly once (1->0)");
        Eq(0, env.Menu.targetDepth, "targetDepth never goes negative");
    }

    // 复审 P2-1：地图 Closing 动画期不得再 HideOne（多退一层最坏 1->0 连主菜单收走）。
    // State.Closing=0 是枚举默认值——未初始化的 ActiveMap 也靠同一守卫兜住。
    private static void OpenEdgeClosingMapLeftAlone()
    {
        Env env = new Env(Game.State.Menu);
        env.Menu.ShowMainPanelStarted = true;
        env.Menu.targetDepth = 2;
        env.Menu.SetRaw(true);
        env.Menu.ActiveMap = new MapTimelineMenu { CurrentState = MapTimelineMenu.State.Closing };
        env.OpenPanel();
        Eq(0, env.Menu.HideOneCalls, "closing map: never HideOne'd");
        Eq(2, env.Menu.targetDepth, "depth untouched");
        Eq(0, env.Menu.HideCalls, "no absolute Hide");
        Eq(0, env.Game.NativeTryShowMenuCalls, "IsMenuShown true: no TryShowMenu");
        Eq(false, env.Menu.interactable, "the menu behind is still gated");
    }

    private static void OpenEdgePureMapInsurance()
    {
        // No M-key pure-map form exists in the game (two entrances only), but if one ever
        // appears the open edge must walk the same step-2 branch: HideOne 1->0 is exactly
        // the native close-map semantic and the fallback then takes over (brief 3.1 fact).
        Env env = new Env(Game.State.Playing);
        env.Menu.ActiveMap = new MapTimelineMenu { CurrentState = MapTimelineMenu.State.ShowingWithTimeline };
        env.Menu.targetDepth = 1; // the map show routine pushed depth to 1, no main menu
        env.OpenPanel();
        Eq(1, env.Menu.HideOneCalls, "pure map: single HideOne = native close-map semantic");
        Eq(0, env.Menu.targetDepth, "1->0");
        Eq(0, env.Menu.HideCalls, "no absolute Hide");
        Eq(0, env.Game.NativeTryShowMenuCalls, "IsMenuShown true via the map: no TryShowMenu");
        True(!PatchUI_PanelFocus.Engaged, "map still shown: engage waits");
        env.Menu.ActiveMap.CurrentState = MapTimelineMenu.State.Closed;
        Env.PanelUpdate(env, escDown: false, f5Down: false);
        True(PatchUI_PanelFocus.Engaged, "the fallback takes over once the map coroutine exits");
        Eq(0f, UnityEngine.Time.timeScale, "fallback freeze after the map closed");
        env.ClosePanelByF5();
        Eq(1f, UnityEngine.Time.timeScale, "fallback restore on close");
    }

    private static void GateCountersBothOrders()
    {
        // Order B: ModPanel.Update runs first; the ShowMenu run signal lands later the same
        // frame (Game.Update) — a one-frame :724-true window exists, the next panel update
        // re-gates. Explicitly: this test pins the Game.Update/Menu coroutine vs
        // ModPanel.Update script order.
        Env panelFirst = new Env(Game.State.Playing);
        panelFirst.Game.DeferredMenuOpen = true;
        panelFirst.OpenPanel();
        Eq(1, panelFirst.Game.NativeTryShowMenuCalls, "panel-first: request armed");
        Env.PanelUpdate(panelFirst, escDown: false, f5Down: false);
        Eq(0, panelFirst.Menu.InteractableFalseWrites, "panel-first: no gate write before the menu exists");
        panelFirst.Game.PumpRunSignal(); // Game.Update processes the signal: :724 writes true
        Eq(true, panelFirst.Menu.interactable, "panel-first: one-frame window, :724 wrote true");
        Env.PanelUpdate(panelFirst, escDown: false, f5Down: false);
        Eq(false, panelFirst.Menu.interactable, "panel-first: the next panel update re-gates");
        Eq(1, panelFirst.Menu.InteractableFalseWrites, "panel-first: exactly one effective false write");

        // Order A: the run signal lands before ModPanel.Update in the same frame — the gate
        // wins within that frame, no window at all.
        Env gameFirst = new Env(Game.State.Playing);
        gameFirst.Game.DeferredMenuOpen = true;
        gameFirst.OpenPanel();
        gameFirst.Game.PumpRunSignal();
        Eq(true, gameFirst.Menu.interactable, "game-first setup: :724 wrote true");
        Env.PanelUpdate(gameFirst, escDown: false, f5Down: false);
        Eq(false, gameFirst.Menu.interactable, "game-first: the gate wins within the same frame");
        Eq(1, gameFirst.Menu.InteractableFalseWrites, "game-first: one effective false write");
    }

    private static void CloseOursHidesWithoutRestore()
    {
        Env env = new Env(Game.State.Playing);
        env.OpenPanel(); // synchronous model: menu shown + gated on the open Tick
        Eq(1, env.Game.NativeTryShowMenuCalls, "setup: our TryShowMenu");
        Eq(false, env.Menu.interactable, "setup: gated");
        // ESC while gated: Menu.Update ignores it entirely (interactable false, :2733).
        StubRewired esc = new StubRewired();
        esc.Press(5);
        NativeMenuUpdate.Update(env, esc);
        Eq(0, env.Menu.HideOneCalls, "gated menu ignores ESC: ours path writes 0 once, no -1 race");
        esc.EndFrame();
        env.ClosePanelByF5();
        Eq(1, env.Menu.HideCalls, "our menu: exactly one Hide on close");
        Eq(0, env.Menu.HideOneCalls, "ours path never HideOnes");
        Eq(1, env.Menu.InteractableTrueWrites, "no restore write beyond the :724 baseline (P1-1)");
        Eq(0, env.Menu.targetDepth, "absolute 0");
        True(!PatchUI_PanelFocus.Engaged, "menu mode never engaged");
        Env.FinishMenuTail(env);
        Eq(1f, UnityEngine.Time.timeScale, "the native exit path restored the clock");
        Eq(1, Lines("close: our menu hidden").Count, "close line logged once");
    }

    private static void CloseUserMenuBothOrders()
    {
        // Order A (panel update first): the user opened the menu by hand, F5 gated it, the
        // closing ESC restores the menu and the same press then layers it down once (P0-2).
        Env a = new Env(Game.State.Playing);
        StubRewired esc = new StubRewired();
        esc.Press(5);
        NativeGameLoop.Update(a, esc);
        esc.EndFrame();
        True(a.Game.state == Game.State.Menu, "A setup: the user's ESC opened the menu");
        Eq(true, a.Menu.interactable, "A setup: :724 true");
        a.OpenPanel();
        Eq(0, a.Menu.HideCalls + a.Menu.HideOneCalls, "A: the user's menu is never Hidden on the open edge");
        Eq(1, a.Game.NativeTryShowMenuCalls, "A: no extra TryShowMenu while the menu is shown");
        Eq(false, a.Menu.interactable, "A: gated behind the panel");
        esc.Press(5);
        Env.PanelUpdate(a, escDown: true, f5Down: false);
        Eq(true, a.Menu.interactable, "A: user path restores interactivity, never Hides");
        Eq(0, a.Menu.HideCalls, "A: zero Hide");
        NativeMenuUpdate.Update(a, esc);
        Eq(1, a.Menu.HideOneCalls, "A: the same press layers the menu down exactly once (1->0)");
        Eq(0, a.Menu.targetDepth, "A: never -1");
        esc.EndFrame();

        // Order B (menu update first): the still-gated menu eats the closing press; the
        // panel closes and restores; the user's NEXT ESC closes the menu once.
        Env b = new Env(Game.State.Playing);
        esc.Press(5);
        NativeGameLoop.Update(b, esc);
        esc.EndFrame();
        b.OpenPanel();
        esc.Press(5);
        NativeMenuUpdate.Update(b, esc); // gated -> ignored
        Eq(0, b.Menu.HideOneCalls, "B: the gated menu ignores the closing ESC");
        Env.PanelUpdate(b, escDown: true, f5Down: false);
        Eq(0, b.Menu.HideOneCalls, "B: the closing frame itself never HideOnes");
        Eq(true, b.Menu.interactable, "B: restored on close");
        Eq(0, b.Menu.HideCalls, "B: never Hidden");
        Eq(1, b.Menu.targetDepth, "B: the menu waits for the user");
        esc.EndFrame();
        esc.Press(5);
        NativeMenuUpdate.Update(b, esc);
        Eq(1, b.Menu.HideOneCalls, "B: the user's next ESC layers it down exactly once");
        Eq(0, b.Menu.targetDepth, "B: still never -1");
    }

    private static void FallbackEngageAfterRejection()
    {
        Env env = new Env(Game.State.Playing);
        env.InstallGate();
        UnityEngine.Time.timeScale = 1.25f;
        EngageFallback(env);
        Eq(1, env.Game.NativeTryShowMenuCalls, "the attempt was made and natively rejected");
        True(PatchUI_PanelFocus.Engaged, "window elapsed: the fallback takes over");
        Eq(0f, UnityEngine.Time.timeScale, "fallback freeze at the stashed value");
        Eq(1, Lines("[PanelFocus] engaged").Count, "exactly one engage line per engage");
        True(Lines("[PanelFocus] engaged")[0].Contains("IControllable_ReceiveInput"),
            "the engage line carries the resolved hook name");
        Eq(1, Lines("input gate installed on Player.IControllable_ReceiveInput").Count,
            "install line logged exactly once (O-3)");
        env.ClosePanelByF5();
        Eq(1.25f, UnityEngine.Time.timeScale, "closing restores the exact stashed timeScale");

        // The window itself: no freeze while the menu attempt is still pending.
        Env waiting = new Env(Game.State.Playing);
        waiting.Menu.CanOpen = false;
        waiting.OpenPanel();
        True(!PatchUI_PanelFocus.Engaged, "sixth condition: the 0.5s window lets the menu path go first");
        Eq(1f, UnityEngine.Time.timeScale, "no freeze during the window");
        Env.Advance(0.3f);
        Env.PanelUpdate(waiting, escDown: false, f5Down: false);
        True(!PatchUI_PanelFocus.Engaged, "still inside the window at 0.3s");
    }

    private static void AttemptWindowClaim()
    {
        Env env = new Env(Game.State.Playing);
        env.Game.DeferredMenuOpen = true;
        env.OpenPanel();
        Eq(1, env.Game.NativeTryShowMenuCalls, "attempt armed as a pending run signal");
        True(!PatchUI_PanelFocus.Engaged, "no fallback while the menu signal is pending");
        Env.Advance(0.2f);
        env.Game.PumpRunSignal(); // the menu opens within the claim window
        True(env.Game.state == Game.State.Menu, "the native menu opened");
        Env.PanelUpdate(env, escDown: false, f5Down: false);
        Eq(false, env.Menu.interactable, "gated the moment it shows");
        // Ownership is claimed within the window: close Hides, never restores.
        env.ClosePanelByF5();
        Eq(1, env.Menu.HideCalls, "claimed within 0.5s: ours -> Hide on close");
        Eq(1, env.Menu.InteractableTrueWrites, "no restore write beyond :724 (P1-1)");
    }

    private static void StaleAttemptNeverClaims()
    {
        Env env = new Env(Game.State.Playing);
        env.Menu.CanOpen = false;
        env.OpenPanel();   // attempt recorded, natively rejected
        Env.Advance(1.0f); // the attempt goes stale
        env.Menu.CanOpen = true;
        env.Game.TryShowMenu(0); // an external path opens the menu (invite notification)
        True(Menu.IsMenuShown, "setup: the menu is externally shown");
        Env.PanelUpdate(env, escDown: false, f5Down: false);
        Eq(false, env.Menu.interactable, "still gated while the panel is up");
        env.ClosePanelByF5();
        Eq(0, env.Menu.HideCalls, "stale attempt: the external menu is NOT ours -> never Hidden");
        Eq(2, env.Menu.InteractableTrueWrites, "restored (:724 + the restore)");
    }

    private static void ZeroTimeScaleNeverEngages()
    {
        Env env = new Env(Game.State.Playing);
        env.Menu.CanOpen = false; // somebody else already paused the world and menu can't open
        UnityEngine.Time.timeScale = 0f;
        env.OpenPanel();
        Env.Advance(0.6f);
        Env.PanelUpdate(env, escDown: false, f5Down: false);
        True(!PatchUI_PanelFocus.Engaged, "ts==0 must never engage, even past the window");
        Eq(0f, UnityEngine.Time.timeScale, "no write while ts==0");
        env.ClosePanelByF5();
        Eq(0f, UnityEngine.Time.timeScale, "closing writes nothing when never engaged");
    }

    private static void NetworkGateWithoutFreeze()
    {
        Env host = new Env(Game.State.Playing);
        NetworkBigBoss.IsClientPresent = true;
        host.OpenPanel();
        Eq(1, host.Game.NativeTryShowMenuCalls, "host with a client still opens the menu");
        Eq(1f, UnityEngine.Time.timeScale, "no freeze: native skips ts with a client (Menu.cs:710)");
        True(!PatchUI_PanelFocus.Engaged, "host with a client mirrors native: no pause");
        Eq(false, host.Menu.interactable, "联机带客机：闸照常");
        host.ClosePanelByF5();
        Eq(1, host.Menu.HideCalls, "our menu is still collected on close");
        Eq(1f, UnityEngine.Time.timeScale, "no ts write anywhere in the client-present flow");

        Env client = new Env(Game.State.NetworkClientPlaying);
        NetworkBigBoss.HasWorldAuth = false;
        NetworkBigBoss.IsClientPresent = false;
        client.OpenPanel();
        Eq(1, client.Game.NativeTryShowMenuCalls, "clients open the menu too (state in the G set)");
        Eq(1f, UnityEngine.Time.timeScale, "clients never freeze");
        True(!PatchUI_PanelFocus.Engaged, "client machines never pause");
        Eq(false, client.Menu.interactable, "gated on the client as well");
    }

    private static void FallbackEscBothOrders()
    {
        // Fallback mode ESC semantics (P1-3①): panel closes AND the menu opens natively.
        // Panel-update-first: the closing Tick restores ts, the same press opens the menu.
        Env panelFirst = new Env(Game.State.Playing);
        EngageFallback(panelFirst);
        True(PatchUI_PanelFocus.Engaged, "panel-first setup: fallback engaged");
        panelFirst.Menu.CanOpen = true; // whatever held the menu closed released meanwhile
        StubRewired esc = new StubRewired();
        esc.Press(5);
        Env.PanelUpdate(panelFirst, escDown: true, f5Down: false); // panel closes + disengage
        NativeGameLoop.Update(panelFirst, esc);                    // same press -> native open
        True(!ModPanel.IsShown, "panel-first: panel closed");
        Eq(2, panelFirst.Game.NativeTryShowMenuCalls, "panel-first: rejected once, opened by the ESC");
        True(panelFirst.Game.state == Game.State.Menu, "panel-first: the native menu opened");
        Eq(0f, UnityEngine.Time.timeScale, "panel-first: re-frozen by the menu");
        Eq(1f, panelFirst.Menu.Pts, "panel-first: the menu captured the restored 1, not the fallback 0");
        Env.PanelUpdate(panelFirst, escDown: false, f5Down: false);
        Eq(0, panelFirst.Menu.HideCalls, "panel-first: the ESC-opened menu is the user's, no Hide");

        // Game-update-first: the press opens the menu while the panel is still shown
        // (no TryShowMenu hook exists in v3.2), then the panel closes on top of it.
        Env gameFirst = new Env(Game.State.Playing);
        EngageFallback(gameFirst);
        True(PatchUI_PanelFocus.Engaged, "game-first setup: fallback engaged");
        gameFirst.Menu.CanOpen = true;
        esc.EndFrame();
        esc.Press(5);
        NativeGameLoop.Update(gameFirst, esc); // TryShowMenu runs unpatched even while shown
        True(gameFirst.Game.state == Game.State.Menu, "game-first: the menu opened behind the panel");
        Env.PanelUpdate(gameFirst, escDown: true, f5Down: false); // then the panel closes
        True(!ModPanel.IsShown, "game-first: panel closed");
        Eq(0f, UnityEngine.Time.timeScale, "game-first: clear-only disengage, the menu keeps the clock");
        Eq(true, gameFirst.Menu.interactable, "game-first: never gated before the close -> interactive");
        Eq(0, gameFirst.Menu.HideCalls, "game-first: not ours and never gated -> fully the user's");
    }

    private static void DisengageWriteBranches()
    {
        // a) Engaged with ts still 0, state leaves the playable set: clear only, no write.
        Env left = new Env(Game.State.Playing);
        EngageFallback(left);
        True(PatchUI_PanelFocus.Engaged, "a: setup engaged");
        left.Game.state = Game.State.Loss;
        left.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "a: flag cleared");
        Eq(0f, UnityEngine.Time.timeScale, "a: no write when the state left playable");

        // b) game==null (context torn down): clear only, no write.
        Env gone = new Env(Game.State.Playing);
        EngageFallback(gone);
        Managers.Inst.game = null;
        gone.ClosePanelByF5();
        True(!PatchUI_PanelFocus.Engaged, "b: flag cleared");
        Eq(0f, UnityEngine.Time.timeScale, "b: no write on game==null");

        // c) Engaged but native already moved ts: clear only, native wins.
        Env native = new Env(Game.State.Playing);
        EngageFallback(native);
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
        EngageFallback(env);
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
        // Menu path first: a later Env ctor re-points the global fixtures.
        Env menu = new Env(Game.State.Playing);
        ModConfig.Enabled.Value = false; // P1-2: the panel is infrastructure, not gameplay
        menu.OpenPanel();
        Eq(1, menu.Game.NativeTryShowMenuCalls, "menu open is not gated by ModConfig.Enabled");
        Eq(false, menu.Menu.interactable, "the gate applies with the master toggle off");
        menu.ClosePanelByF5();
        Eq(1, menu.Menu.HideCalls, "our menu still collected on close");

        Env fallback = new Env(Game.State.Playing);
        fallback.InstallGate();
        ModConfig.Enabled.Value = false; // the ctor reset it; disable again
        EngageFallback(fallback);
        True(PatchUI_PanelFocus.Engaged, "engage is not gated by ModConfig.Enabled");
        Eq(0f, UnityEngine.Time.timeScale, "frozen despite the master toggle off");
        StubRewired pad = new StubRewired();
        NativeGameLoop.Update(fallback, pad);
        Eq(0, fallback.P1.InputTicks, "the input gate still blocks with the master toggle off");
        fallback.ClosePanelByF5();
        Eq(1f, UnityEngine.Time.timeScale, "restore still happens with the master toggle off");
        NativeGameLoop.Update(fallback, pad);
        Eq(1, fallback.P1.InputTicks, "input flows again after the close");
    }

    private static void InputGateCoversBothPlayers()
    {
        Env env = new Env(Game.State.Playing);
        env.Game._secondaryControllable = env.P2; // Game.ResetInput local pair
        env.Menu.CanOpen = false; // keep the state Playing so the pump actually dispatches
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
        env.OpenPanel(); // menu path: unaffected by the missing gate
        Eq(1, env.Game.NativeTryShowMenuCalls, "the menu path is unaffected by a missing gate");
        True(env.Game.state == Game.State.Menu, "the menu really opened");

        // A later install with the real type still wires and works.
        env.InstallGate();
        Eq(1, PanelFocusHarness.PatchedPrefixes.Count, "a later correct install still wires");
        env.Game.state = Game.State.Playing; // synthetic: menu gone, panel still up
        env.Menu.ShowMainPanelStarted = false;
        env.Menu.targetDepth = 0;
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

    private static void CursorForcedGlobally()
    {
        Env env = new Env(Game.State.Playing);
        CursorSystem.Inst.forceVisibleCursor = true; // a native login/error screen set it
        env.OpenPanel();
        True(CursorSystem.Inst.forceVisibleCursor, "forced on open");
        env.ClosePanelByF5();
        True(CursorSystem.Inst.forceVisibleCursor, "the pre-open native true is restored");

        Env fresh = new Env(Game.State.Playing);
        fresh.OpenPanel();
        True(CursorSystem.Inst.forceVisibleCursor, "forced from a false baseline too");
        fresh.ClosePanelByF5();
        True(!CursorSystem.Inst.forceVisibleCursor, "false baseline restored to false");

        Env front = new Env(Game.State.SailingAway);
        front.OpenPanel();
        True(CursorSystem.Inst.forceVisibleCursor, "cursor force is global: outside G (frontend) too");
        front.ClosePanelByF5();
        True(!CursorSystem.Inst.forceVisibleCursor, "frontend close restores as well");
    }

    // -------------------------------------------------------------- helpers

    /// <summary>Fallback fixture: the menu can't open, the 0.5s window passes, the
    /// fallback engages (engagement itself asserted by the caller).</summary>
    private static void EngageFallback(Env env)
    {
        env.Menu.CanOpen = false;
        env.OpenPanel();
        Env.Advance(0.5f);
        Env.PanelUpdate(env, escDown: false, f5Down: false);
    }

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
