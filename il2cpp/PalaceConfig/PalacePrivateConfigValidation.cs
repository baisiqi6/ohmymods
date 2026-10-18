using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod.PalaceSource
{
    /// <summary>
    /// D1 私有 land12 配置字段/组数只读验证（冻结 S3 `ValidatePrivateConfig` 的具名抽取）
    /// 与模板 19 组 GetCount 建前/建后核验。
    ///
    /// SOURCE-only：只读 clone/template 的具名字段与 GetCount；异常 fail-closed（返回 false）。
    /// 通过只证明数据形状，不表示 Ready / native 资格 / 可生成；GetCount 内部缓存行为不作承诺。
    /// </summary>
    internal static class PalacePrivateConfigValidation
    {
        /// <summary>
        /// clone 具名字段 + 19 组 Intent GetCount + 全部实际 groupCounts 行的零/一复核。
        /// 任何异常 => false。不调用 setter；GetCount 的内部缓存行为仍未证明。
        /// </summary>
        internal static bool ValidatePrivateConfig(LevelConfig clone, List<string> parts, out bool ok)
        {
            bool valid = true;
            void Require(bool condition, string message)
            {
                if (!condition) { valid = false; parts.Add(message); }
            }
            try
            {
                Require((clone.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0, "process-owned asset lifetime");
                Require(clone.name == PalacePrivateConfigPlan.PrivateConfigName, "name");
                Require(clone.blockName == PalacePrivateConfigPlan.BlockName, "blockName=" + clone.blockName);
                Require(clone.minLevelWidth == PalacePrivateConfigPlan.MinLevelWidthCandidate, "minLevelWidth");
                Require(clone.questType == QuestType.None, "questType=" + clone.questType);
                Require(clone.islandMonumentID == -1, "monument=" + clone.islandMonumentID);
                Require(clone.caveless, "caveless");
                Require(!clone.twoCliffs, "twoCliffs");
                Require(!clone.randomizeCliffSide, "randomizeCliffSide");
                Require(clone.sharedBlocksConfigs != null && clone.sharedBlocksConfigs.Length == 0, "sharedBlocksConfigs");
                Require(clone.groupCounts != null && clone.groupCounts.Count > 0, "groupCounts empty");

                for (int i = 0; i < PalacePrivateConfigPlan.Intent.Length; i++)
                {
                    PalacePrivateConfigPlan.GroupIntent intent = PalacePrivateConfigPlan.Intent[i];
                    IntRange count = clone.GetCount((LevelBlockGroup)intent.Group);
                    Require(count.min == intent.Min && count.max == intent.Max,
                        "GetCount(" + intent.Group + ")=[" + count.min + "," + count.max + "]");
                }
                var rowGroups = new HashSet<int>();
                for (int i = 0; i < clone.groupCounts.Count; i++)
                {
                    LevelGroupCount entry = clone.groupCounts[i];
                    if (entry == null) { valid = false; parts.Add("null groupCounts entry"); continue; }
                    int group = (int)entry.group;
                    Require(rowGroups.Add(group), "duplicate entry(" + group + ")");
                    int min = entry.count.min, max = entry.count.max;
                    if (group == PalacePrivateConfigPlan.RequiredStartingAreaGroup
                        || group == PalacePrivateConfigPlan.RequiredPlayerSpawnGroup)
                        Require(min == 1 && max == 1, "entry(" + group + ")=[" + min + "," + max + "]");
                    else
                        Require(min == 0 && max == 0, "nonzero entry(" + group + ")=[" + min + "," + max + "]");
                }
                // Effective GetCount readback alone cannot witness the independently built
                // recipe list. Check actual row membership without resetting native caches.
                for (int i = 0; i < PalacePrivateConfigPlan.Intent.Length; i++)
                    Require(rowGroups.Contains(PalacePrivateConfigPlan.Intent[i].Group),
                        "missing entry(" + PalacePrivateConfigPlan.Intent[i].Group + ")");
                ok = valid;
                parts.Insert(0, valid ? "validated" : "invalid");
                return valid;
            }
            catch (Exception e)
            {
                ok = false;
                parts.Add("validate-exception:" + e.GetType().Name + " " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 读取 template 的 19 组 Intent GetCount 快照到调用方缓冲区（组序与 Intent 一致，每项 min/max）。
        /// 读失败/异常 => false（调用方在 Instantiate 之前 fail-closed）。
        /// </summary>
        internal static bool TryCaptureIntentCounts(LevelConfig config, int[] counts, out string error)
        {
            error = null;
            if (ReferenceEquals(config, null)) { error = "template null"; return false; }
            if (counts == null || counts.Length < PalacePrivateConfigPlan.Intent.Length * 2)
            { error = "count buffer too small"; return false; }
            try
            {
                for (int i = 0; i < PalacePrivateConfigPlan.Intent.Length; i++)
                {
                    IntRange range = config.GetCount((LevelBlockGroup)PalacePrivateConfigPlan.Intent[i].Group);
                    counts[i * 2] = range.min;
                    counts[i * 2 + 1] = range.max;
                }
                return true;
            }
            catch (Exception e)
            {
                error = "counts-exception:" + e.GetType().Name + " " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// 建后复核：template 的 19 组 min/max 与建前快照一致。只覆盖这 19 组 GetCount 读回，
        /// 不声称 template 全部 native 内存逐字节不变。不一致/异常 => false。
        /// </summary>
        internal static bool IntentCountsUnchanged(LevelConfig config, int[] before, out string error)
        {
            error = null;
            if (ReferenceEquals(config, null)) { error = "template null"; return false; }
            if (before == null || before.Length < PalacePrivateConfigPlan.Intent.Length * 2)
            { error = "count buffer too small"; return false; }
            try
            {
                for (int i = 0; i < PalacePrivateConfigPlan.Intent.Length; i++)
                {
                    IntRange after = config.GetCount((LevelBlockGroup)PalacePrivateConfigPlan.Intent[i].Group);
                    if (after.min != before[i * 2] || after.max != before[i * 2 + 1])
                    {
                        error = "template count changed for group " + PalacePrivateConfigPlan.Intent[i].Group;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                error = "counts-exception:" + e.GetType().Name + " " + e.Message;
                return false;
            }
        }
    }
}
