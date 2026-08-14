using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 狂战士招募序列：工匠成功拾取 BerserkerTool 的第 1-5 次生成普通狂战士，
/// 第 6 次生成狂战士队长，随后重新从 1 开始。
///
/// 只在 Character.Promote(DroppableTool, IUnitController) 的同步调用栈内临时替换
/// Holder["Berserker"]；Postfix/Finalizer 均恢复，购买、读档、对象池生成和
/// BerserkerLeaderTool 升级不参与序列。
/// </summary>
[HarmonyPatch]
public static class BerserkerPromotionSequence_Patch
{
    private const int LeaderSlot = 6;

    private static int _successfulPromotionsInCycle;
    private static int _leaderCacheHolderInstanceId;
    private static Character _leaderPrefab;

    private sealed class PromotionState
    {
        public Holder Holder;
        public Character OriginalBerserker;
        public string ExpectedPrefabName;
        public string ExpectedTag;
        public int Slot;
        public bool MappingApplied;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
    [HarmonyPrefix]
    private static void Promote_Prefix(Character __instance, DroppableTool tool, out PromotionState __state)
    {
        __state = null;
        if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;

        // Worker.TryPickupBerserkerTool 在 Promote 返回后才设置 pickedUp。直接以
        // 实机已命中的 Promote 调用形态判定：活动 Worker + 尚未拾取的普通
        // BerserkerTool。Peasant、购买、LeaderTool、读档/池生成均不会满足。
        if (__instance == null
            || __instance.gameObject == null
            || !__instance.gameObject.activeInHierarchy
            || __instance.GetComponent<Worker>() == null
            || tool == null
            || tool.gameObject == null
            || !tool.gameObject.activeInHierarchy
            || tool.pickedUp
            || !tool.CompareTag("BerserkerTool")) return;

        try
        {
            var managers = Managers.Inst;
            var holder = managers != null ? managers.holder : null;
            if (holder == null || holder.gameObject == null || holder.tagCharacterPairs == null) return;

            Character ordinary = null;
            if (!holder.tagCharacterPairs.TryGetValue("Berserker", out ordinary)
                || !IsPrefabForTag(ordinary, "Berserker"))
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Roles] Berserker sequence skipped: ordinary prefab mapping is unavailable");
                return;
            }

            int slot = _successfulPromotionsInCycle + 1;
            Character expected = ordinary;
            string expectedTag = "Berserker";

            if (slot == LeaderSlot)
            {
                Character leader = FindLeaderPrefab(holder);
                if (!IsPrefabForTag(leader, "BerserkerLeader"))
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[Roles] Berserker sequence slot 6 skipped: leader prefab is unavailable");
                    return;
                }

                expected = leader;
                expectedTag = "BerserkerLeader";
            }

