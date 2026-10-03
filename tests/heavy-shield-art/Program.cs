// Executes the production metadata directly; the final manifests are the independent oracle.
using System.Text.Json;
using KingdomEnhancedMod;

int checks = 0, groups = 0;
List<string> failures = new();
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
void Near(float actual, double expected, string name) => Check(Math.Abs(actual - expected) < 0.000002, name + ": " + actual);
void Group(string name, Action body)
{
    try { body(); groups++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
string FindRoot()
{
    foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (DirectoryInfo dir = new(start); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "artifacts", "gem-shield-assets-20261001", "animation-manifest.json"))) return dir.FullName;
    throw new Exception("final asset manifests not found");
}
string root = FindRoot();
using JsonDocument characterDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts", "gem-shield-assets-20261001", "animation-manifest.json")));
using JsonDocument shopDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts", "gem-shield-assets-20261001", "shop-bilateral-manifest.json")));
HeavyShieldAtlasId[] ids = Enum.GetValues<HeavyShieldAtlasId>();
bool Soldier(HeavyShieldAtlasId id) => id == HeavyShieldAtlasId.Soldier || id == HeavyShieldAtlasId.SoldierCoarse;
bool Composite(HeavyShieldAtlasId id) => id == HeavyShieldAtlasId.Shop || id == HeavyShieldAtlasId.ShopCoarse;
string Density(HeavyShieldAtlasId id) => HeavyShieldArtLayout.IsCoarse(id) ? "coarse" : "detail";
JsonElement Character(HeavyShieldAtlasId id) => characterDoc.RootElement.GetProperty("density").GetProperty(Density(id));
JsonElement Shop(HeavyShieldAtlasId id) => shopDoc.RootElement.GetProperty("densities").GetProperty(Density(id));
int[] Ints(JsonElement x) => x.EnumerateArray().Select(v => v.GetInt32()).ToArray();

Group("all eight atlas ids and final geometry", () =>
{
    Check(ids.Length == 8, "8 atlas ids");
    Check((int)HeavyShieldAtlasId.Soldier == 0 && (int)HeavyShieldAtlasId.Shop == 1 && (int)HeavyShieldAtlasId.ShopLayers == 2 && (int)HeavyShieldAtlasId.GreekShopLayers == 3, "original id values preserved");
    string[] resources = { "HeavyShieldSoldierAtlas.png", "HeavyShieldShopAtlas.png", "HeavyShieldShopLayers.png", "HeavyShieldGreekShopLayers.png", "HeavyShieldSoldierAtlasCoarse.png", "HeavyShieldShopAtlasCoarse.png", "HeavyShieldShopLayersCoarse.png", "HeavyShieldGreekShopLayersCoarse.png" };
    foreach (var id in ids)
    {
        var metadata = Soldier(id) ? Character(id) : Shop(id);
        int[] cell = Ints(metadata.GetProperty("cell")), foot = Ints(metadata.GetProperty("foot"));
        int columns = Soldier(id) ? metadata.GetProperty("columns").GetInt32() : 4;
        int rows = Soldier(id) ? metadata.GetProperty("rows").GetInt32() : 4;
        int frames = Soldier(id) ? metadata.GetProperty("frameCount").GetInt32() : Composite(id) ? 13 : 14;
        Check(HeavyShieldArtLayout.ResourceName(id) == "KingdomEnhancedMod." + resources[(int)id], id + " resource");
        Check(HeavyShieldArtLayout.CellWidth(id) == cell[0] && HeavyShieldArtLayout.CellHeight(id) == cell[1], id + " cell");
        Check(HeavyShieldArtLayout.Columns(id) == columns && HeavyShieldArtLayout.Rows(id) == rows, id + " grid");
        Check(HeavyShieldArtLayout.SheetWidth(id) == columns * cell[0] && HeavyShieldArtLayout.SheetHeight(id) == rows * cell[1], id + " sheet");
        Check(HeavyShieldArtLayout.FrameCount(id) == frames, id + " frame count");
        Near(HeavyShieldArtLayout.PivotX(id), (foot[0] + .5) / cell[0], id + " foot x");
        Near(HeavyShieldArtLayout.PivotY(id), (cell[1] - foot[1] - .5) / cell[1], id + " foot y");
        Near(HeavyShieldArtLayout.PixelsPerUnitFor(id), metadata.GetProperty(Soldier(id) ? "ppuForBodyHeight0_7" : "ppu").GetDouble(), id + " PPU");
        if (Soldier(id)) Near(metadata.GetProperty("body").GetInt32() / HeavyShieldArtLayout.PixelsPerUnitFor(id), .7, id + " standing body height");
        else { Near(cell[0] / HeavyShieldArtLayout.PixelsPerUnitFor(id), 3, id + " shop world width"); Near(cell[1] / HeavyShieldArtLayout.PixelsPerUnitFor(id), 1.875, id + " shop world height"); }
    }
    Check(HeavyShieldArtLayout.PaidShieldResource == "KingdomEnhancedMod.HeavyShieldPaidShield.png" && HeavyShieldArtLayout.PaidShieldCoarseResource == "KingdomEnhancedMod.HeavyShieldPaidShieldCoarse.png", "independent paid resources");
    Near(HeavyShieldArtLayout.PaidShieldPivotX, .5, "paid bottom center x"); Near(HeavyShieldArtLayout.PaidShieldPivotY, 0, "paid bottom center y");
});

