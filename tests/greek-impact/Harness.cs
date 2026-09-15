// 测试夹具：按 2.4 native 顺序模拟 Pool.Spawn / Arrow.OnEnable / archer 赋值，
// 以及 Operator 接入后的 HitObject wrapper（Capture → BeginHit → native → EndHit / AbortHit）。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace KingdomGreekImpact.Tests
{
    internal sealed class Squad
    {
        internal GameObject Root;
        internal Knight Knight;
        internal Archer Archer;
        internal Character ArcherCharacter, KnightCharacter;
        internal Embarkee ArcherEmbarkee, KnightEmbarkee;
        internal Damageable ArcherDamageable, KnightDamageable;
        internal Buffable KnightBuff, ArcherBuff;
        internal ArrowAttack Attack;
        internal Arrow Prefab;

        internal GameObject Source => Archer.gameObject;

        internal Volley OpenVolley() => new Volley(this, Attack);
        internal Volley OpenVolley(ArrowAttack attack) => new Volley(this, attack);

        /// <summary>打开 / 关闭原生 FireAttacks 窗口（expiry 相对 Time.time）。</summary>
        internal void SetWindow(bool knightOpen, bool archerOpen, float seconds = 300f)
        {
            KnightBuff._activeBuffsExpirations[BuffType.FireAttacks] = knightOpen ? Time.time + seconds : Time.time - 1f;
            ArcherBuff._activeBuffsExpirations[BuffType.FireAttacks] = archerOpen ? Time.time + seconds : Time.time - 1f;
        }
    }

    /// <summary>一次 FireArrowInternal 调用的模拟：scope 打开 → 原箭 + 任意额外箭 → scope 关闭。</summary>
    internal sealed class Volley
    {
        internal readonly Squad Squad;
        internal readonly ArrowAttack Attack;
        internal readonly PatchArcher_GreekImpact.ShotTicket Scope;
        internal readonly List<Arrow> Arrows = new List<Arrow>();

        internal Volley(Squad squad, ArrowAttack attack)
        {
            Squad = squad;
            Attack = attack;
            Scope = PatchArcher_GreekImpact.BeginShot(attack, squad.Source);
        }

        internal Arrow Fire(Arrow prefabOverride = null) => Harness.SpawnInVolley(this, prefabOverride ?? Attack._arrowPrefab);

        internal void Close() => PatchArcher_GreekImpact.EndShot(Scope);
    }

    internal sealed class Foe
    {
        internal GameObject Go;
        internal Damageable Damageable;
        internal readonly List<Collider2D> Colliders = new List<Collider2D>();
        internal int Damage => Damageable != null ? Damageable.ReceivedTotal : 0;
        internal int DotTicks => Damageable != null ? Damageable.DotTicks : 0;
        internal int FireHits
        {
            get
            {
                int n = 0;
                if (Damageable == null) return 0;
                foreach (DamageSource s in Damageable.ReceivedSources) if (s == DamageSource.Fire) n++;
                return n;
            }
        }
    }

    internal readonly struct HitOutcome
    {
        internal readonly bool TicketValid;
        internal readonly bool FxGateVisible;
        internal readonly bool EndResult;      // EndHit 返回值：accepted 且替代成功
        internal readonly bool FireFlagAfter;  // 始终应为 true（本模块从不改 isFireArrow）
        internal readonly bool Threw;

        internal HitOutcome(bool ticketValid, bool fxGateVisible, bool endResult, bool flagAfter, bool threw)
        {
            TicketValid = ticketValid;
            FxGateVisible = fxGateVisible;
            EndResult = endResult;
            FireFlagAfter = flagAfter;
            Threw = threw;
        }
    }

    internal static class Harness
    {
        internal static Squad BuildSquad(GameObject worldRoot, bool style3 = true, bool knightWindow = true,
            bool archerWindow = true, bool withKnight = true, bool living = true)
        {
            Squad squad = new Squad();
            squad.Root = new GameObject("squad", 3);
            squad.Root.transform.parent = worldRoot.transform;

            squad.Knight = squad.Root.AddComponent<Knight>();
            squad.KnightCharacter = squad.Root.AddComponent<Character>();
            squad.KnightDamageable = squad.Root.AddComponent<Damageable>();
            squad.KnightEmbarkee = squad.Root.AddComponent<Embarkee>();
            squad.Knight._character = squad.KnightCharacter;
            squad.Knight._damageable = squad.KnightDamageable;
            squad.Knight._embarkee = squad.KnightEmbarkee;
            squad.Knight._harmless = false;
            squad.KnightDamageable.isDead = !living;
            squad.KnightBuff = Buffable.Create(squad.Root, squad.Knight,
                new[] { BuffType.FireAttacks }, new Dictionary<BuffType, float>());
            squad.Knight.Buffable = squad.KnightBuff;

            GameObject archerGo = new GameObject("archer", 3);
            archerGo.transform.parent = squad.Root.transform;
            squad.Archer = archerGo.AddComponent<Archer>();
            squad.ArcherCharacter = archerGo.AddComponent<Character>();
            squad.ArcherDamageable = archerGo.AddComponent<Damageable>();
            squad.ArcherEmbarkee = archerGo.AddComponent<Embarkee>();
            squad.Archer._character = squad.ArcherCharacter;
            squad.Archer._damageable = squad.ArcherDamageable;
            squad.Archer._embarkee = squad.ArcherEmbarkee;
            squad.ArcherDamageable.isDead = !living;
            squad.ArcherBuff = Buffable.Create(archerGo, squad.Archer,
                new[] { BuffType.FireAttacks }, new Dictionary<BuffType, float>());
            squad.Archer.Buffable = squad.ArcherBuff;
            squad.Archer._knight = withKnight ? squad.Knight : null;

            squad.Prefab = BuildArrowPrefab(isFire: true);
            squad.Attack = new ArrowAttack { _arrowPrefab = squad.Prefab };
            squad.Archer._fireArrowAttack = squad.Attack;

            squad.SetWindow(knightWindow, archerWindow);
            PatchRoles_KnightStyle.Resolver = style3 ? (Func<Knight, int>)(_ => 3) : (_ => 2);
            return squad;
        }

        internal static Arrow BuildArrowPrefab(bool isFire)
        {
            GameObject go = new GameObject("ArrowPrefab");
            Arrow arrow = go.AddComponent<Arrow>();
            arrow.isFireArrow = isFire;
            arrow.hitDamage = 1;
            return arrow;
        }

        /// <summary>模拟 native Pool.Spawn&lt;Arrow&gt; + OnEnable 钩子 + Spawn 之后的 arrow.archer 赋值。</summary>
        internal static Arrow SpawnInVolley(Volley volley, Arrow prefab)
        {
            GameObject go = new GameObject("Arrow", 11);
            go.transform.parent = ArcherOptionsScope.LayerRoot.transform;   // native: Pool.Spawn(..., world.gameLayer, ...)
            Arrow arrow = go.AddComponent<Arrow>();
            arrow.isFireArrow = prefab.isFireArrow;
            arrow.hitDamage = prefab.hitDamage;
            arrow.perfectDamageMultiplier = prefab.perfectDamageMultiplier;
            arrow.damagePerTick = prefab.damagePerTick;
            arrow.damageTicks = prefab.damageTicks;
            arrow.damageDelayTime = prefab.damageDelayTime;
            arrow.damageDelayOffset = prefab.damageDelayOffset;
            arrow._damageSource = prefab._damageSource;

            PatchArcher_GreekImpact.OnArrowEnable(arrow);    // OnEnable Prefix 钩子体
            arrow.OnEnableBody();                           // native OnEnable 本体（2.4 不重置 isFireArrow）
            PatchArcher_GreekImpact.OnArrowSpawned(arrow);   // OnEnable Postfix 钩子体
            arrow.archer = volley.Squad.Source;              // native 在 Spawn 之后赋值
            volley.Arrows.Add(arrow);
            return arrow;
        }

        /// <summary>接入后的 HitObject wrapper 语义（Capture → BeginHit → native → EndHit / Finalizer Abort）。</summary>
        internal static HitOutcome SimulateHit(Arrow arrow, GameObject target, bool physicalHit = true)
        {
            bool fxGate = PatchArcher_GreekImpact.IsEligibleArrow(arrow);
            PatchArcher_GreekImpact.HitTicket ticket = PatchArcher_GreekImpact.BeginHit(arrow, target);
            bool threw = false;
            bool endResult = false;
            try { arrow.HitObjectTracked(target, physicalHit); }
            catch
            {
                PatchArcher_GreekImpact.AbortHit(arrow, ticket);
                threw = true;
            }
            if (!threw) endResult = PatchArcher_GreekImpact.EndHit(arrow, ticket);
            return new HitOutcome(ticket.Valid, fxGate, endResult, arrow.isFireArrow, threw);
        }

        /// <summary>不在任何 FireArrowInternal scope 内 spawn 的箭（验证 fail-closed）。</summary>
        internal static Arrow SpawnStrayArrow(GameObject source, Arrow prefab, GameObject worldRoot)
        {
            GameObject go = new GameObject("StrayArrow", 11);
            go.transform.parent = worldRoot.transform;
            Arrow arrow = go.AddComponent<Arrow>();
            arrow.isFireArrow = prefab.isFireArrow;
            arrow.hitDamage = prefab.hitDamage;
            PatchArcher_GreekImpact.OnArrowEnable(arrow);
            arrow.OnEnableBody();
            PatchArcher_GreekImpact.OnArrowSpawned(arrow);
            arrow.archer = source;
            return arrow;
        }

        /// <summary>native HitObject → TryDamage 的分派：模块窄前缀优先，未替代则执行原生。</summary>
        internal static bool DispatchTryDamage(Arrow arrow, Damageable damageable)
        {
            bool result = false;
            if (!PatchArcher_GreekImpact.TrySubstituteDirectDamage(arrow, damageable, ref result))
                return result;                      // 已替代（含重入短路）
            return arrow.TryDamage(damageable);     // 原生
        }

        /// <summary>模拟 native 命中抛异常：Finalizer 路径只清事务、不做 AoE。</summary>
        internal static HitOutcome SimulateHitThrowing(Arrow arrow, GameObject target)
        {
            bool fxGate = PatchArcher_GreekImpact.IsEligibleArrow(arrow);
            PatchArcher_GreekImpact.HitTicket ticket = PatchArcher_GreekImpact.BeginHit(arrow, target);
            bool threw = false;
            try { throw new InvalidOperationException("native hit exploded"); }
            catch
            {
                PatchArcher_GreekImpact.AbortHit(arrow, ticket);
                threw = true;
            }
            return new HitOutcome(ticket.Valid, fxGate, false, arrow.isFireArrow, threw);
        }

        internal static Foe SpawnFoe(GameObject worldRoot, float x, float y = 0f, float extent = 0.18f,
            int layer = Env.EnemiesLayer, bool immune = false, bool withEnemy = true, bool friendlyTroll = false,
            bool isTrigger = false, bool dead = false, GameObject childColliderHost = null)
        {
            GameObject go = new GameObject("foe", layer);
            go.transform.parent = worldRoot.transform;
            go.transform.position = new Vector3(x, y, 0f);
            Foe foe = new Foe { Go = go };
            foe.Damageable = go.AddComponent<Damageable>();
            foe.Damageable.Immune = immune;
            foe.Damageable.isDead = dead;
            if (withEnemy) go.AddComponent<Enemy>();
            if (friendlyTroll) go.AddComponent<FriendlyTroll>();
            AddCollider(foe, childColliderHost, x, y, extent, isTrigger, layer);
            return foe;
        }

        /// <summary>给已有目标追加一个 collider（验证同一 Damageable 多 collider 去重）。</summary>
        internal static Collider2D AddCollider(Foe foe, GameObject host, float x, float y, float extent, bool isTrigger, int layer)
        {
            GameObject go = host != null ? host : foe.Go;
            if (host != null) { go.transform.position = new Vector3(x, y, 0f); }
            Collider2D collider = go.AddComponent<Collider2D>();
            collider.isTrigger = isTrigger;
            collider.Center = new Vector2(x, y);
            collider.Extent = extent;
            go.layer = layer;
            foe.Colliders.Add(collider);
            Env.Colliders.Add(collider);
            return collider;
        }

        internal static GameObject ChildOf(GameObject parent, string name = "hitbox", int layer = Env.EnemiesLayer)
        {
            GameObject go = new GameObject(name, layer);
            go.transform.parent = parent.transform;
            return go;
        }
    }
}
