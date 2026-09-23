using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// F5 面板自动暂停 + 输入门/原生菜单抑制。
///
/// A 节·暂停状态机（Tick 由 ModPanel.Update 每帧驱动，boot/前端场景也照跑，全程空安全）：
/// - 开门沿（_shown false→true）在状态门内只调 Menu.Inst.Hide()：Playing/NetworkClientPlaying/Menu
///   三态 + ProgramDirector.IsMainSceneActive() + Menu.InstExists 才碰原生覆盖层（前端标题/
///   SailingAway 选岛地图/coop 提示态一律不碰）；Menu 态放行是"先 ESC 后 F5"点击穿透修复的
///   必需路径。绝不同时调 OnButtonClose/HideOne——主菜单+地图叠加时 Hide()（targetDepth 绝对
///   写 0）一次关净，再叠加相对减一会把 targetDepth 打到 -1。
/// - Engage：面板开 + 菜单尾巴已退（IsMenuShown 假）+ 可玩态 + 单机/主机无客机 + ts&gt;0 →
///   记 _pts、写 0。不门 ModConfig.Enabled：面板是基础设施不是玩法逻辑（与 ModPanel.Update
///   内其他 Tick 同口径），关闭总开关后开面板仍要暂停。
/// - Disengage：无条件评估（不门任何开关）。面板关且仍在可玩态且 ts==0 → 按原生公式
///   Approximately(_pts,0)?1:_pts 恢复；state 已离开可玩态 / game==null / 原生已改写 ts（ts≠0）
///   → 只清标志不写（各态入口原生必写时标，外部更新优先）。
///
/// B 节·Player 输入门（手动安装，不走 PatchAll）：显式接口实现 IControllable.ReceiveInput 在
/// 2.4 interop 里的名字是 IControllable_ReceiveInput（Il2CppInterop 把点号改下划线；operator 已
/// 对 interop DLL 字符串堆独立复核）。PatchAll 对解析失败的 attribute 会抛异常拖垮整个插件的
/// 补丁注册，与 fail-closed 目标相反，故 Init 在 PatchAll 之后按候选集解析 MethodInfo 再
/// harmony.Patch 手动安装；全部失败 → ERROR 日志 + 不装 hook（面板其余功能不受影响）。
/// prefix 返回 false 只是跳过方法体：Game.Update 输入泵照常调用（Game.cs:2032-2044），不动泵、
/// 不动 SendInputTo/ReleaseInput，两本地玩家（含客机 tunnel 分支）移动/冲刺/付币全停。
///
/// C 节·原生菜单打开抑制：Game.TryShowMenu prefix（声明式注册，目标方法 2.4 interop 实测存在）。
/// 抑制窗仅由 ModPanel.Update 的 Esc 关闭分支设置，覆盖 Game.Update/ModPanel.Update 两种脚本
/// 执行顺序（Esc=只关面板，再按一次才出原生菜单）；抑制同时跳过 TryShowMenu 内的 ReleaseInput
/// （Game.cs:2342-2352，符合意图）。断线重连/存档失败弹窗/用户切换不走 TryShowMenu，不受影响。
///
/// 已知边界（P2 留档）：ts=0 期间音频 fade 冻结会打原生 LogWarning（日志噪音）；皇冠失落慢动作
/// tween 只认原生 IsMenuShown，可能在面板背后推进（关面板后原生收敛）；Hide() 后 ~0.6s 菜单
/// 尾巴期间 Engage 延迟（与原生 ESC 时序等价）；0.25s 抑制窗理论上可吞一次真开菜单（邀请通知
/// 无重试，概率极低）；_currentControllable 为 TeleporterExit 时不受 Player hook 覆盖；前端标题
/// 菜单不 Hide（面板叠标题菜单点击穿透为已知边界）；Haglet 弹窗不主动关；联机带客机不暂停
/// （镜像原生 Menu.cs:709-712），面板仍可开、输入门照常生效。
/// </summary>
internal static class PatchUI_PanelFocus
{
    /// <summary>Esc 关面板后的原生开菜单抑制窗（秒，unscaledTime 域）。</summary>
    internal const float EscCloseMenuSuppressSeconds = 0.25f;

