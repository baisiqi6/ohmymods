using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

// Unity's Create input is normalized; its pivot getter is in pixels. The shared
// host shim models that conversion so alignment is tested against engine semantics.
internal static class GeometryContractRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Texture2D[] Textures = new Texture2D[2];

    internal static void Run(Action<bool, string> check)
    {
        var failures = new List<string>();
        int count = 0;
        void Assert(bool ok, string why) { ++count; if (!ok) failures.Add(why); }
        Managers.Inst.game.state = Game.State.Playing;
        Time.timeScale = 1f;
        var clock = new HeavyShieldGemPulse();
        clock.TryStart(1d, true);
        clock.Sample(1.2d, true);
        Assert(clock.Sample(1.15d, true) == 0f && !clock.IsActive,
            "a backwards sample inside an active pulse cannot brighten it again");
        Assert(!clock.TryStart(1.16d, true) && clock.TryStart(1.46d, true),
            "a fresh monotonic event after the original interval can recover");

        double now = 100d;
        foreach (bool coarse in new[] { false, true })
        foreach (int parentSign in new[] { -1, 1 })
        foreach (bool flip in new[] { false, true })
        {
            var parent = new GameObject("geometry-parent");
            parent.transform.localScale = new Vector3(parentSign * 2f, 3f, 1f);
            var go = new GameObject("geometry-body");
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = new Vector3(.5f, 1f / 3f, 1f);
            var own = go.AddComponent<SpriteRenderer>();
            own.sprite = Source(coarse);
            own.sharedMaterial = new Material();
            own.color = new Color(.3f, .5f, .8f, .25f);
            own.flipX = flip;
            var fx = HeavyShieldBlockFx.TryCreate(own);
            using var lease = new FxLease(fx);
            Assert(fx != null, "optional sink created for engine-accurate geometry");
            if (fx == null) continue;
            Time.unscaledTimeAsDouble = now;
            Time.unscaledTime = (float)now;
            now += 1d;
            fx.OnBlock();
            int facing = (flip ? -1 : 1) * parentSign;
            fx.Sync(0, facing);
            var body = (SpriteRenderer)Get(fx, "_body");
            var sparks = (SpriteRenderer[])Get(fx, "_sparks");
            var source = own.sprite;
            Assert(body.enabled && body.sprite != null, "real source geometry still has a positive flash");
            if (body.sprite == null) continue;
            Assert(Near(source.pivot.x, coarse ? 17.5f : 24.5f)
                && Near(source.pivot.y, coarse ? 2.5f : 4.5f), "fixture exposes real pixel-valued pivot");
            Assert(Near(source.pivot.x, body.sprite.pivot.x) && Near(source.pivot.y, body.sprite.pivot.y),
                "palette renderer preserves the same native pixel pivot");
            Assert(Near(source.pixelsPerUnit, body.sprite.pixelsPerUnit), "palette uses the original PPU");
            Assert(Near(body.color.a, HeavyShieldGemPulse.PeakAlpha * .25f)
                && Near(body.color.r, own.color.r) && Near(body.color.g, own.color.g)
                && Near(body.color.b, own.color.b), "pulse inherits current body tint and alpha");
            float ppu = source.pixelsPerUnit;
            float[] x = { -.16f, .11f, -.03f }, y = { .52f, .34f, .10f };
            for (int i = 0; i < sparks.Length; i++)
            {
                float localX = (float)Math.Round(x[i] * ppu) / ppu;
                float localY = (float)Math.Round(y[i] * ppu) / ppu;
                var expected = own.transform.TransformPoint(new Vector3(flip ? -localX : localX, localY, -.001f));
                var actual = sparks[i].transform.position;
                Assert(Near(actual.x, expected.x) && Near(actual.y, expected.y),
                    "spark follows the same sprite flip and signed parent once");
            }
        }

        // Fail after the first child was enabled, before the layer-on flag committed.
        var failingGo = new GameObject("partial-fx");
        var failingOwn = failingGo.AddComponent<SpriteRenderer>();
        failingOwn.sprite = Source(false);
        failingOwn.sharedMaterial = new Material();
        var failingFx = HeavyShieldBlockFx.TryCreate(failingOwn);
        using (new FxLease(failingFx))
        {
            var fx = failingFx;
            var sparks = (SpriteRenderer[])Get(fx, "_sparks");
            SpriteRenderer.BeforeEnabledChange = (r, enabled) =>
            { if (enabled && ReferenceEquals(r, sparks[1])) throw new InvalidOperationException("controlled second spark failure"); };
            try
            {
                Time.unscaledTimeAsDouble = now;
                Time.unscaledTime = (float)now;
                fx.OnBlock(); fx.Sync(0, 1);
            }
            finally { SpriteRenderer.BeforeEnabledChange = null; }
            Assert(sparks.All(s => !s.enabled), "partial enable failure closes every owned spark");
            Assert(failingOwn.enabled, "optional rendering failure keeps the original body");
        }
        HeavyShieldBlockFx.ClearUnused();
        var savingGo = new GameObject("saving-fx");
        var savingOwn = savingGo.AddComponent<SpriteRenderer>();
        savingOwn.sprite = Source(false); savingOwn.sharedMaterial = new Material();
        var savingFx = HeavyShieldBlockFx.TryCreate(savingOwn);
        using (new FxLease(savingFx))
        {
            Time.unscaledTimeAsDouble = now; Time.unscaledTime = (float)now;
            savingFx.OnBlock(); savingFx.Sync(0, 1);
            Assert(((SpriteRenderer)Get(savingFx, "_body")).enabled, "save control starts visible");
            IslandSaveData.isSavingGame = true;
            try { savingFx.Sync(0, 1); }
            finally { IslandSaveData.isSavingGame = false; }
            Assert(!((SpriteRenderer)Get(savingFx, "_body")).enabled, "serialization cancels a live glow");
            savingFx.Sync(0, 1);
            Assert(!((SpriteRenderer)Get(savingFx, "_body")).enabled, "save completion does not replay old light");
        }
        HeavyShieldBlockFx.ClearUnused();

        // Same dimensions/PPU alone do not authorize fixed positions on replacement art.
        var tiny = new Texture2D(1024, 1536, TextureFormat.RGBA32, false);
        var tinyPixels = new Color32[1024 * 1536];
        for (int f = 0; f < 509; f++)
            tinyPixels[((31 - f / 16) * 48 + 10) * 1024 + f % 16 * 64 + 10] = new Color32(100, 100, 100, 255);
        tiny.SetPixels32(tinyPixels); tiny.Apply(false, false);
        var tinyGo = new GameObject("replacement-art");
        var tinyOwn = tinyGo.AddComponent<SpriteRenderer>();
        tinyOwn.sprite = Sprite.Create(tiny, new Rect(0, 31 * 48, 64, 48), new Vector2(24.5f / 64, 4.5f / 48),
            HeavyShieldArtLayout.SoldierPixelsPerUnit, 0u, SpriteMeshType.FullRect);
        tinyOwn.sharedMaterial = new Material();
        var tinyFx = HeavyShieldBlockFx.TryCreate(tinyOwn);
        using (new FxLease(tinyFx))
        {
            tinyFx.Prepare(tinyOwn, 0);
            Assert((bool)Get(tinyFx, "_closed") && tinyOwn.enabled,
                "unknown alpha geometry turns off only FX, preserving the base body");
        }
        HeavyShieldBlockFx.ClearUnused();
        using (var restored = new FxFixture())
        {
            Time.unscaledTimeAsDouble = now + 1; Time.unscaledTime = (float)(now + 1);
            Assert(restored.Attach(0, true), "pending-break restore still attaches");
            HeavyShieldActorVisuals.Tick(restored.Handle.GoId);
            var rows = (System.Collections.IDictionary)typeof(HeavyShieldActorVisuals)
                .GetField("Active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var restoredFx = (HeavyShieldBlockFx)Get(rows[restored.Handle.GoId], "Fx");
            Assert(HeavyShieldActorVisuals.TryPose(restored.Archer, restored.Handle.Life, out var pose, out _)
                && pose == HeavyShieldAction.Break, "restoring break keeps the saved pose");
            Assert(!((SpriteRenderer)Get(restoredFx, "_body")).enabled,
                "restoring a saved break pose is not a new successful attack block");
        }
        HeavyShieldBlockFx.ClearUnused();
        Console.WriteLine($"geometry contracts: {count} assertions, {failures.Count} failures");
        foreach (string failure in failures) Console.WriteLine("FAIL " + failure);
        check(failures.Count == 0, "engine-unit, flip, clock and partial-cleanup contracts");
    }

    private sealed class FxLease : IDisposable
    {
        private readonly HeavyShieldBlockFx _fx;
        internal FxLease(HeavyShieldBlockFx fx) { _fx = fx; }
        public void Dispose() => _fx?.Dispose();
    }

    private static object Get(object obj, string name) => obj.GetType().GetField(name, Hidden).GetValue(obj);
    private static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
    private static Sprite Source(bool coarse)
    {
        int index = coarse ? 1 : 0;
        int cw = coarse ? 48 : 64, ch = coarse ? 32 : 48;
        if (Textures[index] == null)
        {
            int w = cw * 16, h = ch * 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int frame = 0; frame < 509; frame++)
            {
                int sx = frame % 16 * cw, sy = (31 - frame / 16) * ch;
                for (int y = coarse ? 3 : 4; y < (coarse ? 25 : 36); y++)
                for (int x = coarse ? 9 : 10; x < (coarse ? 27 : 40); x++)
                    pixels[(sy + y) * w + sx + x] = new Color32(100, 140, 220, 255);
            }
            tex.SetPixels32(pixels); tex.Apply(false, false);
            Textures[index] = tex;
        }
        var pivot = coarse ? new Vector2(17.5f / cw, 2.5f / ch) : new Vector2(24.5f / cw, 4.5f / ch);
        return Sprite.Create(Textures[index], new Rect(0, 31 * ch, cw, ch), pivot,
            coarse ? HeavyShieldArtLayout.SoldierCoarsePixelsPerUnit : HeavyShieldArtLayout.SoldierPixelsPerUnit,
            0u, SpriteMeshType.FullRect);
    }
}
