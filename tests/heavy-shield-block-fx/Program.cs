using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

int checks = 0;
void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
bool Near(float a, float b, float eps = .0006f) => Math.Abs(a - b) <= eps;
void At(double t) { Time.unscaledTimeAsDouble = t; Time.unscaledTime = (float)t; }
void SetPlaying(bool on) { Time.timeScale = on ? 1f : 0f; Managers.Inst.game.state = on ? Game.State.Playing : Game.State.Other; }
var visuals = (System.Collections.IDictionary)typeof(HeavyShieldActorVisuals).GetField("Active", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
object Row(FxFixture f) => visuals[f.Handle.GoId];
object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
SpriteRenderer OwnOf(FxFixture f) => (SpriteRenderer)Field(Row(f), "Own");
HeavyShieldBlockFx FxOf(FxFixture f) => (HeavyShieldBlockFx)Field(Row(f), "Fx");
SpriteRenderer Body(HeavyShieldBlockFx fx) => (SpriteRenderer)Field(fx, "_body");
SpriteRenderer[] Sparks(HeavyShieldBlockFx fx) => (SpriteRenderer[])Field(fx, "_sparks");
HeavyShieldGemPulse Pulse(HeavyShieldBlockFx fx) => (HeavyShieldGemPulse)Field(fx, "_pulse");
bool Closed(HeavyShieldBlockFx fx) => (bool)Field(fx, "_closed");
float BodyWidth(Sprite s) => s.rect.width / s.pixelsPerUnit;
float BodyHeight(Sprite s) => s.rect.height / s.pixelsPerUnit;

Texture2D SourceTexture(bool coarse, int seed)
{
    int width = coarse ? 768 : 1024, height = coarse ? 1024 : 1536;
    var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
    var pixels = new Color32[width * height];
    for (int y = 0; y < height; y++)
    {
        int row = y * width;
        for (int x = 0; x < width; x++)
        {
            bool clear = (x % 13) < 2 && (y % 9) < 2;
            byte v = (byte)((x * 7 + y * 11 + seed * 37) & 255);
            pixels[row + x] = clear ? new Color32(0, 0, 0, 0)
                : new Color32((byte)(v / 3), v, (byte)(255 - v), (byte)(x % 17 == 0 ? 128 : 255));
        }
    }
    texture.SetPixels32(pixels);
    texture.filterMode = FilterMode.Point;
    texture.wrapMode = TextureWrapMode.Clamp;
    texture.anisoLevel = 0;
    texture.Apply(false, false);
    return texture;
}
Sprite SourceSprite(Texture2D texture, bool coarse, int frame)
{
    int cellWidth = coarse ? 48 : 64, cellHeight = coarse ? 32 : 48, columns = 16, rows = 32;
    int column = frame % columns, row = frame / columns;
    float y = (rows - 1 - row) * cellHeight;
    var pivot = coarse ? new Vector2(17.5f / 48f, 2.5f / 32f) : new Vector2(24.5f / 64f, 4.5f / 48f);
    float ppu = coarse ? 31.4285714f : 45.7142857f;
    return Sprite.Create(texture, new Rect(column * cellWidth, y, cellWidth, cellHeight), pivot, ppu, 0u, SpriteMeshType.FullRect);
}
SpriteRenderer DummyOwn(Sprite sprite)
{
    var go = new GameObject("fx-own");
    var renderer = go.AddComponent<SpriteRenderer>();
    renderer.sprite = sprite;
    renderer.sortingLayerID = 7;
    renderer.sortingOrder = 4;
    renderer.gameObject.layer = 9;
    renderer.sharedMaterial = new Material();
    return renderer;
}
void Guard(FxFixture f)
{
    HeavyShieldRuntime.Tick();
    for (int i = 0; i < 8; i++)
    {
        Time.deltaTime = .25f; Time.time += .25f; Time.frameCount++;
        At(Time.unscaledTimeAsDouble + .25);
        HeavyShieldActorVisuals.Tick(f.Handle.GoId);
        HeavyShieldRuntime.Tick();
    }
    Check(HeavyShieldActorVisuals.GuardReady(f.Archer, f.Handle.Life), "guard ready");
}
void TickAt(FxFixture f, double now, float dt = .02f)
{
    At(now); Time.deltaTime = dt; Time.frameCount++;
    HeavyShieldActorVisuals.Tick(f.Handle.GoId);
}
bool Trigger(FxFixture f, HeavyShieldAction action)
{
    if (action != HeavyShieldAction.Block && action != HeavyShieldAction.Break)
        return HeavyShieldActorVisuals.Trigger(f.Archer, f.Handle.Life, action);
    f.Enemy.transform.position = new Vector3(f.Go.transform.position.x + .3f, f.Go.transform.position.y);
    int nativeHits = f.Damage.NativeHits;
    HeavyShieldCombat.ObserveTrollIntent(f.Enemy, f.Damage);
    using var scope = HeavyShieldCombat.EnterTroll(f.Enemy, f.Damage);
    f.Damage.ReceiveDamage(1, f.Enemy.gameObject, DamageSource.Troll);
    return f.Damage.NativeHits == nativeHits;
}

// Pure pulse: fixed natural-clock windows, no restart, no stacking, pause/rollback cancel.
{
    var p = new HeavyShieldGemPulse();
    Check(!p.TryStart(1, false), "paused start rejected");
    Check(p.TryStart(1, true), "first pulse accepted");
    Check(Near(p.Sample(1, true), HeavyShieldGemPulse.PeakAlpha), "exact peak alpha");
    Check(Near(p.Sample(1.14, true), HeavyShieldGemPulse.PeakAlpha * .5f), "linear tail half");
    Check(!p.TryStart(1.15, true), "active duplicate ignored");
    Check(p.Sample(1.279, true) > 0f, "tail remains before 0.28");
    Check(p.Sample(1.28, true) == 0f && !p.IsActive, "exact 0.28 end");
    Check(!p.TryStart(1.3, true), "0.45 receive interval ignores early hit");
    Check(p.TryStart(1.45, true), "0.45 receive interval opens");
    Check(Near(p.Sample(1.45, true), HeavyShieldGemPulse.PeakAlpha), "new pulse is not dim-stacked");
    p.Cancel();
    Check(!p.IsActive, "cancel is explicit");
}
{
    var p = new HeavyShieldGemPulse();
    Check(p.TryStart(10, true), "hf start");
    for (int i = 0; i < 1000; i++) p.Sample(10 + i * .0001, true);
    Check(p.Sample(10.2799, true) > 0f, "high-frequency sampling does not extend");
    Check(p.Sample(10.28, true) == 0f, "high-frequency expiry is natural-clock exact");
    Check(!p.TryStart(10.3, true), "high-frequency hit does not bypass interval");
}
{
    var p = new HeavyShieldGemPulse();
    Check(p.TryStart(20, true), "rollback source start");
    Check(p.Sample(19.9, true) == 0f && !p.IsActive, "backward clock cancels");
    Check(!p.TryStart(19.99, true), "backward try-start cancels and rejects");
    Check(!p.TryStart(20.2, true), "cooldown remains anchored to last accepted");
    Check(p.TryStart(20.45, true), "clock catch-up eventually accepts");
    Check(!p.TryStart(double.NaN, true) && !p.TryStart(double.PositiveInfinity, true) && !p.TryStart(-1, true), "invalid clocks rejected");
    var paused = new HeavyShieldGemPulse();
    Check(paused.TryStart(30, true), "pause source");
    Check(paused.Sample(30.05, false) == 0f && !paused.IsActive, "pause cancels instead of pausing the flash");
    Check(!paused.TryStart(30.1, true), "resume does not replay");
}

// Deterministic palette: source alpha exact, transparent RGB zero, diagonal blue/purple facets,
// brightness order preserved and a finite ramp.
{
    uint Pack(byte r, byte g, byte b, byte a) => ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    byte R(uint v) => (byte)v; byte G(uint v) => (byte)(v >> 8); byte B(uint v) => (byte)(v >> 16); byte A(uint v) => (byte)(v >> 24);
    int Lum(uint v) => R(v) * 54 + G(v) * 183 + B(v) * 19;
    Check(HeavyShieldGemPalette.Map(Pack(255, 170, 90, 0), 0, 0) == 0, "transparent source maps to clear");
    uint source = Pack(128, 128, 128, 200);
    uint blue = HeavyShieldGemPalette.Map(source, 0, 0);
    uint purple = HeavyShieldGemPalette.Map(source, 5, 0);
    Check(blue != purple, "fixed diagonal facet switches blue/purple");
    Check(A(blue) == 200 && A(purple) == 200, "every source alpha preserved exactly");
    Check((R(blue) | G(blue) | B(blue)) != 0 && (R(purple) | G(purple) | B(purple)) != 0, "opaque output keeps visible rgb");
    uint dark = HeavyShieldGemPalette.Map(Pack(16, 16, 16, 255), 3, 3);
    uint bright = HeavyShieldGemPalette.Map(Pack(240, 240, 240, 255), 3, 3);
    Check(Lum(bright) > Lum(dark), "source brightness structure survives");
    Check(HeavyShieldGemPalette.Map(source, 7, 9) == HeavyShieldGemPalette.Map(source, 7, 9), "palette is deterministic");
    var ramp = new HashSet<uint>();
    for (int l = 0; l < 256; l++) ramp.Add(HeavyShieldGemPalette.Map(Pack((byte)l, (byte)l, (byte)l, 255), 0, 0) & 0xFFFFFFu);
    Check(ramp.Count <= 16, "blue facet uses a finite brightness ramp");
    int lit = 0;
    for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) if (A(HeavyShieldGemPalette.SparkPixel(x, y)) != 0) lit++;
    Check(lit == 5 && A(HeavyShieldGemPalette.SparkPixel(0, 0)) == 0, "3x3 diamond spark");
}

