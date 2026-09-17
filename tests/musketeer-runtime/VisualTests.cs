using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 池复用（评审 final-corrections #2）：同一 pointer+GoId 的 Archer 在 OnEnable 复用为新的火铳手 life 时，
    /// 旧 life 的 Fire/Reload/idle 相位与 native-hide 责任**绝不**跨 life——
    /// `MusketeerVisuals.BeforeReuse` 在回调里复位/退场、把销毁与失败重试留给 Sync。
    /// </summary>
    internal static class VisualTests
    {
        internal static void Run()
        {
            Case.Run("reuse resets the old Fire/Reload phase before the new life renders", ReuseResetsOldPhase);
            Case.Run("reuse then reapply shows a fresh idle frame (old phase does not carry)", ReuseThenReapply);
            Case.Run("reuse without a new career never leaves the native hidden", ReuseWithoutNewCareer);
            Case.Run("native-hide release failure keeps a receipt and retries on Sync", ReleaseFailureKeepsReceipt);
            Case.Run("own visual carries the shared 0.9 appearance scale (absolute; Z stays 1)", OwnVisualAppearanceScale);
        }

        private static void OwnVisualAppearanceScale()
        {
            Archer archer = ArmedMusketeerWithVisuals(out _);
            SpriteRenderer own = FindOwn(archer);
            Check.True(own != null, "own renderer exists");
            Transform ownRoot = own.transform;
            Check.Near(MusketeerAtlas.AppearanceScale, ownRoot.localScale.x, 1e-6d, "X = shared appearance scale");
            Check.Near(MusketeerAtlas.AppearanceScale, ownRoot.localScale.y, 1e-6d, "Y = shared appearance scale");
            Check.Near(1d, ownRoot.localScale.z, 1e-6d, "Z stays 1 (depth untouched)");
            Check.True(ReferenceEquals(ownRoot.parent, archer._spriteRenderer.transform),
                "still mounted under the native renderer (foot-pivot anchor)");
        }

        private static void ReuseResetsOldPhase()
        {
            Archer archer = ArmedMusketeerWithVisuals(out Sprite[] sprites);
            SpriteRenderer own = FindOwn(archer);
            Check.True(own != null, "own renderer exists");
            SpriteRenderer native = archer._spriteRenderer;

            ShowFirePhase(archer, sprites, own);
            Check.True(native.forceRenderingOff, "the current life hides the native renderer");
            Check.True(own.enabled, "own renderer visible during Fire");

            // Reload 相位（真实时钟推进）
            Time.time = 0.5f;
            Sync();
            int reloadFrame = Index(own.sprite, sprites);
            Check.True(reloadFrame >= 40 && reloadFrame <= 51, "showing a Reload frame before reuse: " + reloadFrame);

            UnityEngine.Object.Destroyed.Clear();
            MusketeerRuntime.Archer_OnEnable_MusketeerPackage_Patch.Prefix(archer);   // 池复用（同一对象）

            Check.False(MusketeerVisuals.HasVisual(archer), "the old life visual is no longer the current life's");
            Check.False(native.forceRenderingOff, "native hide responsibility was released (CAS)");
            Check.False(own.enabled, "old own renderer is hidden immediately");
            Check.False(UnityEngine.Object.Destroyed.Contains(own.gameObject),
                "destruction is deferred out of the callback");

            Sync();
            Check.True(UnityEngine.Object.Destroyed.Contains(own.gameObject), "deferred destroy completes on Sync");
            Check.Equal(0, MusketeerVisuals.Count, "no visual until a life re-applies");
            Check.True(MusketeerRuntime.IsArmedMusketeer(archer),
                "a still-marked life keeps its combat package through the visual reuse");
        }

        private static void ReuseThenReapply()
        {
            Archer archer = ArmedMusketeerWithVisuals(out Sprite[] sprites);
            SpriteRenderer own = FindOwn(archer);
            ShowFirePhase(archer, sprites, own);
            Time.time = 0.5f;
            Sync();
            Check.True(Index(own.sprite, sprites) >= 40, "old life was in Reload");

            MusketeerRuntime.Archer_OnEnable_MusketeerPackage_Patch.Prefix(archer);
            Sync();
            Check.Equal(0, MusketeerVisuals.Count, "old visual fully retired");

            // 新 life：重新挂载（runtime 会通过 Tick/ReconcileApplied 调用同样的入口）
            MusketeerVisuals.Apply(archer);
            Check.True(MusketeerVisuals.HasVisual(archer), "new life gets a fresh visual");
            archer._shootingTarget = null;   // 新 life 尚未选目标 → 应是待机相位
            Time.time = 1.0f;
            SetNativeState(archer, "Stand", 0f, 0.5f);
            Sync();

            SpriteRenderer fresh = FindOwn(archer);
            Check.True(fresh != null && fresh != own, "a brand new own renderer serves the new life");
            int frame = Index(fresh.sprite, sprites);
            Check.True(frame >= 0 && frame <= 11, "fresh life shows an idle frame, not the old phase: " + frame);
            Check.True(archer._spriteRenderer.forceRenderingOff, "the new life hides the native again");
        }

        private static void ReuseWithoutNewCareer()
        {
            Archer archer = ArmedMusketeerWithVisuals(out Sprite[] sprites);
            SpriteRenderer own = FindOwn(archer);
            ShowFirePhase(archer, sprites, own);

            MusketeerIdentity.Units.Remove(archer);           // 池化后被别的职业复用
            MusketeerRuntime.Archer_OnEnable_MusketeerPackage_Patch.Prefix(archer);
            Check.False(archer._spriteRenderer.forceRenderingOff,
                "the old career never leaves the native renderer hidden");
            Check.False(MusketeerVisuals.HasVisual(archer), "no visual for a non-musketeer life");

            Sync();
            Check.True(UnityEngine.Object.Destroyed.Contains(own.gameObject), "old own renderer retired");
            Check.Equal(0, MusketeerVisuals.Count, "nothing re-applies for a non-musketeer");
        }

        private static void ReleaseFailureKeepsReceipt()
        {
            Archer archer = ArmedMusketeerWithVisuals(out Sprite[] sprites);
            SpriteRenderer own = FindOwn(archer);
            ShowFirePhase(archer, sprites, own);
            Check.True(archer._spriteRenderer.forceRenderingOff, "native hidden before reuse");

            archer._spriteRenderer.ThrowOnForceRenderingOffSet = true;   // 归还那一次写失败
            MusketeerRuntime.Archer_OnEnable_MusketeerPackage_Patch.Prefix(archer);
            Check.False(MusketeerVisuals.HasVisual(archer), "old life visual detached even though the release failed");
            Check.True(archer._spriteRenderer.forceRenderingOff, "native still hidden until the release succeeds");

            Sync();
            Check.False(archer._spriteRenderer.forceRenderingOff,
                "the retained receipt retried on Sync and released the native hide");
            Check.True(UnityEngine.Object.Destroyed.Contains(own.gameObject), "the root was still retired");
        }

        // ---------- helpers ----------

        private static Archer ArmedMusketeerWithVisuals(out Sprite[] sprites)
        {
            Fixture.Reset();
            Fixture.NewWorld();
            Fixture.InstallNativeArcherPrefab();
            sprites = new Sprite[MusketeerAtlas.FrameCount];
            for (int i = 0; i < sprites.Length; i++) sprites[i] = new Sprite();
            MusketeerVisuals.SetAtlasForTests(sprites);

            Archer archer = Fixture.ArmMusketeer();
            Time.frameCount++;
            MusketeerRuntime.Tick(); // visual reconciliation follows successful package setup
            Check.True(MusketeerVisuals.HasVisual(archer), "visual created for the armed musketeer");
            // 有合法地面目标 = 处于举枪姿态（与游戏里开火中的火铳手一致）。
            archer._shootingTarget = Fixture.NewEnemy(EnemyType.TrollWeak);
            SetNativeState(archer, "Stand", 0f, 0.5f);
            Sync();
            return archer;
        }

        /// <summary>真实射击事件 → Fire 相位（与 gameplay 同一条路径）。</summary>
        private static void ShowFirePhase(Archer archer, Sprite[] sprites, SpriteRenderer own)
        {
            Time.time = 0.05f;
            MusketeerVisuals.NotifyShot(archer, 4.3333333f);
            Sync();
            int frame = Index(own.sprite, sprites);
            Check.True(frame >= 36 && frame <= 39, "showing a Fire frame after the confirmed shot: " + frame);
        }

        private static void SetNativeState(Archer archer, string stateName, float normalizedTime, float length)
        {
            archer._animator.State = new AnimatorStateInfo
            {
                shortNameHash = Animator.StringToHash(stateName),
                normalizedTime = normalizedTime,
                length = length,
            };
        }

        private static void Sync()
        {
            Time.frameCount++;
            Time.deltaTime = 0.05f;
            MusketeerVisuals.Sync();
        }

        private static SpriteRenderer FindOwn(Archer archer)
        {
            Transform native = archer._spriteRenderer.transform;
            for (int i = 0; i < native.Children.Count; i++)
            {
                Transform child = native.Children[i];
                if (child == null || child.gameObject == null) continue;
                SpriteRenderer renderer = child.gameObject.GetComponent<SpriteRenderer>();
                if (renderer != null) return renderer;
            }
            return null;
        }

        private static int Index(Sprite sprite, Sprite[] sprites)
            => sprite == null ? -1 : System.Array.IndexOf(sprites, sprite);
    }
}
