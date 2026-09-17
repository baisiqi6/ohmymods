using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 中世纪散射资格（medieval-scatter）：只回答「本次射击是否允许散射」，由
/// PatchArcher_Options.OnArrowSpawnedByAttack 解析出 Archer 后调用；总箭数（1..3 含主箭，
/// 走现有 config/slider）由调用方决定。本类纯读零副作用：不生成/移动/删除箭、不碰池、
/// 不改任何原生字段/层级/目标、不新增扫描/driver/hook、不涉及颜色（独立 worker）。
///
/// 准入（全部满足才 true；任何异常 fail-closed）：
/// 1) 射手守卫（沿 rate 同签名）：active/enabled、!harmless、当前 world
///    （ArcherOptionsScope.IsCurrent）、_damageable 存活、_character 非 inert/grabbed、
///    _attackMode 与 _desiredAttackMode 同时 Ranged。
/// 2) 队籍：_knight 非空、tag=="Knight"、_damageable 存活、当前 world，且
///    PatchRoles_KnightStyle.TryGetResolvedStyleIndex == 0（读已有固定身份/收据，不 hash、
///    不按当前 world/biome 重推；未决=不散射）。另排除 PatchRoles_Crossbowman.IsCrossbowman
///    与 PatchRoles_NorseSquad.IsNorseArcherInstance（回收进中世纪队的北境近战随从）。
/// 3) 目标：Archer._shootingTarget（本次 FireArrow 实际使用的字段，不用 _huntingTarget 旧值、
///    与昼夜无关）非空、active/当前 world、layer==LayerMask.NameToLayer(Layers.Enemies)
///    （野生动物不在该层）、tag!="Wildlife"（防御）、无 FriendlyTroll 组件/父级（友好转换）、
///    Damageable 存在且 enabled、!isDead、IsDamagedBy(DamageSource.Arrow)。不要求 Enemy 组件
///    （原生敌方建筑同样吃箭）；无目标即不散射，绝不调用 scanner。
/// </summary>
internal static class MedievalScatterPolicy
{
    private const int MedievalStyleIndex = 0; // KnightStyle 风格表 index 0 = medieval
    private const string KnightTagName = "Knight";
    private const string WildlifeTagName = "Wildlife";
    private const string EnemiesLayerName = "Enemies"; // Layers.Enemies 取不到时的兜底字面量

    private static int EnemiesLayer = -1;

    /// <summary>本次射击是否允许散射（true = 调用方按现有配置追加总额外的箭）。纯读判定。</summary>
    internal static bool IsEligible(Archer archer)
    {
        try
        {
            if (!ShooterReady(archer)) return false;             // 守卫 + 军种排除
            if (!MedievalLeader(archer._knight)) return false;   // 固定身份 style0 骑士队籍
            return CombatTarget(archer._shootingTarget);         // 本次射击的真实目标
        }
        catch (Exception)
        {
            return false; // 读字段/组件失败一律 fail-closed，绝不散射
        }
    }

    /// <summary>射手守卫（含弩手/北境近战排除）：未知状态一律拒绝。</summary>
    private static bool ShooterReady(Archer archer)
    {
        if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
        if (MusketeerIdentity.IsUnit(archer)) return false;
        if (!archer.enabled || archer.harmless) return false;
        if (archer._attackMode != Archer.AttackMode.Ranged
            || archer._desiredAttackMode != Archer.AttackMode.Ranged) return false;
        Character character = archer._character;
        if (character == null || character.inert || character.grabbed) return false;
        Damageable damageable = archer._damageable;
        if (damageable == null || damageable.isDead) return false;
        if (!ArcherOptionsScope.IsCurrent(archer)) return false;
        if (PatchRoles_Crossbowman.IsCrossbowman(archer)) return false;  // 独立/弩包弩手
        return !PatchRoles_NorseSquad.IsNorseArcherInstance(archer);     // 移交过来的北境近战随从
    }

    /// <summary>当前队籍骑士：活跃 tagKnight、存活、当前 world，且固定身份解析为 style0。</summary>
    private static bool MedievalLeader(Knight knight)
    {
        if (knight == null || knight.gameObject == null) return false;
        if (!knight.gameObject.CompareTag(KnightTagName)) return false;
        Damageable damageable = knight._damageable;
        if (damageable == null || damageable.isDead) return false;
        if (!ArcherOptionsScope.IsCurrent(knight)) return false;
        return PatchRoles_KnightStyle.TryGetResolvedStyleIndex(knight, out int styleIndex)
            && styleIndex == MedievalStyleIndex;
    }

    /// <summary>本次射击目标：当前世界的 Enemies 层敌方 Damageable；野生动物/友军巨魔排除。</summary>
    private static bool CombatTarget(GameObject target)
    {
        if (target == null || target.transform == null || !target.activeInHierarchy) return false;
        if (!ArcherOptionsScope.IsCurrent(target.transform)) return false;
        if (target.layer != EnemiesLayerIndex()) return false;   // 野生动物等不在 Enemies 层
        if (target.CompareTag(WildlifeTagName)) return false;    // 防御：层被改也单发
        if (target.GetComponentInParent<FriendlyTroll>() != null) return false;
        Damageable damageable = target.GetComponent<Damageable>();
        if (damageable == null || !damageable.enabled || damageable.isDead) return false;
        return damageable.IsDamagedBy(DamageSource.Arrow);
    }

    /// <summary>Enemies 层索引（惰性缓存一次；解析失败返回 -1，调用方 fail-closed 且下次重试）。</summary>
    private static int EnemiesLayerIndex()
    {
        if (EnemiesLayer < 0)
        {
            string name = null;
            try { name = Layers.Enemies; } catch (Exception) { }
            EnemiesLayer = LayerMask.NameToLayer(string.IsNullOrEmpty(name) ? EnemiesLayerName : name);
        }
        return EnemiesLayer;
    }
}
