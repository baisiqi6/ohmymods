using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KingdomEnhancedMod;

/// <summary>
/// 金币哥布林 campaign 存档的单键 schema（纯 C#，无 Unity/Il2Cpp 引用，离线套件直接链接）。
///
/// 存储位置：<c>GlobalSaveData.prefs</c>（原生 string KV）里的唯一命名空间键
/// <see cref="Key"/>；不写原生 JSON 顶层 schema、不用 sidecar/Filer、不碰银行
/// UnityEngine.PlayerPrefs。所有读失败/损坏/未知版本只允许“关闭本模块”，绝不猜测成
/// 空档或 0 余额（保留旧字符串、不截断、不覆写）。
/// </summary>
internal static class CoinCourierSaveSchema
{
    /// <summary>唯一键名（含版本；版本另以 <c>version</c> 字段再校验一次）。</summary>
    internal const string Key = "KEM.CoinCourier.Campaigns.v1";

    internal const int Version = 1;

    /// <summary>普通 campaign 上限（原生 MAX_CAMPAIGNS 同量级；超界=损坏，不截断）。</summary>
    internal const int MaxCampaigns = 32;

    /// <summary>单键字符串上限（UTF-8 字节）；超界不写入并冻结，不截断。</summary>
    internal const int MaxDocumentBytes = 128 * 1024;

    /// <summary>钱袋金币合理上限（配置容量上限 40；纯损坏护栏，命中即拒绝不猜测）。</summary>
    internal const int MaxPurseCoins = 1_000_000;
}

/// <summary>
/// 一个 campaign 在键里的完整 staged 快照。全部为显式公开属性，由 JsonSerializer 序列化；
/// 绝不依赖 internal readonly 字段被反射自动写出。
/// </summary>
internal sealed class CoinCourierCampaignRecord
{
    public int Slot { get; set; }
    public string Guid { get; set; }
    public bool Owned { get; set; }
    public CoinCourierPurseRecord Purse { get; set; }
}

/// <summary>钱袋快照（含未决交接与已锁存故障）。</summary>
internal sealed class CoinCourierPurseRecord
{
    public int Coins { get; set; }
    public bool PendingDelivery { get; set; }
    public long PendingLife { get; set; }
    public CoinCourierFaultRecord Fault { get; set; }
}

/// <summary>未决结果证据（CoinCourierFault 的显式序列化形状；-1 = 未观测）。</summary>
internal sealed class CoinCourierFaultRecord
{
    public int Kind { get; set; }
    public int Reason { get; set; }
    public int BankBefore { get; set; }
    public int BankAfter { get; set; }
    public int WalletBefore { get; set; }
    public int WalletAfter { get; set; }
    public long ExpectedLife { get; set; }
}

/// <summary>单键文档。</summary>
internal sealed class CoinCourierDocument
{
    public int Version { get; set; }
    public List<CoinCourierCampaignRecord> Campaigns { get; set; }
}