GeometryContractRegression.Run(Check);

// Direct sink: own palette texture, exact rect/pivot/PPU, 3 sparks, order/layer/material mirror,
// frame sprite reuse, sharedMaterial replacement and both facings.
{
    var texture = SourceTexture(false, 1);
    var source = SourceSprite(texture, false, 0);
    var own = DummyOwn(source);
    var fx = HeavyShieldBlockFx.TryCreate(own);
    Check(fx != null, "sink create");
    var body = Body(fx); var sparks = Sparks(fx); var root = body.gameObject;
    Check(!body.enabled && sparks.Length == 3 && !sparks[0].enabled, "sink starts inert");
    At(100); SetPlaying(true); fx.OnBlock(); fx.Sync(0, 1);
    Check(body.enabled, "sink enables on accepted pulse");
    Check(body.sprite != null && !ReferenceEquals(body.sprite.texture, texture), "palette texture is newly owned");
    Check(body.sprite.texture.width == 1024 && body.sprite.texture.height == 1536, "palette keeps full detail atlas size");
    Check(body.sprite.rect.x == source.rect.x && body.sprite.rect.y == source.rect.y
        && body.sprite.rect.width == source.rect.width && body.sprite.rect.height == source.rect.height, "frame rect copied");
    Check(Near(body.sprite.pixelsPerUnit, source.pixelsPerUnit) && Near(body.sprite.pivot.x, source.pivot.x)
        && Near(body.sprite.pivot.y, source.pivot.y), "pivot/ppu copied");
    Check(Near(body.color.a, HeavyShieldGemPulse.PeakAlpha) && body.color.r == 1f && body.color.g == 1f && body.color.b == 1f,
        "renderer color drives exact alpha only");
    Check(body.sortingLayerID == own.sortingLayerID && body.sortingOrder == own.sortingOrder + 1
        && body.gameObject.layer == own.gameObject.layer && ReferenceEquals(body.sharedMaterial, own.sharedMaterial),
        "look mirrored at Own+1 without material writes");
    Check(body.transform.IsChildOf(own.transform), "fx layer is under Own root");
    for (int i = 0; i < 3; i++)
    {
        Check(sparks[i].enabled && sparks[i].sprite != null && sparks[i].sortingOrder == own.sortingOrder + 1
            && ReferenceEquals(sparks[i].sharedMaterial, own.sharedMaterial), "spark " + i + " mirrors look");
        Vector3 p = sparks[i].transform.localPosition;
        float width = BodyWidth(source), height = BodyHeight(source);
        float minX = -source.pivot.x / source.pixelsPerUnit, maxX = (source.rect.width - source.pivot.x) / source.pixelsPerUnit;
        float minY = -source.pivot.y / source.pixelsPerUnit, maxY = (source.rect.height - source.pivot.y) / source.pixelsPerUnit;
        Check(p.x >= minX && p.x <= maxX && p.y >= minY && p.y <= maxY, "spark " + i + " stays inside body bounds");
    }
    var sourcePixels = texture.GetPixels32();
    var generated = body.sprite.texture.GetPixels32();
    int opaque = -1, clear = -1;
    for (int i = 0; i < sourcePixels.Length && (opaque < 0 || clear < 0); i++)
    {
        if (opaque < 0 && sourcePixels[i].a != 0) opaque = i;
        if (clear < 0 && sourcePixels[i].a == 0) clear = i;
    }
    Check(opaque >= 0 && generated[opaque].a == sourcePixels[opaque].a
        && (generated[opaque].r | generated[opaque].g | generated[opaque].b) != 0, "source alpha kept at same texel");
    Check(clear >= 0 && generated[clear].a == 0 && (generated[clear].r | generated[clear].g | generated[clear].b) == 0, "transparent texels stay clear");
    var frameSprites = new Sprite[5];
    for (int frame = 1; frame <= 4; frame++) frameSprites[frame] = SourceSprite(texture, false, frame);
    int creates = Sprite.CreateCalls;
    for (int frame = 1; frame <= 4; frame++)
    {
        own.sprite = frameSprites[frame];
        fx.Sync(frame, 1);
        Check(body.sprite.rect.x == own.sprite.rect.x && body.sprite.rect.y == own.sprite.rect.y, "frame " + frame + " same-tick rect");
    }
    int afterFrames = Sprite.CreateCalls;
    Check(afterFrames - creates == 4, "one cached frame sprite per new frame");
    fx.Sync(4, 1);
    Check(Sprite.CreateCalls == afterFrames, "repeated frame does not create sprites");
    var replacement = new Material();
    own.sharedMaterial = replacement;
    fx.Sync(4, 1);
    Check(ReferenceEquals(body.sharedMaterial, replacement) && ReferenceEquals(sparks[0].sharedMaterial, replacement), "sharedMaterial replacement follows same tick");
    float[] right = new float[3];
    for (int i = 0; i < 3; i++) right[i] = sparks[i].transform.localPosition.x;
    float centerX = 0f; // Sprite flip is around the pixel pivot / local origin.
    own.flipX = true;
    fx.Sync(4, -1);
    Check(body.flipX == own.flipX, "body flipX mirrors Own");
    for (int i = 0; i < 3; i++) Check(Near(sparks[i].transform.localPosition.x + right[i], centerX * 2f), "spark " + i + " mirrors with world facing");
    fx.HideAndCancel();
    Check(!body.enabled && !sparks[0].enabled && !Pulse(fx).IsActive, "hide cancels and disables every layer");
    At(100.5); fx.OnBlock(); fx.Sync(4, 1);
    Check(body.enabled, "next accepted pulse re-enables the same owned layer");
    own.color = new Color(1f, 1f, 1f, 0f);
    fx.Sync(4, 1);
    Check(!body.enabled && !Pulse(fx).IsActive, "native alpha zero immediately hides the fx layer");
    var paletteTexture = body.sprite.texture;
    fx.Dispose();
    Check(!root.activeInHierarchy, "dispose destroys owned child root");
    Check(!texture.Destroyed, "source texture is never destroyed or rewritten");
    HeavyShieldBlockFx.ClearUnused();
    Check(paletteTexture.Destroyed, "zero-ref palette texture is released by ClearUnused");
}
// Coarse density: fixed 768x1024 atlas and 48x32 cell, no fallback to detail.
{
    var texture = SourceTexture(true, 2);
    var source = SourceSprite(texture, true, 0);
    var own = DummyOwn(source);
    var fx = HeavyShieldBlockFx.TryCreate(own);
    Check(fx != null, "coarse sink create");
    At(200); SetPlaying(true); fx.OnBlock(); fx.Sync(0, 1);
    var body = Body(fx);
    Check(body.enabled && body.sprite.texture.width == 768 && body.sprite.texture.height == 1024, "coarse palette dimensions");
    Check(body.sprite.rect.width == 48 && body.sprite.rect.height == 32
        && Near(body.sprite.pixelsPerUnit, HeavyShieldArtLayout.SoldierCoarsePixelsPerUnit)
        && Near(body.sprite.pivot.x, HeavyShieldArtLayout.SoldierCoarseFootPixelX + .5f), "coarse rect/pivot/ppu");
    fx.Dispose();
    HeavyShieldBlockFx.ClearUnused();
}
// Cache: one identity shares a slot; two busy slots are never evicted for a third identity.
{
    HeavyShieldBlockFx.ClearUnused();
    var textureA = SourceTexture(false, 11); var textureB = SourceTexture(false, 12); var textureC = SourceTexture(false, 13);
    var ownA = DummyOwn(SourceSprite(textureA, false, 0));
    var ownB = DummyOwn(SourceSprite(textureB, false, 0));
    var ownC = DummyOwn(SourceSprite(textureC, false, 0));
    var ownA2 = DummyOwn(SourceSprite(textureA, false, 0));
    var fxA = HeavyShieldBlockFx.TryCreate(ownA); var fxB = HeavyShieldBlockFx.TryCreate(ownB);
    var fxC = HeavyShieldBlockFx.TryCreate(ownC); var fxA2 = HeavyShieldBlockFx.TryCreate(ownA2);
    At(300); SetPlaying(true);
    fxA.OnBlock(); fxA.Sync(0, 1);
    fxB.OnBlock(); fxB.Sync(0, 1);
    fxC.OnBlock(); fxC.Sync(0, 1);
    Check(Body(fxA).enabled && Body(fxA).sprite != null, "first identity ready");
    Check(Body(fxB).enabled && Body(fxB).sprite != null, "second identity ready");
    Check(!Body(fxC).enabled && Closed(fxC), "third identity closes this life instead of evicting");
    Check(Body(fxA).sprite.texture.width == 1024 && !textureA.Destroyed, "in-use first slot retained");
    Check(!textureC.Destroyed, "busy failure does not destroy the new source");
    int reads = textureA.GetPixels32Calls;
    fxA2.OnBlock(); fxA2.Sync(0, 1);
    Check(Body(fxA2).enabled && textureA.GetPixels32Calls == reads, "same identity reuses baked palette");
    fxA.Dispose(); fxA2.Dispose(); fxB.Dispose(); fxC.Dispose();
    HeavyShieldBlockFx.ClearUnused();
    var retry = DummyOwn(SourceSprite(textureC, false, 0));
    var fxD = HeavyShieldBlockFx.TryCreate(retry);
    At(301); fxD.OnBlock(); fxD.Sync(0, 1);
    Check(Body(fxD).enabled, "zero-ref slot can be reused after references release");
    fxD.Dispose();
    HeavyShieldBlockFx.ClearUnused();
}

