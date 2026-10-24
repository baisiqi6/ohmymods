// 免费回程的明确 input action：只在自有宫廷门 Transform 旁消费原生 payKey/payKeyDown，
// 并在真实 payKeyDown 时请求与十币进入同一条 native 短链。不是 Payable、不走 Payment、
// 不合成按键、不 Select/PerformPay/OnPay、不改其它 hook（HoldPurchase 保持原样）。
using System;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 回程门（gate）：
/// - InstallOnce：`PALACE_RETURN_PRIVATE=1` 且 <see cref="PalaceLand12Runtime.Enabled"/> 时才挂
///   <c>Player.UpdatePayState(bool,bool,bool)</c> prefix（Priority.First，不改入参）并创建 GUI 宿主；
///   未激活零 hook、零副作用。
/// - 门判定：own Transform/Scene + 当前 Kingdom 本地玩家 ptr/localAuthority + land12 actualPlaying +
///   payStateNone/floatingCoins0 + 非 Menu/saving/online + 门前 |dx|≤1.7 且 |dy|≤1.5。
/// - nearGate && (payKey||payKeyDown) → 消费（skip 原方法），避免同位置 native 篝火/投币；
///   仅真实 payKeyDown 触发一次 <see cref="PalaceIslandTravel.TryReturn"/>（免费，不经 Payment）。
///   unknown/TripMissing：中文 label「回程暂不可用」并消费真实 down，不误落地币；
///   alreadyTransaction（非 None/浮币非 0）原样放行，绝不打断。
/// </summary>
internal static class PalaceReturnGate
{
    internal const float MaxDx = 1.7f;
    internal const float MaxDy = 1.5f;
    private const string ReturnEnv = "PALACE_RETURN_PRIVATE";
    private const float LogIntervalSeconds = 10f;
    private const float FeedbackSeconds = 4f;

    private static bool _installed;
    private static IntPtr _gestureGame, _gesturePlayer, _gestureKingdom;
    private static IntPtr _arrivalGame, _arrivalPlayer, _arrivalKingdom;
    private static float _nextLog;
    private static string _lastReason = "";
    private static string _feedback = "";
    private static float _feedbackUntil;

