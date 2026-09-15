// Hermes 头饰轮换游标 + 稳定配额（主机全局、跨存档/跨进程接续；不读写任何原生存档字段）。
//
// 契约（schema2）：
//  * 文件：<BepInEx.Paths.ConfigPath>/KingdomEnhancedMod/ModSave/hermes-headwear-cycle.v2.json；
//    schema2 = {"v":2,"kind":"hermes-headwear-cycle","nextChoice":0..43,"credit":0..99}。
//    闭 schema：未知/重复字段、未知版本、越界值、空文件、超 4 KiB 一律判非法（拒绝且一字不改）。
//  * 唯一入口 TryAssign(chancePercent, out choice)：Operator 在 EffectiveHostEnabled + ReadChancePercent
//    之后直接调用。确定性配额（无任何 RNG）：每只新转换的小怪 credit += chance（chance 按 0..100 夹取）；
//    credit >= 100 时 credit -= 100、发放 nextChoice 并把 44 轮换推进一位；否则只记录 credit（choice=-1）。
//    true = 本次配额已成功落盘（choice 可能为 -1）；false = 非主机/路径缺失/读取校验失败/写盘失败，
//    choice 恒 -1 且盘上零进度。30% 从 credit 0 起在第 4/7/10 只发放（每 10 只 3 只）；跨技能使用与
//    进程重启由盘上 credit 接续。chance <= 0 不推进、不写文件（直接 true + choice=-1）。
//  * 旧 v1（schema1）迁移：v2 不存在时只读兄弟文件 hermes-headwear-cycle.v1.json（同一套闭 schema 校验），
//    把其 nextChoice 带入、credit 从 0 起；旧 v1 永远保留、绝不写删（读时 FileAccess.Read）。
//    v2 一旦存在即优先：坏/未知 v2 一律拒绝，绝不回退 v1、绝不覆盖。
//  * 进度以盘上为准：外部写入的合法值被采纳后再推进（绝不基于陈旧内存覆写未知新值）；外部损坏/
//    未知 schema 只拒绝本次，不静默重置、不降级。
//  * 权限：只有主机（NetworkBigBoss.HasWorldAuth）可推进；非主机/探测异常绝不写盘。
//  * I/O：仅转化事件内同步发生（每次至多两次小文件读 + 一次原子写），无 Tick/扫描/后台线程。
//    原子写 = 同目录唯一 temp → 完整写 + Flush(true) → File.Replace（保留 .bak）或 Move；异常清 temp。
//  * 日志：固定类目、每类目最多一条，不包含完整路径等敏感内容。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KingdomEnhancedMod
{
    /// <summary>主机全局“下一顶”游标 + 稳定命中配额：见文件头契约。</summary>
    internal static class HermesHeadwearCycle
    {
        /// <summary>0..43：与 HermesHeadwearCodec.MinChoice/MaxChoice 对齐的固定码总数。</summary>
        internal const int ChoiceCount = 44;

        /// <summary>对齐 HermesHeadwearCodec.ChoiceNone：本次不戴头饰（保持原生外观）。</summary>
        internal const int ChoiceNone = -1;

        /// <summary>盘上 schema 版本；未知版本拒绝、不降级。</summary>
        internal const int SchemaVersion = 2;

        /// <summary>旧 v1（只读迁移来源）的 schema 版本。</summary>
        internal const int LegacySchemaVersion = 1;

        /// <summary>配额刻度：credit 与 chance 同为百分点，满 100 点发放一顶。</summary>
        internal const int CreditScale = 100;

        /// <summary>单文件上限 4 KiB（结构固定，正常仅 ~60 B）；超限不解析、不改写。</summary>
        internal const long MaxFileBytes = 4L * 1024;

        private const string KindMarker = "hermes-headwear-cycle";
        private const string FileName = "hermes-headwear-cycle.v2.json";
        private const string LegacyFileName = "hermes-headwear-cycle.v1.json";
        private const string VField = "v";
        private const string KindField = "kind";
        private const string NextChoiceField = "nextChoice";
        private const string CreditField = "credit";
        private const int MaxLogCategories = 64;

        private static readonly object Gate = new object();
        private static readonly HashSet<string> LoggedCategories = new HashSet<string>(StringComparer.Ordinal);
        private static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 2,
        };

        /// <summary>
        /// 记一只新转换小怪的配额：命中 true + choice 0..43；未命中 true + choice=-1（credit 已落盘）；
        /// 任何失败 false + choice=-1（盘上零进度，该只保留原生外观；后续新转化继续原配额）。
        /// 原子持久化成功前绝不返回值，已有收据不因重启重新计数。
        /// </summary>
        internal static bool TryAssign(int chancePercent, out int choice)
        {
            choice = ChoiceNone;
            lock (Gate)
            {
                if (!IsHostAuthority()) { LogOnce("authority"); return false; }
                if (chancePercent <= 0) return true; // 0/负数：不推进、不写文件，配额视为已记录
                int chance = chancePercent > CreditScale ? CreditScale : chancePercent; // 配置越界按 0..100 夹取
                string path = ResolvePath();
                if (string.IsNullOrEmpty(path)) { LogOnce("path"); return false; }
                if (!TryReadState(path, out int nextChoice, out int credit, out bool existed, out string reason))
                { LogOnce("refuse-" + reason); return false; }
#if HERMES_CYCLE_TEST
                Action<string> hook = BeforeWriteHook;
                if (hook != null) hook(path); // 测试注入的“读后写前”精确屏障；生产编译不含
#endif
                int total = credit + chance;
                int assigned = ChoiceNone;
                int next;
                if (total >= CreditScale)
                {
                    total -= CreditScale;              // 一次入账至多发放一顶（chance ≤ 100、credit ≤ 99）
                    assigned = nextChoice;
                    next = (nextChoice + 1) % ChoiceCount;
                }
                else
                {
                    next = nextChoice;
                }
                if (!TryWriteAtomic(path, next, total, existed)) { LogOnce("write-failed"); return false; }
                choice = assigned;
                return true;
            }
        }

        /// <summary>
        /// 盘上状态：v2 优先；v2 确证缺失时只读旧 v1 兄弟文件（合法则带入 nextChoice、credit=0），
        /// 两者都不在则 0/0 起。existed = v2 读取时是否已存在（决定写入走 Replace 还是 Move）。
        /// </summary>
        private static bool TryReadState(string path, out int nextChoice, out int credit, out bool existed, out string reason)
        {
            nextChoice = 0;
            credit = 0;
            existed = false;
            reason = null;

            if (!TryReadFile(path, out byte[] bytes, out existed, out reason)) return false;
            if (existed) return TryParseV2(bytes, out nextChoice, out credit, out reason);

            string legacy = LegacyPathOf(path);
            if (legacy == null) { reason = "legacy-path"; return false; }
            if (!TryReadFile(legacy, out byte[] legacyBytes, out bool legacyExisted, out reason)) return false;
            if (!legacyExisted) return true; // 首次：0/0 起
            if (!TryParseV1(legacyBytes, out nextChoice, out string legacyError))
            { reason = "legacy-" + legacyError; return false; }
            credit = 0;
            return true;
        }

        /// <summary>
        /// 一次有界读取（缓冲区 4 KiB）。只有明确的 FileNotFoundException / DirectoryNotFoundException
        /// 才当“确证缺失”（existed=false，不抛）；其余 I/O/共享/权限失败一律 false + reason——
        /// File.Exists 会把访问错误折叠成 false，绝不能当作缺失（否则误判首次 0 起并覆盖未知旧游标）。
        /// </summary>
        private static bool TryReadFile(string path, out byte[] bytes, out bool existed, out string reason)
        {
            bytes = null;
            existed = false;
            reason = null;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan))
                {
                    existed = true;
                    long length = stream.Length;
                    if (length <= 0) { reason = "empty"; return false; }
                    if (length > MaxFileBytes) { reason = "size"; return false; }
                    byte[] buffer = new byte[(int)length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int chunk = stream.Read(buffer, read, buffer.Length - read);
                        if (chunk <= 0) break;
                        read += chunk;
                    }
                    if (read != buffer.Length || stream.ReadByte() != -1) { reason = "unstable"; return false; }
                    bytes = buffer;
                    return true;
                }
            }
            catch (FileNotFoundException) { return true; }      // 确证缺失（v1 只读探测同样适用）
            catch (DirectoryNotFoundException) { return true; } // 确证缺失：父目录都不存在
            catch (Exception e) when (IsIoFailure(e)) { reason = "io-" + e.GetType().Name; return false; }
        }

        /// <summary>旧 v1 只读迁移来源：与 v2 同目录的兄弟文件（测试 seam 只注入 v2，故按目录推导）。</summary>
        private static string LegacyPathOf(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(full);
                return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, LegacyFileName);
            }
            catch (Exception e) when (IsIoFailure(e)) { return null; }
        }

        /// <summary>schema1（旧 v1）：闭 schema，非法一律 false（不抛、不部分接受）。</summary>
        internal static bool TryParseV1(byte[] utf8, out int nextChoice, out string error)
        {
            return TryParseCore(utf8, LegacySchemaVersion, withCredit: false, out nextChoice, out int _, out error);
        }

        /// <summary>schema2（当前）：闭 schema，非法一律 false（不抛、不部分接受）。</summary>
        internal static bool TryParseV2(byte[] utf8, out int nextChoice, out int credit, out string error)
        {
            return TryParseCore(utf8, SchemaVersion, withCredit: true, out nextChoice, out credit, out error);
        }

        private static bool TryParseCore(byte[] utf8, int expectedVersion, bool withCredit,
            out int nextChoice, out int credit, out string error)
        {
            nextChoice = 0;
            credit = 0;
            error = null;
            if (utf8 == null || utf8.Length == 0 || utf8.Length > MaxFileBytes) { error = "size"; return false; }
            JsonDocument document;
            try { document = JsonDocument.Parse(utf8, ParseOptions); }
            catch (JsonException) { error = "json"; return false; }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) { error = "root"; return false; }

                int required = withCredit ? 15 : 7; // v/kind/nextChoice[/credit] 四位全部必须出现
                int seen = 0;
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    int slot;
                    if (property.NameEquals(VField)) slot = 0;
                    else if (property.NameEquals(KindField)) slot = 1;
                    else if (property.NameEquals(NextChoiceField)) slot = 2;
                    else if (withCredit && property.NameEquals(CreditField)) slot = 3;
                    else { error = "unknown"; return false; }

                    if ((seen & (1 << slot)) != 0) { error = "dup"; return false; }
                    seen |= 1 << slot;

                    JsonElement value = property.Value;
                    if (slot == 0 && (!TryInt(value, out int version) || version != expectedVersion))
                    { error = "schema"; return false; }
                    if (slot == 1 && (value.ValueKind != JsonValueKind.String
                        || !string.Equals(value.GetString(), KindMarker, StringComparison.Ordinal)))
                    { error = "kind"; return false; }
                    if (slot == 2)
                    {
                        if (!TryInt(value, out int code) || code < 0 || code >= ChoiceCount) { error = "range"; return false; }
                        nextChoice = code;
                    }
                    if (slot == 3)
                    {
                        if (!TryInt(value, out int points) || points < 0 || points >= CreditScale) { error = "credit"; return false; }
                        credit = points;
                    }
                }

                if ((seen & required) != required) { error = "missing"; return false; }
                return true;
            }
        }

        private static bool TryInt(JsonElement value, out int result)
        {
            result = 0;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
        }

        /// <summary>
        /// 确定性字节：{"v":2,"kind":"hermes-headwear-cycle","nextChoice":N,"credit":C}（无空白、无随机）。
        /// </summary>
        private static byte[] SerializeV2(int nextChoice, int credit)
        {
            using (MemoryStream stream = new MemoryStream(64))
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(VField, SchemaVersion);
                    writer.WriteString(KindField, KindMarker);
                    writer.WriteNumber(NextChoiceField, nextChoice);
                    writer.WriteNumber(CreditField, credit);
                    writer.WriteEndObject();
                }
                return stream.ToArray();
            }
        }

        /// <summary>
        /// 同目录唯一 temp → 完整写 + Flush(true) → 写回。写回方式严格跟随读取时的存在状态：
        /// 读时有 → 只 File.Replace（保留 .bak），写时已丢失则失败而不是重新创建；
        /// 读时无 → 只 File.Move（写时已有别的文件则拒绝覆盖：Move 本身 + 显式 File.Exists 护栏）。
        /// 失败原文件不动、temp 清除。
        /// </summary>
        private static bool TryWriteAtomic(string path, int nextChoice, int credit, bool existedAtRead)
        {
            byte[] bytes = SerializeV2(nextChoice, credit);
            if (bytes.Length > MaxFileBytes) return false;

            string tempPath = null;
            try
            {
                string full = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(full);
                if (string.IsNullOrEmpty(directory)) return false;

                tempPath = Path.Combine(directory, Path.GetFileName(full) + ".tmp-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (existedAtRead) File.Replace(tempPath, path, path + ".bak", true);
                else if (File.Exists(path)) return false; // 读后出现：绝不覆盖兄弟文件（跨平台一致护栏）
                else File.Move(tempPath, path);
                return true;
            }
            catch (Exception e) when (IsIoFailure(e)) { return false; }
            finally
            {
                // 只清自己这一个 exact temp；绝不扫描目录、绝不删其他文件。
                if (tempPath != null)
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch (Exception e) when (IsIoFailure(e)) { }
                }
            }
        }

        private static bool IsIoFailure(Exception e)
        {
            return e is IOException || e is UnauthorizedAccessException || e is NotSupportedException
                || e is ArgumentException || e is System.Security.SecurityException;
        }

        private static bool IsHostAuthority()
        {
#if HERMES_CYCLE_TEST
            try { Func<bool> probe = AuthorityOverride; return probe != null && probe(); }
            catch { return false; }
#else
            try { return NetworkBigBoss.HasWorldAuth; }
            catch (Exception e) { LogOnce("authority-" + e.GetType().Name); return false; }
#endif
        }

        private static string ResolvePath()
        {
#if HERMES_CYCLE_TEST
            return string.IsNullOrEmpty(PathOverride) ? null : PathOverride;
#else
            try { return Path.Combine(BepInEx.Paths.ConfigPath, "KingdomEnhancedMod", "ModSave", FileName); }
            catch (Exception e) { LogOnce("config-path-" + e.GetType().Name); return null; }
#endif
        }

        /// <summary>固定类目、每类目一次；key 集有界，消息不含完整路径等敏感内容。</summary>
        private static void LogOnce(string category)
        {
            if (LoggedCategories.Count >= MaxLogCategories && !LoggedCategories.Contains(category)) return;
            if (!LoggedCategories.Add(category)) return;
#if !HERMES_CYCLE_TEST
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[HermesHeadwearCycle] " + category); }
            catch
            {
                // 日志不可用不影响功能。
            }
#endif
        }

#if HERMES_CYCLE_TEST
        /// <summary>测试专用（生产编译不含）：主机权限探针；null = 非主机。</summary>
        internal static Func<bool> AuthorityOverride;

        /// <summary>测试专用（生产编译不含）：v2 目标文件路径；未注入时不写盘（绝不落到真实 BepInEx 目录）。</summary>
        internal static string PathOverride;

        /// <summary>测试专用（生产编译不含）：读后写前的精确屏障（模拟读后外部改动），参数为 v2 路径。</summary>
        internal static Action<string> BeforeWriteHook;

        /// <summary>测试专用：清空一次性日志类目（模拟新进程重启；权限/路径覆盖项不动）。</summary>
        internal static void ResetForTests()
        {
            LoggedCategories.Clear();
        }
#endif
    }
}
