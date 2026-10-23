// Patch_PalacePaidJournal.cs
// R3 纯值层：paid journal 的不可变 DTO、严格 JSON codec、不变量校验与纯状态机。
//
// 边界（本文件绝不越界）：
//   * 无 IO / 无 File·Directory / 无锁 / 无 store / 无 storageReady；
//   * 无 Unity·BepInEx·Harmony·游戏类型 / 无原生调用 / 不读存档或配置；
//   * 不判定 native 真实成功（FileProof.NativeRawResult 只是形状域，72/73/Failure 均不特判）；
//   * 不实现文件 CAS、原子写或并发；R4 负责存储接线与真实费用记账。
//
// 契约要点：
//   * schema1 preserves legacy unknown layout; schema2 binds a known current destination; stage strings are fixed;
//   * owner 七元组：profile, campaignGuid, campaignSlot, biome(5), reign, start, playerSlot(1)，逐项精确比较；
//   * sourceLand 属于 record（0..11），palaceLand 固定 12，两者都不进 ScopeKey；
//   * ScopeKey = 长度前缀 UTF-8 → SHA-256 → 小写 64 hex；文件名算法只返回安全字符串；
//   * canonical JSON 固定字段次序，紧凑无空白；hash 绑定输入原始 bytes（Envelope.SourceBytesSha256）。
#nullable enable