// Real Actor/Runtime wiring: successful Block/Break only, exact flash, hidden/suspend/death/
// unregister teardown, and FX exceptions never change the pose/block result.
{
    HeavyShieldBlockFx.ClearUnused();
    using var f = new FxFixture();
    At(400); SetPlaying(true);
    Check(f.Attach(), "actor attach");
    Guard(f);
    var fx = FxOf(f); Check(fx != null, "actor owns fx instance");
    var body = Body(fx); var sparks = Sparks(fx); var own = OwnOf(f);
    Check(!body.enabled, "actor fx stays inert while no block arrives");
    At(500); Check(Trigger(f, HeavyShieldAction.Bash), "bash remains a pose event");
    TickAt(f, 500.02);
    Check(!body.enabled && Pulse(fx).IsActive == false, "bash never starts the block flash");
    At(501); Check(Trigger(f, HeavyShieldAction.Block), "block accepted");
    Check(HeavyShieldActorVisuals.TryPose(f.Archer, f.Handle.Life, out var action, out _) && action == HeavyShieldAction.Block, "block pose retained");
    TickAt(f, 501);
    Check(body.enabled && Near(body.color.a, HeavyShieldGemPulse.PeakAlpha), "actor body flashes at peak");
    Check(body.sprite.rect.x == own.sprite.rect.x && body.sprite.rect.y == own.sprite.rect.y
        && body.sprite.rect.width == own.sprite.rect.width && body.sprite.rect.height == own.sprite.rect.height, "full body uses the same frame");
    Check(body.sprite.texture.width == 1024 && body.sprite.texture.height == 1536, "actor palette uses the real source atlas size");
    Check(Near(body.sprite.pixelsPerUnit, own.sprite.pixelsPerUnit) && Near(body.sprite.pivot.x, own.sprite.pivot.x)
        && Near(body.sprite.pivot.y, own.sprite.pivot.y), "actor sprite keeps source pivot/ppu");
    Check(body.sortingOrder == own.sortingOrder + 1 && body.gameObject.layer == own.gameObject.layer
        && ReferenceEquals(body.sharedMaterial, own.sharedMaterial), "actor fx mirrors Own+1 look");
    Check(sparks.Length == 3 && sparks[0].enabled && sparks[0].sprite != null, "actor has three fixed sparks");
    int objects = GameObject.Created, reads = sourceReads(f);
    for (int i = 0; i < 10; i++)
    {
        At(502 + i * .5);
        Check(Trigger(f, HeavyShieldAction.Block), "continuous block " + i);
        TickAt(f, 502 + i * .5);
        Check(body.enabled, "continuous block remains visible " + i);
    }
    Check(GameObject.Created == objects, "continuous hits create no GameObjects");
    Check(sourceReads(f) == reads, "continuous hits never re-read the source atlas");
    var replacement = new Material();
    f.Archer._spriteRenderer.sharedMaterial = replacement;
    TickAt(f, 506.6);
    Check(ReferenceEquals(body.sharedMaterial, replacement), "actor sharedMaterial replacement same tick");
    HeavyShieldActorVisuals.SetFacing(f.Archer, f.Handle.Life, -1);
    TickAt(f, 506.62);
    Check(body.flipX == own.flipX, "actor body flip follows Own");
    // Pause cancels; resume must not replay the old flash.
    At(507); Check(Trigger(f, HeavyShieldAction.Block), "pause fixture block");
    TickAt(f, 507);
    SetPlaying(false);
    TickAt(f, 507.05);
    Check(!body.enabled && !Pulse(fx).IsActive, "pause hides and cancels");
    SetPlaying(true);
    TickAt(f, 507.2);
    Check(!body.enabled, "resume does not replay");
    // A decoration clock exception is contained and cannot change the block result.
    Time.UnscaledFails = true;
    Check(Trigger(f, HeavyShieldAction.Block), "clock exception does not cancel block");
    Check(HeavyShieldActorVisuals.TryPose(f.Archer, f.Handle.Life, out var faulted, out _) && faulted == HeavyShieldAction.Block, "clock exception keeps block pose");
    Time.UnscaledFails = false;
    Check(HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer, f.Handle.Life), "fx fault does not unqualify the body");
}
// Break, hidden native, suspend and unregister teardown.
{
    using (var f = new FxFixture())
    {
        At(500); SetPlaying(true); Check(f.Attach(1), "break attach"); Guard(f);
        var fx = FxOf(f); var body = Body(fx); var own = OwnOf(f);
        At(600); Check(Trigger(f, HeavyShieldAction.Break), "break accepted");
        TickAt(f, 600.02);
        Check(body.enabled && HeavyShieldActorVisuals.TryPose(f.Archer, f.Handle.Life, out var action, out _)
            && action == HeavyShieldAction.Break, "break flashes on its final event");
#if !ANDROID
        // Android 2.4 shares Renderer.enabled with our own hide, so only the PC
        // forceRenderingOff ledger can distinguish an external hide. Android host
        // coverage for this path is the suspend/unregister cases below.
        f.Archer._spriteRenderer.enabled = false;
        TickAt(f, 600.04);
        Check(!body.enabled && !Pulse(fx).IsActive, "native hidden tears the fx layer down");
        f.Archer._spriteRenderer.enabled = true;
#endif
    }
    using (var f = new FxFixture())
    {
        At(510); SetPlaying(true); Check(f.Attach(), "suspend attach"); Guard(f);
        var fx = FxOf(f); var body = Body(fx);
        At(611); Check(Trigger(f, HeavyShieldAction.Block), "suspend source block");
        TickAt(f, 611);
        Check(body.enabled, "suspend source visible");
        HeavyShieldActorVisuals.Suspend(f.Archer, f.Handle.Life);
        Check(!body.enabled && !Pulse(fx).IsActive, "suspend hides and cancels");
    }
    using (var f = new FxFixture())
    {
        At(520); SetPlaying(true); Check(f.Attach(), "unregister attach"); Guard(f);
        var fx = FxOf(f); var body = Body(fx); var root = body.gameObject;
        At(621); Check(Trigger(f, HeavyShieldAction.Block), "unregister source block"); TickAt(f, 621);
        Check(HeavyShieldActorVisuals.Unregister(f.Archer, f.Handle.Life), "unregister succeeds");
        Check(!visuals.Contains(f.Handle.GoId) && !root.activeInHierarchy, "unregister releases the fx child");
        Check(HeavyShieldActorVisuals.Register(f.Archer, f.Handle.Life), "same life rebinds a fresh visual");
        var fresh = FxOf(f);
        Check(fresh != null && !ReferenceEquals(fresh, fx), "rebind creates a new fx lifetime, not the stale one");
        Check(!root.activeInHierarchy, "old fx root stays destroyed");
        Check(HeavyShieldActorVisuals.Unregister(f.Archer, f.Handle.Life), "fresh unregister");
    }
    using (var f = new FxFixture())
    {
        At(700); SetPlaying(true); Check(f.Attach(), "world attach"); Guard(f);
        var fx = FxOf(f); var body = Body(fx); var fxRoot = body.gameObject;
        At(703); Check(Trigger(f, HeavyShieldAction.Block), "world source block"); TickAt(f, 703);
        Check(body.enabled, "world source flash visible");
        var oldWorld = Managers.Inst.world.gameLayer;
        Managers.Inst.world.gameLayer = new GameObject("other-world").transform;
        HeavyShieldActorVisuals.Tick(f.Handle.GoId);
        Check(!fxRoot.activeInHierarchy, "world switch destroys the attached fx child");
        Managers.Inst.world.gameLayer = oldWorld;
        Check(HeavyShieldActorVisuals.Unregister(f.Archer, f.Handle.Life), "world switch releases the retained native credential");
        Check(!visuals.Contains(f.Handle.GoId), "world switch leaves no active visual row");
    }
    using (var f = new FxFixture())
    {
        At(530); SetPlaying(true); Check(f.Attach(), "death attach"); Guard(f);
        var fx = FxOf(f); var body = Body(fx); var root = body.gameObject;
        At(631); Check(Trigger(f, HeavyShieldAction.Block), "death source block"); TickAt(f, 631);
        f.Damage.isDead = true;
        HeavyShieldRuntime.ObserveNativeDeath(f.Damage);
        Check(!visuals.Contains(f.Handle.GoId) && !root.activeInHierarchy, "native death tears down the fx child");
    }
    HeavyShieldBlockFx.ClearUnused();
    using (var f = new FxFixture())
    {
        At(540); SetPlaying(true); Check(f.Attach(), "prepare fault attach");
        var source = OwnOf(f).sprite.texture;
        source.Unreadable = true;
        TickAt(f, 540.02);
        source.Unreadable = false;
        var fx = FxOf(f);
        Check(fx != null && Closed(fx), "unreadable source closes this fx life once");
        Check(HeavyShieldActorVisuals.HasQualifiedVisual(f.Archer, f.Handle.Life), "palette failure never unqualifies the body");
        Guard(f); // A real block also requires the original guard/equip state.
        Check(Trigger(f, HeavyShieldAction.Block), "palette failure does not reject a later block");
        Check(HeavyShieldActorVisuals.TryPose(f.Archer, f.Handle.Life, out var action, out _) && action == HeavyShieldAction.Block, "palette failure keeps the block pose");
    }
    HeavyShieldBlockFx.ClearUnused();
}

