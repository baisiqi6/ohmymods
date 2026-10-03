using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 扩展希腊岛的实例登记／UI-native 编号／Confirm 导航（travel-worker）。
    ///
    /// 边界（native-travel CONTRACT + integration-contract + review-lifecycle-followup）：
    /// - physical land11 / 可见 UI10；原 0..9 与 10→UI9 全部保持。
    /// - 只操作当前 Greek <see cref="MapTimelineMenuGreece"/> 实例：追加 detail lands[10]、
    ///   mainMap._lands[10] 与 _mainMapLands[10]（exact 配对），并 append 实例字典 11→10。
    ///   共享 CampaignData/模板数组/资产一律不写。
    /// - 登记有效性 = 实例活着 + 三套数组槽位 + **当前 menu._mainMap 身份** + 当前 campaign；
    ///   任一失效即撤销：隐藏/销毁自有节点、解除 list/array 槽位与 UI selection、字典降级 9，
    ///   并把该登记移入 tombstone（拒绝身份）直到原生 ClearLands/菜单销毁真清理。
    ///   tombstone 只拦“该菜单实例的 UI10”（原生 Greek 不存在 index 10），
    ///   其他菜单实例/未知 context/0..9 原样交还原生。
    /// - UILand.UpdateLand 前缀只对登记实例把 10 → 11：船标与全部动态/静态图标都按 physical11
    ///   读取；旧历史 reign 缺 landData[11] 时原生按越界即缺失处理（绝不借用 Top10/未来数据）。
    /// - Confirm 只替换登记 UI10 的两阶段分支；旅行门 = 当前普通 Greek owner +
    ///   IsAvailableForCurrentCampaign && EnsureReady（fail-closed）。
    /// - 克隆（模板路径与懒建路径一致）先净化运行期自建节点（KEM_MapResourceIcons/
    ///   KEM_MapIconSources：inactive+detach+Destroy）再重建静态图标缓存；懒建优先复用
    ///   LoadLands 捕获的干净模板 prefab，绝不把 live 详情实例的旧资源目录带进 land11。
    /// </summary>
    internal static class ExtensionIslandMap
    {
        private const string LogPrefix = "[ExtensionIslandMap] ";
        private const int MaxTombstones = 8;

        private sealed class Owner
        {
            internal MapTimelineMenuGreece Menu;
            internal UIMainMap Map;
            internal UILand Detail;                // menu.lands[10]
            internal UILand Overview;              // map._lands[10]
            internal UIMainMapLand OverviewButton; // map._mainMapLands[10]
            internal GameObject DetailGo;
            internal GameObject OverviewGo;
            internal IntPtr CampaignPointer;
            internal bool Revoked;
            /// <summary>撤销原因仅为授权/就绪失效（上下文未变），允许后续合法重建成新登记。</summary>
            internal bool RevokeRecoverable;
            internal string RevokedReason;
        }

        /// <summary>当前有效登记（唯一）。</summary>
        private static Owner _owner;
        /// <summary>已撤销登记的拒绝身份（按菜单实例区分；有界，随真清理/菜单销毁回收）。</summary>
        private static readonly List<Owner> Tombstones = new List<Owner>(4);
        /// <summary>LoadLands 参数里捕获的干净详情模板（懒建优先复用，避免继承 live 旧资源）。</summary>
        private static MapTimelineMenuGreece _detailTemplateMenu;
        private static GameObject _detailTemplatePrefab;
        private static readonly HashSet<string> LoggedKeys = new HashSet<string>(StringComparer.Ordinal);

        // ------------------------------------------------------------------ Harmony 入口

        /// <summary>LoadLands postfix：当前实例追加第 11 份详情模板与总览簇。</summary>
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
                if (_owner != null && SameMenu(_owner.Menu, menu))
                {
                    FixLookup(menu, false);
                    ClearOwner("native-clear");
                }
                if (SameMenu(_detailTemplateMenu, menu))
                {
                    _detailTemplateMenu = null;
                    _detailTemplatePrefab = null;
                }
                RemoveTombstone(menu);
                PruneTombstones();
            }
            catch (Exception e) { LogError("clear-lands failed: " + e.GetType().Name + " " + e.Message); }
        }

        /// <summary>UILand.UpdateLand 前缀：登记实例的 UI10 → physical11。</summary>
        internal static bool TryRemapUpdateLand(UILand land, int uiLand, out int physical)
        {
            physical = uiLand;
            bool registered = false;
            try { registered = TryGetPhysicalLandIndex(land, out _); }
            catch (Exception) { registered = false; }
            if (!ExtensionIslandMapPlan.ShouldRemapUpdateLand(registered, uiLand, out int mapped)) return false;
            physical = mapped;
            return physical != uiLand;
        }

        /// <summary>Confirm 前缀判定体：返回 true 表示本前缀已接管（调用方必须跳过原生）。</summary>
        internal static bool TryHandleConfirm(MapTimelineMenuGreece menu, int landIndex)
        {
            bool ownMenu = false;
            try
            {
                if (menu == null || landIndex != ExtensionIslandMapPlan.UiIndex) return false;

                Owner owner = _owner;
                if (owner != null && SameMenu(owner.Menu, menu))
                {
                    ownMenu = true;
                    if (!Usable(owner))
                    {
                        RevokeOwner("unusable");
                        return true;   // 可靠拒绝：consume、无 result、绝不回落 native10
                    }

                    int currentLand = CurrentPhysicalLand(out bool known);
                    bool canTravel = known
                        && ExtensionIslandRuntime.IsAvailableForCurrentCampaign()
                        && ExtensionIslandRuntime.EnsureReady();

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
                            confirm.interactable = ExtensionIslandMapPlan.Stage0ConfirmInteractable(userCanSelectLand, canTravel, currentLand);
                        }
                        return true;
                    }

                    if (action != ExtensionConfirmAction.Succeed) return true;

                    // 原生阶段 2 成功：结果写 physical11；音效/菜单字段与原生一致。
                    menu.landResult = ExtensionIslandRuntime.LandIndex;
                    AudioSource audio = menu.newIslandAudioSource;
                    if (audio != null && !audio.isPlaying) audio.Play();
                    Menu menuInst = Menu.InstExists ? Menu.Inst : null;
                    if (menuInst != null) menuInst.menuBackoutAllowed = true;
                    Menu hide = menuInst != null ? menuInst : menu._menu;
                    if (hide != null) hide.Hide();
                    return true;
                }

                // 已撤销登记：仅拦该菜单实例的 UI10（consume/无 result），绝不全局拦其他 context。
                if (FindTombstone(menu) != null) return true;
                return false;
            }
            catch (Exception e)
            {
                LogError("confirm failed: " + e.GetType().Name + " " + e.Message);
                // 已确认是本登记菜单的 UI10 时 fail-closed：绝不把 10 交回原生（会出航 Top）。
                return ownMenu;
            }
        }

        // ------------------------------------------------------------------ 查询接口（map-worker 使用）

        /// <summary>
        /// 仅当前登记的扩展 UILand（详情 / 总览两个 exact 实例）返回 true / physical11。
        /// 严格 owner：实例活着 + 当前 menu._mainMap 身份 + 数组槽位 + campaign 一致；未撤销。
        /// 非登记实例即使 index=10 也返回 false。
        /// </summary>
        internal static bool TryGetPhysicalLandIndex(UILand land, out int index)
        {
            index = 0;
            try
            {
                Owner owner = _owner;
                if (owner == null || land == null) return false;
                if (owner.Menu == null) { ClearOwner("dead-menu"); return false; }
                if (!SlotsIntact(owner)) { RevokeOwner("invalid"); return false; }
                if (!SameInstance(land, owner.Detail) && !SameInstance(land, owner.Overview)) return false;
                if (!CampaignIntact(owner)) { RevokeOwner("campaign"); return false; }
                index = ExtensionIslandRuntime.LandIndex;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>点击派发门（详情回调 / 总览按钮 listener）：撤销后自有回调必须 no-op。</summary>
        internal static bool IsDispatchLive(MapTimelineMenuGreece menu)
        {
            try
            {
                Owner owner = _owner;
                if (owner == null || menu == null) return false;
                if (!SameMenu(owner.Menu, menu)) return false;
                return SlotsIntact(owner) && CampaignIntact(owner);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ 追加/撤销/清理

        private static void EnsureAppended(MapTimelineMenuGreece menu, Il2CppReferenceArray<GameObject> landPrefabs, bool fromLoad)
        {
            if (menu == null) return;
            PruneTombstones();

            Owner live = _owner;
            if (live != null)
            {
                if (SameMenu(live.Menu, menu))
                {
                    bool usable = Usable(live);
                    bool authorized = usable
                        && ExtensionIslandRuntime.TryScope(out _)
                        && ExtensionIslandRuntime.IsAvailableForCurrentCampaign()
                        && ExtensionIslandRuntime.EnsureReady();
                    if (usable && authorized)
                    {
                        FixLookup(menu, true);   // 幂等：只校正字典值
                        return;
                    }
                    RevokeOwner(usable ? "authorization-lost" : "stale");
                    return;   // 本轮不重建：重建留待后续合法生命周期
                }
                // 登记属于另一个菜单实例（Unity Destroy 可能延迟到帧末）：先把它降级为
                // tombstone（保留其拒绝身份），再允许本实例走正常登记。
                RevokeOwner("menu-replaced");
            }

            Owner tomb = FindTombstone(menu);
            if (tomb != null && !tomb.RevokeRecoverable) return;   // 该菜单上下文已失效：保持拒绝，等真清理

            // LoadLands 参数入口先保留干净详情模板引用：懒追加也优先从该 prefab 创建，
            // 避免把 live 详情实例的旧资源目录（KEM 节点/Oracle-only 图标）带进 land11。
            GameObject loadedTemplate = fromLoad ? CaptureDetailTemplate(menu, landPrefabs) : null;

            if (!ExtensionIslandRuntime.TryScope(out CampaignSaveData campaign)) return;   // 非当前普通 Greek owner
            if (!ExtensionIslandRuntime.IsAvailableForCurrentCampaign()) return;           // 不在授权范围：不动 UI
            if (!ExtensionIslandRuntime.EnsureReady())
            {
                LogError("EnsureReady failed; extension island UI stays closed");
                // 防当前岛=11 时 ShowRoutine 对 11 的字典查找抛 KeyNotFound：降级聚焦 UI9。
                SetLookup(menu, ExtensionIslandMapPlan.FallbackFocusUi);
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

            GameObject detailGo = null;
            GameObject overviewGo = null;
            bool appended = false;
            bool arraysReplaced = false;
            bool success = false;
            try
            {
                // 1) 详情：与原生同路径（模板 → GetAssetSwap → _landsHolder 下实例化），随后
                //    净化自建节点并重建静态图标缓存（模板路径与懒建路径同一净化）。
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
                detailGo = UnityEngine.Object.Instantiate(detailPrefab, menu._landsHolder, false);
                if (detailGo == null) return;
                try { detailGo.transform.localScale = Vector3.one; } catch (Exception) { }
                try { detailGo.name = "Map_Land_Extension_Greece"; } catch (Exception) { }
                SanitizeClone(detailGo);
                UILand detail = detailGo.GetComponent<UILand>();
                if (detail == null) return;
                RebuildStaticIconCache(detailGo, detail);
                if (!AttachDetailCallback(menu, detail)) return;   // 不可导航的 UI 不登记（回滚）
                bool detailFocused = false;
                try { detailFocused = menu.focusedLand == ExtensionIslandMapPlan.UiIndex; } catch (Exception) { }
                detailGo.SetActive(detailFocused);
                lands.Add(detail);
                appended = true;
                if (lands.Count != ExtensionIslandMapPlan.NativeUiCount + 1
                    || !SameInstance(lands[ExtensionIslandMapPlan.UiIndex], detail))
                {
                    return;   // finally 回滚
                }

                // 2) 总览簇：实例克隆 UI0（正常外岛美术）+ 同一净化 + 事件整体替换。
                overviewGo = UnityEngine.Object.Instantiate(overviewSource.gameObject, map.transform, false);
                if (overviewGo == null) return;
                try { overviewGo.name = "Main_Map_Land_Extension_Greece"; } catch (Exception) { }
                SanitizeClone(overviewGo);
                UILand overview = overviewGo.GetComponent<UILand>();
                UIMainMapLand overviewButton = overviewGo.GetComponent<UIMainMapLand>();
                if (overview == null || overviewButton == null) return;
                RebuildStaticIconCache(overviewGo, overview);
                try { overviewButton._lockedByQuest = QuestType.None; }
                catch (Exception e) { LogOnce("lock", "set extension lock failed: " + e.Message); }
                if (!WireOverviewButton(menu, map, overviewButton, overviewGo)) return;
                try { overviewGo.SetActive(true); } catch (Exception) { }

                // 3) 两套数组同步扩到 11（exact 配对）；只改当前实例。
                var newLands = new Il2CppReferenceArray<UILand>(ExtensionIslandMapPlan.NativeUiCount + 1);
                var newButtons = new Il2CppReferenceArray<UIMainMapLand>(ExtensionIslandMapPlan.NativeUiCount + 1);
                for (int i = 0; i < ExtensionIslandMapPlan.NativeUiCount; i++)
                {
                    newLands[i] = originalLands[i];
                    newButtons[i] = originalButtons[i];
                }
                newLands[ExtensionIslandMapPlan.UiIndex] = overview;
                newButtons[ExtensionIslandMapPlan.UiIndex] = overviewButton;
                map._lands = newLands;
                arraysReplaced = true;
                map._mainMapLands = newButtons;

                Il2CppReferenceArray<UILand> checkLands = map._lands;
                Il2CppReferenceArray<UIMainMapLand> checkButtons = map._mainMapLands;
                if (checkLands == null || checkButtons == null
                    || !ExtensionIslandMapPlan.HasExtendedShape(lands.Count, checkLands.Length, checkButtons.Length)
                    || !SameInstance(checkLands[ExtensionIslandMapPlan.UiIndex], overview)
                    || !SameInstance(checkButtons[ExtensionIslandMapPlan.UiIndex], overviewButton))
                {
                    return;   // finally 回滚
                }

                // 4) 字典 11→10（只 append；原生 0..10 值不动）——写失败/读回不符则整笔回滚，
                //    绝不登记一个 focus 会 KeyNotFound 的 UI；成功后登记 owner 并清同名 tombstone。
                if (!FixLookup(menu, true)) return;   // finally 回滚
                _owner = new Owner
                {
                    Menu = menu,
                    Map = map,
                    Detail = detail,
                    Overview = overview,
                    OverviewButton = overviewButton,
                    DetailGo = detailGo,
                    OverviewGo = overviewGo,
                    CampaignPointer = campaign != null ? campaign.Pointer : IntPtr.Zero,
                };
                RemoveTombstone(menu);
                success = true;
                Log("extension island UI appended: detail lands[10], overview _lands[10]/_mainMapLands[10], lookup 11->10");
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
                        try
                        {
                            Il2CppSystem.Collections.Generic.List<UILand> liveLands = menu.lands;
                            if (liveLands != null && liveLands.Count > ExtensionIslandMapPlan.NativeUiCount)
                            {
                                liveLands.RemoveAt(liveLands.Count - 1);
                            }
                        }
                        catch (Exception) { }
                    }
                    DestroySafe(overviewGo);
                    DestroySafe(detailGo);
                }
            }
        }

        /// <summary>
        /// 撤销当前登记：隐藏/销毁自有节点、解除 list/array 槽位与 UI selection、字典降级 9；
        /// 登记移入 tombstone（拒绝身份）直到 menu 销毁或原生 ClearLands 真清理。
        /// 结构回退只碰“当前 map 就是登记 map 且槽位仍是我们”的数组；绝不触碰外来新 map。
        /// </summary>
        private static void RevokeOwner(string reason)
        {
            Owner owner = _owner;
            if (owner == null || owner.Revoked) return;
            bool slotsOk = false;
            bool campaignOk = false;
            try { slotsOk = SlotsIntact(owner); } catch (Exception) { }
            try { campaignOk = CampaignIntact(owner); } catch (Exception) { }
            owner.Revoked = true;
            owner.RevokeRecoverable = slotsOk && campaignOk;   // 仅授权/就绪失效时可重建
            owner.RevokedReason = reason;
            LogOnce("revoked-" + reason, "extension island registration revoked (" + reason
                + ", recoverable=" + (owner.RevokeRecoverable ? "yes" : "no") + "); extra UI dismantled");

            MapTimelineMenuGreece menu = owner.Menu;
            UIMainMap map = owner.Map;

            try { if (owner.DetailGo != null) owner.DetailGo.SetActive(false); } catch (Exception) { }
            try { if (owner.OverviewGo != null) owner.OverviewGo.SetActive(false); } catch (Exception) { }
            try { if (menu != null) FixLookup(menu, false); } catch (Exception) { }

            // 详情：仅当仍在 menu.lands 末尾且确为我们时移除。
            try
            {
                Il2CppSystem.Collections.Generic.List<UILand> lands = menu != null ? menu.lands : null;
                if (lands != null && lands.Count > ExtensionIslandMapPlan.NativeUiCount
                    && SameInstance(lands[lands.Count - 1], owner.Detail))
                {
                    lands.RemoveAt(lands.Count - 1);
                }
            }
            catch (Exception e) { LogOnce("revoke-detail", "revoke detail slot failed: " + e.Message); }

            // 总览数组：仅当当前 menu._mainMap 就是登记 map 时，逐数组、逐槽位确认后缩回原生 10
            // （处理单侧数组被外部替换的半损坏状态）。外来新 map 一律不碰。
            try
            {
                if (menu != null && map != null && SameInstance(menu._mainMap, map))
                {
                    Il2CppReferenceArray<UILand> mapLands = map._lands;
                    if (mapLands != null
                        && mapLands.Length == ExtensionIslandMapPlan.NativeUiCount + 1
                        && SameInstance(mapLands[ExtensionIslandMapPlan.UiIndex], owner.Overview))
                    {
                        var shrunkLands = new Il2CppReferenceArray<UILand>(ExtensionIslandMapPlan.NativeUiCount);
                        for (int i = 0; i < ExtensionIslandMapPlan.NativeUiCount; i++) shrunkLands[i] = mapLands[i];
                        map._lands = shrunkLands;
                    }
                    Il2CppReferenceArray<UIMainMapLand> mapButtons = map._mainMapLands;
                    if (mapButtons != null
                        && mapButtons.Length == ExtensionIslandMapPlan.NativeUiCount + 1
                        && SameInstance(mapButtons[ExtensionIslandMapPlan.UiIndex], owner.OverviewButton))
                    {
                        var shrunkButtons = new Il2CppReferenceArray<UIMainMapLand>(ExtensionIslandMapPlan.NativeUiCount);
                        for (int i = 0; i < ExtensionIslandMapPlan.NativeUiCount; i++) shrunkButtons[i] = mapButtons[i];
                        map._mainMapLands = shrunkButtons;
                    }
                }
            }
            catch (Exception e) { LogOnce("revoke-arrays", "revoke overview arrays failed: " + e.Message); }

            // UI selection：若当前选中对象是我们的自有节点，显式解除（其余情况由 Unity 在
            // inactive/销毁时自动失焦）。
            try
            {
                if (Menu.InstExists)
                {
                    Menu inst = Menu.Inst;
                    GameObject selected = inst != null ? inst.CurrentSelectedGameObject : null;
                    if (selected != null
                        && (SameInstance(selected, owner.OverviewGo) || SameInstance(selected, owner.DetailGo)))
                    {
                        inst.CurrentSelectedGameObject = null;
                    }
                }
            }
            catch (Exception e) { LogOnce("revoke-selection", "revoke selection clear failed: " + e.Message); }

            DestroySafe(owner.OverviewGo);
            DestroySafe(owner.DetailGo);

            _owner = null;
            Tombstones.Add(owner);
            PruneTombstones();
        }

        private static void ClearOwner(string reason)
        {
            if (_owner == null) return;
            LogOnce("cleared-" + reason, "extension island map registration dropped (" + reason + ")");
            _owner = null;
        }

        private static Owner FindTombstone(MapTimelineMenuGreece menu)
        {
            if (menu == null) return null;
            for (int i = 0; i < Tombstones.Count; i++)
            {
                Owner keeper = Tombstones[i];
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
                Owner keeper = Tombstones[i];
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
                Owner keeper = Tombstones[i];
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

        private static bool AttachDetailCallback(MapTimelineMenuGreece menu, UILand detail)
        {
            try
            {
                Il2CppSystem.Action<int> callback = (Il2CppSystem.Action<int>)(System.Action<int>)
                    (index => { if (menu != null && IsDispatchLive(menu)) menu.OnButtonLand(index); });
                detail.SetupLandCallback(callback, ExtensionIslandMapPlan.UiIndex);
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
        /// 只保留唯一回调 → 当前 mainMap.OnButtonSelectLand(UI10)（撤销后 no-op）。
        /// </summary>
        private static bool WireOverviewButton(MapTimelineMenuGreece menu, UIMainMap map, UIMainMapLand host, GameObject go)
        {
            try
            {
                Button button = host._button;
                if (button == null) button = go.GetComponentInChildren<Button>();
                if (button == null)
                {
                    LogError("overview button missing; extension UI not appended");
                    return false;
                }
                var fresh = new Button.ButtonClickedEvent();
                button.onClick = fresh;
                UnityAction action = (UnityAction)(System.Action)(() =>
                {
                    try
                    {
                        if (map != null && menu != null && IsDispatchLive(menu)) map.OnButtonSelectLand(ExtensionIslandMapPlan.UiIndex);
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
        /// KEM_MapIconSources），避免 Deferred 销毁期间仍被 GetComponentsInChildren 拾取，
        /// 从而把 Oracle/God-only 图标带进 land11 缓存。源实例不受影响。
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
        /// 把该实例字典的 11 值校正为当前 UI 存在性（有 UI→10，无 UI→9 降级焦点）。
        /// 返回 true 表示字典已按键写入且读回一致（追加事务要求）；无字典时返回 false。
        /// </summary>
        private static bool FixLookup(MapTimelineMenuGreece menu, bool uiPresent)
        {
            try
            {
                if (menu == null) return false;
                Il2CppSystem.Collections.Generic.Dictionary<int, int> dict = menu.LAND_TO_MAP_LAND;
                if (dict == null) return false;
                int value = ExtensionIslandMapPlan.LookupUiForPhysical(uiPresent);
                if (!uiPresent && !dict.ContainsKey(ExtensionIslandMapPlan.PhysicalIndex))
                {
                    return true;   // 无需降级写入（从未登记过）
                }
                dict[ExtensionIslandMapPlan.PhysicalIndex] = value;
                return dict[ExtensionIslandMapPlan.PhysicalIndex] == value;   // 读回校验
            }
            catch (Exception e)
            {
                LogOnce("lookup", "LAND_TO_MAP_LAND update failed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

        /// <summary>强制写入该实例字典的 11 值（仅扩展自己使用的键，绝不触碰原生 0..10）。</summary>
        private static void SetLookup(MapTimelineMenuGreece menu, int value)
        {
            try
            {
                if (menu == null) return;
                Il2CppSystem.Collections.Generic.Dictionary<int, int> dict = menu.LAND_TO_MAP_LAND;
                if (dict == null) return;
                dict[ExtensionIslandMapPlan.PhysicalIndex] = value;
            }
            catch (Exception e)
            {
                LogOnce("lookup-set", "LAND_TO_MAP_LAND write failed: " + e.GetType().Name + " " + e.Message);
            }
        }

        private static bool SameMenu(MapTimelineMenuGreece a, MapTimelineMenuGreece b)
        {
            if (a == null || b == null) return false;
            try { return a.Pointer == b.Pointer; } catch (Exception) { return false; }
        }

        private static bool InstancesAlive(Owner owner)
        {
            return owner != null
                && owner.Detail != null && owner.Overview != null && owner.OverviewButton != null
                && owner.DetailGo != null && owner.OverviewGo != null
                && owner.Menu != null && owner.Map != null;
        }

        /// <summary>槽位一致性：实例活着 + 当前 menu._mainMap 就是登记 map + 三套数组 exact 含我们。</summary>
        private static bool SlotsIntact(Owner owner)
        {
            try
            {
                if (owner == null || owner.Menu == null || owner.Map == null) return false;
                if (!InstancesAlive(owner)) return false;
                if (!SameInstance(owner.Menu._mainMap, owner.Map)) return false;
                Il2CppSystem.Collections.Generic.List<UILand> lands = owner.Menu.lands;
                Il2CppReferenceArray<UILand> mapLands = owner.Map._lands;
                Il2CppReferenceArray<UIMainMapLand> mapButtons = owner.Map._mainMapLands;
                if (lands == null || mapLands == null || mapButtons == null) return false;
                if (!ExtensionIslandMapPlan.HasExtendedShape(lands.Count, mapLands.Length, mapButtons.Length)) return false;
                return SameInstance(lands[ExtensionIslandMapPlan.UiIndex], owner.Detail)
                    && SameInstance(mapLands[ExtensionIslandMapPlan.UiIndex], owner.Overview)
                    && SameInstance(mapButtons[ExtensionIslandMapPlan.UiIndex], owner.OverviewButton);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CampaignIntact(Owner owner)
        {
            if (owner == null) return false;
            try
            {
                CampaignSaveData campaign = CampaignSaveData.current;
                return campaign != null && campaign.Pointer == owner.CampaignPointer;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Usable(Owner owner)
            => owner != null && !owner.Revoked && SlotsIntact(owner) && CampaignIntact(owner);

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
