using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Passive, cached calendar overlay. No controls, scene searches or game-state writes.</summary>
internal static class CalendarHud
{
    private const float Width = 688f, Height = 86f;
    private static readonly Color Gold = new Color(0.96f, 0.81f, 0.50f);
    private static readonly Color Ink = new Color(0.94f, 0.96f, 0.98f);
    private static readonly Color Muted = new Color(0.66f, 0.73f, 0.80f);
    private static readonly Color[] SeasonColors =
    {
        new Color(0.55f, 0.89f, 0.64f), new Color(1f, 0.79f, 0.35f),
        new Color(1f, 0.59f, 0.35f), new Color(0.64f, 0.85f, 1f)
    };
    private static Texture2D _back, _white;
    private static readonly Texture2D[] Icons = new Texture2D[6];
    private static GUIStyle _large, _small, _number;
    private static IntPtr _world, _scene, _director;
    private static float _nextRead, _retryAfter;
    private static bool _valid, _faultLogged;
    private static CalendarSnapshot _snapshot;
    private static string _dayText = "", _hourText = "", _seasonText = "", _nextText = "";

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
            _valid = CalendarReader.TryRead(director, out _snapshot);
            if (!_valid) return;
            _dayText = "第 " + _snapshot.TotalDay + " 天";
            _hourText = _snapshot.Hour + " 点";
            _seasonText = "第 " + _snapshot.SeasonDay + " 天";
            _nextText = _snapshot.HasNextSeason ? "第 " + _snapshot.NextSeasonDay + " 天开始" : "未定";
        }
        catch (Exception ex) { Clear(); _retryAfter = Time.unscaledTime + 1f; LogOnce(ex); }
    }

    private static void Clear()
    {
        _valid = false;
        _world = _scene = _director = IntPtr.Zero;
        _nextRead = 0f;
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
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.45f, 1f);
            float x = (Screen.width / scale - Width) * 0.5f;
            const float y = 18f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            ImGuiCompat.DrawTexture(new Rect(x, y, Width, Height), _back);
            Line(x + 22, y + 1, Width - 44, 1, new Color(Gold.r, Gold.g, Gold.b, 0.45f));
            Label(x + 23, y + 12, 156, 19, "王国历", _small, Muted);
            Label(x + 21, y + 33, 160, 34, _dayText, _large, Gold);
            Line(x + 183, y + 18, 1, 45, new Color(1, 1, 1, 0.12f));
            Icon(4, x + 199, y + 36, 27, Muted);
            Label(x + 232, y + 33, 77, 34, _hourText, _number, Ink);
            Label(x + 201, y + 12, 104, 19, "时刻", _small, Muted);
            Line(x + 309, y + 18, 1, 45, new Color(1, 1, 1, 0.12f));
            int season = SeasonIndex(_snapshot.CurrentSeason);
            Color currentColor = season >= 0 ? SeasonColors[season] : Muted;
            Label(x + 327, y + 12, 133, 19, "本季", _small, Muted);
            if (season >= 0) Icon(season, x + 323, y + 34, 34, currentColor);
            Label(x + 363, y + 34, 109, 32, _seasonText, _number, currentColor);
            Icon(5, x + 475, y + 42, 22, Muted);
            Label(x + 512, y + 12, 149, 19, "下一季", _small, Muted);
            int next = SeasonIndex(_snapshot.NextSeason);
            if (_snapshot.HasNextSeason && next >= 0)
                Icon(next, x + 504, y + 35, 31, SeasonColors[next]);
            Label(x + 542, y + 35, 132, 31, _nextText, _small, Ink);
            Line(x + 22, y + Height - 10, Width - 44, 3, new Color(1, 1, 1, 0.10f));
            if (_snapshot.HasNextSeason)
                Line(x + 22, y + Height - 10, (Width - 44) * Mathf.Clamp01(_snapshot.Progress), 3, currentColor);
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
        GUI.contentColor = color;
        // Keep unusually long reign dates inside their own columns without allocating styles.
        int originalSize = style.fontSize;
        float units = 0f;
        foreach (char c in text) units += c > 127 ? 1f : c == ' ' ? 0.3f : 0.65f;
        style.fontSize = Mathf.Min(originalSize, Mathf.Max(8, Mathf.FloorToInt((w - 4f) / Mathf.Max(1f, units))));
        try { GUI.Label(new Rect(x, y, w, h), text, style); }
        finally { style.fontSize = originalSize; GUI.contentColor = Color.white; }
    }

    private static void Line(float x, float y, float w, float h, Color color)
    {
        if (w <= 0) return;
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

    private static void EnsureResources()
    {
        bool ready = _back != null && _white != null && _large != null
            && _number != null && _small != null;
        for (int i = 0; i < Icons.Length; i++) ready &= Icons[i] != null;
        if (ready) return;
        // Unity may destroy hidden resources during a reload. Recreate as one bounded cache.
        Destroy(_back); Destroy(_white);
        for (int i = 0; i < Icons.Length; i++) { Destroy(Icons[i]); Icons[i] = null; }
        _back = MakeTexture(344, 43, (x, y) =>
        {
            float px = x * 344f, py = y * 43f;
            float dx = Mathf.Max(Mathf.Abs(px - 172f) - 166f, 0f);
            float dy = Mathf.Max(Mathf.Abs(py - 21.5f) - 15.5f, 0f);
            float edge = Mathf.Clamp01(6.5f - Mathf.Sqrt(dx * dx + dy * dy));
            return new Color(0.045f + y * 0.022f, 0.06f + y * 0.023f, 0.08f + y * 0.03f, edge * 0.90f);
        });
        _white = MakeTexture(1, 1, (x, y) => Color.white);
        for (int i = 0; i < Icons.Length; i++)
        {
            int index = i;
            Icons[i] = MakeTexture(64, 64, (x, y) =>
            {
                int samples = 0;
                for (int sx = 0; sx < 3; sx++)
                    for (int sy = 0; sy < 3; sy++)
                        if (IconShape(index, x + (sx - 1) / 192f, y + (sy - 1) / 192f)) samples++;
                return new Color(1, 1, 1, samples / 9f);
            });
        }
        _large = TextStyle(26, FontStyle.Bold);
        _number = TextStyle(22, FontStyle.Normal);
        _small = TextStyle(17, FontStyle.Normal);
    }

    private static GUIStyle TextStyle(int size, FontStyle weight)
    {
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
        texture.filterMode = FilterMode.Bilinear;
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
        return Segment(x, y, 0.18f, 0.5f, 0.80f, 0.5f, 0.04f)
            || Segment(x, y, 0.56f, 0.73f, 0.80f, 0.5f, 0.04f)
            || Segment(x, y, 0.56f, 0.27f, 0.80f, 0.5f, 0.04f);
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
