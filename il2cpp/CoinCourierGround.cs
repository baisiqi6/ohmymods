using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 局部地面/空间验证：地面掩码来自当前世界真实 GroundCollider 所在层，
/// 落点/站位用一次局部射线确认（绝不用 GroundCollider.bounds.max.y 冒充"处处可站"），
/// 净空用原生 "Obstacles" 掩码的点查询。任何读不到/异常一律 fail-closed。
/// </summary>
internal static class CoinCourierGround
{
    private const int ProbeCapacity = 4;

    private static int _groundMask;
    private static int _obstacleMask;
    private static Il2CppStructArray<RaycastHit2D> _probeHits;

    internal static void Invalidate() { _groundMask = 0; _obstacleMask = 0; }

    private static bool EnsureMasks()
    {
        if (_groundMask != 0) return true;
        try
        {
            BoxCollider2D ground = World.GroundCollider;
            if (ground == null) return false;
            GameObject root = ground.gameObject;
            if (root == null) return false;
            int layer = root.layer;
            if (layer < 0 || layer > 31) return false;
            _groundMask = 1 << layer;
            if (_obstacleMask == 0) _obstacleMask = LayerMask.GetMask("Obstacles");
            return _groundMask != 0;
        }
        catch (Exception)
        {
            _groundMask = 0;
            return false;
        }
    }

    /// <summary>在 x 处向下做一次局部线段查询，返回真实地面表面高度（最高命中面）。</summary>
    internal static bool TryProbe(float x, out float groundY)
    {
        groundY = 0f;
        if (!float.IsFinite(x) || !EnsureMasks()) return false;
        try
        {
            BoxCollider2D ground = World.GroundCollider;
            if (ground == null) return false;
            float top = ground.bounds.max.y;
            if (!float.IsFinite(top)) return false;
            if (_probeHits == null || _probeHits.Length != ProbeCapacity)
                _probeHits = new Il2CppStructArray<RaycastHit2D>(ProbeCapacity);
            // 与弹道侧同一已验证重载：LinecastNonAlloc(Vector2, Vector2, Il2CppStructArray<RaycastHit2D>, int)。
            float startY = top + 2f;
            int hits = Physics2D.LinecastNonAlloc(new Vector2(x, startY),
                new Vector2(x, top - CoinCourierTiming.GroundProbeDepth), _probeHits, _groundMask);
            if (hits <= 0 || hits > _probeHits.Length) return false;
            float best = float.NegativeInfinity;
            for (int i = 0; i < hits; i++)
            {
                RaycastHit2D hit = _probeHits[i];
                if (hit.collider == null) continue;
                // 竖直向下线段：命中高度 = 起点高度 - 沿途距离（只用已验证的 distance 字段）。
                float y = startY - hit.distance;
                if (float.IsFinite(y) && y > best) best = y;
            }
            if (!float.IsFinite(best)) return false;
            groundY = best;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 该点是否没有真实障碍物（墙/建筑）。用一次完整局部列表查询（不截断），只认
    /// 非触发体且 bounds 覆盖该点的 collider；触发体（无关 FX）不阻站位。
    /// 掩码/查询不可用时 fail-closed。
    /// </summary>
    internal static bool ObstacleFree(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) return false;
        if (!EnsureMasks()) return false;
        if (_obstacleMask == 0) return false;
        try
        {
            const float half = 0.05f;
            var hits = Physics2D.OverlapAreaAll(new Vector2(x - half, y - half),
                new Vector2(x + half, y + half), _obstacleMask);
            if (hits == null) return false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D collider = hits[i];
                if (collider == null || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                if (!float.IsFinite(bounds.min.x) || !float.IsFinite(bounds.max.x)
                    || !float.IsFinite(bounds.min.y) || !float.IsFinite(bounds.max.y)) continue;
                if (x >= bounds.min.x && x <= bounds.max.x && y >= bounds.min.y && y <= bounds.max.y)
                    return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>地面存在且落点净空：所有显形/站位都以此为准。</summary>
    internal static bool TryStandable(float x, out float groundY)
    {
        groundY = 0f;
        if (!TryProbe(x, out groundY)) return false;
        return ObstacleFree(x, groundY + CoinCourierTiming.LandingClearance);
    }

    /// <summary>
    /// 固定主城锚（招募标记与已招哥布林出生/回家共用）：x = 营火 + <see cref="CoinCourierTiming.HomeOffsetX"/>，
    /// y 只用该点一次 <see cref="TryProbe"/> 读当前真实地面。没有候选窗口/建筑排除/净空/付款避让；
    /// 世界或地面不可读时返回 false——调用方等待，绝不猜高度、绝不重选点。
    /// </summary>
    internal static bool TryResolveHome(Kingdom kingdom, out float x, out float y)
    {
        x = 0f;
        y = 0f;
        if (kingdom == null) return false;
        float campfire;
        try { campfire = kingdom.campfirePosition; } catch (Exception) { return false; }
        if (!float.IsFinite(campfire)) return false;
        float homeX = campfire + CoinCourierTiming.HomeOffsetX;
        if (!float.IsFinite(homeX)) return false;
        if (!TryProbe(homeX, out float groundY) || !float.IsFinite(groundY)) return false;
        x = homeX;
        y = groundY;
        return true;
    }
}