using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KingdomEnhancedMod
{
    // ───────────────────────── 枚举 ─────────────────────────

    internal enum PalacePaidStage
    {
        ChargeIntent = 0,
        Cancelled = 1,
        PaidObserved = 2,
        SourceCaptured = 3,
        Resumable = 4,
        DepartureClaimed = 5,
        Entered = 6,
        Returned = 7,
        VisitCommitted = 8,
        Unknown = 9,
    }

    internal enum PalacePaidAction
    {
        Begin = 0,
        Cancel = 1,
        ObservePaid = 2,
        ObserveSettlement = 3,
        Capture = 4,
        Resolve = 5,
        ClaimDeparture = 6,
        MarkEntered = 7,
        MarkReturned = 8,
        CommitVisit = 9,
        MarkUnknown = 10,
    }

    internal enum PalacePaidOutcome
    {
        Changed = 0,
        NoChange = 1,
        Rejected = 2,
    }

    internal enum PalacePaidError
    {
        None = 0,
        // 输入层
        Empty = 1,
        TooLarge = 2,
        Bom = 3,
        Utf8 = 4,
        Syntax = 5,
        Depth = 6,
        Trailing = 7,
        DuplicateKey = 8,
        UnknownKey = 9,
        MissingField = 10,
        WrongType = 11,
        BadValue = 12,
        // 语义层（构造/解析）
        BadStage = 20,
        BadVersion = 21,
        BadScope = 22,
        BadAnchor = 23,
        BadCurrencies = 24,
        BadProof = 25,
        BadRecord = 26,
        BadEnvelope = 27,
        // 期望比对
        OwnerMismatch = 30,
        PayerMismatch = 31,
        // CAS 期望
        ExpectationMismatch = 35,
        ExpectationUnbound = 36,
        // 状态机
        NoRecord = 40,
        Undecided = 41,
        Terminal = 42,
        NotAllowed = 43,
        Rollback = 44,
        StickyUnknown = 45,
        TripIdMismatch = 46,
        TripIdReuse = 47,
        RevisionOverflow = 48,
        ProofConflict = 49,
        ProofMismatch = 50,
        Payload = 51,
    }

    // 契约违例（构造/纯函数的前置条件）。codec 会把它们映射成错误码，不会让异常穿透成可用 record。
    internal sealed class PalacePaidContractException : Exception
    {
        public PalacePaidContractException(string message) : base(message)
        {
        }
    }

    // ───────────────────────── 文本与编码规则 ─────────────────────────

    internal static class PalacePaidText
    {
        // 标识符域：1..64 字符，[A-Za-z0-9._-]；用于 owner.profile 与 proof.payer。
        // 不接受路径分隔符、':'、空白、非 ASCII；比较一律 StringComparison.Ordinal（不做大小写折叠）。
        public static bool IsIdentifier(string? value, int min, int max)
        {
            if (value == null) return false;
            if (value.Length < min || value.Length > max) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // 小写 hex 域：长度精确匹配，字符 [0-9a-f]；不接受大写（规范化规则 = 拒绝而非转换）。
        public static bool IsLowerHex(string? value, int length)
        {
            if (value == null || value.Length != length) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        // GUID 接受域：36 字符、8-4-4-4-12、全部小写 hex、必须带连字符（即 "D" 格式）。
        public static bool IsCanonicalGuid(string? value)
        {
            if (value == null || value.Length != 36) return false;
            for (int i = 0; i < 36; i++)
            {
                char c = value[i];
                if (i == 8 || i == 13 || i == 18 || i == 23)
                {
                    if (c != '-') return false;
                    continue;
                }
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        // Unknown 的 reason：1..200 个可打印 ASCII（0x20..0x7E，含引号与反斜杠，序列化时转义）。
        public static bool IsReason(string? value)
        {
            if (value == null || value.Length < 1 || value.Length > 200) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < (char)0x20 || c > (char)0x7E) return false;
            }
            return true;
        }

        public static string ToLowerHex(ReadOnlySpan<byte> bytes)
        {
            const string digits = "0123456789abcdef";
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                chars[i * 2] = digits[b >> 4];
                chars[i * 2 + 1] = digits[b & 0x0F];
            }
            return new string(chars);
        }

        public static void AppendQuoted(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c < (char)0x20)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append('"');
        }

        public static void AppendInt(StringBuilder sb, string prefix, int value)
        {
            sb.Append(prefix).Append(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    // ───────────────────────── 结果类型 ─────────────────────────

    internal readonly struct PalacePaidResult<T> where T : class
    {
        public bool Ok { get; }
        public T? Value { get; }
        public PalacePaidError Error { get; }
        public string? Detail { get; }

        private PalacePaidResult(bool ok, T? value, PalacePaidError error, string? detail)
        {
            Ok = ok;
            Value = value;
            Error = error;
            Detail = detail;
        }

        public static PalacePaidResult<T> Success(T value)
        {
            return new PalacePaidResult<T>(true, value, PalacePaidError.None, null);
        }

        public static PalacePaidResult<T> Fail(PalacePaidError error, string? detail)
        {
            return new PalacePaidResult<T>(false, null, error, detail);
        }

        public string Describe()
        {
            if (Ok) return "ok";
            return Error.ToString() + (Detail == null ? string.Empty : ": " + Detail);
        }
    }

    // Apply 的唯一返回类型：Changed（新 envelope + canonical bytes + SHA）/ NoChange（原对象，未变）
    // / Rejected（错误码；不产生可用 envelope）。
    // CanonicalBytes 内部持有防御副本：读出恒为克隆，外部改写读出的数组既不能改变本结果的内部状态，
    // 也不能改变 CanonicalSha256；Changed 的 hash 由内部字节现场计算一次，不接受调用方传入的 hash。
    internal readonly struct PalacePaidApplyResult
    {
        private readonly byte[]? _canonicalBytes;

        public PalacePaidOutcome Outcome { get; }
        public PalacePaidEnvelope? Envelope { get; }
        public byte[]? CanonicalBytes { get { return _canonicalBytes == null ? null : (byte[])_canonicalBytes.Clone(); } }
        public string? CanonicalSha256 { get; }
        public string? SourceSha256 { get; }
        public PalacePaidError Error { get; }
        public string? Detail { get; }

        private PalacePaidApplyResult(PalacePaidOutcome outcome, PalacePaidEnvelope? envelope, byte[]? bytes, string? canonicalSha, string? sourceSha, PalacePaidError error, string? detail)
        {
            Outcome = outcome;
            Envelope = envelope;
            _canonicalBytes = bytes;
            CanonicalSha256 = canonicalSha;
            SourceSha256 = sourceSha;
            Error = error;
            Detail = detail;
        }

        // bytes 存入防御副本，hash 由同一副本计算，二者恒一致；不存在“bytes 与 hash 不一致仍产 Changed”的路径。
        public static PalacePaidApplyResult Changed(PalacePaidEnvelope envelope, byte[] canonicalBytes)
        {
            if (envelope == null)
            {
                throw new PalacePaidContractException("envelope is required for a Changed result");
            }
            if (canonicalBytes == null)
            {
                throw new PalacePaidContractException("canonicalBytes are required for a Changed result");
            }
            byte[] owned = (byte[])canonicalBytes.Clone();
            return new PalacePaidApplyResult(PalacePaidOutcome.Changed, envelope, owned, PalacePaidCodec.Sha256Hex(owned), null, PalacePaidError.None, null);
        }

        public static PalacePaidApplyResult NoChange(PalacePaidEnvelope existing)
        {
            return new PalacePaidApplyResult(PalacePaidOutcome.NoChange, existing, null, null, existing.SourceBytesSha256, PalacePaidError.None, null);
        }

        public static PalacePaidApplyResult Rejected(PalacePaidError error, string? detail)
        {
            return new PalacePaidApplyResult(PalacePaidOutcome.Rejected, null, null, null, null, error, detail);
        }
    }

    // ───────────────────────── scope（owner 七元组） ─────────────────────────

    internal sealed class PalacePaidScope
    {
        public const int RequiredBiome = 5;
        public const int RequiredPlayerSlot = 1;
        // ScopeKey 域标签：防止同一 SHA 在其它域被复用。
        private const string ScopeKeyDomain = "palace-paid-scope-key/v1";

        private string? _scopeKey;

        public string Profile { get; }
        public Guid CampaignGuid { get; }
        public int CampaignSlot { get; }
        public int Biome { get; }
        public int Reign { get; }
        // start 必须是调用方显式提供的 campaign.realStartDateTime 的 int；库不提供默认值、不用 0/哨兵代缺失。
        public int Start { get; }
        public int PlayerSlot { get; }

        public PalacePaidScope(string profile, Guid campaignGuid, int campaignSlot, int biome, int reign, int start, int playerSlot)
        {
            if (!PalacePaidText.IsIdentifier(profile, 1, 64))
            {
                throw new PalacePaidContractException("owner.profile must be 1..64 chars of [A-Za-z0-9._-]");
            }
            if (campaignGuid == Guid.Empty)
            {
                throw new PalacePaidContractException("owner.campaignGuid must be a non-empty Guid");
            }
            if (campaignSlot < 0)
            {
                throw new PalacePaidContractException("owner.campaignSlot must be >= 0");
            }
            if (biome != RequiredBiome)
            {
                throw new PalacePaidContractException("owner.biome must be 5");
            }
            if (reign < 0)
            {
                throw new PalacePaidContractException("owner.reign must be >= 0");
            }
            if (playerSlot != RequiredPlayerSlot)
            {
                throw new PalacePaidContractException("owner.playerSlot must be 1");
            }

            Profile = profile;
            CampaignGuid = campaignGuid;
            CampaignSlot = campaignSlot;
            Biome = biome;
            Reign = reign;
            Start = start;
            PlayerSlot = playerSlot;
        }

        public bool ContentEquals(PalacePaidScope? other)
        {
            if (other == null) return false;
            return string.Equals(Profile, other.Profile, StringComparison.Ordinal)
                && CampaignGuid == other.CampaignGuid
                && CampaignSlot == other.CampaignSlot
                && Biome == other.Biome
                && Reign == other.Reign
                && Start == other.Start
                && PlayerSlot == other.PlayerSlot;
        }

        // ScopeKey 算法（纯函数，无 IO）：
        //   payload = 字段序列，按固定次序 [profile, campaignGuid, campaignSlot, biome, reign, start, playerSlot]，
        //   每项先写 4 字节大端 uint32 长度，再写该项 UTF-8 字节；域标签 "palace-paid-scope-key/v1" 作为第 0 项。
        //   字段文本：profile 原样（已限字符集；不做大小写折叠）；campaignGuid 为 "D" 格式小写 36 字符；
        //   其余 int 为 InvariantCulture 十进制（负号仅在值为负时出现）。
        //   对 payload 做 SHA-256，返回小写 64 hex。
        public string ScopeKey()
        {
            string? cached = _scopeKey;
            if (cached != null) return cached;

            using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendScopeKeyField(hash, ScopeKeyDomain);
                AppendScopeKeyField(hash, Profile);
                AppendScopeKeyField(hash, CampaignGuid.ToString("D"));
                AppendScopeKeyField(hash, CampaignSlot.ToString(CultureInfo.InvariantCulture));
                AppendScopeKeyField(hash, Biome.ToString(CultureInfo.InvariantCulture));
                AppendScopeKeyField(hash, Reign.ToString(CultureInfo.InvariantCulture));
                AppendScopeKeyField(hash, Start.ToString(CultureInfo.InvariantCulture));
                AppendScopeKeyField(hash, PlayerSlot.ToString(CultureInfo.InvariantCulture));

                byte[] digest = hash.GetHashAndReset();
                string key = PalacePaidText.ToLowerHex(digest);
                _scopeKey = key;
                return key;
            }
        }

        // 文件名算法：只返回安全字符串，不做任何 IO。形如 "palace-paid-<64hex>.json"（共 81 字符，
        // 字符集 [a-z0-9.-]）。真实路径拼接与落盘由 R4 负责。
        public string StoreFileName()
        {
            return "palace-paid-" + ScopeKey() + ".json";
        }

        public static bool IsSafeStoreFileName(string? name)
        {
            if (name == null || name.Length < 1 || name.Length > 128) return false;
            if (name == "." || name == "..") return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        private static void AppendScopeKeyField(IncrementalHash hash, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
    }

    // ───────────────────────── anchor 镜像 ─────────────────────────
    // 自有镜像，不重新定义游戏的 PalaceEntryAnchorData。
    // JSON 名逐字 lowerCamel：entryX, entryY, entryZ, sourcePlayerX, standX, side。
    // 0 是合法坐标；缺锚用 null（record.anchor 非 null）。
    internal sealed class PalacePaidAnchor
    {
        public const double StandXTolerance = 0.05;

        public float EntryX { get; }
        public float EntryY { get; }
        public float EntryZ { get; }
        public float SourcePlayerX { get; }
        public float StandX { get; }
        // Side 为整数 -1 或 1，不从 SourcePlayerX 反推；参与 standX 校验的只有 entryX/standX/side。
        public int Side { get; }

        public PalacePaidAnchor(float entryX, float entryY, float entryZ, float sourcePlayerX, float standX, int side)
        {
            if (!float.IsFinite(entryX) || !float.IsFinite(entryY) || !float.IsFinite(entryZ) || !float.IsFinite(sourcePlayerX) || !float.IsFinite(standX))
            {
                throw new PalacePaidContractException("anchor floats must all be finite");
            }
            if (side != 1 && side != -1)
            {
                throw new PalacePaidContractException("anchor.side must be exactly -1 or 1");
            }

            // 只用 double 运算：禁止先做 float 相减（float 舍入会放宽或收紧 0.05 边界）。
            double delta = Math.Abs((double)standX - (double)entryX - side * 2.0);
            if (delta > StandXTolerance)
            {
                throw new PalacePaidContractException("anchor.standX - anchor.entryX - side*2 must be within +/-0.05 (double math)");
            }

            EntryX = entryX;
            EntryY = entryY;
            EntryZ = entryZ;
            SourcePlayerX = sourcePlayerX;
            StandX = standX;
            Side = side;
        }

        public bool ContentEquals(PalacePaidAnchor? other)
        {
            if (other == null) return false;
            return EntryX.Equals(other.EntryX)
                && EntryY.Equals(other.EntryY)
                && EntryZ.Equals(other.EntryZ)
                && SourcePlayerX.Equals(other.SourcePlayerX)
                && StandX.Equals(other.StandX)
                && Side == other.Side;
        }
    }

    // ───────────────────────── 8 币种（固定次序） ─────────────────────────

    internal static class PalacePaidCurrencies
    {
        public const int Count = 8;
        // 固定次序：[Coins, Gems, Crown, Skulls, Shades, Merchandise, Candle, Egg]；每项必须显式且 >= 0。

        public static int[] CopyValidated(int[]? values, string path)
        {
            if (values == null)
            {
                throw new PalacePaidContractException(path + " is required");
            }
            if (values.Length != Count)
            {
                throw new PalacePaidContractException(path + " must have exactly 8 entries");
            }
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0)
                {
                    throw new PalacePaidContractException(path + "[" + i.ToString(CultureInfo.InvariantCulture) + "] must be >= 0");
                }
            }
            return (int[])values.Clone();
        }

        public static bool ContentEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }

    // ───────────────────────── CaptureProof ─────────────────────────
    // JSON 字段：captureId, owner, sourceLand, payer, currencies, sourceIslandFingerprint,
    //            nativeMarkerSeen, nativeRan, noException
    internal sealed class PalacePaidCaptureProof
    {
        private readonly int[] _currencies;
        private readonly IReadOnlyList<int> _currenciesView;

        public long CaptureId { get; }
        public PalacePaidScope Owner { get; }
        public int SourceLand { get; }
        public string Payer { get; }
        public IReadOnlyList<int> Currencies { get { return _currenciesView; } }
        public string SourceIslandFingerprint { get; }
        public bool NativeMarkerSeen { get; }
        public bool NativeRan { get; }
        public bool NoException { get; }

        public PalacePaidCaptureProof(
            long captureId,
            PalacePaidScope owner,
            int sourceLand,
            string payer,
            int[] currencies,
            string sourceIslandFingerprint,
            bool nativeMarkerSeen,
            bool nativeRan,
            bool noException)
        {
            if (captureId < 1)
            {
                throw new PalacePaidContractException("captureProof.captureId must be >= 1 (session 内单调由 R4 维护，库不做跨记录比较)");
            }
            if (owner == null)
            {
                throw new PalacePaidContractException("captureProof.owner is required");
            }
            if (sourceLand < 0 || sourceLand > 11)
            {
                throw new PalacePaidContractException("captureProof.sourceLand must be 0..11");
            }
            if (!PalacePaidText.IsIdentifier(payer, 1, 64))
            {
                throw new PalacePaidContractException("captureProof.payer must be 1..64 chars of [A-Za-z0-9._-]");
            }
            _currencies = PalacePaidCurrencies.CopyValidated(currencies, "captureProof.currencies");
            if (!PalacePaidText.IsLowerHex(sourceIslandFingerprint, 64))
            {
                throw new PalacePaidContractException("captureProof.sourceIslandFingerprint must be 64 lowercase hex");
            }
            if (!nativeMarkerSeen || !nativeRan || !noException)
            {
                throw new PalacePaidContractException("captureProof requires nativeMarkerSeen && nativeRan && noException");
            }

            _currenciesView = Array.AsReadOnly(_currencies);
            CaptureId = captureId;
            Owner = owner;
            SourceLand = sourceLand;
            Payer = payer;
            SourceIslandFingerprint = sourceIslandFingerprint;
            NativeMarkerSeen = nativeMarkerSeen;
            NativeRan = nativeRan;
            NoException = noException;
        }

        public bool ContentEquals(PalacePaidCaptureProof? other)
        {
            if (other == null) return false;
            return CaptureId == other.CaptureId
                && Owner.ContentEquals(other.Owner)
                && SourceLand == other.SourceLand
                && string.Equals(Payer, other.Payer, StringComparison.Ordinal)
                && string.Equals(SourceIslandFingerprint, other.SourceIslandFingerprint, StringComparison.Ordinal)
                && NativeMarkerSeen == other.NativeMarkerSeen
                && NativeRan == other.NativeRan
                && NoException == other.NoException
                && PalacePaidCurrencies.ContentEquals(_currencies, other._currencies);
        }
    }

    // ───────────────────────── FileProof ─────────────────────────
    // JSON 字段：rawSaveSha256, owner, sourceLand, payer, currencies, sourceIslandFingerprint,
    //            nativeVersion(固定 16), nativeRawResult(必填 int，0..65535，纯形状域)
    internal sealed class PalacePaidFileProof
    {
        public const int RequiredNativeVersion = 16;
        public const int MinRawResult = 0;
        public const int MaxRawResult = 65535;

        private readonly int[] _currencies;
        private readonly IReadOnlyList<int> _currenciesView;

        public string RawSaveSha256 { get; }
        public PalacePaidScope Owner { get; }
        public int SourceLand { get; }
        public string Payer { get; }
        public IReadOnlyList<int> Currencies { get { return _currenciesView; } }
        public string SourceIslandFingerprint { get; }
        public int NativeVersion { get; }
        public int NativeRawResult { get; }

        public PalacePaidFileProof(
            string rawSaveSha256,
            PalacePaidScope owner,
            int sourceLand,
            string payer,
            int[] currencies,
            string sourceIslandFingerprint,
            int nativeVersion,
            int nativeRawResult)
        {
            if (!PalacePaidText.IsLowerHex(rawSaveSha256, 64))
            {
                throw new PalacePaidContractException("fileProof.rawSaveSha256 must be 64 lowercase hex");
            }
            if (owner == null)
            {
                throw new PalacePaidContractException("fileProof.owner is required");
            }
            if (sourceLand < 0 || sourceLand > 11)
            {
                throw new PalacePaidContractException("fileProof.sourceLand must be 0..11");
            }
            if (!PalacePaidText.IsIdentifier(payer, 1, 64))
            {
                throw new PalacePaidContractException("fileProof.payer must be 1..64 chars of [A-Za-z0-9._-]");
            }
            _currencies = PalacePaidCurrencies.CopyValidated(currencies, "fileProof.currencies");
            if (!PalacePaidText.IsLowerHex(sourceIslandFingerprint, 64))
            {
                throw new PalacePaidContractException("fileProof.sourceIslandFingerprint must be 64 lowercase hex");
            }
            if (nativeVersion != RequiredNativeVersion)
            {
                throw new PalacePaidContractException("fileProof.nativeVersion must be 16");
            }
            // 形状域：0..65535 内任何值都合法（含 72/73/Failure 语义值）。库不判定成功、绝不默认填 72。
            if (nativeRawResult < MinRawResult || nativeRawResult > MaxRawResult)
            {
                throw new PalacePaidContractException("fileProof.nativeRawResult must be within 0..65535");
            }

            _currenciesView = Array.AsReadOnly(_currencies);
            RawSaveSha256 = rawSaveSha256;
            Owner = owner;
            SourceLand = sourceLand;
            Payer = payer;
            SourceIslandFingerprint = sourceIslandFingerprint;
            NativeVersion = nativeVersion;
            NativeRawResult = nativeRawResult;
        }

        // 物理匹配：与 CaptureProof 逐项一致（owner 全七元组 / sourceLand / payer / 8 币种 / fingerprint）。
        // 真实 native 成功资格由 Root 另行判定，不在此处表达。
        public bool Matches(PalacePaidCaptureProof? capture)
        {
            if (capture == null) return false;
            return Owner.ContentEquals(capture.Owner)
                && SourceLand == capture.SourceLand
                && string.Equals(Payer, capture.Payer, StringComparison.Ordinal)
                && string.Equals(SourceIslandFingerprint, capture.SourceIslandFingerprint, StringComparison.Ordinal)
                && PalacePaidCurrencies.ContentEquals(_currencies, capture.Currencies);
        }

        public bool ContentEquals(PalacePaidFileProof? other)
        {
            if (other == null) return false;
            return string.Equals(RawSaveSha256, other.RawSaveSha256, StringComparison.Ordinal)
                && Owner.ContentEquals(other.Owner)
                && SourceLand == other.SourceLand
                && string.Equals(Payer, other.Payer, StringComparison.Ordinal)
                && string.Equals(SourceIslandFingerprint, other.SourceIslandFingerprint, StringComparison.Ordinal)
                && NativeVersion == other.NativeVersion
                && NativeRawResult == other.NativeRawResult
                && PalacePaidCurrencies.ContentEquals(_currencies, other._currencies);
        }
    }

    // ───────────────────────── record ─────────────────────────
    // JSON 字段次序：tripId, revision, stage, owner, sourceLand, palaceLand, anchor,
    //                onPayObserved, settlementObserved, captureProof, fileProof, reason
    internal sealed class PalacePaidRecord
    {
        public const int RequiredPalaceLand = 12;

        public Guid TripId { get; }
        public int Revision { get; }
        public PalacePaidStage Stage { get; }
        public PalacePaidScope Owner { get; }
        // sourceLand 属于 record，不属于 ScopeKey；0..11，绝不等于 palaceLand(12)。
        public int SourceLand { get; }
        public int PalaceLand { get; }
        public PalacePaidAnchor Anchor { get; }
        public PalaceLayoutRecipe? DestinationLayout { get; }
        public bool OnPayObserved { get; }
        public bool SettlementObserved { get; }
        public PalacePaidCaptureProof? CaptureProof { get; }
        public PalacePaidFileProof? FileProof { get; }
        public string? Reason { get; }

        public PalacePaidRecord(
            Guid tripId,
            int revision,
            PalacePaidStage stage,
            PalacePaidScope owner,
            int sourceLand,
            int palaceLand,
            PalacePaidAnchor anchor,
            bool onPayObserved,
            bool settlementObserved,
            PalacePaidCaptureProof? captureProof,
            PalacePaidFileProof? fileProof,
            string? reason,
            PalaceLayoutRecipe? destinationLayout = null)
        {
            if (destinationLayout != null && !PalaceLayoutRecipe.IsKnown(destinationLayout))
                throw new PalacePaidContractException("record.destinationLayout must be known or legacy unknown");
            if (tripId == Guid.Empty)
            {
                throw new PalacePaidContractException("record.tripId must be a non-empty Guid");
            }
            if (revision < 1)
            {
                throw new PalacePaidContractException("record.revision must be >= 1 (新 trip = 1)");
            }
            if (!PalacePaidStages.IsDefined(stage))
            {
                throw new PalacePaidContractException("record.stage is not a defined stage");
            }
            if (owner == null)
            {
                throw new PalacePaidContractException("record.owner is required");
            }
            if (sourceLand < 0 || sourceLand > 11)
            {
                throw new PalacePaidContractException("record.sourceLand must be 0..11");
            }
            if (palaceLand != RequiredPalaceLand)
            {
                throw new PalacePaidContractException("record.palaceLand must be 12");
            }
            if (anchor == null)
            {
                throw new PalacePaidContractException("record.anchor is required (missing anchor is null, and null is not accepted)");
            }
            if (stage == PalacePaidStage.Unknown)
            {
                if (!PalacePaidText.IsReason(reason))
                {
                    throw new PalacePaidContractException("record.reason must be 1..200 printable ASCII when stage is Unknown");
                }
            }
            else if (reason != null)
            {
                throw new PalacePaidContractException("record.reason must be null unless stage is Unknown");
            }

            // stage/facts/proof/revision 阶梯（revision 对非 Unknown 阶段是确定的，Unknown 为 >= 2）。
            switch (stage)
            {
                case PalacePaidStage.ChargeIntent:
                    RequireRevision(revision, 1);
                    RequireUnpaid(onPayObserved, settlementObserved);
                    RequireNoProofs(captureProof, fileProof);
                    break;
                case PalacePaidStage.Cancelled:
                    RequireRevision(revision, 2);
                    RequireUnpaid(onPayObserved, settlementObserved);
                    RequireNoProofs(captureProof, fileProof);
                    break;
                case PalacePaidStage.PaidObserved:
                    if (!onPayObserved)
                    {
                        throw new PalacePaidContractException("PaidObserved requires onPayObserved = true");
                    }
                    RequireNoProofs(captureProof, fileProof);
                    RequireRevision(revision, settlementObserved ? 3 : 2);
                    break;
                case PalacePaidStage.SourceCaptured:
                    RequireSettled(onPayObserved, settlementObserved);
                    RequireRevision(revision, 4);
                    RequireCapture(captureProof);
                    if (fileProof != null)
                    {
                        throw new PalacePaidContractException("fileProof is not allowed before Resumable");
                    }
                    break;
                case PalacePaidStage.Resumable:
                    RequireSettled(onPayObserved, settlementObserved);
                    RequireRevision(revision, 5);
                    RequireCapture(captureProof);
                    RequireFile(fileProof);
                    break;
                case PalacePaidStage.DepartureClaimed:
                    RequireSettled(onPayObserved, settlementObserved);
                    RequireRevision(revision, 6);
                    RequireCapture(captureProof);
                    RequireFile(fileProof);
                    break;
                case PalacePaidStage.Entered:
                    RequireSettled(onPayObserved, settlementObserved);
                    RequireRevision(revision, 7);
                    RequireCapture(captureProof);
                    RequireFile(fileProof);
                    break;
                case PalacePaidStage.Returned:
                    RequireSettled(onPayObserved, settlementObserved);
                    RequireRevision(revision, 8);
                    RequireCapture(captureProof);
                    RequireFile(fileProof);
                    break;
                case PalacePaidStage.VisitCommitted:
                    RequireSettled(onPayObserved, settlementObserved);
                    RequireRevision(revision, 9);
                    RequireCapture(captureProof);
                    RequireFile(fileProof);
                    break;
                case PalacePaidStage.Unknown:
                    // 保留进入 Unknown 前的数据；只接受与已有 receipt/proof 一致的形状。
                    if (revision < 2)
                    {
                        throw new PalacePaidContractException("Unknown requires revision >= 2");
                    }
                    if (settlementObserved && !onPayObserved)
                    {
                        throw new PalacePaidContractException("Unknown: settlementObserved requires onPayObserved");
                    }
                    if (captureProof != null && !(onPayObserved && settlementObserved))
                    {
                        throw new PalacePaidContractException("Unknown: captureProof requires onPayObserved and settlementObserved");
                    }
                    if (fileProof != null && captureProof == null)
                    {
                        throw new PalacePaidContractException("Unknown: fileProof requires captureProof");
                    }
                    if (!onPayObserved && (settlementObserved || captureProof != null || fileProof != null))
                    {
                        throw new PalacePaidContractException("Unknown: proof facts cannot exist without onPayObserved");
                    }
                    // 可达性下限：Unknown 的最小 revision = 2 + 已持有事实数
                    // （无事实 2 / 仅 onPay 3 / +settlement 4 / +capture 5 / +file 6），低 1 即拒；
                    // 只规定下限不锁死等值，同事实的 reason 更新仍可多次 +1。
                    int unknownMinimumRevision = 2;
                    if (onPayObserved) unknownMinimumRevision++;
                    if (settlementObserved) unknownMinimumRevision++;
                    if (captureProof != null) unknownMinimumRevision++;
                    if (fileProof != null) unknownMinimumRevision++;
                    if (revision < unknownMinimumRevision)
                    {
                        throw new PalacePaidContractException("Unknown revision must be >= " + unknownMinimumRevision.ToString(CultureInfo.InvariantCulture) + " for the facts it carries (earliest legal path)");
                    }
                    break;
            }

            if (captureProof != null)
            {
                if (!captureProof.Owner.ContentEquals(owner))
                {
                    throw new PalacePaidContractException("captureProof.owner must equal record.owner");
                }
                if (captureProof.SourceLand != sourceLand)
                {
                    throw new PalacePaidContractException("captureProof.sourceLand must equal record.sourceLand");
                }
            }

            if (fileProof != null)
            {
                if (captureProof == null)
                {
                    throw new PalacePaidContractException("fileProof requires captureProof");
                }
                if (!fileProof.Owner.ContentEquals(owner))
                {
                    throw new PalacePaidContractException("fileProof.owner must equal record.owner");
                }
                if (fileProof.SourceLand != sourceLand)
                {
                    throw new PalacePaidContractException("fileProof.sourceLand must equal record.sourceLand");
                }
                if (!fileProof.Matches(captureProof))
                {
                    throw new PalacePaidContractException("fileProof must match captureProof field by field");
                }
            }

            TripId = tripId;
            Revision = revision;
            Stage = stage;
            Owner = owner;
            SourceLand = sourceLand;
            PalaceLand = palaceLand;
            Anchor = anchor;
            DestinationLayout = destinationLayout;
            OnPayObserved = onPayObserved;
            SettlementObserved = settlementObserved;
            CaptureProof = captureProof;
            FileProof = fileProof;
            Reason = reason;
        }

        private static void RequireRevision(int actual, int expected)
        {
            if (actual != expected)
            {
                throw new PalacePaidContractException("revision must be " + expected.ToString(CultureInfo.InvariantCulture) + " at this stage (no skipping, no rollback)");
            }
        }

        private static void RequireUnpaid(bool onPay, bool settlement)
        {
            if (onPay || settlement)
            {
                throw new PalacePaidContractException("payment facts must stay false at this stage");
            }
        }

        private static void RequireSettled(bool onPay, bool settlement)
        {
            if (!onPay || !settlement)
            {
                throw new PalacePaidContractException("this stage requires onPayObserved = true and settlementObserved = true");
            }
        }

        private static void RequireNoProofs(PalacePaidCaptureProof? capture, PalacePaidFileProof? file)
        {
            if (capture != null || file != null)
            {
                throw new PalacePaidContractException("proofs are not allowed at this stage");
            }
        }

        private static void RequireCapture(PalacePaidCaptureProof? capture)
        {
            if (capture == null)
            {
                throw new PalacePaidContractException("this stage requires a full captureProof");
            }
        }

        private static void RequireFile(PalacePaidFileProof? file)
        {
            if (file == null)
            {
                throw new PalacePaidContractException("this stage requires a matching fileProof");
            }
        }

        public bool ContentEquals(PalacePaidRecord? other)
        {
            if (other == null) return false;
            if (TripId != other.TripId || Revision != other.Revision || Stage != other.Stage) return false;
            if (!Owner.ContentEquals(other.Owner)) return false;
            if (SourceLand != other.SourceLand || PalaceLand != other.PalaceLand) return false;
            if (!ReferenceEquals(DestinationLayout, other.DestinationLayout)) return false;
            if (!Anchor.ContentEquals(other.Anchor)) return false;
            if (OnPayObserved != other.OnPayObserved || SettlementObserved != other.SettlementObserved) return false;
            if ((CaptureProof == null) != (other.CaptureProof == null)) return false;
            if (CaptureProof != null && !CaptureProof.ContentEquals(other.CaptureProof)) return false;
            if ((FileProof == null) != (other.FileProof == null)) return false;
            if (FileProof != null && !FileProof.ContentEquals(other.FileProof)) return false;
            return string.Equals(Reason, other.Reason, StringComparison.Ordinal);
        }
    }

    // ───────────────────────── envelope（current + 最多一笔 terminal history） ─────────────────────────
    internal sealed class PalacePaidEnvelope
    {
        public const int CurrentSchema = 2;
        public const int LegacySchema = 1;
        public const int MaxHistory = 1;

        private readonly IReadOnlyList<PalacePaidRecord> _history;

        public int Schema { get; }
        public PalacePaidRecord Current { get; }
        public IReadOnlyList<PalacePaidRecord> History { get { return _history; } }
        // 来源绑定：由 PalacePaidCodec.Parse 经 FromRawBytes 用解析所用的那一份原始 bytes 设置
        // （该 bytes 的 SHA-256 小写 hex）；纯状态机产出的新 envelope 与一切手工构造的 envelope 恒为 null。
        // 不属于线格式，canonical 序列化不写它。
        private readonly string? _sourceBytesSha256;
        public string? SourceBytesSha256 { get { return _sourceBytesSha256; } }

        // 公开构造恒为 unbound：不接受任何 hash/bytes 绑定参数。
        public PalacePaidEnvelope(
            PalacePaidRecord current,
            IReadOnlyList<PalacePaidRecord>? history = null,
            // Compatibility default for legacy DTO callers. New Begin explicitly selects schema2.
            int schema = LegacySchema)
            : this(current, history, schema, null)
        {
        }

        // 绑定构造：private，只有 FromRawBytes 能到达；hash 与 envelope 内容来自同一份 raw bytes。
        private PalacePaidEnvelope(
            PalacePaidRecord current,
            IReadOnlyList<PalacePaidRecord>? history,
            int schema,
            string? boundRawSha)
        {
            if (current == null)
            {
                throw new PalacePaidContractException("envelope.current is required");
            }
            if (schema != LegacySchema && schema != CurrentSchema)
                throw new PalacePaidContractException("envelope.schema must be 1 or 2");
            if (schema == LegacySchema && current.DestinationLayout != null)
                throw new PalacePaidContractException("schema1 current has no layout");
            if (schema == CurrentSchema && !PalaceLayoutRecipe.IsKnown(current.DestinationLayout!))
                throw new PalacePaidContractException("schema2 current requires a known layout");
            if (boundRawSha != null && !PalacePaidText.IsLowerHex(boundRawSha, 64))
            {
                throw new PalacePaidContractException("envelope.sourceBytesSha256 must be 64 lowercase hex");
            }

            PalacePaidRecord[] items;
            if (history == null || history.Count == 0)
            {
                items = Array.Empty<PalacePaidRecord>();
            }
            else
            {
                items = new PalacePaidRecord[history.Count];
                for (int i = 0; i < history.Count; i++)
                {
                    items[i] = history[i];
                }
            }
            if (items.Length > MaxHistory)
            {
                throw new PalacePaidContractException("envelope.history may hold at most 1 record");
            }
            for (int i = 0; i < items.Length; i++)
            {
                PalacePaidRecord item = items[i];
                if (item == null)
                {
                    throw new PalacePaidContractException("envelope.history entries must be full records");
                }
                if (schema == LegacySchema && item.DestinationLayout != null)
                    throw new PalacePaidContractException("schema1 history has no layout");
                if (!PalacePaidStages.IsTerminal(item.Stage))
                {
                    throw new PalacePaidContractException("envelope.history may only hold terminal records");
                }
                if (item.TripId == current.TripId)
                {
                    throw new PalacePaidContractException("envelope.history must not reuse current.tripId");
                }
                if (!item.Owner.ContentEquals(current.Owner))
                {
                    throw new PalacePaidContractException("envelope.current and envelope.history must share the exact same owner");
                }
            }

            Schema = schema;
            Current = current;
            _history = Array.AsReadOnly(items);
            _sourceBytesSha256 = boundRawSha;
        }

        // 唯一的来源绑定入口：raw bytes 必须严格解析成功，envelope 内容就是该解析结果本身，
        // hash 是同一份 bytes 的 SHA-256——不存在“候选内容 + 任意 hash”的入口，也不接受调用方 hash 字符串。
        // 解析失败时原样返回失败结果（Value 恒为 null），不产生部分可用的 envelope。
        internal static PalacePaidResult<PalacePaidEnvelope> FromRawBytes(ReadOnlySpan<byte> rawBytes)
        {
            // Reject out-of-budget input before allocating, then parse and hash one owned snapshot.
            // ReadOnlySpan does not prevent its caller from concurrently changing the backing array.
            if (rawBytes.Length == 0)
                return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.Empty, "input is empty");
            if (rawBytes.Length > PalacePaidCodec.MaxBytes)
                return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.TooLarge, "input exceeds 131072 bytes");
            byte[] snapshot = rawBytes.ToArray();
            if (!PalacePaidCodec.TryParseStrict(snapshot, out int schema, out PalacePaidRecord? current, out List<PalacePaidRecord>? history, out PalacePaidError error, out string? detail))
            {
                return PalacePaidResult<PalacePaidEnvelope>.Fail(error, detail);
            }
            try
            {
                return PalacePaidResult<PalacePaidEnvelope>.Success(new PalacePaidEnvelope(current!, history, schema, PalacePaidCodec.Sha256Hex(snapshot)));
            }
            catch (PalacePaidContractException ex)
            {
                return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.BadEnvelope, ex.Message);
            }
        }

        public bool ContentEquals(PalacePaidEnvelope? other)
        {
            if (other == null) return false;
            if (Schema != other.Schema) return false;
            if (!Current.ContentEquals(other.Current)) return false;
            if (History.Count != other.History.Count) return false;
            for (int i = 0; i < History.Count; i++)
            {
                if (!History[i].ContentEquals(other.History[i])) return false;
            }
            return true;
        }
    }

    // ───────────────────────── 期望（owner + 可选 payer） ─────────────────────────

    internal sealed class PalacePaidExpectation
    {
        public PalacePaidScope Owner { get; }
        // 可选：非 null 时描述当前 trip 的 payer（保存行证据 id）：只核 current record 的 proof 逐字符相等；
        // current 尚无 proof 时不检查，history 的 payer 不参与比较。
        public string? Payer { get; }

        public PalacePaidExpectation(PalacePaidScope owner, string? payer = null)
        {
            if (owner == null)
            {
                throw new PalacePaidContractException("expectation.owner is required");
            }
            if (payer != null && !PalacePaidText.IsIdentifier(payer, 1, 64))
            {
                throw new PalacePaidContractException("expectation.payer must be 1..64 chars of [A-Za-z0-9._-]");
            }
            Owner = owner;
            Payer = payer;
        }
    }

    // ───────────────────────── 请求 ─────────────────────────
    // SourceLand / Anchor 仅对 Begin 有意义；其余 action 的这两个字段被忽略（工厂填 0 / null）。
    internal sealed class PalacePaidRequest
    {
        public PalacePaidAction Action { get; }
        public Guid TripId { get; }
        public PalacePaidExpectation Expectation { get; }
        public int SourceLand { get; }
        public PalacePaidAnchor? Anchor { get; }
        public PalaceLayoutRecipe? DestinationLayout { get; }
        public PalacePaidCaptureProof? CaptureProof { get; }
        public PalacePaidFileProof? FileProof { get; }
        public string? Reason { get; }

        private PalacePaidRequest(
            PalacePaidAction action,
            PalacePaidExpectation expectation,
            Guid tripId,
            int sourceLand,
            PalacePaidAnchor? anchor,
            PalacePaidCaptureProof? captureProof,
            PalacePaidFileProof? fileProof,
            string? reason,
            PalaceLayoutRecipe? destinationLayout = null)
        {
            if (expectation == null)
            {
                throw new PalacePaidContractException("request.expectation is required");
            }
            Action = action;
            Expectation = expectation;
            DestinationLayout = destinationLayout;
            TripId = tripId;
            SourceLand = sourceLand;
            Anchor = anchor;
            CaptureProof = captureProof;
            FileProof = fileProof;
            Reason = reason;
        }

        public static PalacePaidRequest Begin(PalacePaidExpectation expectation, Guid tripId, int sourceLand, PalacePaidAnchor anchor, PalaceLayoutRecipe destinationLayout)
        {
            if (anchor == null)
            {
                throw new PalacePaidContractException("Begin requires an anchor");
            }
            if (!PalaceLayoutRecipe.IsKnown(destinationLayout))
                throw new PalacePaidContractException("Begin requires a known destination layout");
            return new PalacePaidRequest(PalacePaidAction.Begin, expectation, tripId, sourceLand, anchor, null, null, null, destinationLayout);
        }

        public static PalacePaidRequest Cancel(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.Cancel, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest ObservePaid(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.ObservePaid, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest ObserveSettlement(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.ObserveSettlement, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest Capture(PalacePaidExpectation expectation, Guid tripId, PalacePaidCaptureProof captureProof)
        {
            if (captureProof == null)
            {
                throw new PalacePaidContractException("Capture requires a full captureProof");
            }
            return new PalacePaidRequest(PalacePaidAction.Capture, expectation, tripId, 0, null, captureProof, null, null);
        }

        public static PalacePaidRequest Resolve(PalacePaidExpectation expectation, Guid tripId, PalacePaidFileProof fileProof)
        {
            if (fileProof == null)
            {
                throw new PalacePaidContractException("Resolve requires a fileProof");
            }
            return new PalacePaidRequest(PalacePaidAction.Resolve, expectation, tripId, 0, null, null, fileProof, null);
        }

        public static PalacePaidRequest ClaimDeparture(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.ClaimDeparture, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest MarkEntered(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.MarkEntered, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest MarkReturned(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.MarkReturned, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest CommitVisit(PalacePaidExpectation expectation, Guid tripId)
        {
            return new PalacePaidRequest(PalacePaidAction.CommitVisit, expectation, tripId, 0, null, null, null, null);
        }

        public static PalacePaidRequest MarkUnknown(PalacePaidExpectation expectation, Guid tripId, string reason)
        {
            if (reason == null)
            {
                throw new PalacePaidContractException("MarkUnknown requires a reason");
            }
            return new PalacePaidRequest(PalacePaidAction.MarkUnknown, expectation, tripId, 0, null, null, null, reason);
        }
    }

    // ───────────────────────── stage 文本 ─────────────────────────

    internal static class PalacePaidStages
    {
        public static bool IsTerminal(PalacePaidStage stage)
        {
            return stage == PalacePaidStage.Cancelled || stage == PalacePaidStage.VisitCommitted;
        }

        public static bool IsDefined(PalacePaidStage stage)
        {
            return stage >= PalacePaidStage.ChargeIntent && stage <= PalacePaidStage.Unknown;
        }

        public static string Text(PalacePaidStage stage)
        {
            switch (stage)
            {
                case PalacePaidStage.ChargeIntent: return "ChargeIntent";
                case PalacePaidStage.Cancelled: return "Cancelled";
                case PalacePaidStage.PaidObserved: return "PaidObserved";
                case PalacePaidStage.SourceCaptured: return "SourceCaptured";
                case PalacePaidStage.Resumable: return "Resumable";
                case PalacePaidStage.DepartureClaimed: return "DepartureClaimed";
                case PalacePaidStage.Entered: return "Entered";
                case PalacePaidStage.Returned: return "Returned";
                case PalacePaidStage.VisitCommitted: return "VisitCommitted";
                case PalacePaidStage.Unknown: return "Unknown";
                default: throw new PalacePaidContractException("undefined stage value: " + ((int)stage).ToString(CultureInfo.InvariantCulture));
            }
        }

        public static bool TryParse(string? text, out PalacePaidStage stage)
        {
            switch (text)
            {
                case "ChargeIntent": stage = PalacePaidStage.ChargeIntent; return true;
                case "Cancelled": stage = PalacePaidStage.Cancelled; return true;
                case "PaidObserved": stage = PalacePaidStage.PaidObserved; return true;
                case "SourceCaptured": stage = PalacePaidStage.SourceCaptured; return true;
                case "Resumable": stage = PalacePaidStage.Resumable; return true;
                case "DepartureClaimed": stage = PalacePaidStage.DepartureClaimed; return true;
                case "Entered": stage = PalacePaidStage.Entered; return true;
                case "Returned": stage = PalacePaidStage.Returned; return true;
                case "VisitCommitted": stage = PalacePaidStage.VisitCommitted; return true;
                case "Unknown": stage = PalacePaidStage.Unknown; return true;
                default: stage = PalacePaidStage.ChargeIntent; return false;
            }
        }
    }

    // ───────────────────────── 严格 codec ─────────────────────────

    internal static class PalacePaidCodec
    {
        public const int MaxBytes = 131072;   // 128 KiB
        public const int MaxDepth = 12;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        // 严格解析 + 来源绑定：成功时 envelope.SourceBytesSha256 就是本次输入 bytes 的 SHA-256（同一份 bytes）；
        // 绑定只能由 PalacePaidEnvelope.FromRawBytes 产生。失败时不产出任何可用 record（Value 恒为 null）。
        public static PalacePaidResult<PalacePaidEnvelope> Parse(ReadOnlySpan<byte> utf8, PalacePaidExpectation? expectation = null)
        {
            PalacePaidResult<PalacePaidEnvelope> bound = PalacePaidEnvelope.FromRawBytes(utf8);
            if (!bound.Ok || expectation == null)
            {
                return bound;
            }
            PalacePaidResult<PalacePaidEnvelope> validation = PalacePaidJournal.Validate(bound.Value!, expectation);
            if (!validation.Ok)
            {
                return PalacePaidResult<PalacePaidEnvelope>.Fail(validation.Error, validation.Detail);
            }
            return bound;
        }

        // 解析严格性：输入非空、<=128KiB、无 BOM、严格 UTF-8、深度 <=12、单根对象；
        // 每层拒绝重复 key、未知 key、缺字段、错误类型、非整数/非 finite 数字、非法枚举、非 canonical GUID/hex；
        // 只接受显式 null 出现在 anchor(不接受)/captureProof/fileProof/reason 四个位置；
        // 失败时不产出任何可用 record（Value 恒为 null）。本层不构造 envelope、不做绑定。
        internal static bool TryParseStrict(
            ReadOnlySpan<byte> utf8,
            out int schema,
            out PalacePaidRecord? current,
            out List<PalacePaidRecord>? history,
            out PalacePaidError error,
            out string? detail)
        {
            schema = 0;
            current = null;
            history = null;
            error = PalacePaidError.None;
            detail = null;

            if (utf8.Length == 0)
            {
                error = PalacePaidError.Empty;
                detail = "input is empty";
                return false;
            }
            if (utf8.Length > MaxBytes)
            {
                error = PalacePaidError.TooLarge;
                detail = "input exceeds 131072 bytes";
                return false;
            }
            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            {
                error = PalacePaidError.Bom;
                detail = "UTF-8 BOM is not part of the accepted input domain";
                return false;
            }
            try
            {
                StrictUtf8.GetCharCount(utf8);
            }
            catch (DecoderFallbackException)
            {
                error = PalacePaidError.Utf8;
                detail = "input is not valid UTF-8";
                return false;
            }

            PalacePaidError scan;
            try
            {
                scan = ScanDocument(utf8);
            }
            catch (JsonException)
            {
                error = ClassifyJsonFailure(utf8);
                detail = "malformed JSON";
                return false;
            }
            if (scan == PalacePaidError.Syntax)
            {
                error = PalacePaidError.Syntax;
                detail = "document is not a single well-formed JSON value";
                return false;
            }
            if (scan == PalacePaidError.Trailing)
            {
                error = PalacePaidError.Trailing;
                detail = "data after the root value";
                return false;
            }

            var state = new ParseState();
            JsonReaderOptions options = new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaxDepth,
            };
            Utf8JsonReader reader = new Utf8JsonReader(utf8, options);

            bool ok;
            try
            {
                ok = ParseRoot(state, ref reader, out schema, out current, out history);
            }
            catch (JsonException)
            {
                error = ClassifyJsonFailure(utf8);
                detail = "malformed JSON";
                schema = 0;
                current = null;
                history = null;
                return false;
            }
            if (!ok)
            {
                error = state.Error;
                detail = state.Detail;
                schema = 0;
                current = null;
                history = null;
                return false;
            }
            return true;
        }

        // 规范 bytes：固定字段次序、紧凑无空白、InvariantCulture；
        // 已存的 receipt/capture/file 字段全部 roundtrip；SourceBytesSha256 属带外信息，不写入。
        public static byte[] Serialize(PalacePaidEnvelope envelope)
        {
            if (envelope == null)
            {
                throw new PalacePaidContractException("envelope is required");
            }
            var sb = new StringBuilder(1024);
            PalacePaidText.AppendInt(sb, "{\"schema\":", envelope.Schema);
            sb.Append(",\"current\":");
            WriteRecord(sb, envelope.Current, envelope.Schema);
            sb.Append(",\"history\":[");
            for (int i = 0; i < envelope.History.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteRecord(sb, envelope.History[i], envelope.Schema);
            }
            sb.Append("]}");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static string Sha256Hex(ReadOnlySpan<byte> bytes)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(bytes, hash);
            return PalacePaidText.ToLowerHex(hash);
        }

        private static void WriteRecord(StringBuilder sb, PalacePaidRecord record, int schema)
        {
            sb.Append("{\"tripId\":\"").Append(record.TripId.ToString("D")).Append('"');
            PalacePaidText.AppendInt(sb, ",\"revision\":", record.Revision);
            sb.Append(",\"stage\":\"").Append(PalacePaidStages.Text(record.Stage)).Append('"');
            sb.Append(",\"owner\":");
            WriteOwner(sb, record.Owner);
            PalacePaidText.AppendInt(sb, ",\"sourceLand\":", record.SourceLand);
            PalacePaidText.AppendInt(sb, ",\"palaceLand\":", record.PalaceLand);
            sb.Append(",\"anchor\":");
            WriteAnchor(sb, record.Anchor);
            sb.Append(",\"onPayObserved\":").Append(record.OnPayObserved ? "true" : "false");
            sb.Append(",\"settlementObserved\":").Append(record.SettlementObserved ? "true" : "false");
            sb.Append(",\"captureProof\":");
            if (record.CaptureProof == null) sb.Append("null"); else WriteCaptureProof(sb, record.CaptureProof);
            sb.Append(",\"fileProof\":");
            if (record.FileProof == null) sb.Append("null"); else WriteFileProof(sb, record.FileProof);
            sb.Append(",\"reason\":");
            if (record.Reason == null) sb.Append("null"); else PalacePaidText.AppendQuoted(sb, record.Reason);
            if (schema == PalacePaidEnvelope.CurrentSchema)
            {
                sb.Append(",\"destinationLayoutId\":");
                if (record.DestinationLayout == null) sb.Append("null");
                else PalacePaidText.AppendQuoted(sb, record.DestinationLayout.LayoutId);
                sb.Append(",\"destinationLayoutVersion\":");
                if (record.DestinationLayout == null) sb.Append("null");
                else sb.Append(record.DestinationLayout.LayoutVersion.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('}');
        }

        private static void WriteOwner(StringBuilder sb, PalacePaidScope owner)
        {
            sb.Append("{\"profile\":");
            PalacePaidText.AppendQuoted(sb, owner.Profile);
            sb.Append(",\"campaignGuid\":\"").Append(owner.CampaignGuid.ToString("D")).Append('"');
            PalacePaidText.AppendInt(sb, ",\"campaignSlot\":", owner.CampaignSlot);
            PalacePaidText.AppendInt(sb, ",\"biome\":", owner.Biome);
            PalacePaidText.AppendInt(sb, ",\"reign\":", owner.Reign);
            PalacePaidText.AppendInt(sb, ",\"start\":", owner.Start);
            PalacePaidText.AppendInt(sb, ",\"playerSlot\":", owner.PlayerSlot);
            sb.Append('}');
        }

        private static void WriteAnchor(StringBuilder sb, PalacePaidAnchor anchor)
        {
            sb.Append("{\"entryX\":").Append(anchor.EntryX.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"entryY\":").Append(anchor.EntryY.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"entryZ\":").Append(anchor.EntryZ.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"sourcePlayerX\":").Append(anchor.SourcePlayerX.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"standX\":").Append(anchor.StandX.ToString("R", CultureInfo.InvariantCulture));
            PalacePaidText.AppendInt(sb, ",\"side\":", anchor.Side);
            sb.Append('}');
        }

        private static void WriteCurrencies(StringBuilder sb, IReadOnlyList<int> currencies)
        {
            sb.Append('[');
            for (int i = 0; i < currencies.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(currencies[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(']');
        }

        private static void WriteCaptureProof(StringBuilder sb, PalacePaidCaptureProof proof)
        {
            sb.Append("{\"captureId\":").Append(proof.CaptureId.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"owner\":");
            WriteOwner(sb, proof.Owner);
            PalacePaidText.AppendInt(sb, ",\"sourceLand\":", proof.SourceLand);
            sb.Append(",\"payer\":");
            PalacePaidText.AppendQuoted(sb, proof.Payer);
            sb.Append(",\"currencies\":");
            WriteCurrencies(sb, proof.Currencies);
            sb.Append(",\"sourceIslandFingerprint\":\"").Append(proof.SourceIslandFingerprint).Append('"');
            sb.Append(",\"nativeMarkerSeen\":").Append(proof.NativeMarkerSeen ? "true" : "false");
            sb.Append(",\"nativeRan\":").Append(proof.NativeRan ? "true" : "false");
            sb.Append(",\"noException\":").Append(proof.NoException ? "true" : "false");
            sb.Append('}');
        }

        private static void WriteFileProof(StringBuilder sb, PalacePaidFileProof proof)
        {
            sb.Append("{\"rawSaveSha256\":\"").Append(proof.RawSaveSha256).Append('"');
            sb.Append(",\"owner\":");
            WriteOwner(sb, proof.Owner);
            PalacePaidText.AppendInt(sb, ",\"sourceLand\":", proof.SourceLand);
            sb.Append(",\"payer\":");
            PalacePaidText.AppendQuoted(sb, proof.Payer);
            sb.Append(",\"currencies\":");
            WriteCurrencies(sb, proof.Currencies);
            sb.Append(",\"sourceIslandFingerprint\":\"").Append(proof.SourceIslandFingerprint).Append('"');
            PalacePaidText.AppendInt(sb, ",\"nativeVersion\":", proof.NativeVersion);
            PalacePaidText.AppendInt(sb, ",\"nativeRawResult\":", proof.NativeRawResult);
            sb.Append('}');
        }

        // ── 解析实现 ──────────────────────────────────────────────

        private sealed class ParseState
        {
            // Parse-local presence mask; root schema may occur after current/history.
            internal readonly Dictionary<PalacePaidRecord, int> LayoutPresence = new();
            public PalacePaidError Error { get; private set; }
            public string? Detail { get; private set; }

            public bool Fail(PalacePaidError error, string? detail)
            {
                Error = error;
                Detail = detail;
                return false;
            }
        }

        // 文档级形状扫描：整个输入必须恰好是一个完整 JSON 值、深度不超过 MaxDepth(12)、其后除空白外无内容。
        // 深度上限必须在全文档范围生效（包括会被结构解析提前拒绝的区域），因此这里先走一遍 token 流；
        // 扫描代价是 ≤128KiB 的一次线性 pass，换取"深度/尾随"判定的全局性。
        private static PalacePaidError ScanDocument(ReadOnlySpan<byte> utf8)
        {
            JsonReaderOptions options = new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaxDepth,
            };
            Utf8JsonReader reader = new Utf8JsonReader(utf8, options);
            if (!reader.Read())
            {
                return PalacePaidError.Syntax;
            }
            if (!SkipValue(ref reader))
            {
                return PalacePaidError.Syntax;
            }
            if (reader.Read())
            {
                return PalacePaidError.Trailing;
            }
            return PalacePaidError.None;
        }

        private static bool SkipValue(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.StartObject && reader.TokenType != JsonTokenType.StartArray)
            {
                return true;
            }
            int depth = 1;
            while (depth > 0)
            {
                if (!reader.Read())
                {
                    return false;
                }
                if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                {
                    depth++;
                }
                else if (reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray)
                {
                    depth--;
                }
            }
            return true;
        }

        // 深度超限与语法错误都由 Utf8JsonReader 抛 JsonException；用一次放宽深度的纯扫描区分两者。
        private static PalacePaidError ClassifyJsonFailure(ReadOnlySpan<byte> utf8)
        {
            try
            {
                JsonReaderOptions options = new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                };
                Utf8JsonReader reader = new Utf8JsonReader(utf8, options);
                while (reader.Read())
                {
                }
                return PalacePaidError.Depth;
            }
            catch (JsonException)
            {
                return PalacePaidError.Syntax;
            }
        }

        private static bool ParseRoot(
            ParseState state,
            ref Utf8JsonReader reader,
            out int schema,
            out PalacePaidRecord? current,
            out List<PalacePaidRecord>? history)
        {
            schema = 0;
            current = null;
            history = null;

            if (!reader.Read())
            {
                return state.Fail(PalacePaidError.Syntax, "no JSON value");
            }
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return state.Fail(PalacePaidError.WrongType, "root must be an object");
            }

            bool seenSchema = false, seenCurrent = false, seenHistory = false, sawEnd = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    sawEnd = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return state.Fail(PalacePaidError.Syntax, "expected a property name");
                }
                if (!TryDecodeString(ref reader, out string? rawName))
                {
                    return state.Fail(PalacePaidError.Syntax, "property name has an invalid UTF-16 escape (lone surrogate)");
                }
                string name = rawName ?? string.Empty;
                if (!reader.Read())
                {
                    return state.Fail(PalacePaidError.Syntax, "truncated value for " + name);
                }
                switch (name)
                {
                    case "schema":
                        if (seenSchema) return state.Fail(PalacePaidError.DuplicateKey, "schema");
                        seenSchema = true;
                        if (!ReadInt(state, ref reader, "schema", out schema)) return false;
                        break;
                    case "current":
                        if (seenCurrent) return state.Fail(PalacePaidError.DuplicateKey, "current");
                        seenCurrent = true;
                        if (!ParseRecord(state, ref reader, "current", out current)) return false;
                        break;
                    case "history":
                        if (seenHistory) return state.Fail(PalacePaidError.DuplicateKey, "history");
                        seenHistory = true;
                        if (!ParseHistory(state, ref reader, out history)) return false;
                        break;
                    default:
                        return state.Fail(PalacePaidError.UnknownKey, name);
                }
            }
            if (!sawEnd)
            {
                return state.Fail(PalacePaidError.Syntax, "root object is not closed");
            }
            if (!seenSchema) return state.Fail(PalacePaidError.MissingField, "schema");
            if (!seenCurrent) return state.Fail(PalacePaidError.MissingField, "current");
            if (!seenHistory) return state.Fail(PalacePaidError.MissingField, "history");
            if (schema != PalacePaidEnvelope.LegacySchema && schema != PalacePaidEnvelope.CurrentSchema)
                return state.Fail(PalacePaidError.BadVersion, "schema must be 1 or 2");
            if (!ValidateLayoutFields(state, current!, schema, false, "current")) return false;
            foreach (var item in history!)
                if (!ValidateLayoutFields(state, item, schema, true, "history")) return false;
            if (reader.Read())
            {
                return state.Fail(PalacePaidError.Trailing, "data after the root object");
            }
            return true;
        }

        private static bool ValidateLayoutFields(ParseState state, PalacePaidRecord record, int schema, bool history, string path)
        {
            int fields = state.LayoutPresence[record];
            if (schema == PalacePaidEnvelope.LegacySchema)
                return fields == 0 || state.Fail(PalacePaidError.UnknownKey, path + ".destinationLayout");
            if (fields != 3) return state.Fail(PalacePaidError.MissingField, path + ".destinationLayout");
            if (!history && record.DestinationLayout == null)
                return state.Fail(PalacePaidError.BadRecord, path + ".destinationLayout must be known");
            return true;
        }

        private static bool ParseHistory(ParseState state, ref Utf8JsonReader reader, out List<PalacePaidRecord>? history)
        {
            history = null;
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return state.Fail(PalacePaidError.WrongType, "history must be an array");
            }
            var list = new List<PalacePaidRecord>(PalacePaidEnvelope.MaxHistory);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    history = list;
                    return true;
                }
                if (list.Count >= PalacePaidEnvelope.MaxHistory)
                {
                    return state.Fail(PalacePaidError.BadEnvelope, "history may hold at most 1 record");
                }
                if (!ParseRecord(state, ref reader, "history[0]", out PalacePaidRecord? item)) return false;
                list.Add(item!);
            }
            return state.Fail(PalacePaidError.Syntax, "history array is not closed");
        }

        private static bool ParseRecord(ParseState state, ref Utf8JsonReader reader, string path, out PalacePaidRecord? record)
        {
            record = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be an object");
            }

            bool seenTripId = false, seenRevision = false, seenStage = false, seenOwner = false;
            bool seenSourceLand = false, seenPalaceLand = false, seenAnchor = false;
            bool seenOnPay = false, seenSettlement = false, seenCapture = false, seenFile = false, seenReason = false;
            bool seenLayoutId = false, seenLayoutVersion = false;
            string? layoutId = null;
            int? layoutVersion = null;
            bool sawEnd = false;

            Guid tripId = Guid.Empty;
            int revision = 0;
            string? stageText = null;
            PalacePaidScope? owner = null;
            int sourceLand = 0;
            int palaceLand = 0;
            PalacePaidAnchor? anchor = null;
            bool onPay = false;
            bool settlement = false;
            PalacePaidCaptureProof? capture = null;
            PalacePaidFileProof? file = null;
            string? reason = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    sawEnd = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return state.Fail(PalacePaidError.Syntax, path + ": expected a property name");
                }
                if (!TryDecodeString(ref reader, out string? rawName))
                {
                    return state.Fail(PalacePaidError.Syntax, "property name has an invalid UTF-16 escape (lone surrogate)");
                }
                string name = rawName ?? string.Empty;
                if (!reader.Read())
                {
                    return state.Fail(PalacePaidError.Syntax, path + "." + name + ": missing value");
                }
                switch (name)
                {
                    case "tripId":
                        if (seenTripId) return state.Fail(PalacePaidError.DuplicateKey, path + ".tripId");
                        seenTripId = true;
                        if (!ReadGuid(state, ref reader, path + ".tripId", out tripId)) return false;
                        break;
                    case "revision":
                        if (seenRevision) return state.Fail(PalacePaidError.DuplicateKey, path + ".revision");
                        seenRevision = true;
                        if (!ReadInt(state, ref reader, path + ".revision", out revision)) return false;
                        break;
                    case "stage":
                        if (seenStage) return state.Fail(PalacePaidError.DuplicateKey, path + ".stage");
                        seenStage = true;
                        if (!ReadString(state, ref reader, path + ".stage", out stageText)) return false;
                        break;
                    case "owner":
                        if (seenOwner) return state.Fail(PalacePaidError.DuplicateKey, path + ".owner");
                        seenOwner = true;
                        if (!ParseOwner(state, ref reader, path + ".owner", out owner)) return false;
                        break;
                    case "sourceLand":
                        if (seenSourceLand) return state.Fail(PalacePaidError.DuplicateKey, path + ".sourceLand");
                        seenSourceLand = true;
                        if (!ReadInt(state, ref reader, path + ".sourceLand", out sourceLand)) return false;
                        break;
                    case "palaceLand":
                        if (seenPalaceLand) return state.Fail(PalacePaidError.DuplicateKey, path + ".palaceLand");
                        seenPalaceLand = true;
                        if (!ReadInt(state, ref reader, path + ".palaceLand", out palaceLand)) return false;
                        break;
                    case "anchor":
                        if (seenAnchor) return state.Fail(PalacePaidError.DuplicateKey, path + ".anchor");
                        seenAnchor = true;
                        if (reader.TokenType == JsonTokenType.Null)
                        {
                            return state.Fail(PalacePaidError.BadAnchor, path + ".anchor must not be null (missing anchor is not representable in a record)");
                        }
                        if (!ParseAnchor(state, ref reader, path + ".anchor", out anchor)) return false;
                        break;
                    case "onPayObserved":
                        if (seenOnPay) return state.Fail(PalacePaidError.DuplicateKey, path + ".onPayObserved");
                        seenOnPay = true;
                        if (!ReadBool(state, ref reader, path + ".onPayObserved", out onPay)) return false;
                        break;
                    case "settlementObserved":
                        if (seenSettlement) return state.Fail(PalacePaidError.DuplicateKey, path + ".settlementObserved");
                        seenSettlement = true;
                        if (!ReadBool(state, ref reader, path + ".settlementObserved", out settlement)) return false;
                        break;
                    case "captureProof":
                        if (seenCapture) return state.Fail(PalacePaidError.DuplicateKey, path + ".captureProof");
                        seenCapture = true;
                        if (reader.TokenType == JsonTokenType.Null)
                        {
                            capture = null;
                        }
                        else if (!ParseCaptureProof(state, ref reader, path + ".captureProof", out capture))
                        {
                            return false;
                        }
                        break;
                    case "fileProof":
                        if (seenFile) return state.Fail(PalacePaidError.DuplicateKey, path + ".fileProof");
                        seenFile = true;
                        if (reader.TokenType == JsonTokenType.Null)
                        {
                            file = null;
                        }
                        else if (!ParseFileProof(state, ref reader, path + ".fileProof", out file))
                        {
                            return false;
                        }
                        break;
                    case "reason":
                        if (seenReason) return state.Fail(PalacePaidError.DuplicateKey, path + ".reason");
                        seenReason = true;
                        if (reader.TokenType == JsonTokenType.Null)
                        {
                            reason = null;
                        }
                        else if (!ReadString(state, ref reader, path + ".reason", out reason))
                        {
                            return false;
                        }
                        break;
                    case "destinationLayoutId":
                        if (seenLayoutId) return state.Fail(PalacePaidError.DuplicateKey, path + ".destinationLayoutId");
                        seenLayoutId = true;
                        if (reader.TokenType != JsonTokenType.Null && !ReadString(state, ref reader, path + ".destinationLayoutId", out layoutId)) return false;
                        break;
                    case "destinationLayoutVersion":
                        if (seenLayoutVersion) return state.Fail(PalacePaidError.DuplicateKey, path + ".destinationLayoutVersion");
                        seenLayoutVersion = true;
                        if (reader.TokenType != JsonTokenType.Null)
                        {
                            if (!ReadInt(state, ref reader, path + ".destinationLayoutVersion", out int parsedVersion)) return false;
                            layoutVersion = parsedVersion;
                        }
                        break;
                    default:
                        return state.Fail(PalacePaidError.UnknownKey, path + "." + name);
                }
            }
            if (!sawEnd) return state.Fail(PalacePaidError.Syntax, path + " is not closed");
            if (!seenTripId) return state.Fail(PalacePaidError.MissingField, path + ".tripId");
            if (!seenRevision) return state.Fail(PalacePaidError.MissingField, path + ".revision");
            if (!seenStage) return state.Fail(PalacePaidError.MissingField, path + ".stage");
            if (!seenOwner) return state.Fail(PalacePaidError.MissingField, path + ".owner");
            if (!seenSourceLand) return state.Fail(PalacePaidError.MissingField, path + ".sourceLand");
            if (!seenPalaceLand) return state.Fail(PalacePaidError.MissingField, path + ".palaceLand");
            if (!seenAnchor) return state.Fail(PalacePaidError.MissingField, path + ".anchor");
            if (!seenOnPay) return state.Fail(PalacePaidError.MissingField, path + ".onPayObserved");
            if (!seenSettlement) return state.Fail(PalacePaidError.MissingField, path + ".settlementObserved");
            if (!seenCapture) return state.Fail(PalacePaidError.MissingField, path + ".captureProof");
            if (!seenFile) return state.Fail(PalacePaidError.MissingField, path + ".fileProof");
            if (!seenReason) return state.Fail(PalacePaidError.MissingField, path + ".reason");

            PalacePaidStage stage;
            if (!PalacePaidStages.TryParse(stageText, out stage))
            {
                return state.Fail(PalacePaidError.BadStage, path + ".stage: " + stageText);
            }

            try
            {
                PalaceLayoutRecipe? layout = null;
                if ((layoutId == null) != (layoutVersion == null))
                    return state.Fail(PalacePaidError.BadRecord, path + ".destinationLayout null pair");
                if (layoutId != null && !PalaceLayoutRecipe.TryResolve(layoutId, layoutVersion!.Value, out layout!))
                    return state.Fail(PalacePaidError.BadRecord, path + ".destinationLayout unknown");
                record = new PalacePaidRecord(tripId, revision, stage, owner!, sourceLand, palaceLand, anchor!, onPay, settlement, capture, file, reason, layout);
                state.LayoutPresence.Add(record, (seenLayoutId ? 1 : 0) | (seenLayoutVersion ? 2 : 0));
            }
            catch (PalacePaidContractException ex)
            {
                return state.Fail(PalacePaidError.BadRecord, path + ": " + ex.Message);
            }
            return true;
        }

        private static bool ParseOwner(ParseState state, ref Utf8JsonReader reader, string path, out PalacePaidScope? scope)
        {
            scope = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be an object");
            }

            bool seenProfile = false, seenGuid = false, seenSlot = false, seenBiome = false;
            bool seenReign = false, seenStart = false, seenPlayer = false, sawEnd = false;
            string? profile = null;
            string? guidText = null;
            int slot = 0, biome = 0, reign = 0, start = 0, player = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    sawEnd = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return state.Fail(PalacePaidError.Syntax, path + ": expected a property name");
                }
                if (!TryDecodeString(ref reader, out string? rawName))
                {
                    return state.Fail(PalacePaidError.Syntax, "property name has an invalid UTF-16 escape (lone surrogate)");
                }
                string name = rawName ?? string.Empty;
                if (!reader.Read())
                {
                    return state.Fail(PalacePaidError.Syntax, path + "." + name + ": missing value");
                }
                switch (name)
                {
                    case "profile":
                        if (seenProfile) return state.Fail(PalacePaidError.DuplicateKey, path + ".profile");
                        seenProfile = true;
                        if (!ReadString(state, ref reader, path + ".profile", out profile)) return false;
                        break;
                    case "campaignGuid":
                        if (seenGuid) return state.Fail(PalacePaidError.DuplicateKey, path + ".campaignGuid");
                        seenGuid = true;
                        if (!ReadString(state, ref reader, path + ".campaignGuid", out guidText)) return false;
                        break;
                    case "campaignSlot":
                        if (seenSlot) return state.Fail(PalacePaidError.DuplicateKey, path + ".campaignSlot");
                        seenSlot = true;
                        if (!ReadInt(state, ref reader, path + ".campaignSlot", out slot)) return false;
                        break;
                    case "biome":
                        if (seenBiome) return state.Fail(PalacePaidError.DuplicateKey, path + ".biome");
                        seenBiome = true;
                        if (!ReadInt(state, ref reader, path + ".biome", out biome)) return false;
                        break;
                    case "reign":
                        if (seenReign) return state.Fail(PalacePaidError.DuplicateKey, path + ".reign");
                        seenReign = true;
                        if (!ReadInt(state, ref reader, path + ".reign", out reign)) return false;
                        break;
                    case "start":
                        if (seenStart) return state.Fail(PalacePaidError.DuplicateKey, path + ".start");
                        seenStart = true;
                        if (!ReadInt(state, ref reader, path + ".start", out start)) return false;
                        break;
                    case "playerSlot":
                        if (seenPlayer) return state.Fail(PalacePaidError.DuplicateKey, path + ".playerSlot");
                        seenPlayer = true;
                        if (!ReadInt(state, ref reader, path + ".playerSlot", out player)) return false;
                        break;
                    default:
                        return state.Fail(PalacePaidError.UnknownKey, path + "." + name);
                }
            }
            if (!sawEnd) return state.Fail(PalacePaidError.Syntax, path + " is not closed");
            if (!seenProfile) return state.Fail(PalacePaidError.MissingField, path + ".profile");
            if (!seenGuid) return state.Fail(PalacePaidError.MissingField, path + ".campaignGuid");
            if (!seenSlot) return state.Fail(PalacePaidError.MissingField, path + ".campaignSlot");
            if (!seenBiome) return state.Fail(PalacePaidError.MissingField, path + ".biome");
            if (!seenReign) return state.Fail(PalacePaidError.MissingField, path + ".reign");
            if (!seenStart) return state.Fail(PalacePaidError.MissingField, path + ".start");
            if (!seenPlayer) return state.Fail(PalacePaidError.MissingField, path + ".playerSlot");

            if (!PalacePaidText.IsCanonicalGuid(guidText))
            {
                return state.Fail(PalacePaidError.BadValue, path + ".campaignGuid must be a 36-char lowercase hyphenated Guid");
            }
            Guid guid = Guid.ParseExact(guidText!, "D");
            if (guid == Guid.Empty)
            {
                return state.Fail(PalacePaidError.BadValue, path + ".campaignGuid must be non-empty");
            }

            try
            {
                scope = new PalacePaidScope(profile!, guid, slot, biome, reign, start, player);
            }
            catch (PalacePaidContractException ex)
            {
                return state.Fail(PalacePaidError.BadScope, path + ": " + ex.Message);
            }
            return true;
        }

        private static bool ParseAnchor(ParseState state, ref Utf8JsonReader reader, string path, out PalacePaidAnchor? anchor)
        {
            anchor = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be an object");
            }

            bool seenX = false, seenY = false, seenZ = false, seenPlayerX = false, seenStandX = false, seenSide = false, sawEnd = false;
            float entryX = 0f, entryY = 0f, entryZ = 0f, sourcePlayerX = 0f, standX = 0f;
            int side = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    sawEnd = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return state.Fail(PalacePaidError.Syntax, path + ": expected a property name");
                }
                if (!TryDecodeString(ref reader, out string? rawName))
                {
                    return state.Fail(PalacePaidError.Syntax, "property name has an invalid UTF-16 escape (lone surrogate)");
                }
                string name = rawName ?? string.Empty;
                if (!reader.Read())
                {
                    return state.Fail(PalacePaidError.Syntax, path + "." + name + ": missing value");
                }
                switch (name)
                {
                    case "entryX":
                        if (seenX) return state.Fail(PalacePaidError.DuplicateKey, path + ".entryX");
                        seenX = true;
                        if (!ReadFloat(state, ref reader, path + ".entryX", out entryX)) return false;
                        break;
                    case "entryY":
                        if (seenY) return state.Fail(PalacePaidError.DuplicateKey, path + ".entryY");
                        seenY = true;
                        if (!ReadFloat(state, ref reader, path + ".entryY", out entryY)) return false;
                        break;
                    case "entryZ":
                        if (seenZ) return state.Fail(PalacePaidError.DuplicateKey, path + ".entryZ");
                        seenZ = true;
                        if (!ReadFloat(state, ref reader, path + ".entryZ", out entryZ)) return false;
                        break;
                    case "sourcePlayerX":
                        if (seenPlayerX) return state.Fail(PalacePaidError.DuplicateKey, path + ".sourcePlayerX");
                        seenPlayerX = true;
                        if (!ReadFloat(state, ref reader, path + ".sourcePlayerX", out sourcePlayerX)) return false;
                        break;
                    case "standX":
                        if (seenStandX) return state.Fail(PalacePaidError.DuplicateKey, path + ".standX");
                        seenStandX = true;
                        if (!ReadFloat(state, ref reader, path + ".standX", out standX)) return false;
                        break;
                    case "side":
                        if (seenSide) return state.Fail(PalacePaidError.DuplicateKey, path + ".side");
                        seenSide = true;
                        if (!ReadInt(state, ref reader, path + ".side", out side)) return false;
                        break;
                    default:
                        return state.Fail(PalacePaidError.UnknownKey, path + "." + name);
                }
            }
            if (!sawEnd) return state.Fail(PalacePaidError.Syntax, path + " is not closed");
            if (!seenX) return state.Fail(PalacePaidError.MissingField, path + ".entryX");
            if (!seenY) return state.Fail(PalacePaidError.MissingField, path + ".entryY");
            if (!seenZ) return state.Fail(PalacePaidError.MissingField, path + ".entryZ");
            if (!seenPlayerX) return state.Fail(PalacePaidError.MissingField, path + ".sourcePlayerX");
            if (!seenStandX) return state.Fail(PalacePaidError.MissingField, path + ".standX");
            if (!seenSide) return state.Fail(PalacePaidError.MissingField, path + ".side");

            try
            {
                anchor = new PalacePaidAnchor(entryX, entryY, entryZ, sourcePlayerX, standX, side);
            }
            catch (PalacePaidContractException ex)
            {
                return state.Fail(PalacePaidError.BadAnchor, path + ": " + ex.Message);
            }
            return true;
        }

        private static bool ParseCaptureProof(ParseState state, ref Utf8JsonReader reader, string path, out PalacePaidCaptureProof? proof)
        {
            proof = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be an object");
            }

            bool seenId = false, seenOwner = false, seenSourceLand = false, seenPayer = false;
            bool seenCurrencies = false, seenFingerprint = false;
            bool seenMarker = false, seenRan = false, seenNoException = false, sawEnd = false;
            long captureId = 0;
            PalacePaidScope? owner = null;
            int sourceLand = 0;
            string? payer = null;
            int[]? currencies = null;
            string? fingerprint = null;
            bool marker = false, ran = false, noException = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    sawEnd = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return state.Fail(PalacePaidError.Syntax, path + ": expected a property name");
                }
                if (!TryDecodeString(ref reader, out string? rawName))
                {
                    return state.Fail(PalacePaidError.Syntax, "property name has an invalid UTF-16 escape (lone surrogate)");
                }
                string name = rawName ?? string.Empty;
                if (!reader.Read())
                {
                    return state.Fail(PalacePaidError.Syntax, path + "." + name + ": missing value");
                }
                switch (name)
                {
                    case "captureId":
                        if (seenId) return state.Fail(PalacePaidError.DuplicateKey, path + ".captureId");
                        seenId = true;
                        if (!ReadLong(state, ref reader, path + ".captureId", out captureId)) return false;
                        break;
                    case "owner":
                        if (seenOwner) return state.Fail(PalacePaidError.DuplicateKey, path + ".owner");
                        seenOwner = true;
                        if (!ParseOwner(state, ref reader, path + ".owner", out owner)) return false;
                        break;
                    case "sourceLand":
                        if (seenSourceLand) return state.Fail(PalacePaidError.DuplicateKey, path + ".sourceLand");
                        seenSourceLand = true;
                        if (!ReadInt(state, ref reader, path + ".sourceLand", out sourceLand)) return false;
                        break;
                    case "payer":
                        if (seenPayer) return state.Fail(PalacePaidError.DuplicateKey, path + ".payer");
                        seenPayer = true;
                        if (!ReadString(state, ref reader, path + ".payer", out payer)) return false;
                        break;
                    case "currencies":
                        if (seenCurrencies) return state.Fail(PalacePaidError.DuplicateKey, path + ".currencies");
                        seenCurrencies = true;
                        if (!ReadCurrencies(state, ref reader, path + ".currencies", out currencies)) return false;
                        break;
                    case "sourceIslandFingerprint":
                        if (seenFingerprint) return state.Fail(PalacePaidError.DuplicateKey, path + ".sourceIslandFingerprint");
                        seenFingerprint = true;
                        if (!ReadString(state, ref reader, path + ".sourceIslandFingerprint", out fingerprint)) return false;
                        break;
                    case "nativeMarkerSeen":
                        if (seenMarker) return state.Fail(PalacePaidError.DuplicateKey, path + ".nativeMarkerSeen");
                        seenMarker = true;
                        if (!ReadBool(state, ref reader, path + ".nativeMarkerSeen", out marker)) return false;
                        break;
                    case "nativeRan":
                        if (seenRan) return state.Fail(PalacePaidError.DuplicateKey, path + ".nativeRan");
                        seenRan = true;
                        if (!ReadBool(state, ref reader, path + ".nativeRan", out ran)) return false;
                        break;
                    case "noException":
                        if (seenNoException) return state.Fail(PalacePaidError.DuplicateKey, path + ".noException");
                        seenNoException = true;
                        if (!ReadBool(state, ref reader, path + ".noException", out noException)) return false;
                        break;
                    default:
                        return state.Fail(PalacePaidError.UnknownKey, path + "." + name);
                }
            }
            if (!sawEnd) return state.Fail(PalacePaidError.Syntax, path + " is not closed");
            if (!seenId) return state.Fail(PalacePaidError.MissingField, path + ".captureId");
            if (!seenOwner) return state.Fail(PalacePaidError.MissingField, path + ".owner");
            if (!seenSourceLand) return state.Fail(PalacePaidError.MissingField, path + ".sourceLand");
            if (!seenPayer) return state.Fail(PalacePaidError.MissingField, path + ".payer");
            if (!seenCurrencies) return state.Fail(PalacePaidError.MissingField, path + ".currencies");
            if (!seenFingerprint) return state.Fail(PalacePaidError.MissingField, path + ".sourceIslandFingerprint");
            if (!seenMarker) return state.Fail(PalacePaidError.MissingField, path + ".nativeMarkerSeen");
            if (!seenRan) return state.Fail(PalacePaidError.MissingField, path + ".nativeRan");
            if (!seenNoException) return state.Fail(PalacePaidError.MissingField, path + ".noException");

            try
            {
                proof = new PalacePaidCaptureProof(captureId, owner!, sourceLand, payer!, currencies!, fingerprint!, marker, ran, noException);
            }
            catch (PalacePaidContractException ex)
            {
                return state.Fail(PalacePaidError.BadProof, path + ": " + ex.Message);
            }
            return true;
        }

        private static bool ParseFileProof(ParseState state, ref Utf8JsonReader reader, string path, out PalacePaidFileProof? proof)
        {
            proof = null;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be an object");
            }

            bool seenRawSha = false, seenOwner = false, seenSourceLand = false, seenPayer = false;
            bool seenCurrencies = false, seenFingerprint = false, seenVersion = false, seenRawResult = false, sawEnd = false;
            string? rawSha = null;
            PalacePaidScope? owner = null;
            int sourceLand = 0;
            string? payer = null;
            int[]? currencies = null;
            string? fingerprint = null;
            int version = 0;
            int rawResult = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    sawEnd = true;
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return state.Fail(PalacePaidError.Syntax, path + ": expected a property name");
                }
                if (!TryDecodeString(ref reader, out string? rawName))
                {
                    return state.Fail(PalacePaidError.Syntax, "property name has an invalid UTF-16 escape (lone surrogate)");
                }
                string name = rawName ?? string.Empty;
                if (!reader.Read())
                {
                    return state.Fail(PalacePaidError.Syntax, path + "." + name + ": missing value");
                }
                switch (name)
                {
                    case "rawSaveSha256":
                        if (seenRawSha) return state.Fail(PalacePaidError.DuplicateKey, path + ".rawSaveSha256");
                        seenRawSha = true;
                        if (!ReadString(state, ref reader, path + ".rawSaveSha256", out rawSha)) return false;
                        break;
                    case "owner":
                        if (seenOwner) return state.Fail(PalacePaidError.DuplicateKey, path + ".owner");
                        seenOwner = true;
                        if (!ParseOwner(state, ref reader, path + ".owner", out owner)) return false;
                        break;
                    case "sourceLand":
                        if (seenSourceLand) return state.Fail(PalacePaidError.DuplicateKey, path + ".sourceLand");
                        seenSourceLand = true;
                        if (!ReadInt(state, ref reader, path + ".sourceLand", out sourceLand)) return false;
                        break;
                    case "payer":
                        if (seenPayer) return state.Fail(PalacePaidError.DuplicateKey, path + ".payer");
                        seenPayer = true;
                        if (!ReadString(state, ref reader, path + ".payer", out payer)) return false;
                        break;
                    case "currencies":
                        if (seenCurrencies) return state.Fail(PalacePaidError.DuplicateKey, path + ".currencies");
                        seenCurrencies = true;
                        if (!ReadCurrencies(state, ref reader, path + ".currencies", out currencies)) return false;
                        break;
                    case "sourceIslandFingerprint":
                        if (seenFingerprint) return state.Fail(PalacePaidError.DuplicateKey, path + ".sourceIslandFingerprint");
                        seenFingerprint = true;
                        if (!ReadString(state, ref reader, path + ".sourceIslandFingerprint", out fingerprint)) return false;
                        break;
                    case "nativeVersion":
                        if (seenVersion) return state.Fail(PalacePaidError.DuplicateKey, path + ".nativeVersion");
                        seenVersion = true;
                        if (!ReadInt(state, ref reader, path + ".nativeVersion", out version)) return false;
                        break;
                    case "nativeRawResult":
                        if (seenRawResult) return state.Fail(PalacePaidError.DuplicateKey, path + ".nativeRawResult");
                        seenRawResult = true;
                        if (!ReadInt(state, ref reader, path + ".nativeRawResult", out rawResult)) return false;
                        break;
                    default:
                        return state.Fail(PalacePaidError.UnknownKey, path + "." + name);
                }
            }
            if (!sawEnd) return state.Fail(PalacePaidError.Syntax, path + " is not closed");
            if (!seenRawSha) return state.Fail(PalacePaidError.MissingField, path + ".rawSaveSha256");
            if (!seenOwner) return state.Fail(PalacePaidError.MissingField, path + ".owner");
            if (!seenSourceLand) return state.Fail(PalacePaidError.MissingField, path + ".sourceLand");
            if (!seenPayer) return state.Fail(PalacePaidError.MissingField, path + ".payer");
            if (!seenCurrencies) return state.Fail(PalacePaidError.MissingField, path + ".currencies");
            if (!seenFingerprint) return state.Fail(PalacePaidError.MissingField, path + ".sourceIslandFingerprint");
            if (!seenVersion) return state.Fail(PalacePaidError.MissingField, path + ".nativeVersion");
            if (!seenRawResult) return state.Fail(PalacePaidError.MissingField, path + ".nativeRawResult");

            try
            {
                proof = new PalacePaidFileProof(rawSha!, owner!, sourceLand, payer!, currencies!, fingerprint!, version, rawResult);
            }
            catch (PalacePaidContractException ex)
            {
                return state.Fail(PalacePaidError.BadProof, path + ": " + ex.Message);
            }
            return true;
        }

        // ── 标量读取 ──────────────────────────────────────────────

        // 所有 JSON 字符串（属性名与字符串值）的统一解码点。Utf8JsonReader.GetString 对 \uXXXX 形式的
        // 孤立 surrogate 抛 InvalidOperationException（输入本身仍是合法 UTF-8 与合法 JSON 语法），
        // 这里只在 GetString 调用点窄捕获这一种解码失败并转成严格失败结果，异常绝不穿透；
        // 合法 surrogate pair 正常解码后仍由字段接受域决定接受或拒绝；不catch OOM/StackOverflow 等环境异常。
        private static bool TryDecodeString(ref Utf8JsonReader reader, out string? text)
        {
            try
            {
                text = reader.GetString();
                return true;
            }
            catch (InvalidOperationException)
            {
                // 调用点已确认 token 是字符串/属性名，整段输入也通过了严格 UTF-8 预检，
                // 因此这里唯一可达的 InvalidOperationException 就是孤立 surrogate 的 UTF-16 解码失败。
                text = null;
                return false;
            }
        }

        private static bool ReadString(ParseState state, ref Utf8JsonReader reader, string path, out string? value)
        {
            value = null;
            if (reader.TokenType != JsonTokenType.String)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be a string");
            }
            if (!TryDecodeString(ref reader, out value))
            {
                return state.Fail(PalacePaidError.Syntax, path + " has an invalid UTF-16 escape (lone surrogate)");
            }
            if (value == null)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be a string");
            }
            return true;
        }

        private static bool ReadBool(ParseState state, ref Utf8JsonReader reader, string path, out bool value)
        {
            value = false;
            if (reader.TokenType == JsonTokenType.True)
            {
                value = true;
                return true;
            }
            if (reader.TokenType == JsonTokenType.False)
            {
                value = false;
                return true;
            }
            return state.Fail(PalacePaidError.WrongType, path + " must be a boolean");
        }

        private static bool ReadInt(ParseState state, ref Utf8JsonReader reader, string path, out int value)
        {
            value = 0;
            if (reader.TokenType != JsonTokenType.Number)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be a JSON number");
            }
            int consumed;
            if (!Utf8Parser.TryParse(reader.ValueSpan, out value, out consumed, 'd') || consumed != reader.ValueSpan.Length)
            {
                return state.Fail(PalacePaidError.BadValue, path + " must be a 32-bit integer");
            }
            return true;
        }

        private static bool ReadLong(ParseState state, ref Utf8JsonReader reader, string path, out long value)
        {
            value = 0L;
            if (reader.TokenType != JsonTokenType.Number)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be a JSON number");
            }
            int consumed;
            if (!Utf8Parser.TryParse(reader.ValueSpan, out value, out consumed, 'd') || consumed != reader.ValueSpan.Length)
            {
                return state.Fail(PalacePaidError.BadValue, path + " must be a 64-bit integer");
            }
            return true;
        }

        private static bool ReadFloat(ParseState state, ref Utf8JsonReader reader, string path, out float value)
        {
            value = 0f;
            if (reader.TokenType != JsonTokenType.Number)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be a JSON number");
            }
            string text = Encoding.UTF8.GetString(reader.ValueSpan);
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !float.IsFinite(value))
            {
                return state.Fail(PalacePaidError.BadValue, path + " must be a finite number");
            }
            return true;
        }

        private static bool ReadGuid(ParseState state, ref Utf8JsonReader reader, string path, out Guid value)
        {
            value = Guid.Empty;
            string? text;
            if (!ReadString(state, ref reader, path, out text)) return false;
            if (!PalacePaidText.IsCanonicalGuid(text))
            {
                return state.Fail(PalacePaidError.BadValue, path + " must be a 36-char lowercase hyphenated Guid");
            }
            value = Guid.ParseExact(text!, "D");
            if (value == Guid.Empty)
            {
                return state.Fail(PalacePaidError.BadValue, path + " must be non-empty");
            }
            return true;
        }

        private static bool ReadCurrencies(ParseState state, ref Utf8JsonReader reader, string path, out int[]? values)
        {
            values = null;
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                return state.Fail(PalacePaidError.WrongType, path + " must be an array");
            }
            var list = new List<int>(PalacePaidCurrencies.Count);
            bool sawEnd = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    sawEnd = true;
                    break;
                }
                if (list.Count >= PalacePaidCurrencies.Count)
                {
                    return state.Fail(PalacePaidError.BadCurrencies, path + " must have exactly 8 entries");
                }
                int item;
                if (!ReadInt(state, ref reader, path, out item)) return false;
                if (item < 0)
                {
                    return state.Fail(PalacePaidError.BadCurrencies, path + " entries must be >= 0");
                }
                list.Add(item);
            }
            if (!sawEnd)
            {
                return state.Fail(PalacePaidError.Syntax, path + " is not closed");
            }
            if (list.Count != PalacePaidCurrencies.Count)
            {
                return state.Fail(PalacePaidError.BadCurrencies, path + " must have exactly 8 entries");
            }
            values = list.ToArray();
            return true;
        }
    }

    // ───────────────────────── 校验与纯状态机 ─────────────────────────

    internal static class PalacePaidJournal
    {
        // 纯校验：invariant 由构造保证；expected owner（全七元组）逐条核 current + history，
        // expected payer 只描述当前 trip，仅核 current record 的 proof。
        public static PalacePaidResult<PalacePaidEnvelope> Validate(PalacePaidEnvelope? envelope, PalacePaidExpectation? expectation)
        {
            if (envelope == null)
            {
                return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.BadEnvelope, "envelope is required");
            }
            if (expectation == null)
            {
                return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.OwnerMismatch, "expectation is required");
            }

            PalacePaidResult<PalacePaidEnvelope> ownerCheck = ValidateOwner(envelope, expectation.Owner);
            if (!ownerCheck.Ok)
            {
                return ownerCheck;
            }

            // expected payer 只核 current：history 的 payer 属于上一笔已完整的 terminal 证据，不拿新 trip 的
            // 期望去核旧记录；current 尚无 proof 时本项不产生拒绝。
            if (expectation.Payer != null)
            {
                PalacePaidRecord current = envelope.Current;
                string? payer = current.CaptureProof != null ? current.CaptureProof!.Payer : null;
                if (payer == null && current.FileProof != null) payer = current.FileProof!.Payer;
                if (payer != null && !string.Equals(payer, expectation.Payer, StringComparison.Ordinal))
                {
                    return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.PayerMismatch, "payer does not match the expected payer id");
                }
            }

            return PalacePaidResult<PalacePaidEnvelope>.Success(envelope);
        }

        // owner 核验单独成点：Begin 对旧 envelope 只核 owner（新 trip 的 expected payer 不适用于旧 terminal），
        // 不伪造、不放宽公共 payer 域校验。
        private static PalacePaidResult<PalacePaidEnvelope> ValidateOwner(PalacePaidEnvelope envelope, PalacePaidScope owner)
        {
            // current 与 history 都必须与 expected owner 完全一致。
            if (!envelope.Current.Owner.ContentEquals(owner))
            {
                return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.OwnerMismatch, "owner does not match the expected scope (all seven fields are compared)");
            }
            for (int i = 0; i < envelope.History.Count; i++)
            {
                if (!envelope.History[i].Owner.ContentEquals(owner))
                {
                    return PalacePaidResult<PalacePaidEnvelope>.Fail(PalacePaidError.OwnerMismatch, "owner does not match the expected scope (all seven fields are compared)");
                }
            }
            return PalacePaidResult<PalacePaidEnvelope>.Success(envelope);
        }

        // 唯一入口。existing 为 R4 从存储读出的 envelope（未读过则为 null）；
        // expectedRawSha256 为 null 表示 expectedAbsent（必须没有 envelope），否则必须是上一次读到的原始 bytes 的
        // SHA-256 小写 hex（整 envelope 原始 bytes 绑定，revision 单独不足以做 CAS）。
        // 返回 Changed / NoChange / Rejected 三态；本函数不做任何 IO，也不声称原子性。
        public static PalacePaidApplyResult Apply(PalacePaidEnvelope? existing, string? expectedRawSha256, PalacePaidRequest? request)
        {
            if (request == null)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.Payload, "request is null");
            }

            if (expectedRawSha256 == null)
            {
                if (existing != null)
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.ExpectationMismatch, "expected absent, but an envelope is present");
                }
            }
            else
            {
                if (!PalacePaidText.IsLowerHex(expectedRawSha256, 64))
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.ExpectationMismatch, "expectedRawSha256 must be 64 lowercase hex");
                }
                if (existing == null)
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.ExpectationMismatch, "expected a stored envelope, but none is present");
                }
                if (existing.SourceBytesSha256 == null)
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.ExpectationUnbound, "envelope has no raw-bytes binding; only envelopes parsed from stored bytes can be CAS-verified");
                }
                if (!string.Equals(existing.SourceBytesSha256, expectedRawSha256, StringComparison.Ordinal))
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.ExpectationMismatch, "raw-bytes SHA mismatch (stale read)");
                }
            }

            PalacePaidExpectation expectation = request.Expectation;
            if (existing != null)
            {
                if (request.Action == PalacePaidAction.Begin)
                {
                    // Begin 的 expectation.Payer 描述即将新建的 trip，不能用于旧 envelope：旧 envelope 仍完整核
                    // owner（current + history 七元组），但不比较 payer；随后 ApplyBegin 继续核 terminal/未决、
                    // 新 Guid、source 0..11 与 history tripId 复用禁令。
                    PalacePaidResult<PalacePaidEnvelope> ownerValidation = ValidateOwner(existing, expectation.Owner);
                    if (!ownerValidation.Ok)
                    {
                        return PalacePaidApplyResult.Rejected(ownerValidation.Error, ownerValidation.Detail);
                    }
                }
                else
                {
                    PalacePaidResult<PalacePaidEnvelope> validation = Validate(existing, expectation);
                    if (!validation.Ok)
                    {
                        return PalacePaidApplyResult.Rejected(validation.Error, validation.Detail);
                    }
                }
            }

            if (request.Action == PalacePaidAction.Begin)
            {
                return ApplyBegin(existing, request);
            }

            if (existing == null)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.NoRecord, "no current record for " + request.Action.ToString());
            }

            PalacePaidRecord current = existing.Current;
            if (current.TripId != request.TripId)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.TripIdMismatch, "request.tripId does not match current.tripId");
            }
            // 溢出检查不在 action 分派之前：完整的合法 NoChange（同事实 repeat）必须先返回，
            // 只有真正要构造 revision+1 的推进路径（Transition）才拒 RevisionOverflow。
            switch (request.Action)
            {
                case PalacePaidAction.Cancel:
                    if (current.Stage == PalacePaidStage.Cancelled) return PalacePaidApplyResult.NoChange(existing);
                    if (current.Stage != PalacePaidStage.ChargeIntent) return StageRejected(current);
                    // Cancelled 只从 ChargeIntent 进入，付款事实仍为 false；不凭钱包差值签名。
                    return Transition(existing, PalacePaidStage.Cancelled, false, false, null, null);

                case PalacePaidAction.ObservePaid:
                    if (current.Stage == PalacePaidStage.ChargeIntent)
                    {
                        return Transition(existing, PalacePaidStage.PaidObserved, true, false, null, null);
                    }
                    if (current.Stage == PalacePaidStage.PaidObserved)
                    {
                        if (current.SettlementObserved)
                        {
                            return PalacePaidApplyResult.Rejected(PalacePaidError.Rollback, "settlementObserved cannot return from true to false");
                        }
                        return PalacePaidApplyResult.NoChange(existing);
                    }
                    return StageRejected(current);

                case PalacePaidAction.ObserveSettlement:
                    if (current.Stage != PalacePaidStage.PaidObserved) return StageRejected(current);
                    if (current.SettlementObserved) return PalacePaidApplyResult.NoChange(existing);
                    return Transition(existing, PalacePaidStage.PaidObserved, true, true, null, null);

                case PalacePaidAction.Capture:
                {
                    PalacePaidCaptureProof proof = request.CaptureProof!;
                    if (current.Stage == PalacePaidStage.SourceCaptured)
                    {
                        if (current.CaptureProof!.ContentEquals(proof)) return PalacePaidApplyResult.NoChange(existing);
                        return PalacePaidApplyResult.Rejected(PalacePaidError.ProofConflict, "captureProof cannot be replaced within a trip");
                    }
                    if (current.Stage != PalacePaidStage.PaidObserved) return StageRejected(current);
                    if (!current.SettlementObserved)
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.NotAllowed, "capture requires settlementObserved = true");
                    }
                    if (!proof.Owner.ContentEquals(current.Owner))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.OwnerMismatch, "captureProof.owner must equal the record owner");
                    }
                    if (proof.SourceLand != current.SourceLand)
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.ProofMismatch, "captureProof.sourceLand must equal record.sourceLand");
                    }
                    if (expectation.Payer != null && !string.Equals(proof.Payer, expectation.Payer, StringComparison.Ordinal))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.PayerMismatch, "captureProof.payer does not match the expected payer id");
                    }
                    return Transition(existing, PalacePaidStage.SourceCaptured, true, true, proof, null);
                }

                case PalacePaidAction.Resolve:
                {
                    PalacePaidFileProof fileProof = request.FileProof!;
                    if (current.Stage == PalacePaidStage.Resumable)
                    {
                        if (current.FileProof!.ContentEquals(fileProof)) return PalacePaidApplyResult.NoChange(existing);
                        return PalacePaidApplyResult.Rejected(PalacePaidError.ProofConflict, "fileProof cannot be replaced within a trip");
                    }
                    if (current.Stage != PalacePaidStage.SourceCaptured) return StageRejected(current);
                    if (!fileProof.Owner.ContentEquals(current.Owner))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.OwnerMismatch, "fileProof.owner must equal the record owner");
                    }
                    if (fileProof.SourceLand != current.SourceLand)
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.ProofMismatch, "fileProof.sourceLand must equal record.sourceLand");
                    }
                    if (!fileProof.Matches(current.CaptureProof))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.ProofMismatch, "fileProof must match the stored captureProof field by field");
                    }
                    if (expectation.Payer != null && !string.Equals(fileProof.Payer, expectation.Payer, StringComparison.Ordinal))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.PayerMismatch, "fileProof.payer does not match the expected payer id");
                    }
                    return Transition(existing, PalacePaidStage.Resumable, true, true, current.CaptureProof, fileProof);
                }

                case PalacePaidAction.ClaimDeparture:
                    if (current.DestinationLayout == null)
                        return PalacePaidApplyResult.Rejected(PalacePaidError.Payload, "legacy destination layout unknown; departure refused");
                    if (current.Stage == PalacePaidStage.DepartureClaimed) return PalacePaidApplyResult.NoChange(existing);
                    if (current.Stage != PalacePaidStage.Resumable) return StageRejected(current);
                    return Transition(existing, PalacePaidStage.DepartureClaimed, true, true, current.CaptureProof, current.FileProof);

                case PalacePaidAction.MarkEntered:
                    if (current.Stage == PalacePaidStage.Entered) return PalacePaidApplyResult.NoChange(existing);
                    if (current.Stage != PalacePaidStage.DepartureClaimed) return StageRejected(current);
                    return Transition(existing, PalacePaidStage.Entered, true, true, current.CaptureProof, current.FileProof);

                case PalacePaidAction.MarkReturned:
                    if (current.Stage == PalacePaidStage.Returned) return PalacePaidApplyResult.NoChange(existing);
                    if (current.Stage != PalacePaidStage.Entered) return StageRejected(current);
                    return Transition(existing, PalacePaidStage.Returned, true, true, current.CaptureProof, current.FileProof);

                case PalacePaidAction.CommitVisit:
                    if (current.Stage == PalacePaidStage.VisitCommitted) return PalacePaidApplyResult.NoChange(existing);
                    if (current.Stage != PalacePaidStage.Returned) return StageRejected(current);
                    return Transition(existing, PalacePaidStage.VisitCommitted, true, true, current.CaptureProof, current.FileProof);

                case PalacePaidAction.MarkUnknown:
                {
                    string reason = request.Reason!;
                    if (!PalacePaidText.IsReason(reason))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.Payload, "reason must be 1..200 printable ASCII");
                    }
                    if (current.Stage == PalacePaidStage.Unknown)
                    {
                        if (string.Equals(current.Reason, reason, StringComparison.Ordinal)) return PalacePaidApplyResult.NoChange(existing);
                        // Unknown 保持 sticky：只允许刷新 reason，不改变任何既有数据，也不自动清账。
                        return Transition(existing, PalacePaidStage.Unknown, current.OnPayObserved, current.SettlementObserved, current.CaptureProof, current.FileProof, reason);
                    }
                    if (PalacePaidStages.IsTerminal(current.Stage))
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.Terminal, "terminal records cannot be rewritten to Unknown");
                    }
                    return Transition(existing, PalacePaidStage.Unknown, current.OnPayObserved, current.SettlementObserved, current.CaptureProof, current.FileProof, reason);
                }
            }

            return PalacePaidApplyResult.Rejected(PalacePaidError.Payload, "unknown action");
        }

        private static PalacePaidApplyResult ApplyBegin(PalacePaidEnvelope? existing, PalacePaidRequest request)
        {
            if (existing != null)
            {
                if (!PalacePaidStages.IsTerminal(existing.Current.Stage))
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.Undecided, "current trip is undecided; a new trip is refused regardless of the intended source island");
                }
                if (request.TripId == existing.Current.TripId)
                {
                    return PalacePaidApplyResult.Rejected(PalacePaidError.TripIdReuse, "new trip must use a new tripId (current.tripId is retained)");
                }
                for (int i = 0; i < existing.History.Count; i++)
                {
                    if (request.TripId == existing.History[i].TripId)
                    {
                        return PalacePaidApplyResult.Rejected(PalacePaidError.TripIdReuse, "new trip must not reuse a tripId retained in history");
                    }
                }
            }
            if (request.TripId == Guid.Empty)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.Payload, "tripId must be a non-empty Guid");
            }
            if (request.SourceLand < 0 || request.SourceLand > 11)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.Payload, "sourceLand must be 0..11 for Begin");
            }

            PalacePaidScope owner = request.Expectation.Owner;
            PalacePaidRecord record;
            try
            {
                record = new PalacePaidRecord(request.TripId, 1, PalacePaidStage.ChargeIntent, owner, request.SourceLand, PalacePaidRecord.RequiredPalaceLand, request.Anchor!, false, false, null, null, null, request.DestinationLayout);
            }
            catch (PalacePaidContractException ex)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.BadRecord, ex.Message);
            }

            // 旧 current 必须是 terminal（上面已核）；更旧的 terminal history 在此被丢弃，硬界恒为 1。
            PalacePaidRecord[] historyRecords;
            if (existing == null)
            {
                historyRecords = Array.Empty<PalacePaidRecord>();
            }
            else
            {
                historyRecords = new PalacePaidRecord[1];
                historyRecords[0] = existing.Current;
            }

            PalacePaidEnvelope envelope;
            try
            {
                envelope = new PalacePaidEnvelope(record, historyRecords, PalacePaidEnvelope.CurrentSchema);
            }
            catch (PalacePaidContractException ex)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.BadEnvelope, ex.Message);
            }

            return PalacePaidApplyResult.Changed(envelope, PalacePaidCodec.Serialize(envelope));
        }

        private static PalacePaidApplyResult StageRejected(PalacePaidRecord current)
        {
            if (current.Stage == PalacePaidStage.Unknown)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.StickyUnknown, "Unknown is sticky: only MarkUnknown may be applied, and it never advances the stage");
            }
            return PalacePaidApplyResult.Rejected(PalacePaidError.NotAllowed, "action is not allowed from stage " + PalacePaidStages.Text(current.Stage));
        }

        private static PalacePaidApplyResult Transition(
            PalacePaidEnvelope existing,
            PalacePaidStage stage,
            bool onPayObserved,
            bool settlementObserved,
            PalacePaidCaptureProof? captureProof,
            PalacePaidFileProof? fileProof,
            string? reason = null)
        {
            PalacePaidRecord current = existing.Current;
            if (current.Revision == int.MaxValue)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.RevisionOverflow, "revision would overflow");
            }
            PalacePaidRecord next;
            try
            {
                next = new PalacePaidRecord(
                    current.TripId,
                    current.Revision + 1,
                    stage,
                    current.Owner,
                    current.SourceLand,
                    current.PalaceLand,
                    current.Anchor,
                    onPayObserved,
                    settlementObserved,
                    captureProof,
                    fileProof,
                    reason,
                    current.DestinationLayout);
            }
            catch (PalacePaidContractException ex)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.BadRecord, ex.Message);
            }

            PalacePaidEnvelope envelope;
            try
            {
                envelope = new PalacePaidEnvelope(next, existing.History, existing.Schema);
            }
            catch (PalacePaidContractException ex)
            {
                return PalacePaidApplyResult.Rejected(PalacePaidError.BadEnvelope, ex.Message);
            }

            return PalacePaidApplyResult.Changed(envelope, PalacePaidCodec.Serialize(envelope));
        }
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// R4 store 追加块（同一文件的第二个 namespace 块；不新增 using、不改前 135790 字节）
//
// 职责边界：
//   * 只做「真实文件系统接线」：目录内的单文件 journal、同目录唯一 temp、原子 replace、
//     跨进程同目录 FileShare.None 锁、scope 专属 .intent 声明标记、进程内 Held 闩锁；
//   * 不做任何业务状态判定：Changed/NoChange/Rejected 的全部状态规则仍由已采纳纯层
//     PalacePaidJournal.Apply 决定，本块只搬运「读到的原始 bytes → 纯层 → 写回 → 读回校验」；
//   * 无 native/Game/save/config 依赖；只使用 BCL net6/net8 API。
//
// 路径后缀（全部位于构造参数给出的固定绝对目录内，文件名仅来自 scope.StoreFileName()）：
//   journal : palace-paid-<64hex>.json
//   lock    : palace-paid-<64hex>.json.lock        （可为空、可复用；单独存在不表示不确定）
//   intent  : palace-paid-<64hex>.json.intent      （有界诊断 header；存在即不确定）
//   temp    : palace-paid-<64hex>.json.tmp-<32hex> （同目录唯一 CreateNew；写后 Flush(true)）
//
// 稳定错误码（Error 字段）：
//   Held（本次尝试；其中 held_lock_busy 不闩锁）：
//     held_lock_busy / held_directory_missing / held_symlink / held_intent_present /
//     held_temp_present / held_unknown_residual / held_corrupt_journal / held_oversize /
//     held_io_error / held_short_read / held_lock_io_error / held_lock_unwritable /
//     held_intent_create_failed / held_temp_create_failed / held_rename_failed /
//     held_readback_failed / held_readback_mismatch / held_intent_delete_failed
//   Rejected（已知零 journal 写入）："rejected_" + PalacePaidError 枚举名
//
// Held 生命周期：
//   * corruption / residual / IO 歧义 → 写入进程内静态闩锁（键 = 规范化目录 + scope 文件名）；
//     新 store 实例读同一闩锁，无法清除；无 recovery/reset API；
//   * 锁竞争（进程内 guard 或跨进程 lock）只对「本次尝试」返回 held_lock_busy，不闩锁；
//   * 闩锁后 Read 与 TryApply 一律返回 Held，且不提供 Envelope/RawSha256。
//
// 测试钩子（编译期剔除，生产构建不含回调字段）：
//   #if PALACE_PAID_JOURNAL_TESTS 下的 TestBoundaryCallback 只在下列命名边界被同步调用：
//     lock-acquired / intent-created / temp-written / replaced / readback-ok
// ═════════════════════════════════════════════════════════════════════════════

