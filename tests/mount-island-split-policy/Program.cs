using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// 纯策略测试：由 Tests.csproj 直接编译链接生产文件 il2cpp/Patch_MountIslandSplitPolicy.cs（不镜像实现）。
// 契约来源：用户 2026-10-09 双岛分组（docs/project-harness/tasks/mount-islands-split-20261009/plan.md 的 8+8 表）。
internal static class Program
{
    private static int _checks;
    private static readonly List<string> Failures = new List<string>();

    private static void Check(bool condition, string label)
    {
        _checks++;
        if (!condition) Failures.Add(label);
    }

    private static void CheckEq(int expected, int actual, string label) =>
        Check(expected == actual, label + $" (expected={expected} actual={actual})");

    private static int Main()
    {
        // A 组契约（北境与樱林 8 条）与 B 组契约（幽林与奇境 8 条）。
        string[] aIds =
        {
            "norselands.gullinbursti", "norselands.sleipnir", "norselands.reindeer", "norselands.catcart",
            "norselands.kelpie", "norselands.hrimfaxe", "norselands.wolf", "bamboo.kirin",
        };
        string[] bIds =
        {
            "woodlands.beetle", "woodlands.golem", "woodlands.gamigin", "woodlands.mansion",
            "swamp.eggsteed", "deadlands.grave", "santahouse.reindeer", "anniversary.rainbowpony",
        };

        // 1) 契约表 8+8、16 条互不重复。
        CheckEq(8, aIds.Length, "A 组条目数");
        CheckEq(8, bIds.Length, "B 组条目数");
        var union = new HashSet<string>();
        foreach (string id in aIds) Check(union.Add(id), "A 组重复 Id: " + id);
        foreach (string id in bIds) Check(union.Add(id), "B 组重复 Id: " + id);
        CheckEq(16, union.Count, "16 条并集唯一");

        // 2) 每条 Id 的 new-grant 目标岛：A→Primary、B→Secondary，逐条核对。
        foreach (string id in aIds)
            CheckEq(MountIslandSplitPolicy.PrimaryLand, MountIslandSplitPolicy.GetTargetLand(id), "A→Primary: " + id);
        foreach (string id in bIds)
            CheckEq(MountIslandSplitPolicy.SecondaryLand, MountIslandSplitPolicy.GetTargetLand(id), "B→Secondary: " + id);

        // 3) norselands.wolf：其 SteedType=13（catalog CrossWorldMountData.cs 字面量）与 SecondaryLand 同值，
        //    必须按 Id 归 Primary，不得按 int/SteedType 猜岛。
        CheckEq(MountIslandSplitPolicy.PrimaryLand, MountIslandSplitPolicy.GetTargetLand("norselands.wolf"), "wolf→Primary");
        Check(MountIslandSplitPolicy.GetTargetLand("norselands.wolf") != MountIslandSplitPolicy.SecondaryLand,
            "wolf 不因 SteedType 13 归 Secondary");

        // 4) 未知 / null / 大小写差异 / 空白 / 近似串 → -1（不得有默认岛）。
        string[] rejected =
        {
            null, "", " ", "Norselands.Wolf", "NORSELANDS.WOLF", "norselands.wolf ",
            " norselands.wolf", "norselands.wolfs", "bamboo.Kirin", "woodlands.Mansion",
            "norselands.", "greek.pegasus",
        };
        foreach (string probe in rejected)
            CheckEq(-1, MountIslandSplitPolicy.GetTargetLand(probe), "拒绝未知: '" + (probe ?? "<null>") + "'");

        // 5) IsExtensionLand：仅 11/13；原生 0..10、宫廷 12、负数与界外均 false。
        Check(MountIslandSplitPolicy.IsExtensionLand(11), "11 是扩展岛");
        Check(MountIslandSplitPolicy.IsExtensionLand(13), "13 是扩展岛");
        int[] nonExtension = { -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 15, 99 };
        foreach (int land in nonExtension)
            Check(!MountIslandSplitPolicy.IsExtensionLand(land), "非扩展岛: " + land);

        // 6) 物理→UI：仅 11→10、13→11；其余 false 且 ui=-1（原生 10→9 由原生自理）。
        Check(MountIslandSplitPolicy.TryGetMapIndex(11, out int ui11) && ui11 == 10, "物理 11 → UI 10");
        Check(MountIslandSplitPolicy.TryGetMapIndex(13, out int ui13) && ui13 == 11, "物理 13 → UI 11");
        int[] noMapIndex = { -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 14, 99 };
        foreach (int land in noMapIndex)
        {
            bool ok = MountIslandSplitPolicy.TryGetMapIndex(land, out int ui);
            Check(!ok && ui == -1, $"无 UI 映射: {land} (ok={ok} ui={ui})");
        }

        // 7) UI→物理：仅 10→11、11→13；其余 false 且 physical=-1（含原生 UI9 与数值 13）。
        Check(MountIslandSplitPolicy.TryGetPhysicalIndex(10, out int p10) && p10 == 11, "UI 10 → 物理 11");
        Check(MountIslandSplitPolicy.TryGetPhysicalIndex(11, out int p11) && p11 == 13, "UI 11 → 物理 13");
        int[] noPhysicalIndex = { -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 13, 14, 99 };
        foreach (int ui in noPhysicalIndex)
        {
            bool ok = MountIslandSplitPolicy.TryGetPhysicalIndex(ui, out int physical);
            Check(!ok && physical == -1, $"无物理映射: {ui} (ok={ok} physical={physical})");
        }

        // 8) 双向互逆：physical→ui→physical 与 ui→physical→ui 均回到原值。
        foreach (int land in new[] { 11, 13 })
            Check(MountIslandSplitPolicy.TryGetMapIndex(land, out int ui)
                  && MountIslandSplitPolicy.TryGetPhysicalIndex(ui, out int back)
                  && back == land, "互逆 physical→ui→physical: " + land);
        foreach (int ui in new[] { 10, 11 })
            Check(MountIslandSplitPolicy.TryGetPhysicalIndex(ui, out int physical)
                  && MountIslandSplitPolicy.TryGetMapIndex(physical, out int backUi)
                  && backUi == ui, "互逆 ui→physical→ui: " + ui);

        // 9) 常量契约（用字面量独立核对，不依赖生产常量自证）。
        CheckEq(11, MountIslandSplitPolicy.PrimaryLand, "PrimaryLand");
        CheckEq(13, MountIslandSplitPolicy.SecondaryLand, "SecondaryLand");
        CheckEq(14, MountIslandSplitPolicy.RequiredFileCapacity, "RequiredFileCapacity");
        CheckEq(10, MountIslandSplitPolicy.PrimaryUi, "PrimaryUi");
        CheckEq(11, MountIslandSplitPolicy.SecondaryUi, "SecondaryUi");
        Check(MountIslandSplitPolicy.RequiredFileCapacity >= MountIslandSplitPolicy.SecondaryLand + 1,
            "文件容量覆盖 0..13");
        Check(MountIslandSplitPolicy.PrimaryLand != MountIslandSplitPolicy.SecondaryLand, "两岛不同");
        Check(MountIslandSplitPolicy.PrimaryUi != MountIslandSplitPolicy.SecondaryUi, "两 UI 索引不同");

        // 同时编译实际 catalog，防止策略遗漏新增目录项或与目录 Id 拼写漂移。
        var catalogIds = new HashSet<string>();
        foreach (var definition in CrossWorldMountCatalog.Definitions)
        {
            Check(catalogIds.Add(definition.Id), "生产目录 Id 唯一: " + definition.Id);
            Check(union.Contains(definition.Id), "分组覆盖实际目录: " + definition.Id);
            Check(MountIslandSplitPolicy.GetTargetLand(definition.Id) == 11
                || MountIslandSplitPolicy.GetTargetLand(definition.Id) == 13,
                "实际目录有扩展目标: " + definition.Id);
        }
        Check(catalogIds.SetEquals(union), "分组并集精确等于生产目录");

        Console.WriteLine($"MountIslandSplitPolicy pure tests: checks={_checks} passed={_checks - Failures.Count} failed={Failures.Count}");
        foreach (string failure in Failures) Console.WriteLine("FAIL: " + failure);
        Console.WriteLine(Failures.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL");
        return Failures.Count == 0 ? 0 : 1;
    }
}
