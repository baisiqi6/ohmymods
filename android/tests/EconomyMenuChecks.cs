using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MelonLoader;
using Config = KingdomEnhancedMod.ModConfig;

namespace OhMyMods.AndroidProbe.Tests
{
    // Executes the real preferences/menu sources. Only the panel and cached runtime
    // summary are host doubles; there is no game, native purchase, or saved-file claim.
    internal static class EconomyMenuChecks
    {
        private const string Category = "OhMyMods.Android";
        private static bool seeded;
        private static readonly int[] Loaded = { 0, 201, 17, -8, 199, 200, 1, 42, int.MaxValue };
        private static readonly (int Role, string Title, string Key,
            Func<MelonPreferences_Entry<bool>> Enabled, Func<MelonPreferences_Entry<int>> Target)[] Rows =
        {
            (0, "工匠", "Workers", () => Config.AutoRestockWorkersEnabled, () => Config.AutoRestockWorkersTarget),
            (1, "弓箭手", "Archers", () => Config.AutoRestockArchersEnabled, () => Config.AutoRestockArchersTarget),
            (8, "火枪手 · 火枪", "Musketeers", () => Config.AutoRestockMusketeersEnabled, () => Config.AutoRestockMusketeersTarget),
            (2, "忍者", "Ninjas", () => Config.AutoRestockNinjasEnabled, () => Config.AutoRestockNinjasTarget),
            (3, "狂战士", "Berserkers", () => Config.AutoRestockBerserkersEnabled, () => Config.AutoRestockBerserkersTarget),
            (4, "无业村民 · 面包", "Peasants", () => Config.AutoRestockPeasantsEnabled, () => Config.AutoRestockPeasantsTarget),
            (5, "农民 · 镰刀", "Farmers", () => Config.AutoRestockFarmersEnabled, () => Config.AutoRestockFarmersTarget),
            (6, "投石车 · 火药桶", "CatapultBarrels", () => Config.AutoRestockCatapultBarrelsEnabled, () => Config.AutoRestockCatapultBarrelsTarget),
            (7, "希腊火焰塔 · 弹药", "FireTowerAmmo", () => Config.AutoRestockFireTowerAmmoEnabled, () => Config.AutoRestockFireTowerAmmoTarget)
        };

        internal static bool Seed(string argument)
        {
            if (argument != "--seed-economy-targets") return false;
            seeded = true;
            for (int i = 0; i < Rows.Length; i++)
            {
                MelonPreferencesStub.Seed(Category, Rows[i].Key + "Enabled", i % 2 == 0);
                MelonPreferencesStub.Seed(Category, Rows[i].Key + "Target", Loaded[i]);
            }
            return true;
        }