namespace KingdomEnhancedMod
{
    internal enum PalacePaidStoreReadStatus
    {
        Missing = 0,
        Valid = 1,
        Held = 2,
    }

    internal enum PalacePaidStoreApplyStatus
    {
        Changed = 0,
        NoChange = 1,
        Rejected = 2,
        Held = 3,
    }

    // 不可变只读结果：Missing 与成功态 Error 恒为 null；Held/Rejected 的 Error 为非空稳定码。
    internal sealed class PalacePaidStoreReadResult
    {
        public PalacePaidStoreReadStatus Status { get; }
        public PalacePaidEnvelope? Envelope { get; }
        public string? RawSha256 { get; }
        public string? Error { get; }
        public string? Detail { get; }

        private PalacePaidStoreReadResult(PalacePaidStoreReadStatus status, PalacePaidEnvelope? envelope, string? rawSha256, string? error, string? detail)
        {
            Status = status;
            Envelope = envelope;
            RawSha256 = rawSha256;
            Error = error;
            Detail = detail;
        }

        internal static PalacePaidStoreReadResult Missing()
        {
            return new PalacePaidStoreReadResult(PalacePaidStoreReadStatus.Missing, null, null, null, null);
        }

        internal static PalacePaidStoreReadResult Valid(PalacePaidEnvelope envelope, string rawSha256)
        {
            return new PalacePaidStoreReadResult(PalacePaidStoreReadStatus.Valid, envelope, rawSha256, null, null);
        }

