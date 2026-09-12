using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Passive population overlay; Draw consumes cached text only.</summary>
internal static class PopulationHud
{
    private const float Width = 330f, Row = 20f;
    private static readonly Color Ivory = new(0.95f, 0.92f, 0.84f);
    private static readonly Color Gold = new(0.93f, 0.78f, 0.47f);
    private static readonly string[] RoleNames = { "工匠", "弓箭手", "农民", "长枪兵", "忍者", "狂战士", "无业村民", "乞丐" };
    private static readonly string[] StyleNames = { "中世纪", "死地", "幕府", "希腊", "北境" };
    private static readonly string[] RoleText = new string[8], StyleText = new string[6];
    private static string _knightsText = "";
    private static GUIStyle _style;
    private static long _version = -1;
    private static bool _valid, _clientUnavailable, _snapshotLogged, _faultLogged;
    private static float _retryAfter, _tickRetryAfter;

    private static bool Enabled => ModConfig.Enabled != null && ModConfig.Enabled.Value
        && ModConfig.ShowPopulationHud != null && ModConfig.ShowPopulationHud.Value;

    internal static void Tick()
    {
        try
        {
            bool enabled = Enabled;
            if (enabled && Time.unscaledTime < _tickRetryAfter) { _valid = false; return; }
            bool ready = PopulationCounts.Refresh(enabled ? Managers.Inst : null, enabled, Time.unscaledTime);
            _clientUnavailable = enabled && PopulationCounts.ClientUnavailable;
            _valid = enabled && (ready || _clientUnavailable);
            if (_clientUnavailable) return;
            if (!_valid || _version == PopulationCounts.Version) return;
            _version = PopulationCounts.Version;
            for (int i = 0; i < RoleText.Length; i++) RoleText[i] = RoleNames[i] + "  " + PopulationCounts.Role(i);
            for (int i = 0; i < StyleNames.Length; i++) StyleText[i] = StyleNames[i] + "  " + PopulationCounts.Style(i);
            StyleText[5] = PopulationCounts.UnknownKnights > 0 ? "待识别  " + PopulationCounts.UnknownKnights : "";
            _knightsText = "骑士  " + PopulationCounts.Knights;
            if (!_snapshotLogged)
            {
                _snapshotLogged = true;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[PopulationHUD] ready=true "
                    + string.Join("; ", RoleText) + "; " + _knightsText + "; " + string.Join("; ", StyleText));
            }
        }
        catch (Exception ex) { _valid = false; _tickRetryAfter = Time.unscaledTime + 1f; LogOnce(ex); }
    }

    internal static void Draw()
    {
        if (!_valid || Event.current == null || Event.current.type != EventType.Repaint
            || Time.unscaledTime < _retryAfter) return;
        GUISkin savedSkin = GUI.skin;
        Color savedColor = GUI.color, savedContent = GUI.contentColor, savedBackground = GUI.backgroundColor;
        Matrix4x4 savedMatrix = GUI.matrix;
        bool savedEnabled = GUI.enabled, savedChanged = GUI.changed;
        int savedDepth = GUI.depth;
        try
        {
            EnsureStyle();
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUI.depth = -20;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.45f, 2f);
            scale = Mathf.Min(scale, Mathf.Max(1f, Screen.width - 24f) / (Width + 16f));
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            const float x = 16f, y = 76f;
            Label(x, 54f, "本岛人数", Gold);
            if (_clientUnavailable)
            {
                Label(x, y, "联机客机人数暂不可用", Ivory, Width);
                return;
            }
            for (int i = 0; i < RoleText.Length; i++)
                Label(x + (i % 2) * 165f, y + (i / 2) * Row, RoleText[i], Ivory);
            Label(x, y + 4 * Row + 6f, _knightsText, Gold);
            for (int i = 0; i < StyleText.Length; i++)
                if (!string.IsNullOrEmpty(StyleText[i]))
                    Label(x + (i % 2) * 165f, y + 5 * Row + 6f + (i / 2) * Row, StyleText[i], Ivory);
        }
        catch (Exception ex) { _style = null; _retryAfter = Time.unscaledTime + 1f; LogOnce(ex); }
        finally
        {
            GUI.skin = savedSkin; GUI.color = savedColor; GUI.contentColor = savedContent;
            GUI.backgroundColor = savedBackground; GUI.matrix = savedMatrix;
            GUI.enabled = savedEnabled; GUI.changed = savedChanged; GUI.depth = savedDepth;
        }
    }

    private static void EnsureStyle()
    {
        if (_style != null) return;
        // Inherit the game's native fallback font chain, including CJK, without external font discovery.
        _style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
            wordWrap = false, richText = false, clipping = TextClipping.Clip
        };
        _style.normal.textColor = Color.white;
    }

    private static void Label(float x, float y, string text, Color color, float width = 161f)
    {
        GUI.contentColor = new Color(0f, 0f, 0f, 0.55f);
        GUI.Label(new Rect(x + 1, y + 1, width, Row), text, _style);
        GUI.contentColor = color;
        GUI.Label(new Rect(x, y, width, Row), text, _style);
    }

    private static void LogOnce(Exception ex)
    {
        if (_faultLogged) return;
        _faultLogged = true;
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[PopulationHUD] unavailable: " + ex.GetType().Name); }
        catch { }
    }
}
