using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 幕府骑士 PowerDash 候选（style index 2 限定）。Reviewer P1/P2 修订版：
///
/// - P1（可验证命中/伤害）：不再只触发动画。冲刺位移期间每帧对骑士周围的
///   Enemies|Wildlife 层做 OverlapCircleNonAlloc 窗口，对每个未命中过的
///   Damageable 调 ReceiveDamage(knight._attackDamage, knight.gameObject,
///   DamageSource.Knight)——逐字镜像原生 Knight.Slash 的伤害路径
///   （Knight.cs Slash()：_hitObjects 去重 + IsDamagedBy(DamageSource.Knight)
///   + ReceiveDamage(_attackDamage, ...)），伤害数值与普攻完全一致，可实测验证。
/// - P2（扫描成本）：删掉每骑士 0.2s 的 Physics2D.OverlapCircleAll 全场扫描和
///   TryHasSquadLeashViolation 里的 FindObjectsOfType&lt;Archer&gt;。目标获取改用
///   原生 knight._enemyScanner.GetClosest()（骑士自带的层过滤扫描器，原生
///   ShouldSlash 同款，零额外全场景扫描）；随从距离判定改用共享 UnitScanCache.
///   GetArchers()（3s 窗口一份缓存，与 DefenseSpacing/KnightStyle 共用）。
/// - 位移本体：Mover.SetGoal 仍承担移动（原生 Charge/GrabArmor 同款机制，
///   走 Rigidbody/PositionSync，无瞬移 desync），但冲刺不再以"到达目标"结束，
///   而是以命中窗口 + 行程上限（≤MaxRange 格）+ 超时结束；SetGoal 只是位移
///   载体，伤害与命中判定才是冲刺本体。
/// - 池复用/协程死亡清理：协程随宿主禁用被 Unity 静默终止时 finally 不会执行，
///   invulnerable/trail 可能残留。Knight.OnDisable postfix 强制复位
///   invulnerable/trail 并清 Active/NextScan 记录（despawn=禁用，池复用即自愈；
///   字典不跨世界清理也不会泄漏——按 instanceID 键控，OnDisable 逐骑士清除）。
/// - 网络限制（明示）：Knight.Update 在无世界权威端被原生禁用（OnEnable
///   base.enabled=false），Tick 仅在主机/单机运行；冲刺位移经 PositionSync
///   同步，伤害走原生 Damageable 主机权威路径，联机行为与原生 Slash 一致。
///   动画触发是本地 animator.SetTrigger（不走 AnimationSync RPC）——客户端
///   上该触发可能不播放，纯视觉差异，不影响伤害判定。
/// - 保留约束：仅 style 2；冷却约 3s；行程 5-7 格（MinRange..MaxRange 发起，
///   行程硬上限 MaxRange）；冲刺期间无敌（damageable.invulnerable）+ 白光
///   （Character.spriteFX.GlowOverlay 白色）+ PowerSlash 动画 + 原生拖尾
///   （knight._trail）；随从距离 &gt; FollowLeash 时不发起新冲刺、冲刺中超出即
///   提前收招；不复用狂战士跳劈（无 Berserker 交互）。
/// </summary>
internal static class PatchRoles_SamuraiPowerDash
{
    private const int ShogunStyleIndex = 2;
    private const float ScanInterval = 0.2f;   // 仅冷却/随从距离复查节奏，无全场景扫描
    private const float Cooldown = 3f;          // 约 3 秒冷却
    private const float MinRange = 1.5f;        // 太近不冲（普攻够得着）
    private const float MaxRange = 7f;          // 发起距离上限 = 行程硬上限（5-7 格短冲刺）
    private const float FollowLeash = 10f;      // 超过随从 10 格不继续脱离
    private const float DashSpeed = 18f;        // 冲刺移动速度（短时间覆盖长距离）
    private const float DashTimeout = 0.6f;     // 位移超时（防 mover 卡死）
    private const float HitWindowRadius = 1.2f; // 冲刺路径伤害窗口半径（镜像原生 Slash 判定盒量级）
    private const int MaxHitsPerWindow = 16;

