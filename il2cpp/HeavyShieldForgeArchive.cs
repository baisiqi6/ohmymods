#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KingdomEnhancedMod;

// One sidecar may hold several save lineages. The game bridge supplies a stable
// campaign/save lineage key; island, runtime object IDs and session timestamps are
// insufficient. This class never initiates or counts native gem payments.
internal sealed class HeavyShieldForgeArchive
{
    internal const int Version = 3;
    internal const int GemPrice = 4;
    internal const int ExtraSeatGemPrice = 2;
    internal const int MaxEntries = 512;
    internal const int MaxBytes = 256 * 1024;
    internal const int MaxKeyLength = 512;

    internal enum LookupResult { Locked, Unlocked, InvalidKey, Corrupt, Unsupported, IoError }
    internal enum RecordResult
    {
        Saved, AlreadyUnlocked, InvalidKey, Unavailable, SaveFailed,
        ForgeLocked, InvalidReceipt, ReceiptAlreadyUsed, SameReceiptAlreadySaved
    }
    internal enum Side { Left, Right }

    private readonly string _path;

    // The caller explicitly chooses the absolute sidecar path. No game or BepInEx
    // directory is inferred here, so tests cannot accidentally touch player data.
    internal HeavyShieldForgeArchive(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
            throw new ArgumentException("An absolute sidecar path is required.", nameof(absolutePath));
        _path = Path.GetFullPath(absolutePath);
    }

    internal LookupResult Lookup(string lineageKey) => LookupCore(lineageKey, null);

    // The base seat per side is implicit after the forge is unlocked.
    internal LookupResult LookupExtraSeat(string lineageKey, Side side)
    {
        if (!ValidSide(side)) return LookupResult.InvalidKey;
        return LookupCore(lineageKey, side);
    }

    // Restores in-memory receipt deduplication after restart. Unlocked means
    // the mold exists; a null output means that side's extra seat is unbought.
    // Errors leave both outputs null and must fail closed in the caller.
    internal LookupResult TryGetSeatReceipts(string lineageKey,
        out string leftReceipt, out string rightReceipt)
    {
        leftReceipt = null;
        rightReceipt = null;
        if (!ValidKey(lineageKey)) return LookupResult.InvalidKey;
        var read = Read();
        if (read.State == ReadState.Missing) return LookupResult.Locked;
        if (read.State == ReadState.Unsupported) return LookupResult.Unsupported;
        if (read.State == ReadState.Corrupt) return LookupResult.Corrupt;
        if (read.State != ReadState.Valid) return LookupResult.IoError;
        if (!read.Keys.Contains(lineageKey)) return LookupResult.Locked;
        foreach (var purchase in read.Purchases)
        {
            if (purchase.Lineage != lineageKey) continue;
            if (purchase.Side == Side.Left) leftReceipt = purchase.Receipt;
            else rightReceipt = purchase.Receipt;
        }
        return LookupResult.Unlocked;
    }

    // A migrated v1/v2 forge has no known receipt. Never invent provenance:
    // return Unlocked with null, so the caller restores the mold but does not
    // seed an unverified spent-payment token.
    internal LookupResult TryGetMoldReceipt(string lineageKey, out string moldReceipt)
    {
        moldReceipt = null;
        if (!ValidKey(lineageKey)) return LookupResult.InvalidKey;
        var read = Read();
        if (read.State == ReadState.Missing) return LookupResult.Locked;
        if (read.State == ReadState.Unsupported) return LookupResult.Unsupported;
        if (read.State == ReadState.Corrupt) return LookupResult.Corrupt;
        if (read.State != ReadState.Valid) return LookupResult.IoError;
        if (!read.Keys.Contains(lineageKey)) return LookupResult.Locked;
        moldReceipt = read.MoldPurchases.FirstOrDefault(x => x.Lineage == lineageKey)?.Receipt;
        return LookupResult.Unlocked;
    }

