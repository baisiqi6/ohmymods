#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PrivateBankR3;

public enum BankCategory { Normal, Challenge }
public enum SaveDocumentStatus { Rejected, NoKey, Ready }

public readonly struct BankCatalogEntry
{
    public readonly nint Pointer;
    public readonly BankCategory Category;
    public readonly int Ordinal;
    public BankCatalogEntry(nint pointer, BankCategory category, int ordinal)
    {
        Pointer = pointer;
        Category = category;
        Ordinal = ordinal;
    }
}

public readonly struct BankCapture
{
    public readonly nint Pointer;
    public readonly int Coins;
    public BankCapture(nint pointer, int coins)
    {
        Pointer = pointer;
        Coins = coins;
    }
}

// One instance belongs to one real loaded Global. The adapter owns native wrapper lifetime.
public sealed class SharedBankState
{
    private sealed class Account
    {
        public int? Live;
        public int? Staged;
    }

    private readonly object _owner;
    private Dictionary<nint, BankCatalogEntry> _catalog = new();
    private Dictionary<nint, Account> _accounts = new();
    private readonly HashSet<(int Land, nint Account)> _unresolved = new();
    private bool _bound;
    private bool _corrupt;
    private bool _keyMayExist;
    private string _lastBuilt;

    public SharedBankState(object owner) => _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    public bool IsCorrupt => _corrupt;
    private bool Active(object owner) => ReferenceEquals(owner, _owner) && _bound && !_corrupt;

    public bool BindOnce(object owner, IReadOnlyList<BankCatalogEntry> catalog, string document)
    {
        if (!ReferenceEquals(owner, _owner) || _bound) return false;
        _bound = true; // A missing key also consumes the only import opportunity.
        if (!ValidateCatalog(catalog, null, out var next)) return Freeze();
        _keyMayExist = document is not null;
        var imported = new Dictionary<nint, int>();
        if (document is not null && !BankDocument.TryParse(document, next.Values, imported)) return Freeze();
        _catalog = next;
        _accounts = next.Keys.ToDictionary(p => p, p => new Account
        {
            Live = imported.TryGetValue(p, out var value) ? value : null,
            Staged = imported.TryGetValue(p, out value) ? value : null
        });
        return true;
    }

    public bool ReconcileCatalog(object owner, IReadOnlyList<BankCatalogEntry> catalog)
    {
        if (!Active(owner) || !ValidateCatalog(catalog, _catalog, out var next)) return false;
        // Validate every entry before changing any account or the catalog.
        var accounts = new Dictionary<nint, Account>();
        foreach (var pointer in next.Keys)
            accounts.Add(pointer, _accounts.TryGetValue(pointer, out var old) ? old : new Account());
        _catalog = next;
        _accounts = accounts;
        _unresolved.RemoveWhere(source => !next.ContainsKey(source.Account));
        _lastBuilt = null;
        return true;
    }

    public bool ObserveLive(object owner, nint pointer, int coins)
    {
        if (!Active(owner) || coins < 0 || !_accounts.TryGetValue(pointer, out var account)) return false;
        account.Live = coins;
        return true;
    }

    public bool TryReadLive(object owner, nint pointer, out int coins)
    {
        coins = default;
        if (!Active(owner) || !_accounts.TryGetValue(pointer, out var account) || account.Live is not int value)
            return false;
        coins = value;
        return true;
    }

    public bool HasUncapturedBalance(object owner, nint pointer)
        => Active(owner) && _accounts.TryGetValue(pointer, out var account)
            && account.Live.HasValue && account.Live != account.Staged;

    public bool CaptureFailed(object owner, int sourceLand, nint pointer)
    {
        if (!Active(owner) || sourceLand < 0 || !_accounts.ContainsKey(pointer)) return false;
        _unresolved.Add((sourceLand, pointer));
        _lastBuilt = null;
        return true;
    }

    public bool CaptureSucceeded(object owner, int sourceLand, IReadOnlyList<BankCapture> captured,
        bool clearSourceFault = true)
    {
        if (!Active(owner) || sourceLand < 0 || captured is null) return false;
        var seen = new HashSet<nint>();
        foreach (var entry in captured)
            if (entry.Coins < 0 || !_accounts.ContainsKey(entry.Pointer) || !seen.Add(entry.Pointer))
                return false;
        foreach (var entry in captured)
        {
            _accounts[entry.Pointer].Staged = entry.Coins;
            if (clearSourceFault) _unresolved.Remove((sourceLand, entry.Pointer));
        }
        _lastBuilt = null;
        return true;
    }

