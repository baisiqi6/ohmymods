using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 一枚金币当前 life 的 mod 本地来源（issue-89 八助手同君主单轮收币）。
///
/// 只保存证据本身，不做经济决策：
/// * <see cref="BankAssistantCoinOriginKind.KnownPlayer"/>：由七参 Droppable.Drop 的
///   实参 dropper（Wallet 所在 GO → Player）或 ReceivePolicyRPC 的完整 header
///   路径证明的具体君主对象；对象失效/换层由消费者降级，不猜最近玩家。
/// * <see cref="BankAssistantCoinOriginKind.UnknownPlayer"/>：原生已证 Player、具体
///   君主不可证的旧币/无 header 包；保留原 Player 收集资格但不冒充 owner。
/// * <see cref="BankAssistantCoinOriginKind.Farm"/>：Droppable.Drop 实参 dropper
///   属于 Farmland 的精确农田币标记。
///
/// 生命周期：仅以币对象实例（GameObject 引用 + InstanceID）为键；枚对象的
/// OnEnable/OnDisable/Drop2/Drop7 换代入口调用 <see cref="Clear"/> 递增 life 代数，
/// 消费者据此丢弃旧成熟观察，绝不把旧来源/旧观察带进复用后的新 life。
/// 打标不扣币、不认领、不产生经济账，也不改原生 reader 游标或 dropper 字段。
/// </summary>
internal enum BankAssistantCoinOriginKind : byte
{
    None = 0,
    UnknownPlayer = 1,
    KnownPlayer = 2,
    Farm = 3
}

internal static class BankAssistantCoinOrigin
{
    private sealed class Entry
    {
        internal GameObject Coin;
        internal BankAssistantCoinOriginKind Kind;
        internal Player Player;
        internal long Generation;
    }

    private static readonly Dictionary<int, Entry> Entries = new();
    private static long _nextGeneration;

    internal static void MarkKnownPlayer(DroppableCurrency coin, Player player)
    {
        if (coin == null || coin.gameObject == null
            || player == null || player.gameObject == null) return;
        try
        {
            Mark(coin, BankAssistantCoinOriginKind.KnownPlayer, player, newDrop: false);
        }
        catch (Exception) { }
    }

    /// <summary>真 Drop7 投掷事件（同 coin/同 Player/未经 OnEnable 也换代）。</summary>
    internal static void MarkDropKnownPlayer(DroppableCurrency coin, Player player)
    {
        if (coin == null || coin.gameObject == null
            || player == null || player.gameObject == null) return;
        try
        {
            Mark(coin, BankAssistantCoinOriginKind.KnownPlayer, player, newDrop: true);
        }
        catch (Exception) { }
    }

