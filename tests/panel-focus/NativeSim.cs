// NativeSim.cs: faithful reproduction of the vanilla 2.1.0 Game.Update input pump +
// CheckForPause + TryShowMenu, Menu.Update ESC tail and Menu close-tail flows (game-source
// Game.cs:1901-1920 / 2032-2044 / 2316-2358, Menu.cs:664-675 / 697-712 / 724 / 733 / 986 /
// 2729-2736) so the tests can run the production code in front of the real native flow,
// plus the panel-update script (the production ModPanel.Update wiring: shortcut handling
// then PanelFocus.Tick) and the fixture.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;

/// <summary>
/// Test-side harness: invokes the real production decision points (the manually installed
/// input-gate prefix) and applies the "false => original body skipped" contract.
/// No Harmony runtime is involved. v3.2 hooks nothing on Game.TryShowMenu (C section
/// deleted): the native body always runs when the game calls it.
/// </summary>
public static class PanelFocusHarness
{
    public static readonly Dictionary<MethodInfo, HarmonyMethod> PatchedPrefixes =
        new Dictionary<MethodInfo, HarmonyMethod>();

    private static readonly MethodInfo PlayerInputBody =
        typeof(Player).GetMethod("IControllable_ReceiveInput",
            BindingFlags.Public | BindingFlags.Instance);

    /// <summary>The hooked input-pump call Game.Update makes per controllable (Game.cs:2038/2041).</summary>
    internal static void DispatchInput(Player player, StubRewired rewired)
    {
        if (PatchedPrefixes.TryGetValue(PlayerInputBody, out HarmonyMethod prefix))
        {
            bool proceed = (bool)prefix.Method.Invoke(null, null);
            if (!proceed)
            {
                player.GateSkips++;
                return;
            }
        }
        player.IControllable_ReceiveInput(rewired);
    }
}

/// <summary>Vanilla Game.Update: input pump in the two playable states, then CheckForPause
/// (rewired button 5 goes straight to TryShowMenu, never through the Player input gate).</summary>
internal static class NativeGameLoop
{
    public static void Update(Env env, StubRewired p1, StubRewired p2 = null)
    {
        Game.State state = env.Game.state;
        if (state == Game.State.Playing || state == Game.State.NetworkClientPlaying)
        {
            if (env.Game._currentControllable != null)
                PanelFocusHarness.DispatchInput(env.Game._currentControllable, p1);
            if (env.Game._secondaryControllable != null)
                PanelFocusHarness.DispatchInput(env.Game._secondaryControllable, p2 ?? p1);
        }
        if (p1 != null && p1.GetButtonDown(5)) env.Game.TryShowMenu(0);
        if (p2 != null && p2.GetButtonDown(5)) env.Game.TryShowMenu(1);
    }
}

/// <summary>Vanilla Menu.Update back-out tail (Menu.cs:2729-2736): while a map is up the map
/// owns back-out (DoesControlMenuHolding simplified to map-not-closed), a gated menu
/// (interactable false) ignores ESC entirely, an interactive menu HideOne's exactly once.</summary>
internal static class NativeMenuUpdate
{
    public static void Update(Env env, StubRewired rewired)
    {
        if (env.Menu.ActiveMap != null
            && env.Menu.ActiveMap.CurrentState != MapTimelineMenu.State.Closed)
            return; // Menu.cs:2729-2732: map controls menu hiding
        if (env.Menu.interactable && rewired.GetButtonDown(5)) // Menu.cs:2733-2736
            env.Menu.HideOne();
    }
}

/// <summary>Fresh game/menu/players fixture; resets every global the production file reads.</summary>
internal sealed class Env
{
    internal readonly Game Game;
    internal readonly Menu Menu;
    internal readonly Player P1;
    internal readonly Player P2;

    internal Env(Game.State state = Game.State.Playing)
    {
        ResetProductionStatics();
        UnityEngine.Time.time = 0f;
        UnityEngine.Time.deltaTime = 0f;
        UnityEngine.Time.unscaledTime = 0f;
        UnityEngine.Time.timeScale = 1f;
        ModConfig.Enabled.Value = true;
        ModPanel.IsShown = false;
        NetworkBigBoss.HasWorldAuth = true;
        NetworkBigBoss.IsClientPresent = false;
        ProgramDirector.MainSceneActive = true;
        PanelFocusHarness.PatchedPrefixes.Clear();
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        CursorSystem.Inst = new CursorSystem();
        CursorSystem.InstExists = true;

        Menu = new Menu();
        Menu.Inst = Menu;
        Menu.InstExists = true;

        Game = new Game { state = state };
        P1 = new Player();
        P2 = new Player();
        Game._currentControllable = P1;
        Game._secondaryControllable = null; // tests opt in (Game.ResetInput local-authority pair)

        Managers.Inst = new Managers { game = Game };
    }

    /// <summary>Installs the production input gate through the real 1-arg entry point.</summary>
    internal void InstallGate()
        => PatchUI_PanelFocus.InstallInputGate(new HarmonyLib.Harmony());

    /// <summary>ModPanel.Update wiring: shortcut handling first, then PanelFocus.Tick.</summary>
    internal static void PanelUpdate(Env env, bool escDown, bool f5Down)
    {
        if (f5Down) ModPanel.IsShown = !ModPanel.IsShown;
        else if (ModPanel.IsShown && escDown)
            ModPanel.IsShown = false;
        PatchUI_PanelFocus.Tick();
    }

    /// <summary>F5 open: toggle + the Tick that observes the open edge.</summary>
    internal void OpenPanel()
    {
        ModPanel.IsShown = true;
        PatchUI_PanelFocus.Tick();
    }

    /// <summary>F5 close: toggle + the Tick that observes the closed state.</summary>
    internal void ClosePanelByF5()
    {
        ModPanel.IsShown = false;
        PatchUI_PanelFocus.Tick();
    }

    /// <summary>Menu close tail end (Menu.cs:967-989): started clears, the bare
    /// _interactable=false write (:986), native restores ts (:970-972, solo/host only),
    /// the game returns to the playable state.</summary>
    internal static void FinishMenuTail(Env env)
    {
        env.Menu.ShowMainPanelStarted = false;
        env.Menu.ExitBareInteractableFalse();
        if (!NetworkBigBoss.IsClientPresent)
            UnityEngine.Time.timeScale = UnityEngine.Mathf.Approximately(env.Menu.Pts, 0f)
                ? 1f
                : env.Menu.Pts;
        if (env.Game.state == Game.State.Menu)
            env.Game.state = Game.State.Playing;
    }

    /// <summary>Unity frame clock: unscaled always advances, scaled only while ts &gt; 0.</summary>
    internal static void Advance(float dt)
    {
        UnityEngine.Time.deltaTime = UnityEngine.Time.timeScale <= 0f ? 0f : dt;
        UnityEngine.Time.time += UnityEngine.Time.deltaTime;
        UnityEngine.Time.unscaledTime += dt;
    }

    /// <summary>Every static of the production class back to first-process state.</summary>
    private static void ResetProductionStatics()
    {
        foreach (FieldInfo field in typeof(PatchUI_PanelFocus).GetFields(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.IsLiteral) continue;
            field.SetValue(null, field.FieldType.IsValueType
                ? Activator.CreateInstance(field.FieldType)
                : null);
        }
    }
}