        internal static PalacePaidStoreReadResult Held(string code, string? detail)
        {
            return new PalacePaidStoreReadResult(PalacePaidStoreReadStatus.Held, null, null, code, detail);
        }
    }

    internal sealed class PalacePaidStoreApplyResult
    {
        public PalacePaidStoreApplyStatus Status { get; }
        public PalacePaidEnvelope? Envelope { get; }
        public string? RawSha256 { get; }
        public string? Error { get; }
        public string? Detail { get; }

        private PalacePaidStoreApplyResult(PalacePaidStoreApplyStatus status, PalacePaidEnvelope? envelope, string? rawSha256, string? error, string? detail)
        {
            Status = status;
            Envelope = envelope;
            RawSha256 = rawSha256;
            Error = error;
            Detail = detail;
        }

        internal static PalacePaidStoreApplyResult Changed(PalacePaidEnvelope envelope, string rawSha256)
        {
            return new PalacePaidStoreApplyResult(PalacePaidStoreApplyStatus.Changed, envelope, rawSha256, null, null);
        }

        internal static PalacePaidStoreApplyResult NoChange(PalacePaidEnvelope envelope, string rawSha256)
        {
            return new PalacePaidStoreApplyResult(PalacePaidStoreApplyStatus.NoChange, envelope, rawSha256, null, null);
        }