    public SaveDocumentStatus BuildSaveDocument(object owner, out string document)
    {
        document = null;
        if (!Active(owner) || _unresolved.Count != 0)
        {
            _lastBuilt = null;
            return SaveDocumentStatus.Rejected;
        }
        var records = _catalog.Values
            .Where(entry => _accounts[entry.Pointer].Staged.HasValue)
            .OrderBy(entry => entry.Category).ThenBy(entry => entry.Ordinal)
            .Select(entry => (entry.Category, entry.Ordinal, _accounts[entry.Pointer].Staged!.Value))
            .ToArray();
        if (records.Length == 0 && !_keyMayExist)
        {
            _lastBuilt = null;
            return SaveDocumentStatus.NoKey;
        }
        document = BankDocument.Write(records);
        _lastBuilt = document;
        // Set may succeed even if readback fails; later deletion must clear that possible key.
        _keyMayExist = true;
        return SaveDocumentStatus.Ready;
    }

    // Call only after the adapter's set and readback both succeeded.
    public bool MarkWriteVerified(object owner, string document)
    {
        if (!Active(owner) || _lastBuilt is null || document != _lastBuilt) return false;
        _keyMayExist = true;
        _lastBuilt = null;
        return true;
    }

    private bool Freeze()
    {
        _corrupt = true;
        _catalog.Clear();
        _accounts.Clear();
        return false;
    }

    private static bool ValidateCatalog(
        IReadOnlyList<BankCatalogEntry> entries,
        Dictionary<nint, BankCatalogEntry> previous,
        out Dictionary<nint, BankCatalogEntry> result)
    {
        result = new();
        if (entries is null) return false;
        var slots = new HashSet<(BankCategory, int)>();
        foreach (var entry in entries)
        {
            if (entry.Pointer == 0 || !Enum.IsDefined(entry.Category) || entry.Ordinal < 0 ||
                !result.TryAdd(entry.Pointer, entry) || !slots.Add((entry.Category, entry.Ordinal)) ||
                (previous is not null && previous.TryGetValue(entry.Pointer, out var old) && old.Category != entry.Category))
                return false;
        }
        return true;
    }
}

internal static class BankDocument
{
    internal static bool TryParse(string source, IEnumerable<BankCatalogEntry> catalog, Dictionary<nint, int> imported)
    {
        try
        {
            using var document = JsonDocument.Parse(source);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Fields(root, "version", "records") ||
                !Int(root.GetProperty("version"), out var version) || version != 1 ||
                root.GetProperty("records").ValueKind != JsonValueKind.Array) return false;
            var bySlot = catalog.ToDictionary(e => (e.Category, e.Ordinal), e => e.Pointer);
            var seen = new HashSet<(BankCategory, int)>();
            foreach (var record in root.GetProperty("records").EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object || !Fields(record, "category", "ordinal", "coins"))
                    return false;
                var rawCategory = record.GetProperty("category");
                if (rawCategory.ValueKind != JsonValueKind.String ||
                    !TryCategory(rawCategory.GetString(), out var category) ||
                    !Int(record.GetProperty("ordinal"), out var ordinal) || ordinal < 0 ||
                    !Int(record.GetProperty("coins"), out var coins) || coins < 0 ||
                    !seen.Add((category, ordinal)) || !bySlot.TryGetValue((category, ordinal), out var pointer))
                    return false;
                imported.Add(pointer, coins);
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    internal static string Write((BankCategory Category, int Ordinal, int Coins)[] records)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("records");
            foreach (var record in records)
            {
                writer.WriteStartObject();
                writer.WriteString("category", record.Category == BankCategory.Normal ? "normal" : "challenge");
                writer.WriteNumber("ordinal", record.Ordinal);
                writer.WriteNumber("coins", record.Coins);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool Int(JsonElement element, out int value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) &&
            element.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) < 0;
    }

    private static bool TryCategory(string source, out BankCategory category)
    {
        category = source == "challenge" ? BankCategory.Challenge : BankCategory.Normal;
        return source is "normal" or "challenge";
    }

    private static bool Fields(JsonElement element, params string[] required)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
            if (!seen.Add(field.Name) || Array.IndexOf(required, field.Name) < 0) return false;
        return seen.Count == required.Length;
    }
}
