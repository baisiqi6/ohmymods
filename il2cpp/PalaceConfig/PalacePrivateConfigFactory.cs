using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod.PalaceSource
{
    /// <summary>
    /// D1 私有 land12 配置工厂（冻结 S3 `TryBuildClone`/`TryFindTemplateIndex`/
    /// `BuildGroupCountList`/`AddGroup` 的具名最小闭包 + 批准的所有权顺序）。
    ///
    /// SOURCE-only：真实 `UnityEngine.Object.Instantiate` 与原生字段 setter 是 native 效果，
    /// 必须与纯 recipe/校验分开看待；本工程只证明源码/ABI，不证明 native 执行、native 返回资格
    /// 或生命周期完成。无 Destroy/DestroyImmediate/Dispose，无释放/资格 delegate，无第二个 provider。
    ///
    /// 顺序（批准裁定钉死，禁止照抄旧 S3 的 setter 后再查 template 同 pointer）：
    /// 1) 读 holder/curBiomeData/levelConfigs（null 或长度 &lt;=1 拒绝）、模板选择与 19 组建前快照；
    /// 2) 预留唯一在途责任槽（拒重入/并发/满额，均在 Instantiate 之前）；
    /// 3) Instantiate；返回非 null 后先把 exact wrapper 放入预留槽为 Unclassified，再读 pointer；
    /// 4) pointer 抛错/为零、template/currentPublished/保留 pending 同 pointer => Ambiguous + sticky，
    ///    零 setter、零销毁、对象保留；仅识别为可识别自有 clone 后才写字段/校验；
    /// 5) 字段校验 + 模板 19 组建后复核均通过才发布；否则 built 保留为未发布失败责任。
    /// </summary>
    internal static class PalacePrivateConfigFactory
    {
        /// <summary>pointer 读取（getter 本身可能抛出）；异常/空对象 => 0（unknown）。</summary>
        internal static ulong PointerOf(Il2CppObjectBase value)
        {
            try { return !ReferenceEquals(value, null) ? (ulong)value.Pointer.ToInt64() : 0UL; }
            catch (Exception) { return 0UL; }
        }

        internal static string Hex(ulong value) => "0x" + value.ToString("x");

        /// <summary>
        /// 一次有界构建尝试。调用方（cache.TryGetClone）已确认 sticky/ holder 指针；本方法负责
        /// 预留在途槽、Instantiate 与全部责任交接。失败时 out clone=null，sticky 由 cache 记录。
        /// </summary>
        internal static bool TryBuildClone(BiomeHolder holder, ulong holderPtr,
            PalacePrivateConfigCache cache, out LevelConfig clone, out string report)
        {
            clone = null;
            bool wrapperAttached = false;
            try
            {
                BiomeData biomeData = holder != null ? holder.curBiomeData : null;
                if (biomeData == null) { report = PreAllocationFailure(cache, "curBiomeData null"); return false; }
                Il2CppReferenceArray<LevelConfig> configs = biomeData.levelConfigs;
                if (configs == null || configs.Length <= 1)
                { report = PreAllocationFailure(cache, "levelConfigs unusable"); return false; }
                if (!TryFindTemplateIndex(configs, out int templateIndex))
                { report = PreAllocationFailure(cache, "template " + PalacePrivateConfigPlan.TemplateAssetName + " not found"); return false; }
                LevelConfig template = configs[templateIndex];
                if (template == null) { report = PreAllocationFailure(cache, "template null"); return false; }
                ulong templatePtr = PointerOf(template);
                if (templatePtr == 0)
                { report = PreAllocationFailure(cache, "template pointer unreadable"); return false; }

                var templateBefore = new int[PalacePrivateConfigPlan.Intent.Length * 2];
                if (!PalacePrivateConfigValidation.TryCaptureIntentCounts(template, templateBefore, out string captureError))
                { report = PreAllocationFailure(cache, "template counts unreadable: " + captureError); return false; }

                // 在途责任槽必须在 Instantiate 之前预留；满额/重入/并发在这里被拒绝。
                if (!cache.TryReserveForAllocation(out string reserveReason))
                {
                    report = "allocation refused: " + reserveReason;
                    return false;
                }

                LevelConfig built;
                try
                {
                    built = UnityEngine.Object.Instantiate(template);
                }
                catch (Exception e)
                {
                    // 可识别 wrapper 返回前抛出：不虚构 owner，保留预留歧义责任、sticky、不重开分配。
                    cache.MarkReservedAmbiguous("instantiate-exception:" + e.GetType().Name + " " + e.Message);
                    report = cache.FailureReason;
                    return false;
                }
                if (ReferenceEquals(built, null))
                {
                    // 无可识别对象：不虚构 owner；清预留但 sticky，后续不重试、不循环分配。
                    cache.ReleaseReservationNothingReturned("instantiate returned null");
                    report = cache.FailureReason;
                    return false;
                }

                // 非 null 返回后的第一动作：exact wrapper 入预留槽为 Unclassified（先于 pointer 读取，
                // pointer getter 本身可能抛出）。
                bool attached = cache.AttachReservedUnclassified(built);
                wrapperAttached = true;
                if (!attached) { report = cache.FailureReason; return false; }

                if (!cache.TryReadPendingPointer(out ulong builtPtr, out string pointerError))
                {
                    cache.MarkReservedAmbiguous("pointer unreadable: " + pointerError);
                    report = cache.FailureReason;
                    return false;
                }
                if (builtPtr == 0)
                {
                    cache.MarkReservedAmbiguous("pointer zero");
                    report = cache.FailureReason;
                    return false;
                }
                if (builtPtr == templatePtr)
                {
                    cache.MarkReservedAmbiguous("alias of template " + Hex(builtPtr));
                    report = cache.FailureReason;
                    return false;
                }
                ulong publishedPtr = cache.PublishedPointer;
                if (publishedPtr != 0 && builtPtr == publishedPtr)
                {
                    cache.MarkReservedAmbiguous("alias of current published " + Hex(builtPtr));
                    report = cache.FailureReason;
                    return false;
                }
                // A retained pending object already blocks reservation before Instantiate.
                // The current pending slot now contains built itself; do not compare it as an alias.

                // 到这里才是可识别自有 clone：仅自有对象允许字段写入与校验。
                if (!cache.TryMarkReservedOwnedInFlight(built, builtPtr, out string classifyReason))
                { report = classifyReason; return false; }
                // The single cache owns this private asset for the entire process, across Reload.
                // CLR wrapper retention alone does not keep a Unity ScriptableObject alive.
                HideFlags flagsBefore = built.hideFlags;
                built.hideFlags = flagsBefore | HideFlags.DontUnloadUnusedAsset;
                if ((built.hideFlags & HideFlags.DontUnloadUnusedAsset) == 0)
                { cache.RetainReservedFailedUnpublished("lifetime-flag-readback"); report = cache.FailureReason; return false; }
                built.name = PalacePrivateConfigPlan.PrivateConfigName;
                built.blockName = PalacePrivateConfigPlan.BlockName;
                built.minLevelWidth = PalacePrivateConfigPlan.MinLevelWidthCandidate;
                built.questType = QuestType.None;
                built.islandMonumentID = -1;
                built.sharedBlocksConfigs = new Il2CppReferenceArray<SharedBlocksConfig>(0);
                built.caveless = true;
                built.twoCliffs = false;
                built.randomizeCliffSide = false;
                built.groupCounts = BuildGroupCountList(template.groupCounts);
                built._counts = null;   // 必须在任何 GetCount 校验读回之前

                var parts = new List<string>(8);
                if (!PalacePrivateConfigValidation.ValidatePrivateConfig(built, parts, out bool fieldOk) || !fieldOk)
                {
                    cache.RetainReservedFailedUnpublished("validation failed: " + string.Join("; ", parts));
                    report = cache.FailureReason;
                    return false;
                }

                if (!PalacePrivateConfigValidation.IntentCountsUnchanged(template, templateBefore, out string templateError))
                {
                    cache.RetainReservedFailedUnpublished("template count check failed: " + templateError);
                    report = cache.FailureReason;
                    return false;
                }

                // Capture diagnostics before the publication boundary. No fallible native
                // property reads may occur after publishing/retiring the responsibility slots.
                string successReport = "built holder=" + Hex(holderPtr) + " clone=" + Hex(builtPtr)
                    + " template=" + Hex(templatePtr) + " hideFlags=" + (int)flagsBefore + "->" + (int)built.hideFlags + " (validated private recipe; SOURCE-only)";
                if (!cache.PublishReserved(builtPtr, holderPtr, out string publishError))
                {
                    report = publishError;
                    return false;
                }

                clone = built;
                report = successReport;
                return true;
            }
            catch (Exception e)
            {
                string why = "build-exception:" + e.GetType().Name + " " + e.Message;
                clone = null;
                PalacePrivateConfigCache.PendingKind state = cache.PendingState;
                if (wrapperAttached && (state == PalacePrivateConfigCache.PendingKind.OwnedInFlight
                    || state == PalacePrivateConfigCache.PendingKind.FailedUnpublished))
                {
                    // wrapper 已返回：责任不得丢失，保留为未发布失败。
                    cache.RetainReservedFailedUnpublished(why);
                }
                else if (state == PalacePrivateConfigCache.PendingKind.InFlight
                    || state == PalacePrivateConfigCache.PendingKind.Unclassified
                    || state == PalacePrivateConfigCache.PendingKind.Ambiguous)
                {
                    // 预留后、wrapper 返回前抛出：保留预留歧义责任。
                    cache.MarkReservedAmbiguous(why);
                }
                else
                {
                    cache.RecordFailure(why);
                }
                report = cache.FailureReason;
                return false;
            }
        }

        private static string PreAllocationFailure(PalacePrivateConfigCache cache, string why)
        {
            cache.RecordFailure(why);
            return cache.FailureReason;
        }

        /// <summary>模板选择：数组顺序第一个精确 name 优先，未找到再取第一个 GodIslandArtemis quest；不改数组。</summary>
        internal static bool TryFindTemplateIndex(Il2CppReferenceArray<LevelConfig> configs, out int index)
        {
            index = -1;
            for (int i = 0; i < configs.Length; i++)
            {
                LevelConfig candidate = configs[i];
                if (candidate == null) continue;
                try
                {
                    if (string.Equals(candidate.name, PalacePrivateConfigPlan.TemplateAssetName, StringComparison.Ordinal))
                    { index = i; return true; }
                }
                catch (Exception) { }
            }
            for (int i = 0; i < configs.Length; i++)
            {
                LevelConfig candidate = configs[i];
                if (candidate == null) continue;
                try
                {
                    if (candidate.questType == QuestType.GodIslandArtemis) { index = i; return true; }
                }
                catch (Exception) { }
            }
            return false;
        }

        /// <summary>模板 group 出现顺序去重，再按 19 项 Intent 顺序补缺；每行全新对象；未知组 [0,0]。</summary>
        internal static Il2CppSystem.Collections.Generic.List<LevelGroupCount> BuildGroupCountList(
            Il2CppSystem.Collections.Generic.List<LevelGroupCount> template)
        {
            var result = new Il2CppSystem.Collections.Generic.List<LevelGroupCount>();
            var handled = new HashSet<int>();
            if (template != null)
            {
                for (int i = 0; i < template.Count; i++)
                {
                    LevelGroupCount entry = template[i];
                    if (entry == null) continue;
                    int group = (int)entry.group;
                    if (!handled.Add(group)) continue;
                    AddGroup(result, group);
                }
            }
            for (int i = 0; i < PalacePrivateConfigPlan.Intent.Length; i++)
            {
                int group = PalacePrivateConfigPlan.Intent[i].Group;
                if (handled.Add(group)) AddGroup(result, group);
            }
            return result;
        }

        private static void AddGroup(Il2CppSystem.Collections.Generic.List<LevelGroupCount> list, int group)
        {
            PalacePrivateConfigPlan.TryGetIntent(group, out int min, out int max);   // absent => [0,0]
            var entry = new LevelGroupCount();
            entry.group = (LevelBlockGroup)group;
            entry.count = new IntRange(min, max);
            list.Add(entry);
        }
    }
}
