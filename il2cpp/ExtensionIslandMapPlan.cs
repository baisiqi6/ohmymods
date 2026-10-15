using System;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 扩展岛地图层纯决策（可离线逐值复算，不依赖 Unity/IL2CPP）。
    ///
    /// 事实依据（native-travel / native-lifecycle / native-map-contract 实际 ABI 与反汇编）：
    /// - 原生物理 land 0..10；可见 UI 0..9（physical 10=MtOlympus Top → UI9）。
    ///   扩展岛物理 11（A）→ UI10、物理 13（B）→ UI11；原 0..9 与 10→9 一律不动。
    /// - <c>UILand.UpdateLand(reign, land)</c> 的 land 同时驱动船标（reign.currentLand==land）
    ///   与全部动态/静态图标（landData[land]）；登记实例参数按其 tuple 重映射 10→11 / 11→13。
    /// - <c>Confirm(int)</c> 两阶段：state 0（ShowingWorld）只切单岛视图；
    ///   state 1（ShowingSingleIsland）满足 userCanSelectLand && focusedLand==index &&
    ///   Game.currentLand!=目标 land 才写 landResult 并关菜单；其余 state 由原生抛异常。
    /// - <c>LAND_TO_MAP_LAND</c> 是每实例 Dictionary&lt;physical,ui&gt;：0→0…9→9、10→9；
    ///   扩展只 append 11→UI10、13→UI11。B 不可用只隐藏节点/禁 travel，dict 13→11 仍存在
    ///   （读未知禁 travel，绝不回落 native Top 焦点）。
    ///
    /// 岛号/UI 映射的真实来源是 <see cref="MountIslandSplitPolicy"/>（直接 Compile link，不复制常量语义）。
    /// </summary>
    internal enum ExtensionConfirmAction
    {
        /// <summary>非登记分支：完全交还原生（0..9 或未知 owners）。</summary>
        PassThrough,
        /// <summary>登记分支：执行后跳过原生，但本次不产生出航结果。</summary>
        Consumed,
        /// <summary>登记分支：按原生第二阶段写 landResult=目标 physical 并关菜单。</summary>
        Succeed
    }

    /// <summary>两套总览数组整笔替换的结果：非 Complete 一律整笔回滚（含第一数组已写、第二数组失败）。</summary>
    internal enum ExtensionArrayReplaceOutcome
    {
        Complete,
        Rollback
    }

    /// <summary>单 tuple 处置：Proceed=保持可用；LockHidden=登记保留但隐藏/禁 travel；Revoked=仅真外部失效。</summary>
    internal enum ExtensionSlotDisposition
    {
        Proceed,
        LockHidden,
        Revoked
    }

    internal static class ExtensionIslandMapPlan
    {
        /// <summary>原生可见岛数量（原生物理 0..10 中的 UI 0..9）。</summary>
        internal const int NativeUiCount = 10;
        /// <summary>扩展岛 tuple 数（A=11/UI10、B=13/UI11）。</summary>
        internal const int SlotCount = 2;
        /// <summary>登记完成后的三套数组固定长度：native 10 + 2 = 12。</summary>
        internal const int ExtendedUiCount = NativeUiCount + 2;
        /// <summary>无扩展 UI 时的降级焦点：原生 Top 的 UI9（绝不用 10/11，避免越界）。</summary>
        internal const int FallbackFocusUi = NativeUiCount - 1;

        /// <summary>A 岛 UI 索引（10）。</summary>
        internal const int PrimaryUi = MountIslandSplitPolicy.PrimaryUi;
        /// <summary>A 岛物理索引（11）。</summary>
        internal const int PrimaryPhysical = MountIslandSplitPolicy.PrimaryLand;
        /// <summary>B 岛 UI 索引（11）。</summary>
        internal const int SecondaryUi = MountIslandSplitPolicy.SecondaryUi;
        /// <summary>B 岛物理索引（13）。</summary>
        internal const int SecondaryPhysical = MountIslandSplitPolicy.SecondaryLand;

        /// <summary>扩展 UI 索引 → 目标物理岛（仅 10→11、11→13）。</summary>
        internal static bool TryGetSlotPhysical(int uiIndex, out int physical)
            => MountIslandSplitPolicy.TryGetPhysicalIndex(uiIndex, out physical);

        /// <summary>扩展物理岛 → UI 索引（仅 11→10、13→11）。</summary>
        internal static bool TryGetSlotUi(int physical, out int uiIndex)
            => MountIslandSplitPolicy.TryGetMapIndex(physical, out uiIndex);

        /// <summary>该 UI 索引是否属于扩展岛（仅 10/11）。</summary>
        internal static bool IsExtensionUi(int uiIndex)
            => MountIslandSplitPolicy.TryGetPhysicalIndex(uiIndex, out _);

        /// <summary>原生 UI 槽（0..9，含 native 10→UI9 的焦点路径）——扩展逻辑不得接管。</summary>
        internal static bool IsNativeUiSlot(int uiIndex) => uiIndex >= 0 && uiIndex < NativeUiCount;

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

        /// <summary>
        /// 扩展物理岛在该实例字典中的可见焦点：tuple UI 存在时 = 该 tuple UI（B 不可用只隐藏节点，
        /// 字典 13→11 仍保留）；UI 真的未登记时才降级 UI9。非扩展 physical 返回 -1 —— 调用方
        /// 不得写入本表未拥有的键（native 0..10 原样交还原生）。
        /// </summary>
        internal static int LookupUiForPhysical(int physical, bool slotUiPresent)
        {
            if (!TryGetSlotUi(physical, out int ui)) return -1;
            return slotUiPresent ? ui : FallbackFocusUi;
        }

        /// <summary>原生 Loading/刷新形状：详情、总览 UILand、总览 UIMainMapLand 必须都是 10。</summary>
        internal static bool HasNativeShape(int detailCount, int overviewLandCount, int overviewButtonCount)
            => detailCount == NativeUiCount
            && overviewLandCount == NativeUiCount
            && overviewButtonCount == NativeUiCount;

        /// <summary>追加后的形状：三套都恰好 12（native 10 + A/B 两 tuple，exact 配对，前 10 不动）。</summary>
        internal static bool HasExtendedShape(int detailCount, int overviewLandCount, int overviewButtonCount)
            => detailCount == ExtendedUiCount
            && overviewLandCount == ExtendedUiCount
            && overviewButtonCount == ExtendedUiCount;

        /// <summary>
        /// 登记实例的 UpdateLand 参数重映射：registered 为该 UILand 实例的 exact tuple UI
        /// （未登记即 -1，实例身份由 <c>ExtensionIslandMap.TryGetPhysicalLandIndex</c> 桥判，本函数不猜实例）。
        /// 仅把 exact 登记的扩展 UI 值映射到其物理岛；数值相同但非 exact 实例绝不获权限。
        /// </summary>
        internal static bool ShouldRemapUpdateLand(int registeredUi, int uiLand, out int physical)
        {
            physical = uiLand;
            if (registeredUi < 0 || uiLand != registeredUi) return false;
            if (!TryGetSlotPhysical(uiLand, out int mapped)) return false;
            physical = mapped;
            return true;
        }

        /// <summary>阶段 0 的确认按钮可用性（原生口径 + 旅行门）：userCanSelectLand && 当前岛≠该 tuple 的物理岛 && 可旅行。</summary>
        internal static bool Stage0ConfirmInteractable(bool userCanSelectLand, bool canTravel,
            int currentLand, int targetPhysical)
            => userCanSelectLand && canTravel && currentLand != targetPhysical;

        /// <summary>
        /// Confirm 分支决策（两阶段纯函数，按 requestedIndex 所属 tuple 裁决）。
        /// <paramref name="ownMenu"/> = 本次 Confirm 来自本 worker 登记过的 Greek 菜单实例；
        /// <paramref name="ownerUsable"/> = 登记仍有效（未撤销 / 实例与数组与 campaign 与当前
        /// menu._mainMap 一致）。原生 Greek 不存在 UI10/UI11，因此“本菜单 + 扩展 UI”必须由本层裁决：
        /// 失效登记 → 可靠拒绝（consume、无 result），绝不回落 native Top。
        /// 非本菜单或非扩展 UI → 原样交还原生（不拦其他 owner/未知 context）。
        /// </summary>
        internal static ExtensionConfirmAction DecideConfirm(
            bool ownMenu, bool ownerUsable, int state, bool userCanSelectLand, bool canTravel,
            int focusedLand, int requestedIndex, int currentLand)
        {
            if (!ownMenu || !TryGetSlotPhysical(requestedIndex, out int slotPhysical))
                return ExtensionConfirmAction.PassThrough;
            if (!ownerUsable) return ExtensionConfirmAction.Consumed;
            if (state == 0) return ExtensionConfirmAction.Consumed;
            if (state != 1) return ExtensionConfirmAction.PassThrough;   // 未知 state 不当成功
            if (!canTravel) return ExtensionConfirmAction.Consumed;
            if (!userCanSelectLand) return ExtensionConfirmAction.Consumed;         // 纯浏览绝不出航
            if (focusedLand != requestedIndex) return ExtensionConfirmAction.Consumed;
            if (currentLand == slotPhysical) return ExtensionConfirmAction.Consumed; // 当前已在该岛不可确认
            return ExtensionConfirmAction.Succeed;
        }

        /// <summary>阶段 1 成功写回：landResult 必须是 requested UI 对应 tuple 的物理岛；非扩展 UI 返回 false。</summary>
        internal static bool TryGetConfirmResultLand(int requestedIndex, out int landResultPhysical)
            => TryGetSlotPhysical(requestedIndex, out landResultPhysical);

        /// <summary>
        /// 旅行可用性：快照读失败（unknown）或该 tuple 槽不可用一律禁 travel —— 读未知绝不放行，
        /// 也绝不改路由到 native Top；campaign 门关闭同样禁行。
        /// </summary>
        internal static bool ResolveCanTravel(bool snapshotKnown, bool slotAvailable, bool campaignGateOpen)
            => snapshotKnown && slotAvailable && campaignGateOpen;

        /// <summary>
        /// 操作权限：必须 exact 实例 + 同一 menu + campaign 一致 + 槽位完好全部成立；
        /// 同数值 index 的 foreign 实例（exact=false）永不获得权限。
        /// </summary>
        internal static bool HasAuthority(bool exactSlotInstance, bool sameMenu, bool campaignSame, bool slotsIntact)
            => exactSlotInstance && sameMenu && campaignSame && slotsIntact;

        /// <summary>单 tuple 的处置：授权/可用性未知关闭只能是 LockHidden，绝不 Revoke（保 12 槽与兄弟）。</summary>
        internal static ExtensionSlotDisposition DecideSlotDisposition(bool registrationIntact, bool campaignIntact,
            bool slotsIntact, bool scopeOk, bool authorized)
        {
            if (!registrationIntact || !campaignIntact || !slotsIntact) return ExtensionSlotDisposition.Revoked;
            if (!scopeOk) return ExtensionSlotDisposition.Revoked;
            return authorized ? ExtensionSlotDisposition.Proceed : ExtensionSlotDisposition.LockHidden;
        }

        /// <summary>撤销登记 → tombstone：仅拦该菜单的扩展 UI 请求（consume/无 result）。</summary>
        internal static bool ShouldConsumeTombstone(bool ownMenu, bool tombstoneForThisMenu, int requestedIndex)
            => ownMenu && tombstoneForThisMenu && IsExtensionUi(requestedIndex);

        /// <summary>
        /// 两套总览数组整笔替换：两数组写后读回都 exact 才算 Complete；
        /// 第一数组已替换而第二数组未确认 → 整笔回滚（含恢复第一数组原引用）。
        /// </summary>
        internal static ExtensionArrayReplaceOutcome DecideArrayReplace(bool landsConfirmed, bool buttonsConfirmed)
            => landsConfirmed && buttonsConfirmed
                ? ExtensionArrayReplaceOutcome.Complete
                : ExtensionArrayReplaceOutcome.Rollback;

    }
}