    private LookupResult LookupCore(string lineageKey, Side? side)
    {
        if (!ValidKey(lineageKey)) return LookupResult.InvalidKey;
        var read = Read();
        return read.State switch
        {
            ReadState.Missing => LookupResult.Locked,
            ReadState.Valid => side.HasValue
                ? read.Purchases.Any(x => x.Lineage == lineageKey && x.Side == side.Value)
                    ? LookupResult.Unlocked : LookupResult.Locked
                : read.Keys.Contains(lineageKey) ? LookupResult.Unlocked : LookupResult.Locked,
            ReadState.Unsupported => LookupResult.Unsupported,
            ReadState.Corrupt => LookupResult.Corrupt,
            _ => LookupResult.IoError
        };
    }

    // Call only after a single native Price=4 Gems payment has completed.
    // Reuse its GUID across retries. SameReceiptAlreadySaved is the same
    // payment already persisted; AlreadyUnlocked is a distinct charge, also
    // returned for legacy unlocks with unknown provenance. The bridge owns
    // native cancellation/refund. No partial 1-3-gem progress is stored.
    internal RecordResult RecordAfterCompletedPayment(string lineageKey, string paymentReceiptId)
    {
        if (!ValidKey(lineageKey)) return RecordResult.InvalidKey;
        if (!CanonicalReceipt(paymentReceiptId, out string receipt)) return RecordResult.InvalidReceipt;
        return Mutate(read =>
        {
            var priorMold = read.MoldPurchases.FirstOrDefault(x => x.Receipt == receipt);
            if (priorMold != null)
                return priorMold.Lineage == lineageKey
                    ? RecordResult.SameReceiptAlreadySaved : RecordResult.ReceiptAlreadyUsed;
            if (read.Purchases.Any(x => x.Receipt == receipt)) return RecordResult.ReceiptAlreadyUsed;
            if (read.Keys.Contains(lineageKey)) return RecordResult.AlreadyUnlocked;
            if (read.Keys.Count >= MaxEntries) return RecordResult.Unavailable;
            read.Keys.Add(lineageKey);
            read.MoldPurchases.Add(new MoldPurchase(lineageKey, receipt));
            return RecordResult.Saved;
        });
    }

    // Mint one GUID when a native 2-Gem payment completes and retain it on
    // retries. The persisted receipt prevents that payment from buying both
    // sides, even through separate archive instances or across restarts.
    // SameReceiptAlreadySaved is an idempotent retry. AlreadyUnlocked means a
    // distinct new charge for a full seat and must be refunded by the bridge.
    internal RecordResult RecordExtraSeatAfterCompletedPayment(
        string lineageKey, Side side, string paymentReceiptId)
    {
        if (!ValidKey(lineageKey) || !ValidSide(side)) return RecordResult.InvalidKey;
        if (!CanonicalReceipt(paymentReceiptId, out string receipt)) return RecordResult.InvalidReceipt;
        return Mutate(read =>
        {
            var priorReceipt = read.Purchases.FirstOrDefault(x => x.Receipt == receipt);
            if (priorReceipt != null)
                return priorReceipt.Lineage == lineageKey && priorReceipt.Side == side
                    ? RecordResult.SameReceiptAlreadySaved : RecordResult.ReceiptAlreadyUsed;
            if (read.MoldPurchases.Any(x => x.Receipt == receipt)) return RecordResult.ReceiptAlreadyUsed;
            if (!read.Keys.Contains(lineageKey)) return RecordResult.ForgeLocked;
            if (read.Purchases.Any(x => x.Lineage == lineageKey && x.Side == side))
                return RecordResult.AlreadyUnlocked;
            if (read.Purchases.Count >= MaxEntries * 2) return RecordResult.Unavailable;
            read.Purchases.Add(new SeatPurchase(lineageKey, side, receipt));
            return RecordResult.Saved;
        });
    }

