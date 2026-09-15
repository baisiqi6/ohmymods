using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace FriendlyTrollDisguiseTests
{
    /// <summary>资格判定：原生面具路径 + 44 头饰委托 + 开关/权威/世界/存活 + 池复用 + 只读。</summary>
    internal static class IsProtectedTests
    {
        internal static void Run()
        {
            Case.Run("原生面具 index 0 → 保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(0, maskRenderer: true);
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "index 0 应保护");
                Fixture.AssertNativeFieldsUntouched(unit, "index 0");
            });

            Case.Run("原生面具 index 5 → 保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(5, maskRenderer: true);
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "index 5 应保护");
            });

            Case.Run("index -1 无面具且无头饰 → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(-1);
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "无面具应不保护");
            });

            Case.Run("index 6 超出原生面具范围 → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(6, maskRenderer: true);
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "index 6 不是原生面具");
                Check.Equal(1, PatchDivine_HermesHeadwear.Calls, "原生路径判否后应落到头饰委托（结果为 false）");
            });

            Case.Run("renderer disabled → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true, maskEnabled: false);
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "disabled renderer 不算显示");
            });

            Case.Run("sprite 为 null → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true, maskSprite: false);
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "无 sprite 不算显示");
            });

            Case.Run("mask 子对象 inactive → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                unit.MaskObject.activeInHierarchy = false;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "inactive renderer 不算显示");
            });

            Case.Run("自有帽隐藏原生 mask，但 44 头饰为真 → 仍保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true, maskEnabled: false);
                PatchDivine_HermesHeadwear.DisguiseHeadwear = true;
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll),
                    "自有帽隐藏原生 mask 时头饰路径必须兜住");
                Check.Equal(1, PatchDivine_HermesHeadwear.Calls, "应委托 HasDisguiseHeadwear 一次");
                Check.Same(unit.Troll, PatchDivine_HermesHeadwear.LastTroll, "委托实参应为该 troll");
            });

            Case.Run("无原生面具 + 44 头饰委托为真 → 保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(-1);
                PatchDivine_HermesHeadwear.DisguiseHeadwear = true;
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "头饰路径应独立生效");
                Check.Equal(1, PatchDivine_HermesHeadwear.Calls, "原生无面具时应直接查头饰");
                Check.Same(unit.Troll, PatchDivine_HermesHeadwear.LastTroll, "委托实参应为该 troll");
            });

            Case.Run("无原生面具 + 44 头饰委托为假 → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(-1);
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "未伪装应不保护");
                Check.Equal(1, PatchDivine_HermesHeadwear.Calls, "无原生面具时必须查头饰");
            });

            Case.Run("裸面具可见时不查头饰（原生路径短路）", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(2, maskRenderer: true);
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "index 2 应保护");
                Check.Equal(0, PatchDivine_HermesHeadwear.Calls, "原生面具已足够，不该再查头饰");
            });

            Case.Run("ModConfig.Enabled=false → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                ModConfig.Enabled.Value = false;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "开关关闭应不保护");
            });

            Case.Run("ModConfig.Enabled 为 null → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                ModConfig.Enabled = null;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "配置未就绪应不保护");
            });

            Case.Run("无世界权威（客户端）→ 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                NetworkBigBoss.HasWorldAuth = false;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "客户端不裁决");
            });

            Case.Run("异 world（IsCurrent=false）→ 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                OptionalQoLScope.SameIsland = false;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "换岛滞留不应保护");
            });

            Case.Run("单位 inactive → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true, unitActive: false);
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "inactive 单位不保护");
            });

            Case.Run("组件 disabled → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                unit.Troll.enabled = false;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "disabled 组件不保护");
            });

            Case.Run("Damageable 已死 → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                unit.Damageable.isDead = true;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "已死单位不保护");
            });

            Case.Run("Damageable 所在对象 inactive → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                unit.Damageable.gameObject.activeInHierarchy = false;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "死对象不保护");
            });

            Case.Run("无 Damageable 组件 → 不保护", () =>
            {
                GameObject go = Fixture.NewObject();
                var troll = go.AddComponent<FriendlyTroll>();
                var mask = Fixture.NewObject().AddComponent<SpriteRenderer>();
                mask.sprite = new Sprite();
                troll._mask = mask;
                troll._maskIndex = 1;
                Check.False(FriendlyTrollDisguise.IsProtected(troll), "无 Damageable 不保护");
            });

            Case.Run("troll 已销毁 → 不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                unit.Object.MarkDestroyed();
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "销毁对象不保护");
            });

            Case.Run("池复用：同实例资格按当前字段现算，不缓存", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "复用前应有面具资格");

                // 池回收：原生清字段（disguise 拆掉）
                unit.Mask.enabled = false;
                unit.Troll._maskIndex = -1;
                unit.Troll._mask = null;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "回收后应立刻失去资格");

                // 同一实例再次出池：重新戴上面具，资格必须回来（证明无资格缓存）
                unit.Troll._mask = unit.Mask;
                unit.Mask.enabled = true;
                unit.Troll._maskIndex = 4;
                unit.Troll.MaskIndexWrites = 0;
                unit.Troll.MaskWrites = 0;
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "重新出池应恢复资格");
                Fixture.AssertNativeFieldsUntouched(unit, "池复用后");
            });

            Case.Run("资格判定只读原生字段（重复调用结果一致）", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(1, maskRenderer: true);
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "首次应保护");
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "二次仍应保护");
                Fixture.AssertNativeFieldsUntouched(unit, "只读性");
            });

            Case.Run("HasDisguiseHeadwear 抛错 → 回退不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(-1);
                PatchDivine_HermesHeadwear.Throw = true;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "头饰查询失败应回退 false");
            });

            Case.Run("原生 mask 读取抛错 → 回退不保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(3, maskRenderer: true);
                PatchDivine_HermesHeadwear.DisguiseHeadwear = true;
                unit.Troll.ThrowOnMaskRead = true;
                Check.False(FriendlyTrollDisguise.IsProtected(unit.Troll), "读取失败应回退 false");
            });

            Case.Run("null troll → 不保护", () =>
            {
                Check.False(FriendlyTrollDisguise.IsProtected(null), "null 应回退 false");
            });

            Case.Run("正常子 renderer → 保护", () =>
            {
                FriendlyUnit unit = Fixture.Friendly(1, maskRenderer: true);
                Check.True(unit.Mask.transform.IsChildOf(unit.Troll.transform), "夹具应为父子关系");
                Check.True(FriendlyTrollDisguise.IsProtected(unit.Troll), "本 troll 名下的 mask 应保护");
            });

            Case.Run("mask 已被别的 troll 租走（残留旧引用）→ 不保护；租走者保护", () =>
            {
                FriendlyUnit renter = Fixture.Friendly(-1);
                FriendlyUnit stale = Fixture.Friendly(3, maskRenderer: true);

                // 池复用：同一个 renderer 转挂到 renter 名下，stale 字段里只剩残留旧引用。
                stale.MaskObject.Transform.SetParent(renter.Object.Transform);
                renter.Troll._mask = stale.Mask;
                renter.Troll._maskIndex = 3;
                renter.Troll.MaskWrites = 0;
                renter.Troll.MaskIndexWrites = 0;

                Check.False(FriendlyTrollDisguise.IsProtected(stale.Troll),
                    "旧引用不属于本 troll 层级，不算伪装");
                Check.True(FriendlyTrollDisguise.IsProtected(renter.Troll),
                    "实际持有 renderer 的 troll 应保护");
                Fixture.AssertNativeFieldsUntouched(renter, "租走者");
            });
        }
    }
}
