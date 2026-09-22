// 骑士稳定身份（GUID + style 0..4）运行时与独立 sidecar 存档桥。
//
// 契约（与 KnightIdentityArchive.cs / KnightIdentityContext.cs 核心配套）：
//  * 不读写任何原生存档字段/名字/JSON；native uniqueID 只作为「某一份精确原生岛快照」里的索引。
//  * 状态独立于原 KnightStyle/KnightStyleState：Strip、关闭 mod 都只是视觉，不删收据；
//    OnEnable 的清理不受 ModConfig.Enabled 门影响；Save 关闭功能时仍保存已有收据。
//  * 只有主机（NetworkBigBoss.HasWorldAuth，离线为真）才能创建 GUID；client 只接受主机收据。
//  * Load 期间（TryPopObjectsToScene 作用域或 poppingObjectsToScene）禁止分配新 GUID。
//  * 作用域 = 稳定上下文（存档文件名 + campaign/challenge + land）下的不透明 epoch；绝不使用
//    realStartDateTime/NetID/instanceID 之类每次读取都会重建的运行时值。
//  * 恢复严格性：只有精确快照（kind1 全量 hash 或 kind2 时钟无关指纹，且同 scope）才允许
//    uniqueID→收据；解析不出证明（冲突 / 已知历史对不上 / 仍有未归属历史）一律 unresolved：
//    不恢复、不写、不种，sidecar 原样保留 —— 绝不把历史人群当新档重种。
//  * 吸收态自愈（B）例外：精确 known-mismatch 会话的 save 可凭严格历史子集证据门新建 epoch 基线；
//    携带身份只在本次 save 有同 life live owner 证据时才重绑，证据缺口一律维持 unresolved（写保护），
//    绝不靠清全局标志放行会静默丢人的部分快照。
//  * 风格面板例外（功能 A 与设计 C，仅主机/单机、仅用户动作或既有 5s 巡检触发）：
//    功能 A（首见均匀分配）只作用于已解析上下文里的零记录装载骑士，由既有 5s IntegrityPass 在
//    PrimeExisting 之前批量执行（取代逐人哈希迁移）；unresolved/失配/冲突绝不自动分配。
//    设计 C（用户锚定再基线化）由面板"应用"在 unresolved 上下文上先只铸运行时收据并挂 pending，
//    epoch/快照写入严格发生在下一次原生 Save 的捕获作用域内（save 形态 JSON，不是 apply 时点的
//    装载形态）；旧历史全保留，证据缺口一律 pending 保持、不写（未保存退出=零写入）。
//    二者都绝不在 save 路径内铸造收据、绝不改既有匹配/冲突不变量。
//  * 本次 save 的捕获证据必须自洽：同一 uniqueID 只有「同 owner 同 life」的重复 GetID 才幂等，
//    不同 owner/life、盘记录重复或证据容量截断都算缺口（两条写路径一律 fail-closed，绝不静默放行）。
//  * life 由进程全局单调计数器分配，删除/重建/池复用都不重用；OnEnable 每次都是新 life 并清旧收据。
//  * state 有界（MaxTrackedKnights）：满时只回收已销毁/异 world/无收据条目，绝不牺牲活收据。
//  * world 归属必须实测（同 scene 且在 world.gameLayer 下）；无法验证一律延后，绝不 fallback 到旧 world。
//
// 补丁次序：Priority.First = 800 先跑、Priority.Normal = 400、Priority.Last = 0 最后
// （0Harmony 2.10.2 实测 PriorityComparer 按数值降序发射，HarmonyManipulator 正序 foreach）。
// 因此本模块 Save 后缀用 Priority.Last 确保在所有原生 JSON 扩展（如 Hermes 默认后缀）之后捕获。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod
{
    /// <summary>模块内统一的一次性日志门限（key 集合有界；日志不可用绝不影响 gameplay）。</summary>
    internal static class KnightIdentityLog
    {
        private const int MaxKeys = 64;
        private static readonly HashSet<string> OnceKeys = new HashSet<string>(StringComparer.Ordinal);

        internal static void Once(string key, Exception exception)
        {
            if (!TryAddKey(key)) return;
            Warning(key + (exception != null ? ": " + exception.Message : string.Empty));
        }

        /// <summary>一次性 Info（与 Once 共用有界 key 预算；"info:" 前缀避免与警告 key 冲突）。</summary>
        internal static void InfoOnce(string key, string message)
        {
            if (!TryAddKey("info:" + key)) return;
            Info(message);
        }

        /// <summary>事件级简洁回执（每次成功 save / load 命中各一条，绝不逐帧）。</summary>
        internal static void Receipt(string message)
        {
            Info(message);
        }

        private static bool TryAddKey(string key)
        {
            if (OnceKeys.Count >= MaxKeys && !OnceKeys.Contains(key)) return false; // 动态 key 不撑爆内存
            return OnceKeys.Add(key);
        }

        private static void Info(string message)
        {
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[KnightIdentity] " + message);
            }
            catch
            {
                // 日志不可用不影响 gameplay
            }
        }

        private static void Warning(string message)
        {
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[KnightIdentity] " + message);
            }
            catch
            {
                // 日志不可用不影响 gameplay
            }
        }

        internal static void ResetForTests()
        {
            OnceKeys.Clear();
        }
    }

    /// <summary>
    /// 骑士风格面板（KnightStylePanel）与面板身份路径的事件日志：前缀 [KnightPanel]（与 [KnightIdentity]
    /// 分开，便于玩家/操作者按功能归因）。只记录用户动作或一次性判定（首见批量分配、用户锚定再基线化、
    /// 门槛缺口），绝不逐帧；日志失败绝不影响 gameplay。定义在本文件是为了保持既有身份测试套件的
    /// 编译面（该套件同样编译本文件，不编译 UI 文件）。
    /// </summary>
    internal static class KnightPanelLog
    {
        private const int MaxKeys = 16;
        private static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);

        internal static void Info(string message)
        {
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[KnightPanel] " + message);
            }
            catch
            {
                // 日志不可用不影响 gameplay
            }
        }

        internal static void WarnOnce(string key, string message)
        {
            if (WarnedKeys.Count >= MaxKeys && !WarnedKeys.Contains(key)) return;
            if (!WarnedKeys.Add(key)) return;
            try
            {
                KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[KnightPanel] " + message);
            }
            catch
            {
            }
        }

        internal static void ResetForTests()
        {
            WarnedKeys.Clear();
        }
    }

    /// <summary>
    /// 骑士身份运行时：稳定 GUID + 固定 style 0..4，按 (GameObject instanceID, pointer) + 全局 life 管理。
    /// 所有入口都由既有 KnightStyle 事件/巡检路径调用，不新增 driver / 逐帧 scanner / RPC。
    /// 风格面板（KnightStylePanel）只经本文件的显式接入点读写身份：TryVerifyPanelKnight（只读探针）、
    /// TryPanelAssignAll（用户动作的一次性重派，允许 unresolved 上下文）、ArmPanelRebaseline（设计 C
    /// pending，写入严格推迟到下一次原生 Save 的捕获作用域）、AssignFirstSeenUniform（功能 A 首见批量均匀）。
    /// </summary>
    internal static class KnightIdentityRuntime
    {
        /// <summary>状态上限：超过后不再新增条目，只回收已死/异世界/无收据条目。</summary>
        internal const int MaxTrackedKnights = 2048;

        /// <summary>inactive 且无收据的条目连续 N 次巡检后回收（同 world 换层会销毁重建对象）。</summary>
        private const int InactivePollThreshold = 3;

        private const string KnightTag = "Knight";

        private struct OwnerKey : IEquatable<OwnerKey>
        {
            internal readonly int GoId;
            internal readonly IntPtr GoPointer;

            internal OwnerKey(int goId, IntPtr goPointer)
            {
                GoId = goId;
                GoPointer = goPointer;
            }

            public bool Equals(OwnerKey other)
            {
                return GoId == other.GoId && GoPointer == other.GoPointer;
            }

            public override bool Equals(object obj)
            {
                return obj is OwnerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked((GoId * 397) ^ GoPointer.GetHashCode());
            }
        }

        private sealed class Entry
        {
            internal long LoadReceiptScope;
            internal bool FailedLoad;
            internal Knight KnightRef;
            internal IntPtr KnightPointer;
            internal long Lifetime;
            internal IntPtr World; // 只在实测属于当前 world 时写入；未验证保持旧值/零
            internal bool HasReceipt;
            internal KnightIdentityReceipt Receipt;
            internal bool MarkedNew;
            internal int InactiveStreak;
        }

        private static readonly Dictionary<OwnerKey, Entry> Entries = new Dictionary<OwnerKey, Entry>();
        private static readonly List<OwnerKey> SweepScratch = new List<OwnerKey>();
        private static readonly int[] CountScratch = new int[KnightIdentityReceipt.StyleCount];

        // 风格面板（功能 A 首见批量 / 设计 C 用户锚定再基线化）的会话缓冲与 pending 状态。
        // 只在用户动作或既有 5s 巡检内使用；容量全部有界（批次 ≤ MaxTrackedKnights）。
        private static readonly List<Entry> PanelEntries = new List<Entry>();
        private static readonly List<int> PanelStyles = new List<int>();
        private static readonly List<PanelEntryBackup> PanelBackup = new List<PanelEntryBackup>();
        // 设计 C pending 的铸出身份（ArmPanelRebaseline 登记；KnightIdentitySaveBridge 在 save 期复核消费）。
        internal static readonly List<PanelMint> PanelMints = new List<PanelMint>();
        private static string _panelRebaselineContext; // 非 null = 设计 C pending 已挂（上下文键）

        /// <summary>设计 C pending 里的一条面板铸出身份（save 期按同对象同 life 复核存活性）。</summary>
        internal sealed class PanelMint
        {
            internal Knight Knight;
            internal long Lifetime;
            internal KnightIdentityReceipt Receipt;
        }

        /// <summary>TryPanelAssignAll 的回滚快照（绝不半改）。</summary>
        private sealed class PanelEntryBackup
        {
            internal Entry Entry;
            internal bool HasReceipt;
            internal KnightIdentityReceipt Receipt;
            internal bool MarkedNew;
            internal bool FailedLoad;
        }

        private static long _lifetimeCounter; // 进程全局单调：删除/重建/池复用都不重用 life
        private static long _activeLoadScopeId; // 当前 Load 作用域（0 = 无）
        private static bool _priming;

        // ------------------------------------------------------------------ 固定 API

        /// <summary>
        /// Knight.OnEnable：每次都是新 life（全局单调）并清掉旧收据。**所有 Knight 组件（含 Squire）都登记**，
        /// 因为晋升后的对象需要「旧 life 判据」；收据仍然只属于 tag Knight。不发 RPC、不看 ModConfig。
        /// </summary>
        internal static void OnEnable(Knight knight)
        {
            try
            {
                if (!TryGetGameObject(knight, out GameObject go)) return;

                OwnerKey key = MakeKey(go);
                Entry entry = FindEntry(key, knight);
                if (entry == null) entry = TryCreateEntry(key, knight);
                if (entry == null) return;

                entry.KnightRef = knight;
                entry.KnightPointer = SafePointer(knight);
                entry.Lifetime = NextLifetime();
                entry.HasReceipt = false;
                entry.Receipt = default;
                entry.MarkedNew = false;
                entry.InactiveStreak = 0;
                entry.LoadReceiptScope = _activeLoadScopeId; // fresh owner也属于本次Load事务
                entry.FailedLoad = false;
                TouchWorld(entry, knight);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("onenable", e);
            }
        }

        /// <summary>只标本 life 的新招募；重复调用幂等，绝不重建 GUID、不改 style、不清已有收据。</summary>
        internal static void MarkPromoted(Knight knight)
        {
            try
            {
                if (!TryGetGameObject(knight, out GameObject go)) return;
                if (!IsKnightTag(go)) return; // 晋升结果是 Knight 预制体实例（tag Knight），Squire 不登记为招募
                if (!IsActiveGameObject(go)) return;

                OwnerKey key = MakeKey(go);
                Entry entry = FindEntry(key, knight);
                if (entry == null) entry = TryCreateEntry(key, knight);
                if (entry == null) return;

                entry.KnightRef = knight;
                entry.KnightPointer = SafePointer(knight);
                entry.MarkedNew = true; // 幂等：只置标记，不动收据/life/style
                TouchWorld(entry, knight);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("promoted", e);
            }
        }

        /// <summary>
        /// 解析/冻结身份。优先级：已有收据（固定 style，且必须仍在 available 内）→ 本 life 新招募（均衡择最少）
        /// → 已有 live style（冻结）→ 旧档一次性 migrationHash 迁移。只活跃、实测属于当前 world 的 tagKnight；
        /// 只有主机能创建 GUID；任何不满足都返回 false 等待，绝不重抽、绝不重映射。
        /// </summary>
        internal static bool TryResolve(Knight knight, int existingStyle, uint migrationHash, IReadOnlyList<int> available, out int style)
        {
            style = -1;
            try
            {
                if (_priming) return false; // PrimeExisting 的 getLegacyStyle 不得递归进来
                if (!TryGetVerifiedEntry(knight, out Entry entry, out IntPtr world)) return false;

                if (entry.HasReceipt)
                {
                    style = entry.Receipt.Style;
                    // 冻结风格不在可用池内 = 对应资产缺失：等待，不重抽
                    return available != null && Contains(available, style);
                }

                if (!TryIsHost(out bool host) || !host) return false; // client 永不创建 GUID
                if (InLoadContext()) return false;                    // Load 期间禁止分配
                if (_contextUnresolved || entry.FailedLoad) return false; // 加载结束不等于历史身份已确认
                if (available == null || available.Count == 0) return false; // 资产缺失：等待

                int chosen;
                if (entry.MarkedNew)
                {
                    chosen = ChooseBalanced(entry, world, available);
                }
                else if (existingStyle >= 0)
                {
                    if (!IsValidStyle(existingStyle) || !Contains(available, existingStyle)) return false; // 冻结等待
                    chosen = existingStyle;
                }
                else
                {
                    chosen = MigrateByHash(migrationHash, available);
                }
                if (!IsValidStyle(chosen)) return false;

                entry.HasReceipt = true;
                entry.Receipt = new KnightIdentityReceipt(Guid.NewGuid(), chosen); // 私有熵：不用 Unity random
                entry.MarkedNew = false; // 立即记入 state：同批后续招募会看到本次选择
                entry.World = world;
                style = chosen;
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("resolve", e);
                style = -1;
                return false;
            }
        }

        /// <summary>
        /// 现有 5s IntegrityPass 开头 / 新招募平衡前调用：先恢复（已有收据）或迁移（legacy 风格）未决旧骑士，
        /// 排除 MarkedNew，使新招募计数不漏掉加载未决人员。只用传入的既有缓存，不新增全场扫描。
        /// getLegacyStyle 由调用方包装原 hash/现有 style，不得递归 TryResolve。
        /// </summary>
        internal static void PrimeExisting(Knight[] currentKnights, Func<Knight, int> getLegacyStyle)
        {
            if (currentKnights == null || _priming) return;
            _priming = true;
            try
            {
                bool canPin = TryIsHost(out bool host) && host && !InLoadContext() && !_contextUnresolved;
                for (int i = 0; i < currentKnights.Length; i++)
                {
                    Knight knight = currentKnights[i];
                    if (knight == null) continue;
                    if (!TryGetVerifiedEntry(knight, out Entry entry, out IntPtr world)) continue;
                    if (entry.MarkedNew) continue; // 新招募不在此处理
                    if (entry.HasReceipt)
                    {
                        entry.World = world; // 恢复：收据已权威，仅确认 world 归属
                        continue;
                    }

                    if (!canPin || entry.FailedLoad) continue; // 不调用可能产生迁移风格的回调

                    int legacy = -1;
                    try
                    {
                        if (getLegacyStyle != null) legacy = getLegacyStyle(knight);
                    }
                    catch (Exception e)
                    {
                        KnightIdentityLog.Once("priming-legacy", e);
                    }
                    if (!IsValidStyle(legacy)) continue;
                    if (!canPin) continue; // client / Load：等主机收据

                    entry.HasReceipt = true;
                    entry.Receipt = new KnightIdentityReceipt(Guid.NewGuid(), legacy);
                    entry.World = world;
                }
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("priming", e);
            }
            finally
            {
                _priming = false;
            }
        }

        /// <summary>已有收据（固定 style）；没有返回 false。</summary>
        internal static bool TryGetReceipt(Knight knight, out KnightIdentityReceipt receipt)
        {
            receipt = default;
            try
            {
                if (!TryGetTrackedEntry(knight, out Entry entry) || !entry.HasReceipt) return false;
                receipt = entry.Receipt;
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("get-receipt", e);
                return false;
            }
        }

        /// <summary>进程全局单调 life：删除/重建/池复用都不会给出同一个值。未跟踪返回 0。</summary>
        internal static long GetLifetime(Knight knight)
        {
            try
            {
                return TryGetTrackedEntry(knight, out Entry entry) ? entry.Lifetime : 0;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("get-lifetime", e);
                return 0;
            }
        }

        /// <summary>
        /// 仅 client 调用：主机收据只在「本机确实是 client、对象是活跃的 tagKnight、实测属于当前 world、
        /// 指针/组件精确匹配、life 一致」时接收；重复幂等；本 life 已有不同 Guid 不覆写。不读写 client sidecar。
        /// </summary>
        internal static bool ApplyHostReceipt(Knight knight, long expectedLifetime, KnightIdentityReceipt receipt)
        {
            try
            {
                if (expectedLifetime <= 0 || !receipt.IsValid) return false;
                if (!TryIsHost(out bool host) || host) return false;                  // 主机不接受 client 判定
                if (!TryGetVerifiedEntry(knight, out Entry entry, out IntPtr world)) return false;
                if (entry.Lifetime != expectedLifetime) return false;                 // 不是本 life 的收据

                if (entry.HasReceipt) return entry.Receipt.Equals(receipt);           // 幂等；不同 Guid 不覆写

                entry.HasReceipt = true;
                entry.Receipt = receipt;
                entry.MarkedNew = false;
                entry.World = world;
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("apply-host-receipt", e);
                return false;
            }
        }

        /// <summary>
        /// 现有 KnightStyle IntegrityPass 调用：清理已销毁 / 实测异 world / 长期 inactive 且无收据的状态。
        /// 当前 world 无法解析时延后（绝不 fallback、绝不串 world）；Load 作用域内不清理。无逐帧 scanner。
        /// </summary>
        internal static void Poll()
        {
            try
            {
                if (InLoadContext()) return;
                if (!TryGetWorldContext(out World world, out GameObject worldGo)) return;

                SweepScratch.Clear();
                foreach (KeyValuePair<OwnerKey, Entry> pair in Entries)
                {
                    Entry entry = pair.Value;
                    if (IsDestroyed(entry))
                    {
                        SweepScratch.Add(pair.Key);
                        continue;
                    }
                    bool active = IsActive(entry);
                    bool inWorld = IsUnitInWorld(entry.KnightRef, world, worldGo, strict: true);
                    if (inWorld)
                    {
                        entry.World = SafePointer(world);
                    }
                    else if (entry.World != IntPtr.Zero && entry.World != SafePointer(world))
                    {
                        SweepScratch.Add(pair.Key); // 实测别的 world
                        continue;
                    }
                    if (active) entry.InactiveStreak = 0;
                    else
                    {
                        entry.InactiveStreak++;
                        if (entry.InactiveStreak >= InactivePollThreshold && !entry.HasReceipt) SweepScratch.Add(pair.Key);
                    }
                }
                for (int i = 0; i < SweepScratch.Count; i++) Entries.Remove(SweepScratch[i]);
                SweepScratch.Clear();
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("poll", e);
            }
        }

        // ------------------------------------------------------------------ 存档桥接入口（非网络 API）

        /// <summary>TryPopObjectsToScene 期间绑定实际 Persistent root 上的收据：tag/场景/指针核对，重复不重抽。</summary>
        internal static bool BindLoadedReceipt(Knight knight, KnightIdentityReceipt receipt)
        {
            try
            {
                if (!receipt.IsValid) return false;
                if (!TryGetGameObject(knight, out GameObject go)) return false;
                if (!IsKnightTag(go)) return false;
                if (!TryIsHost(out bool host) || !host) return false;
                // 绑定时层级可能尚未挂到 world.gameLayer：只要求与当前 world 同 scene（TryResolve 才要求全严格）
                if (!IsInCurrentScene(go)) return false;

                OwnerKey key = MakeKey(go);
                Entry entry = FindEntry(key, knight);
                if (entry == null) entry = TryCreateEntry(key, knight);
                if (entry == null) return false;

                if (entry.HasReceipt)
                {
                    if (entry.Receipt.Equals(receipt)) return true; // 重复绑定：幂等
                    KnightIdentityLog.Once("load-duplicate-receipt", null); // 本 life 已有不同收据：保留先到的
                    return false;
                }

                entry.KnightRef = knight;
                entry.KnightPointer = SafePointer(knight);
                entry.HasReceipt = true;
                entry.Receipt = receipt;
                entry.MarkedNew = false;
                entry.LoadReceiptScope = _activeLoadScopeId;
                TouchWorld(entry, knight);
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("bind-loaded", e);
                return false;
            }
        }

        internal static void EnterLoadScope(long scopeId)
        {
            _activeLoadScopeId = scopeId;
        }

        /// <summary>
        /// 再基线化重绑定：把历史携带的收据绑到本次 save 捕获的 live owner。严格复核「仍是同 life、tagKnight、
        /// 活跃、条目还是原来那个对象」；已有不同收据绝不覆写（返回 false 由调用方按缺口处理）。不新建条目。
        /// </summary>
        internal static bool TryBindCarriedReceipt(Knight knight, long expectedLifetime, KnightIdentityReceipt receipt)
        {
            try
            {
                if (expectedLifetime <= 0 || !receipt.IsValid) return false;
                if (!TryGetGameObject(knight, out GameObject go)) return false;
                if (!IsKnightTag(go) || !IsActiveGameObject(go)) return false;

                Entry entry = FindEntry(MakeKey(go), knight);
                if (entry == null || entry.Lifetime != expectedLifetime) return false; // 未被跟踪 / 已换 life：没有证据
                if (entry.HasReceipt) return entry.Receipt.Equals(receipt);            // 同收据幂等；不同绝不覆写

                entry.HasReceipt = true;
                entry.Receipt = receipt;
                entry.MarkedNew = false;
                entry.FailedLoad = false; // 身份已由本次 save 证据证实，旧的失败装载标记作废
                TouchWorld(entry, knight);
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("rebaseline-bind", e);
                return false;
            }
        }

        /// <summary>
        /// 再基线化里被丢弃（历史不一致 / 同 GUID 输者）的 live owner：仅清 FailedLoad 交给既有 PrimeExisting/5s
        /// 路径重铸；已有收据、换 life、未被跟踪一律不动，绝不在这里铸造收据。
        /// </summary>
        internal static bool TryClearFailedLoad(Knight knight, long expectedLifetime)
        {
            try
            {
                if (expectedLifetime <= 0) return false;
                if (!TryGetGameObject(knight, out GameObject go)) return false;
                if (!IsKnightTag(go) || !IsActiveGameObject(go)) return false;

                Entry entry = FindEntry(MakeKey(go), knight);
                if (entry == null || entry.Lifetime != expectedLifetime || entry.HasReceipt) return false;
                entry.FailedLoad = false;
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("rebaseline-clear", e);
                return false;
            }
        }

        // ------------------------------------------------------------------ 风格面板接入（KnightStylePanel）

        /// <summary>
        /// 功能 A（首见均匀分配）：把当前 world 内「零记录装载骑士」作为一批一次均匀分配到可用风格池
        /// （每风格数量差 ≤1，余数按私有熵随机派发），并立即写入运行时收据（持久化仍走既有 Save 桥）。
        /// 由既有 5s IntegrityPass 在 PrimeExisting 之前调用：本批次先于任何哈希迁移拿到收据，
        /// PrimeExisting 只保留「本会话已生效风格冻结」职责（首见哈希迁移由本批次取代）。
        /// 只处理 tagKnight、实测属于当前 world、无收据、非 MarkedNew、非 FailedLoad 且无在场景格的条目；
        /// 主机 authority / 非 Load 上下文 / 上下文已解析才有资格（unresolved/失配/冲突绝不自动分配，
        /// 只能经面板 + 设计 C）。hasLiveStyle 是风格模块提供的只读探针：已在场的风格交给既有冻结路径，
        /// 不在本批重摇。返回本批分配人数（0 = 无可分配骑士或门不满足，未做任何写入）。
        /// </summary>
        internal static int AssignFirstSeenUniform(Knight[] currentKnights, IReadOnlyList<int> available, Func<Knight, bool> hasLiveStyle)
        {
            if (currentKnights == null || currentKnights.Length == 0) return 0;
            if (!IsUsableStylePool(available)) return 0;
            if (_priming) return 0; // 批次执行期间拒绝重入分配（回调不得递归 TryResolve）
            if (!TryIsHost(out bool host) || !host) return 0;
            if (InLoadContext() || _contextUnresolved) return 0;

            _priming = true;
            try
            {
                PanelEntries.Clear();
                for (int i = 0; i < currentKnights.Length; i++)
                {
                    Knight knight = currentKnights[i];
                    if (knight == null) continue;
                    if (!TryGetVerifiedEntry(knight, out Entry entry, out _)) continue;
                    if (entry.HasReceipt || entry.MarkedNew || entry.FailedLoad) continue;
                    bool live = false;
                    try { live = hasLiveStyle != null && hasLiveStyle(knight); }
                    catch (Exception e) { KnightIdentityLog.Once("first-seen-live", e); continue; }
                    if (live) continue; // 已在场风格：PrimeExisting 的冻结路径负责
                    PanelEntries.Add(entry);
                }
                if (PanelEntries.Count == 0) return 0;

                for (int i = 0; i < CountScratch.Length; i++) CountScratch[i] = 0;
                for (int i = 0; i < PanelEntries.Count; i++)
                {
                    Entry entry = PanelEntries[i];
                    int style = KnightIdentityBalance.ChooseLeast(CountScratch, available, NextPanelEntropy());
                    if (!IsValidStyle(style)) continue; // ChooseLeast 保证在池内；防御性
                    CountScratch[style]++;
                    entry.Receipt = new KnightIdentityReceipt(Guid.NewGuid(), style);
                    entry.HasReceipt = true;
                    entry.MarkedNew = false;
                }

                int total = 0;
                StringBuilder counts = new StringBuilder(16);
                for (int i = 0; i < CountScratch.Length; i++)
                {
                    total += CountScratch[i];
                    if (i > 0) counts.Append('/');
                    counts.Append(CountScratch[i].ToString(CultureInfo.InvariantCulture));
                }
                if (total == 0) return 0;
                KnightPanelLog.Info("first-seen auto-assigned " + total.ToString(CultureInfo.InvariantCulture)
                    + " knights (" + counts + ")");
                return total;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("first-seen", e);
                return 0;
            }
            finally
            {
                PanelEntries.Clear();
                _priming = false;
            }
        }

        /// <summary>面板门槛探针（只读）：该骑士是否已实测属于当前 world 的活跃 tagKnight（可被面板读写身份）。</summary>
        internal static bool TryVerifyPanelKnight(Knight knight)
        {
            try { return TryGetVerifiedEntry(knight, out _, out _); }
            catch (Exception e) { KnightIdentityLog.Once("panel-verify", e); return false; }
        }

        /// <summary>
        /// 面板一次性重派（用户显式动作；唯一允许在 unresolved 上下文上改身份的入口——自动路径绝不调用）：
        /// 先全量验证（任何一名不在场/读不到/重复 → 整体拒绝），再就地重写/铸造收据；
        /// 任何意外整体回滚（绝不半改）。已有收据保留 GUID 只改 style（历史留同 GUID 异 style 记录）；
        /// 零记录才铸新 GUID；FailedLoad/MarkedNew 一并清除（身份已由用户动作确认）。
        /// 绝不触碰 sidecar/原生存档：持久化由既有 Save 桥完成（应用后未保存退出=丢弃）。
        /// </summary>
        internal static bool TryPanelAssignAll(Knight[] knights, int[] styles, out int assigned)
        {
            assigned = 0;
            if (knights == null || styles == null || knights.Length == 0 || knights.Length != styles.Length) return false;
            try
            {
                if (!IsHostAuthority()) return false;
                if (InLoadContext()) return false;

                PanelEntries.Clear();
                PanelStyles.Clear();
                HashSet<Entry> seen = new HashSet<Entry>();
                for (int i = 0; i < knights.Length; i++)
                {
                    if (knights[i] == null || !IsValidStyle(styles[i])) return false;
                    if (!TryGetVerifiedEntry(knights[i], out Entry entry, out _)) return false;
                    if (!seen.Add(entry)) return false; // 同一对象重复进入批次：fail-closed
                    PanelEntries.Add(entry);
                    PanelStyles.Add(styles[i]);
                }

                PanelBackup.Clear();
                for (int i = 0; i < PanelEntries.Count; i++)
                {
                    Entry entry = PanelEntries[i];
                    PanelBackup.Add(new PanelEntryBackup
                    {
                        Entry = entry,
                        HasReceipt = entry.HasReceipt,
                        Receipt = entry.Receipt,
                        MarkedNew = entry.MarkedNew,
                        FailedLoad = entry.FailedLoad,
                    });
                }

                for (int i = 0; i < PanelEntries.Count; i++)
                {
                    Entry entry = PanelEntries[i];
                    int style = PanelStyles[i];
                    entry.Receipt = entry.HasReceipt && entry.Receipt.IsValid
                        ? new KnightIdentityReceipt(entry.Receipt.Id, style) // GUID 保留：历史留同 GUID 异 style 记录
                        : new KnightIdentityReceipt(Guid.NewGuid(), style);  // 零记录：铸新 GUID
                    entry.HasReceipt = true;
                    entry.MarkedNew = false;
                    entry.FailedLoad = false;
                    assigned++;
                }
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("panel-assign", e);
                for (int i = 0; i < PanelBackup.Count; i++)
                {
                    PanelEntryBackup backup = PanelBackup[i];
                    try
                    {
                        backup.Entry.Receipt = backup.Receipt;
                        backup.Entry.HasReceipt = backup.HasReceipt;
                        backup.Entry.MarkedNew = backup.MarkedNew;
                        backup.Entry.FailedLoad = backup.FailedLoad;
                    }
                    catch
                    {
                        // 回滚尽力而为：条目本身已不可读时保持 fail-closed
                    }
                }
                assigned = 0;
                return false;
            }
            finally
            {
                PanelEntries.Clear();
                PanelStyles.Clear();
                PanelBackup.Clear();
            }
        }

        /// <summary>设计 C pending 已挂的上下文键（null = 未挂）。save 期进入用户锚定再基线化路径的开关。</summary>
        internal static string PanelRebaselineContext { get { return _panelRebaselineContext; } }

        /// <summary>面板/测试读的便捷视图：设计 C pending 是否已挂。</summary>
        internal static bool PanelRebaselineArmed { get { return _panelRebaselineContext != null; } }

        /// <summary>
        /// 面板在 unresolved 上下文上的应用明确授权再基线化：把当前 world 内全部实测归属、持收据的
        /// live owner 登记为本次用户锚定集合（= 面板刚铸/改的身份）。写入严格推迟到下一次原生 Save 的
        /// 捕获作用域（save 形态 JSON）；失败=pending 保持，未保存退出=零写入。
        /// 只在面板应用成功后调用（用户显式动作），自动路径绝不调用。
        /// </summary>
        internal static void ArmPanelRebaseline(string contextKey)
        {
            try
            {
                if (string.IsNullOrEmpty(contextKey)) return;
                if (!TryGetWorldContext(out World world, out _)) return;
                IntPtr worldPointer = SafePointer(world);
                if (worldPointer == IntPtr.Zero) return;

                PanelMints.Clear();
                foreach (KeyValuePair<OwnerKey, Entry> pair in Entries)
                {
                    Entry entry = pair.Value;
                    if (!entry.HasReceipt || entry.KnightRef == null) continue;
                    if (entry.World != worldPointer) continue; // 只收实测归属当前 world 的条目
                    PanelMints.Add(new PanelMint { Knight = entry.KnightRef, Lifetime = entry.Lifetime, Receipt = entry.Receipt });
                }
                if (PanelMints.Count == 0) return;
                _panelRebaselineContext = contextKey;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("panel-arm", e);
            }
        }

        /// <summary>撤销设计 C pending（成功写入 / 已失效 / 测试复位）。</summary>
        internal static void DisarmPanelRebaseline()
        {
            _panelRebaselineContext = null;
            PanelMints.Clear();
        }

        /// <summary>池可用性预检（ChooseLeast 的输入约束）：非空、全合法、无重复；失败=整批不写。</summary>
        private static bool IsUsableStylePool(IReadOnlyList<int> available)
        {
            if (available == null || available.Count == 0) return false;
            for (int i = 0; i < available.Count; i++)
            {
                if (!IsValidStyle(available[i])) return false;
                for (int j = 0; j < i; j++) if (available[j] == available[i]) return false;
            }
            return true;
        }

        /// <summary>私有熵（Guid 前 4 字节）：与 ChooseBalanced 同源，绝不用 Unity random。面板重派与首见批次共用。</summary>
        internal static uint NextPanelEntropy()
        {
            Guid entropySource = Guid.NewGuid();
            Span<byte> buffer = stackalloc byte[16];
            entropySource.TryWriteBytes(buffer);
            return (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
        }

        private static bool _contextUnresolved;
        internal static bool CanFlushSeed { get { return !_contextUnresolved && !InLoadContext(); } }

        internal static void ConfirmContext(bool unresolved) { _contextUnresolved = unresolved; }

        internal static void FinishLoadedReceipts(long scopeId, bool succeeded, long previousScopeId)
        {
            foreach (Entry entry in Entries.Values)
            {
                if (entry.LoadReceiptScope != scopeId) continue;
                entry.LoadReceiptScope = succeeded ? previousScopeId : 0;
                if (succeeded) continue;
                entry.HasReceipt = false;
                entry.Receipt = default;
                entry.MarkedNew = false;
                entry.FailedLoad = true; // 失败 load 的新 owner 不得污染此前 world/save
            }
        }

        internal static void ExitLoadScope(long scopeId, long previousScopeId)
        {
            if (_activeLoadScopeId == scopeId) _activeLoadScopeId = previousScopeId;
        }

        /// <summary>本次快照可用的骑士身份：同 life 且持有收据（Save 快照条目只认这些 owner）。</summary>
        internal static bool TryGetTrackedIdentity(Knight knight, out long lifetime, out KnightIdentityReceipt receipt)
        {
            lifetime = 0;
            receipt = default;
            if (!TryGetTrackedEntry(knight, out Entry entry) || !entry.HasReceipt) return false;
            lifetime = entry.Lifetime;
            receipt = entry.Receipt;
            return true;
        }

        internal static int TrackedCount
        {
            get { return Entries.Count; }
        }

        internal static int ReceiptCount
        {
            get
            {
                int count = 0;
                foreach (Entry entry in Entries.Values) if (entry.HasReceipt) count++;
                return count;
            }
        }

        /// <summary>仅测试使用：清空全部内存状态（含全局 life 计数）。</summary>
        internal static void ResetForTests()
        {
            KnightIdentityLoadSeed.Clear();
            KnightIdentityContexts.ResetForTests();
            Entries.Clear();
            SweepScratch.Clear();
            KnightIdentityLog.ResetForTests();
            KnightPanelLog.ResetForTests();
            _lifetimeCounter = 0;
            _activeLoadScopeId = 0;
            _contextUnresolved = false;
            KnightIdentityGeneration.ResetForTests();
            _priming = false;
            PanelEntries.Clear();
            PanelStyles.Clear();
            PanelBackup.Clear();
            DisarmPanelRebaseline();
        }

        // ------------------------------------------------------------------ 内部

        private static long NextLifetime()
        {
            return ++_lifetimeCounter;
        }

        private static int ChooseBalanced(Entry self, IntPtr world, IReadOnlyList<int> available)
        {
            for (int i = 0; i < CountScratch.Length; i++) CountScratch[i] = 0;

            bool haveWorld = TryGetWorldContext(out World liveWorld, out GameObject worldGo);
            foreach (KeyValuePair<OwnerKey, Entry> pair in Entries)
            {
                Entry other = pair.Value;
                if (ReferenceEquals(other, self)) continue;
                if (!other.HasReceipt) continue;
                if (other.World != world) continue; // 只统计当前 world（world 值只在实测后写入）
                if (!haveWorld || !IsLivingKnight(other.KnightRef)
                    || !IsUnitInWorld(other.KnightRef, liveWorld, worldGo, strict: true)) continue; // 现场过滤，不吃缓存
                int style = other.Receipt.Style;
                if (!IsValidStyle(style)) continue;
                CountScratch[style]++;
            }

            Guid entropySource = Guid.NewGuid(); // 私有 Guid 熵，兼作 tie-break
            Span<byte> buffer = stackalloc byte[16];
            entropySource.TryWriteBytes(buffer);
            uint entropy = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
            return KnightIdentityBalance.ChooseLeast(CountScratch, available, entropy);
        }

        private static int MigrateByHash(uint migrationHash, IReadOnlyList<int> available)
        {
            if (available == null || available.Count == 0) return -1;
            int style = available[(int)(migrationHash % (uint)available.Count)];
            return IsValidStyle(style) ? style : -1;
        }

        private static bool IsValidStyle(int style)
        {
            return KnightIdentityReceipt.IsValidStyle(style);
        }

        private static bool Contains(IReadOnlyList<int> values, int value)
        {
            for (int i = 0; i < values.Count; i++) if (values[i] == value) return true;
            return false;
        }

        private static OwnerKey MakeKey(GameObject go)
        {
            return new OwnerKey(SafeInstanceId(go), SafePointer(go));
        }

        private static Entry FindEntry(OwnerKey key, Knight knight)
        {
            if (!Entries.TryGetValue(key, out Entry entry)) return null;
            // 键一致之外还要指针一致：instanceID 复用给别的对象时旧条目作废
            if (IsDestroyed(entry) || entry.KnightPointer != SafePointer(knight))
            {
                Entries.Remove(key);
                return null;
            }
            return entry;
        }

        private static bool TryGetTrackedEntry(Knight knight, out Entry entry)
        {
            entry = null;
            if (!TryGetGameObject(knight, out GameObject go)) return false;
            entry = FindEntry(MakeKey(go), knight);
            return entry != null;
        }

        /// <summary>严格准入门槛：活跃、tagKnight、实测当前 world（同 scene 且在 gameLayer 下）。</summary>
        private static bool TryGetVerifiedEntry(Knight knight, out Entry entry, out IntPtr worldPointer)
        {
            entry = null;
            worldPointer = IntPtr.Zero;
            if (!IsLivingKnight(knight)) return false;
            if (!TryGetGameObject(knight, out GameObject go)) return false;
            if (!IsKnightTag(go) || !IsActiveGameObject(go)) return false;
            if (!TryGetWorldContext(out World world, out GameObject worldGo)) return false;
            if (!IsUnitInWorld(knight, world, worldGo, strict: true)) return false;

            OwnerKey key = MakeKey(go);
            Entry found = FindEntry(key, knight);
            if (found == null)
            {
                found = TryCreateEntry(key, knight);
                if (found == null) return false;
            }
            found.KnightRef = knight;
            found.KnightPointer = SafePointer(knight);
            entry = found;
            worldPointer = SafePointer(world);
            entry.World = worldPointer;
            return true;
        }

        private static Entry TryCreateEntry(OwnerKey key, Knight knight)
        {
            if (Entries.Count >= MaxTrackedKnights && !EvictForCapacity())
            {
                KnightIdentityLog.Once("capacity", null); // 满：不瞎分配，也不牺牲活收据
                return null;
            }

            DropRecycledKey(key); // 同 GOID 不同指针：instanceID 被复用，旧世代作废

            Entry entry = new Entry
            {
                KnightRef = knight,
                KnightPointer = SafePointer(knight),
                Lifetime = NextLifetime(),
                LoadReceiptScope = _activeLoadScopeId,
            };
            Entries[key] = entry;
            return entry;
        }

        /// <summary>同一 GameObject instanceID 被 Unity 复用给新对象：旧条目（不同指针）作废。</summary>
        private static void DropRecycledKey(OwnerKey key)
        {
            SweepScratch.Clear();
            foreach (KeyValuePair<OwnerKey, Entry> pair in Entries)
            {
                if (pair.Key.GoId == key.GoId && pair.Key.GoPointer != key.GoPointer) SweepScratch.Add(pair.Key);
            }
            for (int i = 0; i < SweepScratch.Count; i++) Entries.Remove(SweepScratch[i]);
            SweepScratch.Clear();
        }

        private static bool EvictForCapacity()
        {
            IntPtr world = IntPtr.Zero;
            bool haveWorld = TryGetWorldContext(out World liveWorld, out GameObject worldGo);
            if (haveWorld) world = SafePointer(liveWorld);

            SweepScratch.Clear();
            foreach (KeyValuePair<OwnerKey, Entry> pair in Entries)
            {
                Entry entry = pair.Value;
                if (IsDestroyed(entry)) { SweepScratch.Add(pair.Key); continue; }
                if (entry.World != IntPtr.Zero && world != IntPtr.Zero && entry.World != world) { SweepScratch.Add(pair.Key); continue; }
                if (!entry.HasReceipt && !IsActive(entry)) SweepScratch.Add(pair.Key);
            }
            for (int i = 0; i < SweepScratch.Count; i++) Entries.Remove(SweepScratch[i]);
            SweepScratch.Clear();
            return Entries.Count < MaxTrackedKnights;
        }

        private static bool IsDestroyed(Entry entry)
        {
            try
            {
                return entry.KnightRef == null;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsLivingKnight(Knight knight)
        {
            try
            {
                if (!TryGetGameObject(knight, out GameObject go)
                    || !IsKnightTag(go) || !IsActiveGameObject(go)) return false;
                Damageable damageable = knight._damageable;
                return damageable != null && !damageable.isDead;
            }
            catch { return false; }
        }

        private static bool IsActive(Entry entry)
        {
            try
            {
                Knight knight = entry.KnightRef;
                if (knight == null) return false;
                return IsActiveGameObject(knight.gameObject);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>实测归属：world 非 null；同 scene；严格模式还要求挂在 world.gameLayer 之下。</summary>
        private static bool IsUnitInWorld(Knight knight, World world, GameObject worldGo, bool strict)
        {
            try
            {
                if (knight == null) return false;
                GameObject go = knight.gameObject;
                if (go == null) return false;

                GameObject liveWorldGo = worldGo;
                World liveWorld = world;
                if (liveWorld == null || liveWorldGo == null)
                {
                    if (!TryGetWorldContext(out liveWorld, out liveWorldGo)) return false;
                }
                if (liveWorldGo == null || !IsSameScene(go, liveWorldGo)) return false;
                if (!strict) return true;
                Transform layer = liveWorld.gameLayer;
                if (layer == null) return false;
                Transform unitTransform = go.transform;
                return unitTransform != null && unitTransform.IsChildOf(layer);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInCurrentScene(GameObject go)
        {
            return TryGetWorldContext(out _, out GameObject worldGo) && IsSameScene(go, worldGo);
        }

        private static bool IsSameScene(GameObject a, GameObject b)
        {
            try
            {
                return a != null && b != null && a.scene.handle == b.scene.handle;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>实测 world 归属后写入：验证失败保持原值（未验证绝不认领当前 world）。</summary>
        private static void TouchWorld(Entry entry, Knight knight)
        {
            if (!TryGetWorldContext(out World world, out GameObject worldGo)) return;
            if (!IsUnitInWorld(knight, world, worldGo, strict: true)) return;
            entry.World = SafePointer(world);
        }

        // ------------------------------------------------------------------ native 薄封装

        private static bool InLoadContext()
        {
            if (_activeLoadScopeId != 0) return true;
            if (KnightIdentityGeneration.Active) return true;
            try
            {
                return IslandSaveData.poppingObjectsToScene;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("popping", e);
                return true; // 解析不了就当在加载中：延后，绝不跨上下文写状态
            }
        }

        private static bool TryGetWorldContext(out World world, out GameObject worldGo)
        {
            world = null;
            worldGo = null;
            try
            {
                Managers managers = Managers.Inst;
                world = managers != null ? managers.world : null;
                if (world == null) return false;
                worldGo = world.gameObject;
                return worldGo != null;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("world", e);
                world = null;
                worldGo = null;
                return false;
            }
        }

        private static bool TryGetGameObject(Knight knight, out GameObject go)
        {
            go = null;
            try
            {
                if (knight == null) return false;
                go = knight.gameObject;
                return go != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsKnightTag(GameObject go)
        {
            try
            {
                return go.CompareTag(KnightTag);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("tag", e);
                return false;
            }
        }

        private static bool IsActiveGameObject(GameObject go)
        {
            try
            {
                return go != null && go.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInstanceId(GameObject go)
        {
            try
            {
                return go.GetInstanceID();
            }
            catch
            {
                return 0;
            }
        }

        private static IntPtr SafePointer(UnityEngine.Object value)
        {
            try
            {
                return value == null ? IntPtr.Zero : value.Pointer;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        internal static bool TryIsHost(out bool host)
        {
            host = false;
            try
            {
                host = NetworkBigBoss.HasWorldAuth; // 离线单机为真
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("authority", e);
                return false;
            }
        }

        internal static bool IsHostAuthority()
        {
            return TryIsHost(out bool host) && host;
        }
    }

    /// <summary>
    /// 原生 Save(campaign, land, challenge) 的调用期捕获：GetID 映射本次 uniqueID→(knight owner, life, 收据)，
    /// Save 返回后（Priority.Last）用捕获岛生成完整岛 JSON 并把快照写入 sidecar。只 Save 事件 I/O。
    /// </summary>
    internal static class KnightIdentitySaveBridge
    {
        private const int MaxSnapshotEntries = KnightIdentityArchive.MaxEntriesPerSnapshot;

        internal readonly struct CapturedOwner
        {
            internal readonly Knight Knight;
            internal readonly long Lifetime;
            internal readonly KnightIdentityReceipt Receipt;

            internal CapturedOwner(Knight knight, long lifetime, KnightIdentityReceipt receipt)
            {
                Knight = knight;
                Lifetime = lifetime;
                Receipt = receipt;
            }
        }

        internal sealed class SaveCapture
        {
            internal SaveCapture Previous;
            internal IslandSaveData Island; // 只在 GetID 期间从 CurrentlySavingIsland 捕获（prefix 时为 null）
            internal int Campaign;
            internal int Land;
            internal int Challenge;
            internal readonly Dictionary<string, CapturedOwner> Owners = new Dictionary<string, CapturedOwner>(StringComparer.Ordinal);

            /// <summary>已跟踪但尚无收据的 owner（吸收态旧骑士）：只作再基线化重绑定的 life 证据，绝不进 Owners。</summary>
            internal readonly Dictionary<string, CapturedOwner> PendingOwners = new Dictionary<string, CapturedOwner>(StringComparer.Ordinal);

            /// <summary>同一次 save 内同一 uniqueID 落到不同 owner/life 的歧义 ID（有界；这类证据不可信）。</summary>
            internal readonly HashSet<string> AmbiguousIds = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>证据/歧义集合达到容量上限：截断绝不静默放行，一律当缺口。</summary>
            internal bool EvidenceOverflow;

            /// <summary>本次 save 是否存在证据缺口（歧义 ID 或容量截断）：两条写路径都必须 fail-closed。</summary>
            internal bool HasEvidenceGap
            {
                get { return EvidenceOverflow || AmbiguousIds.Count > 0; }
            }

            internal void AddEvidence(string uniqueId, CapturedOwner owner, bool pending)
            {
                Dictionary<string, CapturedOwner> target = pending ? PendingOwners : Owners;
                if (target.Count >= MaxCapturedEvidence)
                {
                    EvidenceOverflow = true; // 满：不瞎截断，整批证据作废
                    return;
                }
                target[uniqueId] = owner;
            }

            internal void MarkAmbiguous(string uniqueId)
            {
                if (AmbiguousIds.Count >= MaxCapturedEvidence)
                {
                    EvidenceOverflow = true;
                    return;
                }
                AmbiguousIds.Add(uniqueId);
            }
        }

        /// <summary>捕获证据（Owners/PendingOwners/歧义集）的上限；与快照条目上限同量级，超限一律当缺口。</summary>
        internal const int MaxCapturedEvidence = KnightIdentityArchive.MaxEntriesPerSnapshot;

        private static SaveCapture _capture;

        internal static bool HasActiveSaveCapture
        {
            get { return _capture != null; }
        }

        internal static SaveCapture BeginCapture(int campaign, int land, int challenge)
        {
            SaveCapture capture = new SaveCapture
            {
                Previous = _capture,
                Campaign = campaign,
                Land = land,
                Challenge = challenge,
            };
            _capture = capture; // prefix 只建空 scope：原生主体此时还没设 CurrentlySavingIsland
            return capture;
        }

        /// <summary>finalizer：恒定还原嵌套上下文，原样返回异常（绝不吞）。</summary>
        internal static Exception EndCapture(Exception exception, SaveCapture capture)
        {
            if (capture != null && ReferenceEquals(_capture, capture)) _capture = capture.Previous;
            return exception;
        }

        /// <summary>GetID 后缀：捕获实际正在保存的岛（唯一可靠时点），登记 knight owner 的 life 与收据快照；
        /// 无收据但已跟踪的 owner 另记 life 证据（PendingOwners），供再基线化重绑定复核；同一 uniqueID 只有
        /// 「同 owner 同 life」的重复回调才幂等，不同 owner/life 记入有界歧义集（= 证据缺口）。</summary>
        internal static void HandleGetId(Persistent forObject, string uniqueId)
        {
            try
            {
                SaveCapture capture = _capture;
                if (capture == null) return;
                if (forObject == null || string.IsNullOrEmpty(uniqueId)) return;
                if (uniqueId.Length > KnightIdentitySnapshotEntry.MaxNativeUniqueIdLength) return;

                if (capture.Island == null && !TryCaptureIsland(capture)) return;

                GameObject owner = SafeGameObject(forObject);
                if (owner == null || !SafeCompareTag(owner, "Knight")) return;

                Knight knight = SafeGetComponent<Knight>(owner);
                if (knight == null) return;

                bool hasReceipt = KnightIdentityRuntime.TryGetTrackedIdentity(knight, out long lifetime, out KnightIdentityReceipt receipt);
                if (!hasReceipt)
                {
                    // 已跟踪但无收据（吸收态旧骑士）：登记 life 证据供再基线化重绑定复核；Owners 语义与 IsValidOwner 不动。
                    lifetime = KnightIdentityRuntime.GetLifetime(knight);
                    if (lifetime <= 0)
                    {
                        // 已确认为 tagKnight 却取不到 life（如条目容量满未登记）：这条证据缺失也是缺口，
                        // 绝不静默跳过——否则第二个同 ID 的失证 owner 会被当成幂等放行。
                        capture.MarkAmbiguous(uniqueId);
                        return;
                    }
                }

                // 同一 uniqueID 重复回调：只有「同 owner 同 life」才幂等；不同 owner/life 是证据冲突，绝不保留先到者。
                if (TryGetCapturedOwner(capture, uniqueId, out CapturedOwner known))
                {
                    if (!ReferenceEquals(known.Knight, knight) || known.Lifetime != lifetime) capture.MarkAmbiguous(uniqueId);
                    return;
                }

                capture.AddEvidence(uniqueId, new CapturedOwner(knight, lifetime, hasReceipt ? receipt : default), pending: !hasReceipt);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-getid", e);
            }
        }

        /// <summary>Save 后缀（Priority.Last = 0，最后执行）：构造精确快照并合并写入 sidecar；失败绝不影响原生。</summary>
        internal static void ApplyCapture(SaveCapture capture)
        {
            try
            {
                if (capture == null || capture.Island == null) return; // 无完整可信 scope：不写
                if (KnightIdentityLoadBridge.Current != null || KnightIdentityGeneration.Active) return;
                if (!KnightIdentityRuntime.IsHostAuthority()) return; // 仅主机
                if (capture.HasEvidenceGap)
                {
                    // 缺失/歧义证据必须在进入任何写路径（包括备份恢复）之前拒绝。
                    KnightIdentityLog.Once("save-evidence-gap", null);
                    return;
                }
                // 用户锚定再基线化（设计 C）排在吸收态/CanFlushSeed 早退之前：unresolved 上下文
                // 零收据也能在本次 save 的捕获作用域内新建 epoch 基线（pending 由面板"应用"显式挂上）。
                if (TryConsumePanelRebaseline(capture)) return;
                // 吸收态自愈（B）必须排在 CanFlushSeed / Owners 早退之前：该会话无任何收据且 context unresolved。
                if (TryRebaselineAbsorbingContext(capture)) return;
                if (!KnightIdentityRuntime.CanFlushSeed) return; // context未知时连已有收据也不得写入旧epoch
                if (capture.Owners.Count == 0) return;

                IslandSaveData island = capture.Island;
                string json;
                try
                {
                    json = JsonUtility.ToJson(island, false); // 完整岛 JSON，不是只 knight 列表
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("save-json", e);
                    return;
                }
                if (string.IsNullOrEmpty(json)) return;

                int land = SafeIslandLand(island);
                if (!KnightIdentitySidecar.TryBuildContextKey(capture.Campaign, capture.Challenge, land, out string contextKey))
                {
                    KnightIdentityLog.Once("save-context", null);
                    return;
                }

                // 正常路径复用本次加载已经解析出的 epoch；没有绑定（如全新岛本会话第一次保存）才独立解析。
                if (!KnightIdentityContexts.TryGetBinding(contextKey, out string epoch, out bool unresolved, out bool newEpoch))
                {
                    if (!KnightIdentitySidecar.TryResolveForWrite(contextKey, json, out epoch, out newEpoch, out unresolved))
                    {
                        KnightIdentityLog.Once("save-context-unresolved", null);
                        return;
                    }
                    KnightIdentityContexts.RememberBinding(contextKey, epoch, unresolved, newEpoch);
                }
                if (unresolved || string.IsNullOrEmpty(epoch))
                {
                    // unresolved 世代不得产出新的匿名快照（可能顶掉最后一份可证明来源）；sidecar 原样保留。
                    KnightIdentityLog.Once("save-preserve-unresolved", null);
                    return;
                }

                string snapshotHash;
                try
                {
                    snapshotHash = KnightIdentityFingerprint.Normalized(json, epoch); // kind2：只剔除 3 个实测时钟
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("save-json", e);
                    return;
                }

                if (!TryBuildEntries(capture, island, out List<KnightIdentitySnapshotEntry> entries)) return;

                if (!KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, snapshotHash, DateTimeOffset.UtcNow, entries, out KnightIdentitySnapshot snapshot, out string error))
                {
                    KnightIdentityLog.Once("save-snapshot:" + error, null);
                    return;
                }

                KnightIdentitySidecar.AppendSnapshot(epoch, snapshot, contextKey, newEpoch);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-apply", e);
            }
        }

        /// <summary>
        /// 用户锚定再基线化（设计 C）：面板"应用"在 unresolved 上下文上挂 pending（KnightIdentityRuntime.
        /// ArmPanelRebaseline）后，本方法在下一次原生 Save 的捕获作用域内消费它——以 save 形态 JSON
        /// （不是 apply 时点的装载形态：装载后 objects 已被原生清空/Decay，拍出来是死 hash，下次读档
        /// 必 known-mismatch）新建 epoch 的 kind2 快照；条目 = 本次 save 捕获并现场复核过的面板铸出收据。
        /// 严格门：上下文一致、binding 仍 unresolved、全部存活 mint 都按 uniqueID 出现在本次捕获且收据
        /// 一致、快照只含面板铸出的收据；任何缺口 = 不写（pending 保持，下次 Save 重试；未保存退出 = 零写入，
        /// 不产生 legacy-pending→known-mismatch 恶化边）。旧 epoch/历史记录原样保留；成功后 context 转
        /// resolved（CanFlushSeed 恢复）。返回 true = 本 save 已由本路径处理（成功/门拒绝/fail-closed 皆是）。
        /// </summary>
        private static bool TryConsumePanelRebaseline(SaveCapture capture)
        {
            try
            {
                string contextKey = KnightIdentityRuntime.PanelRebaselineContext;
                if (contextKey == null || capture == null || capture.Island == null) return false;
                if (!KnightIdentitySidecar.TryBuildContextKey(capture.Campaign, capture.Challenge, SafeIslandLand(capture.Island), out string captureKey)) return false;
                if (!string.Equals(contextKey, captureKey, StringComparison.Ordinal)) return false; // 别的岛：pending 保持

                if (CountLivePanelMints() == 0)
                {
                    // 应用过的骑士已全部离场（死亡/换 world/换 life）：pending 无内容可写，交还既有路径。
                    KnightIdentityRuntime.DisarmPanelRebaseline();
                    KnightPanelLog.Info("user-anchored rebaseline dropped: no applied knight remains");
                    return false;
                }
                if (!KnightIdentityContexts.TryGetBinding(contextKey, out _, out bool unresolved, out _)) return false;
                if (!unresolved)
                {
                    // 该上下文已被其它路径解析：pending 已无意义，交还正常写路径。
                    KnightIdentityRuntime.DisarmPanelRebaseline();
                    return false;
                }

                string json;
                try
                {
                    json = JsonUtility.ToJson(capture.Island, false); // 写入来源只能是本次 save 的整岛 JSON
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("panel-rebaseline-json", e);
                    return true; // pending 保持：本 save 不写
                }
                if (string.IsNullOrEmpty(json)) return true;

                if (!TryBuildPanelEntries(capture, out List<KnightIdentitySnapshotEntry> entries, out bool complete) || !complete)
                {
                    KnightPanelLog.WarnOnce("panel-rebaseline-incomplete", "user-anchored rebaseline incomplete: roster changed, will retry on the next save");
                    return true; // pending 保持：绝不写部分名单
                }

                if (!KnightIdentitySidecar.TryUserRebaseline(contextKey, json, entries, out string epoch))
                    return true; // 写入失败：日志在写路径；pending 保持，下次 save 重试

                KnightIdentityContexts.RememberBinding(contextKey, epoch, false, true, "user-rebaseline");
                KnightIdentityRuntime.ConfirmContext(false);
                KnightIdentityRuntime.DisarmPanelRebaseline();
                KnightPanelLog.Info("user-anchored rebaseline: knights=" + entries.Count.ToString(CultureInfo.InvariantCulture)
                    + " styles=" + PanelStyleSummary(entries) + ", prior records preserved");
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("panel-rebaseline", e);
                return true;
            }
        }

        /// <summary>仍在世（同对象同 life）的面板 mint 数。</summary>
        private static int CountLivePanelMints()
        {
            List<KnightIdentityRuntime.PanelMint> mints = KnightIdentityRuntime.PanelMints;
            int live = 0;
            for (int i = 0; i < mints.Count; i++)
            {
                Knight knight = mints[i].Knight;
                if (knight == null) continue;
                if (KnightIdentityRuntime.GetLifetime(knight) != mints[i].Lifetime) continue;
                live++;
            }
            return live;
        }

        /// <summary>
        /// 再基线化条目：盘记录 ∩ 本次捕获 owner，且每张收据都必须来自面板 mint（expected），
        /// 全部存活 mint 都必须被覆盖。未捕获的记录（侍从等非 tagKnight 对象）与正常快照路径同样跳过。
        /// </summary>
        private static bool TryBuildPanelEntries(SaveCapture capture, out List<KnightIdentitySnapshotEntry> entries, out bool complete)
        {
            entries = null;
            complete = false;
            List<KnightIdentityRuntime.PanelMint> mints = KnightIdentityRuntime.PanelMints;
            if (mints.Count == 0) return false;

            HashSet<Guid> expected = new HashSet<Guid>();
            for (int i = 0; i < mints.Count; i++)
            {
                KnightIdentityRuntime.PanelMint mint = mints[i];
                if (mint.Knight == null) continue;
                if (KnightIdentityRuntime.GetLifetime(mint.Knight) != mint.Lifetime) continue; // 已换 life / 不在世
                if (!mint.Receipt.IsValid) return false;
                expected.Add(mint.Receipt.Id);
            }
            if (expected.Count == 0) return false;

            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> records;
            try
            {
                records = capture.Island.objects;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("panel-rebaseline-records", e);
                return false;
            }
            if (records == null) return false;

            List<KnightIdentitySnapshotEntry> built = new List<KnightIdentitySnapshotEntry>(expected.Count);
            HashSet<Guid> seen = new HashSet<Guid>();
            for (int i = 0; i < records.Count; i++)
            {
                IslandSaveData.ObjectData record = records[i];
                if (record == null) continue;
                string uniqueId = record.uniqueID;
                if (!KnightIdentitySnapshot.IsValidUniqueId(uniqueId)) continue;
                if (!capture.Owners.TryGetValue(uniqueId, out CapturedOwner owner)) continue; // 未捕获（侍从等）：跳过
                if (!IsValidOwner(owner)) return false;                     // owner 换 life / 收据变了：整份拒写
                if (!RecordIsKnight(record)) continue;
                if (!expected.Contains(owner.Receipt.Id)) return false;     // 非面板铸出的收据：fail-closed
                if (!seen.Add(owner.Receipt.Id)) return false;              // 同 GUID 两个 uniqueID：fail-closed
                if (built.Count >= MaxSnapshotEntries) return false;        // 超限：整份拒写
                built.Add(new KnightIdentitySnapshotEntry(uniqueId, owner.Receipt));
            }

            complete = built.Count > 0 && seen.IsSupersetOf(expected);
            entries = complete ? built : null;
            return true;
        }

        /// <summary>日志用：条目按 style 的计数 "a/b/c/d/e"。</summary>
        private static string PanelStyleSummary(List<KnightIdentitySnapshotEntry> entries)
        {
            int[] counts = new int[KnightIdentityReceipt.StyleCount];
            for (int i = 0; i < entries.Count; i++)
            {
                int style = entries[i].Receipt.Style;
                if (KnightIdentityReceipt.IsValidStyle(style)) counts[style]++;
            }
            StringBuilder builder = new StringBuilder(16);
            for (int i = 0; i < counts.Length; i++)
            {
                if (i > 0) builder.Append('/');
                builder.Append(counts[i].ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>
        /// 吸收态自愈（B）：本会话装载解析为精确 known-mismatch（历史对不上当前岛、此后所有 save 都被
        /// save-preserve-unresolved 拒写）时，若当前盘骑士个体全部来自历史并集（严格子集证据门），
        /// 以当前盘 JSON 新建 epoch 的 kind2 基线快照，并把携带身份重绑到本次 save 的 live owner；
        /// 证据缺口（缺 owner / 换 life / 已有不同收据）时维持写保护（binding=rebaseline-incomplete），
        /// 不依赖 capture.Owners——该会话没有任何收据。返回 true = 本 save 已由本路径处理
        /// （无论成功/门拒绝/fail-closed），不再回落旧路径。
        /// 已知活性边界：每会话每 context 至多消耗一个 epoch（MaxEpochs=8）；跨会话若内容持续对不上
        /// 就会逐代再基线，直至容量耗尽后永久 fail-closed——这是本机制接受的上限，不是回归。
        /// </summary>
        private static bool TryRebaselineAbsorbingContext(SaveCapture capture)
        {
            try
            {
                IslandSaveData island = capture.Island;
                if (!KnightIdentitySidecar.TryBuildContextKey(capture.Campaign, capture.Challenge, SafeIslandLand(island), out string contextKey)) return false;
                if (!KnightIdentityContexts.TryGetBindingKind(contextKey, out string kind)
                    || !string.Equals(kind, "known-mismatch", StringComparison.Ordinal)) return false;

                string json;
                try
                {
                    json = JsonUtility.ToJson(island, false);
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("rebaseline-json", e);
                    return true;
                }
                if (string.IsNullOrEmpty(json)) return true;

                if (!TryEnumerateDiskKnightUniqueIds(island, out List<string> diskUniqueIds)) return true; // 枚举失败/盘记录重复：fail-closed

                if (!KnightIdentitySidecar.TryRebaseline(contextKey, json, diskUniqueIds,
                        out string rebaselinedEpoch, out List<KnightIdentitySnapshotEntry> carried, out HashSet<string> dropped))
                    return true; // 证据缺口/门拒绝/容量耗尽/写入失败：日志在写路径，本 save 保持 fail-closed

                if (!TryRebindCarriedOwners(capture, carried, dropped))
                {
                    // 证据缺口（缺 live owner / 换 life / 已有不同收据）：绝不放行普通快照，否则后续 save 会静默丢人。
                    KnightIdentityContexts.RememberBinding(contextKey, rebaselinedEpoch, true, true, "rebaseline-incomplete");
                    KnightIdentityRuntime.ConfirmContext(true);
                    return true;
                }

                // 防同会话后续自动保存重复过门、重复建 epoch（MaxEpochs=8 会被撑爆）。
                KnightIdentityContexts.RememberBinding(contextKey, rebaselinedEpoch, false, true, "rebaseline");
                KnightIdentityRuntime.ConfirmContext(false);
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("rebaseline", e);
                return true;
            }
        }

        /// <summary>
        /// 再基线化重绑定事务：carried 的每个 uniqueID 必须由本次 save 捕获的 live owner 现场通过终检
        /// （同 life + tagKnight + 活跃 + 当前收据相同；捕获时的收据不算数）才放行；任一缺口返回 false，
        /// 由调用方维持写保护。dropped（历史不一致 / 同 GUID 输者）只清 FailedLoad 交给既有 fresh 路径，
        /// 绝不在 save 路径内铸造收据，也绝不全局清 FailedLoad。
        /// </summary>
        private static bool TryRebindCarriedOwners(SaveCapture capture, List<KnightIdentitySnapshotEntry> carried, HashSet<string> dropped)
        {
            bool complete = true;
            HashSet<Guid> boundIds = new HashSet<Guid>();
            if (carried != null)
            {
                for (int i = 0; i < carried.Count; i++)
                {
                    KnightIdentitySnapshotEntry entry = carried[i];
                    if (!boundIds.Add(entry.Receipt.Id))
                    {
                        complete = false; // 同 GUID 只认一个 live owner；防御性 fail-closed
                        continue;
                    }
                    if (!TryGetCapturedOwner(capture, entry.NativeUniqueId, out CapturedOwner owner))
                    {
                        complete = false; // 本次 save 没有该 uniqueID 的任何 live 证据
                        continue;
                    }
                    // 捕获值不等于现状：统一走同一终检（同 receipt 同 life 幂等；换 life/tag/active/不同收据一律拒绝）。
                    if (!KnightIdentityRuntime.TryBindCarriedReceipt(owner.Knight, owner.Lifetime, entry.Receipt))
                        complete = false;
                }
            }

            if (dropped != null)
            {
                foreach (string uniqueId in dropped)
                {
                    if (!TryGetCapturedOwner(capture, uniqueId, out CapturedOwner owner)) continue;
                    if (owner.Receipt.IsValid) continue; // 已有收据的 live owner：本事务不动
                    KnightIdentityRuntime.TryClearFailedLoad(owner.Knight, owner.Lifetime);
                }
            }
            return complete;
        }

        private static bool TryGetCapturedOwner(SaveCapture capture, string uniqueId, out CapturedOwner owner)
        {
            if (capture.Owners.TryGetValue(uniqueId, out owner)) return true;
            return capture.PendingOwners.TryGetValue(uniqueId, out owner);
        }

        /// <summary>当前盘骑士 uniqueID 枚举：island.objects + 精确 Knight/KnightData（同 Save 快照口径）。</summary>
        private static bool TryEnumerateDiskKnightUniqueIds(IslandSaveData island, out List<string> uniqueIds)
        {
            uniqueIds = null;
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> records;
            try
            {
                records = island.objects;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("rebaseline-records", e);
                return false;
            }
            if (records == null) return false;

            List<string> ids = new List<string>(records.Count);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < records.Count; i++)
            {
                IslandSaveData.ObjectData record = records[i];
                if (record == null) continue;
                string uniqueId = record.uniqueID;
                if (!KnightIdentitySnapshot.IsValidUniqueId(uniqueId)) continue;
                if (!RecordIsKnight(record)) continue;
                if (seen.Add(uniqueId)) ids.Add(uniqueId);
                else
                {
                    // 盘记录本身重复：这不是「唯一 live owner」的证据，绝不把去重结果当可靠输入（fail-closed）。
                    KnightIdentityLog.Once("rebaseline-duplicate-record", null);
                    return false;
                }
            }
            uniqueIds = ids;
            return true;
        }

        /// <summary>
        /// 原生主体里才设 CurrentlySavingIsland（finally 清掉），所以只能在 GetID 期间取；且必须属于本次
        /// Save 参数（land 一致，-1 表示按当前岛），不拿嵌套外层的岛。
        /// </summary>
        private static bool TryCaptureIsland(SaveCapture capture)
        {
            IslandSaveData island = CurrentSavingIsland();
            if (island == null) return false;
            try
            {
                if (IslandSaveData.isSavingGame != true) return false; // 只认正在保存的那次调用
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-saving-flag", e);
                return false;
            }
            int land = SafeIslandLand(island);
            if (capture.Land != -1 && land != capture.Land) return false;
            capture.Island = island;
            return true;
        }

        /// <summary>条目只来自本次 native snapshot 的记录；任何冲突（超限/重复 GUID/owner 变 life 或收据）整份拒写。</summary>
        private static bool TryBuildEntries(SaveCapture capture, IslandSaveData island, out List<KnightIdentitySnapshotEntry> entries)
        {
            entries = null;
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData> records;
            try
            {
                records = island.objects;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-records", e);
                return false;
            }
            if (records == null) return false;

            List<KnightIdentitySnapshotEntry> built = new List<KnightIdentitySnapshotEntry>(capture.Owners.Count);
            HashSet<Guid> seenGuids = new HashSet<Guid>();

            for (int i = 0; i < records.Count; i++)
            {
                IslandSaveData.ObjectData record = records[i];
                if (record == null) continue;
                string uniqueId = record.uniqueID;
                if (!KnightIdentitySnapshot.IsValidUniqueId(uniqueId)) continue;
                if (!capture.Owners.TryGetValue(uniqueId, out CapturedOwner owner)) continue;
                if (!IsValidOwner(owner)) return false;                       // owner 换 life / 收据变了：整份拒写
                if (!RecordIsKnight(record)) continue;                     // 精确 Knight + KnightData：排除 Squire 等
                if (!seenGuids.Add(owner.Receipt.Id))
                {
                    KnightIdentityLog.Once("save-duplicate-guid", null);
                    return false;                                          // 冲突：整份拒写
                }
                if (built.Count >= MaxSnapshotEntries)
                {
                    KnightIdentityLog.Once("save-entry-cap", null);
                    return false;                                          // 超限：整份拒写
                }
                built.Add(new KnightIdentitySnapshotEntry(uniqueId, owner.Receipt));
            }

            if (built.Count == 0) return false;
            entries = built;
            return true;
        }

        private static bool IsValidOwner(CapturedOwner owner)
        {
            if (owner.Knight == null || !owner.Receipt.IsValid) return false;
            GameObject go = SafeGameObject(owner.Knight);
            if (go == null || !SafeCompareTag(go, "Knight")) return false;
            if (KnightIdentityRuntime.GetLifetime(owner.Knight) != owner.Lifetime) return false; // 必须仍是同 life
            return KnightIdentityRuntime.TryGetReceipt(owner.Knight, out KnightIdentityReceipt current)
                && current.Equals(owner.Receipt); // 同 life 内收据变化同样拒写
        }

        /// <summary>精确组件记录（与 Hermes 的 name/type 双等比较同一约定）：name == "Knight" && type == "KnightData"。</summary>
        private static bool RecordIsKnight(IslandSaveData.ObjectData record)
        {
            Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData.ComponentData> components;
            try
            {
                components = record.componentData2;
            }
            catch
            {
                return false;
            }
            if (components == null) return false;
            for (int i = 0; i < components.Count; i++)
            {
                IslandSaveData.ObjectData.ComponentData component = components[i];
                if (component == null) continue;
                if (string.Equals(component.name, "Knight", StringComparison.Ordinal)
                    && string.Equals(component.type, "KnightData", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static IslandSaveData CurrentSavingIsland()
        {
            try
            {
                IslandSaveData island = IslandSaveData.CurrentlySavingIsland;
                if (island != null) return island;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-island-property", e);
            }
            try
            {
                return IslandSaveData._currentlySavingIsland; // 属性不可用时的字段兜底（不假设 current）
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-island-field", e);
                return null;
            }
        }

        private static int SafeIslandLand(IslandSaveData island)
        {
            try
            {
                return island.land;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-land", e);
                return -2;
            }
        }

        internal static GameObject SafeGameObject(UnityEngine.Object owner)
        {
            try
            {
                if (owner == null) return null;
                return owner is Component component ? component.gameObject : owner as GameObject;
            }
            catch
            {
                return null;
            }
        }

        internal static T SafeGetComponent<T>(GameObject go) where T : Component
        {
            try
            {
                return go == null ? null : go.GetComponent<T>();
            }
            catch
            {
                return null;
            }
        }

        private static bool SafeCompareTag(GameObject go, string tag)
        {
            try
            {
                return go != null && go.CompareTag(tag);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// TryPopObjectsToScene 作用域：native ClearLevel/排序/Decay 之前拍完整 JSON + 稳定上下文（文件名 +
    /// campaign/challenge + land），读取 sidecar 解析出精确快照或本代可写 epoch（kind1 legacy / kind2
    /// 时钟无关指纹都是精确匹配），供 TryCreateOrFind 绑定。finalizer 恒定还原嵌套上下文。
    /// </summary>
    internal static class KnightIdentityGeneration
    {
        internal sealed class Capture
        {
            internal IntPtr Campaign, Island, World, Layer;
            internal string Context;
            internal bool Pending;
        }

        private static int _depth;
        private static readonly Dictionary<string, Capture> Confirmed = new Dictionary<string, Capture>(StringComparer.Ordinal);
        internal static bool Active { get { return _depth != 0; } }
        internal static void ResetForTests() { _depth = 0; Confirmed.Clear(); }

        private static bool Read(CampaignSaveData campaign, out Capture capture)
        {
            capture = null;
            if (!KnightIdentityRuntime.IsHostAuthority() || campaign == null || GlobalSaveData.loaded == null) return false;
            IslandSaveData island = campaign.CurrentIsland;
            // 与已验证的 HeroRecruitment generation bridge 相同的原生事实；不以时钟/指针作永久身份。
            if (island == null || !island.isNew || island.playTimeDays != 0.0) return false;
            World world = Managers.Inst != null ? Managers.Inst.world : null;
            if (world == null || world.Pointer == IntPtr.Zero) return false;
            Transform layer = world.gameLayer;
            if (layer == null || layer.Pointer == IntPtr.Zero || layer.gameObject == null || !layer.gameObject.activeInHierarchy) return false;
            if (!KnightIdentitySidecar.TryBuildContextKey(GlobalSaveData.loaded.currentCampaign,
                GlobalSaveData.loaded.currentChallenge, island.land, out string context)) return false;
            capture = new Capture { Campaign = campaign.Pointer, Island = island.Pointer, World = world.Pointer, Layer = layer.Pointer, Context = context };
            return true;
        }

        internal static Capture Begin(CampaignSaveData campaign)
        {
            if (Active) return null; // 外层生成调用负责唯一提交
            if (!Read(campaign, out Capture capture)) return null;
            if (Confirmed.TryGetValue(capture.Context, out Capture old) && old.Island == capture.Island && old.World == capture.World && old.Layer == capture.Layer) return null;
            if (!Confirmed.ContainsKey(capture.Context) && Confirmed.Count >= KnightIdentityArchive.MaxContexts)
            { KnightIdentityRuntime.ConfirmContext(true); return null; }
            capture.Pending = true;
            _depth++;
            return capture;
        }

        internal static void End(CampaignSaveData campaign, Capture capture, bool succeeded)
        {
            if (capture == null || !capture.Pending) return;
            capture.Pending = false;
            _depth--;
            if (!succeeded || !Read(campaign, out Capture current) || CampaignSaveData.current == null
                || CampaignSaveData.current.Pointer != capture.Campaign || current.Campaign != capture.Campaign
                || current.Island != capture.Island || current.World != capture.World || current.Layer != capture.Layer || current.Context != capture.Context) return;
            string epoch = KnightIdentityArchive.NewScope();
            string json = JsonUtility.ToJson(campaign.CurrentIsland, false);
            if (!KnightIdentitySidecar.CommitGeneration(capture.Context, epoch, json))
            {
                KnightIdentityContexts.RememberBinding(capture.Context, null, true, false);
                KnightIdentityRuntime.ConfirmContext(true);
                return;
            }
            Confirmed[capture.Context] = capture;
            KnightIdentityContexts.RememberBinding(capture.Context, epoch, false, false);
            KnightIdentityRuntime.ConfirmContext(false);
            KnightIdentityLog.Receipt("new-generation context=" + capture.Context.Substring(0, 8) + " scope=" + epoch.Substring(0, 8));
        }
    }

    [HarmonyPatch(typeof(CampaignSaveData), nameof(CampaignSaveData.ApplyToScene))]
    internal static class KnightIdentityGenerationPatch
    {
        [HarmonyPrefix]
        private static void Before(CampaignSaveData __instance, out KnightIdentityGeneration.Capture __state)
        {
            __state = null;
            try { __state = KnightIdentityGeneration.Begin(__instance); }
            catch (Exception e) { KnightIdentityLog.Once("generation-begin", e); }
        }

        [HarmonyFinalizer]
        private static Exception Finally(CampaignSaveData __instance, Exception __exception, KnightIdentityGeneration.Capture __state)
        {
            try { KnightIdentityGeneration.End(__instance, __state, __exception == null); }
            catch (Exception e) { KnightIdentityRuntime.ConfirmContext(true); KnightIdentityLog.Once("generation-end", e); }
            return __exception;
        }
    }

    internal static class KnightIdentityLoadBridge
    {
        internal sealed class LoadScope
        {
            internal bool Ended;
            internal readonly List<LoadScope> CompletedChildren = new List<LoadScope>();
            internal LoadScope Previous;
            internal long Id;
            internal IslandSaveData Island;
            internal string ContextKey;    // 稳定岛上下文（文件名 + campaign/challenge + land）
            internal string Kind;          // 解析诊断标签
            internal string ScopeKey;      // 本代 epoch；null = unresolved（不可恢复也不可写）
            internal string SnapshotHash;  // 精确命中的存储 hash，或待写入的新 kind2 hash
            internal KnightIdentitySnapshot Snapshot;
            internal Dictionary<string, KnightIdentityReceipt> Receipts;
            internal bool NewEpoch;        // 写路径需为 context 登记新 epoch
            internal bool Unresolved = true; // 解析中途失败也必须 fail closed
        }

        private static LoadScope _scope;
        private static long _scopeCounter; // 单调：与活动 id 分离，不受嵌套还原影响

        internal static LoadScope Current
        {
            get { return _scope; }
        }

        internal static LoadScope Begin(IslandSaveData island)
        {
            LoadScope scope = new LoadScope
            {
                Id = ++_scopeCounter,
                Island = island,
                Previous = _scope,
            };
            _scope = scope;
            KnightIdentityRuntime.EnterLoadScope(scope.Id);

            try
            {
                if (island == null) return scope;
                if (!KnightIdentityRuntime.IsHostAuthority()) return scope; // 本方法 scope 不会 client 使用
                if (KnightIdentityGeneration.Active) return scope; // 新生成只由成功的 ApplyToScene 建立 epoch

                string json;
                try
                {
                    json = JsonUtility.ToJson(island, false);
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("load-json", e);
                    return scope;
                }
                if (string.IsNullOrEmpty(json)) return scope;

                if (!TryReadInt(() => GlobalSaveData.loaded.currentCampaign, out int campaign)
                    || !TryReadInt(() => GlobalSaveData.loaded.currentChallenge, out int challenge)
                    || !KnightIdentitySidecar.TryBuildContextKey(campaign, challenge, SafeIslandLand(island), out string contextKey))
                {
                    KnightIdentityLog.Once("load-context", null);
                    return scope;
                }
                scope.ContextKey = contextKey;

                KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(KnightIdentitySidecar.Path);
                if (loaded.Status == KnightIdentityArchiveStatus.UnsupportedVersion
                    || (loaded.Status == KnightIdentityArchiveStatus.Corrupt && !loaded.RecoveredBackup))
                {
                    KnightIdentityLog.Once("load-sidecar-readonly:" + loaded.Status, null);
                    return scope;
                }
                if (loaded.Status == KnightIdentityArchiveStatus.Missing)
                {
                    // 取证：把「主备皆缺，按无历史重种」与普通 load-fresh 区分开（一次性 Info）。
                    KnightIdentityLog.InfoOnce("load-sidecar-missing", "load-sidecar-missing（主备皆缺，重种）");
                }
                KnightIdentityArchive archive = loaded.Status == KnightIdentityArchiveStatus.Missing
                    ? KnightIdentityArchive.CreateEmpty()
                    : loaded.Archive;
                if (archive == null) return scope;

                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, contextKey, json);
                scope.ContextKey = contextKey;
                scope.Kind = resolution.Kind;
                scope.NewEpoch = resolution.NewEpoch;
                scope.Unresolved = resolution.Unresolved;

                if (resolution.Unresolved)
                {
                    // 冲突 / 对不上的已知历史 / 仍有未归属历史：不恢复、不建 epoch、不写种子，sidecar 原样保留。
                    KnightIdentityLog.Once("load-unresolved:" + resolution.Kind, null);
                    return scope;
                }

                scope.ScopeKey = resolution.Epoch;
                if (resolution.Receipts != null)
                {
                    scope.SnapshotHash = resolution.MatchHash;
                    scope.Snapshot = resolution.Matched;
                    scope.Receipts = resolution.Receipts;
                    KnightIdentityLog.Receipt("load-match scope=" + ShortHash(resolution.Epoch) + " hash=" + ShortHash(resolution.MatchHash)
                        + " entries=" + resolution.Receipts.Count.ToString(CultureInfo.InvariantCulture)
                        + " kind=" + (resolution.MatchKind == KnightIdentityFingerprint.KindNormalized ? "exact" : "legacy"));
                    return scope;
                }

                scope.SnapshotHash = resolution.FreshHash;
                KnightIdentityLog.Receipt("load-fresh scope=" + ShortHash(resolution.Epoch) + " context=" + ShortHash(contextKey));
                KnightIdentityLoadSeed.Begin(scope);
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("load-begin", e);
            }
            return scope;
        }

        internal static Exception End(Exception exception, LoadScope scope, bool succeeded = true)
        {
            if (scope != null && !scope.Ended)
            {
                scope.Ended = true;
                long previous = scope.Previous != null ? scope.Previous.Id : 0;
                KnightIdentityRuntime.ExitLoadScope(scope.Id, previous);
                if (ReferenceEquals(_scope, scope)) _scope = scope.Previous;
                KnightIdentityRuntime.FinishLoadedReceipts(scope.Id, exception == null && succeeded && !scope.Unresolved, previous);
                if (exception == null && succeeded && scope.Previous != null)
                {
                    scope.Previous.CompletedChildren.AddRange(scope.CompletedChildren);
                    scope.Previous.CompletedChildren.Add(scope);
                }
                if (exception == null && succeeded && scope.Previous == null && KnightIdentityRuntime.IsHostAuthority())
                {
                    foreach (LoadScope child in scope.CompletedChildren) CommitBinding(child);
                    KnightIdentityContexts.RememberBinding(scope.ContextKey, scope.ScopeKey, scope.Unresolved, scope.NewEpoch, scope.Kind);
                    KnightIdentityRuntime.ConfirmContext(scope.Unresolved);
                }
                KnightIdentityLoadSeed.Complete(scope, exception == null && succeeded);
                if (exception == null && succeeded && scope.Previous == null && scope.Receipts != null && scope.NewEpoch
                    && !string.IsNullOrEmpty(scope.ContextKey) && !string.IsNullOrEmpty(scope.ScopeKey))
                {
                    // 精确命中且该 scope 还没有归属：持久化「上下文 → epoch」映射（一次性、非破坏；失败只降级）。
                    KnightIdentitySidecar.ClaimContext(scope.ContextKey, scope.ScopeKey);
                }
            }
            return exception;
        }

        private static void CommitBinding(LoadScope scope)
        {
            KnightIdentityContexts.RememberBinding(scope.ContextKey, scope.ScopeKey, scope.Unresolved, scope.NewEpoch, scope.Kind);
            if (scope.Receipts != null && scope.NewEpoch && !string.IsNullOrEmpty(scope.ContextKey) && !string.IsNullOrEmpty(scope.ScopeKey))
                KnightIdentitySidecar.ClaimContext(scope.ContextKey, scope.ScopeKey);
        }

        /// <summary>TryCreateOrFind 后缀：按当前 LoadScope 快照的 uniqueID→收据绑定实际 Persistent root，不改 native 参数。</summary>
        internal static void HandleTryCreateOrFind(IslandSaveData.ObjectData objectData, Persistent result)
        {
            try
            {
                LoadScope scope = _scope;
                if (scope == null || result == null) return;
                KnightIdentityLoadSeed.Capture(scope, objectData, result);
                if (scope.Receipts == null) return;

                string uniqueId = objectData != null ? objectData.uniqueID : null;
                if (!KnightIdentitySnapshot.IsValidUniqueId(uniqueId)) return;
                if (!scope.Receipts.TryGetValue(uniqueId, out KnightIdentityReceipt receipt)) return;

                GameObject root = KnightIdentitySaveBridge.SafeGameObject(result);
                if (root == null) return;
                Knight knight = KnightIdentitySaveBridge.SafeGetComponent<Knight>(root);
                if (knight == null) return; // 共享 uniqueID 的非骑士对象：不绑

                if (!KnightIdentityRuntime.BindLoadedReceipt(knight, receipt))
                {
                    KnightIdentityLog.Once("load-bind-rejected:" + uniqueId, null);
                }
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("load-bind", e);
            }
        }

        private static string ShortHash(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length < 8 ? value : value.Substring(0, 8);
        }

        private static int SafeIslandLand(IslandSaveData island)
        {
            try
            {
                return island.land;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("load-land", e);
                return -1;
            }
        }

        private static bool TryReadInt(Func<int> read, out int value)
        {
            value = -1;
            try
            {
                value = read();
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("load-context", e);
                return false;
            }
        }
    }

    /// <summary>sidecar 文件定位、scope 上下文与「合并后写」的唯一入口。所有 I/O 都在显式调用里。</summary>
    internal static class KnightIdentitySidecar
    {
        internal static string Path
        {
            get
            {
                try
                {
                    return System.IO.Path.Combine(Paths.ConfigPath, "KingdomEnhancedMod", "ModSave", "knight-identities.v1.json");
                }
                catch (Exception e)
                {
                    KnightIdentityLog.Once("path", e);
                    return null;
                }
            }
        }

        /// <summary>
        /// 稳定上下文键：存档文件名 + 实际 campaign/challenge + island.land（不含任何运行时重建值）。
        /// 文件名缺失、land 越界或读取失败 → false（不写、不读）。challenge 模式的 campaign 可能是 -1，按原值参与。
        /// </summary>
        internal static bool TryBuildContextKey(int campaign, int challenge, int land, out string contextKey)
        {
            contextKey = null;
            string file = SafeGlobalFilename();
            if (string.IsNullOrEmpty(file) || file.Length > 256) return false;
            if (land < 0) return false;
            contextKey = KnightIdentityArchive.ContextKey(file, campaign, challenge, land);
            return true;
        }

        /// <summary>
        /// 重新 Load 磁盘后合并本代快照，并在同一次原子写入里登记「上下文 → epoch」映射。
        /// 状态分流：Missing 才 CreateEmpty；Valid 直接用；Valid+RecoveredBackup（数据来自备份）先按核心方式
        /// 显式修复主文件再写；Corrupt（无有效备份）与 UnsupportedVersion 一律只读降级，绝不覆盖、绝不删文件。
        /// </summary>
        internal static void AppendSnapshot(string scopeKey, KnightIdentitySnapshot snapshot, string contextKey, bool newEpoch)
        {
            try
            {
                string path = Path;
                if (string.IsNullOrEmpty(path)) return;
                if (string.IsNullOrEmpty(contextKey))
                {
                    KnightIdentityLog.Once("sidecar-context-missing", null); // 没有上下文映射就不落盘
                    return;
                }
                if (!TryOpenWritableArchive(path, true, out KnightIdentityArchive archive, out string blocked))
                {
                    KnightIdentityLog.Once(blocked, null);
                    return;
                }

                KnightIdentityArchive.MutationStatus status = archive.RecordSnapshot(scopeKey, snapshot);
                if (status == KnightIdentityArchive.MutationStatus.RejectedInvalid
                    || status == KnightIdentityArchive.MutationStatus.RejectedConflict)
                {
                    KnightIdentityLog.Once("sidecar-rejected-invalid", null);
                    return;
                }
                if (status == KnightIdentityArchive.MutationStatus.RejectedCapacity)
                {
                    KnightIdentityLog.Once("sidecar-rejected-capacity", null); // 满：不牺牲别的 scope
                    return;
                }
                if (!archive.EnsureContext(contextKey, scopeKey, newEpoch))
                {
                    KnightIdentityLog.Once("sidecar-context-rejected", null); // scope 已属别的上下文/容量满：fail closed
                    return;
                }

                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                KnightIdentityArchiveStore.SaveResult saved = KnightIdentityArchiveStore.Save(path, archive);
                LogRetryOutcome(saved);
                if (!saved.Ok)
                {
                    KnightIdentityLog.Once("sidecar-save:" + saved.Status, null);
                    return;
                }
                if (saved.Status == KnightIdentityArchiveStore.SaveStatus.Unchanged) return; // 盘上已一致（含 context 映射）
                KnightIdentityLog.Receipt("save scope=" + ShortHash(scopeKey) + " hash=" + ShortHash(snapshot.Hash)
                    + " entries=" + snapshot.Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("sidecar-append", e); // 任何 I/O 异常都不影响原生保存
            }
        }

        /// <summary>
        /// 精确命中一个尚未归属的 epoch 后，把「上下文 → epoch」映射持久化（不写快照、不改历史）。
        /// 与 AppendSnapshot 同一套状态分流/原子写；任何失败只降级成本会话内解析。
        /// </summary>
        internal static void ClaimContext(string contextKey, string scopeKey)
        {
            try
            {
                string path = Path;
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(contextKey) || string.IsNullOrEmpty(scopeKey)) return;
                if (!TryOpenWritableArchive(path, true, out KnightIdentityArchive archive, out string blocked))
                {
                    KnightIdentityLog.Once(blocked, null);
                    return;
                }
                if (!archive.EnsureContext(contextKey, scopeKey, true))
                {
                    KnightIdentityLog.Once("sidecar-claim-rejected", null);
                    return;
                }

                KnightIdentityArchiveStore.SaveResult saved = KnightIdentityArchiveStore.Save(path, archive);
                LogRetryOutcome(saved);
                if (!saved.Ok)
                {
                    KnightIdentityLog.Once("sidecar-claim-save:" + saved.Status, null);
                    return;
                }
                if (saved.Status == KnightIdentityArchiveStore.SaveStatus.Unchanged) return;
                KnightIdentityLog.Receipt("claim scope=" + ShortHash(scopeKey) + " context=" + ShortHash(contextKey));
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("sidecar-claim", e);
            }
        }

        /// <summary>
        /// 吸收态再基线化写路径（B，仅 knight 侧）：严格子集证据门通过后，以当前盘 JSON 新建 epoch 的
        /// kind2 快照，历史同一 uniqueID 在多处收据全一致才携带，否则丢弃走 fresh。旧 epoch/快照全保留；
        /// 容量耗尽与门拒绝一律 fail-closed（不写盘）。复刻 AppendSnapshot 的单写者纪律（重读磁盘→合并→原子写）。
        /// <paramref name="carriedEntries"/> 只在成功时为非 null，交给调用方做同会话重绑定，禁止重读/重解析；
        /// <paramref name="droppedUniqueIds"/> 是本盘被有意丢弃（历史不一致或同 GUID 输者）的 uniqueID。
        /// </summary>
        internal static bool TryRebaseline(string contextKey, string rawJson, List<string> diskUniqueIds,
            out string newEpoch, out List<KnightIdentitySnapshotEntry> carriedEntries, out HashSet<string> droppedUniqueIds)
        {
            newEpoch = null;
            carriedEntries = null;
            droppedUniqueIds = null;
            try
            {
                string path = Path;
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(contextKey))
                {
                    KnightIdentityLog.Once("rebaseline-path", null);
                    return false;
                }
                if (!TryOpenWritableArchive(path, true, out KnightIdentityArchive archive, out string blocked))
                {
                    KnightIdentityLog.Once(blocked, null);
                    return false;
                }
                if (archive == null || !archive.TryGetContext(contextKey, out KnightIdentityContext context))
                {
                    KnightIdentityLog.Once("rebaseline-context", null);
                    return false;
                }

                // 历史并集：本 context 全部 epoch 的全部快照条目按 uniqueID；同 id 收据不一致则记 conflicted。
                Dictionary<string, KnightIdentityReceipt> history = new Dictionary<string, KnightIdentityReceipt>(StringComparer.Ordinal);
                HashSet<string> conflicted = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < context.Epochs.Count; i++)
                {
                    if (!archive.TryGetSnapshots(context.Epochs[i], out IReadOnlyList<KnightIdentitySnapshot> snapshots)) continue;
                    for (int s = 0; s < snapshots.Count; s++)
                    {
                        IReadOnlyList<string> ids = snapshots[s].OrderedUniqueIds;
                        for (int e = 0; e < ids.Count; e++)
                        {
                            if (!snapshots[s].TryGet(ids[e], out KnightIdentityReceipt receipt)) continue;
                            if (history.TryGetValue(ids[e], out KnightIdentityReceipt known))
                            {
                                if (!known.Equals(receipt)) conflicted.Add(ids[e]);
                            }
                            else
                            {
                                history.Add(ids[e], receipt);
                            }
                        }
                    }
                }

                int unknown = 0;
                for (int i = 0; i < diskUniqueIds.Count; i++)
                {
                    if (!history.ContainsKey(diskUniqueIds[i])) unknown++;
                }
                if (unknown > 0)
                {
                    // 历史外新个体（可能是别人的档/超历史回滚）：拒绝并维持 fail-closed。
                    KnightIdentityLog.Once("rebaseline-gate-reject:" + unknown.ToString(CultureInfo.InvariantCulture), null);
                    return false;
                }

                // 携带集确定性构造：uniqueID 升序；同 GUID 只由最小 uniqueID 携带（跨 uniqueID 同 GUID 可达，
                // 绑两个同 GUID 活体会让后续每次保存整份拒写）；输者与历史不一致者同样走 fresh。
                List<string> ordered = new List<string>(diskUniqueIds);
                ordered.Sort(StringComparer.Ordinal);
                HashSet<string> dropped = new HashSet<string>(conflicted, StringComparer.Ordinal);
                List<KnightIdentitySnapshotEntry> entries = new List<KnightIdentitySnapshotEntry>(ordered.Count);
                HashSet<Guid> seenGuids = new HashSet<Guid>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    string uniqueId = ordered[i];
                    if (conflicted.Contains(uniqueId)) continue; // 历史收据不一致：绝不猜，走 fresh
                    if (!history.TryGetValue(uniqueId, out KnightIdentityReceipt known))
                    {
                        dropped.Add(uniqueId); // 门已保证不可达，保守走 fresh
                        continue;
                    }
                    if (!seenGuids.Add(known.Id))
                    {
                        dropped.Add(uniqueId); // 同 GUID 输者：绝不写进快照
                        continue;
                    }
                    entries.Add(new KnightIdentitySnapshotEntry(uniqueId, known));
                }

                string epoch = KnightIdentityArchive.NewScope();
                string hash = KnightIdentityFingerprint.Normalized(rawJson, epoch);
                if (!KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hash, DateTimeOffset.UtcNow, entries,
                        out KnightIdentitySnapshot snapshot, out string error))
                {
                    KnightIdentityLog.Once("rebaseline-snapshot:" + error, null);
                    return false;
                }

                KnightIdentityArchive.MutationStatus status = archive.RecordSnapshot(epoch, snapshot);
                if (status != KnightIdentityArchive.MutationStatus.Applied)
                {
                    KnightIdentityLog.Once("rebaseline-capacity-scope", null); // 容量耗尽：不牺牲别的 scope
                    return false;
                }
                if (!archive.EnsureContext(contextKey, epoch, true))
                {
                    KnightIdentityLog.Once("rebaseline-capacity-context", null); // MaxContexts/MaxEpochs 耗尽：fail-closed
                    return false;
                }

                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                KnightIdentityArchiveStore.SaveResult saved = KnightIdentityArchiveStore.Save(path, archive);
                LogRetryOutcome(saved);
                if (!saved.Ok)
                {
                    KnightIdentityLog.Once("rebaseline-save:" + saved.Status, null);
                    return false;
                }
                KnightIdentityLog.Receipt("rebaseline: context=" + ShortHash(contextKey)
                    + " entries=" + diskUniqueIds.Count.ToString(CultureInfo.InvariantCulture)
                    + " carried=" + entries.Count.ToString(CultureInfo.InvariantCulture)
                    + " dropped=" + dropped.Count.ToString(CultureInfo.InvariantCulture)
                    + " epoch=" + ShortHash(epoch));
                newEpoch = epoch;
                carriedEntries = entries;
                droppedUniqueIds = dropped;
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("rebaseline", e); // 任何 I/O 异常都不影响原生保存
                return false;
            }
        }

        /// <summary>
        /// 用户锚定再基线化写路径（设计 C，仅 knight 侧）：面板"应用"已铸出运行时收据并挂 pending，
        /// 本方法在下一次原生 Save 内以 save 形态 JSON 新建 epoch 的 kind2 快照（条目由调用方按本次
        /// 捕获复核后给出，绝不重读/重解析）。旧 epoch/历史记录全保留；容量耗尽/未知版本/损坏主档
        /// 一律 fail-closed（不写盘）。复刻 AppendSnapshot 的单写者纪律（重读磁盘→合并→原子写）。
        /// </summary>
        internal static bool TryUserRebaseline(string contextKey, string rawJson, IReadOnlyList<KnightIdentitySnapshotEntry> entries, out string newEpoch)
        {
            newEpoch = null;
            try
            {
                string path = Path;
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(contextKey) || entries == null || entries.Count == 0)
                {
                    KnightIdentityLog.Once("panel-rebaseline-path", null);
                    return false;
                }
                if (!TryOpenWritableArchive(path, true, out KnightIdentityArchive archive, out string blocked))
                {
                    KnightIdentityLog.Once(blocked, null);
                    return false;
                }
                if (archive == null) return false;

                string epoch = KnightIdentityArchive.NewScope();
                string hash = KnightIdentityFingerprint.Normalized(rawJson, epoch);
                if (!KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hash, DateTimeOffset.UtcNow, entries,
                        out KnightIdentitySnapshot snapshot, out string error))
                {
                    KnightIdentityLog.Once("panel-rebaseline-snapshot:" + error, null);
                    return false;
                }
                if (archive.RecordSnapshot(epoch, snapshot) != KnightIdentityArchive.MutationStatus.Applied)
                {
                    KnightIdentityLog.Once("panel-rebaseline-capacity-scope", null); // 容量耗尽：不牺牲别的 scope
                    return false;
                }
                if (!archive.EnsureContext(contextKey, epoch, true))
                {
                    KnightIdentityLog.Once("panel-rebaseline-capacity-context", null); // MaxContexts/MaxEpochs 耗尽：fail-closed
                    return false;
                }

                string directory = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                KnightIdentityArchiveStore.SaveResult saved = KnightIdentityArchiveStore.Save(path, archive);
                LogRetryOutcome(saved);
                if (!saved.Ok)
                {
                    KnightIdentityLog.Once("panel-rebaseline-save:" + saved.Status, null);
                    return false;
                }
                newEpoch = epoch;
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("panel-rebaseline-write", e);
                return false;
            }
        }

        /// <summary>
        /// save 路径的独立解析（本会话没有该上下文的 load 绑定时）：只读磁盘解析当前 json，
        /// 返回写目标 epoch / 是否新 epoch / 是否 unresolved；任何只读降级返回 false。
        /// </summary>
        internal static bool TryResolveForWrite(string contextKey, string rawJson, out string epoch, out bool newEpoch, out bool unresolved)
        {
            epoch = null;
            newEpoch = false;
            unresolved = true;
            try
            {
                string path = Path;
                if (string.IsNullOrEmpty(path)) return false;
                // 纯解析：不在这里修主档；真正写入时 AppendSnapshot 才按既有流程显式修复。
                if (!TryOpenWritableArchive(path, false, out KnightIdentityArchive archive, out string blocked))
                {
                    KnightIdentityLog.Once(blocked, null);
                    return false;
                }
                if (archive == null) return false;

                KnightIdentityResolution resolution = KnightIdentityContexts.Resolve(archive, contextKey, rawJson);
                epoch = resolution.Epoch;
                newEpoch = resolution.NewEpoch;
                unresolved = resolution.Unresolved || string.IsNullOrEmpty(epoch);
                return true;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("save-context", e);
                return false;
            }
        }

        internal static bool CommitGeneration(string contextKey, string epoch, string rawJson)
        {
            // 只有已证实成功的新生成调用。保留旧 epoch，用空基线原子登记新代，后续正常 Save 写人口。
            if (!TryOpenWritableArchive(Path, true, out KnightIdentityArchive archive, out string blocked))
            { KnightIdentityLog.Once(blocked, null); return false; }
            string hash = KnightIdentityFingerprint.Normalized(rawJson, epoch);
            if (!KnightIdentitySnapshot.TryCreate(KnightIdentityFingerprint.KindNormalized, hash, DateTimeOffset.UtcNow,
                Array.Empty<KnightIdentitySnapshotEntry>(), out KnightIdentitySnapshot snapshot, out _)) return false;
            if (archive.RecordSnapshot(epoch, snapshot) != KnightIdentityArchive.MutationStatus.Applied
                || !archive.EnsureContext(contextKey, epoch, true)) return false;
            string directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            KnightIdentityArchiveStore.SaveResult saved = KnightIdentityArchiveStore.Save(Path, archive);
            LogRetryOutcome(saved);
            return saved.Ok;
        }

        /// <summary>
        /// 打开的归档可写性唯一入口：Missing → 空档可写；Valid → 直接用（<paramref name="recoverBackup"/> 时
        /// 来自备份的数据先显式修复主文件）；Corrupt / UnsupportedVersion → 只读降级（blocked 为一次性日志 key）。
        /// </summary>
        private static bool TryOpenWritableArchive(string path, bool recoverBackup, out KnightIdentityArchive archive, out string blocked)
        {
            archive = null;
            blocked = null;
            KnightIdentityArchiveStore.LoadResult loaded = KnightIdentityArchiveStore.Load(path);
            if (loaded.Status == KnightIdentityArchiveStatus.Missing)
            {
                archive = KnightIdentityArchive.CreateEmpty();
                return true;
            }
            if (loaded.Status == KnightIdentityArchiveStatus.Valid)
            {
                if (loaded.RecoveredBackup && recoverBackup)
                {
                    KnightIdentityArchiveStore.SaveResult recovered = KnightIdentityArchiveStore.RecoverMainFromBackup(path);
                    LogRetryOutcome(recovered);
                    if (!recovered.Ok)
                    {
                        blocked = "sidecar-recover:" + recovered.Status;
                        return false;
                    }
                    KnightIdentityLog.Once("sidecar-recovered-from-backup", null);
                }
                archive = loaded.Archive;
                return archive != null;
            }
            blocked = loaded.Status == KnightIdentityArchiveStatus.Corrupt ? "sidecar-corrupt-readonly" : "sidecar-version-readonly";
            return false;
        }

        /// <summary>写失败重试的统一诊断：发生重试才落日志（恢复=事件级 Info，仍失败=一次性 Warning 附 retry=1）。</summary>
        private static void LogRetryOutcome(KnightIdentityArchiveStore.SaveResult result)
        {
            if (result == null || !result.Retried) return;
            if (result.Ok) KnightIdentityLog.Receipt("write-retry=1 recovered");
            else KnightIdentityLog.Once("write-retry=1 failed", null);
        }

        private static string ShortHash(string value)
        {
            return string.IsNullOrEmpty(value) || value.Length < 8 ? value : value.Substring(0, 8);
        }

        private static string SafeGlobalFilename()
        {
            try
            {
                return GlobalSaveData.filename;
            }
            catch (Exception e)
            {
                KnightIdentityLog.Once("global-filename", e);
                return null;
            }
        }
    }

    /// <summary>IslandSaveData.Save(campaign, land, challenge)：Prefix 建空 scope、Last Postfix 写 sidecar、Finalizer 还原嵌套。</summary>
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    internal static class KnightIdentitySavePatch
    {
        [HarmonyPrefix]
        private static void Before(int __0, int __1, int __2, out KnightIdentitySaveBridge.SaveCapture __state)
        {
            __state = KnightIdentitySaveBridge.BeginCapture(__0, __1, __2);
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPostfix]
        private static void After(KnightIdentitySaveBridge.SaveCapture __state)
        {
            KnightIdentitySaveBridge.ApplyCapture(__state);
        }

        [HarmonyFinalizer]
        private static Exception Finally(Exception __exception, KnightIdentitySaveBridge.SaveCapture __state)
        {
            return KnightIdentitySaveBridge.EndCapture(__exception, __state);
        }
    }

    /// <summary>IslandSaveData.GetID(Persistent)：只在 Save 上下文里捕获实际岛并登记 uniqueID→owner+life+收据。</summary>
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.GetID), new[] { typeof(Persistent) })]
    internal static class KnightIdentityGetIdPatch
    {
        [HarmonyPostfix]
        private static void AfterGetId(Persistent __0, string __result)
        {
            KnightIdentitySaveBridge.HandleGetId(__0, __result);
        }
    }

    /// <summary>IslandSaveData.TryPopObjectsToScene()：Prefix 拍 JSON/上下文并载入收据，Finalizer 还原嵌套作用域。</summary>
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryPopObjectsToScene))]
    internal static class KnightIdentityTryPopPatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        private static void Before(IslandSaveData __instance, out KnightIdentityLoadBridge.LoadScope __state)
        {
            __state = KnightIdentityLoadBridge.Begin(__instance);
        }

        [HarmonyFinalizer]
        private static Exception Finally(Exception __exception, KnightIdentityLoadBridge.LoadScope __state, bool __result)
        {
            return KnightIdentityLoadBridge.End(__exception, __state, __result);
        }
    }

    /// <summary>IslandSaveData.TryCreateOrFind(ObjectData)：Postfix 用当前 LoadScope 快照把收据绑到实际 root，不改 native 参数。</summary>
    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.TryCreateOrFind))]
    internal static class KnightIdentityTryCreateOrFindPatch
    {
        [HarmonyPostfix]
        private static void AfterTryCreateOrFind(IslandSaveData.ObjectData __0, Persistent __result)
        {
            KnightIdentityLoadBridge.HandleTryCreateOrFind(__0, __result);
        }
    }
}
