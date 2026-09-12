using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Passive cached calendar overlay; one-time font discovery, no controls or game-state writes.</summary>
internal static class CalendarHud
{
    private const float Width = 552f, Height = 54f;
    private static readonly Color Gold = new Color(0.93f, 0.78f, 0.47f);
    private static readonly Color Ivory = new Color(0.95f, 0.92f, 0.84f);
    private static readonly Color Muted = new Color(0.68f, 0.62f, 0.52f);
    // Subdued seasonal tints for the pixel strip.
    private static readonly Color[] SeasonColors =
    {
        new Color(0.55f, 0.75f, 0.58f), new Color(0.85f, 0.70f, 0.42f),
        new Color(0.82f, 0.56f, 0.38f), new Color(0.58f, 0.72f, 0.85f)
    };
    private static Texture2D _white;
    private static readonly Texture2D[] Icons = new Texture2D[7];
    private static GUIStyle _large, _small, _number;
    private static bool _presentationLogged;
    private static IntPtr _world, _scene, _director;
    private static float _nextRead, _retryAfter;
    private static bool _valid, _faultLogged;
    private static CalendarSnapshot _snapshot;
    private static string _dayText = "", _hourText = "", _seasonText = "", _nextText = "";
    private static string _bankText = "—";

    private static bool Enabled => ModConfig.Enabled != null && ModConfig.Enabled.Value
        && ModConfig.ShowCalendarHud != null && ModConfig.ShowCalendarHud.Value;