    private const int FaultBudget = 8;

    internal static bool Engaged { get; private set; }

    private static float _pts;               // Engage 时刻的原生时标（条件含 ts>0，恒>0）
    private static float _suppressMenuUntil; // Time.unscaledTime 域
    private static bool _lastShown;
    private static string _inputGateName;    // B 节解析到的 MethodInfo.Name；未装=null
    private static int _faults;

    /// <summary>
    /// B 节 prefix：面板打开期间返回 false，跳过 Player.IControllable_ReceiveInput 方法体。
    /// 与开关无关（面板基础设施），关闭总开关后输入门同样生效。
    /// </summary>
    internal static bool Prefix() => !ModPanel.IsShown;

    /// <summary>D2：Esc 关闭面板时由 ModPanel.Update 调用；F5/OnGUI 故障关闭不设。</summary>
    internal static void NoteEscClose()
        => _suppressMenuUntil = Time.unscaledTime + EscCloseMenuSuppressSeconds;

    /// <summary>C 节门：原生 Game.TryShowMenu 是否放行。</summary>
    internal static bool ShouldRunNativeTryShowMenu()
        => !ModPanel.IsShown && Time.unscaledTime >= _suppressMenuUntil;

    /// <summary>A 节状态机。空上下文（boot/前端/game==null/Menu 未建）只是空转。</summary>
    internal static void Tick()
    {
        bool shown = ModPanel.IsShown;
        bool openEdge = shown && !_lastShown;
        _lastShown = shown;

        Game game = CurrentGame();

        if (openEdge)
        {
            // 开门沿与暂停判定互不拖累：Hide 一侧读原生失败不得丢掉本帧的 Engage/Disengage。
            try { CloseNativeOverlayOnOpen(game); }
            catch (Exception e) { Note("open edge", e); }
        }

        try
        {
            // Engage：每帧评估，全真才挂（不门 ModConfig.Enabled）。!Engaged 守卫不可省：
            // Engaged 期间原生可能改写时标（如皇冠失落慢动作 tween 只认原生 IsMenuShown），
            // 若 ts>0 即重入会把 _pts 覆写成慢动作值，关面板时恢复错值；一次 Engage 只记一次 pts，
            // 原生写入期间不再碰 ts（不踩原生，Disengage 的 ts==0 恢复门自然让位）。
            // game 判空先于 Menu.IsMenuShown：主场景外 Menu.Inst getter 会 FindObjectOfType 场景扫描，
            // 且 IsMenuShown 第二支无 InstExists 保护（标题界面每帧 NRE→吞进 catch 还耗日志预算）。
            if (!Engaged && shown && game != null && Playable(game) && !Menu.IsMenuShown
                && NetworkBigBoss.HasWorldAuth && !NetworkBigBoss.IsClientPresent
                && Time.timeScale > 0f)
            {
                _pts = Time.timeScale;
                Time.timeScale = 0f;
                Engaged = true;
                Info("engaged: timeScale " + _pts.ToString("0.###") + " -> 0, input gate "
                    + (_inputGateName ?? "unresolved"));
            }

            // Disengage：无条件评估，不门任何开关（总开关关闭时的恢复也走这里）。
            if (Engaged && (!shown || game == null || !Playable(game)))
            {
                if (game != null && Playable(game) && Time.timeScale == 0f)
                    Time.timeScale = Mathf.Approximately(_pts, 0f) ? 1f : _pts;
                // 离开可玩态 / game==null / 原生已动 ts：只清标志，不写时标。
                Engaged = false;
            }
        }
        catch (Exception e)
        {
            Note("tick", e);
        }
    }

