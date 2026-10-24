using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

namespace MusketeerRuntimeTests
{
    /// <summary>
    /// 付费枪具（DroppableTool 上的自有 SpriteRenderer 表现，MusketeerGunVisuals）：原生隐藏/归还、
    /// parked/拾取/池复用/Sync 稳定性。
    ///
    /// Android 2.4 语义：原生隐藏原语是共享 <see cref="Renderer.enabled"/>（forceRenderingOff 缺失），
    /// 因此我们写下的隐藏不能再被当作“游戏禁用 renderer”而触发 Restore；view 可见性改由
    /// entry.Owned 凭据判断（PC 分支保持原式，见 NativeGunVisible 注释）。
    /// </summary>
    internal static class GunVisualTests
    {
        internal static void Run()
        {
            Case.Run("gun view owns the native renderer and hides it without flapping", SteadyOwnership);
            Case.Run("parked gun keeps both renderers hidden across Sync", ParkedSurvivesSync);
            Case.Run("unpark restores the view without returning the native renderer", UnparkShowsView);
            Case.Run("picked-up gun returns the native renderer", PickedUpReleasesNative);
            Case.Run("a renderer hidden by a third party is never stolen", ThirdPartyHideNotStolen);
            Case.Run("pool reuse drops parked state and restores the native renderer", ReuseRestoresNative);
            Case.Run("failed native restore keeps ownership and retries on the next Sync", RestoreFailureKeepsReceipt);
        }

        private static void SteadyOwnership()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            SyncGuns();
            SpriteRenderer view = FindView(tool);
            Check.True(view != null, "gun view exists");
            Check.True(RendererContract.NativeHidden(native), "native gun renderer hidden");
            Check.True(view.enabled, "gun view visible");

            for (int i = 0; i < 3; i++) SyncGuns();
            Check.True(RendererContract.NativeHidden(native), "native stays hidden without flapping");
            Check.True(view.enabled, "view stays visible without flapping");
            Check.Equal(1, CountViews(tool), "exactly one gun view for the tool");
        }

        private static void ParkedSurvivesSync()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            SyncGuns();
            SpriteRenderer view = FindView(tool);
            Check.True(view != null, "gun view exists");

            MusketeerGunVisuals.SetParked(tool, true);
            Check.True(MusketeerGunVisuals.IsParked(tool), "gun registered as parked");
            Check.True(RendererContract.NativeHidden(native), "parked gun hides the native renderer");
            Check.False(view.enabled, "parked gun hides the view");

            for (int i = 0; i < 3; i++) SyncGuns();
            Check.True(MusketeerGunVisuals.IsParked(tool), "park survives Sync");
            Check.True(RendererContract.NativeHidden(native), "Sync does not restore a parked native renderer");
            Check.False(view.enabled, "parked view stays hidden across Sync");
        }

        private static void UnparkShowsView()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            SyncGuns();
            SpriteRenderer view = FindView(tool);
            MusketeerGunVisuals.SetParked(tool, true);

            MusketeerGunVisuals.SetParked(tool, false);
            Check.False(MusketeerGunVisuals.IsParked(tool), "unpark clears the parked flag");
            Check.True(view.enabled, "unpark shows the gun view");
            Check.True(RendererContract.NativeHidden(native), "unpark keeps our native hide");

            SyncGuns();
            Check.True(view.enabled, "view stays visible after unpark");
            Check.True(RendererContract.NativeHidden(native), "native stays hidden after unpark");
        }

        private static void PickedUpReleasesNative()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            SyncGuns();
            SpriteRenderer view = FindView(tool);

            tool.pickedUp = true;
            SyncGuns();
            Check.False(RendererContract.NativeHidden(native), "picked-up gun returns the native renderer");
            Check.False(view.enabled, "picked-up gun hides the view");
        }

        private static void ThirdPartyHideNotStolen()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            RendererContract.HideNative(native);   // 第三方在 Sync 之前隐藏了原生 renderer
            SyncGuns();
            Check.Equal(0, CountViews(tool), "no view is created over a renderer hidden by another owner");
            Check.True(RendererContract.NativeHidden(native), "foreign native hide is left untouched");
        }

        private static void ReuseRestoresNative()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            SyncGuns();
            SpriteRenderer view = FindView(tool);
            MusketeerGunVisuals.SetParked(tool, true);

            MusketeerIdentity.Guns.Clear();   // 池回收后该 tool 不再是当前世界的火铳手 gun
            Time.frameCount++;
            MusketeerGunVisuals.BeforeReuse(tool.gameObject);
            Check.False(MusketeerGunVisuals.IsParked(tool), "reuse clears the parked state");
            Check.False(RendererContract.NativeHidden(native), "reuse returns the native renderer immediately");
            Check.False(view.enabled, "reuse hides the old view immediately");
            Check.False(UnityEngine.Object.Destroyed.Contains(view.gameObject),
                "destruction stays deferred out of the reuse callback");

            SyncGuns();
            Check.True(UnityEngine.Object.Destroyed.Contains(view.gameObject),
                "the retired view is destroyed on the next ordinary Sync");
            Check.Equal(0, CountViews(tool), "no view remains for the recycled gun");
        }

        private static void RestoreFailureKeepsReceipt()
        {
            DroppableTool tool = NewTrackedGun(out SpriteRenderer native);
            SyncGuns();
            SpriteRenderer view = FindView(tool);
            Check.True(RendererContract.NativeHidden(native), "native hidden while owned");

            MusketeerIdentity.Guns.Clear();
            RendererContract.FailNextNativeWrite(native);
            SyncGuns();
            Check.False(view.enabled, "view hidden while the ownership release is attempted");
            Check.True(RendererContract.NativeHidden(native), "native stays hidden after a failed restore");

            SyncGuns();
            Check.False(RendererContract.NativeHidden(native),
                "the retained ownership retried on the next Sync and returned the native renderer");
            Check.True(UnityEngine.Object.Destroyed.Contains(view.gameObject), "the view was retired after the retry");
        }

        // ---------- helpers ----------

        private static DroppableTool NewTrackedGun(out SpriteRenderer native)
        {
            Fixture.Reset();
            Fixture.NewWorld();
            ResetGunVisuals();
            var go = new GameObject("gun");
            DroppableTool tool = go.AddComponent<DroppableTool>();
            native = go.AddComponent<SpriteRenderer>();
            MusketeerIdentity.Guns.Add(tool);
            return tool;
        }

        /// <summary>清掉上一个用例留下的静态表（测试专用；生产未提供 Reset 入口）。</summary>
        private static void ResetGunVisuals()
        {
            var entries = (System.Collections.IDictionary)typeof(MusketeerGunVisuals)
                .GetField("Entries", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            entries.Clear();
            typeof(MusketeerGunVisuals).GetField("_frame", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, -1);
        }

        private static void SyncGuns()
        {
            Time.frameCount++;
            MusketeerGunVisuals.Sync();
        }

        private static SpriteRenderer FindView(DroppableTool tool)
        {
            Transform parent = tool.transform;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                Transform child = parent.Children[i];
                if (child == null || child.gameObject == null) continue;
                SpriteRenderer view = child.gameObject.GetComponent<SpriteRenderer>();
                if (view != null) return view;
            }
            return null;
        }

        private static int CountViews(DroppableTool tool)
        {
            int count = 0;
            Transform parent = tool.transform;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                Transform child = parent.Children[i];
                if (child == null || child.gameObject == null) continue;
                if (child.gameObject.GetComponent<SpriteRenderer>() != null) count++;
            }
            return count;
        }
    }
}