        internal static void AfterInitialize()
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                var row = Rows[i];
                Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/" + row.Key + "Enabled default=False"),
                    "real economy boolean PC key/default: " + row.Key);
                Checks.Check(MelonPreferencesStub.CreatedEntries.Contains(Category + "/" + row.Key + "Target default=15"),
                    "real economy integer PC key/default: " + row.Key);
                Checks.Check(row.Enabled().Value == (seeded && i % 2 == 0), "economy loaded boolean: " + row.Key);
                Checks.Check(row.Target().Value == (seeded ? Math.Clamp(Loaded[i], 1, 200) : 15),
                    "economy default or bounded real loaded value: " + row.Key);
            }
            Checks.Check(!ReferenceEquals(Config.MusketeerEnabled, Config.AutoRestockMusketeersEnabled),
                "career switch and automatic musketeer restock are distinct entries");
        }

        internal static void RunMenu()
        {
            var panel = new MobileModPanel();
            var before = Snapshot();
            int saves = MelonPreferencesStub.SaveCalls, messages = MelonLogger.MessageCalls;
            KingdomEnhancedMod.PatchEconomy_AutoRestock.Reads.Clear();
            MobileEconomyMenu.Draw(panel, 320f, 1f);
            Checks.Check(panel.Rows.Count == 19, "nine toggle/quantity pairs and one return row");
            Checks.Check(before.SequenceEqual(Snapshot()) && MelonPreferencesStub.SaveCalls == saves
                && MelonLogger.MessageCalls == messages, "drawing the economy page neither normalizes nor saves/logs values");
            Checks.Check(KingdomEnhancedMod.PatchEconomy_AutoRestock.Reads.SequenceEqual(Rows.Select(row => row.Role)),
                "PC role order feeds the existing summary reader");
            for (int i = 0; i < Rows.Length; i++)
            {
                var expected = Rows[i];
                var toggle = panel.Rows[i * 2];
                var quantity = panel.Rows[i * 2 + 1];
                string unit = expected.Role == 6 || expected.Role == 7 ? "份" : "人";
                Checks.Check(toggle.Kind == "toggle" && toggle.Title == expected.Title + "自动补货"
                    && toggle.BoolValue == expected.Enabled().Value,
                    "economy toggle title and order: " + expected.Key);
                Checks.Check(quantity.Kind == "quantity" && quantity.Title == expected.Title + "目标（" + unit + "）"
                    && quantity.Value == expected.Target().Value, "economy quantity unit/value: " + expected.Key);
                Checks.Check(toggle.Help == "双倍金库付款 · summary:" + expected.Role && quantity.Help == toggle.Help,
                    "economy PC help passes through its cached summary: " + expected.Key);

                bool old = expected.Enabled().Value;
                before = Snapshot(); saves = MelonPreferencesStub.SaveCalls; messages = MelonLogger.MessageCalls;
                toggle.Action();
                var changed = Snapshot();
                Checks.Check(expected.Enabled().Value == !old && Changed(before, changed) == 1,
                    "toggle writes only its real entry: " + expected.Key);
                Checks.Check(MelonPreferencesStub.SaveCalls == saves + 1 && MelonLogger.MessageCalls == messages + 1,
                    "toggle saves/logs once: " + expected.Key);

                expected.Target().Value = 37;
                CheckAction(quantity.Increment, expected.Target, 38, 1, expected.Key + " plus");
                CheckAction(quantity.Decrement, expected.Target, 37, 1, expected.Key + " minus");
                expected.Target().Value = 1;
                CheckAction(quantity.Decrement, expected.Target, 1, 0, expected.Key + " minimum");
                expected.Target().Value = 200;
                CheckAction(quantity.Increment, expected.Target, 200, 0, expected.Key + " maximum");
            }
            panel.Rows[18].Action();
            Checks.Check(panel.Closed, "return row invokes the existing parent-world close action");

            // An action cached by a rendered row must follow the current field, not an old entry/value.
            var first = panel.Rows[1];
            var original = Config.AutoRestockWorkersTarget;
            Config.AutoRestockWorkersTarget = new MelonPreferences_Entry<int>(99);
            first.Increment();
            Checks.Check(Config.AutoRestockWorkersTarget.Value == 100 && original.Value == 200,
                "cached quantity action reads the current preference field");
            Config.AutoRestockWorkersTarget = original;
        }

        private static void CheckAction(Action action, Func<MelonPreferences_Entry<int>> entry,
            int value, int expectedSaves, string label)
        {
            var before = Snapshot();
            int saves = MelonPreferencesStub.SaveCalls, messages = MelonLogger.MessageCalls;
            action();
            Checks.Check(entry().Value == value && Changed(before, Snapshot()) == expectedSaves,
                "quantity changes only the chosen entry or stays at bound: " + label);
            Checks.Check(MelonPreferencesStub.SaveCalls == saves + expectedSaves
                && MelonLogger.MessageCalls == messages + expectedSaves, "quantity save/log count: " + label);
        }

        private static object[] Snapshot()
            => typeof(Config).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.FieldType.IsGenericType
                    && field.FieldType.GetGenericTypeDefinition() == typeof(MelonPreferences_Entry<>))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => field.FieldType.GetField("Value", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(field.GetValue(null))).ToArray();

        private static int Changed(object[] before, object[] after)
            => before.Zip(after, (left, right) => Equals(left, right) ? 0 : 1).Sum();
    }
}

namespace OhMyMods.AndroidProbe
{
    internal sealed class MobileModPanel
    {
        internal sealed class RecordedRow
        {
            internal string Kind, Title, Help;
            internal int Value;
            internal bool BoolValue;
            internal Action Action, Decrement, Increment;
        }
        internal readonly List<RecordedRow> Rows = new();
        internal bool Closed;
        internal Action CloseEconomy => () => Closed = true;
        internal void Toggle(string title, bool value, string help, Action action)
            => Rows.Add(new RecordedRow { Kind = "toggle", Title = title, Help = help, Action = action, BoolValue = value });
        internal void Quantity(string title, int value, string help, Action decrement, Action increment)
            => Rows.Add(new RecordedRow { Kind = "quantity", Title = title, Help = help, Value = value,
                Decrement = decrement, Increment = increment });
        internal void Step(string title, string value, string help, Action action)
            => Rows.Add(new RecordedRow { Kind = "step", Title = title, Help = help, Action = action });
    }
}

namespace KingdomEnhancedMod
{
    internal static class PatchEconomy_AutoRestock
    {
        internal static readonly List<int> Reads = new();
        internal static string GetSummary(int role) { Reads.Add(role); return "summary:" + role; }
    }
}
