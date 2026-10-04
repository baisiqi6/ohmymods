using System;
using System.Collections.Generic;
#if ANDROID
using Il2CppCoatsink.Common;
#else
using Coatsink.Common;
#endif
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

// Four paid, exact island careers at most. No scene enumeration or replacement character creation.
internal static class HeavyShieldRuntime
{
    internal enum AttachResult { Deferred, Attached, Failed }
    internal sealed class Entry
    {
        internal Archer Carrier;
        internal Character Character;
        internal Damageable Damage;
        internal Mover Mover;
        internal Embarkee Embarkee;
        internal TargetCacher Targets;
        internal HeavyShieldCareerHandle Career;
        internal HeavyShieldBlockPolicy Policy = new();
        internal bool EnabledBefore, EnabledWrite, EmbarkBefore, EmbarkWrite, TargetOwned, Attached;
        internal bool Demoting, ExitConfirmed;
        internal HeavyShieldCampaignToken DemoteCampaign;
        internal CombatTargetToken DemoteLife;
        internal int Facing;
        internal float NextScan;
        internal MoverFields MoveBefore, MoveWrite;
        internal bool MoveOwned;
        internal NativeFields NativeBefore, NativeWrite;
        internal bool NativeOwned;
        internal CombatTargetToken TargetLife;
        internal Il2CppReferenceArray<GameObject> Enemies;
    }
    internal readonly struct MoverFields
    {
        internal readonly Mover.GoalMode Mode;
        internal readonly float Position, Speed, Offset;
        internal readonly GameObject Object, FacingTarget;
        internal readonly Mover.OffsetMode OffsetMode;
        internal readonly Mover.FacingMode FacingMode;
        internal MoverFields(Mover m)
        { Mode = m.goalMode; Position = m._goalPosition; Speed = m._goalSpeed; Offset = m._goalOffset;
          Object = m._goalObject; OffsetMode = m._goalOffsetMode; FacingMode = m.facingMode; FacingTarget = m.facingTarget; }
        internal bool Matches(Mover m) => Mode == m.goalMode && Position == m._goalPosition && Speed == m._goalSpeed
            && Offset == m._goalOffset && Ptr(Object) == Ptr(m._goalObject) && OffsetMode == m._goalOffsetMode
            && FacingMode == m.facingMode && Ptr(FacingTarget) == Ptr(m.facingTarget);
        internal void Restore(Mover m)
        { m._goalPosition = Position; m._goalSpeed = Speed; m._goalOffset = Offset; m._goalObject = Object;
          m._goalOffsetMode = OffsetMode; m.goalMode = Mode; m.facingMode = FacingMode; m.facingTarget = FacingTarget; }
    }
    internal readonly struct NativeFields
    {
        internal readonly Side Side;
        internal readonly int Depth, Face;
        internal readonly Archer.AttackMode Desired, Mode;
        internal readonly ArrowAttack Attack;
        internal readonly float Cooldown;
        internal readonly RuntimeAnimatorController Animator;
        internal readonly Color Outfit, Secondary;
        internal NativeFields(Archer a)
        { Side = a._guardSide; Depth = a._guardDepth; Face = a._absoluteFaceIndex; Desired = a._desiredAttackMode;
          Mode = a._attackMode; Attack = a.ActiveArrowAttack; Cooldown = a._cooldown;
          Animator = a._animator.runtimeAnimatorController; Outfit = a._character.outfitColor;
          Secondary = a._character.outfitSecondaryColor; }
        internal void Restore(Archer a, in NativeFields written)
        {
            if (a._guardSide == written.Side) a._guardSide = Side;
            if (a._guardDepth == written.Depth) a._guardDepth = Depth;
            if (a._absoluteFaceIndex == written.Face) a._absoluteFaceIndex = Face;
            if (a._desiredAttackMode == written.Desired) a._desiredAttackMode = Desired;
            if (a._attackMode == written.Mode) a._attackMode = Mode;
            if (Ptr(a.ActiveArrowAttack) == Ptr(written.Attack)) a.ActiveArrowAttack = Attack;
            if (a._cooldown == written.Cooldown) a._cooldown = Cooldown;
            if (Ptr(a._animator.runtimeAnimatorController) == Ptr(written.Animator)) a._animator.runtimeAnimatorController = Animator;
            if (a._character.outfitColor == written.Outfit) a._character.outfitColor = Outfit;
            if (a._character.outfitSecondaryColor == written.Secondary) a._character.outfitSecondaryColor = Secondary;
        }
    }
    private static readonly Dictionary<int, Entry> Entries = new(4);
    private static readonly List<Entry> Scratch = new(4);
    private static bool _logged;
    [ThreadStatic] private static int _carrierMutationDepth;
    [ThreadStatic] private static HeavyShieldIngressStack<NativeDemoteCapture> _nativeDemotes;
    private static HeavyShieldIngressStack<NativeDemoteCapture> NativeDemotes => _nativeDemotes ??= new();
    internal sealed class NativeDemoteCapture : IDisposable
    {
        internal Entry Entry;
        internal Character Source;
        internal HeavyShieldCareerHandle Career;
        internal HeavyShieldCampaignToken Campaign;
        internal CombatTargetToken Life;
        internal bool Active, Confirmed, Closed;
        internal IDisposable Scope;
        public void Dispose()
        {
            if (Closed) return;
            Closed = true;
            Scope?.Dispose(); // the unknown path runs only after the per-call mask has been returned
            if (Active && !Confirmed)
            {
                try { if (OriginalSourceLife(Entry, Life, out _)) ObserveNativeDeath(Entry.Damage); } catch { }
                if (!Entry.ExitConfirmed) HoldNativeDemoteUnknown(Entry);
            }
        }
    }
    internal static bool CarrierMutationInProgress => _carrierMutationDepth > 0;
    private static void MutateCarrier(Action mutation)
    { ++_carrierMutationDepth; try { mutation(); } finally { --_carrierMutationDepth; } }
    internal static bool Enabled
    {
        get
        {
            try { return ModConfig.Enabled?.Value == true && ModConfig.HeavyShieldEnabled?.Value == true
                && !NetworkBigBoss.IsOnline && NetworkBigBoss.HasWorldAuth && ProgramDirector.IsMainSceneActive()
                && Managers.Inst?.world?.gameLayer != null; }
            catch { return false; }
        }
    }
    internal static bool Playing
    {
        get { try { return Enabled && Time.timeScale > 0f && Managers.Inst.game.state == Game.State.Playing; } catch { return false; } }
    }
    // Native contract checks are necessary, while live suspension is additionally verified on each carrier.
    internal static bool CarrierPreflightReady
    {
        get
        {
            try
            {
                if (!Playing || Entries.Count >= 64 || !HeavyShieldNativeHooks.Installed || Managers.Inst.targetCache == null
                    || !CombatTargetLife.EnsureRegistered() || !HeavyShieldArt.TryGetSoldierSprite(0, out var sprite) || sprite == null) return false;
                long world = Managers.Inst.world.gameLayer.Pointer.ToInt64();
                foreach (var e in Entries.Values)
                    if (e.Career.World == world && (!e.Attached || !SameRoot(e) || !HeavyShieldIdentity.ValidateCareer(e.Career))) return false;
                return true;
            }
            catch { return false; }
        }
    }
    internal static bool IsControlled(Archer carrier)
    {
        try { return carrier != null && Entries.TryGetValue(carrier.gameObject.GetInstanceID(), out var e)
                && SameRoot(e) && HeavyShieldIdentity.ValidateCareer(e.Career) && Ptr(e.Carrier) == Ptr(carrier) && !e.ExitConfirmed; }
        catch { return false; }
    }
    // Independent native marker proof. This lookup never initializes a marker or takes a life number.
    internal static bool HasNativeSourceLife(Archer carrier, long careerLife, bool allowInactive)
    {
        try
        {
            return carrier != null && carrier.gameObject != null && careerLife > 0
                && Entries.TryGetValue(carrier.gameObject.GetInstanceID(), out var e)
                && Ptr(e.Carrier) == Ptr(carrier) && e.Career.Life == careerLife && SameRoot(e, allowInactive)
                && HeavyShieldIdentity.ValidateCareer(e.Career)
                && OriginalSourceLife(e, e.TargetLife, out bool ended) && (allowInactive || !ended);
        }
        catch { return false; }
    }
    // Read-only activation proof for A's deferred attach/reserve gate. Ownership remains IsControlled.
    internal static bool IsCarrierActive(in HeavyShieldCareerHandle handle)
    {
        try
        {
            if (!Playing || !TryEntry(handle, out var e) || !HeavyShieldIdentity.ValidateCareer(handle)
                || !e.Attached || e.ExitConfirmed || e.Demoting || e.Policy.RetirementUnknown || e.Policy.PendingBreak
                || e.Carrier.enabled || !CleanCarrier(e.Carrier)
                || (e.Embarkee != null && e.Embarkee.enabled) || HoldsNativeDemoteProof(e.Carrier.gameObject)
                || !Stopped(e.Carrier.behaviour) || !Stopped(e.Carrier.shoot) || !Stopped(e.Carrier.attack)
                || !HasNativeSourceLife(e.Carrier, handle.Life, false)
                || !HeavyShieldActorVisuals.HasQualifiedVisual(e.Carrier, handle.Life)) return false;
            return true;
        }
        catch { return false; }
    }
    internal static bool TryGetCareer(Damageable damage, out HeavyShieldCareerHandle handle)
    {
        handle = default;
        try
        {
            if (damage == null || !Entries.TryGetValue(damage.gameObject.GetInstanceID(), out var e)
                || !SameRoot(e) || Ptr(damage) != Ptr(e.Damage)
                || !HasNativeSourceLife(e.Carrier, e.Career.Life, false)) return false;
            handle = e.Career; return true;
        }
        catch { return false; }
    }
    internal static bool CaptureCombat(in HeavyShieldCareerHandle handle, out HeavyShieldSavedCombatState saved)
    {
        saved = default;
        if (!TryEntry(handle, out var e)) return false;
        saved = new(e.Policy.Durability, e.Policy.PendingBreak, e.Policy.RetirementUnknown);
        return true;
    }
    internal static bool AttachCarrier(Archer carrier, in HeavyShieldCareerHandle handle, in HeavyShieldSavedCombatState restored)
        => TryAttachCarrier(carrier, handle, restored) == AttachResult.Attached;
    internal static AttachResult TryAttachCarrier(Archer carrier, in HeavyShieldCareerHandle handle, in HeavyShieldSavedCombatState restored)
    {
        bool attempted = false;
        try
        {
            // All Deferred exits precede marker/resource registration and native writes, and create no Entry.
            if (carrier == null || carrier.gameObject == null || !HeavyShieldIdentity.ValidateCareer(handle)
                || !HeavyShieldIdentity.TryGetSoldier(carrier, out var career) || career != handle
                || carrier.gameObject.Pointer != handle.Root || carrier.gameObject.GetInstanceID() != handle.GoId
                || !CarrierComponentsValid(carrier) || carrier._damageable.isDead) return AttachResult.Failed;
            if (!Playing) return AttachResult.Deferred;
            if (!InWorld(carrier.gameObject)) return AttachResult.Failed;
            if (Entries.TryGetValue(handle.GoId, out var existing))
            {
                if (existing.Career != handle || !SameRoot(existing) || !existing.Attached) return AttachResult.Failed;
                if (!CleanCarrier(carrier)) return AttachResult.Deferred;
                // Ownership is not activation: do not reborrow or re-register an unqualified owned visual.
                return !carrier.enabled && Stopped(carrier.behaviour) && Stopped(carrier.shoot) && Stopped(carrier.attack)
                    && HeavyShieldActorVisuals.HasQualifiedVisual(carrier, handle.Life) ? AttachResult.Attached : AttachResult.Failed;
            }
            int currentCount = 0;
            foreach (var row in Entries.Values) if (row.Career.World == handle.World) ++currentCount;
            if (currentCount >= 4 || Entries.Count >= 64) return AttachResult.Failed;
            // Only native tower/knight/formation membership is relaxed. OnDisable, not field writes, exits these jobs.
            if (!carrier.enabled || !CleanCarrier(carrier, allowNativeJobs: true)
                || !carrier._spriteRenderer.enabled || carrier._spriteRenderer.forceRenderingOff) return AttachResult.Deferred;
            if (!HeavyShieldNativeHooks.Installed || Managers.Inst.targetCache == null) return AttachResult.Deferred;
            foreach (var row in Entries.Values)
                if (row.Career.World == handle.World && (!row.Attached || !SameRoot(row)
                    || !HeavyShieldIdentity.ValidateCareer(row.Career))) return AttachResult.Deferred;
            var policy = new HeavyShieldBlockPolicy();
            if (!policy.Restore(restored.Durability, restored.PendingBreak, restored.RetirementUnknown)) return AttachResult.Failed;
            attempted = true; // registration/assets can mutate native state; there are no Deferred exits beyond this point
            if (!CarrierPreflightReady) return AttachResult.Failed;
            var e = new Entry { Carrier = carrier, Character = carrier._character, Damage = carrier._damageable,
                Mover = carrier._mover, Embarkee = carrier.Embarkee, Targets = Managers.Inst.targetCache,
                Career = handle, Policy = policy, Facing = handle.Side == HeavyShieldQuota.Side.Left ? -1 : 1 };
            if (!CombatTargetLife.TryResolve(e.Damage, out e.TargetLife)) return AttachResult.Failed;
            e.EnabledBefore = carrier.enabled;
            e.EmbarkBefore = e.Embarkee != null && e.Embarkee.enabled;
            e.MoveBefore = new(e.Mover); e.NativeBefore = new(carrier);
            Entries.Add(handle.GoId, e); // fence precedes native OnDisable + mod fanout
            e.EnabledWrite = true;
            try { MutateCarrier(() => carrier.enabled = false); }
            finally
            {
                e.NativeWrite = new(carrier); e.NativeOwned = true;
                e.MoveWrite = new(e.Mover); e.MoveOwned = true;
            }
            if (!SameRoot(e) || !HeavyShieldIdentity.ValidateCareer(handle)
                || !OriginalSourceLife(e, e.TargetLife, out bool ended) || ended)
                throw new InvalidOperationException("carrier life changed during suspension");
            if (e.Embarkee != null && e.EmbarkBefore) { e.EmbarkWrite = true; MutateCarrier(() => e.Embarkee.enabled = false); }
            if (!Playing || carrier.enabled || !Stopped(carrier.behaviour) || !Stopped(carrier.shoot) || !Stopped(carrier.attack)
                || !CleanCarrier(carrier) || !SameRoot(e) || !HeavyShieldIdentity.ValidateCareer(handle)
                || !OriginalSourceLife(e, e.TargetLife, out ended) || ended) throw new InvalidOperationException("native suspension unconfirmed");
            e.TargetOwned = true; e.Targets.RegisterPriorityTarget(e.Damage);
            if (!HasPriority(e)) throw new InvalidOperationException("priority target registration unconfirmed");
            if (!HeavyShieldActorVisuals.Register(carrier, handle.Life)) throw new InvalidOperationException("visual registration unconfirmed");
            HeavyShieldActorVisuals.SetFacing(carrier, handle.Life, e.Facing);
            Wear(e);
            if (e.Policy.PendingBreak) HeavyShieldActorVisuals.Trigger(carrier, handle.Life, HeavyShieldAction.Break);
            if (!Playing || !HeavyShieldActorVisuals.HasQualifiedVisual(carrier, handle.Life)) throw new InvalidOperationException("initial visual unqualified");
            e.Attached = true;
            if (!Persist(e)) throw new InvalidOperationException("combat state receipt rejected");
            return AttachResult.Attached;
        }
        catch (Exception ex)
        {
            Log("attach held: " + ex.GetType().Name);
            if (attempted) DetachCarrier(handle); // failed CAS/identity leaves the only credential in Entries
            return AttachResult.Failed;
        }
    }
    internal static bool DetachCarrier(in HeavyShieldCareerHandle handle)
    {
        if (!Entries.TryGetValue(handle.GoId, out var e)) return true;
        if (e.Career != handle) return false;
        if (!ReturnBorrowed(e, true)) return false; // ended original life is safe only with its exact managed stamp
        Entries.Remove(handle.GoId); return true;
    }
    private static bool ReturnBorrowed(Entry e, bool allowInactive = false)
    {
        var handle = e.Career;
        try
        {
            e.Attached = false; e.Policy.EndBash(); Persist(e);
            if (!HasNativeSourceLife(e.Carrier, handle.Life, allowInactive))
            { HeavyShieldActorVisuals.Suspend(e.Carrier, handle.Life); return false; }
            if (HeavyShieldActorVisuals.HasVisual(e.Carrier, handle.Life)
                && !HeavyShieldActorVisuals.Unregister(e.Carrier, handle.Life)) return false;
            if (e.EnabledWrite && !e.NativeOwned) return false; // partial suspension with unreadable fields retains its only snapshot
            if (e.TargetOwned)
            {
                e.Targets.DeregisterPriorityTarget(e.Damage);
                if (HasPriority(e)) return false;
                e.TargetOwned = false;
            }
            if (e.MoveOwned)
            {
                if (e.MoveWrite.Matches(e.Mover)) e.MoveBefore.Restore(e.Mover);
                e.MoveOwned = false;
            }
            if (e.NativeOwned) { e.NativeBefore.Restore(e.Carrier, e.NativeWrite); e.NativeOwned = false; }
            // Enable resumes by native Reset+Start, after our target registration has been returned.
            if (e.EmbarkWrite && e.Embarkee != null && !e.Embarkee.enabled)
            { MutateCarrier(() => e.Embarkee.enabled = e.EmbarkBefore); e.EmbarkWrite = false; }
            if (e.EnabledWrite && !e.Carrier.enabled)
            { MutateCarrier(() => e.Carrier.enabled = e.EnabledBefore); e.EnabledWrite = false; }
            return true;
        }
        catch (Exception ex) { Log("detach pending: " + ex.GetType().Name); return false; }
    }
    internal static void Tick()
    {
        Scratch.Clear(); Scratch.AddRange(Entries.Values);
        for (int i = 0; i < Scratch.Count; i++)
        {
            var e = Scratch[i];
            try
            {
                if (!Enabled) { DetachCarrier(e.Career); continue; }
                if (!HasNativeSourceLife(e.Carrier, e.Career.Life, false))
                { e.Attached = false; HeavyShieldActorVisuals.Suspend(e.Carrier, e.Career.Life); continue; }
                if (e.Damage.isDead) { ObserveNativeDeath(e.Damage); continue; }
                if (!Playing || !e.Attached) continue;
                if (e.Carrier.enabled || !Stopped(e.Carrier.behaviour) || !CleanCarrier(e.Carrier)) { DetachCarrier(e.Career); continue; }
                if (!HeavyShieldActorVisuals.HasQualifiedVisual(e.Carrier, e.Career.Life))
                {
                    e.Attached = false; e.Policy.EndBash();
                    if (e.MoveOwned && e.MoveWrite.Matches(e.Mover)) Move(e, e.Carrier.transform.position.x, 0f);
                    HeavyShieldActorVisuals.Suspend(e.Carrier, e.Career.Life); continue;
                }
                if (e.Policy.PendingBreak) { Retire(e); continue; }
                Drive(e);
            }
            catch (Exception ex) { Log("driver held: " + ex.GetType().Name); e.Attached = false; }
        }
    }
    private static void Drive(Entry e)
    {
        float now = Time.time, x = e.Carrier.transform.position.x;
        var anchor = Managers.Inst.kingdom.GetGuardPosition(e.Facing < 0 ? Side.Left : Side.Right);
        if (!float.IsFinite(anchor.Value)) return;
        int seat = 0;
        foreach (var other in Entries.Values)
            if (other.Attached && other.Career.Side == e.Career.Side && other.Career.GoId < e.Career.GoId
                && other.Career.World == e.Career.World && other.Career.Land == e.Career.Land
                && other.Career.CampaignGuid == e.Career.CampaignGuid && SameRoot(other)
                && HeavyShieldIdentity.ValidateCareer(other.Career)) ++seat;
        float home = anchor.Value - e.Facing * (0.25f + seat * 0.5f);
        GameObject closest = null;
        if (now >= e.NextScan)
        {
            e.NextScan = now + 0.2f;
            // Native scanner is local, bounded and reused; it retains its native predicate/filter.
            var scanner = e.Carrier._enemyScanner;
            scanner.Refresh(true); scanner.GetAll(out e.Enemies);
        }
        float nearest = 3f;
        int count = Math.Min(e.Enemies?.Length ?? 0, 32);
        for (int i = 0; i < count; i++)
        {
            GameObject target = e.Enemies[i];
            if (!IsOrdinaryEnemy(target, false)) continue;
            float dx = (target.transform.position.x - x) * e.Facing;
            if (dx > 0f && dx < nearest && Math.Abs(target.transform.position.y - e.Carrier.transform.position.y) < 1.5f)
            { closest = target; nearest = dx; }
        }
        bool guard = closest != null || Math.Abs(home - x) < 0.15f;
        bool motionKnown = TryActualMotion(e, out HeavyShieldStance backMotion);
        bool actuallyMoving = motionKnown && backMotion != HeavyShieldStance.BackIdle;
        if (closest != null || actuallyMoving || !motionKnown)
            HeavyShieldActorVisuals.InterruptLeisure(e.Carrier, e.Career.Life);
        HeavyShieldActorVisuals.TryPose(e.Carrier, e.Career.Life, out var action, out float elapsed);
        if (action == HeavyShieldAction.Bash)
        {
            Move(e, x, 0f);
            if (elapsed >= 3f / 12f && e.Policy.TryClaimBashImpact()) BashImpact(e);
            return;
        }
        if (e.Policy.BashActive) e.Policy.EndBash();
        bool ready = HeavyShieldActorVisuals.GuardReady(e.Carrier, e.Career.Life);
        bool presentation = HeavyShieldPoseMachine.IsPresentationAction(action);
        if (presentation)
        {
            if (guard && !ready) HeavyShieldActorVisuals.Trigger(e.Carrier, e.Career.Life, HeavyShieldAction.Equip);
            else if (!guard && ready) HeavyShieldActorVisuals.Trigger(e.Carrier, e.Career.Life, HeavyShieldAction.Stow);
        }
        if (closest != null && nearest <= 0.9f && ready && presentation && e.Policy.TryBeginBash(now))
        {
            if (!HeavyShieldActorVisuals.Trigger(e.Carrier, e.Career.Life, HeavyShieldAction.Bash)) e.Policy.EndBash();
            Move(e, x, 0); return;
        }
        float goal = home;
        if (closest != null && ready)
            goal = Math.Clamp(closest.transform.position.x - e.Facing * 0.7f, home - 1f, home + 1f);
        bool moving = Math.Abs(goal - x) > 0.1f;
        HeavyShieldActorVisuals.SetPresentation(e.Carrier, e.Career.Life,
            guard ? (actuallyMoving && ready ? HeavyShieldStance.Advance : HeavyShieldStance.GuardIdle) : backMotion,
            closest != null, motionKnown);
        Move(e, goal, moving ? Math.Max(0.1f, e.Carrier.walkSpeed) : 0f);
    }
    private static bool TryActualMotion(Entry e, out HeavyShieldStance stance)
    {
        stance = HeavyShieldStance.BackIdle;
        float speed = float.NaN;
        // 2.1.0 read-only reference: Mover feeds Animator Speed from |rigidbody.velocity.x| and
        // feeds zero while paused. Actual 2.4 interop independently confirms GetFloat/rigidbody.
        // ActualSpeed in the reference is moveSpeed*multiplier, not physical motion evidence.
        // Only the current carrier root's finite physical velocity may replace a failed sample.
        try { if (e.Carrier._animator != null && e.Carrier._animator.enabled) speed = e.Carrier._animator.GetFloat("Speed"); } catch { }
        if (!float.IsFinite(speed))
        {
            try
            {
                var body = e.Mover.rigidbody;
                var root = e.Carrier.gameObject;
                var bodyRoot = body?.gameObject;
                if (bodyRoot == null || root == null || bodyRoot.Pointer != root.Pointer
                    || bodyRoot.GetInstanceID() != root.GetInstanceID()) return false;
                speed = body.velocity.x;
            }
            catch { return false; }
        }
        if (!float.IsFinite(speed)) return false;
        stance = ClassifyActualMotion(speed, e.Carrier.walkSpeed);
        return true;
    }
    internal static HeavyShieldStance ClassifyActualMotion(float speed, float walkSpeed)
    {
        if (!float.IsFinite(speed) || Math.Abs(speed) <= 0.05f) return HeavyShieldStance.BackIdle;
        return float.IsFinite(walkSpeed) && walkSpeed > 0f && Math.Abs(speed) > walkSpeed + 0.05f
            ? HeavyShieldStance.BackRun : HeavyShieldStance.BackWalk;
    }
    private static void Move(Entry e, float goal, float speed)
    {
        if (!HasNativeSourceLife(e.Carrier, e.Career.Life, false)) throw new InvalidOperationException("native mover life unconfirmed");
        if (e.MoveOwned && !e.MoveWrite.Matches(e.Mover)) throw new InvalidOperationException("mover taken by another owner");
        e.Mover.SetGoalNoHaglet(goal, speed);
        e.Mover.SetFacingMode(e.Facing < 0 ? Mover.FacingMode.Left : Mover.FacingMode.Right, null);
        e.MoveWrite = new(e.Mover); e.MoveOwned = true;
    }
    private static void BashImpact(Entry e)
    {
        int count = Math.Min(e.Enemies?.Length ?? 0, 32);
        for (int i = 0; i < count && e.Policy.BashActive; i++)
        {
            try
            {
                var root = e.Enemies[i];
                if (!Playing || !HasNativeSourceLife(e.Carrier, e.Career.Life, false)
                    || !HeavyShieldActorVisuals.GuardReady(e.Carrier, e.Career.Life) || !IsOrdinaryEnemy(root, false)) continue;
                Vector3 delta = root.transform.position - e.Carrier.transform.position;
                if (delta.x * e.Facing < 0f || Math.Abs(delta.x) > 1.2f || Math.Abs(delta.y) > 1.5f) continue;
                var damage = root.GetComponent<Damageable>();
                if (!CombatTargetLife.TryResolve(damage, out var life) || !e.Policy.TryRegisterBashTarget(life.Life, true)
                    || !CombatTargetLife.TryResolve(damage, out var again) || !CombatTargetToken.Matches(life, again)) continue;
                CombatDamage.Submit(damage, HeavyShieldBlockPolicy.BashDamage, e.Carrier.gameObject, DamageSource.Knight);
            }
            catch { } // one partially faulted native hit is never retried
        }
    }
    internal static bool TryBlock(in HeavyShieldCareerHandle handle, bool arrow, float vx, float sourceX)
    {
        if (!Playing || !HeavyShieldIdentity.ValidateCareer(handle) || !TryEntry(handle, out var e) || !e.Attached || e.Carrier.enabled
            || !HasNativeSourceLife(e.Carrier, handle.Life, false)
            || !HeavyShieldActorVisuals.GuardReady(e.Carrier, handle.Life)) return false;
        float defenderX = e.Carrier.transform.position.x;
        if (arrow)
        {
            if (!HeavyShieldDirection.ArrowFront(vx, e.Facing)) return false;
            sourceX = defenderX + e.Facing; // already-proved velocity direction, not shooter position
        }
        var result = e.Policy.EvaluateHit(new(true, true, sourceX, defenderX, e.Facing));
        if (result == HeavyShieldBlockPolicy.HitResult.PassThrough) return false;
        Wear(e);
        if (!Persist(e)) { e.Attached = false; HeavyShieldActorVisuals.Suspend(e.Carrier, handle.Life); }
        HeavyShieldActorVisuals.Trigger(e.Carrier, handle.Life,
            result == HeavyShieldBlockPolicy.HitResult.ShieldBroken ? HeavyShieldAction.Break : HeavyShieldAction.Block);
        return true;
    }
    private static void Retire(Entry e)
    {
        if (HeavyShieldCombat.InNativeDamageStack || e.Demoting || e.Policy.RetirementUnknown || !SameRoot(e)
            || !HasNativeSourceLife(e.Carrier, e.Career.Life, false)) return;
        if (e.Damage.isDead) { ObserveNativeDeath(e.Damage); return; }
        if (HeavyShieldActorVisuals.TryPose(e.Carrier, e.Career.Life, out var action, out float elapsed)
            && action == HeavyShieldAction.Break && HeavyShieldArtLayout.TryGetSequence(HeavyShieldAtlasId.Soldier, "break", out var clip)
            && elapsed < HeavyShieldPoseMachine.SequenceDuration(clip)) return;
        if (!e.Policy.TryBeginDemote(out ulong lease)) return;
        if (!Persist(e)) { e.Attached = false; return; } // reject native conversion if identity refused its pre-call unknown receipt
        var sourceLife = e.TargetLife;
        if (!HeavyShieldIdentity.TryGetCampaign(out var campaign) || !OriginalSourceLife(e, sourceLife, out bool alreadyEnded) || alreadyEnded)
        { e.Attached = false; HoldNativeDemoteUnknown(e); return; }
        e.DemoteCampaign = campaign; e.DemoteLife = sourceLife; e.Demoting = true;
        try
        {
            Character successor = e.Character.Demote();
            if (CompleteDemote(e, e.Career, campaign, sourceLife, successor)) e.Policy.ConfirmDemote(lease);
        }
        catch (Exception ex) { Log("native demote unknown, receipt held: " + ex.GetType().Name); }
        finally
        {
            e.Demoting = false;
            ObserveNativeDeath(e.Damage);
            if (!e.ExitConfirmed) HoldNativeDemoteUnknown(e);
        }
    }