    private RecordResult Mutate(Func<ReadResult, RecordResult> change)
    {
        string temporary = null;
        try
        {
            string directory = Path.GetDirectoryName(_path);
            if (directory == null) return RecordResult.SaveFailed;
            Directory.CreateDirectory(directory);
            // Serializes independent instances/processes of this component. A
            // lock failure is reported to the caller; it never grants access.
            using var writeLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var read = Read();
            if (read.State == ReadState.Corrupt || read.State == ReadState.Unsupported
                || read.State == ReadState.IoError)
                return RecordResult.Unavailable;
            var result = change(read);
            if (result != RecordResult.Saved) return result;
            byte[] bytes = Encode(read);
            if (bytes.Length > MaxBytes) return RecordResult.Unavailable;
            temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
            using (var file = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (read.State == ReadState.Missing) File.Move(temporary, _path);
            else File.Replace(temporary, _path, null, true);
            temporary = null;
            return RecordResult.Saved;
        }
        catch
        {
            return RecordResult.SaveFailed;
        }
        finally
        {
            if (temporary != null) { try { File.Delete(temporary); } catch { } }
        }
    }

    private enum ReadState { Missing, Valid, Corrupt, Unsupported, IoError }
    private sealed class MoldPurchase
    {
        internal readonly string Lineage;
        internal readonly string Receipt;
        internal MoldPurchase(string lineage, string receipt)
        { Lineage = lineage; Receipt = receipt; }
    }
    private sealed class SeatPurchase
    {
        internal readonly string Lineage;
        internal readonly Side Side;
        internal readonly string Receipt;
        internal SeatPurchase(string lineage, Side side, string receipt)
        { Lineage = lineage; Side = side; Receipt = receipt; }
    }
    private sealed class ReadResult
    {
        internal ReadState State;
        internal HashSet<string> Keys = new(StringComparer.Ordinal);
        internal List<SeatPurchase> Purchases = new();
        internal List<MoldPurchase> MoldPurchases = new();
    }

    private ReadResult Read()
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length < 2 || file.Length > MaxBytes) return new() { State = ReadState.Corrupt };
            byte[] bytes = new byte[(int)file.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = file.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) return new() { State = ReadState.Corrupt };
                offset += count;
            }
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new() { State = ReadState.Corrupt };
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in root.EnumerateObject())
                if (!fields.TryAdd(field.Name, field.Value)) return new() { State = ReadState.Corrupt };
            if (!fields.TryGetValue("version", out var version)
                || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number))
                return new() { State = ReadState.Corrupt };
            if (number != 1 && number != 2 && number != Version) return new() { State = ReadState.Unsupported };
            int expectedFields = number == 1 ? 2 : number == 2 ? 3 : 4;
            if (fields.Count != expectedFields || !fields.TryGetValue("unlocked", out var unlocked))
                return new() { State = ReadState.Corrupt };
            if (unlocked.ValueKind != JsonValueKind.Array || unlocked.GetArrayLength() > MaxEntries)
                return new() { State = ReadState.Corrupt };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in unlocked.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return new() { State = ReadState.Corrupt };
                string key = item.GetString();
                if (!ValidKey(key) || !keys.Add(key)) return new() { State = ReadState.Corrupt };
            }
            var purchases = new List<SeatPurchase>();
            var moldPurchases = new List<MoldPurchase>();
            var usedReceipts = new HashSet<string>(StringComparer.Ordinal);
            if (number >= 2)
            {
                if (!fields.TryGetValue("seatPurchases", out var stored)
                    || stored.ValueKind != JsonValueKind.Array || stored.GetArrayLength() > MaxEntries * 2)
                    return new() { State = ReadState.Corrupt };
                var usedSeats = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in stored.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) return new() { State = ReadState.Corrupt };
                    var purchaseFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var property in item.EnumerateObject())
                        if (!purchaseFields.TryAdd(property.Name, property.Value))
                            return new() { State = ReadState.Corrupt };
                    if (purchaseFields.Count != 3
                        || !purchaseFields.TryGetValue("lineage", out var lineageElement)
                        || !purchaseFields.TryGetValue("side", out var sideElement)
                        || !purchaseFields.TryGetValue("receipt", out var receiptElement)
                        || lineageElement.ValueKind != JsonValueKind.String
                        || sideElement.ValueKind != JsonValueKind.String
                        || receiptElement.ValueKind != JsonValueKind.String)
                        return new() { State = ReadState.Corrupt };
                    string lineage = lineageElement.GetString();
                    string sideName = sideElement.GetString();
                    string receiptString = receiptElement.GetString();
                    if (!ValidKey(lineage) || !keys.Contains(lineage)
                        || !TryParseSide(sideName, out Side side)
                        || !CanonicalReceipt(receiptString, out string receipt)
                        || receiptString != receipt
                        || !usedReceipts.Add(receipt)
                        || !usedSeats.Add(lineage + "\0" + sideName))
                        return new() { State = ReadState.Corrupt };
                    purchases.Add(new SeatPurchase(lineage, side, receipt));
                }
            }
            if (number == Version)
            {
                if (!fields.TryGetValue("moldPurchases", out var stored)
                    || stored.ValueKind != JsonValueKind.Array || stored.GetArrayLength() > MaxEntries)
                    return new() { State = ReadState.Corrupt };
                var usedMolds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in stored.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) return new() { State = ReadState.Corrupt };
                    var purchaseFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var property in item.EnumerateObject())
                        if (!purchaseFields.TryAdd(property.Name, property.Value))
                            return new() { State = ReadState.Corrupt };
                    if (purchaseFields.Count != 2
                        || !purchaseFields.TryGetValue("lineage", out var lineageElement)
                        || !purchaseFields.TryGetValue("receipt", out var receiptElement)
                        || lineageElement.ValueKind != JsonValueKind.String
                        || receiptElement.ValueKind != JsonValueKind.String)
                        return new() { State = ReadState.Corrupt };
                    string lineage = lineageElement.GetString();
                    string receiptString = receiptElement.GetString();
                    if (!ValidKey(lineage) || !keys.Contains(lineage)
                        || !CanonicalReceipt(receiptString, out string receipt)
                        || receiptString != receipt
                        || !usedMolds.Add(lineage)
                        || !usedReceipts.Add(receipt))
                        return new() { State = ReadState.Corrupt };
                    moldPurchases.Add(new MoldPurchase(lineage, receipt));
                }
            }
            return new() { State = ReadState.Valid, Keys = keys,
                Purchases = purchases, MoldPurchases = moldPurchases };
        }
        catch (FileNotFoundException) { return new() { State = ReadState.Missing }; }
        catch (DirectoryNotFoundException) { return new() { State = ReadState.Missing }; }
        catch (JsonException) { return new() { State = ReadState.Corrupt }; }
        catch (IOException) { return new() { State = ReadState.IoError }; }
        catch (UnauthorizedAccessException) { return new() { State = ReadState.IoError }; }
    }

    private static bool ValidKey(string key) => key != null && key.Length > 0
        && key.Length <= MaxKeyLength && !string.IsNullOrWhiteSpace(key)
        && !key.Any(char.IsControl);

    private static bool ValidSide(Side side) => side == Side.Left || side == Side.Right;
    private static bool TryParseSide(string text, out Side side)
    {
        side = Side.Left;
        if (text == "left") return true;
        if (text == "right") { side = Side.Right; return true; }
        return false;
    }
    private static bool CanonicalReceipt(string value, out string canonical)
    {
        canonical = null;
        if (!Guid.TryParseExact(value, "D", out Guid guid) || guid == Guid.Empty) return false;
        canonical = guid.ToString("D");
        return true;
    }

    private static byte[] Encode(ReadResult read)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("version", Version);
            json.WritePropertyName("unlocked");
            json.WriteStartArray();
            foreach (string key in read.Keys.OrderBy(x => x, StringComparer.Ordinal)) json.WriteStringValue(key);
            json.WriteEndArray();
            json.WritePropertyName("seatPurchases");
            json.WriteStartArray();
            foreach (var seat in read.Purchases.OrderBy(x => x.Lineage, StringComparer.Ordinal).ThenBy(x => x.Side))
            {
                json.WriteStartObject();
                json.WriteString("lineage", seat.Lineage);
                json.WriteString("side", seat.Side == Side.Left ? "left" : "right");
                json.WriteString("receipt", seat.Receipt);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WritePropertyName("moldPurchases");
            json.WriteStartArray();
            foreach (var mold in read.MoldPurchases.OrderBy(x => x.Lineage, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("lineage", mold.Lineage);
                json.WriteString("receipt", mold.Receipt);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return stream.ToArray();
    }
}