    internal static void InstallOnce(Harmony harmony)
    {
        if (_installed) return;
        if (!PalaceRuntimeProfile.Capability(ReturnEnv)) return;
        if (!PalaceLand12Runtime.Enabled)
        {
            Log("回程门未启用：land12 profile 未确认（不挂 hook）");
            return;
        }
        if (harmony == null) throw new InvalidOperationException("PalaceReturnGate requires Harmony");
        MethodInfo target = AccessTools.Method(typeof(Player), nameof(Player.UpdatePayState),
            new[] { typeof(bool), typeof(bool), typeof(bool) });
        if (target == null || target.ReturnType != typeof(void))
            throw new InvalidOperationException("PalaceReturnGate hook signature unavailable");
        _installed = true;
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(PalaceReturnGate), nameof(Before)));
        EnsureHost();
        Log("回程门已挂载：门前消费 payKey/payKeyDown，真实 down 请求一次免费回程（不合成按键）");
    }

    // The gateway arrival owns only the old held payment gesture until a real Playing key release.
    internal static void ArmArrivalRelease(Game game, Kingdom kingdom, Player player)
    {
        if (!_installed || game == null || kingdom == null || player == null) return;
        _arrivalGame = game.Pointer; _arrivalKingdom = kingdom.Pointer; _arrivalPlayer = player.Pointer;
    }

    /// <summary>Priority.First：nearGate 抑制原生同位置投币/篝火；只在真实 down 时发起一次回程。</summary>
    [HarmonyPriority(Priority.First + 1)]
    private static bool Before(Player __instance, bool __0, bool __1, bool __2)
    {
        try
        {
            if (!_installed || __instance == null) return true;
            if (PalaceIslandTravel.TryConsumePaidResume(__instance, __0, __1)) return false;
            if (_arrivalPlayer == __instance.Pointer && _arrivalGame != IntPtr.Zero)
            {
                var arrival = Managers.Inst;
                if (arrival == null || arrival.game == null || arrival.kingdom == null) return !(__0 || __1);
                if (arrival.game.Pointer == _arrivalGame
                    && arrival.kingdom.Pointer == _arrivalKingdom && arrival.game.currentLand == PalaceLand12Runtime.LandIndex)
                {
                    // Loading does not supply a meaningful key-release observation.
                    if (arrival.game.state != Game.State.Playing) return true;
                    if (__instance._payState != Player.PayState.None
                        || __instance._floatingCurrency == null || __instance._floatingCurrency.Count != 0) return true;
                    if (__0 || __1) return false;
                    Log("进入后的投币手势已松开，免费回程输入可用");
                }
                _arrivalGame = _arrivalPlayer = _arrivalKingdom = IntPtr.Zero;
            }
            // This handler owns the already-consumed gesture until release or source-scene exit.
            // campaign.CurrentLand may already be the selected destination while source Game is still land12.
            if (_gesturePlayer == __instance.Pointer && _gestureGame != IntPtr.Zero)
            {
                var m = Managers.Inst;
                if (m != null && m.game != null && m.kingdom != null && m.game.Pointer == _gestureGame
                    && m.kingdom.Pointer == _gestureKingdom && m.game.currentLand == PalaceLand12Runtime.LandIndex)
                {
                    if (__0 || __1) return false;
                    _gestureGame = _gesturePlayer = _gestureKingdom = IntPtr.Zero;
                }
                else _gestureGame = _gesturePlayer = _gestureKingdom = IntPtr.Zero;
            }
            if (!InScope(__instance, out Game game, out Kingdom kingdom, out string reason))
            {
                Throttle(reason);
                return true;
            }
            if (!TryGetDoor(__instance, out Transform door) || !Near(door, __instance)) return true;
            Player.PayState payState = __instance._payState;
            int floating = __instance._floatingCurrency != null ? __instance._floatingCurrency.Count : -1;
            if (payState != Player.PayState.None || floating != 0) return true; // 不打断已在进行的交易
            bool key = __0 || __1;
            if (!key) return true;
            _gestureGame = game.Pointer; _gesturePlayer = __instance.Pointer; _gestureKingdom = kingdom.Pointer;
            if (__1)
            {
                bool accepted = PalaceIslandTravel.TryReturn(game, kingdom, __instance, out string travelReason);
                if (accepted) Log("回程 down：已受理（受理≠到达）：" + travelReason);
                else
                {
                    SetFeedback(IsInFlight(travelReason) ? "回程已在进行" : "回程暂不可用");
                    Log("回程 down：拒绝：" + travelReason);
                }
            }
            return false; // 消费 nearGate 的 payKey/payKeyDown，避免同位置原生篝火/投币
        }
        catch (Exception e)
        {
            Log("回程门 fault：" + e.GetType().Name);
            IntPtr payer = __instance != null ? __instance.Pointer : IntPtr.Zero;
            return _gesturePlayer != payer && _arrivalPlayer != payer;
        }
    }

    /// <summary>GUI 文案（只读，不消费输入）：near + current 玩家才可见；目标岛显示动态值不加 1。</summary>
    internal static bool TryView(out string text)
    {
        text = "";
        try
        {
            if (!_installed) return false;
            if (!TryLocalPlayer(out Player player)) return false;
            if (!InScope(player, out Game game, out Kingdom kingdom, out _)) return false;
            if (!TryGetDoor(player, out Transform door) || !Near(door, player)) return false;
            string line = "宫廷门 / 按 ↓ 免费回原岛";
            if (PalaceIslandTravel.TryPeekReturnTarget(game, kingdom, player, out int sourceLand, out string peekReason))
                line += "｜返回出发岛";
            else if (IsInFlight(peekReason))
                line += "｜回程已在进行";
            else
                line += "｜回程暂不可用";
            string feedback = FeedbackText();
            text = feedback.Length > 0 ? line + "\n" + feedback : line;
            return true;
        }
        catch (Exception)
        {
            text = "";
            return false;
        }
    }

    private static bool InScope(Player player, out Game game, out Kingdom kingdom, out string reason)
    {
        game = null;
        kingdom = null;
        reason = "";
        try
        {
            if (Time.timeScale <= 0f) { reason = "timescale"; return false; }
            if (NetworkBigBoss.IsOnline || !NetworkBigBoss.HasWorldAuth) { reason = "network"; return false; }
            if (!PalaceLand12Runtime.Enabled) { reason = "disabled"; return false; }
            Managers managers = Managers.Inst;
            if (managers == null || managers.game == null || managers.kingdom == null) { reason = "managers"; return false; }
            game = managers.game;
            kingdom = managers.kingdom;
            if (game.state != Game.State.Playing) { reason = "state:" + game.state; return false; }
            if (game.currentLand != PalaceLand12Runtime.LandIndex) { reason = "game-land:" + game.currentLand; return false; }
            // Saving prevents a new journey in TryReturn; it must not release this free door gesture to native spending.
            if (player.Pointer == IntPtr.Zero || !player.hasLocalAuthority) { reason = "authority"; return false; }
            if (player.gameObject == null || !player.gameObject.activeInHierarchy) { reason = "inactive"; return false; }
            if (!IsKingdomPlayer(kingdom, player)) { reason = "kingdom-player"; return false; }
            GlobalSaveData global = GlobalSaveData.loaded;
            CampaignSaveData campaign = global != null ? global.GetCurrentCampaign() : null;
            CampaignSaveData current = CampaignSaveData.current;
            if (global == null || global.InChallenge || campaign == null || current == null
                || campaign.Pointer != current.Pointer)
            { reason = "campaign"; return false; }
            if (campaign.CurrentLand != PalaceLand12Runtime.LandIndex)
            { reason = "campaign-land:" + campaign.CurrentLand; return false; }
            if (BiomeHolder.Inst == null || BiomeHolder.Inst.BiomeIndex != BiomeHolder.GreeceBiomeIndex)
            { reason = "biome"; return false; }
            return true;
        }
        catch (Exception e)
        {
            reason = "scope-fault:" + e.GetType().Name;
            return false;
        }
    }

    private static bool TryGetDoor(Player player, out Transform door)
    {
        door = null;
        try
        {
            door = PalaceCourtVisuals.ReturnDoor;
            if (door == null) return false;
            GameObject go = door.gameObject;
            if (go == null || !go.activeInHierarchy) return false;
            if (player.gameObject == null) return false;
            if (go.scene.handle != player.gameObject.scene.handle) return false;
            return true;
        }
        catch (Exception)
        {
            door = null;
            return false;
        }
    }

    private static bool Near(Transform door, Player player)
    {
        try
        {
            Vector3 a = door.position;
            Vector3 b = player.transform.position;
            return Mathf.Abs(a.x - b.x) <= MaxDx && Mathf.Abs(a.y - b.y) <= MaxDy;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryLocalPlayer(out Player player)
    {
        player = null;
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            if (kingdom == null) return false;
            Player p1 = kingdom.playerOne;
            Player p2 = kingdom.playerTwo;
            if (p1 != null && p1.hasLocalAuthority && p1.gameObject != null) { player = p1; return true; }
            if (p2 != null && p2.hasLocalAuthority && p2.gameObject != null) { player = p2; return true; }
            return false;
        }
        catch (Exception)
        {
            player = null;
            return false;
        }
    }

    private static bool IsKingdomPlayer(Kingdom kingdom, Player payer)
    {
        Player p1 = kingdom.playerOne;
        Player p2 = kingdom.playerTwo;
        return (p1 != null && p1.Pointer == payer.Pointer) || (p2 != null && p2.Pointer == payer.Pointer);
    }

    private static void EnsureHost()
    {
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp(typeof(PalaceReturnGateScene)))
                ClassInjector.RegisterTypeInIl2Cpp(typeof(PalaceReturnGateScene));
            var go = new GameObject("KingdomEnhancedMod_PalaceReturnGate");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<PalaceReturnGateScene>();
        }
        catch (Exception e)
        {
            Log("GUI 宿主创建失败（门仍可用，仅缺提示）：" + e.GetType().Name);
        }
    }

    private static void SetFeedback(string text)
    {
        _feedback = text ?? "";
        _feedbackUntil = Time.realtimeSinceStartup + FeedbackSeconds;
    }

    private static string FeedbackText()
    {
        if (_feedback.Length == 0) return "";
        if (Time.realtimeSinceStartup > _feedbackUntil) { _feedback = ""; return ""; }
        return _feedback;
    }

    private static bool IsInFlight(string reason)
        => reason != null && reason.Contains("在途");

    private static void Throttle(string reason)
    {
        if (reason == _lastReason && Time.realtimeSinceStartup < _nextLog) return;
        _lastReason = reason;
        _nextLog = Time.realtimeSinceStartup + LogIntervalSeconds;
        Log("回程门暂不生效：" + reason);
    }

    private static void Log(string message) => PalaceVisibleEntry.Log("[PalaceReturn] " + message);
}

/// <summary>门提示（中文、仅 near/current 玩家可见）：不做输入消费，只画字。</summary>
internal sealed class PalaceReturnGateScene : MonoBehaviour
{
    private static GUIStyle _style;
    public PalaceReturnGateScene(IntPtr pointer) : base(pointer) { }

    private void OnGUI()
    {
        Color prior = GUI.color;
        try
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (!PalaceReturnGate.TryView(out string text)) return;
            const float width = 560f;
            const float height = 64f;
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - height - 72f;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y, width, height), text, Style());
        }
        catch (Exception)
        {
            // 绘制失败绝不影响游戏输入。
        }
        finally { GUI.color = prior; }
    }

    private static GUIStyle Style()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                wordWrap = false
            };
            _style.normal.textColor = new Color(1f, 0.94f, 0.72f, 1f);
        }
        return _style;
    }
}