        internal static PalacePaidStoreApplyResult Rejected(string code, string? detail)
        {
            return new PalacePaidStoreApplyResult(PalacePaidStoreApplyStatus.Rejected, null, null, code, detail);
        }

        internal static PalacePaidStoreApplyResult Held(string code, string? detail)
        {
            return new PalacePaidStoreApplyResult(PalacePaidStoreApplyStatus.Held, null, null, code, detail);
        }
    }

    internal sealed class PalacePaidJournalStore
    {
        private const string LockSuffix = ".lock";
        private const string IntentSuffix = ".intent";
        private const string TempInfix = ".tmp-";
        private const int DetailLimit = 200;
        private const int IntentByteLimit = 512;

        // 进程内：写者 guard（键 = 规范化目录 + scope 文件名）与 Held 闩锁（同键；跨实例静态共享）。
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> WriterGuards =
            new System.Collections.Concurrent.ConcurrentDictionary<string, object>(System.StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LatchedCodes =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(System.StringComparer.Ordinal);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LatchedDetails =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(System.StringComparer.Ordinal);

#if PALACE_PAID_JOURNAL_TESTS
        // 测试专用同步边界回调（编译期剔除；生产构建不含此字段）。
        internal static System.Action<string>? TestBoundaryCallback;
#endif

        private readonly string _directory;
        private readonly string _canonicalKeyPrefix;

        // 构造函数只做规范化与捕获：不创建目录/文件、不解析链接、不读取文件系统状态。
        public PalacePaidJournalStore(string absoluteFeatureDirectory)
        {
            if (absoluteFeatureDirectory == null)
            {
                throw new System.ArgumentException("absoluteFeatureDirectory is required", nameof(absoluteFeatureDirectory));
            }
            if (absoluteFeatureDirectory.Length == 0)
            {
                throw new System.ArgumentException("absoluteFeatureDirectory must not be empty", nameof(absoluteFeatureDirectory));
            }
            if (!System.IO.Path.IsPathFullyQualified(absoluteFeatureDirectory))
                throw new System.ArgumentException("absoluteFeatureDirectory must be absolute; no CWD fallback", nameof(absoluteFeatureDirectory));
            string normalized;
            try
            {
                normalized = System.IO.Path.GetFullPath(absoluteFeatureDirectory);
            }
            catch (System.ArgumentException ex)
            {
                throw new System.ArgumentException("absoluteFeatureDirectory must be a valid absolute path", nameof(absoluteFeatureDirectory), ex);
            }
            catch (System.NotSupportedException ex)
            {
                throw new System.ArgumentException("absoluteFeatureDirectory must be a valid absolute path", nameof(absoluteFeatureDirectory), ex);
            }
            if (!System.IO.Path.IsPathFullyQualified(normalized))
            {
                throw new System.ArgumentException("absoluteFeatureDirectory must be absolute; relative paths are rejected (no CWD fallback)", nameof(absoluteFeatureDirectory));
            }
            while (normalized.Length > (System.IO.Path.GetPathRoot(normalized)?.Length ?? 1) && (normalized[normalized.Length - 1] == System.IO.Path.DirectorySeparatorChar || normalized[normalized.Length - 1] == System.IO.Path.AltDirectorySeparatorChar))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }
            _directory = normalized;
            _canonicalKeyPrefix = normalized + "\n";
        }

