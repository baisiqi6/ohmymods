// Patch_PalacePaidSourceProof.cs
// R5 纯 BCL source proof reader：宫廷付费的“来源岛冻结事实”读取（已批准 exact-gate
// APPROVE_EXACT_PALACE_SOURCE_PROOF_READER 的有限 R5 纯 helper 范围）。
//
// 边界（本文件绝不越界）：
//   * 纯 BCL（System.Text.Json / System.IO.Compression / SHA256），无 Unity/BepInEx/Harmony/游戏类型；
//   * 无 IO / 无 File·Directory / 无锁 / 不读真实存档 / 不写任何游戏文件/配置；
//   * 不提供 nativeRan / marker / OnPay / receipt / nativeSaveSuccess72 之类的布尔或默认值；
//     不能单独构成 SourceCaptured；journal、writer、travel、ClockArm、clock receipt 构造均不在本文件；
//   * 只读调用方传入的冻结 string/byte[] 与 PalacePaidScope；不解释、不验证 HeavyShield GUID
//     （GUID 不是 native campaign scalar；owner 语义 GUID 由 Root 现有 semantic binder 负责）。
//     caller 传任意 scope 只会得到绑定该 scope 域的 hash，不是“已验证原生 owner”。
//
// 输入域（2.4.0 pinned interop 元数据 + f2af 实际 gzip 结构证据；未知 native 字段保留参与
// canonical hash 且允许，不冻结游戏全部 schema）：
//   root:     serializedSaveDataVersion(16)、_currentCampaign、campaigns[]、prefs.srzEntries[{key,val}]
//   campaign: currentLand、biomeIndex、reign、realStartDateTime、totalGameDays、playTimeDays、
//             currentReign.daysSurvived、_islands[]
//   island:   playTimeDays、lastPlayedTimeDays、_islandTimePlayed、land、biome、lastPlayedReign、objects[]
//   object:   uniqueID、componentData2[{name,type,data}]；data 是 JSON 字符串
//   Player 组件 name=Player/type=PlayerIslandData（playerId 显式 int 0 = native P1，对应 owner.PlayerSlot 1；
//   P2 是 playerId 1，保留不当活动 P2）；Wallet 组件 name=Wallet/type=WalletData 必须同 row 且恰一
//   （usesCurrencySystem==true + currency 8 键显式 int>=0，不拿 legacy coins/gems/false mode 补值）；
//   Director 组件 name=Director/type=DirectorData 恰一（lastReignNum/daysInReign 显式 int，
//   lastReignNum 与 island.lastPlayedReign 一致）。
//
// 指纹（唯一格式，新 palace 域；不改 Hero/Musketeer 域）：
//   SHA256(逐项“4字节大端长度前缀 + UTF-8/原始字节”)，顺序固定：
//     ["palace-paid-source-v1", owner.ScopeKey()（7 元组 canonical SHA）, sourceLand(Invariant decimal),
//      canonicalIslandBytes,
//      五key按固定顺序：key、存在标记 "0"/"1"、仅 present 时 raw string]
//   五key固定顺序：KEM.CoinCourier.Campaigns.v1、KEM.HeavyShield.Campaigns.v1、
//     KingdomEnhancedMod_MusketeerRights_v1、KingdomEnhancedMod_HeroRights_v1、MyMod_SharedBankNative_v1。
//   canonicalIslandBytes：只跳三个 TOP-LEVEL 属性 playTimeDays/lastPlayedTimeDays/_islandTimePlayed，
//     其余按原 EnumerateObject 顺序逐 property.WriteTo(Utf8JsonWriter)；不排序、不递归跳 clock、
//     不删完整 objects/wallet/职业组件；任意层级重名拒。
//   PayerKey 同格式：[palace-native-payer-id-v1, "0"(native playerId), exactUniqueID]；这是 identity
//     encoding，不是另造 payer 事实；capture/file 同 trip 必须相同，不经截断/空格替换。
//
// gzip 完整性：与现 MusketeerNativeArchive.TryDecompress 同纯 BCL 算法（32MiB 压缩上限、128MiB
// 流式解压上限、标准 header、实际流式 CRC32/ISIZE 对 trailer 校验、拒 truncated/普通追加/坏 footer），
// 本文件独立实现以保持只依赖 PalacePaidScope，不调用其职业 keys/lease。该完整性校验针对当前 native
// 输出的形状与普通损坏；不宣称对抗任意多 member 伪造的数学 proof。
#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod
{
    internal static class PalacePaidSourceProof
    {
        public const int NativeVersion16 = 16;
        public const int MaxNativeCompressedBytes = 32 * 1024 * 1024;
        public const int MaxNativeDecompressedBytes = 128 * 1024 * 1024;
        public const int MaxFrozenIslandBytes = 64 * 1024 * 1024;
        public const int JsonMaxDepth = 64;

        private const string FingerprintDomain = "palace-paid-source-v1";
        private const string PayerDomain = "palace-native-payer-id-v1";
        private const int PrefCount = 5;

        // 固定顺序，绝不重排（capture 快照与 native srzEntries 两侧共用）。
        private static readonly string[] PrefKeys =
        {
            "KEM.CoinCourier.Campaigns.v1",
            "KEM.HeavyShield.Campaigns.v1",
            "KingdomEnhancedMod_MusketeerRights_v1",
            "KingdomEnhancedMod_HeroRights_v1",
            "MyMod_SharedBankNative_v1",
        };

        private static readonly string[] CurrencyNames =
        {
            "Coins", "Gems", "Crown", "Skulls", "Shades", "Merchandise", "Candle", "Egg",
        };

        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = JsonMaxDepth,
        };

        // Public Try readers convert malformed JSON string decoding into a structured refusal.
        // No native calls occur here, and environmental fatal exceptions are not swallowed.
        public static bool TryReadCapturedIsland(string islandJson, PalacePaidScope owner, int sourceLand,
            IReadOnlyDictionary<string,string> prefs, out PalacePaidSourceValues values, out string reason)
        {
            try { return TryReadCapturedIslandCore(islandJson,owner,sourceLand,prefs,out values,out reason); }
            catch (InvalidOperationException) { values=null!; reason="json-string"; return false; }
            catch (DecoderFallbackException) { values=null!; reason="utf8"; return false; }
        }
        public static bool TryReadNativeSnapshot(byte[] compressed, PalacePaidScope owner, int sourceLand,
            int expectedCurrentLand, out PalacePaidNativeSourceValues values, out string reason)
        {
            try { return TryReadNativeSnapshotCore(compressed,owner,sourceLand,expectedCurrentLand,out values,out reason); }
            catch (InvalidOperationException) { values=null!; reason="json-string"; return false; }
            catch (DecoderFallbackException) { values=null!; reason="utf8"; return false; }
        }

        // ───────────────────────── API 1：冻结 island JSON（capture / Prefs 快照侧） ─────────────────────────
        //
        // frozenIslandJson：调用方冻结的完整 Island JSON（单岛对象）。
        // fivePrefs：五key“存在项 -> 原始 string”快照；缺项 = absent。拒未知 key / null value；
        //            HeavyShield key 必须存在且非空。
        // 返回 immutable facts，不含 nativeRan/marker/OnPay 任何布尔；只接受全部明确字段，
        // 没读到（缺字段/坏类型）一律拒，不用 default 0 补。
        private static bool TryReadCapturedIslandCore(
            string frozenIslandJson,
            PalacePaidScope owner,
            int sourceLand,
            IReadOnlyDictionary<string, string> fivePrefs,
            out PalacePaidSourceValues values,
            out string reason)
        {
            values = null!;
            reason = "";
            if (owner == null) { reason = "owner"; return false; }
            if (sourceLand < 0 || sourceLand > 11) { reason = "source-land"; return false; }
            if (!TryNormalizeFivePrefs(fivePrefs, out string?[] prefRaw, out bool[] prefPresent, out reason)) return false;
            if (frozenIslandJson == null || frozenIslandJson.Length == 0) { reason = "island-json"; return false; }
            if (frozenIslandJson[0] == '\uFEFF') { reason = "bom"; return false; }
            if (HasLoneSurrogate(frozenIslandJson)) { reason = "utf16"; return false; }
            if ((long)Encoding.UTF8.GetByteCount(frozenIslandJson) > MaxFrozenIslandBytes) { reason = "size"; return false; }
            byte[] utf8 = Encoding.UTF8.GetBytes(frozenIslandJson);
            JsonDocument document;
            try { document = JsonDocument.Parse(new ReadOnlyMemory<byte>(utf8), DocumentOptions); }
            catch (JsonException) { reason = "json"; return false; }
            using (document)
            {
                JsonElement island = document.RootElement;
                if (island.ValueKind != JsonValueKind.Object) { reason = "island"; return false; }
                if (!HasNoDuplicateKeys(island)) { reason = "duplicate"; return false; }
                if (HasLoneSurrogateInTree(island)) { reason = "utf16"; return false; }
                if (!TryReadIslandFacts(island, owner, sourceLand, prefRaw, prefPresent,
                        out string fingerprint, out string payerUniqueId, out string payerKey, out int[] currencies,
                        out double islandPlayTimeDays, out double islandLastPlayedTimeDays,
                        out int islandTimePlayed, out int islandLastPlayedReign,
                        out string directorSummarySha256, out string objectsSummarySha256, out reason))
                {
                    return false;
                }
                values = new PalacePaidSourceValues(owner, sourceLand, fingerprint, payerUniqueId, payerKey,
                    currencies, islandPlayTimeDays, islandLastPlayedTimeDays, islandTimePlayed, islandLastPlayedReign,
                    directorSummarySha256, objectsSummarySha256);
                return true;
            }
        }

        // ───────────────────────── API 2：同份 gzip 的 native snapshot（raw bytes 侧） ─────────────────────────
        //
        // compressed：调用方冻结的真实存档 raw bytes（gzip，<=32MiB）；先防御复制到私有 snapshot，
        //             再流式解压（<=128MiB）并校验 header/trailer 的实际 CRC32 与 ISIZE。
        // expectedCurrentLand：Root 按初始源/不同已知阶段提供的 campaign currentLand 期望值（0..12）。
        // 返回：rawSHA（= 传入 raw bytes 的 SHA256）、nativeVersion16、campaign.totalGameDays、
        //       currentReign.daysSurvived、campaign.playTimeDays，及其 island facts（同 capture reader）。
        // GUID 不是 native campaign scalar：本 BCL helper 不声称验证 HeavyShieldGUID；HeavyShield 原 raw
        // 字符串参与 fingerprint，由 Root 与 capture/paid proof 交叉核对。
        private static bool TryReadNativeSnapshotCore(
            byte[] compressed,
            PalacePaidScope owner,
            int sourceLand,
            int expectedCurrentLand,
            out PalacePaidNativeSourceValues values,
            out string reason)
        {
            values = null!;
            reason = "";
            if (owner == null) { reason = "owner"; return false; }
            if (sourceLand < 0 || sourceLand > 11) { reason = "source-land"; return false; }
            if (expectedCurrentLand < 0 || expectedCurrentLand > 12) { reason = "expected-land"; return false; }
            if (compressed == null || compressed.Length == 0) { reason = "native"; return false; }
            if (compressed.Length > MaxNativeCompressedBytes) { reason = "size"; return false; }

            byte[] snapshot = new byte[compressed.Length];
            Array.Copy(compressed, snapshot, compressed.Length);
            string rawSha256 = PalacePaidText.ToLowerHex(SHA256.HashData(snapshot));

            if (!TryDecompress(snapshot, out byte[] plain, out int plainLength, out reason)) return false;
            if (plainLength >= 3 && plain[0] == 0xEF && plain[1] == 0xBB && plain[2] == 0xBF)
            {
                reason = "bom";
                return false;
            }
            try { new UTF8Encoding(false, true).GetCharCount(plain.AsSpan(0, plainLength)); }
            catch (DecoderFallbackException) { reason = "utf8"; return false; }
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(
                    new ReadOnlyMemory<byte>(plain, 0, plainLength), DocumentOptions);
            }
            catch (JsonException) { reason = "json"; return false; }
            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { reason = "root"; return false; }
                if (!HasNoDuplicateKeys(root)) { reason = "duplicate"; return false; }
                if (HasLoneSurrogateInTree(root)) { reason = "utf16"; return false; }

                if (!TryGetProperty(root, "serializedSaveDataVersion", out JsonElement versionElement)
                    || !TryReadExplicitInt(versionElement, out int version)) { reason = "version"; return false; }
                if (version != NativeVersion16) { reason = "version"; return false; }

                if (!TryGetProperty(root, "_currentCampaign", out JsonElement currentElement)
                    || !TryReadExplicitInt(currentElement, out int currentCampaign)) { reason = "campaign-slot"; return false; }
                if (currentCampaign != owner.CampaignSlot) { reason = "campaign-slot"; return false; }

                if (!TryGetProperty(root, "campaigns", out JsonElement campaigns)
                    || campaigns.ValueKind != JsonValueKind.Array) { reason = "campaigns"; return false; }
                if (owner.CampaignSlot >= campaigns.GetArrayLength()) { reason = "campaigns"; return false; }
                JsonElement campaign = campaigns[owner.CampaignSlot];
                if (campaign.ValueKind != JsonValueKind.Object) { reason = "campaign"; return false; }

                if (!TryGetProperty(campaign, "currentLand", out JsonElement currentLandElement)
                    || !TryReadExplicitInt(currentLandElement, out int currentLand)) { reason = "current-land"; return false; }
                if (currentLand != expectedCurrentLand) { reason = "current-land"; return false; }
                if (!TryGetProperty(campaign, "biomeIndex", out JsonElement campaignBiomeElement)
                    || !TryReadExplicitInt(campaignBiomeElement, out int campaignBiome)) { reason = "campaign-biome"; return false; }
                if (campaignBiome != owner.Biome) { reason = "campaign-biome"; return false; }
                if (!TryGetProperty(campaign, "reign", out JsonElement campaignReignElement)
                    || !TryReadExplicitInt(campaignReignElement, out int campaignReign)) { reason = "campaign-reign"; return false; }
                if (campaignReign != owner.Reign) { reason = "campaign-reign"; return false; }
                if (!TryGetProperty(campaign, "realStartDateTime", out JsonElement campaignStartElement)
                    || !TryReadExplicitInt(campaignStartElement, out int campaignStart)) { reason = "campaign-start"; return false; }
                if (campaignStart != owner.Start) { reason = "campaign-start"; return false; }

                if (!TryGetProperty(campaign, "totalGameDays", out JsonElement totalDaysElement)
                    || !TryReadExplicitInt(totalDaysElement, out int totalGameDays)) { reason = "total-game-days"; return false; }
                if (totalGameDays < 0) { reason = "total-game-days"; return false; }
                if (!TryGetProperty(campaign, "playTimeDays", out JsonElement campaignPlayTimeElement)
                    || !TryReadFiniteDouble(campaignPlayTimeElement, out double campaignPlayTime)) { reason = "campaign-play-time"; return false; }
                if (campaignPlayTime < 0) { reason = "campaign-play-time"; return false; }
                if (!TryGetProperty(campaign, "currentReign", out JsonElement currentReign)
                    || currentReign.ValueKind != JsonValueKind.Object) { reason = "current-reign"; return false; }
                if (!TryGetProperty(currentReign, "daysSurvived", out JsonElement daysSurvivedElement)
                    || !TryReadExplicitInt(daysSurvivedElement, out int daysSurvived)) { reason = "current-reign"; return false; }
                if (daysSurvived < 0) { reason = "current-reign"; return false; }

                if (!TryGetProperty(campaign, "_islands", out JsonElement islands)
                    || islands.ValueKind != JsonValueKind.Array) { reason = "islands"; return false; }
                if (sourceLand >= islands.GetArrayLength()) { reason = "islands"; return false; }
                JsonElement island = islands[sourceLand];
                if (island.ValueKind != JsonValueKind.Object) { reason = "island"; return false; }

                if (!TryReadNativePrefs(root, out string?[] prefRaw, out bool[] prefPresent, out reason)) return false;

                if (!TryReadIslandFacts(island, owner, sourceLand, prefRaw, prefPresent,
                        out string fingerprint, out string payerUniqueId, out string payerKey, out int[] currencies,
                        out double islandPlayTimeDays, out double islandLastPlayedTimeDays,
                        out int islandTimePlayed, out int islandLastPlayedReign,
                        out string directorSummarySha256, out string objectsSummarySha256, out reason))
                {
                    return false;
                }

                PalacePaidSourceValues islandValues = new PalacePaidSourceValues(owner, sourceLand, fingerprint,
                    payerUniqueId, payerKey, currencies, islandPlayTimeDays, islandLastPlayedTimeDays,
                    islandTimePlayed, islandLastPlayedReign, directorSummarySha256, objectsSummarySha256);
                values = new PalacePaidNativeSourceValues(islandValues, rawSha256, version, totalGameDays,
                    daysSurvived, campaignPlayTime);
                return true;
            }
        }

        // ───────────────────────── island 事实（capture 与 native 共用同一 reader） ─────────────────────────
        private static bool TryReadIslandFacts(
            JsonElement island,
            PalacePaidScope owner,
            int sourceLand,
            string?[] prefRaw,
            bool[] prefPresent,
            out string fingerprint,
            out string payerUniqueId,
            out string payerKey,
            out int[] currencies,
            out double islandPlayTimeDays,
            out double islandLastPlayedTimeDays,
            out int islandTimePlayed,
            out int islandLastPlayedReign,
            out string directorSummarySha256,
            out string objectsSummarySha256,
            out string reason)
        {
            fingerprint = null!;
            payerUniqueId = null!;
            payerKey = null!;
            currencies = null!;
            islandPlayTimeDays = 0;
            islandLastPlayedTimeDays = 0;
            islandTimePlayed = 0;
            islandLastPlayedReign = 0;
            directorSummarySha256 = null!;
            objectsSummarySha256 = null!;
            reason = "";

            if (island.ValueKind != JsonValueKind.Object) { reason = "island"; return false; }

            if (!TryGetProperty(island, "land", out JsonElement landElement)
                || !TryReadExplicitInt(landElement, out int land)) { reason = "land"; return false; }
            if (land != sourceLand) { reason = "land"; return false; }
            if (!TryGetProperty(island, "biome", out JsonElement biomeElement)
                || !TryReadExplicitInt(biomeElement, out int biome)) { reason = "biome"; return false; }

            if (!TryGetProperty(island, "playTimeDays", out JsonElement playElement)
                || !TryReadFiniteDouble(playElement, out islandPlayTimeDays)) { reason = "playTimeDays"; return false; }
            if (!TryGetProperty(island, "lastPlayedTimeDays", out JsonElement lastPlayedElement)
                || !TryReadFiniteDouble(lastPlayedElement, out islandLastPlayedTimeDays)) { reason = "lastPlayedTimeDays"; return false; }
            if (!TryGetProperty(island, "_islandTimePlayed", out JsonElement islandTimeElement)
                || !TryReadExplicitInt(islandTimeElement, out islandTimePlayed)) { reason = "_islandTimePlayed"; return false; }
            if (!TryGetProperty(island, "lastPlayedReign", out JsonElement lastReignElement)
                || !TryReadExplicitInt(lastReignElement, out islandLastPlayedReign)) { reason = "lastPlayedReign"; return false; }

            if (!TryGetProperty(island, "objects", out JsonElement objects)
                || objects.ValueKind != JsonValueKind.Array) { reason = "objects"; return false; }

            // P1 row 扫描：只认 name=Player/type=PlayerIslandData 且 data 内 playerId 显式 int 0 的唯一组件。
            // P2 行（playerId!=0）原样保留，不按 displayName/首个 Wallet 猜。
            int p1Components = 0;
            JsonElement p1Row = default;
            bool hasP1Row = false;
            JsonElement p1WalletComponent = default;
            bool hasP1Wallet = false;
            string? directorDataText = null;
            int directorComponents = 0;
            foreach (JsonElement row in objects.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) { reason = "objects"; return false; }
                if (!TryGetProperty(row, "componentData2", out JsonElement components)) continue;
                if (components.ValueKind != JsonValueKind.Array) { reason = "components"; return false; }
                int playerComponentsOnRow = 0;
                int walletComponentsOnRow = 0;
                int rowPlayerId = -1;
                JsonElement rowWalletComponent = default;
                bool rowHasWalletComponent = false;
                foreach (JsonElement component in components.EnumerateArray())
                {
                    if (component.ValueKind != JsonValueKind.Object) { reason = "components"; return false; }
                    string? name = TryGetString(component, "name");
                    string? type = TryGetString(component, "type");
                    if (name == "Player" && type == "PlayerIslandData")
                    {
                        if (++playerComponentsOnRow > 1) { reason = "player-component"; return false; }
                        if (!TryReadNestedObject(component, "player-data", out JsonDocument playerDocument, out reason)) return false;
                        using (playerDocument)
                        {
                            if (!TryGetProperty(playerDocument.RootElement, "playerId", out JsonElement playerIdElement)
                                || !TryReadExplicitInt(playerIdElement, out rowPlayerId)) { reason = "player-data"; return false; }
                        }
                    }
                    else if (name == "Wallet" && type == "WalletData")
                    {
                        walletComponentsOnRow++;
                        if (walletComponentsOnRow > 1) { reason = "wallet-component"; return false; }
                        if (!component.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String)
                        { reason = "wallet-data"; return false; }
                        rowWalletComponent = component;
                        rowHasWalletComponent = true;
                    }
                    else if (name == "Director" && type == "DirectorData")
                    {
                        directorComponents++;
                        if (directorComponents > 1) { reason = "director"; return false; }
                        if (!component.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String)
                        { reason = "director-data"; return false; }
                        directorDataText = data.GetString();
                    }
                }
                if (playerComponentsOnRow == 1 && rowPlayerId == 0)
                {
                    p1Components++;
                    if (p1Components > 1) { reason = "p1-count"; return false; }
                    if (walletComponentsOnRow != 1 || !rowHasWalletComponent) { reason = "wallet-component"; return false; }
                    p1Row = row;
                    p1WalletComponent = rowWalletComponent;
                    hasP1Wallet = true;
                    hasP1Row = true;
                }
            }
            if (p1Components != 1 || !hasP1Row) { reason = "p1-count"; return false; }

            // 非空 exact uniqueID 且对象 uniqueID 不歧义（同 UID 行必须恰此一）。
            if (!TryGetProperty(p1Row, "uniqueID", out JsonElement uniqueIdElement)
                || uniqueIdElement.ValueKind != JsonValueKind.String) { reason = "unique-id"; return false; }
            payerUniqueId = uniqueIdElement.GetString()!;
            if (payerUniqueId.Length == 0) { reason = "unique-id"; return false; }
            int sameUidRows = 0;
            foreach (JsonElement row in objects.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                string? uid = TryGetString(row, "uniqueID");
                if (uid != null && string.Equals(uid, payerUniqueId, StringComparison.Ordinal)) sameUidRows++;
            }
            if (sameUidRows != 1) { reason = "unique-id"; return false; }

            if (!hasP1Wallet) { reason = "wallet-component"; return false; }
            if (!TryReadNestedObject(p1WalletComponent, "wallet-data", out JsonDocument walletDocument, out reason)) return false;
            using (walletDocument)
            {
                if (!TryReadWalletCurrencies(walletDocument.RootElement, out currencies, out reason)) return false;
            }

            if (directorComponents != 1 || directorDataText == null) { reason = "director"; return false; }
            if (!TryReadDirector(directorDataText, islandLastPlayedReign, out directorSummarySha256, out reason)) return false;

            // Known Player/P1 Wallet/Director readers retain their strict schema and reason order.
            // Validate every embedded component JSON before publishing island facts.
            foreach (JsonElement row in objects.EnumerateArray())
            {
                if (!TryGetProperty(row, "componentData2", out JsonElement components)) continue;
                foreach (JsonElement component in components.EnumerateArray())
                {
                    if (!TryValidateComponentData(component, out reason)) return false;
                }
            }

            objectsSummarySha256 = HashCanonical(objects);

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendField(hash, FingerprintDomain);
                AppendField(hash, owner.ScopeKey());
                AppendField(hash, sourceLand.ToString(CultureInfo.InvariantCulture));
                AppendCanonicalIsland(hash, island);
                for (int i = 0; i < PrefCount; i++)
                {
                    AppendField(hash, PrefKeys[i]);
                    AppendField(hash, prefPresent[i] ? "1" : "0");
                    if (prefPresent[i]) AppendField(hash, prefRaw[i]!);
                }
                fingerprint = PalacePaidText.ToLowerHex(hash.GetHashAndReset());
            }

            payerKey = ComputePayerKey(payerUniqueId);
            return true;
        }

        // ───────────────────────── 具体字段读取 ─────────────────────────

        private static bool TryReadWalletCurrencies(JsonElement walletData, out int[] currencies, out string reason)
        {
            currencies = null!;
            reason = "";
            if (walletData.ValueKind != JsonValueKind.Object) { reason = "wallet-data"; return false; }
            if (!TryGetProperty(walletData, "usesCurrencySystem", out JsonElement uses)
                || uses.ValueKind != JsonValueKind.True) { reason = "uses-currency-system"; return false; }
            if (!TryGetProperty(walletData, "currency", out JsonElement currency)
                || currency.ValueKind != JsonValueKind.Object) { reason = "currency"; return false; }
            int[] values = new int[CurrencyNames.Length];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (JsonProperty property in currency.EnumerateObject())
            {
                count++;
                if (!seen.Add(property.Name)) { reason = "currency"; return false; }
                int index = CurrencyIndex(property.Name);
                if (index < 0) { reason = "currency"; return false; }
                if (!TryReadExplicitInt(property.Value, out int value)) { reason = "currency"; return false; }
                if (value < 0) { reason = "currency"; return false; }
                values[index] = value;
            }
            if (count != CurrencyNames.Length) { reason = "currency"; return false; }
            currencies = values;
            return true;
        }

        private static int CurrencyIndex(string name)
        {
            for (int i = 0; i < CurrencyNames.Length; i++)
            {
                if (string.Equals(CurrencyNames[i], name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        // DirectorData：解码为 object，全部字段/事件表原样参与 canonical 摘要（不删 clock/events）；
        // lastReignNum/daysInReign 必须显式 int 存在，lastReignNum 与 island.lastPlayedReign 一致。
        private static bool TryReadDirector(string directorDataText, int islandLastPlayedReign,
            out string directorSummarySha256, out string reason)
        {
            directorSummarySha256 = null!;
            reason = "";
            JsonDocument document;
            try { document = JsonDocument.Parse(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(directorDataText)), DocumentOptions); }
            catch (JsonException) { reason = "director-data"; return false; }
            using (document)
            {
                JsonElement data = document.RootElement;
                if (data.ValueKind != JsonValueKind.Object) { reason = "director-data"; return false; }
                if (!HasNoDuplicateKeys(data)) { reason = "duplicate"; return false; }
                if (HasLoneSurrogateInTree(data)) { reason = "utf16"; return false; }
                if (!TryGetProperty(data, "lastReignNum", out JsonElement lastReignElement)
                    || !TryReadExplicitInt(lastReignElement, out int lastReignNum)) { reason = "director-data"; return false; }
                if (!TryGetProperty(data, "daysInReign", out JsonElement daysElement)
                    || !TryReadExplicitInt(daysElement, out _)) { reason = "director-data"; return false; }
                if (lastReignNum != islandLastPlayedReign) { reason = "director-reign"; return false; }
                directorSummarySha256 = HashCanonical(data);
                return true;
            }
        }

        // Validation only: unknown component data may be any legal JSON value. Keep its raw
        // string unchanged in the original island/objects hash; do not recursively parse strings.
        private static bool TryValidateComponentData(JsonElement component, out string reason)
        {
            reason = "";
            if (!component.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String)
            { reason = "component-data"; return false; }
            string? text = data.GetString();
            if (text == null || HasLoneSurrogate(text)) { reason = "utf16"; return false; }
            JsonDocument document;
            try { document = JsonDocument.Parse(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text)), DocumentOptions); }
            catch (JsonException) { reason = "json"; return false; }
            using (document)
            {
                if (!HasNoDuplicateKeys(document.RootElement)) { reason = "duplicate"; return false; }
                if (HasLoneSurrogateInTree(document.RootElement)) { reason = "utf16"; return false; }
                return true;
            }
        }

        private static bool TryReadNestedObject(JsonElement component, string failReason,
            out JsonDocument document, out string reason)
        {
            document = null!;
            reason = "";
            if (!component.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String)
            {
                reason = failReason;
                return false;
            }
            string? text = data.GetString();
            if (text == null || HasLoneSurrogate(text)) { reason = "utf16"; return false; }
            JsonDocument parsed;
            try { parsed = JsonDocument.Parse(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text)), DocumentOptions); }
            catch (JsonException) { reason = "json"; return false; }
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                reason = failReason;
                return false;
            }
            if (!HasNoDuplicateKeys(parsed.RootElement))
            {
                parsed.Dispose();
                reason = "duplicate";
                return false;
            }
            if (HasLoneSurrogateInTree(parsed.RootElement))
            {
                parsed.Dispose();
                reason = "utf16";
                return false;
            }
            document = parsed;
            return true;
        }

        // 五key快照（capture 传入侧）：未知 key / null value 拒；HeavyShield 必须存在非空。
        private static bool TryNormalizeFivePrefs(IReadOnlyDictionary<string, string>? fivePrefs,
            out string?[] raw, out bool[] present, out string reason)
        {
            raw = new string?[PrefCount];
            present = new bool[PrefCount];
            reason = "";
            if (fivePrefs == null) { reason = "prefs"; return false; }
            foreach (KeyValuePair<string, string> pair in fivePrefs)
            {
                int index = PrefIndex(pair.Key);
                if (index < 0) { reason = "prefs-key"; return false; }
                if (pair.Value == null) { reason = "prefs-value"; return false; }
                if (HasLoneSurrogate(pair.Value)) { reason = "utf16"; return false; }
                if (present[index]) { reason = "prefs-key"; return false; }
                present[index] = true;
                raw[index] = pair.Value;
            }
            return CheckHeavyShield(raw, present, out reason);
        }

        // 同一解压 gzip 的 root.prefs.srzEntries：行必须是 {key,val} 字符串对；任意重复 key 拒；
        // 五key从原 string 直读（不跨时刻补 RAM 权益）。
        private static bool TryReadNativePrefs(JsonElement root, out string?[] raw, out bool[] present, out string reason)
        {
            raw = new string?[PrefCount];
            present = new bool[PrefCount];
            reason = "";
            if (!TryGetProperty(root, "prefs", out JsonElement prefs) || prefs.ValueKind != JsonValueKind.Object)
            { reason = "prefs"; return false; }
            if (!TryGetProperty(prefs, "srzEntries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
            { reason = "entries"; return false; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) { reason = "prefs-row"; return false; }
                if (!TryGetProperty(entry, "key", out JsonElement keyElement) || keyElement.ValueKind != JsonValueKind.String
                    || !TryGetProperty(entry, "val", out JsonElement valElement) || valElement.ValueKind != JsonValueKind.String)
                { reason = "prefs-row"; return false; }
                string key = keyElement.GetString()!;
                if (!seen.Add(key)) { reason = "prefs-key"; return false; }
                int index = PrefIndex(key);
                if (index < 0) continue;
                present[index] = true;
                raw[index] = valElement.GetString()!;
            }
            return CheckHeavyShield(raw, present, out reason);
        }

        private static bool CheckHeavyShield(string?[] raw, bool[] present, out string reason)
        {
            reason = "";
            int heavyShield = PrefIndex("KEM.HeavyShield.Campaigns.v1");
            if (!present[heavyShield] || raw[heavyShield] == null || raw[heavyShield]!.Length == 0)
            {
                reason = "heavyshield";
                return false;
            }
            return true;
        }

        private static int PrefIndex(string name)
        {
            for (int i = 0; i < PrefCount; i++)
            {
                if (string.Equals(PrefKeys[i], name, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        // ───────────────────────── canonical / hash ─────────────────────────

        private static void AppendCanonicalIsland(IncrementalHash hash, JsonElement island)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty property in island.EnumerateObject())
                    {
                        if (string.Equals(property.Name, "playTimeDays", StringComparison.Ordinal)) continue;
                        if (string.Equals(property.Name, "lastPlayedTimeDays", StringComparison.Ordinal)) continue;
                        if (string.Equals(property.Name, "_islandTimePlayed", StringComparison.Ordinal)) continue;
                        property.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                // CanonicalHashBuffer 等价语义：只取已完成 stream[0, Length]，不读 Capacity 尾巴。
                AppendField(hash, stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
            }
        }

        private static string HashCanonical(JsonElement element)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    element.WriteTo(writer);
                }
                return PalacePaidText.ToLowerHex(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
            }
        }

        private static void AppendField(IncrementalHash hash, string text)
        {
            AppendField(hash, Encoding.UTF8.GetBytes(text));
        }

        private static void AppendField(IncrementalHash hash, ReadOnlySpan<byte> bytes)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        private static string ComputePayerKey(string uniqueId)
        {
            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendField(hash, PayerDomain);
                AppendField(hash, "0");
                AppendField(hash, uniqueId);
                return PalacePaidText.ToLowerHex(hash.GetHashAndReset());
            }
        }

        // ───────────────────────── 严格 JSON 读取工具 ─────────────────────────

        private static bool TryGetProperty(JsonElement parent, string name, out JsonElement value)
        {
            return parent.TryGetProperty(name, out value);
        }

        private static string? TryGetString(JsonElement parent, string name)
        {
            if (!parent.TryGetProperty(name, out JsonElement value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        // 显式 int：只接受 JSON 整数字面量（无小数/指数/前导零），拒绝 string/float 混淆与溢出。
        private static bool TryReadExplicitInt(JsonElement element, out int value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.Number) return false;
            string raw = element.GetRawText();
            if (raw.Length == 0 || raw.Length > 11) return false;
            int i = raw[0] == '-' ? 1 : 0;
            if (i >= raw.Length) return false;
            if (raw[i] == '0' && raw.Length - i > 1) return false;
            for (; i < raw.Length; i++)
            {
                if (raw[i] < '0' || raw[i] > '9') return false;
            }
            return int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        // 显式有限 double：JSON number，且 double.IsFinite；NaN/Infinity/溢出拒。
        private static bool TryReadFiniteDouble(JsonElement element, out double value)
        {
            value = 0;
            if (element.ValueKind != JsonValueKind.Number) return false;
            if (!element.TryGetDouble(out value)) return false;
            return double.IsFinite(value);
        }

        // 任意层级重名拒（含转义后解码同名的两个属性）；不做 schema 冻结，未知字段正常保留。
        private static bool HasNoDuplicateKeys(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) return false;
                    if (!HasNoDuplicateKeys(property.Value)) return false;
                }
                return true;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (!HasNoDuplicateKeys(item)) return false;
                }
            }
            return true;
        }

        private static bool HasLoneSurrogateInTree(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (HasLoneSurrogate(property.Name)) return true;
                    if (HasLoneSurrogateInTree(property.Value)) return true;
                }
                return false;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (HasLoneSurrogateInTree(item)) return true;
                }
                return false;
            }
            if (element.ValueKind == JsonValueKind.String)
            {
                return HasLoneSurrogate(element.GetString());
            }
            return false;
        }

        private static bool HasLoneSurrogate(string? text)
        {
            if (text == null) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return true;
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return true;
                }
            }
            return false;
        }

        // ───────────────────────── gzip（与 MusketeerNativeArchive 同算法，独立实现） ─────────────────────────

        private static bool TryDecompress(byte[] input, out byte[] output, out int outputLength, out string reason)
        {
            output = null!;
            outputLength = 0;
            reason = "";
            if (input.Length < 18) { reason = "gzip"; return false; }
            if (input[0] != 0x1F || input[1] != 0x8B || input[2] != 0x08 || (input[3] & 0xE0) != 0)
            {
                reason = "gzip-header";
                return false;
            }
            try
            {
                using (var compressed = new MemoryStream(input, writable: false))
                using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
                using (var plain = new MemoryStream())
                {
                    byte[] chunk = new byte[81920];
                    uint crc = 0xFFFFFFFFu;
                    int total = 0;
                    int read;
                    while ((read = gzip.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        total += read;
                        if (total > MaxNativeDecompressedBytes) { reason = "size"; return false; }
                        for (int i = 0; i < read; i++) crc = Crc32Table[(crc ^ chunk[i]) & 0xFF] ^ (crc >> 8);
                        plain.Write(chunk, 0, read);
                    }
                    crc = ~crc;
                    if (!ValidateTrailer(input, crc, (uint)total, out reason)) return false;
                    // 缓冲区按容量暴露，必须同时返回实际长度；调用方只读 [0, outputLength]。
                    output = plain.GetBuffer();
                    outputLength = total;
                    return true;
                }
            }
            catch (Exception) { reason = "gzip"; return false; }
        }

        // 由实际解压字节计算的 CRC32/ISIZE 必须等于 payload 末尾 8 字节的 trailer；普通追加/截断/坏 footer 拒。
        private static bool ValidateTrailer(byte[] payload, uint crc32, uint size, out string reason)
        {
            reason = "";
            if (payload.Length < 18) { reason = "gzip"; return false; }
            if (crc32 != ReadUInt32Le(payload, payload.Length - 8)) { reason = "gzip-crc"; return false; }
            if (size != ReadUInt32Le(payload, payload.Length - 4)) { reason = "gzip-size"; return false; }
            return true;
        }

        private static uint ReadUInt32Le(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            uint[] table = new uint[256];
            for (int index = 0; index < table.Length; index++)
            {
                uint value = (uint)index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                }
                table[index] = value;
            }
            return table;
        }
    }

    // ───────────────────────── immutable facts ─────────────────────────

    internal sealed class PalacePaidSourceValues
    {
        private readonly int[] _currencies;
        private readonly IReadOnlyList<int> _currenciesView;

        public PalacePaidScope Owner { get; }
        public int SourceLand { get; }
        public string SourceFingerprint { get; }
        public string PayerUniqueId { get; }
        public string PayerKey { get; }
        public IReadOnlyList<int> Currencies { get { return _currenciesView; } }
        public double IslandPlayTimeDays { get; }
        public double IslandLastPlayedTimeDays { get; }
        public int IslandTimePlayed { get; }
        public int IslandLastPlayedReign { get; }
        public string DirectorSummarySha256 { get; }
        public string ObjectsSummarySha256 { get; }

        internal PalacePaidSourceValues(
            PalacePaidScope owner,
            int sourceLand,
            string sourceFingerprint,
            string payerUniqueId,
            string payerKey,
            int[] currencies,
            double islandPlayTimeDays,
            double islandLastPlayedTimeDays,
            int islandTimePlayed,
            int islandLastPlayedReign,
            string directorSummarySha256,
            string objectsSummarySha256)
        {
            if (owner == null) throw new PalacePaidContractException("sourceValues.owner is required");
            if (sourceLand < 0 || sourceLand > 11) throw new PalacePaidContractException("sourceValues.sourceLand must be 0..11");
            if (!PalacePaidText.IsLowerHex(sourceFingerprint, 64)) throw new PalacePaidContractException("sourceValues.sourceFingerprint must be 64 lowercase hex");
            if (string.IsNullOrEmpty(payerUniqueId)) throw new PalacePaidContractException("sourceValues.payerUniqueId must be non-empty");
            if (!PalacePaidText.IsLowerHex(payerKey, 64)) throw new PalacePaidContractException("sourceValues.payerKey must be 64 lowercase hex");
            if (!PalacePaidText.IsLowerHex(directorSummarySha256, 64)) throw new PalacePaidContractException("sourceValues.directorSummarySha256 must be 64 lowercase hex");
            if (!PalacePaidText.IsLowerHex(objectsSummarySha256, 64)) throw new PalacePaidContractException("sourceValues.objectsSummarySha256 must be 64 lowercase hex");
            if (!double.IsFinite(islandPlayTimeDays)) throw new PalacePaidContractException("sourceValues.islandPlayTimeDays must be finite");
            if (!double.IsFinite(islandLastPlayedTimeDays)) throw new PalacePaidContractException("sourceValues.islandLastPlayedTimeDays must be finite");

            _currencies = PalacePaidCurrencies.CopyValidated(currencies, "sourceValues.currencies");
            _currenciesView = Array.AsReadOnly(_currencies);
            Owner = owner;
            SourceLand = sourceLand;
            SourceFingerprint = sourceFingerprint;
            PayerUniqueId = payerUniqueId;
            PayerKey = payerKey;
            IslandPlayTimeDays = islandPlayTimeDays;
            IslandLastPlayedTimeDays = islandLastPlayedTimeDays;
            IslandTimePlayed = islandTimePlayed;
            IslandLastPlayedReign = islandLastPlayedReign;
            DirectorSummarySha256 = directorSummarySha256;
            ObjectsSummarySha256 = objectsSummarySha256;
        }

        public bool ContentEquals(PalacePaidSourceValues? other)
        {
            if (other == null) return false;
            return Owner.ContentEquals(other.Owner)
                && SourceLand == other.SourceLand
                && string.Equals(SourceFingerprint, other.SourceFingerprint, StringComparison.Ordinal)
                && string.Equals(PayerUniqueId, other.PayerUniqueId, StringComparison.Ordinal)
                && string.Equals(PayerKey, other.PayerKey, StringComparison.Ordinal)
                && IslandPlayTimeDays == other.IslandPlayTimeDays
                && IslandLastPlayedTimeDays == other.IslandLastPlayedTimeDays
                && IslandTimePlayed == other.IslandTimePlayed
                && IslandLastPlayedReign == other.IslandLastPlayedReign
                && string.Equals(DirectorSummarySha256, other.DirectorSummarySha256, StringComparison.Ordinal)
                && string.Equals(ObjectsSummarySha256, other.ObjectsSummarySha256, StringComparison.Ordinal)
                && PalacePaidCurrencies.ContentEquals(_currencies, other._currencies);
        }
    }

    internal sealed class PalacePaidNativeSourceValues
    {
        public PalacePaidSourceValues Island { get; }
        public string RawSaveSha256 { get; }
        public int NativeVersion { get; }
        public int CampaignTotalGameDays { get; }
        public int CurrentReignDaysSurvived { get; }
        public double CampaignPlayTimeDays { get; }

        internal PalacePaidNativeSourceValues(
            PalacePaidSourceValues island,
            string rawSaveSha256,
            int nativeVersion,
            int campaignTotalGameDays,
            int currentReignDaysSurvived,
            double campaignPlayTimeDays)
        {
            if (island == null) throw new PalacePaidContractException("nativeSourceValues.island is required");
            if (!PalacePaidText.IsLowerHex(rawSaveSha256, 64)) throw new PalacePaidContractException("nativeSourceValues.rawSaveSha256 must be 64 lowercase hex");
            if (nativeVersion != PalacePaidSourceProof.NativeVersion16) throw new PalacePaidContractException("nativeSourceValues.nativeVersion must be 16");
            if (campaignTotalGameDays < 0) throw new PalacePaidContractException("nativeSourceValues.campaignTotalGameDays must be >= 0");
            if (currentReignDaysSurvived < 0) throw new PalacePaidContractException("nativeSourceValues.currentReignDaysSurvived must be >= 0");
            if (!double.IsFinite(campaignPlayTimeDays) || campaignPlayTimeDays < 0) throw new PalacePaidContractException("nativeSourceValues.campaignPlayTimeDays must be finite and >= 0");

            Island = island;
            RawSaveSha256 = rawSaveSha256;
            NativeVersion = nativeVersion;
            CampaignTotalGameDays = campaignTotalGameDays;
            CurrentReignDaysSurvived = currentReignDaysSurvived;
            CampaignPlayTimeDays = campaignPlayTimeDays;
        }
    }
}