    private static readonly Dictionary<int, float> NextScan = new();
    private static readonly HashSet<int> Active = new();
    private static readonly int PowerSlash = Animator.StringToHash("PowerSlash");
    private static int _hitLayerMask; // Enemies|Wildlife（懒加载，镜像原生 Slash 的层并集）

    internal static void Tick(Knight knight)
    {
        if (knight == null || !knight.gameObject.activeInHierarchy) return;
        if (!NetworkBigBoss.HasWorldAuth) return; // 无权威端 Knight.Update 原生禁用，双保险
        if (!PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int style) || style != ShogunStyleIndex) return;
        int id = knight.gameObject.GetInstanceID();
        if (Active.Contains(id)) return;
        float now = Time.time;
        if (NextScan.TryGetValue(id, out float next) && now < next) return;
        NextScan[id] = now + ScanInterval;

        try
        {
            // 原生扫描器取最近敌人（层过滤、随骑士视角），替代全场 OverlapCircleAll
            Scanner scanner = knight._enemyScanner;
            GameObject target = scanner != null ? scanner.GetClosest() : null;
            if (target == null) return;
            Damageable targetDamageable = target.GetComponent<Damageable>();
            if (targetDamageable == null || !targetDamageable.IsDamagedBy(DamageSource.Knight)) return;
            float dx = Mathf.Abs(target.transform.position.x - knight.transform.position.x);
            if (dx < MinRange || dx > MaxRange) return;
            // 随从距离（共享 3s 缓存反查，无 FindObjectsOfType）
            if (IsBeyondFollowerLeash(knight)) return;

            knight.StartCoroutine(DashRoutine(knight, target).WrapToIl2Cpp());
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiDash/tick] " + e);
        }
    }

    /// <summary>随从最近距离是否超过 FollowLeash（反查共享缓存，不枚举 knight._archers）。</summary>
    private static bool IsBeyondFollowerLeash(Knight knight)
    {
        try
        {
            float knightX = knight.transform.position.x;
            float nearest = float.MaxValue;
            Archer[] archers = UnitScanCache.GetArchers();
            for (int i = 0; i < archers.Length; i++)
            {
                Archer archer = archers[i];
                if (archer == null || archer._knight != knight) continue;
                nearest = Mathf.Min(nearest, Mathf.Abs(archer.transform.position.x - knightX));
            }
            return nearest != float.MaxValue && nearest > FollowLeash;
        }
        catch
        {
            return false; // 判定失败不阻塞冲刺（宁可冲刺也不永久哑火）
        }
    }

    private static IEnumerator DashRoutine(Knight knight, GameObject target)
    {
        int id = knight.gameObject.GetInstanceID();
        Active.Add(id);
        Damageable damageable = null;
        TrailRenderer trail = null;
        try
        {
            Mover mover = knight._mover;
            if (mover == null) mover = knight.GetComponent<Mover>();
            damageable = knight._damageable;
            if (damageable == null) damageable = knight.GetComponent<Damageable>();
            Animator animator = knight._animator;
            if (animator == null) animator = knight.GetComponent<Animator>();
            trail = knight._trail; // 原生冲锋拖尾（Awake/OnEnable 默认关闭，仅冲刺开启）
            if (mover == null || damageable == null || target == null) yield break;

            // 冲刺视觉包：无敌 + 白光 + PowerSlash 动画 + 拖尾
            damageable.invulnerable = true;
            if (trail != null) trail.enabled = true;
            if (animator != null) animator.SetTrigger(PowerSlash);
            try
            {
                Character character = knight.GetComponent<Character>();
                if (character != null && character.spriteFX != null)
                    character.spriteFX.GlowOverlay(Color.white, DashTimeout);
            }
            catch { /* 白光是视觉增益，失败不阻断冲刺 */ }

            // 位移：朝目标前方 0.5 格处冲刺（保持短兵接触距离），原生 SetGoal 机制
            float dir = Mathf.Sign(target.transform.position.x - knight.transform.position.x);
            float goalX = target.transform.position.x - dir * 0.5f;
            mover.SetGoal(goalX, DashSpeed);

            // 冲刺本体：命中窗口 + 行程/超时上限（不以到达目标为结束条件）
            if (_hitLayerMask == 0)
                _hitLayerMask = LayerMask.GetMask("Enemies", "Wildlife");
            float startX = knight.transform.position.x;
            float deadline = Time.time + DashTimeout;
            var hitObjects = new HashSet<int>();
            var colliders = new Collider2D[MaxHitsPerWindow];
            while (Time.time < deadline && knight != null && knight.gameObject.activeInHierarchy
                && Mathf.Abs(knight.transform.position.x - startX) < MaxRange)
            {
                // 随从被甩开超限：立即收招（不继续脱离）
                if (IsBeyondFollowerLeash(knight)) break;
                int count = Physics2D.OverlapCircleNonAlloc(
                    knight.transform.position, HitWindowRadius, colliders, _hitLayerMask);
                for (int i = 0; i < count; i++)
                {
                    Collider2D hit = colliders[i];
                    if (hit == null) continue;
                    Damageable enemy = hit.GetComponent<Damageable>();
                    if (enemy == null || !enemy.IsDamagedBy(DamageSource.Knight)) continue;
                    int enemyId = hit.gameObject.GetInstanceID();
                    if (!hitObjects.Add(enemyId)) continue;
                    // 原生 Knight.Slash 同款伤害调用（同数值、同伤害源）
                    enemy.ReceiveDamage(knight._attackDamage, knight.gameObject, DamageSource.Knight);
                }
                yield return null;
            }
            if (mover != null && mover.movingToGoal) mover.Stop();
        }
        finally
        {
            if (knight != null && knight.gameObject != null)
            {
                // 池对象禁用时协程被静默终止、finally 不执行——OnDisable postfix 兜底
                if (damageable != null) damageable.invulnerable = false;
                if (trail != null) trail.enabled = false;
            }
            Active.Remove(id);
            NextScan[id] = Time.time + Cooldown;
        }
    }

    /// <summary>
    /// 池复用/协程死亡清理：despawn（=禁用）时 Unity 会静默终止骑士身上的协程，
    /// DashRoutine 的 finally 不会执行。此 postfix 强制复位无敌/拖尾并清簿记，
    /// 保证池对象复用时不携带 invulnerable=true 或残留 Active 锁。
    /// </summary>
    internal static void OnKnightDisabled(Knight knight)
    {
        try
        {
            if (knight == null || knight.gameObject == null) return;
            int id = knight.gameObject.GetInstanceID();
            if (!Active.Remove(id) && !NextScan.ContainsKey(id)) return;
            Damageable damageable = knight._damageable;
            if (damageable == null) damageable = knight.GetComponent<Damageable>();
            if (damageable != null) damageable.invulnerable = false;
            TrailRenderer trail = knight._trail;
            if (trail != null) trail.enabled = false;
            NextScan.Remove(id);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiDash/on-disable] " + e);
        }
    }
}

[HarmonyPatch(typeof(Knight), "Update")]
internal static class Knight_Update_SamuraiPowerDash_Patch
{
    private static void Postfix(Knight __instance)
    {
        if (!ModConfig.Enabled.Value || __instance == null) return;
        try { PatchRoles_SamuraiPowerDash.Tick(__instance); }
        catch (Exception e) { KingdomEnhancedPlugin.Instance?.LogSource.LogError("[SamuraiDash] " + e); }
    }
}

/// <summary>池复用/协程死亡清理（私有 OnDisable 按名字符串补丁，先例：KnightStyle 的 OnEnable）。</summary>
[HarmonyPatch(typeof(Knight), "OnDisable")]
internal static class Knight_OnDisable_SamuraiPowerDash_Patch
{
    [HarmonyPostfix]
    private static void Postfix(Knight __instance)
    {
        if (__instance == null) return;
        PatchRoles_SamuraiPowerDash.OnKnightDisabled(__instance);
    }
}