        public PalacePaidStoreReadResult Read(PalacePaidScope owner)
        {
            if (owner == null) throw new System.ArgumentNullException(nameof(owner));
            string fileName = owner.StoreFileName();
            if (!PalacePaidScope.IsSafeStoreFileName(fileName)) return PalacePaidStoreReadResult.Held("held_unknown_residual", "unsafe scope name");
            string key = Key(fileName);
            if (TryGetLatched(key, out string code, out string detail)) return PalacePaidStoreReadResult.Held(code, detail);
            object guard = Guard(key);
            if (!System.Threading.Monitor.TryEnter(guard, 0)) return PalacePaidStoreReadResult.Held("held_lock_busy", "in-process scope is busy");
            try
            {
                if (TryGetLatched(key, out code, out detail)) return PalacePaidStoreReadResult.Held(code, detail);
                using (Observation observed = Observe(fileName, owner))
                {
                    if (observed.Held)
                    {
                        if (observed.Latch) Latch(key, observed.Code, observed.Detail);
                        return PalacePaidStoreReadResult.Held(observed.Code, observed.Detail);
                    }
                    return observed.Present ? PalacePaidStoreReadResult.Valid(observed.Existing!, observed.RawSha256!) : PalacePaidStoreReadResult.Missing();
                }
            }
            finally { System.Threading.Monitor.Exit(guard); }
        }