    private static void CloseNativeOverlayOnOpen(Game game)
    {
        if (game == null) return;
        Game.State state = game.state;
        if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying
            && state != Game.State.Menu)
            return;
        if (!ProgramDirector.IsMainSceneActive() || !Menu.InstExists || !Menu.IsMenuShown)
            return;
        // 唯一动作：地图协程靠 targetDepth 降幅自退（MapTimelineMenu.cs:424）。
        Menu.Inst.Hide();
    }

    private static Game CurrentGame()
    {
        try
        {
            Managers managers = Managers.Inst;
            return managers != null ? managers.game : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool Playable(Game game)
    {
        Game.State state = game.state;
        return state == Game.State.Playing || state == Game.State.NetworkClientPlaying;
    }

    // ------------------------------------------------- B 节：输入门手动安装

    /// <summary>Init 在 PatchAll 之后调用（harmony 与 PatchAll 同一实例）。</summary>
    internal static void InstallInputGate(Harmony harmony)
        => InstallInputGate(harmony, typeof(Player));

    /// <summary>带类型参数的安装体（测试用替身类型驱动解析失败路径）。</summary>
    internal static void InstallInputGate(Harmony harmony, Type playerType)
    {
        try
        {
            MethodInfo method = ResolveInputGateMethod(playerType);
            if (method == null)
            {
                Error("input gate target not found on " + playerType.Name
                    + " (expected IControllable_ReceiveInput); panel input blocking disabled,"
                    + " all other features unchanged");
                return;
            }
            MethodInfo prefix = typeof(PatchUI_PanelFocus).GetMethod(nameof(Prefix),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prefix == null)
            {
                Error("input gate prefix not found; panel input blocking disabled");
                return;
            }
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            _inputGateName = method.Name;
            // O-3：解析名只在启动日志记这一次。
            Info("input gate installed on Player." + method.Name);
        }
        catch (Exception e)
        {
            Error("input gate install failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    /// <summary>
    /// 候选集解析：精确下划线名（2.4 interop 实测）→ 精确点号名 → 双 token 模糊。
    /// 反射异常与未命中同口径返回 null（fail-closed 交给调用方日志）。
    /// </summary>
    internal static MethodInfo ResolveInputGateMethod(Type playerType)
    {
        try
        {
            MethodInfo best = null;
            int bestRank = int.MaxValue;
            foreach (MethodInfo method in playerType.GetMethods(BindingFlags.Public
                     | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string name = method.Name;
                int rank = name == "IControllable_ReceiveInput" ? 1
                    : name == "IControllable.ReceiveInput" ? 2
                    : name.Contains("IControllable") && name.Contains("ReceiveInput") ? 3
                    : 0;
                if (rank == 0 || rank >= bestRank) continue;
                best = method;
                bestRank = rank;
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    // ----------------------------------------------------------------- 日志

    private static void Info(string message) => TryLog(message, false);
    private static void Error(string message) => TryLog(message, true);

    private static void Note(string what, Exception e)
    {
        if (_faults >= FaultBudget) return;
        _faults++;
        TryLog(what + " fault: " + e.GetType().Name + " " + e.Message, true);
    }

    private static void TryLog(string message, bool error)
    {
        try
        {
            var source = KingdomEnhancedPlugin.Instance?.LogSource;
            if (source == null) return;
            if (error) source.LogError("[PanelFocus] " + message);
            else source.LogInfo("[PanelFocus] " + message);
        }
        catch
        {
            // 日志失败不得改变暂停/输入门决策。
        }
    }
}

/// <summary>
/// C 节：面板打开或 Esc 抑制窗内不放行原生开菜单。声明式注册（Game.TryShowMenu 在
/// 2.4 interop 实测存在，params=2）；断线重连/存档失败弹窗等不走 TryShowMenu 的路径不受影响。
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.TryShowMenu))]
internal static class Game_TryShowMenu_PanelFocus_Patch
{
    private static bool Prefix() => PatchUI_PanelFocus.ShouldRunNativeTryShowMenu();
}
