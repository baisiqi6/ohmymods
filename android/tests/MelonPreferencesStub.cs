// Host-side double for the deployed MelonLoader 0.7.3 preferences surface used by
// MobilePlayerConfig / MobileCalendar (CreateCategory / Category.CreateEntry<T> /
// Entry<T>.Value / Category.SaveToFile). Shapes mirror the verified MelonLoader.dll
// metadata + IL (see the settings task recon: preferences-types.json and
// settings-implementation/evidence/melonprefs-*.json). It records the observable
// adapter contract only — category/entry names, declared defaults, seeded-value
// absorption on create, and the number of saves — and never touches a real
// UserData/MelonPreferences.cfg. This is a host double, not a device result.
namespace MelonLoader
{
    internal static class MelonPreferencesStub
    {
        internal static int SaveCalls;
        internal static readonly System.Collections.Generic.List<string> CreatedEntries = new System.Collections.Generic.List<string>();
        private static readonly System.Collections.Generic.Dictionary<string, object> Seeds = new System.Collections.Generic.Dictionary<string, object>();

        internal static void Seed(string category, string entry, object value)
            => Seeds[category + "/" + entry] = value;

        internal static bool TryGetSeed<T>(string category, string entry, out T value)
        {
            if (Seeds.TryGetValue(category + "/" + entry, out object stored) && stored is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }
    }

    internal static class MelonPreferences
    {
        private static readonly System.Collections.Generic.Dictionary<string, MelonPreferences_Category> Categories = new System.Collections.Generic.Dictionary<string, MelonPreferences_Category>();

        internal static MelonPreferences_Category CreateCategory(string identifier)
        {
            if (!Categories.TryGetValue(identifier, out MelonPreferences_Category category))
            {
                category = new MelonPreferences_Category(identifier);
                Categories.Add(identifier, category);
            }
            return category;
        }
    }

    internal sealed class MelonPreferences_Category
    {
        internal readonly string Identifier;

        internal MelonPreferences_Category(string identifier) { Identifier = identifier; }

        internal MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T default_value,
            string display_name = null, string description = null, bool is_hidden = false,
            bool dont_save_default = false, object validator = null)
        {
            MelonPreferencesStub.CreatedEntries.Add(Identifier + "/" + identifier + " default=" + default_value);
            T value = MelonPreferencesStub.TryGetSeed<T>(Identifier, identifier, out T seeded) ? seeded : default_value;
            return new MelonPreferences_Entry<T>(value);
        }

        internal void SaveToFile(bool printmsg = true) => MelonPreferencesStub.SaveCalls++;
    }

    internal sealed class MelonPreferences_Entry<T>
    {
        internal T Value;

        internal MelonPreferences_Entry(T value) { Value = value; }
    }
}
