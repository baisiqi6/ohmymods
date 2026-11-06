#if !ANDROID
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

// Issue281 用户裁定的简化策略（no-boarding-design，2026-10-25）：已付费火枪手不上船、留守原岛；
// 异常已登船者在源岛保存捕获前转换为普通弓兵（到岸即普通，无运输身份恢复）。不新增退款/赠枪/再招募
// 或运输 schema，不改弩手。
//
// 三个原生入口（Actual 2.4 三者均为唯一真实长体，见 transport-contract native-boundary 证据）：
//  * Archer.get_CanEmbark postfix：只读资格门——Registrar 内联 Embarkee 包装但虚调用 owner 资格，
//    令已确认付费火枪手返回 false；普通/弩手/其他单位沿原生结果。
//  * Embarkee.SetEmbarkableTarget prefix：非空登船目标对已知付费火枪手拒绝；null 是原生清理，放行
//    （Knight.__EmbarkArchers_d__248.MoveNext 直接 SetEmbarkableTarget(-1) 绕过 CanEmbark）。
//  * Embarkee.Embark prefix：已知付费火枪手禁止；postfix 兜底——原生确实完成登船（IsEmbarked=true）
//    时用与源保存前完全相同的证明和转换 helper 立即降级。
//
// 旧状态兼容：IslandSaveData.Save HarmonyPrefix(Priority.First) 先于现有 SaveCapture.Begin（默认
// 优先级）执行；仅对明确 land>=0 且 (campaign,challenge,land) 解析出的 context 与 Current 一致、
// 世界一致、状态可读的当前源，用有限 Careers 副本（<=MaxRecords）逐个经同一 proof 处理已确认
// embarked 的单位。-1/非法/背景/foreign/不可读源不猜测、不转换，原 MissingRow Guard 保持。
//
// 身份证明（gate 与转换共用同一实现，防判据漂移）：offline + 当前世界 authority、current ready
// 状态、BoundAt/ValidBinding 同 career 且 kind=unit、same root/goId/life/world、exact Archer 指针、
// actor._embarkee 同根、embarkee._owner 经 TryCast 的实际 Component 同 actor/根（Hero borrow 同形式）、
// root 活跃且与 world layer 同 scene。不用 MusketeerIdentity.IsUnit：它要求 root 仍在 world
// gameLayer 之下（MusketeerAccess.InWorld），已登船单位被 reparent 到船下——不得让已挂船者借层假设
// 逃脱本策略；也不因父级变化丢 identity。一切以指针/实例 id 证明，绝不凭同 wrapper 引用相等。
// 未确认 → 返回原生结果、不转换。
internal static class MusketeerBoardingPolicy
{
    // ---- shared proof（gate 与转换唯一实现）----

    internal static Archer ActorOf(Embarkee embarkee)
    {
        try { return embarkee?._owner?.TryCast<Archer>(); }
        catch { return null; }
    }

    internal static bool TryProveKnownUnit(Archer actor, out MusketeerIdentity.Career career, out Embarkee embarkee)
    {
        career = null;
        embarkee = null;
        try
        {
            if (!MusketeerAccess.TrackAllowed) return false;   // offline + 当前世界 authority
            var state = MusketeerIdentity.Current;
            if (state == null || !state.Ready) return false;
            if (state.ReadOnly || state.Unresolved || state.GenerationPending) return false;
            if (actor == null) return false;
            var root = actor.gameObject;
            if (root == null) return false;
            // world 一致：绑定世界必须是当前活跃的 world layer
            long world = MusketeerIdentity.WorldKey();
            if (world == 0 || world != state.World) return false;
            // career：同 state、ValidBinding（指针+life+goId+world）、付费单位 kind
            var candidate = MusketeerIdentity.BoundAt(state, root);
            if (candidate == null || candidate.Kind != MusketeerCareer.KindUnit) return false;
            // exact Archer 组件指针；同 root/实例 id（不凭 wrapper 引用相等）
            if (candidate.Archer == null || candidate.Archer.Pointer != actor.Pointer) return false;
            if (candidate.Slot == null || candidate.Slot.Pointer != root.Pointer) return false;
            if (root.GetInstanceID() != candidate.GoId) return false;
            // 该 root 自己的 embarkee
            var unitEmbarkee = actor._embarkee;
            if (unitEmbarkee == null || unitEmbarkee.gameObject == null) return false;
            if (unitEmbarkee.gameObject.Pointer != root.Pointer) return false;
            // native owner 是这个 actor（Hero borrow 检查同款 TryCast 形式）
            var owner = unitEmbarkee._owner?.TryCast<UnityEngine.Component>();
            if (owner == null || owner.Pointer != actor.Pointer) return false;
            if (owner.gameObject == null || owner.gameObject.Pointer != root.Pointer) return false;
            // active 且在当前 scene（同 scene 即可，不要求仍在 world gameLayer 之下：
            // 已登船单位被 reparent 到船下，层假设不得让已挂船者逃脱本策略）
            var layer = MusketeerAccess.World;
            if (layer == null || layer.gameObject == null) return false;
            if (!root.activeInHierarchy) return false;
            if (root.scene.handle != layer.gameObject.scene.handle) return false;
            career = candidate;
            embarkee = unitEmbarkee;
            return true;
        }
        catch { career = null; embarkee = null; return false; }
    }