        public PalacePaidStoreApplyResult TryApply(string? expectedRawSha256, PalacePaidRequest request)
        {
            if (request == null) return PalacePaidStoreApplyResult.Rejected("rejected_Payload", "request is null");
            PalacePaidScope owner = request.Expectation.Owner;
            string fileName = owner.StoreFileName();
            if (!PalacePaidScope.IsSafeStoreFileName(fileName)) return PalacePaidStoreApplyResult.Rejected("rejected_Payload", "unsafe scope name");
            string key = Key(fileName);
            if (TryGetLatched(key, out string code, out string detail)) return PalacePaidStoreApplyResult.Held(code, detail);
            object guard = Guard(key);
            if (!System.Threading.Monitor.TryEnter(guard, 0)) return PalacePaidStoreApplyResult.Held("held_lock_busy", "in-process scope is busy");
            try
            {
                if (TryGetLatched(key, out code, out detail)) return PalacePaidStoreApplyResult.Held(code, detail);
                using (Observation pre = Observe(fileName, owner))
                {
                    if (pre.Held) return LatchApply(key, pre);
                    PalacePaidApplyResult decision = PalacePaidJournal.Apply(pre.Existing, expectedRawSha256, request);
                    if (decision.Outcome != PalacePaidOutcome.Changed)
                    {
                        // An absent lock may not be created for a zero-write decision. Recheck the observation boundary.
                        if (pre.LockStream == null && !CheckUnlockedEnd(fileName, pre.Present, out code, out detail))
                        { if (code != "held_lock_busy") Latch(key, code, detail); return PalacePaidStoreApplyResult.Held(code, detail); }
                        if (decision.Outcome == PalacePaidOutcome.Rejected) return PalacePaidStoreApplyResult.Rejected("rejected_" + decision.Error, decision.Detail);
                        return PalacePaidStoreApplyResult.NoChange(pre.Existing!, pre.RawSha256!);
                    }
                    if (pre.LockStream != null)
                    {
                        // Observe acquired the existing exclusive lock before reading; retain it through mutation.
                        TestBoundary("lock-acquired");
                        return Mutate(fileName, owner, decision, request, expectedRawSha256);
                    }
                    if (!TryValidateDirectoryPath(_directory, out code, out detail) || !CheckNoLinks(fileName, out code, out detail))
                    { Latch(key, code, detail); return PalacePaidStoreApplyResult.Held(code, detail); }
                    using (System.IO.FileStream? writer = AcquireFileLock(fileName, false, out code, out detail, out bool latch))
                    {
                        if (writer == null) { if (latch) Latch(key, code, detail); return PalacePaidStoreApplyResult.Held(code, detail); }
                        TestBoundary("lock-acquired");
                        using (Observation locked = Observe(fileName, owner, true))
                        {
                            if (locked.Held) return LatchApply(key, locked);
                            PalacePaidApplyResult authoritative = PalacePaidJournal.Apply(locked.Existing, expectedRawSha256, request);
                            if (authoritative.Outcome != PalacePaidOutcome.Changed)
                            {
                                // This operation already created the absent lock. It cannot claim a zero-write outcome.
                                Latch(key, "held_authority_drift", "scope changed after creating the writer lock; preserve observed journal and lock");
                                return PalacePaidStoreApplyResult.Held("held_authority_drift", "authoritative decision changed after lock creation");
                            }
                            return Mutate(fileName, owner, authoritative, request, expectedRawSha256);
                        }
                    }
                }
            }
            finally { System.Threading.Monitor.Exit(guard); }
        }

        // ───────────────────────── 内部：读写与校验 ─────────────────────────

        private sealed class ScopeScan
        {
            public bool JournalPresent;
            public bool LockPresent;
            public bool IntentPresent;
            public bool TempPresent;
        }

        private sealed class Observation : System.IDisposable
        {
            public System.IO.FileStream? LockStream;
            public void Dispose() { LockStream?.Dispose(); }
            public bool Held;
            public string Code = string.Empty;
            public string Detail = string.Empty;
            public bool Latch;
            public bool Present;
            public PalacePaidEnvelope? Existing;
            public string? RawSha256;
        }

        private string Key(string fileName)
        {
            return _canonicalKeyPrefix + fileName;
        }

        private static object Guard(string key)
        {
            return WriterGuards.GetOrAdd(key, _ => new object());
        }

        private static bool TryGetLatched(string key, out string code, out string detail)
        {
            if (LatchedCodes.TryGetValue(key, out string? storedCode))
            {
                code = storedCode;
                detail = LatchedDetails.TryGetValue(key, out string? storedDetail) ? storedDetail : "latched";
                return true;
            }
            code = string.Empty;
            detail = string.Empty;
            return false;
        }

        private static void Latch(string key, string code, string? detail)
        {
            LatchedCodes.TryAdd(key, code);
            LatchedDetails.TryAdd(key, Truncate(detail, DetailLimit));
        }

        private PalacePaidStoreReadResult LatchRead(string key, string code, string? detail)
        {
            Latch(key, code, detail);
            return PalacePaidStoreReadResult.Held(code, Truncate(detail, DetailLimit));
        }

        private static PalacePaidStoreApplyResult LatchApply(string key, Observation observation)
        {
            if (observation.Latch)
            {
                Latch(key, observation.Code, observation.Detail);
            }
            return PalacePaidStoreApplyResult.Held(observation.Code, Truncate(observation.Detail, DetailLimit));
        }

        private static string Truncate(string? text, int limit)
        {
            if (text == null)
            {
                return "held";
            }
            if (text.Length <= limit)
            {
                return text;
            }
            return text.Substring(0, limit) + "…";
        }

        // 目录与全部路径分量必须存在且不是链接/重解析点（只做属性观察，不 follow、不写入）。
        private static bool TryValidateDirectoryPath(string fullPath, out string code, out string detail)
        {
            try
            {
                string? current = fullPath;
                while (!string.IsNullOrEmpty(current))
                {
                    System.IO.FileAttributes attrs = System.IO.File.GetAttributes(current);
                    if ((attrs & System.IO.FileAttributes.ReparsePoint) != 0) { code = "held_symlink"; detail = "linked directory component"; return false; }
                    if ((attrs & System.IO.FileAttributes.Directory) == 0) { code = "held_directory_missing"; detail = "directory component changed entity"; return false; }
                    string? parent = System.IO.Path.GetDirectoryName(current);
                    if (string.Equals(parent, current, System.StringComparison.Ordinal)) break;
                    current = parent;
                }
                code = detail = string.Empty; return true;
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException)
            { code = "held_io_error"; detail = "directory physical check failed: " + ex.GetType().Name; return false; }
        }

        private static string NameOf(string path)
        {
            string name = System.IO.Path.GetFileName(path);
            return name.Length == 0 ? path : name;
        }