    internal static void Tick()
    {
        if (!Enabled) { Clear(); return; }
        try
        {
            if (Time.unscaledTime < _retryAfter) return;
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            Director director = managers != null ? managers.director : null;
            Game game = managers != null ? managers.game : null;
            Transform scene = world != null ? world.gameLayer : null;
            // Game has explicit loading/menu states; a title-screen director can still exist.
            if (world == null || scene == null || director == null || game == null)
            { Clear(); return; }
            Game.State state = game.state;
            bool pausedInKnownWorld = state == Game.State.Menu && _valid
                && _world == world.Pointer && _scene == scene.Pointer && _director == director.Pointer;
            if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying && !pausedInKnownWorld)
            { Clear(); return; }
            if (_world != world.Pointer || _scene != scene.Pointer || _director != director.Pointer)
            {
                Clear();
                _world = world.Pointer; _scene = scene.Pointer; _director = director.Pointer;
            }
            float now = Time.unscaledTime;
            if (now < _nextRead) return;
            _nextRead = now + 0.5f;
            // Same source as the main panel; sampled only by this half-second cache, never during Draw.
            int stashed = BankAssistantCoordinator.GetStashedCoinsForPanel();
            // The coin icon plus the 主城金库 caption already identify the currency; keep the bare number.
            _bankText = stashed < 0 ? "—" : stashed.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            _valid = CalendarReader.TryRead(director, out _snapshot);
            if (!_valid) return;
            _dayText = "第 " + _snapshot.TotalDay + " 天";
            _hourText = _snapshot.Hour + " 点";
            _seasonText = SeasonName(_snapshot.CurrentSeason) + " 第 " + _snapshot.SeasonDay + " 天";
            _nextText = _snapshot.HasNextSeason
                ? SeasonName(_snapshot.NextSeason) + " 第 " + _snapshot.NextSeasonDay + " 天开始"
                : "下一季未定";
        }
        catch (Exception ex) { Clear(); _retryAfter = Time.unscaledTime + 1f; LogOnce(ex); }
    }

    private static void Clear()
    {
        _valid = false;
        _world = _scene = _director = IntPtr.Zero;
        _nextRead = 0f;
        _bankText = "—";
    }

    internal static void Draw()
    {
        if (!Enabled || !_valid || Event.current == null || Event.current.type != EventType.Repaint) return;
        GUISkin savedSkin = GUI.skin;
        Color savedColor = GUI.color, savedContent = GUI.contentColor, savedBackground = GUI.backgroundColor;
        Matrix4x4 savedMatrix = GUI.matrix;
        bool savedEnabled = GUI.enabled, savedChanged = GUI.changed;
        int savedDepth = GUI.depth;
        try
        {
            EnsureResources();
            GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUI.depth = -20;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.45f, 2f);
            scale = Mathf.Min(scale, Mathf.Max(1f, Screen.width - 24f) / Width);
            float x = Mathf.Round((Screen.width / scale - Width) * 0.5f);
            const float y = 12f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            // Floating overlay: no panel frame, no background, no divider — just text, icons, track.
            // Row 1 (primary, ~18px): day | clock+hour | current season | separator | coin+balance.
            Label(x + 14, y + 6, 96, 22, _dayText, _large, Gold);
            Icon(4, x + 112, y + 7, 16, Muted);
            Label(x + 134, y + 6, 50, 22, _hourText, _number, Ivory);
            int season = SeasonIndex(_snapshot.CurrentSeason);
            Color currentColor = season >= 0 ? SeasonColors[season] : Muted;
            if (season >= 0) Icon(season, x + 196, y + 8, 16, currentColor);
            Label(x + 218, y + 6, 158, 22, _seasonText, _number, currentColor);
            Icon(6, x + 400, y + 8, 16, Gold);
            Label(x + 424, y + 6, 114, 22, _bankText, _number, Gold);
            // Row 2 (secondary, ~12px): next season | progress track | bank caption.
            Label(x + 14, y + 31, 205, 20, _nextText, _small, Ivory);
            Line(x + 236, y + 39, 136, 3, new Color(1, 1, 1, 0.12f));
            if (_snapshot.HasNextSeason)
                Line(x + 236, y + 39, 136 * Mathf.Clamp01(_snapshot.Progress), 3, currentColor);
            Label(x + 400, y + 31, 132, 20, "主城金库", _small, Muted);
        }
        catch (Exception ex) { _valid = false; _retryAfter = Time.unscaledTime + 1f; LogOnce(ex); }
        finally
        {
            GUI.skin = savedSkin; GUI.color = savedColor; GUI.contentColor = savedContent;
            GUI.backgroundColor = savedBackground; GUI.matrix = savedMatrix;
            GUI.enabled = savedEnabled; GUI.changed = savedChanged; GUI.depth = savedDepth;
        }
    }

    private static void Label(float x, float y, float w, float h, string text, GUIStyle style, Color color)
    {
        if (w <= 0 || h <= 0) return;
        GUI.contentColor = color;
        // Keep unusually long reign dates and bank balances inside their own columns without allocating styles.
        int originalSize = style.fontSize;
        float units = 0f;
        foreach (char c in text) units += c > 127 ? 1f : c == ' ' ? 0.3f : 0.65f;
        style.fontSize = Mathf.Min(originalSize, Mathf.Max(10, Mathf.FloorToInt((w - 4f) / Mathf.Max(1f, units))));
        try
        {
            // 1px dark shadow pass first for contrast on any background.
            GUI.contentColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(x + 1, y + 1, w, h), text, style);
            GUI.contentColor = color;
            GUI.Label(new Rect(x, y, w, h), text, style);
        }
        finally { style.fontSize = originalSize; GUI.contentColor = Color.white; }
    }

    private static void Line(float x, float y, float w, float h, Color color)
    {
        if (w <= 0 || h <= 0) return;
        GUI.color = color;
        ImGuiCompat.DrawTexture(new Rect(x, y, w, h), _white);
        GUI.color = Color.white;
    }

    private static void Icon(int index, float x, float y, float size, Color tint)
    {
        GUI.color = tint;
        ImGuiCompat.DrawTexture(new Rect(x, y, size, size), Icons[index]);
        GUI.color = Color.white;
    }

    private static int SeasonIndex(Season season) => season switch
    {
        Season.Spring => 0, Season.Summer => 1, Season.Autumn => 2, Season.Winter => 3, _ => -1
    };

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春", Season.Summer => "夏", Season.Autumn => "秋", Season.Winter => "冬", _ => "季"
    };

    private static void EnsureResources()
    {
        bool ready = _white != null && _large != null && _number != null && _small != null;
        for (int i = 0; i < Icons.Length; i++) ready &= Icons[i] != null;
        if (ready) return;
        // Unity may destroy hidden resources during a reload. Recreate as one bounded cache.
        Destroy(_white);
        for (int i = 0; i < Icons.Length; i++) { Destroy(Icons[i]); Icons[i] = null; }
        _white = MakeTexture(1, 1, (x, y) => Color.white);
        for (int i = 0; i < Icons.Length; i++)
        {
            int index = i;
            // Binary 16x16: one sample per pixel, no supersampling, rendered point-filtered.
            Icons[i] = MakeTexture(16, 16, (u, v) =>
                IconShape(index, u, v) ? new Color(1, 1, 1, 1) : new Color(0, 0, 0, 0));
        }
        _large = TextStyle(18, FontStyle.Normal);
        _number = TextStyle(17, FontStyle.Normal);
        _small = TextStyle(12, FontStyle.Normal);
        if (!_presentationLogged)
        {
            _presentationLogged = true;
            Font skinFont = GUI.skin != null ? GUI.skin.font : null;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[CalendarHUD] floating strip ready; style=GUI.skin.label font="
                + (skinFont != null ? skinFont.name : "null") + " sizes=18/17/12 shadow=1px");
        }
    }

    private static GUIStyle TextStyle(int size, FontStyle weight)
    {
        // Own a copy of the default label style so its native fallback font chain renders CJK
        // exactly as ModPanel does; never assign an explicit font or load external fonts.
        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = size; style.fontStyle = weight;
        style.alignment = TextAnchor.MiddleLeft;
        style.padding = new RectOffset(0, 0, 0, 0);
        style.margin = new RectOffset(0, 0, 0, 0);
        style.normal.textColor = Color.white;
        style.wordWrap = false;
        style.richText = false;
        style.clipping = TextClipping.Clip;
        return style;
    }

    private static Texture2D MakeTexture(int width, int height, Func<float, float, Color> sample)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Point;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) pixels[y * width + x] = sample((x + 0.5f) / width, (y + 0.5f) / height);
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static bool IconShape(int index, float x, float y)
    {
        float dx = x - 0.5f, dy = y - 0.5f;
        float radius = Mathf.Sqrt(dx * dx + dy * dy);
        if (index == 0) // Spring: two leaves opening above a stem.
            return Segment(x, y, 0.50f, 0.16f, 0.50f, 0.63f, 0.034f)
                || Leaf(x, y, 0.33f, 0.58f, -0.65f, 0.24f, 0.115f)
                || Leaf(x, y, 0.65f, 0.71f, 0.65f, 0.25f, 0.115f);
        if (index == 1) // Summer: filled sun with eight separated rays.
        {
            if (radius < 0.205f) return true;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                if (Segment(x, y, 0.5f + Mathf.Cos(a) * 0.29f, 0.5f + Mathf.Sin(a) * 0.29f,
                    0.5f + Mathf.Cos(a) * 0.40f, 0.5f + Mathf.Sin(a) * 0.40f, 0.027f)) return true;
            }
            return false;
        }
        if (index == 2) // Autumn: pointed leaf with a cut-out central vein.
        {
            bool leaf = Leaf(x, y, 0.55f, 0.56f, 0.78f, 0.38f, 0.20f);
            bool vein = Segment(x, y, 0.36f, 0.37f, 0.74f, 0.74f, 0.021f);
            return (leaf && !vein) || Segment(x, y, 0.20f, 0.19f, 0.37f, 0.36f, 0.027f);
        }
        if (index == 3) // Winter: six-armed branched snow crystal.
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3, c = Mathf.Cos(a), s = Mathf.Sin(a);
                if (Segment(x, y, 0.5f, 0.5f, 0.5f + c * 0.4f, 0.5f + s * 0.4f, 0.023f)) return true;
                for (int side = -1; side <= 1; side += 2)
                    if (Segment(x, y, 0.5f + c * 0.24f, 0.5f + s * 0.24f,
                        0.5f + c * 0.32f - s * 0.10f * side, 0.5f + s * 0.32f + c * 0.10f * side, 0.020f)) return true;
            }
            return false;
        }
        if (index == 4) // Clock, intentionally no minute readout.
            return Mathf.Abs(radius - 0.37f) < 0.029f
                || Segment(x, y, 0.5f, 0.5f, 0.5f, 0.75f, 0.031f)
                || Segment(x, y, 0.5f, 0.5f, 0.69f, 0.42f, 0.031f);
        if (index == 5) // Arrow pointing at the upcoming season.
            return Segment(x, y, 0.18f, 0.5f, 0.80f, 0.5f, 0.04f)
                || Segment(x, y, 0.56f, 0.73f, 0.80f, 0.5f, 0.04f)
                || Segment(x, y, 0.56f, 0.27f, 0.80f, 0.5f, 0.04f);
        // index 6: coin — thick ring with a solid core, stays legible at HUD icon size.
        return Mathf.Abs(radius - 0.34f) < 0.085f || radius < 0.12f;
    }

    private static bool Leaf(float x, float y, float cx, float cy, float angle, float length, float width)
    {
        float dx = x - cx, dy = y - cy;
        float u = dx * Mathf.Cos(angle) + dy * Mathf.Sin(angle);
        float v = -dx * Mathf.Sin(angle) + dy * Mathf.Cos(angle);
        float t = Mathf.Abs(u) / length;
        return t < 1f && Mathf.Abs(v) < width * (1f - t * t);
    }

    private static bool Segment(float x, float y, float ax, float ay, float bx, float by, float width)
    {
        float dx = bx - ax, dy = by - ay;
        float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
        float px = x - ax - t * dx, py = y - ay - t * dy;
        return px * px + py * py <= width * width;
    }

    private static void Destroy(Texture2D texture)
    {
        if (texture != null) UnityEngine.Object.Destroy(texture);
    }

    private static void LogOnce(Exception ex)
    {
        if (_faultLogged) return;
        _faultLogged = true;
        KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[CalendarHUD] Hidden until valid game state: " + ex.GetType().Name);
    }
}