    /// <summary>真 Drop7 农田投掷事件。</summary>
    internal static void MarkDropFarm(DroppableCurrency coin)
    {
        if (coin == null || coin.gameObject == null) return;
        try
        {
            Mark(coin, BankAssistantCoinOriginKind.Farm, null, newDrop: true);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// 币离场/换 life/掉出旧身份：清来源并递增代数。墓碑即使原本无记录也会建立，
    /// 这样消费者能在同一 InstanceID + 同一 native pointer 复用后察觉新 life。
    /// </summary>
    internal static void Clear(DroppableCurrency coin)
    {
        if (coin == null || coin.gameObject == null) return;
        try
        {
            if (!GreekBankScope.IsActive)
            {
                // 非当前希腊世界：只删除已有自有记录，绝不新建墓碑——其他世界的币
                // 不进本协调器的表，也不会被扫描/清理。经济回执不在此处理。
                RemoveExisting(coin);
                return;
            }
            Entry entry = GetOrCreate(coin);
            if (entry == null) return;
            entry.Generation = ++_nextGeneration;
            entry.Kind = BankAssistantCoinOriginKind.None;
            entry.Player = null;
            entry.Coin = coin.gameObject;
        }
        catch (Exception) { }
    }

    private static void RemoveExisting(DroppableCurrency coin)
    {
        int id;
        try { id = coin.gameObject.GetInstanceID(); }
        catch (Exception) { return; }
        if (!Entries.TryGetValue(id, out Entry entry)) return;
        if (entry.Coin == null || entry.Coin.Pointer != coin.gameObject.Pointer) return;
        Entries.Remove(id);
    }

    /// <summary>
    /// policy 包只降级“曾经是具体君主”的记录：无 header / 短包 / 异常路径不能证明
    /// 来源，但也不能把 exactFarm 标记或原生 Player 资格一并抹掉。
    /// </summary>
    internal static void DemoteKnownToUnknown(DroppableCurrency coin)
    {
        if (coin == null || coin.gameObject == null) return;
        try
        {
            // 只降级已有 Known 记录：无记录时不建表（非 Greek 的 policy 包不产生墓碑）。
            Entry entry = Find(coin);
            if (entry == null || entry.Kind != BankAssistantCoinOriginKind.KnownPlayer) return;
            // 同一 life 的重新分类：不换代数（否则重复 policy 包会反复重置成熟时间）。
            entry.Kind = BankAssistantCoinOriginKind.UnknownPlayer;
            entry.Player = null;
        }
        catch (Exception) { }
    }

    /// <summary>
    /// 该币当前 life 的来源与（KnownPlayer 时）实际君主对象。存储的 Player 引用
    /// 已销毁时返回 UnknownPlayer 并给出 null：具体身份不可证，资格边界不变。
    /// </summary>
    internal static BankAssistantCoinOriginKind KindOf(DroppableCurrency coin, out Player player)
    {
        player = null;
        Entry entry = Find(coin);
        if (entry == null) return BankAssistantCoinOriginKind.None;
        if (entry.Kind == BankAssistantCoinOriginKind.KnownPlayer)
        {
            Player owner = entry.Player;
            if (owner == null || owner.gameObject == null) return BankAssistantCoinOriginKind.UnknownPlayer;
            player = owner;
            return BankAssistantCoinOriginKind.KnownPlayer;
        }
        return entry.Kind;
    }

    /// <summary>当前 life 代数；无记录为 0。消费者用它检测同实例的新 life。</summary>
    internal static long GenerationOf(DroppableCurrency coin)
    {
        Entry entry = Find(coin);
        return entry != null ? entry.Generation : 0L;
    }

    /// <summary>无代递增的记账清理（币随扫描离场时调用）。</summary>
    internal static void RemoveSilently(int instanceId)
    {
        Entries.Remove(instanceId);
    }

    internal static void ClearAll()
    {
        Entries.Clear();
    }

    private static void Mark(DroppableCurrency coin, BankAssistantCoinOriginKind kind, Player player,
        bool newDrop)
    {
        Entry entry = GetOrCreate(coin);
        if (entry == null) return;
        if (newDrop || entry.Kind != kind || !SamePlayer(entry.Player, player))
        {
            // 真投掷事件或来源变化才换代数；重复同来源 policy 包保持原代数，
            // 因此不会刷新已观察币的成熟时间。
            entry.Generation = ++_nextGeneration;
        }
        entry.Kind = kind;
        entry.Player = player;
        entry.Coin = coin.gameObject;
    }

    private static Entry GetOrCreate(DroppableCurrency coin)
    {
        int id = coin.gameObject.GetInstanceID();
        if (!Entries.TryGetValue(id, out Entry entry))
        {
            entry = new Entry { Generation = ++_nextGeneration };
            Entries[id] = entry;
        }
        return entry;
    }

    private static Entry Find(DroppableCurrency coin)
    {
        if (coin == null || coin.gameObject == null) return null;
        int id;
        try { id = coin.gameObject.GetInstanceID(); }
        catch (Exception) { return null; }
        if (!Entries.TryGetValue(id, out Entry entry)) return null;
        // 实例 ID 复用但对象身份不同：不是本记录的 life。
        if (entry.Coin == null || entry.Coin.Pointer != coin.gameObject.Pointer) return null;
        return entry;
    }

    private static bool SamePlayer(Player left, Player right)
        => (left == null && right == null)
        || (left != null && right != null && left.gameObject != null && right.gameObject != null
            && left.Pointer == right.Pointer);
}
