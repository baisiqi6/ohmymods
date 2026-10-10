using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 扩展希腊岛的实例登记／UI-native 编号／Confirm 导航（travel-worker，issue-200 双岛）。
    ///
    /// 边界（native-travel CONTRACT + integration-contract + review-lifecycle-followup + 双岛契约）：
    /// - 两不可变 tuple：A physical11/UI10、B physical13/UI11（来源 MountIslandSplitPolicy，不复制数值）。
    /// - 只操作当前 Greek <see cref="MapTimelineMenuGreece"/> 实例：追加详情 lands[10]/lands[11]、
    ///   mainMap._lands[10..11]、_mainMapLands[10..11]（exact 配对），并 append 实例字典 11→10、13→11。
    ///   共享 CampaignData/模板数组/资产一律不写；原 0..9 与 10→UI9 完全不动。
    /// - 登记有效性 = 该 tuple 实例活着 + 三套数组槽位（固定 12，前 10 exact 引用）+ **当前 menu._mainMap 身份**
    ///   + 当前 campaign；任一失效即对该 tuple 撤销（隐藏/销毁自有节点、解除数组槽位与 UI selection、
    ///   字典降级 9），并移入 tombstone（按 menu+UI 拒绝身份）直到原生 ClearLands/菜单销毁真清理。
    /// - 每 tuple 持有独立资约 token（<see cref="TryGetVisualOwnerTokenForLand"/>，MapMountIcons 的
    ///   shore/detail 租约按 land 绑定与归还）；menu 代次另有聚合 token 供旧 caller 兼容。
    ///   per-land 释放只 ReleaseOwner(该 tuple token)，绝不误释放兄弟岛。
    /// - B 不可用（未宣布/未就绪/未访问）只隐藏该 slot 节点并禁 travel；数组槽位与字典 13→11 仍存在，
    ///   读取未知一律禁 travel，绝不回落 native Top。
    /// - UILand.UpdateLand 前缀只对登记实例把其 UI 值（10/11）映射到 tuple physical：船标与全部
    ///   动态/静态图标都按 physical 读取；旧历史 reign 缺 landData 时原生按越界即缺失处理。
    /// - Confirm 只替换登记 tuple 的两阶段分支；旅行门 = 该 land 的当前普通 Greek owner +
    ///   IsAvailableForCurrentCampaign(land) && EnsureReady(land) + 可用性快照（fail-closed）。
    /// - 克隆（模板路径与懒建路径一致）先净化运行期自建节点（KEM_MapResourceIcons/
    ///   KEM_MapIconSources：inactive+detach+Destroy）再重建静态图标缓存。
    /// </summary>
    internal static class ExtensionIslandMap
    {
        private const string LogPrefix = "[ExtensionIslandMap] ";
        private const int MaxTombstones = 8;

        /// <summary>单个扩展岛 tuple 的登记（A=11/UI10、B=13/UI11 各一份）。</summary>
        private sealed class Registration
        {
            internal MapTimelineMenuGreece Menu;
            internal UIMainMap Map;
            internal UILand Detail;
            internal UILand Overview;
            internal UIMainMapLand OverviewButton;
            internal GameObject DetailGo;
            internal GameObject OverviewGo;
            internal IntPtr CampaignPointer;
            internal int UiIndex;
            internal int PhysicalLand;
            /// <summary>visible/travel 授权（B 未证时为 false；节点隐藏但槽位/字典保留）。</summary>
            internal bool Available;
            /// <summary>该 tuple 的 world/总览 shore 资约 token（每次成功登记 new 实例 = 新代际）。</summary>
            internal object WorldToken;
            /// <summary>该 tuple 的 detail shore 资约 token（与 world 分离：detail 切换只归还 detail）。</summary>
            internal object DetailToken;
            internal bool Revoked;
            /// <summary>撤销原因仅为授权/就绪失效（上下文未变），允许后续合法重建成新登记。</summary>
            internal bool RevokeRecoverable;
            internal string RevokedReason;
        }

        /// <summary>当前有效登记（两个 tuple 槽，索引 0=A、1=B；同一 menu 代次）。</summary>
        private static readonly Registration[] Slots = new Registration[ExtensionIslandMapPlan.SlotCount];
        /// <summary>menu 代次聚合 token（旧 caller 兼容；每次成功登记 new 实例）。</summary>
        private static object _menuToken;

        /// <summary>登记代次的最初引用（整代 cleanup 只按它归还原生 10，不依赖 Slots[] 当前是否非 null）。</summary>
        private sealed class Generation
        {
            internal MapTimelineMenuGreece Menu;
            internal UIMainMap Map;   // capture 的 exact map 实例（不靠 menu._mainMap 新 getter 借权）
            internal Il2CppReferenceArray<UILand> OriginalLands;
            internal Il2CppReferenceArray<UIMainMapLand> OriginalButtons;
            internal UILand[] OriginalListPrefix;
            internal readonly Registration[] Owned = new Registration[ExtensionIslandMapPlan.SlotCount];
        }

        private static Generation _generation;

        /// <summary>字典回滚读回未知/失败 → 该菜单代次 poisoned：不 dispatch、不 append、不 travel，
        /// 等 Native Clear/menu lifecycle 有界归还；无自动 retry 扫描。</summary>
        private static MapTimelineMenuGreece _poisonedMenu;

        private static bool IsPoisoned(MapTimelineMenuGreece menu)
            => menu != null && _poisonedMenu != null && SameMenu(_poisonedMenu, menu);
        /// <summary>已撤销登记的拒绝身份（按 menu+UI 区分；有界，随真清理/菜单销毁回收）。</summary>
        private static readonly List<Registration> Tombstones = new List<Registration>(4);
        /// <summary>LoadLands 参数里捕获的干净详情模板（懒建优先复用，避免继承 live 旧资源）。</summary>
        private static MapTimelineMenuGreece _detailTemplateMenu;
        private static GameObject _detailTemplatePrefab;
        private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

        // ------------------------------------------------------------------ Harmony 入口

        /// <summary>LoadLands postfix：当前实例追加第 11/12 份详情模板与两扩展总览簇。</summary>
        internal static void OnLandsLoaded(MapTimelineMenuGreece menu, Il2CppReferenceArray<GameObject> landPrefabs, GameObject mainMap)
        {
            try { EnsureAppended(menu, landPrefabs, true); }
            catch (Exception e) { LogError("load-lands failed: " + e.GetType().Name + " " + e.Message); }
        }

        /// <summary>UpdateLands prefix：懒建/修复/按失效状态撤销（同一幂等例程收敛）。</summary>
        internal static void BeforeLandsUpdated(MapTimelineMenuGreece menu)
        {
            try { EnsureAppended(menu, null, false); }
            catch (Exception e) { LogError("ensure failed: " + e.GetType().Name + " " + e.Message); }
        }

        /// <summary>ClearLands postfix：原生已销毁该实例全部自有对象，执行真清理。</summary>
        internal static void OnLandsCleared(MapTimelineMenuGreece menu)
        {
            try
            {
                if (menu == null) return;
                if (AnySlotMenu(menu))
                {
                    FixLookup(menu, false, false);
                    ClearOwner(menu, "native-clear");
                }
                if (SameMenu(_detailTemplateMenu, menu))
                {
                    _detailTemplateMenu = null;
                    _detailTemplatePrefab = null;
                }
                if (IsPoisoned(menu)) _poisonedMenu = null;   // 真清理后解除 poisoned
                if (_generation != null && SameMenu(_generation.Menu, menu)) _generation = null;
                RemoveTombstone(menu);
                PruneTombstones();
            }
            catch (Exception e) { LogError("clear-lands failed: " + e.GetType().Name + " " + e.Message); }
        }

        /// <summary>UILand.UpdateLand 前缀：登记实例的扩展 UI 值（10/11）→ 该 tuple 的 physical。</summary>
        internal static bool TryRemapUpdateLand(UILand land, int uiLand, out int physical)
        {
            physical = uiLand;
            int registeredUi = -1;
            try { TryGetRegisteredUiIndex(land, out registeredUi); }
            catch (Exception) { registeredUi = -1; }
            if (!ExtensionIslandMapPlan.ShouldRemapUpdateLand(registeredUi, uiLand, out int mapped)) return false;
            physical = mapped;
            return physical != uiLand;
        }

        /// <summary>Confirm 前缀判定体：返回 true 表示本前缀已接管（调用方必须跳过原生）。</summary>
        internal static bool TryHandleConfirm(MapTimelineMenuGreece menu, int landIndex)
        {
            bool ownMenu = false;
            try
            {
                if (menu == null || !ExtensionIslandMapPlan.IsExtensionUi(landIndex)) return false;
                if (IsPoisoned(menu)) return true;   // poisoned：consume、无 result、不 dispatch
                if (!IsActiveMenu(menu)) return true;   // 旧 queued sender/foreign menu：consume，不 Confirm

                Registration slot = FindSlotByUi(menu, landIndex);
                if (slot != null)
                {
                    ownMenu = true;
                    if (!Usable(slot))
                    {
                        RevokeSlot(slot, "unusable");
                        return true;   // 可靠拒绝：consume、无 result、绝不回落 native Top
                    }
                    if (!slot.Available)
                    {
                        // 不可用 tuple（如 B 未证/未访问）被键盘聚焦：consume 且**不进入** Stage0 单岛空详情；
                        // 槽位与字典键保留（hidden 登记），兄弟岛不受影响。
                        return true;
                    }

                    int currentLand = CurrentPhysicalLand(out bool known);
                    bool canTravel = ExtensionIslandMapPlan.ResolveCanTravel(
                        known, slot.Available,
                        IsAvailableForCurrentCampaign(slot.PhysicalLand) && EnsureReady(slot.PhysicalLand));

                    int state = (int)menu._openWorldMapState;
                    bool userCanSelectLand = false;
                    try { userCanSelectLand = menu.currentOptions.userCanSelectLand; } catch (Exception) { }
                    int focused = 0;
                    try { focused = menu.focusedLand; } catch (Exception) { }

                    ExtensionConfirmAction action = ExtensionIslandMapPlan.DecideConfirm(
                        true, true, state, userCanSelectLand, canTravel, focused, landIndex, currentLand);
                    if (action == ExtensionConfirmAction.PassThrough) return false;

                    if (state == (int)MapTimelineMenuGreece.OpenWorldMapState.ShowingWorld)
                    {
                        // 原生阶段 0 顺序：focusedLand → selectSound → 进入单岛 → leftButton → confirmButton。
                        menu.focusedLand = landIndex;
                        AudioEmitter sound = menu.selectSound;
                        if (sound != null) sound.Play(-1);
                        menu._openWorldMapState = MapTimelineMenuGreece.OpenWorldMapState.ShowingSingleIsland;
                        Button left = menu.leftButton;
                        if (left != null) left.interactable = true;
                        Button confirm = menu.confirmButton;
                        if (confirm != null)
                        {
                            confirm.interactable = ExtensionIslandMapPlan.Stage0ConfirmInteractable(
                                userCanSelectLand, canTravel, currentLand, slot.PhysicalLand);
                        }
                        return true;
                    }

                    if (action != ExtensionConfirmAction.Succeed) return true;

                    // 原生阶段 2 成功：结果写该 tuple 的 physical（11/13）；音效/菜单字段与原生一致。
                    if (!ExtensionIslandMapPlan.TryGetConfirmResultLand(landIndex, out int resultLand)) return true;
                    menu.landResult = resultLand;
                    AudioSource audio = menu.newIslandAudioSource;
                    if (audio != null && !audio.isPlaying) audio.Play();
                    Menu menuInst = Menu.InstExists ? Menu.Inst : null;
                    if (menuInst != null) menuInst.menuBackoutAllowed = true;
                    Menu hide = menuInst != null ? menuInst : menu._menu;
                    if (hide != null) hide.Hide();
                    return true;
                }

                // 已撤销登记：仅拦该菜单实例的扩展 UI（consume/无 result），绝不全局拦其他 context。
                if (FindTombstone(menu, landIndex) != null) return true;
                return false;
            }
            catch (Exception e)
            {
                LogError("confirm failed: " + e.GetType().Name + " " + e.Message);
                // 已确认是本登记菜单的扩展 UI 时 fail-closed：绝不把扩展 index 交回原生（会出航错岛）。
                return ownMenu;
            }
        }

        // ------------------------------------------------------------------ 查询接口（map-worker / MapMountIcons 使用）

        /// <summary>
        /// menu 代次聚合 token（只读薄查询，旧 caller 兼容）：任一 tuple 成功登记时 new 实例 = 新代际；
        /// 全部撤销/清理后为 null。新代码应按 land 取 <see cref="TryGetVisualOwnerTokenForLand"/>，
        /// 保证 per-land 释放不误伤兄弟岛。
        /// </summary>
        internal static object TryGetVisualOwnerToken()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Usable(Slots[i])) return _menuToken;
            }
            return null;
        }

        /// <summary>该 land 的 world/总览独立 tuple token（只读薄查询）：未登记/不可用返回 false 且 token=null。</summary>
        internal static bool TryGetVisualOwnerTokenForLand(int physicalLand, out object token)
        {
            token = null;
            Registration slot = FindSlotByPhysical(physicalLand);
            if (!Usable(slot)) return false;
            token = slot.WorldToken;
            return token != null;
        }

        /// <summary>
        /// 该 land 的 detail 专用 token（与 world token 分离）：detail 形状切换/归还只作用于本 token，
        /// 绝不连带释放该岛总览 shore 的租约（world 背景保持 applied）。
        /// </summary>
        internal static bool TryGetDetailTokenForLand(int physicalLand, out object token)
        {
            token = null;
            Registration slot = FindSlotByPhysical(physicalLand);
            if (!Usable(slot)) return false;
            token = slot.DetailToken;
            return token != null;
        }

        /// <summary>该 menu 是否当前登记的 exact menu（native pointer 身份；只读）——菜单级收尾门。</summary>
        internal static bool IsVisualOwnerMenu(MapTimelineMenuGreece menu)
        {
            if (menu == null) return false;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Usable(Slots[i]) && SameMenu(Slots[i].Menu, menu)) return true;
            }
            return false;
        }

        /// <summary>该 menu 的该 land tuple 是否当前登记且可用（per-land 收尾/绑定的 exact sender 门）。</summary>
        internal static bool IsVisualOwnerMenuForLand(MapTimelineMenuGreece menu, int physicalLand)
        {
            Registration slot = FindSlotByPhysical(physicalLand);
            return Usable(slot) && menu != null && SameMenu(slot.Menu, menu);
        }

        /// <summary>
        /// 仅当前登记的扩展 UILand（详情/总览两 tuple 的 exact 实例）返回 true / 对应 physical。
        /// 严格 owner：实例活着 + 当前 menu._mainMap 身份 + 数组槽位 + campaign 一致；未撤销。
        /// 非登记实例即使 index 数值相同也返回 false。
        /// </summary>
        internal static bool TryGetPhysicalLandIndex(UILand land, out int index)
        {
            index = 0;
            try
            {
                if (land == null) return false;
                for (int i = 0; i < Slots.Length; i++)
                {
                    Registration slot = Slots[i];
                    if (slot == null) continue;
                    if (!SameInstance(land, slot.Detail) && !SameInstance(land, slot.Overview)) continue;
                    if (slot.Menu == null) { RevokeSlot(slot, "dead-menu"); return false; }
                    if (!SlotsIntact(slot)) { RevokeSlot(slot, "invalid"); return false; }
                    if (!CampaignIntact(slot)) { RevokeSlot(slot, "campaign"); return false; }
                    index = slot.PhysicalLand;
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>该 exact 实例的 tuple UI 值（10/11）；未登记返回 false。</summary>
        internal static bool TryGetRegisteredUiIndex(UILand land, out int uiIndex)
        {
            uiIndex = -1;
            if (!TryGetPhysicalLandIndex(land, out int physical)) return false;
            return ExtensionIslandMapPlan.TryGetSlotUi(physical, out uiIndex);
        }

        /// <summary>
        /// 派发桥（exact capture 有效性）：token 必须是该 tuple 当前登记的 World/Detail token
        /// （撤销时已置 null → 立即失效），且 capture 的 menu/map/land 与当前登记一致、ActiveMap 仍是该 menu、
        /// tuple 仍 Available/usable。绝不按 physical 找“新 slot”借权限，也不看 aggregate 状态。
        /// </summary>
        internal static bool TryGetLiveTuple(object token, UILand land, MapTimelineMenuGreece menu, UIMainMap map,
            out int physicalLand)
        {
            physicalLand = -1;
            if (token == null || land == null || menu == null || map == null) return false;
            try
            {
                if (!IsActiveMenu(menu)) return false;
                for (int i = 0; i < Slots.Length; i++)
                {
                    Registration slot = Slots[i];
                    if (slot == null || !ReferenceEquals(Slots[i], slot) || slot.Revoked) continue;
                    if (!ReferenceEquals(slot.WorldToken, token) && !ReferenceEquals(slot.DetailToken, token)) continue;
                    // 全部以 capture 实例比对（slot.Map = 登记时捕获的 map；map = 回调捕获的 map），
                    // 不读 menu._mainMap 新 getter 借权。
                    if (!SameMenu(slot.Menu, menu) || !SameInstance(slot.Map, map)) continue;
                    if (!SameInstance(land, slot.Detail) && !SameInstance(land, slot.Overview)) continue;
                    if (!slot.Available) continue;
                    if (!Usable(slot)) continue;
                    physicalLand = slot.PhysicalLand;
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>ActiveMap 是否仍是该 exact menu（旧 queued sender/foreign menu 不得 dispatch）。</summary>
        internal static bool IsActiveMenu(MapTimelineMenuGreece menu)
        {
            if (menu == null) return false;
            try
            {
                Menu inst = Menu.InstExists ? Menu.Inst : null;
                MapTimelineMenu active = inst != null ? inst.ActiveMap : null;
                return SameMenu(active as MapTimelineMenuGreece, menu);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>该 land 的 slot 是否可用（visible/travel）；未登记返回 false。</summary>
        internal static bool IsSlotAvailable(int physicalLand)
        {
            Registration slot = FindSlotByPhysical(physicalLand);
            return Usable(slot) && slot.Available;
        }

        /// <summary>点击派发门（详情回调 / 总览按钮 listener）：撤销后自有回调必须 no-op。</summary>
        internal static bool IsDispatchLive(MapTimelineMenuGreece menu)
        {
            try
            {
                if (menu == null) return false;
                for (int i = 0; i < Slots.Length; i++)
                {
                    Registration slot = Slots[i];
                    if (slot == null || !SameMenu(slot.Menu, menu)) continue;
                    if (SlotsIntact(slot) && CampaignIntact(slot)) return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>per-land 派发门：该 tuple 的节点回调只在 still-mine 时生效。</summary>
        internal static bool IsDispatchLiveForLand(MapTimelineMenuGreece menu, int physicalLand)
        {
            Registration slot = FindSlotByPhysical(physicalLand);
            if (slot == null || menu == null || !SameMenu(slot.Menu, menu)) return false;
            try { return SlotsIntact(slot) && CampaignIntact(slot); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ 追加/撤销/清理

        private static void EnsureAppended(MapTimelineMenuGreece menu, Il2CppReferenceArray<GameObject> landPrefabs, bool fromLoad)
        {
            if (menu == null) return;
            if (IsPoisoned(menu)) return;   // poisoned 代次：不重建/不 append（等 Native Clear/menu lifecycle）
            PruneTombstones();

            if (AnySlotMenu(menu))
            {
                if (!ExtensionIslandRuntime.TryScope(out _))
                {
                    RevokeAll(menu, "scope-lost");
                    return;
                }
                for (int i = 0; i < Slots.Length; i++)
                {
                    Registration slot = Slots[i];
                    if (slot == null || slot.Revoked || !SameMenu(slot.Menu, menu)) continue;
                    bool slotsIntact = false;
                    bool campaignIntact = false;
                    try { slotsIntact = SlotsIntact(slot); } catch (Exception) { }
                    try { campaignIntact = CampaignIntact(slot); } catch (Exception) { }
                    bool authorized = AuthorizeLand(slot.PhysicalLand);
                    ExtensionSlotDisposition disposition = ExtensionIslandMapPlan.DecideSlotDisposition(
                        true, campaignIntact, slotsIntact, true, authorized);
                    if (disposition == ExtensionSlotDisposition.Revoked)
                    {
                        RevokeSlot(slot, slotsIntact ? "authorization-lost" : "stale");
                        continue;
                    }
                    if (disposition == ExtensionSlotDisposition.LockHidden)
                    {
                        // 授权/可用性未知关闭只更新该 tuple 的 available/visible/travel；登记整体保留，不缩 12 槽。
                        slot.Available = false;
                        ApplySlotVisibility(slot);
                        continue;
                    }
                    slot.Available = true;
                    ApplySlotVisibility(slot);
                }
                RefreshAvailability(menu);
                // 幂等：只校正字典值与可见性；被撤 tuple 的键按注册存在性交给降级 CAS（不指回已销毁 UI）。
                FixLookup(menu, SlotPresentForLookup(ExtensionIslandMapPlan.PrimaryPhysical),
                    SlotPresentForLookup(ExtensionIslandMapPlan.SecondaryPhysical));
                return;   // 本轮不重建：重建留待后续合法生命周期
            }

            // 登记属于另一个菜单实例（Unity Destroy 可能延迟到帧末）：先降级为 tombstone，再允许本实例登记。
            if (AnySlot()) RevokeAllAny("menu-replaced");

            Registration tomb = FindAnyTombstone(menu);
            if (tomb != null && !tomb.RevokeRecoverable) return;   // 该菜单上下文已失效：保持拒绝，等真清理

            // LoadLands 参数入口先保留干净详情模板引用：懒追加也优先从该 prefab 创建。
            GameObject loadedTemplate = fromLoad ? CaptureDetailTemplate(menu, landPrefabs) : null;

            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return;   // 非当前普通 Greek owner
            // aggregate 登记只要求**任一** tuple 可用且 ready（两岛恢复独立：A 未创建/不可用不得阻止 B-only）。
            bool primaryReady = IsAvailableForCurrentCampaign(ExtensionIslandMapPlan.PrimaryPhysical)
                && EnsureReady(ExtensionIslandMapPlan.PrimaryPhysical);
            bool secondaryReady = IsAvailableForCurrentCampaign(ExtensionIslandMapPlan.SecondaryPhysical)
                && EnsureReady(ExtensionIslandMapPlan.SecondaryPhysical);
            if (!primaryReady && !secondaryReady)
            {
                LogOnce("no-extension-authorized", "no extension tuple available/ready; extension island UI stays closed");
                // 无 ownership 时**不写** keys 11/13：只对仍持有 owned 期望映射的遗留键做 CAS 降级
                //（foreign/未知一律不动，绝不旁路 WriteLookup 的 foreign 保护）。
                try { FixLookup(menu, false, false); } catch (Exception) { }
                return;
            }

            Il2CppSystem.Collections.Generic.List<UILand> lands = menu.lands;
            UIMainMap map = menu._mainMap;
            if (lands == null || map == null) return;
            Il2CppReferenceArray<UILand> originalLands = map._lands;
            Il2CppReferenceArray<UIMainMapLand> originalButtons = map._mainMapLands;
            if (originalLands == null || originalButtons == null) return;
            if (!ExtensionIslandMapPlan.HasNativeShape(lands.Count, originalLands.Length, originalButtons.Length))
            {
                LogOnce("shape", "unexpected native shape lands=" + lands.Count + " mapLands=" + originalLands.Length
                    + " mapButtons=" + originalButtons.Length + "; extension UI not appended");
                return;
            }

            GameObject detailSource = loadedTemplate ?? ResolveDetailSource(menu);
            UILand overviewSource = originalLands[0];
            if (detailSource == null || overviewSource == null || overviewSource.gameObject == null || map.gameObject == null)
            {
                LogError("extension island source missing; UI not appended");
                return;
            }

            // 代次最初引用（整代 cleanup 归还用；不依赖 Slots[] 值）。
            var generation = new Generation
            {
                Menu = menu,
                Map = map,
                OriginalLands = originalLands,
                OriginalButtons = originalButtons,
                OriginalListPrefix = new UILand[ExtensionIslandMapPlan.NativeUiCount],
            };
            for (int i = 0; i < ExtensionIslandMapPlan.NativeUiCount && i < lands.Count; i++)
            {
                generation.OriginalListPrefix[i] = lands[i];
            }

            var created = new List<Registration>(ExtensionIslandMapPlan.SlotCount);
            // append ledger：**每次 lands.Add 后即时记录 exact ui/detail**（不依赖 overview 完成后的 created 列表）；
            // finally 按实际写入倒序 CAS 回滚，覆盖 A-detail-added/A-overview-fail、B 同理等路径。
            var appendLedger = new List<Registration>(ExtensionIslandMapPlan.SlotCount);
            GameObject[] detailGos = new GameObject[ExtensionIslandMapPlan.SlotCount];
            GameObject[] overviewGos = new GameObject[ExtensionIslandMapPlan.SlotCount];
            bool appended = false;
            bool arraysReplaced = false;
            bool success = false;
            Il2CppSystem.Collections.Generic.Dictionary<int, int> lookupDict = null;
            List<MountIslandMapDictEntry> lookupPriors = null;
            try
            {
                for (int s = 0; s < ExtensionIslandMapPlan.SlotCount; s++)
                {
                    int ui = s == 0 ? ExtensionIslandMapPlan.PrimaryUi : ExtensionIslandMapPlan.SecondaryUi;
                    int physical = s == 0 ? ExtensionIslandMapPlan.PrimaryPhysical : ExtensionIslandMapPlan.SecondaryPhysical;

                    // 1) 详情：与原生同路径（模板 → GetAssetSwap → _landsHolder 下实例化），随后净化 + 重建静态图标缓存。
                    bool prefabSource = SameInstance(detailSource, loadedTemplate) || SameInstance(detailSource, _detailTemplatePrefab);
                    GameObject detailPrefab = detailSource;
                    if (prefabSource)
                    {
                        try
                        {
                            GameObject swapped = BiomeData.GetAssetSwap<GameObject>(detailSource);
                            if (swapped != null) detailPrefab = swapped;
                        }
                        catch (Exception e) { LogOnce("swap", "detail template asset swap failed: " + e.Message); }
                    }
                    GameObject detailGo = UnityEngine.Object.Instantiate(detailPrefab, menu._landsHolder, false);
                    if (detailGo == null) return;
                    detailGos[s] = detailGo;
                    try { detailGo.transform.localScale = Vector3.one; } catch (Exception) { }
                    try { detailGo.name = s == 0 ? "Map_Land_Extension_Greece" : "Map_Land_ExtensionB_Greece"; } catch (Exception) { }
                    SanitizeClone(detailGo);
                    UILand detail = detailGo.GetComponent<UILand>();
                    if (detail == null) return;
                    RebuildStaticIconCache(detailGo, detail);
                    // 不可导航的 UI 不登记（回滚）：回调捕获 immutable Registration（含 detail token）。
                    var pending = new Registration
                    {
                        Menu = menu,
                        Map = map,
                        Detail = detail,
                        UiIndex = ui,
                        PhysicalLand = physical,
                        DetailToken = new object(),
                        WorldToken = new object(),
                    };
                    if (!AttachDetailCallback(pending)) return;   // 不可导航的 UI 不登记（回滚）
                    bool detailFocused = false;
                    try { detailFocused = menu.focusedLand == ui; } catch (Exception) { }
                    detailGo.SetActive(detailFocused);
                    lands.Add(detail);
                    appended = true;
                    appendLedger.Add(pending);   // 即时记账（此后任一失败都必须回滚这一项）
                    if (lands.Count != ExtensionIslandMapPlan.NativeUiCount + s + 1
                        || !SameInstance(lands[ui], detail))
                    {
                        return;   // finally 回滚
                    }

                    // 2) 总览簇：实例克隆 UI0（正常外岛美术）+ 同一净化 + 事件整体替换（回调捕获 immutable ui）。
                    GameObject overviewGo = UnityEngine.Object.Instantiate(overviewSource.gameObject, map.transform, false);
                    if (overviewGo == null) return;
                    overviewGos[s] = overviewGo;
                    try { overviewGo.name = s == 0 ? "Main_Map_Land_Extension_Greece" : "Main_Map_Land_ExtensionB_Greece"; } catch (Exception) { }
                    SanitizeClone(overviewGo);
                    UILand overview = overviewGo.GetComponent<UILand>();
                    UIMainMapLand overviewButton = overviewGo.GetComponent<UIMainMapLand>();
                    if (overview == null || overviewButton == null) return;
                    RebuildStaticIconCache(overviewGo, overview);
                    try { overviewButton._lockedByQuest = QuestType.None; }
                    catch (Exception e) { LogOnce("lock", "set extension lock failed: " + e.Message); }
                    pending.Overview = overview;   // 先赋 exact 引用再接线：回调捕获的 land 必须非 null
                    pending.OverviewButton = overviewButton;
                    if (!WireOverviewButton(pending, overviewButton, overviewGo)) return;
                    try { overviewGo.SetActive(true); } catch (Exception) { }

                    pending.DetailGo = detailGo;
                    pending.OverviewGo = overviewGo;
                    pending.CampaignPointer = campaign != null ? campaign.Pointer : IntPtr.Zero;
                    pending.Available = s == 0 ? primaryReady : secondaryReady;
                    created.Add(pending);
                }

                // 3) 三套数组同步扩到 12（exact 配对；前 10 exact 引用不动）。
                var newLands = new Il2CppReferenceArray<UILand>(ExtensionIslandMapPlan.ExtendedUiCount);
                var newButtons = new Il2CppReferenceArray<UIMainMapLand>(ExtensionIslandMapPlan.ExtendedUiCount);
                for (int i = 0; i < ExtensionIslandMapPlan.NativeUiCount; i++)
                {
                    newLands[i] = originalLands[i];
                    newButtons[i] = originalButtons[i];
                }
                for (int s = 0; s < created.Count; s++)
                {
                    newLands[created[s].UiIndex] = created[s].Overview;
                    newButtons[created[s].UiIndex] = created[s].OverviewButton;
                }
                map._lands = newLands;
                arraysReplaced = true;
                map._mainMapLands = newButtons;

                Il2CppReferenceArray<UILand> checkLands = map._lands;
                Il2CppReferenceArray<UIMainMapLand> checkButtons = map._mainMapLands;
                bool arraysOk = checkLands != null && checkButtons != null
                    && ExtensionIslandMapPlan.HasExtendedShape(lands.Count, checkLands.Length, checkButtons.Length);
                for (int s = 0; s < created.Count && arraysOk; s++)
                {
                    arraysOk = SameInstance(checkLands[created[s].UiIndex], created[s].Overview)
                        && SameInstance(checkButtons[created[s].UiIndex], created[s].OverviewButton);
                }
                if (!arraysOk) return;   // finally 回滚

                // 4) 字典 11→10、13→11（只 append；原生 0..10 值不动）——先捕获 prior（存在/值），
                //    任一步失败按 CAS 恢复/移除我们写入的键（绝不覆盖 foreign/未知值）。
                if (!CaptureLookupPriors(menu, out lookupDict, out lookupPriors))
                {
                    return;   // 字典读不到：整笔回滚（不抛、不建半套 UI）
                }
                if (!FixLookup(menu, true, true)) return;   // finally 回滚（含字典 CAS 恢复）

                for (int s = 0; s < created.Count; s++)
                {
                    Registration slot = created[s];
                    int slotIndex = SlotIndexOf(slot.PhysicalLand);
                    if (slotIndex < 0) return;   // 不可能：tuple 常量来自 policy
                    Slots[slotIndex] = slot;
                    generation.Owned[slotIndex] = slot;
                    ApplySlotVisibility(slot);
                }
                _generation = generation;
                _menuToken = new object();
                RemoveTombstone(menu);
                success = true;
                Log("extension island UI appended: detail lands[10,11], overview _lands/_mainMapLands[10,11], lookup 11->10/13->11; available=A:"
                    + (Slots[0] != null && Slots[0].Available ? "yes" : "no")
                    + " B:" + (Slots[1] != null && Slots[1].Available ? "yes" : "no"));
            }
            finally
            {
                if (!success)
                {
                    if (arraysReplaced)
                    {
                        try { map._lands = originalLands; } catch (Exception) { }
                        try { map._mainMapLands = originalButtons; } catch (Exception) { }
                    }
                    if (appended)
                    {
                        // 未 publish 的整笔事务回滚：按 created **倒序**逐项精确 CAS 移除（先 UI11/B 后 UI10/A，
                        // 位移只影响更低位置）；未添加的一项不 rm；槽位被 foreign 替换时不删且 poison 该代次。
                        try
                        {
                            Il2CppSystem.Collections.Generic.List<UILand> liveLands = menu.lands;
                            for (int ci = appendLedger.Count - 1; ci >= 0; ci--)
                            {
                                Registration added = appendLedger[ci];
                                if (added == null || added.Detail == null) continue;
                                if (liveLands == null || added.UiIndex < ExtensionIslandMapPlan.NativeUiCount
                                    || added.UiIndex >= liveLands.Count) continue;
                                if (SameInstance(liveLands[added.UiIndex], added.Detail))
                                {
                                    liveLands.RemoveAt(added.UiIndex);
                                }
                                else if (liveLands[added.UiIndex] != null)
                                {
                                    // foreign 替换：不删他人元素，保留责任并 poison（等 Native Clear/lifecycle）。
                                    if (MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true)) _poisonedMenu = menu;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            if (MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true)) _poisonedMenu = menu;
                        }
                    }
                    RestoreLookupPriors(menu, lookupDict, lookupPriors);
                    for (int s = 0; s < ExtensionIslandMapPlan.SlotCount; s++)
                    {
                        DestroySafe(overviewGos[s]);
                        DestroySafe(detailGos[s]);
                    }
                }
            }
        }

        /// <summary>
        /// 撤销单个 tuple：隐藏/销毁自有节点、解除该 tuple 的数组槽位与 UI selection、字典降级 9；
        /// 登记移入 tombstone（拒绝身份）。只碰“当前 map 就是登记 map 且槽位仍是我们”的数组；
        /// 兄弟 tuple 的槽位/字典键/租约不动（它的 hidden slot 继续存在）。
        /// </summary>
        private static void RevokeSlot(Registration slot, string reason)
        {
            if (slot == null || slot.Revoked) return;
            bool slotsOk = false;
            bool campaignOk = false;
            try { slotsOk = SlotsIntact(slot); } catch (Exception) { }
            try { campaignOk = CampaignIntact(slot); } catch (Exception) { }
            slot.Revoked = true;
            slot.RevokeRecoverable = slotsOk && campaignOk;   // 仅授权/就绪失效时可重建
            slot.RevokedReason = reason;
            LogOnce("revoked-" + reason + "-" + slot.PhysicalLand, "extension island registration revoked (land "
                + slot.PhysicalLand + ", " + reason + ", recoverable=" + (slot.RevokeRecoverable ? "yes" : "no")
                + "); extra UI dismantled");

            MapTimelineMenuGreece menu = slot.Menu;
            UIMainMap map = slot.Map;

            // 先撤本 tuple 的 token（任何已捕获回调/租约立即失去 authority，不授旁人），再动节点/回调。
            slot.WorldToken = null;
            slot.DetailToken = null;

            try { if (slot.DetailGo != null) slot.DetailGo.SetActive(false); } catch (Exception) { }
            try { if (slot.OverviewGo != null) slot.OverviewGo.SetActive(false); } catch (Exception) { }
            try { if (menu != null) FixLookup(menu, SlotPresentForLookup(ExtensionIslandMapPlan.PrimaryPhysical), SlotPresentForLookup(ExtensionIslandMapPlan.SecondaryPhysical)); } catch (Exception) { }

            // 单 tuple 撤销**绝不缩** menu.lands 或三数组（兄弟 exact/旅行必须继续有效）；
            // 只 CAS 把自己槽位置 null（仅当该槽仍是我们），长度保持 12，等整代 cleanup 才归还原生 10。
            try
            {
                Il2CppSystem.Collections.Generic.List<UILand> lands = menu != null ? menu.lands : null;
                if (lands != null && slot.UiIndex >= 0 && slot.UiIndex < lands.Count
                    && SameInstance(lands[slot.UiIndex], slot.Detail))
                {
                    lands[slot.UiIndex] = null;
                }
            }
            catch (Exception e) { LogOnce("revoke-detail", "revoke detail slot CAS failed: " + e.Message); }

            try
            {
                if (map != null && SameInstance(menu != null ? menu._mainMap : null, map))
                {
                    Il2CppReferenceArray<UILand> mapLands = map._lands;
                    if (mapLands != null && slot.UiIndex >= 0 && slot.UiIndex < mapLands.Length
                        && SameInstance(mapLands[slot.UiIndex], slot.Overview))
                    {
                        mapLands[slot.UiIndex] = null;
                    }
                    Il2CppReferenceArray<UIMainMapLand> mapButtons = map._mainMapLands;
                    if (mapButtons != null && slot.UiIndex >= 0 && slot.UiIndex < mapButtons.Length
                        && SameInstance(mapButtons[slot.UiIndex], slot.OverviewButton))
                    {
                        mapButtons[slot.UiIndex] = null;
                    }
                }
            }
            catch (Exception e) { LogOnce("revoke-arrays", "revoke overview slot CAS failed: " + e.Message); }

            // UI selection：若当前选中对象是我们的自有节点，显式解除。
            try
            {
                if (Menu.InstExists)
                {
                    Menu inst = Menu.Inst;
                    GameObject selected = inst != null ? inst.CurrentSelectedGameObject : null;
                    if (selected != null
                        && (SameInstance(selected, slot.OverviewGo) || SameInstance(selected, slot.DetailGo)))
                    {
                        inst.CurrentSelectedGameObject = null;
                    }
                }
            }
            catch (Exception e) { LogOnce("revoke-selection", "revoke selection clear failed: " + e.Message); }

            DestroySafe(slot.OverviewGo);
            DestroySafe(slot.DetailGo);

            int index = SlotIndexOf(slot.PhysicalLand);
            if (index >= 0 && ReferenceEquals(Slots[index], slot)) Slots[index] = null;
            Tombstones.Add(slot);
            PruneTombstones();
            TryReclaimGeneration(menu);
        }

        /// <summary>撤销该 menu 的全部登记（menu 换代/授权失效/清理）。</summary>
        private static void RevokeAll(MapTimelineMenuGreece menu, string reason)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Registration slot = Slots[i];
                if (slot != null && !slot.Revoked && (menu == null || SameMenu(slot.Menu, menu)))
                {
                    RevokeSlot(slot, reason);
                }
            }
            if (!AnySlot()) _menuToken = null;
        }

        private static void RevokeAllAny(string reason)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] != null) RevokeSlot(Slots[i], reason);
            }
            if (!AnySlot()) _menuToken = null;
        }

        /// <summary>
        /// 整代归还：仅当本代两个登记槽都已撤销（或从未登记），且当前数组仍是我们的 12 槽/两位已 CAS 清空时，
        /// 用**保存的最初引用**归还原生 10（不依赖 Slots[] 当前值，也不依赖任何单岛可用性开关）。
        /// </summary>
        private static void TryReclaimGeneration(MapTimelineMenuGreece menu)
        {
            try
            {
                Generation generation = _generation;
                if (generation == null || menu == null || !SameMenu(generation.Menu, menu)) return;
                for (int i = 0; i < generation.Owned.Length; i++)
                {
                    Registration owned = generation.Owned[i];
                    if (owned != null && !owned.Revoked) return;   // 还有活 tuple：保持 12
                }
                // 只有当前 menu._mainMap 仍是 capture 的**同一实例**才允许写回原数组；
                // 否则（外部替换/新 map）只清旧 owned 节点/责任，绝不把旧 generations 的 arrays 写进新 map。
                UIMainMap currentMap = null;
                try { currentMap = menu._mainMap; } catch (Exception) { currentMap = null; }
                if (currentMap == null || generation.Map == null || !SameInstance(currentMap, generation.Map))
                {
                    LogOnce("reclaim-map-drifted-" + (generation.Menu != null ? generation.Menu.GetInstanceID() : 0),
                        "generation reclaim skipped: current map is not the captured map (old nodes cleared, arrays untouched)");
                    _generation = null;
                    return;
                }
                UIMainMap map = generation.Map;
                Il2CppReferenceArray<UILand> mapLands = map._lands;
                Il2CppReferenceArray<UIMainMapLand> mapButtons = map._mainMapLands;
                if (mapLands == null || mapButtons == null) return;
                bool slotsClear = mapLands.Length == ExtensionIslandMapPlan.ExtendedUiCount
                    && mapButtons.Length == ExtensionIslandMapPlan.ExtendedUiCount;
                for (int i = 0; slotsClear && i < generation.Owned.Length; i++)
                {
                    Registration owned = generation.Owned[i];
                    if (owned == null) continue;
                    slotsClear = mapLands[owned.UiIndex] == null && mapButtons[owned.UiIndex] == null;
                }
                if (!slotsClear) return;
                if (generation.OriginalLands != null && generation.OriginalButtons != null)
                {
                    map._lands = generation.OriginalLands;       // 最初 native 10 段（exact 引用）
                    map._mainMapLands = generation.OriginalButtons;
                }
                Il2CppSystem.Collections.Generic.List<UILand> lands = menu.lands;
                if (lands != null && generation.OriginalListPrefix != null)
                {
                    while (lands.Count > ExtensionIslandMapPlan.NativeUiCount)
                    {
                        int last = lands.Count - 1;
                        // 只回收我们 CAS 清空/仍属本代的尾部槽；遇到外来对象立即停止（不丢兄弟/外部替换）
                        if (lands[last] != null) break;
                        lands.RemoveAt(last);
                    }
                    for (int i = 0; i < generation.OriginalListPrefix.Length && i < lands.Count; i++)
                    {
                        if (generation.OriginalListPrefix[i] != null
                            && !SameInstance(lands[i], generation.OriginalListPrefix[i]))
                        {
                            break;   // 前缀被外部替换：不强行改写
                        }
                    }
                }
                _generation = null;
                LogOnce("generation-reclaimed", "extension island generation reclaimed to native 10 (all tuples down)");
            }
            catch (Exception e)
            {
                LogOnce("reclaim-" + e.GetType().Name, "generation reclaim failed: " + e.Message);
            }
        }

        private static void ClearOwner(MapTimelineMenuGreece menu, string reason)
        {
            bool cleared = false;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] != null && (menu == null || SameMenu(Slots[i].Menu, menu)))
                {
                    Slots[i] = null;
                    cleared = true;
                }
            }
            if (cleared)
            {
                if (!AnySlot()) _menuToken = null;
                LogOnce("cleared-" + reason, "extension island map registration dropped (" + reason + ")");
            }
        }

        private static Registration FindSlotByUi(MapTimelineMenuGreece menu, int uiIndex)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Registration slot = Slots[i];
                if (slot == null) continue;
                if (slot.UiIndex != uiIndex) continue;
                if (menu == null || SameMenu(slot.Menu, menu)) return slot;
            }
            return null;
        }

        private static Registration FindSlotByPhysical(int physicalLand)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Registration slot = Slots[i];
                if (slot != null && slot.PhysicalLand == physicalLand) return slot;
            }
            return null;
        }

        private static int SlotIndexOf(int physicalLand)
        {
            if (physicalLand == ExtensionIslandMapPlan.PrimaryPhysical) return 0;
            if (physicalLand == ExtensionIslandMapPlan.SecondaryPhysical) return 1;
            return -1;
        }

        private static Registration FindTombstone(MapTimelineMenuGreece menu, int uiIndex)
        {
            if (menu == null) return null;
            for (int i = 0; i < Tombstones.Count; i++)
            {
                Registration keeper = Tombstones[i];
                if (keeper == null) { Tombstones.RemoveAt(i--); continue; }
                if (keeper.Menu == null) { Tombstones.RemoveAt(i--); continue; }
                if (keeper.UiIndex == uiIndex && SameMenu(keeper.Menu, menu)) return keeper;
            }
            return null;
        }

        private static Registration FindAnyTombstone(MapTimelineMenuGreece menu)
        {
            if (menu == null) return null;
            for (int i = 0; i < Tombstones.Count; i++)
            {
                Registration keeper = Tombstones[i];
                if (keeper == null) { Tombstones.RemoveAt(i--); continue; }
                if (keeper.Menu == null) { Tombstones.RemoveAt(i--); continue; }
                if (SameMenu(keeper.Menu, menu)) return keeper;
            }
            return null;
        }

        private static void RemoveTombstone(MapTimelineMenuGreece menu)
        {
            if (menu == null) return;
            for (int i = 0; i < Tombstones.Count; i++)
            {
                Registration keeper = Tombstones[i];
                if (keeper == null || keeper.Menu == null || SameMenu(keeper.Menu, menu))
                {
                    Tombstones.RemoveAt(i--);
                }
            }
        }

        private static void PruneTombstones()
        {
            for (int i = 0; i < Tombstones.Count; i++)
            {
                Registration keeper = Tombstones[i];
                if (keeper == null || keeper.Menu == null) Tombstones.RemoveAt(i--);
            }
            while (Tombstones.Count > MaxTombstones) Tombstones.RemoveAt(0);
        }

        /// <summary>LoadLands 参数入口：选定并保留干净详情模板（懒追加复用；绝不改资产本身）。</summary>
        private static GameObject CaptureDetailTemplate(MapTimelineMenuGreece menu, Il2CppReferenceArray<GameObject> landPrefabs)
        {
            if (landPrefabs == null || landPrefabs.Length == 0) return null;
            var names = new string[landPrefabs.Length];
            for (int i = 0; i < landPrefabs.Length; i++)
            {
                try { names[i] = landPrefabs[i] != null ? landPrefabs[i].name : null; } catch (Exception) { names[i] = null; }
            }
            int index = ExtensionIslandMapPlan.SelectDetailTemplateIndex(names);
            if (index < 0 || index >= landPrefabs.Length) return null;
            _detailTemplateMenu = menu;
            _detailTemplatePrefab = landPrefabs[index];
            return landPrefabs[index];
        }

        /// <summary>懒建来源：优先本实例 LoadLands 捕获的干净模板，其次 live 详情实例（克隆后净化）。</summary>
        private static GameObject ResolveDetailSource(MapTimelineMenuGreece menu)
        {
            if (_detailTemplatePrefab != null && SameMenu(_detailTemplateMenu, menu)) return _detailTemplatePrefab;
            Il2CppSystem.Collections.Generic.List<UILand> lands = menu != null ? menu.lands : null;
            if (lands != null && lands.Count > 0)
            {
                UILand first = lands[0];
                if (first != null) return first.gameObject;
            }
            return null;
        }

        /// <summary>详情回调：闭包捕获 immutable tuple（ui/physical），派发门按 tuple 校验（撤销/换代 no-op）。</summary>
        private static bool AttachDetailCallback(Registration slot)
        {
            try
            {
                MapTimelineMenuGreece menu = slot.Menu;
                UILand detail = slot.Detail;
                UIMainMap map = slot.Map;
                int uiIndex = slot.UiIndex;
                object detailToken = slot.DetailToken;
                Il2CppSystem.Action<int> callback = (Il2CppSystem.Action<int>)(System.Action<int>)
                    (index =>
                    {
                        // immutable capture：token/menu/map/land 全 exact，ActiveMap 与 tuple 可用性由桥复检；
                        // 旧 queued sender / foreign map / 已换代 token 一律 no-op（不按 physical 借新 slot）。
                        if (index == uiIndex
                            && TryGetLiveTuple(detailToken, detail, menu, map, out _))
                        {
                            menu.OnButtonLand(index);
                        }
                    });
                detail.SetupLandCallback(callback, uiIndex);
                return true;
            }
            catch (Exception e)
            {
                LogError("detail callback attach failed: " + e.GetType().Name + " " + e.Message);
                return false;   // 不可导航的 UI 不登记（调用方回滚整笔追加）
            }
        }

        /// <summary>
        /// 总览按钮事件整体替换：克隆带来的持久回调（旧岛号）与旧运行时 listener 全部丢弃，
        /// 只保留唯一回调 → 当前 mainMap.OnButtonSelectLand(tuple UI)（撤销/换代后 no-op）。
        /// </summary>
        private static bool WireOverviewButton(Registration slot, UIMainMapLand host, GameObject go)
        {
            try
            {
                MapTimelineMenuGreece menu = slot.Menu;
                UIMainMap map = slot.Map;
                UILand overview = slot.Overview;
                int uiIndex = slot.UiIndex;
                object worldToken = slot.WorldToken;
                Button button = host._button;
                if (button == null) button = go.GetComponentInChildren<Button>();
                if (button == null)
                {
                    LogError("overview button missing; extension UI not appended (ui=" + uiIndex + ")");
                    return false;
                }
                var fresh = new Button.ButtonClickedEvent();
                button.onClick = fresh;
                UnityAction action = (UnityAction)(System.Action)(() =>
                {
                    try
                    {
                        // immutable capture + exact bridge：旧 sender/已换代 token/foreign map 一律 no-op。
                        if (TryGetLiveTuple(worldToken, overview, menu, map, out _))
                        {
                            map.OnButtonSelectLand(uiIndex);
                        }
                    }
                    catch (Exception e) { LogError("select land failed: " + e.Message); }
                });
                fresh.AddListener(action);
                return true;
            }
            catch (Exception e)
            {
                LogError("overview button wiring failed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 克隆净化：立即 inactive + detach + Destroy 运行期自建节点（KEM_MapResourceIcons /
        /// KEM_MapIconSources），避免 Deferred 销毁期间仍被 GetComponentsInChildren 拾取。
        /// </summary>
        private static void SanitizeClone(GameObject clone)
        {
            if (clone == null) return;
            try
            {
                var doomed = new List<GameObject>(4);
                var queue = new Queue<Transform>(16);
                queue.Enqueue(clone.transform);
                int visited = 0;
                while (queue.Count > 0 && visited < 256 && doomed.Count < 16)
                {
                    Transform cursor = queue.Dequeue();
                    visited++;
                    int count = cursor.childCount;
                    for (int i = 0; i < count; i++)
                    {
                        Transform child = cursor.GetChild(i);
                        if (child == null) continue;
                        if (ExtensionIslandMapPlan.IsSelfGeneratedNode(child.name))
                        {
                            doomed.Add(child.gameObject);
                            continue;
                        }
                        queue.Enqueue(child);
                    }
                }
                for (int i = 0; i < doomed.Count; i++)
                {
                    GameObject go = doomed[i];
                    try { go.SetActive(false); } catch (Exception) { }
                    try { go.transform.SetParent(null, false); } catch (Exception) { }
                    try { UnityEngine.Object.Destroy(go); } catch (Exception) { }
                }
                if (doomed.Count > 0)
                {
                    LogOnce("sanitize", "clone sanitized: removed " + doomed.Count + " self-generated node(s)");
                }
            }
            catch (Exception e)
            {
                LogOnce("sanitize-fail", "clone sanitize failed: " + e.GetType().Name + " " + e.Message);
            }
        }

        /// <summary>
        /// 重建静态图标缓存：在已净化的克隆树 + 隐藏 Bespoke 后重新 GetComponentsInChildren，
        /// 覆盖 Awake 时可能拾取自建节点/专属资源的缓存；通用（船/灯塔/侧栏状态）保持。
        /// </summary>
        private static void RebuildStaticIconCache(GameObject root, UILand land)
        {
            Transform bespoke = FindBespokeRoot(root);
            if (bespoke != null)
            {
                try { bespoke.gameObject.SetActive(false); } catch (Exception) { }
            }
            try
            {
                Il2CppArrayBase<UIMapIcon> found = root.GetComponentsInChildren<UIMapIcon>();
                var kept = new List<UIMapIcon>(found != null ? found.Length : 0);
                if (found != null)
                {
                    for (int i = 0; i < found.Length; i++)
                    {
                        UIMapIcon icon = found[i];
                        if (icon == null) continue;
                        if (bespoke != null && IsUnder(icon.transform, bespoke)) continue;
                        kept.Add(icon);
                    }
                }
                var filtered = new Il2CppReferenceArray<UIMapIcon>(kept.Count);
                for (int i = 0; i < kept.Count; i++) filtered[i] = kept[i];
                land._mapIcons = filtered;
            }
            catch (Exception e)
            {
                LogOnce("icons", "static icon cache rebuild failed: " + e.GetType().Name + " " + e.Message);
            }
        }

        private static Transform FindBespokeRoot(GameObject root)
        {
            if (root == null) return null;
            try
            {
                Transform direct = root.transform.Find("Map Icons/Bespoke");
                if (direct != null) return direct;
            }
            catch (Exception) { }
            try
            {
                // 有界 BFS：找不到标准路径时按名字兜底（深度/数量上限，绝不全场扫描）。
                var queue = new Queue<Transform>(8);
                queue.Enqueue(root.transform);
                int visited = 0;
                while (queue.Count > 0 && visited < 64)
                {
                    Transform cursor = queue.Dequeue();
                    visited++;
                    int count = cursor.childCount;
                    for (int i = 0; i < count && i < 32; i++)
                    {
                        Transform child = cursor.GetChild(i);
                        if (child == null) continue;
                        if (child.name == "Bespoke") return child;
                        if (visited + queue.Count < 64) queue.Enqueue(child);
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        private static bool IsUnder(Transform node, Transform root)
        {
            Transform cursor = node;
            for (int depth = 0; cursor != null && depth < 32; depth++, cursor = cursor.parent)
            {
                if (cursor == root) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 字典 / 生命周期

        /// <summary>
        /// 把该实例字典的 tuple 键校正为当前 UI 存在性（A 有 UI→10、B 有 UI→11；无 UI→9 降级焦点）。
        /// 返回 true 表示全部已写键读回一致（追加事务要求）；无字典时返回 false。
        /// </summary>
        private static bool FixLookup(MapTimelineMenuGreece menu, bool primaryUiPresent, bool secondaryUiPresent)
        {
            try
            {
                if (menu == null) return false;
                Il2CppSystem.Collections.Generic.Dictionary<int, int> dict = menu.LAND_TO_MAP_LAND;
                if (dict == null) return false;
                if (!WriteLookup(dict, ExtensionIslandMapPlan.PrimaryPhysical, primaryUiPresent)) return false;
                return WriteLookup(dict, ExtensionIslandMapPlan.SecondaryPhysical, secondaryUiPresent);
            }
            catch (Exception e)
            {
                LogOnce("lookup", "LAND_TO_MAP_LAND update failed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

        private static bool WriteLookup(Il2CppSystem.Collections.Generic.Dictionary<int, int> dict,
            int physicalLand, bool uiPresent)
        {
            int value = ExtensionIslandMapPlan.LookupUiForPhysical(physicalLand, uiPresent);
            if (value < 0) return false;   // 非扩展键：绝不写（native 0..10/12 交还原生）
            bool present = dict.ContainsKey(physicalLand);
            if (!uiPresent)
            {
                if (!present) return true;   // 无需降级写入（从未登记过）
                // 降级只清**我们 owned 的期望映射**（11→10 / 13→11）；foreign/未知值绝不覆盖。
                int expected = ExtensionIslandMapPlan.LookupUiForPhysical(physicalLand, true);
                int current = dict[physicalLand];
                if (!MountIslandMapDictTransaction.OwnsExpectedValue(current, expected)) return true;
                dict[physicalLand] = value;
                return dict[physicalLand] == value;
            }
            if (present)
            {
                int expected = ExtensionIslandMapPlan.LookupUiForPhysical(physicalLand, true);
                int current = dict[physicalLand];
                if (current != value && !MountIslandMapDictTransaction.OwnsExpectedValue(current, expected))
                {
                    // 已有 11/13 键不是我们的 owned 期望映射（foreign/未知状态）：不可接管，拒绝 append。
                    LogOnce("lookup-foreign-" + physicalLand, "LAND_TO_MAP_LAND key " + physicalLand +
                        " holds non-owned value " + current + "; extension append refused (no takeover)");
                    return false;
                }
            }
            dict[physicalLand] = value;
            return dict[physicalLand] == value;   // 读回校验
        }


        /// <summary>按当次读值刷新两 tuple 的 visible/travel 授权与节点显隐（幂等；不改数组/字典键）。</summary>
        private static void RefreshAvailability(MapTimelineMenuGreece menu)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Registration slot = Slots[i];
                if (slot == null || slot.Revoked) continue;
                slot.Available = ReadAvailability(slot.PhysicalLand);
                ApplySlotVisibility(slot);
            }
        }

        private static bool ReadAvailability(int physicalLand)
            => IsAvailableForCurrentCampaign(physicalLand);

        /// <summary>该 tuple 是否仍被授权（可用 + 就绪）；未知/读故障一律 false（不创建、不回落 native Top）。</summary>
        private static bool AuthorizeLand(int physicalLand)
            => IsAvailableForCurrentCampaign(physicalLand) && EnsureReady(physicalLand);

        /// <summary>追加事务前捕获两扩展键的 prior（存在/值）；读失败返回 false（调用方整笔回滚）。</summary>
        private static bool CaptureLookupPriors(MapTimelineMenuGreece menu,
            out Il2CppSystem.Collections.Generic.Dictionary<int, int> dict,
            out List<MountIslandMapDictEntry> priors)
        {
            dict = null;
            priors = null;
            try
            {
                if (menu == null) return false;
                dict = menu.LAND_TO_MAP_LAND;
                if (dict == null) return false;
                priors = new List<MountIslandMapDictEntry>(ExtensionIslandMapPlan.SlotCount);
                int[] lands = { ExtensionIslandMapPlan.PrimaryPhysical, ExtensionIslandMapPlan.SecondaryPhysical };
                int[] uis = { ExtensionIslandMapPlan.PrimaryUi, ExtensionIslandMapPlan.SecondaryUi };
                for (int i = 0; i < lands.Length; i++)
                {
                    bool had = dict.ContainsKey(lands[i]);
                    int prior = had ? dict[lands[i]] : 0;
                    priors.Add(MountIslandMapDictTransaction.Capture(lands[i], had, prior, uis[i]));
                }
                return true;
            }
            catch (Exception e)
            {
                LogOnce("lookup-capture", "LAND_TO_MAP_LAND capture failed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

        /// <summary>整笔回滚：对我们写入的键按 CAS 恢复 prior / 移除新建键（foreign/未知值绝不覆盖）。</summary>
        private static void RestoreLookupPriors(MapTimelineMenuGreece menu,
            Il2CppSystem.Collections.Generic.Dictionary<int, int> dict, List<MountIslandMapDictEntry> priors)
        {
            if (dict == null || priors == null) return;
            for (int i = 0; i < priors.Count; i++)
            {
                MountIslandMapDictEntry entry = priors[i];
                try
                {
                    bool present = dict.ContainsKey(entry.Physical);
                    int current = present ? dict[entry.Physical] : 0;
                    if (MountIslandMapDictTransaction.ShouldRestoreRollback(entry, present, current))
                    {
                        dict[entry.Physical] = entry.PriorValue;
                    }
                    else if (MountIslandMapDictTransaction.ShouldRemoveRollback(entry, present, current))
                    {
                        dict.Remove(entry.Physical);
                    }
                }
                catch (Exception e)
                {
                    // 回滚读回未知/失败：该代次 poisoned（不 dispatch/不 append/不 travel），
                    // 由 Native Clear/menu lifecycle 有界归还；不做自动 retry 扫描。
                    if (MountIslandMapDictTransaction.RequiresPoisonOnRollbackFailure(true)) _poisonedMenu = menu;
                    LogOnce("lookup-rollback", "LAND_TO_MAP_LAND rollback failed: " + e.GetType().Name + " " + e.Message);
                }
            }
        }

        private static void ApplySlotVisibility(Registration slot)
        {
            if (slot == null) return;
            bool overviewVisible = !slot.Revoked && slot.Available;
            bool detailVisible = overviewVisible;
            try { detailVisible = detailVisible && slot.Menu != null && slot.Menu.focusedLand == slot.UiIndex; }
            catch (Exception) { }
            try { if (slot.OverviewGo != null) slot.OverviewGo.SetActive(overviewVisible); } catch (Exception) { }
            try { if (slot.DetailGo != null) slot.DetailGo.SetActive(detailVisible); } catch (Exception) { }
        }

        private static bool IsAvailableForCurrentCampaign(int land)
        {
            try { return ExtensionIslandRuntime.IsAvailableForCurrentCampaign(land); }
            catch (Exception) { return false; }
        }

        private static bool EnsureReady(int land)
        {
            try { return ExtensionIslandRuntime.EnsureReady(land); }
            catch (Exception) { return false; }
        }

        private static bool AnySlot()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i] != null) return true;
            }
            return false;
        }

        private static bool AnySlotMenu(MapTimelineMenuGreece menu)
        {
            if (menu == null) return false;
            for (int i = 0; i < Slots.Length; i++)
            {
                Registration slot = Slots[i];
                if (slot != null && !slot.Revoked && SameMenu(slot.Menu, menu)) return true;
            }
            return false;
        }

        private static bool AnyUsable(MapTimelineMenuGreece menu)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Registration slot = Slots[i];
                if (slot == null) continue;
                if (menu != null && !SameMenu(slot.Menu, menu)) continue;
                if (Usable(slot)) return true;
            }
            return false;
        }

        /// <summary>字典键的 UI 存在性：槽位结构仍在（含 B 不可用的 hidden slot）即 present；
        /// 只在该 tuple 撤销/未登记时降级 9（绝不指向不存在的 UI）。</summary>
        private static bool SlotPresentForLookup(int physicalLand)
        {
            Registration slot = FindSlotByPhysical(physicalLand);
            return slot != null && !slot.Revoked;
        }

        /// <summary>三套数组是否都仍为我们登记的 exact 槽位（撤销缩表前的安全门）。</summary>
        private static bool ArraysAllOurs(UIMainMap map)
        {
            try
            {
                Il2CppReferenceArray<UILand> mapLands = map._lands;
                Il2CppReferenceArray<UIMainMapLand> mapButtons = map._mainMapLands;
                if (mapLands == null || mapButtons == null) return false;
                if (mapLands.Length != ExtensionIslandMapPlan.ExtendedUiCount
                    || mapButtons.Length != ExtensionIslandMapPlan.ExtendedUiCount) return false;
                for (int i = 0; i < Slots.Length; i++)
                {
                    Registration slot = Slots[i];
                    if (slot == null) return false;
                    if (!SameInstance(mapLands[slot.UiIndex], slot.Overview)) return false;
                    if (!SameInstance(mapButtons[slot.UiIndex], slot.OverviewButton)) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SameMenu(MapTimelineMenuGreece a, MapTimelineMenuGreece b)
        {
            if (a == null || b == null) return false;
            try { return a.Pointer == b.Pointer; } catch (Exception) { return false; }
        }

        private static bool InstancesAlive(Registration slot)
        {
            return slot != null
                && slot.Detail != null && slot.Overview != null && slot.OverviewButton != null
                && slot.DetailGo != null && slot.OverviewGo != null
                && slot.Menu != null && slot.Map != null;
        }

        /// <summary>槽位一致性：实例活着 + 当前 menu._mainMap 就是登记 map + 三套数组 exact 含我们（12 形状）。</summary>
        private static bool SlotsIntact(Registration slot)
        {
            try
            {
                if (slot == null || slot.Menu == null || slot.Map == null) return false;
                if (!InstancesAlive(slot)) return false;
                if (!SameInstance(slot.Menu._mainMap, slot.Map)) return false;
                Il2CppSystem.Collections.Generic.List<UILand> lands = slot.Menu.lands;
                Il2CppReferenceArray<UILand> mapLands = slot.Map._lands;
                Il2CppReferenceArray<UIMainMapLand> mapButtons = slot.Map._mainMapLands;
                if (lands == null || mapLands == null || mapButtons == null) return false;
                if (!ExtensionIslandMapPlan.HasExtendedShape(lands.Count, mapLands.Length, mapButtons.Length)) return false;
                return SameInstance(lands[slot.UiIndex], slot.Detail)
                    && SameInstance(mapLands[slot.UiIndex], slot.Overview)
                    && SameInstance(mapButtons[slot.UiIndex], slot.OverviewButton);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CampaignIntact(Registration slot)
        {
            if (slot == null) return false;
            try
            {
                CampaignSaveData campaign = CampaignSaveData.current;
                return campaign != null && campaign.Pointer == slot.CampaignPointer;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Usable(Registration slot)
            => slot != null && !slot.Revoked && SlotsIntact(slot) && CampaignIntact(slot);

        private static void DestroySafe(GameObject go)
        {
            if (go == null) return;
            try { UnityEngine.Object.Destroy(go); } catch (Exception) { }
        }

        private static bool SameInstance(UnityEngine.Object a, UnityEngine.Object b)
        {
            if (a == null || b == null) return false;
            try { return a.Pointer == b.Pointer; } catch (Exception) { return false; }
        }

        private static int CurrentPhysicalLand(out bool known)
        {
            known = false;
            try
            {
                Managers managers = Managers.Inst;
                if (managers != null && managers.game != null)
                {
                    known = true;
                    return managers.game.currentLand;
                }
                CampaignSaveData campaign = CampaignSaveData.current;
                if (campaign != null)
                {
                    known = true;
                    return campaign.CurrentLand;
                }
            }
            catch (Exception) { }
            return -1;
        }

        // ------------------------------------------------------------------ 日志

        private static void Log(string message)
        {
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + message); }
            catch (Exception) { }
        }

        private static void LogOnce(string key, string message)
        {
            try
            {
                if (!LoggedKeys.Add(key)) return;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + message);
            }
            catch (Exception) { }
        }

        private static void LogError(string message)
        {
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogError(LogPrefix + message); }
            catch (Exception) { }
        }
    }
}