Group("every production sequence agrees with final fps loop and contiguous slots", () =>
{
    foreach (var id in ids)
    {
        Check(HeavyShieldArtLayout.Validate(id, out string error), id + " valid: " + error);
        if (Soldier(id))
        {
            JsonProperty[] expected = Character(id).GetProperty("sequences").EnumerateObject().ToArray();
            Check(expected.Length == 77 && HeavyShieldArtLayout.SequenceCount(id) == 77, id + " 77 sequences");
            int cursor = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                var x = expected[i];
                Check(HeavyShieldArtLayout.TryGetSequenceAt(id, i, out var sequence), id + " readable sequence");
                Check(sequence.Name == x.Name && sequence.First == x.Value.GetProperty("first").GetInt32() && sequence.Count == x.Value.GetProperty("count").GetInt32(), id + " " + x.Name + " interval");
                Near(sequence.Fps, x.Value.GetProperty("fps").GetDouble(), id + " " + x.Name + " fps");
                Check(sequence.Loop == x.Value.GetProperty("loop").GetBoolean(), id + " " + x.Name + " loop");
                Check(sequence.First == cursor && sequence.Count > 0 && sequence.Fps > 0, id + " continuity");
                cursor += sequence.Count;
            }
            Check(cursor == 509, id + " covers 0..508");
        }
        else
        {
            string theme = id == HeavyShieldAtlasId.GreekShopLayers || id == HeavyShieldAtlasId.GreekShopLayersCoarse ? "greek" : "medieval";
            var themeMeta = Shop(id).GetProperty("themes").GetProperty(theme);
            if (Composite(id))
            {
                Check(HeavyShieldArtLayout.TryGetSequence(id, "states", out var states) && states.First == 0 && states.Count == 7 && !states.Loop, id + " states");
                Check(HeavyShieldArtLayout.TryGetSequence(id, "idle", out var idle) && idle.First == 7 && idle.Count == 6 && idle.Loop, id + " idle"); Near(idle.Fps, themeMeta.GetProperty("smithFps").GetDouble(), id + " smith fps");
            }
            else
                foreach (JsonProperty x in themeMeta.GetProperty("sequences").EnumerateObject())
                {
                    int[] interval = Ints(x.Value);
                    Check(HeavyShieldArtLayout.TryGetSequence(id, x.Name, out var sequence) && sequence.First == interval[0] && sequence.Count == interval[1], id + " " + x.Name + " interval");
                    Check(sequence.Fps > 0 && sequence.Loop == (x.Name == "merchant"), id + " " + x.Name + " playback");
                    if (x.Name == "merchant") Near(sequence.Fps, themeMeta.GetProperty("smithFps").GetDouble(), id + " merchant fps");
                }
        }
    }
});

Group("19 actions for each of four wear states plus one terminal break", () =>
{
    string[] actions = { "back_idle", "back_walk", "back_run", "guard_idle", "defense_advance", "equip", "stow", "block", "bash", "walk_start", "walk_start_alt", "walk_stop", "walk_stop_alt", "run_start", "run_stop", "relax_idle", "rest_enter", "rest_idle", "rest_exit" };
    HashSet<string> expected = new(StringComparer.Ordinal) { "break" };
    foreach (string prefix in new[] { "", "worn_", "critical_", "half_" }) foreach (string action in actions) expected.Add(prefix + action);
    Check(expected.Count == 77, "full set size");
    foreach (var id in new[] { HeavyShieldAtlasId.Soldier, HeavyShieldAtlasId.SoldierCoarse })
    {
        for (int i = 0; i < HeavyShieldArtLayout.SequenceCount(id); i++) { Check(HeavyShieldArtLayout.TryGetSequenceAt(id, i, out var s) && expected.Contains(s.Name), id + " no extra sequence"); }
        foreach (string name in expected) Check(HeavyShieldArtLayout.TryGetSequence(id, name, out _), id + " contains " + name);
        Check(!HeavyShieldArtLayout.TryGetSequence(id, "worn_guard", out _) && !HeavyShieldArtLayout.TryGetSequence(id, "critical_guard", out _), "obsolete guard names absent");
    }
});