/// <summary>
/// 纯编解码/校验核：负责 DTO&lt;-&gt;purse 快照映射、严格损坏判定、确定性序列化。
/// 不持有任何运行时状态、不接触原生对象。
/// </summary>
internal static class CoinCourierSaveCodec
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    internal static string CreateGuid() => Guid.NewGuid().ToString("D");

    internal static CoinCourierDocument CreateDocument()
        => new CoinCourierDocument { Version = CoinCourierSaveSchema.Version, Campaigns = new List<CoinCourierCampaignRecord>() };

    /// <summary>确定性序列化 + 上限检查；失败必须保留旧字符串，绝不截断。</summary>
    internal static bool TrySerialize(CoinCourierDocument document, out string json, out string reason)
    {
        json = null;
        reason = null;
        try
        {
            if (document == null || document.Campaigns == null) { reason = "null-document"; return false; }
            if (document.Campaigns.Count > CoinCourierSaveSchema.MaxCampaigns) { reason = "too-many-campaigns"; return false; }
            document.Version = CoinCourierSaveSchema.Version;
            string text = JsonSerializer.Serialize(document, Options);
            if (string.IsNullOrEmpty(text)) { reason = "empty-serialization"; return false; }
            if (Encoding.UTF8.GetByteCount(text) > CoinCourierSaveSchema.MaxDocumentBytes) { reason = "too-large"; return false; }
            json = text;
            return true;
        }
        catch (Exception)
        {
            json = null;
            reason = "serialize-fault";
            return false;
        }
    }

    /// <summary>
    /// 严格解析：任何异常/未知版本/超界/重复/越界字段都判为损坏（reason 供有界诊断）。
    /// 只有完整合法的文档才返回 true；调用方对损坏一律关闭本模块且不覆写。
    /// </summary>
    internal static bool TryParse(string json, out CoinCourierDocument document, out string reason)
    {
        document = null;
        reason = null;
        if (string.IsNullOrWhiteSpace(json)) { reason = "empty"; return false; }
        if (Encoding.UTF8.GetByteCount(json) > CoinCourierSaveSchema.MaxDocumentBytes) { reason = "too-large"; return false; }
        CoinCourierDocument parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CoinCourierDocument>(json, Options);
        }
        catch (Exception)
        {
            reason = "json";
            return false;
        }
        if (parsed == null) { reason = "null-document"; return false; }
        if (parsed.Version != CoinCourierSaveSchema.Version) { reason = "version"; return false; }
        if (parsed.Campaigns == null) { reason = "campaigns-missing"; return false; }
        if (parsed.Campaigns.Count > CoinCourierSaveSchema.MaxCampaigns) { reason = "too-many-campaigns"; return false; }
        var slots = new HashSet<int>();
        var guids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < parsed.Campaigns.Count; i++)
        {
            CoinCourierCampaignRecord record = parsed.Campaigns[i];
            if (record == null) { reason = "record-null"; return false; }
            if (record.Slot < 0) { reason = "slot-range"; return false; }
            if (!slots.Add(record.Slot)) { reason = "slot-duplicate"; return false; }
            if (string.IsNullOrEmpty(record.Guid) || !Guid.TryParse(record.Guid, out _)) { reason = "guid"; return false; }
            if (!guids.Add(record.Guid)) { reason = "guid-duplicate"; return false; }
            if (!TryReadPurse(record.Purse, out _, out string purseReason)) { reason = purseReason; return false; }
        }
        document = parsed;
        return true;
    }

    /// <summary>purse 记录 -&gt; 纯快照（严格校验；失败 reason 说明具体损坏字段）。</summary>
    internal static bool TryReadPurse(CoinCourierPurseRecord record, out CoinCourierPurseSnapshot snapshot, out string reason)
    {
        snapshot = default;
        reason = null;
        if (record == null) { reason = "purse-missing"; return false; }
        if (record.Coins < 0 || record.Coins > CoinCourierSaveSchema.MaxPurseCoins) { reason = "coins-range"; return false; }
        if (record.PendingDelivery)
        {
            if (record.Coins < 1) { reason = "pending-empty"; return false; }
            if (record.PendingLife == 0) { reason = "pending-life"; return false; }
        }
        else if (record.PendingLife != 0)
        {
            reason = "pending-flag";
            return false;
        }
        if (!TryReadFault(record.Fault, out CoinCourierFault fault, out reason)) return false;
        snapshot = new CoinCourierPurseSnapshot(record.Coins, record.PendingDelivery, record.PendingLife, fault);
        return true;
    }

    /// <summary>fault 记录 -&gt; 纯证据；null 记录 = 无故障。</summary>
    internal static bool TryReadFault(CoinCourierFaultRecord record, out CoinCourierFault fault, out string reason)
    {
        fault = default;
        reason = null;
        if (record == null) return true;
        if (record.Kind < (int)CoinCourierFaultKind.BagUnknown || record.Kind > (int)CoinCourierFaultKind.DeliveryUnknown)
        {
            reason = "fault-kind";
            return false;
        }
        if (record.Reason < 0 || record.Reason > (int)CoinCourierReason.PurseCreditRefused)
        {
            reason = "fault-reason";
            return false;
        }
        if (record.BankBefore < -1 || record.BankAfter < -1 || record.WalletBefore < -1 || record.WalletAfter < -1)
        {
            reason = "fault-range";
            return false;
        }
        if (record.ExpectedLife < 0) { reason = "fault-life"; return false; }
        fault = new CoinCourierFault((CoinCourierFaultKind)record.Kind, (CoinCourierReason)record.Reason,
            record.BankBefore, record.BankAfter, record.WalletBefore, record.WalletAfter, record.ExpectedLife);
        return true;
    }

    /// <summary>纯快照 -&gt; purse 记录（fault 为 None 时不写 Fault 对象）。</summary>
    internal static CoinCourierPurseRecord ToPurseRecord(in CoinCourierPurseSnapshot snapshot)
    {
        return new CoinCourierPurseRecord
        {
            Coins = snapshot.Coins,
            PendingDelivery = snapshot.PendingDelivery,
            PendingLife = snapshot.PendingLife,
            Fault = ToFaultRecord(snapshot.Fault),
        };
    }

    internal static CoinCourierFaultRecord ToFaultRecord(in CoinCourierFault fault)
    {
        if (fault.Kind == CoinCourierFaultKind.None) return null;
        return new CoinCourierFaultRecord
        {
            Kind = (int)fault.Kind,
            Reason = (int)fault.Reason,
            BankBefore = fault.BankBefore,
            BankAfter = fault.BankAfter,
            WalletBefore = fault.WalletBefore,
            WalletAfter = fault.WalletAfter,
            ExpectedLife = fault.ExpectedLife,
        };
    }

    internal static CoinCourierPurseSnapshot EmptyPurseSnapshot
        => new CoinCourierPurseSnapshot(0, false, 0, default);

    /// <summary>该 staged 快照是否有需要写入键的内容（未购且空袋的 campaign 不落记录）。</summary>
    internal static bool HasContent(bool owned, in CoinCourierPurseSnapshot snapshot)
        => owned || snapshot.Coins > 0 || snapshot.PendingDelivery || snapshot.Fault.Kind != CoinCourierFaultKind.None;
}
