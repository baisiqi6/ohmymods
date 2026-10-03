using System;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 扩展岛地图层纯决策（可离线逐值复算，不依赖 Unity/IL2CPP）。
    ///
    /// 事实依据（native-travel / native-lifecycle / native-map-contract 实际 ABI 与反汇编）：
    /// - 原生物理 land 0..10；可见 UI 0..9（physical 10=MtOlympus Top → UI9）。
    ///   扩展岛 physical 11 → UI10，原 0..9 与 10→9 一律不动。
    /// - <c>UILand.UpdateLand(reign, land)</c> 的 land 同时驱动船标（reign.currentLand==land）
    ///   与全部动态/静态图标（landData[land]）；登记实例参数 10 → 11。
    /// - <c>Confirm(int)</c> 两阶段：state 0（ShowingWorld）只切单岛视图；
    ///   state 1（ShowingSingleIsland）满足 userCanSelectLand && focusedLand==index &&
    ///   Game.currentLand!=focusedLand 才写 landResult 并关菜单；其余 state 由原生抛异常。
    /// - <c>LAND_TO_MAP_LAND</c> 是每实例 Dictionary&lt;physical,ui&gt;：0→0…9→9、10→9；
    ///   扩展只 append 11→UI。
    /// </summary>
    internal enum ExtensionConfirmAction
    {
        /// <summary>非登记分支：完全交还原生（0..9 或未知 owners）。</summary>
        PassThrough,
        /// <summary>登记 UI10 分支：执行后跳过原生，但本次不产生出航结果。</summary>
        Consumed,
        /// <summary>登记 UI10 分支：按原生第二阶段写 landResult=11 并关菜单。</summary>
        Succeed
    }

    internal static class ExtensionIslandMapPlan
    {
        /// <summary>可见 UI 索引（详情 lands 与总览两套数组的第 11 项）。</summary>
        internal const int UiIndex = 10;
        /// <summary>物理 land 索引（config/存档/资源/出航结果）。</summary>
        internal const int PhysicalIndex = 11;
        /// <summary>原生可见岛数量（原生物理 0..10 中的 UI 0..9）。</summary>
        internal const int NativeUiCount = 10;
        /// <summary>无扩展 UI 时的降级焦点：原生 Top 的 UI9（绝不用 10，避免越界）。</summary>
        internal const int FallbackFocusUi = NativeUiCount - 1;

        /// <summary>
        /// 详情模板选择：优先 Oracle（普通外岛美术，原 UI0）；无则退 God；再退第一个
        /// 非 MtOlympus/Quest 的普通模板。绝不选神祇任务/奥林匹斯内容。找不到返回 -1（不追加）。
        /// </summary>
        internal static int SelectDetailTemplateIndex(string[] names)
        {
            if (names == null || names.Length == 0) return -1;
            for (int i = 0; i < names.Length; i++)
            {
                if (Contains(names[i], "Oracle")) return i;
            }
            for (int i = 0; i < names.Length; i++)
            {
                if (Contains(names[i], "God")) return i;
            }
            for (int i = 0; i < names.Length; i++)
            {
                if (!LooksBossOrQuest(names[i])) return i;
            }
            return -1;
        }

        /// <summary>Boss/任务专属模板判定（仅用于避免把神祇任务/奥林匹斯美术当普通外岛模板）。</summary>
        internal static bool LooksBossOrQuest(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return Contains(name, "MtOlympus")
                || Contains(name, "Olympus")
                || Contains(name, "Serpent")
                || Contains(name, "Quest")
                || Contains(name, "_Top");
        }

        /// <summary>
        /// 克隆净化目标：MapMountIcons / MapCustomIconAssets 运行期自建节点。
        /// 懒建路径从 live 详情实例克隆时会带走这些节点与其 Oracle-only UIMapIcon，
        /// 必须在重建图标缓存前 deactivate + detach + destroy（Deferred 销毁期间不能
        /// 再被 GetComponentsInChildren 拾取）。
        /// </summary>
        internal static bool IsSelfGeneratedNode(string name)
            => name == "KEM_MapResourceIcons" || name == "KEM_MapIconSources";

        private static bool Contains(string value, string token)
            => !string.IsNullOrEmpty(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>物理 11 在该实例字典中的可见焦点：有扩展 UI 时 10，否则降级 9（不越界）。</summary>
        internal static int LookupUiForPhysical(bool extensionUiPresent)
            => extensionUiPresent ? UiIndex : FallbackFocusUi;

        /// <summary>原生 Loading/刷新形状：详情、总览 UILand、总览 UIMainMapLand 必须都是 10。</summary>
        internal static bool HasNativeShape(int detailCount, int overviewLandCount, int overviewButtonCount)
            => detailCount == NativeUiCount
            && overviewLandCount == NativeUiCount
            && overviewButtonCount == NativeUiCount;

        /// <summary>追加后的形状：三套都恰好 11（exact 配对）。</summary>
        internal static bool HasExtendedShape(int detailCount, int overviewLandCount, int overviewButtonCount)
            => detailCount == NativeUiCount + 1
            && overviewLandCount == NativeUiCount + 1
            && overviewButtonCount == NativeUiCount + 1;

        /// <summary>登记实例的 UpdateLand 参数重映射：仅 UI10 → physical11。</summary>
        internal static bool ShouldRemapUpdateLand(bool registered, int uiLand, out int physical)
        {
            physical = uiLand;
            if (!registered || uiLand != UiIndex) return false;
            physical = PhysicalIndex;
            return true;
        }

        /// <summary>阶段 0 的确认按钮可用性（原生口径 + 旅行门）：userCanSelectLand && 当前岛≠11 && 可旅行。</summary>
        internal static bool Stage0ConfirmInteractable(bool userCanSelectLand, bool canTravel, int currentLand)
            => userCanSelectLand && canTravel && currentLand != PhysicalIndex;

        /// <summary>
        /// Confirm 分支决策（两阶段纯函数）。
        /// <paramref name="ownMenu"/> = 本次 Confirm 来自本 worker 登记过的 Greek 菜单实例；
        /// <paramref name="ownerUsable"/> = 登记仍有效（未撤销 / 实例与数组与 campaign 与当前
        /// menu._mainMap 一致）。原生 Greek 不存在 UI10，因此“本菜单 + UI10”必须由本层裁决：
        /// 失效登记 → 可靠拒绝（consume、无 result），绝不回落 native 10（会出航 Top）。
        /// 非本菜单或非 UI10 → 原样交还原生（不拦其他 owner/未知 context）。
        /// </summary>
        internal static ExtensionConfirmAction DecideConfirm(
            bool ownMenu, bool ownerUsable, int state, bool userCanSelectLand, bool canTravel,
            int focusedLand, int requestedIndex, int currentLand)
        {
            if (!ownMenu || requestedIndex != UiIndex) return ExtensionConfirmAction.PassThrough;
            if (!ownerUsable) return ExtensionConfirmAction.Consumed;
            if (state == 0) return ExtensionConfirmAction.Consumed;
            if (state != 1) return ExtensionConfirmAction.PassThrough;   // 未知 state 不当成功
            if (!canTravel) return ExtensionConfirmAction.Consumed;
            if (!userCanSelectLand) return ExtensionConfirmAction.Consumed;         // 纯浏览绝不出航
            if (focusedLand != requestedIndex) return ExtensionConfirmAction.Consumed;
            if (currentLand == PhysicalIndex) return ExtensionConfirmAction.Consumed; // 当前 11 不可确认本岛
            return ExtensionConfirmAction.Succeed;
        }
    }
}
