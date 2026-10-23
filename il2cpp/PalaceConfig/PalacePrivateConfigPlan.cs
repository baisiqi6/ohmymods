namespace KingdomEnhancedMod.PalaceSource
{
    /// <summary>
    /// D1 私有 land12 配置配方（从冻结 S3 `PalaceService12Plan` 抽取的具名最小集）。
    ///
    /// SOURCE-only：只包含模板名/私有名/blockName/宽度候选与 19 组 Intent 纯查询。
    /// 不包含旧 root/path/Ready/capacity 写入、旧 attempt 门、内容候选闭包或任何 native 资格。
    /// MinLevelWidthCandidate=48 是既有候选宽度，不是最终地图尺寸或已证原生最小值。
    /// group 2/8 为 [1,1]，其余 intent 组与未知组为 [0,0]。
    /// </summary>
    internal static class PalacePrivateConfigPlan
    {
        internal const string TemplateAssetName = "Greece_Land_God_Artemis";
        internal const string PrivateConfigName = "Greece_Land_Palace_P12_Service";
        internal const string BlockName = "blocks_greece";

        /// <summary>R3-identical clone width (experimental candidate, not a proven native minimum).</summary>
        internal const int MinLevelWidthCandidate = 48;

        /// <summary>Required generation groups for a minimal peaceful ground.</summary>
        internal const int RequiredStartingAreaGroup = 2;   // StartingAreaTight
        internal const int RequiredPlayerSpawnGroup = 8;    // PlayerSpawn

        /// <summary>One fixed native group quota (min==max for every palace group).</summary>
        internal readonly struct GroupIntent
        {
            internal readonly int Group;
            internal readonly int Min;
            internal readonly int Max;

            internal GroupIntent(int group, int min, int max)
            {
                Group = group;
                Min = min;
                Max = max;
            }
        }

        /// <summary>R3-identical intent table: only generic ground/spawn, everything else hard-zero.</summary>
        internal static readonly GroupIntent[] Intent =
        {
            new GroupIntent(2, 1, 1),    // StartingAreaTight: one native ground base
            new GroupIntent(8, 1, 1),    // PlayerSpawn: one
            new GroupIntent(3, 0, 0),    // Forest
            new GroupIntent(4, 0, 0),    // Bridge
            new GroupIntent(5, 0, 0),    // Clearing
            new GroupIntent(14, 0, 0),   // Chest (no god chest)
            new GroupIntent(15, 0, 0),   // BeggarCamp
            new GroupIntent(6, 0, 0),    // Portal: no Greed portals, no travel gates
            new GroupIntent(18, 0, 0),   // BeachLeft
            new GroupIntent(19, 0, 0),   // BeachRight
            new GroupIntent(20, 0, 0),   // CliffLeft
            new GroupIntent(21, 0, 0),   // CliffRight (carries cave entry)
            new GroupIntent(30, 0, 0),   // EndLeft
            new GroupIntent(31, 0, 0),   // EndRight
            new GroupIntent(32, 0, 0),   // Cliff
            new GroupIntent(33, 0, 0),   // Beach
            new GroupIntent(54, 0, 0),   // CaveSection
            new GroupIntent(55, 0, 0),   // CaveEndRight
            new GroupIntent(56, 0, 0),   // CaveEndLeft
        };

        /// <summary>Intent lookup: first exact group wins; absent intent => [0,0] with false.</summary>
        internal static bool TryGetIntent(int group, out int min, out int max)
        {
            for (int i = 0; i < Intent.Length; i++)
            {
                if (Intent[i].Group != group) continue;
                min = Intent[i].Min;
                max = Intent[i].Max;
                return true;
            }
            min = 0;
            max = 0;
            return false;
        }
    }
}