    // ---- gates ----

    internal static void RefineCanEmbark(Archer actor, ref bool result)
    {
        try
        {
            if (!result) return;   // 原生已拒绝：只收紧、不放宽
            if (TryProveKnownUnit(actor, out _, out _))
            {
                MusketeerIdentity.Log("boarding-gate:canembark", null);
                result = false;
            }
        }
        catch (Exception e) { MusketeerIdentity.Log("boarding-canembark", e); }
    }

    // 输入组件精确核对（policy-delta 缺陷 1 修复）：目标 gate、Embark gate 与 Embark postfix
    // 兜底共用。proof 只证明「actor 拥有某个已知 embarkee」；传入组件必须就是该 actor 自己的
    // embarkee（指针精确比对，不凭 wrapper 引用；同 root 的第二 Embarkee 也不能冒名），
    // 否则 foreign _owner 别名会借 actor 自身 embarkee 误封原生调用。
    internal static bool TryProveEmbarkee(Embarkee embarkee, out MusketeerIdentity.Career career, out Archer actor)
    {
        career = null;
        actor = null;
        try
        {
            if (embarkee == null) return false;
            var candidateActor = ActorOf(embarkee);
            if (candidateActor == null) return false;
            if (!TryProveKnownUnit(candidateActor, out var unitCareer, out var ownEmbarkee)) return false;
            if (ownEmbarkee.Pointer != embarkee.Pointer) return false;
            career = unitCareer;
            actor = candidateActor;
            return true;
        }
        catch { return false; }
    }

    internal static bool AllowSetEmbarkableTarget(Embarkee embarkee, Embarkable target)
    {
        try
        {
            if (target == null) return true;   // null 是原生清理：保持可用
            if (!TryProveEmbarkee(embarkee, out _, out _)) return true;
            MusketeerIdentity.Log("boarding-gate:target", null);
            return false;
        }
        catch (Exception e) { MusketeerIdentity.Log("boarding-target-gate", e); return true; }
    }

    internal static bool AllowEmbark(Embarkee embarkee)
    {
        try
        {
            if (!TryProveEmbarkee(embarkee, out _, out _)) return true;
            MusketeerIdentity.Log("boarding-gate:embark", null);
            return false;
        }
        catch (Exception e) { MusketeerIdentity.Log("boarding-embark-gate", e); return true; }
    }

    internal static void AfterEmbark(Embarkee embarkee)
    {
        // prefix 被绕过（补丁顺序/原生重入）时的兜底：只有输入组件恰为被证明的 own embarkee
        // 且确已 IsEmbarked 才转换，同一 proof/helper。
        try
        {
            if (!TryProveEmbarkee(embarkee, out _, out var actor)) return;
            if (!embarkee.IsEmbarked) return;
            TryDemoteConfirmedEmbarked(actor);
        }
        catch (Exception e) { MusketeerIdentity.Log("boarding-embark-after", e); }
    }

    // ---- demotion（用户已授权的异常转换）----