int sourceReads(FxFixture f)
{
    var sprite = OwnOf(f).sprite;
    return sprite != null && sprite.texture != null ? sprite.texture.GetPixels32Calls : 0;
}

Console.WriteLine($"PASS heavy-shield block gem fx: {checks} assertions");

sealed class FxFixture : IDisposable
{
    internal GameObject Go = new("shield");
    internal Archer Archer;
    internal Damageable Damage;
    internal Troll Enemy;
    internal HeavyShieldCareerHandle Handle;
    internal FxFixture()
    {
        HeavyShieldIdentity.Current.World = Managers.Inst.world.gameLayer.Pointer.ToInt64();
        HeavyShieldIdentity.Current.Land = 0;
        Go.transform.SetParent(Managers.Inst.world.gameLayer, false);
        Go.transform.position = new(4.75f, 0);
        Archer = Go.AddComponent<Archer>();
        Damage = Go.AddComponent<Damageable>();
        var enemyGo = new GameObject("fx-source-troll");
        enemyGo.transform.SetParent(Managers.Inst.world.gameLayer, false);
        Enemy = enemyGo.AddComponent<Troll>(); Enemy.Type = EnemyType.TrollWeak;
        enemyGo.AddComponent<Damageable>();
        Archer._enemyScanner.observer = Go.transform;
        Archer._damageable = Damage;
        Archer._character = Go.AddComponent<Character>();
        Archer._mover = Go.AddComponent<Mover>();
        Archer.persistent = Go.AddComponent<Persistent>();
        Archer._spriteRenderer = Go.AddComponent<SpriteRenderer>();
        Archer._animator = Go.AddComponent<Animator>();
        Archer.Embarkee = Go.AddComponent<Embarkee>();
        Archer._guardSide = Side.Right;
        Archer._guardDepth = 3;
        Archer._absoluteFaceIndex = 9;
        Archer._cooldown = 7;
        Archer._mover.goalMode = Mover.GoalMode.Position;
        Archer._mover._goalPosition = 23;
        Archer._mover._goalSpeed = 2;
        Handle = new(Guid.NewGuid(), "campaign", HeavyShieldQuota.Side.Right, 0, Go.Pointer, Go.GetInstanceID(),
            Managers.Inst.world.gameLayer.Pointer.ToInt64(), Go.GetInstanceID());
        HeavyShieldIdentity.Careers.Add(Handle);
        HeavyShieldIdentity.Lives[Handle.Root] = Handle.Life;
        Managers.Inst.targetCache.RegisterPriorityTarget(Damage);
        Archer.MutationCheck = () => { if (!HeavyShieldRuntime.CarrierMutationInProgress) throw new Exception("missing carrier mutation mask"); };
        Embarkee.MutationCheck = Archer.MutationCheck;
    }
    internal bool Attach(int durability = 40, bool pendingBreak = false) => HeavyShieldRuntime.AttachCarrier(Archer, Handle, new HeavyShieldSavedCombatState(durability, pendingBreak, false));
    public void Dispose()
    {
        HeavyShieldRuntime.DetachCarrier(Handle);
        HeavyShieldIdentity.Careers.Remove(Handle);
        Managers.Inst.targetCache._trollPriorityTargets.Remove(Damage);
    }
}