    internal static NativeDemoteCapture BeginNativeDemote(Character source)
    {
        var capture = new NativeDemoteCapture();
        try
        {
            // A nested or overflow native call never borrows an outer source proof. Our own Retire owns its exit separately.
            if (Enabled && NativeDemotes.Depth == 0 && source != null && source.gameObject != null
                && source.gameObject.activeInHierarchy && Entries.TryGetValue(source.gameObject.GetInstanceID(), out var e)
                && e.Attached && !e.Demoting && Ptr(source) == Ptr(e.Character) && SameRoot(e)
                && HeavyShieldIdentity.TryGetSoldier(e.Carrier, out var career) && career == e.Career
                && HeavyShieldIdentity.ValidateCareer(career) && HeavyShieldIdentity.TryGetCampaign(out var campaign)
                && HasNativeSourceLife(e.Carrier, career.Life, false)
                && CombatTargetLife.TryResolve(e.Damage, out var life) && CombatTargetToken.Matches(life, e.TargetLife))
            {
                capture.Entry = e; capture.Source = source; capture.Career = career; capture.Campaign = campaign;
                capture.Life = life; capture.Active = true;
            }
        }
        catch { }
        capture.Scope = NativeDemotes.Push(capture, capture.Active);
        return capture;
    }
    internal static void ObserveNativeDemoteReturn(NativeDemoteCapture capture, Character successor)
    {
        try
        {
            if (capture == null || !capture.Active || capture.Closed || !NativeDemotes.TryPeek(out var top)
                || !ReferenceEquals(top, capture)) return;
            capture.Confirmed = CompleteDemote(capture.Entry, capture.Career, capture.Campaign, capture.Life, successor);
        }
        catch { } // the finalizer preserves the unknown paid claim after the stack closes
    }
    internal static bool HoldsNativeDemoteProof(GameObject root)
    {
        try
        {
            if (root == null || !Entries.TryGetValue(root.GetInstanceID(), out var e) || root.Pointer != e.Career.Root
                || !SameRoot(e, true) || !HeavyShieldIdentity.ValidateCareer(e.Career)) return false;
            if (e.Demoting) return HeavyShieldIdentity.ValidateCampaign(e.DemoteCampaign)
                && OriginalSourceLife(e, e.DemoteLife, out _);
            return NativeDemotes.TryPeek(out var capture) && capture.Active && !capture.Closed && capture.Entry == e
                && capture.Career == e.Career && Ptr(capture.Source) == Ptr(e.Character)
                && HeavyShieldIdentity.ValidateCampaign(capture.Campaign) && OriginalSourceLife(e, capture.Life, out _);
        }
        catch { return false; }
    }
    private static bool OriginalSourceLife(Entry e, in CombatTargetToken life, out bool ended)
    {
        ended = false;
        if (e.Carrier.gameObject.Pointer != life.GoPointer || e.Carrier.gameObject.GetInstanceID() != life.GoId
            || Ptr(e.Damage) != life.Damageable || life.Life == 0) return false;
        var marker = e.Carrier.gameObject.GetComponent<CombatTargetLifeMarker>();
        if (marker == null || !marker.enabled || !marker.Observing) return false;
        if (!e.Carrier.gameObject.activeInHierarchy)
        { ended = true; return marker.Life == 0 && marker.LastObservedLife == life.Life; }
        // A current new life is an ended old source, but cannot authorize reads/writes on the new life.
        ended = marker.Life != life.Life;
        return !ended;
    }
    private static bool CompleteDemote(Entry e, in HeavyShieldCareerHandle career, in HeavyShieldCampaignToken campaign,
        in CombatTargetToken life, Character successor)
    {
        if (e == null || e.ExitConfirmed) return e?.ExitConfirmed == true;
        if (e.Career != career || !SameRoot(e, true) || !HeavyShieldIdentity.ValidateCareer(career)
            || !HeavyShieldIdentity.ValidateCampaign(campaign) || !OriginalSourceLife(e, life, out bool ended)) return false;
        if (e.Damage.isDead)
        {
            bool wasDemoting = e.Demoting; e.Demoting = false;
            try { ObserveNativeDeath(e.Damage); }
            finally { e.Demoting = wasDemoting; }
            return e.ExitConfirmed; // same-life Dead always wins over a returned Peasant
        }
        if (!ended || successor == null || successor.gameObject == null || !InWorld(successor.gameObject)
            || successor.gameObject.GetComponent<Peasant>() == null || successor.gameObject.Pointer == career.Root
            || !ReturnBorrowed(e, true)) return false;
        if (!HeavyShieldIdentity.ObserveNativeExitProof(career, HeavyShieldExitKind.DemotedToPeasant)
            || !HeavyShieldIdentity.ConfirmExit(career, HeavyShieldExitKind.DemotedToPeasant, successor)) return false;
        e.ExitConfirmed = true; ReleaseEnded(e); return true;
    }
    private static void HoldNativeDemoteUnknown(Entry e)
    {
        if (e == null || e.ExitConfirmed) return;
        e.Attached = false;
        try
        {
            // Never hand a recycled root to A: that would mark the new career unresolved.
            if (!SameRoot(e, true) || !HeavyShieldIdentity.ValidateCareer(e.Career)) return;
            ReturnBorrowed(e, true);
            HeavyShieldIdentity.ObservePoolDespawn(e.Carrier.gameObject, 0f);
        }
        catch { }
    }
    internal static void ObserveNativeDeath(Damageable damage)
    {
        try
        {
            if (damage == null || !Entries.TryGetValue(damage.gameObject.GetInstanceID(), out var e)
                || !SameRoot(e, true) || Ptr(damage) != Ptr(e.Damage) || !damage.isDead
                || !HeavyShieldIdentity.ValidateCareer(e.Career) || e.Demoting
                || !OriginalSourceLife(e, e.TargetLife, out _)) return;
            if (!ReturnBorrowed(e, true)) return;
            HeavyShieldIdentity.ObserveNativeExitProof(e.Career, HeavyShieldExitKind.Dead);
            if (!HeavyShieldIdentity.ConfirmExit(e.Career, HeavyShieldExitKind.Dead, null)) return;
            e.Policy.MarkDead();
            e.ExitConfirmed = true; ReleaseEnded(e);
        }
        catch { }
    }
    internal static void BeforeNativePoolDespawn(GameObject root)
    {
        try
        {
            if (root == null || !Entries.TryGetValue(root.GetInstanceID(), out var e) || root.Pointer != e.Career.Root) return;
            if (!e.Demoting && e.Damage.isDead) { ObserveNativeDeath(e.Damage); return; }
            bool held = HoldsNativeDemoteProof(root);
            if (ReturnBorrowed(e) && !held) Entries.Remove(e.Career.GoId); // only the exact whole-call source keeps its proof
        }
        catch { }
    }
    private static void ReleaseEnded(Entry e)
    {
        // No native field/enable writes after a terminal career: the old root may be pooled already.
        e.Attached = false;
        HeavyShieldActorVisuals.Unregister(e.Carrier, e.Career.Life);
        if (e.TargetOwned) { e.Targets.DeregisterPriorityTarget(e.Damage); e.TargetOwned = false; }
        Entries.Remove(e.Career.GoId);
    }
    private static bool Persist(Entry e)
        => HeavyShieldIdentity.UpdateCombatState(e.Career, new(e.Policy.Durability, e.Policy.PendingBreak, e.Policy.RetirementUnknown));
    private static void Wear(Entry e)
    { HeavyShieldActorVisuals.SetWear(e.Carrier, e.Career.Life, e.Policy.Durability >= 4 ? HeavyShieldWear.Intact
        : e.Policy.Durability == 3 ? HeavyShieldWear.Worn : e.Policy.Durability == 2 ? HeavyShieldWear.Critical : HeavyShieldWear.Half); }
    internal static bool IsOrdinaryEnemy(GameObject root, bool arrowOwner)
    {
        try
        {
            if (!InWorld(root)) return false;
            var enemy = root.GetComponent<Enemy>(); var damage = root.GetComponent<Damageable>();
            if (enemy == null || damage == null || !damage.enabled || damage.isDead) return false;
            if (arrowOwner) return root.GetComponent<GreedArcher>() != null && enemy.Type == EnemyType.Archer;
            return (root.GetComponent<Troll>() != null && (enemy.Type == EnemyType.TrollWeak
                || enemy.Type == EnemyType.TrollMedium || enemy.Type == EnemyType.ToughTroll))
                || (root.GetComponent<GreedArcher>() != null && enemy.Type == EnemyType.Archer);
        }
        catch { return false; }
    }
    private static bool CarrierComponentsValid(Archer a)
    {
        return a._character != null && a._damageable != null && a._mover != null && a.persistent != null
            && a.behaviour != null && a.shoot != null && a.attack != null
            && a._spriteRenderer != null && a._animator != null && a._enemyScanner != null
            && !HeroArcherRuntime.IsHero(a) && !MusketeerRuntime.IsMusketeer(a);
    }
    private static bool CleanCarrier(Archer a, bool allowNativeJobs = false)
    {
        return InWorld(a.gameObject) && CarrierComponentsValid(a) && a._character.enabled && !a._character.grabbed
            && a._damageable.enabled && !a._damageable.isDead && a._mover.enabled && a.persistent.enabled
            && (allowNativeJobs || (a._guardSlot == null && a._knight == null && a._currentFormation == null)) && a._unitController == null
            && (a.Embarkee == null || (!a.Embarkee.IsEmbarked && !a.Embarkee.IsTargetingEmbarkable));
    }
    private static bool Stopped(IHaglet routine)
    { var haglet = routine?.TryCast<Haglet>(); return haglet != null && (haglet.stopped || haglet.completed) && !haglet.executing; }
    private static bool HasPriority(Entry e)
    {
        var rows = e.Targets._trollPriorityTargets;
        if (rows == null) return false;
        for (int i = 0; i < rows.Count; i++) if (Ptr(rows[i]) == Ptr(e.Damage)) return true;
        return false;
    }
    private static bool TryEntry(in HeavyShieldCareerHandle handle, out Entry e)
    { e = null; return Entries.TryGetValue(handle.GoId, out e) && e.Career == handle && SameRoot(e); }
    private static bool SameRoot(Entry e, bool allowInactive = false)
    {
        try { var go = e.Carrier?.gameObject; var layer = Managers.Inst?.world?.gameLayer;
            return (allowInactive ? go != null : InWorld(go)) && layer != null && go.Pointer == e.Career.Root && go.GetInstanceID() == e.Career.GoId
                && layer.Pointer.ToInt64() == e.Career.World && e.Career.Life > 0; }
        catch { return false; }
    }
    private static IntPtr Ptr(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase value) => value?.Pointer ?? IntPtr.Zero;
    private static bool InWorld(GameObject root)
    {
        var layer = Managers.Inst?.world?.gameLayer;
        return root != null && root.activeInHierarchy && layer != null && layer.gameObject != null
            && layer.gameObject.activeInHierarchy && root.scene.handle == layer.gameObject.scene.handle
            && root.transform.IsChildOf(layer);
    }
    private static void Log(string message)
    { if (_logged) return; _logged = true; try { KingdomEnhancedPlugin.Instance?.LogSource?.LogWarning("[HeavyShieldRuntime] " + message); } catch { } }
}