            Character effective = ResolveEffectivePrefab(expected);
            if (!IsPrefabForTag(effective, expectedTag))
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Roles] Berserker sequence skipped: effective prefab mismatch for " + expectedTag);
                return;
            }

            if (slot == LeaderSlot && Pool.GetPoolFromPrefabAsset(effective.gameObject) == null)
            {
                // Leader pool should normally be registered during Holder/Castle initialization.
                // Repair once, then validate the effective (post-biome-swap) prefab actually used
                // by Character.ReplaceBy before temporarily changing the mapping.
                PatchRoles_Castle.EnsurePoolForCharacter("BerserkerLeader");
                effective = ResolveEffectivePrefab(expected);
                if (!IsPrefabForTag(effective, expectedTag)
                    || Pool.GetPoolFromPrefabAsset(effective.gameObject) == null)
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[Roles] Berserker sequence slot 6 skipped: effective leader pool is unavailable");
                    return;
                }
            }

            __state = new PromotionState
            {
                Holder = holder,
                OriginalBerserker = ordinary,
                ExpectedPrefabName = effective.gameObject.name,
                ExpectedTag = expectedTag,
                Slot = slot,
                MappingApplied = false
            };

            if (slot == LeaderSlot)
            {
                holder.tagCharacterPairs["Berserker"] = expected;
                __state.MappingApplied = true;
            }
        }
        catch (Exception e)
        {
            Restore(__state);
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
    [HarmonyPostfix]
    private static void Promote_Postfix(Character __result, PromotionState __state)
    {
        if (__state == null) return;

        Restore(__state);
        try
        {
            if (!MatchesExpectedResult(__result, __state))
            {
                string actual = __result != null && __result.gameObject != null
                    ? __result.gameObject.name
                    : "<null>";
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                    "[Roles] Berserker sequence result mismatch: expected "
                    + __state.ExpectedPrefabName + ", got " + actual);
                return;
            }

            _successfulPromotionsInCycle = __state.Slot == LeaderSlot
                ? 0
                : __state.Slot;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[Roles] Berserker recruitment slot " + __state.Slot
                + " -> " + __state.ExpectedTag);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Promote), new[] { typeof(DroppableTool), typeof(IUnitController) })]
    [HarmonyFinalizer]
    private static Exception Promote_Finalizer(Exception __exception, PromotionState __state)
    {
        Restore(__state);
        return __exception;
    }

    private static Character FindLeaderPrefab(Holder holder)
    {
        int holderInstanceId = holder.gameObject.GetInstanceID();
        if (_leaderCacheHolderInstanceId == holderInstanceId
            && IsPrefabForTag(_leaderPrefab, "BerserkerLeader"))
            return _leaderPrefab;

        Character leader = null;
        if (!holder.tagCharacterPairs.TryGetValue("BerserkerLeader", out leader)
            || !IsPrefabForTag(leader, "BerserkerLeader"))
        {
            // Cross-biome Holder registration should provide the mapping. The Resources fallback
            // accepts exactly one tagged prefab; ambiguity fails closed instead of guessing.
            leader = null;
            var allCharacters = Resources.LoadAll<Character>("");
            for (int i = 0; i < allCharacters.Length; i++)
            {
                Character candidate = allCharacters[i];
                if (!IsPrefabForTag(candidate, "BerserkerLeader")) continue;
                if (leader != null && leader != candidate)
                {
                    KingdomEnhancedPlugin.Instance?.LogSource.LogWarning(
                        "[Roles] Berserker sequence: multiple leader prefabs found; refusing fallback");
                    leader = null;
                    break;
                }
                leader = candidate;
            }
        }

        _leaderCacheHolderInstanceId = holderInstanceId;
        _leaderPrefab = leader;
        return leader;
    }

    private static Character ResolveEffectivePrefab(Character configured)
    {
        if (configured == null || configured.gameObject == null) return null;
        GameObject effectiveObject = BiomeData.GetAssetSwap<GameObject>(configured.gameObject);
        return effectiveObject != null ? effectiveObject.GetComponent<Character>() : null;
    }

    private static bool IsPrefabForTag(Character character, string expectedTag)
    {
        return character != null
            && character.gameObject != null
            && character.CompareTag(expectedTag);
    }

    private static bool MatchesExpectedResult(Character result, PromotionState state)
    {
        if (result == null || result.gameObject == null || !result.CompareTag(state.ExpectedTag))
            return false;

        string resultName = result.gameObject.name;
        return resultName == state.ExpectedPrefabName
            || resultName == state.ExpectedPrefabName + "(Clone)"
            || resultName.StartsWith(state.ExpectedPrefabName + " P", StringComparison.Ordinal)
            || resultName.StartsWith(state.ExpectedPrefabName + " [", StringComparison.Ordinal);
    }

    private static void Restore(PromotionState state)
    {
        if (state == null || !state.MappingApplied) return;
        state.MappingApplied = false;

        try
        {
            if (state.Holder != null && state.Holder.tagCharacterPairs != null)
                state.Holder.tagCharacterPairs["Berserker"] = state.OriginalBerserker;
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

}