        private static bool IsLinkPath(string path)
        {
            return (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0;
        }

        // 枚举本 scope 前缀的全部条目并分类；任一链接、任一未知同前缀条目、任一超域写法都判不确定。
        private bool TryScanScope(string fileName, out ScopeScan scan, out string code, out string detail)
        {
            scan = new ScopeScan();
            string[] entries;
            try
            {
                entries = System.IO.Directory.GetFileSystemEntries(_directory);
            }
            catch (System.IO.IOException ex)
            {
                code = "held_io_error";
                detail = "feature directory enumeration failed: " + ex.GetType().Name;
                return false;
            }
            catch (System.UnauthorizedAccessException ex)
            {
                code = "held_io_error";
                detail = "feature directory enumeration denied: " + ex.GetType().Name;
                return false;
            }
            try
            {
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i];
                string name = System.IO.Path.GetFileName(entry);
                if (!name.StartsWith(fileName, System.StringComparison.Ordinal))
                {
                    continue;
                }
                if (IsLinkPath(entry))
                {
                    code = "held_symlink";
                    detail = "scope entry is a symlink/reparse point: " + name;
                    return false;
                }
                if ((System.IO.File.GetAttributes(entry) & System.IO.FileAttributes.Directory) != 0)
                { code = "held_io_error"; detail = "scope entry is a directory: " + name; return false; }
                if (string.Equals(name, fileName, System.StringComparison.Ordinal))
                {
                    scan.JournalPresent = true;
                    continue;
                }
                if (string.Equals(name, fileName + LockSuffix, System.StringComparison.Ordinal))
                {
                    scan.LockPresent = true;
                    continue;
                }
                if (string.Equals(name, fileName + IntentSuffix, System.StringComparison.Ordinal))
                {
                    scan.IntentPresent = true;
                    continue;
                }
                if (name.StartsWith(fileName + TempInfix, System.StringComparison.Ordinal) && IsTempSuffix(name.Substring(fileName.Length + TempInfix.Length)))
                {
                    scan.TempPresent = true;
                    continue;
                }
                code = "held_unknown_residual";
                detail = "unrecognized entry with the scope prefix: " + name;
                return false;
            }
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException)
            { code = "held_io_error"; detail = "scope physical check failed: " + ex.GetType().Name; return false; }
            code = string.Empty;
            detail = string.Empty;
            return true;
        }

        private static bool IsTempSuffix(string suffix)
        {
            if (suffix.Length != 32)
            {
                return false;
            }
            for (int i = 0; i < suffix.Length; i++)
            {
                char c = suffix[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }

        private bool CheckNoLinks(string fileName, out string code, out string detail)
        {
            if (!TryScanScope(fileName, out _, out code, out detail))
            {
                return false;
            }
            return true;
        }

        private static Observation HeldObservation(string code, string detail, bool latch = true)
        { return new Observation { Held = true, Code = code, Detail = detail, Latch = latch }; }

        private bool CheckUnlockedEnd(string fileName, bool journalPresent, out string code, out string detail)
        {
            if (!TryValidateDirectoryPath(_directory, out code, out detail) || !TryScanScope(fileName, out ScopeScan after, out code, out detail)) return false;
            if (after.LockPresent || after.IntentPresent || after.TempPresent || after.JournalPresent != journalPresent)
            { code = "held_lock_busy"; detail = "scope changed during an unlocked read; transient competition"; return false; }
            return true;
        }

        private Observation Observe(string fileName, PalacePaidScope owner, bool lockHeld = false)
        {
            if (!TryValidateDirectoryPath(_directory, out string code, out string detail) || !TryScanScope(fileName, out ScopeScan scan, out code, out detail))
                return HeldObservation(code, detail);
            if (!lockHeld && scan.LockPresent)
            {
                System.IO.FileStream? observer = AcquireFileLock(fileName, true, out code, out detail, out bool latch);
                if (observer == null) return HeldObservation(code, detail, latch);
                try { Observation inside = Observe(fileName, owner, true); inside.LockStream = observer; return inside; }
                catch { observer.Dispose(); throw; }
            }
            // A busy writer was tested first. Only a marker observed without a live holder may latch.
            if (scan.IntentPresent || scan.TempPresent)
                return HeldObservation(scan.IntentPresent ? "held_intent_present" : "held_temp_present", "unresolved scope residual");
            TestBoundary("observe-scanned");
            byte[]? bytes = null;
            bool present = scan.JournalPresent;
            if (present && !TryReadJournalBytes(fileName, out bytes, out code, out detail))
            {
                if (code.Length != 0) return HeldObservation(code, detail);
                // Exactly one rescan after an enumerated journal disappeared before open.
                if (!TryValidateDirectoryPath(_directory, out code, out detail) || !TryScanScope(fileName, out ScopeScan missing, out code, out detail)) return HeldObservation(code, detail);
                if (missing.JournalPresent || missing.IntentPresent || missing.TempPresent || (!lockHeld && missing.LockPresent)) return HeldObservation("held_lock_busy", "scope changed before journal open", false);
                present = false;
            }
            if (!lockHeld && !CheckUnlockedEnd(fileName, present, out code, out detail)) return HeldObservation(code, detail, code != "held_lock_busy");
            if (!present) return new Observation();
            PalacePaidResult<PalacePaidEnvelope> parsed = PalacePaidCodec.Parse(bytes!, new PalacePaidExpectation(owner));
            if (!parsed.Ok) return HeldObservation("held_corrupt_journal", "strict parse failed: " + parsed.Error);
            return new Observation { Present = true, Existing = parsed.Value!, RawSha256 = parsed.Value!.SourceBytesSha256 };
        }

        // Bounded actual EOF read, with physical path checks on both sides. BCL cannot prove inode identity.
        private bool TryReadJournalBytes(string fileName, out byte[]? bytes, out string code, out string detail)
        { return TryReadPhysicalBytes(fileName, JournalPath(fileName), PalacePaidCodec.MaxBytes, "journal", out bytes, out code, out detail); }

        private bool TryReadPhysicalBytes(string fileName, string path, int bound, string kind, out byte[]? bytes, out string code, out string detail)
        {
            bytes = null; code = detail = string.Empty;
            if (!TryValidateDirectoryPath(_directory, out code, out detail) || !CheckNoLinks(fileName, out code, out detail)) return false;
            try
            {
                using (System.IO.FileStream stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                    long length = stream.Length;
                    if (length > bound) { code = "held_oversize"; detail = kind + " exceeds bound"; return false; }
                    if (length <= 0) { code = "held_corrupt_journal"; detail = kind + " is empty"; return false; }
                    TestBoundary(kind + "-length-observed");
                    byte[] buffer = new byte[(int)length];
                    int total = 0;
                    while (total < buffer.Length)
                    {
                        int read = stream.Read(buffer, total, buffer.Length - total);
                        if (read <= 0) { code = "held_short_read"; detail = kind + " short read"; return false; }
                        total += read;
                    }
                    TestBoundary(kind + "-before-eof");
                    if (stream.ReadByte() != -1 || stream.Length != length) { code = "held_oversize"; detail = kind + " length changed or trailing bytes"; return false; }
                    if (!TryValidateDirectoryPath(_directory, out code, out detail) || !TryScanScope(fileName, out ScopeScan after, out code, out detail)) return false;
                    if (kind == "journal" ? !after.JournalPresent : !after.IntentPresent) { code = "held_io_error"; detail = kind + " disappeared after read"; return false; }
                    bytes = buffer; return true;
                }
            }
            catch (System.IO.FileNotFoundException) { return false; }
            catch (System.IO.DirectoryNotFoundException) { code = "held_io_error"; detail = "directory disappeared while opening " + kind; return false; }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException)
            { code = "held_io_error"; detail = kind + " read failed: " + ex.GetType().Name; return false; }
        }

        // ───────────────────────── 内部：锁与写入 ─────────────────────────

        private string JournalPath(string fileName)
        {
            return System.IO.Path.Combine(_directory, fileName);
        }

        private string LockPath(string fileName)
        {
            return System.IO.Path.Combine(_directory, fileName + LockSuffix);
        }

        private string IntentPath(string fileName)
        {
            return System.IO.Path.Combine(_directory, fileName + IntentSuffix);
        }

        private string TempPath(string fileName)
        {
            return System.IO.Path.Combine(_directory, fileName + TempInfix + System.Guid.NewGuid().ToString("N"));
        }

        // 已有 lock 只获取（不创建）；缺失 lock 只在本次确实要写时 CreateNew。
        // 竞争 → Held（本次尝试，不闩锁）；其它 IO 失败 → Held 并闩锁。
        private System.IO.FileStream? AcquireFileLock(string fileName, bool lockPresent, out string code, out string detail, out bool latch)
        {
            code = string.Empty;
            detail = string.Empty;
            latch = false;
            string path = LockPath(fileName);
            try
            {
                if (lockPresent)
                {
                    return new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
                }
                return new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
            }
            catch (System.IO.IOException ex)
            {
                if (!System.IO.Directory.Exists(path) && System.IO.File.Exists(path))
                {
                    code = "held_lock_busy";
                    detail = "cross-process scope lock is held by another writer; this attempt performs no write";
                    return null;
                }
                code = "held_lock_io_error";
                detail = "scope lock open failed: " + ex.GetType().Name;
                latch = true;
                return null;
            }
            catch (System.UnauthorizedAccessException ex)
            {
                code = "held_lock_unwritable";
                detail = "scope lock is not accessible: " + ex.GetType().Name;
                latch = true;
                return null;
            }
        }

        // 写入路径：intent（CreateNew/WriteThrough/Flush(true)，先于任何 journal 变更）→
        // temp（同目录唯一 CreateNew/WriteThrough/Flush(true)）→ 原子 replace → 全量读回校验 →
        // 删除本次自有 intent。任一失败：保留现场（intent/temp 不清理、不重试）并 Held+闩锁。
        private PalacePaidStoreApplyResult Mutate(string fileName, PalacePaidScope owner, PalacePaidApplyResult authoritative, PalacePaidRequest request, string? expectedRawSha256)
        {
            string key = Key(fileName);
            PalacePaidEnvelope intendedEnvelope = authoritative.Envelope!;
            byte[] intended = authoritative.CanonicalBytes!;
            string intendedSha = authoritative.CanonicalSha256!;
            if (!string.Equals(intendedSha, PalacePaidCodec.Sha256Hex(intended), System.StringComparison.Ordinal))
            {
                Latch(key, "held_io_error", "internal inconsistency: canonical bytes and hash disagree");
                return PalacePaidStoreApplyResult.Held("held_io_error", "internal inconsistency: canonical bytes and hash disagree");
            }
            if (expectedRawSha256 != null && !PalacePaidText.IsLowerHex(expectedRawSha256, 64))
            {
                Latch(key, "held_io_error", "internal inconsistency: changes require a valid expected raw SHA");
                return PalacePaidStoreApplyResult.Held("held_io_error", "internal inconsistency: changes require a valid expected raw SHA");
            }

            // 变更前重校验：目录/分量无链接，且除本次将创建的对象外无残留。
            if (!TryValidateDirectoryPath(_directory, out string code, out string detail) || !CheckNoLinks(fileName, out code, out detail))
            {
                Latch(key, code, detail);
                return PalacePaidStoreApplyResult.Held(code, Truncate(detail, DetailLimit));
            }
            if (!TryScanScope(fileName, out ScopeScan scan, out code, out detail))
            {
                Latch(key, code, detail);
                return PalacePaidStoreApplyResult.Held(code, Truncate(detail, DetailLimit));
            }
            if (scan.IntentPresent || scan.TempPresent)
            {
                Latch(key, "held_unknown_residual", "a scope residual appeared between evaluation and mutation");
                return PalacePaidStoreApplyResult.Held("held_unknown_residual", "a scope residual appeared between evaluation and mutation");
            }

            byte[] intentBytes = BuildIntent(owner.ScopeKey(), request.TripId, intendedEnvelope.Current.Revision, expectedRawSha256, intendedSha);
            if (intentBytes.Length > IntentByteLimit)
            {
                Latch(key, "held_io_error", "internal inconsistency: intent header exceeds its bound");
                return PalacePaidStoreApplyResult.Held("held_io_error", "internal inconsistency: intent header exceeds its bound");
            }
            string intentPath = IntentPath(fileName);
            try
            {
                using (System.IO.FileStream intentStream = new System.IO.FileStream(intentPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None, 512, System.IO.FileOptions.WriteThrough))
                {
                    intentStream.Write(intentBytes, 0, intentBytes.Length);
                    intentStream.Flush(true);
                }
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException || ex is System.NotSupportedException)
            {
                string intentCode = System.IO.File.Exists(intentPath) ? "held_intent_present" : "held_intent_create_failed";
                string intentDetail = "intent claim creation failed: " + ex.GetType().Name;
                Latch(key, intentCode, intentDetail);
                return PalacePaidStoreApplyResult.Held(intentCode, intentDetail);
            }
            TestBoundary("intent-created");

            string tempPath = TempPath(fileName);
            try
            {
                if (!TryValidateDirectoryPath(_directory, out code, out detail) || !CheckNoLinks(fileName, out code, out detail))
                {
                    Latch(key, code, detail);
                    return PalacePaidStoreApplyResult.Held(code, Truncate(detail, DetailLimit));
                }
                using (System.IO.FileStream tempStream = new System.IO.FileStream(tempPath, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None, 4096, System.IO.FileOptions.WriteThrough))
                {
                    tempStream.Write(intended, 0, intended.Length);
                    tempStream.Flush(true);
                }
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException || ex is System.NotSupportedException)
            {
                Latch(key, "held_temp_create_failed", "temp write failed: " + ex.GetType().Name);
                return PalacePaidStoreApplyResult.Held("held_temp_create_failed", "temp write failed: " + ex.GetType().Name);
            }
            TestBoundary("temp-written");

            if (!TryValidateDirectoryPath(_directory, out code, out detail) || !CheckNoLinks(fileName, out code, out detail))
            {
                Latch(key, code, detail);
                return PalacePaidStoreApplyResult.Held(code, Truncate(detail, DetailLimit));
            }
            try
            {
                System.IO.File.Move(tempPath, JournalPath(fileName), true);
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException || ex is System.NotSupportedException)
            {
                Latch(key, "held_rename_failed", "atomic replacement failed: " + ex.GetType().Name);
                return PalacePaidStoreApplyResult.Held("held_rename_failed", "atomic replacement failed: " + ex.GetType().Name);
            }
            TestBoundary("replaced");

            // 全量读回：bytes 等于写入意图、严格 Parse 成功、内容一致、来源绑定一致。
            if (!TryReadJournalBytes(fileName, out byte[]? readback, out code, out detail))
            {
                Latch(key, "held_readback_failed", detail.Length == 0 ? "readback read failed" : detail);
                return PalacePaidStoreApplyResult.Held("held_readback_failed", Truncate(detail.Length == 0 ? "readback read failed" : detail, DetailLimit));
            }
            if (!BytesEqual(readback!, intended))
            {
                Latch(key, "held_readback_mismatch", "readback bytes differ from the intended canonical bytes");
                return PalacePaidStoreApplyResult.Held("held_readback_mismatch", "readback bytes differ from the intended canonical bytes");
            }
            PalacePaidResult<PalacePaidEnvelope> readParsed = PalacePaidCodec.Parse(readback!, new PalacePaidExpectation(owner));
            if (!readParsed.Ok || !readParsed.Value!.ContentEquals(intendedEnvelope) || !string.Equals(readParsed.Value!.SourceBytesSha256, intendedSha, System.StringComparison.Ordinal))
            {
                Latch(key, "held_readback_mismatch", "readback does not reproduce the intended bound content");
                return PalacePaidStoreApplyResult.Held("held_readback_mismatch", "readback does not reproduce the intended bound content");
            }
            TestBoundary("readback-ok");

            // Compare the complete current marker with this operation's nonce-bearing bytes while retaining the lock.
            if (!TryReadPhysicalBytes(fileName, intentPath, IntentByteLimit, "intent", out byte[]? currentIntent, out code, out detail)
                || !BytesEqual(intentBytes, currentIntent!))
            {
                Latch(key, "held_intent_identity", "intent missing, changed, linked, or unreadable; preserve evidence");
                return PalacePaidStoreApplyResult.Held("held_intent_identity", "intent is not the complete owned marker");
            }
            if (!TryValidateDirectoryPath(_directory, out code, out detail) || !CheckNoLinks(fileName, out code, out detail))
            { Latch(key, code, detail); return PalacePaidStoreApplyResult.Held(code, detail); }
            try
            {
                System.IO.File.Delete(intentPath);
                TestBoundary("intent-deleted");
                if (!TryValidateDirectoryPath(_directory, out code, out detail) || !TryScanScope(fileName, out ScopeScan end, out code, out detail))
                { Latch(key, code, detail); return PalacePaidStoreApplyResult.Held(code, detail); }
                if (end.IntentPresent || end.TempPresent || !end.JournalPresent || !end.LockPresent)
                { Latch(key, "held_intent_delete_failed", "intent reappeared or physical scope drifted"); return PalacePaidStoreApplyResult.Held("held_intent_delete_failed", "intent reappeared or scope drifted"); }
            }
            catch (System.Exception ex) when (ex is System.IO.IOException || ex is System.UnauthorizedAccessException || ex is System.NotSupportedException)
            {
                Latch(key, "held_intent_delete_failed", "intent claim deletion failed after a verified replacement: " + ex.GetType().Name);
                return PalacePaidStoreApplyResult.Held("held_intent_delete_failed", "intent claim deletion failed after a verified replacement: " + ex.GetType().Name);
            }
            return PalacePaidStoreApplyResult.Changed(readParsed.Value!, readParsed.Value!.SourceBytesSha256!);
        }

        // intent header：有界、只含 scope/trip/revision 与 expected/new raw hash 的诊断信息，不是第二业务记录。
        private static byte[] BuildIntent(string scopeKey, System.Guid tripId, int revision, string? expectedRawSha256, string newRawSha256)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(320);
            builder.Append("{\"v\":1,\"scope\":\"").Append(scopeKey).Append("\",\"tripId\":\"").Append(tripId.ToString("D")).Append("\",\"revision\":");
            builder.Append(revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(",\"expectedRawSha256\":");
            if (expectedRawSha256 == null)
            {
                builder.Append("null");
            }
            else
            {
                builder.Append('"').Append(expectedRawSha256).Append('"');
            }
            builder.Append(",\"newRawSha256\":\"").Append(newRawSha256).Append("\",\"nonce\":\"").Append(System.Guid.NewGuid().ToString("N")).Append("\"}");
            return System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void TestBoundary(string name)
        {
#if PALACE_PAID_JOURNAL_TESTS
            System.Action<string>? callback = TestBoundaryCallback;
            if (callback != null)
            {
                callback(name);
            }
#else
            _ = name;
#endif
        }
    }
}