Group("all frames offsets top-to-bottom rects and boundaries", () =>
{
    foreach (var id in ids)
    {
        int columns = HeavyShieldArtLayout.Columns(id), rows = HeavyShieldArtLayout.Rows(id), width = HeavyShieldArtLayout.CellWidth(id), height = HeavyShieldArtLayout.CellHeight(id);
        for (int frame = 0; frame < HeavyShieldArtLayout.FrameCount(id); frame++)
        {
            Check(HeavyShieldArtLayout.IsValidFrame(id, frame), id + " valid frame");
            Check(HeavyShieldArtLayout.FrameToCell(id, frame, out int x, out int y) && x == frame % columns && y == frame / columns, id + " frame cell");
            int unityY = (rows - 1 - y) * height;
            Check(HeavyShieldArtLayout.SheetHeight(id) - unityY - height == y * height && x * width < HeavyShieldArtLayout.SheetWidth(id), id + " top-to-bottom rect");
        }
        for (int i = 0; i < HeavyShieldArtLayout.SequenceCount(id); i++)
        {
            Check(HeavyShieldArtLayout.TryGetSequenceAt(id, i, out var s), id + " sequence readable");
            for (int offset = 0; offset < s.Count; offset++) Check(HeavyShieldArtLayout.TryGetSequenceFrame(id, s.Name, offset, out int frame) && frame == s.First + offset && s.Last == s.First + s.Count - 1, id + " sequence offset");
            Check(!HeavyShieldArtLayout.TryGetSequenceFrame(id, s.Name, -1, out int a) && a == -1, id + " negative offset");
            Check(!HeavyShieldArtLayout.TryGetSequenceFrame(id, s.Name, s.Count, out int b) && b == -1, id + " offset end");
        }
        Check(!HeavyShieldArtLayout.IsValidFrame(id, -1) && !HeavyShieldArtLayout.IsValidFrame(id, HeavyShieldArtLayout.FrameCount(id)), id + " invalid bounds");
        Check(!HeavyShieldArtLayout.TryGetSequenceAt(id, -1, out _) && !HeavyShieldArtLayout.TryGetSequenceAt(id, HeavyShieldArtLayout.SequenceCount(id), out _), id + " sequence bounds");
        Check(!HeavyShieldArtLayout.TryGetSequence(id, null, out _) && !HeavyShieldArtLayout.TryGetSequence(id, "BACK_IDLE", out _), id + " exact names");
    }
    var unknown = (HeavyShieldAtlasId)99;
    Check(!HeavyShieldArtLayout.Validate(unknown, out _) && HeavyShieldArtLayout.ResourceName(unknown) == null && HeavyShieldArtLayout.PixelsPerUnitFor(unknown) == 0, "unknown fails closed");
    Check(!HeavyShieldArtLayout.FrameToCell(unknown, 0, out _, out _) && !HeavyShieldArtLayout.TryGetSequenceAt(unknown, 0, out _), "unknown lookup false");
});

Group("invalid tables reject holes duplicates overflow and unsafe fps", () =>
{
    void Reject(HeavyShieldSequence[] table, int frames = 1, int slots = 1) => Check(!HeavyShieldArtLayout.ValidateSequences(table, frames, slots, out string error) && !string.IsNullOrEmpty(error), "invalid sequence rejected");
    Check(new HeavyShieldSequence("legacy", 0, 1).Fps == 12f && !new HeavyShieldSequence("legacy", 0, 1).Loop, "old negative fixtures constructor default");
    Reject(null); Reject(Array.Empty<HeavyShieldSequence>()); Reject(new[] { new HeavyShieldSequence(null, 0, 1) });
    Reject(new[] { new HeavyShieldSequence("x", 0, 0) }); Reject(new[] { new HeavyShieldSequence("x", 1, 1) });
    Reject(new[] { new HeavyShieldSequence("x", 0, 1), new HeavyShieldSequence("x", 1, 1) }, 2, 2);
    Reject(new[] { new HeavyShieldSequence("x", 0, int.MaxValue) }, 1, int.MaxValue);
    foreach (float fps in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity }) Reject(new[] { new HeavyShieldSequence("x", 0, 1, fps, true) });
    Reject(new[] { new HeavyShieldSequence("x", 0, 1) }, 0, 1); Reject(new[] { new HeavyShieldSequence("x", 0, 1) }, 2, 1); Reject(new[] { new HeavyShieldSequence("x", 0, 1) }, 2, 2);
});
Console.WriteLine($"heavy-shield final art: {checks} checks, {groups} groups, {failures.Count} failures");
if (failures.Count != 0) Environment.Exit(1);