    internal static bool TryDemoteConfirmedEmbarked(Archer actor)
    {
        try
        {
            if (!TryProveKnownUnit(actor, out var career, out var embarkee)) return false;
            if (!embarkee.IsEmbarked) return false;
            // B 前冻结（policy-delta 缺陷 2 修复）：career/RootSlot 是可变对象，ReferenceEquals
            // 不是同一 life 的见证。冻结 exact state 引用、Id、life、goId、root/world、own
            // embarkee 指针与 context/epoch；预检读取异常按未知中止（B 尚未调用，中止干净）。
            var state = career.State;
            var slot = career.Slot;
            if (state == null || slot == null) return false;
            Guid id = career.Id;
            long life = career.Life;
            int goId = career.GoId;
            IntPtr rootPointer = slot.Pointer;
            long world = state.World;
            string contextKey = state.ContextKey;
            string epoch = state.Epoch;
            IntPtr actorPointer = actor.Pointer;
            IntPtr embarkeePointer = embarkee.Pointer;
            if (!MusketeerRuntime.TryPrepareBoardingDemotion(actor))
            {
                // 归还不完整：保留身份与原保存保护，不 remove、不跳 Guard；B 已还的字段交既有
                // Runtime Tick 按保留身份重装（不补写 native 状态、不造钱）。
                MusketeerIdentity.Log("boarding-demote-withheld", null);
                return false;
            }
            // B 后复核：重走指针精确 proof（当场重读 actor._embarkee，禁用冻结 wrapper 的缓存
            // 字段），同 career（纯 managed 记录用引用相等），冻结值逐项一致，own embarkee 仍
            // 为同一组件且当场重读 IsEmbarked 仍 true，才 remove/release。任一 false/读取异常
            // 都不退职业、不绕 SaveGuard。
            if (!TryProveKnownUnit(actor, out var after, out var afterEmbarkee)) return false;
            if (!ReferenceEquals(after, career) || after.State != state) return false;
            if (after.Id != id || after.Life != life || after.GoId != goId) return false;
            if (after.Slot == null || after.Slot.Pointer != rootPointer) return false;
            if (state.World != world || state.ContextKey != contextKey || state.Epoch != epoch) return false;
            if (actor.Pointer != actorPointer) return false;
            if (afterEmbarkee.Pointer != embarkeePointer) return false;
            if (!afterEmbarkee.IsEmbarked) return false;
            if (!state.Careers.Contains(career)) return false;
            state.Careers.Remove(career);
            MusketeerIdentity.Release(career);   // 含既有 CareerChanged 通知；非 Reserved、不造新 GUID
            MusketeerIdentity.Log("boarding-demoted", null);
            return true;
        }
        catch (Exception e) { MusketeerIdentity.Log("boarding-demote-fault", e); return false; }
    }

    internal static void DemoteConfirmedEmbarkedBeforeSave(int campaign, int land, int challenge)
    {
        try
        {
            if (land < 0) return;   // 仅明确源岛；-1/非法不猜
            var state = MusketeerIdentity.Current;
            if (state == null || !state.Ready) return;
            if (!MusketeerIdentity.TryContextKey(campaign, challenge, land, out string key)) return;
            if (!string.Equals(key, state.ContextKey, StringComparison.Ordinal)) return;   // 背景/foreign 源
            long world = MusketeerIdentity.WorldKey();
            if (world == 0 || world != state.World) return;
            // 有限副本（<=MaxRecords）：转换会改 Careers，副本保持遍历稳定
            int count = Math.Min(state.Careers.Count, MusketeerIdentity.MaxRecords);
            if (count <= 0) return;
            var snapshot = new List<MusketeerIdentity.Career>(count);
            for (int i = 0; i < count; i++) snapshot.Add(state.Careers[i]);
            foreach (var career in snapshot)
            {
                if (career == null || career.Kind != MusketeerCareer.KindUnit) continue;
                var actor = career.Archer;
                if (actor == null) continue;
                TryDemoteConfirmedEmbarked(actor);   // proof 不明 → 原样保留（ReadOnly/Unresolved/
                                                      // GenerationPending 等由同一 proof 拒绝）
            }
        }
        catch (Exception e) { MusketeerIdentity.Log("boarding-save-demote", e); }
    }

    // ---- harmony ----

    [HarmonyPatch(typeof(Archer), "get_CanEmbark")]
    private static class CanEmbarkPatch
    {
        [HarmonyPostfix]
        private static void After(Archer __instance, ref bool __result)
            => RefineCanEmbark(__instance, ref __result);
    }

    [HarmonyPatch(typeof(Embarkee), nameof(Embarkee.SetEmbarkableTarget),
        new[] { typeof(Embarkable), typeof(int) })]
    private static class TargetPatch
    {
        [HarmonyPrefix]
        private static bool Before(Embarkee __instance, Embarkable __0)
            => AllowSetEmbarkableTarget(__instance, __0);
    }

    [HarmonyPatch(typeof(Embarkee), nameof(Embarkee.Embark), new System.Type[0])]
    private static class EmbarkPatch
    {
        [HarmonyPrefix]
        private static bool Before(Embarkee __instance)
            => AllowEmbark(__instance);

        [HarmonyPostfix]
        private static void After(Embarkee __instance)
            => AfterEmbark(__instance);
    }

    [HarmonyPatch(typeof(IslandSaveData), nameof(IslandSaveData.Save), new[] { typeof(int), typeof(int), typeof(int) })]
    private static class DemoteBeforeSavePatch
    {
        // Priority.First：先于现有 SaveCapture.Begin（默认优先级）完成转换，出航保存的 GetID
        // 行捕获开始前角色已结束，随船走的是普通弓兵，不再触发 missing-row Guard。
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Before(int __0, int __1, int __2)
            => DemoteConfirmedEmbarkedBeforeSave(__0, __1, __2);
    }
}
#endif
