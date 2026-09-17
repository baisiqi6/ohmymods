// 英雄弓箭手·有界只读“邻居真实高度/缩放”诊断（worker: hero-live-fixes-20260915 / guard；review 修订版）。
//
// 目的：把「附近弓箭手真的跳高度/改缩放」与「英雄自身轮廓/镜头移动造成的错觉」区分开。
// 触发：**由 operator 在 PromoteHero 真正成功之后调用** `OnHeroSetup(archer)`
//       （`HeroArcherVisuals.Apply` 之后且 `HasVisual` 为真）；Stop/Remove/Disable 由 operator 调 `Clear()`。
//       本模块**不**自己发现 hero（没有逐帧世界/NPC 扫描，也不挂任何 hook）。
// 绑定：只用既有 Kingdom.Archers（**不 FindObjects**），一次扫描选同 world、最近的最多 2 名其他弓箭手；
//       邻居身份按 goId + native pointer + life 冻结，绑定后绝不重扫。
// 采样：每 0.1s 一批、最多 12 批（约 1.2s），由既有 hero Tick 桥（HeroArcherGuardFacing.Tick）驱动。
//       每批只读：英雄/邻居 root position.y + localScale.y、原生 body（SpriteRenderer）的
//       enabled/sprite/bounds/position.y，以及 Camera.main 的 y。
// 中止：英雄或任一邻居的对象/指针/life 不再是同一人（池复用、换人、销毁）或已不在当前 world
//       （world/gameLayer 指针变化）→ 立即停止；世界/集合不可用有界等待后停止。
// 预算：每 arm 一次消费一个会话名额（**每个启用/世界上下文 ≤3 次会话**，只有显式 `Clear()`
//       ——停用/换世界——才重置），每会话 ≤12 批、≤2 邻居；同一 hero 生命只 arm 一次（ptr/GO/life 去重）。
//       绑定“放弃”原因与异常只记一次（日志去重），但**无论如何都会结束当前会话**，绝不停留在半开状态。
// 边界：**零写入**（不写原生字段/材质/transform/pool，不新增 RPC/扫描器/协程）；
//       单行日志用固定容量 StringBuilder；原生集合按 2.4 已知约定先 TryCast 到 HashSet 再枚举
//       （坑 26：异常一律 fail-closed，绝不重试轰炸）。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KingdomEnhancedMod;

internal static class HeroArcherLiveDiagnostics
{
    private const int MaxNeighbors = 2;
    private const int MaxBatches = 12;
    private const int MaxSessions = 3;
    private const int MaxBindAttempts = 5;
    private const int MaxScanEntries = 4096;
    private const float SampleIntervalSeconds = 0.1f;
    private const float MaxNeighborDistance = 30f;

    private sealed class Binding
    {
        internal Archer Ref;
        internal IntPtr Pointer;
        internal int GoId;
        internal int Life;
        internal SpriteRenderer Body;
        internal float Distance;
    }

    private static readonly Binding[] Neighbors = new Binding[MaxNeighbors];
    private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
    private static readonly System.Text.StringBuilder Line = new System.Text.StringBuilder(1024);

    private static int _bound;
    private static bool _armed;
    private static int _bindAttempts;
    private static int _batches;
    private static int _sessions;
    private static float _nextSampleAt;

    private static Archer _hero;
    private static IntPtr _heroPointer;
    private static int _heroGoId;
    private static int _heroLife;
    private static SpriteRenderer _heroBody;
    private static IntPtr _worldPointer;
    private static IntPtr _layerPointer;

    /// <summary>当前是否已绑定并在采样（自检/测试用）。</summary>
    internal static bool Active => _bound > 0;

    /// <summary>已产出的采样批数（自检/测试用）。</summary>
    internal static int Batches => _batches;

    /// <summary>已绑定的邻居数（自检/测试用）。</summary>
    internal static int BoundNeighbors => _bound;

    /// <summary>已消费的会话名额（自检/测试用）。</summary>
    internal static int Sessions => _sessions;

    /// <summary>
    /// hero setup 事件（operator 在 PromoteHero 成功挂载自有视觉之后调用）。
    /// 同一 hero 生命只 arm 一次；每个启用/世界上下文总名额 ≤ MaxSessions（arm 时即消费）。
    /// </summary>
    internal static void OnHeroSetup(Archer hero)
    {
        try
        {
            if (hero == null || hero.gameObject == null) return;
            if (_armed || _bound > 0) return;                    // 已有会话在跑：不打断、不叠加
            int goId = hero.gameObject.GetInstanceID();
            IntPtr pointer = hero.Pointer;
            if (goId == 0 || pointer == IntPtr.Zero) return;
            int life = HeroArcherRuntime.CurrentActorLife(hero);
            // 同一 hero 生命只 arm 一次（ptr + GO id + life；life 未知(0) 时按同一对象去重）
            if (goId == _heroGoId && pointer == _heroPointer && life == _heroLife) return;
            if (_sessions >= MaxSessions) return;                 // 预算在 arm 时消费，超出直接拒绝

            _sessions++;
            _armed = true;
            _bindAttempts = 0;
            _hero = hero;
            _heroGoId = goId;
            _heroPointer = pointer;
            _heroLife = life;
            _heroBody = null;
        }
        catch (Exception)
        {
        }
    }

    /// <summary>每帧一次（由 HeroArcherGuardFacing.Tick 在既有 hero Tick 桥上驱动）。</summary>
    internal static void Tick()
    {
        try
        {
            if (_bound == 0)
            {
                if (_armed) TryBind();
                return;
            }
            if (_batches >= MaxBatches)
            {
                Stop("complete");
                return;
            }
            float now = Time.time;
            if (now < _nextSampleAt) return;
            _nextSampleAt = now + SampleIntervalSeconds;
            _batches++;
            Emit();
            if (_batches >= MaxBatches) Stop("complete");
        }
        catch (Exception e)
        {
            Fail("tick", e);
        }
    }

    /// <summary>
    /// 全量作废并**重置会话预算**（operator 接线：英雄 Remove / 模组停用 / 世界切换）。
    /// 只读模块没有任何写入需要归还。
    /// </summary>
    internal static void Clear()
    {
        ClearSession();
        _sessions = 0;
        _heroGoId = 0;
        _heroPointer = IntPtr.Zero;
        _heroLife = 0;
    }

    /// <summary>结束当前会话但**保留预算与 hero 去重键**（Stop/中止/完成走这里）。</summary>
    private static void ClearSession()
    {
        _armed = false;
        _bound = 0;
        _batches = 0;
        _hero = null;
        _heroBody = null;
        _worldPointer = IntPtr.Zero;
        _layerPointer = IntPtr.Zero;
        for (int i = 0; i < Neighbors.Length; i++) Neighbors[i] = null;
    }

    // ============================================================
    // 绑定（一次扫描，绝不重复取场景）
    // ============================================================

    private static void TryBind()
    {
        _bindAttempts++;
        try
        {
            Managers managers = Managers.Inst;
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            World world = managers != null ? managers.world : null;
            Transform layer = world != null ? world.gameLayer : null;
            if (kingdom == null || world == null || layer == null)
            {
                // 世界未就绪：有界等待（唯一允许等待的失败）。
                if (_bindAttempts >= MaxBindAttempts) Stop("world-not-ready", true);
                return;
            }

            if (_hero == null || _hero.gameObject == null || !_hero.gameObject.activeInHierarchy)
            {
                Stop("hero-inactive");
                return;
            }
            int heroGoId = _hero.gameObject.GetInstanceID();
            if (heroGoId != _heroGoId || _hero.Pointer != _heroPointer)
            {
                Stop("hero-identity-changed");
                return;
            }
            if (HeroArcherRuntime.CurrentActorLife(_hero) != _heroLife)
            {
                Stop("hero-identity-changed");
                return;
            }

            // 原生集合：2.4 ICollection 不暴露 CLR 枚举（同 OptionalVegetation 注释），先 Cast 到
            // HashSet 再枚举；任何异常一律 fail-closed（坑 26）。
            var collection = kingdom.Archers;
            if (collection == null)
            {
                if (_bindAttempts >= MaxBindAttempts) Stop("archers-unavailable", true);
                return;
            }
            var set = collection.TryCast<Il2CppSystem.Collections.Generic.HashSet<Archer>>();
            if (set == null)
            {
                Stop("archers-not-hashset");
                return;
            }
            int count = set.Count;
            if (count > MaxScanEntries)
            {
                Stop("archers-too-many");
                return;
            }

            float heroX = _hero.transform.position.x;
            int examined = 0;
            if (count > 0)
            {
                foreach (Archer other in set)
                {
                    examined++;
                    if (examined > MaxScanEntries) break;
                    if (other == null || other.gameObject == null || !other.gameObject.activeInHierarchy) continue;
                    if (other.Pointer == _heroPointer || other.gameObject.GetInstanceID() == _heroGoId) continue;
                    if (!other.transform.IsChildOf(layer)) continue;    // 同 world
                    Damageable damageable = other._damageable;
                    if (damageable == null || damageable.isDead) continue;
                    SpriteRenderer body = FindBody(other);
                    if (body == null) continue;
                    float distance = Mathf.Abs(other.transform.position.x - heroX);
                    if (!float.IsFinite(distance) || distance > MaxNeighborDistance) continue;
                    int life = HeroArcherRuntime.CurrentActorLife(other);
                    if (life <= 0) continue;                     // 无 life 凭据：身份核对不成立，不用
                    Offer(other, body, life, distance);
                }
            }

            if (_bound == 0)
            {
                // 扫描已完成且没有合格邻居（含空集合）：立即结束，不再等待。
                Stop("no-neighbor", true);
                return;
            }

            _armed = false;
            _batches = 0;
            _nextSampleAt = Time.time;   // 绑定成功当帧之后立即出第一批
            _worldPointer = world.Pointer;
            _layerPointer = layer.Pointer;
            Log("[HeroGuardDiag] bind hero=" + _heroGoId + " life=" + _heroLife
                + " neighbors=" + _bound + " candidates=" + examined + " session=" + _sessions + "/" + MaxSessions);
        }
        catch (Exception e)
        {
            Fail("bind", e);
        }
    }

    /// <summary>保持“最近的 ≤2 名”：距离更近就顶掉较远者。</summary>
    private static void Offer(Archer archer, SpriteRenderer body, int life, float distance)
    {
        for (int i = 0; i < MaxNeighbors; i++)
        {
            if (Neighbors[i] == null)
            {
                Neighbors[i] = Make(archer, body, life, distance);
                _bound++;
                return;
            }
        }
        int worst = 0;
        float worstDistance = Neighbors[0] != null ? Neighbors[0].Distance : float.MaxValue;
        for (int i = 1; i < MaxNeighbors; i++)
        {
            float candidate = Neighbors[i] != null ? Neighbors[i].Distance : float.MaxValue;
            if (candidate > worstDistance)
            {
                worst = i;
                worstDistance = candidate;
            }
        }
        if (distance < worstDistance) Neighbors[worst] = Make(archer, body, life, distance);
    }

    private static Binding Make(Archer archer, SpriteRenderer body, int life, float distance)
    {
        return new Binding
        {
            Ref = archer,
            Pointer = archer.Pointer,
            GoId = archer.gameObject.GetInstanceID(),
            Life = life,
            Body = body,
            Distance = distance,
        };
    }

    private static SpriteRenderer FindBody(Archer archer)
    {
        try
        {
            SpriteRenderer renderer = archer.gameObject.GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.gameObject != null) return renderer;
            renderer = archer.GetComponentInChildren<SpriteRenderer>();
            return renderer != null && renderer.gameObject != null ? renderer : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ============================================================
    // 采样（只读；身份 + 当前 world 每批核对）
    // ============================================================

    private static void Emit()
    {
        if (!Identity(_hero, _heroPointer, _heroGoId, _heroLife, _heroBody, allowNoBody: true))
        {
            Stop("hero-identity-changed");
            return;
        }
        if (!InCurrentWorld(_hero))
        {
            Stop("world-changed");
            return;
        }
        if (_heroBody == null) _heroBody = FindBody(_hero);

        Line.Clear();
        Line.Append("[HeroGuardDiag] b=").Append(_batches).Append('/').Append(MaxBatches);
        AppendCamera();
        Append("hero", _hero, _heroBody);
        for (int i = 0; i < MaxNeighbors; i++)
        {
            Binding binding = Neighbors[i];
            if (binding == null) continue;
            if (!Identity(binding.Ref, binding.Pointer, binding.GoId, binding.Life, binding.Body, allowNoBody: false))
            {
                Stop("neighbor-identity-changed");
                return;
            }
            if (!InCurrentWorld(binding.Ref))
            {
                Stop("world-changed");
                return;
            }
            Append("n" + i, binding.Ref, binding.Body);
        }
        Log(Line.ToString());
    }

    /// <summary>身份核对：对象存活 + native pointer + GO id + life（不做世界判断，见 InCurrentWorld）。</summary>
    private static bool Identity(Archer archer, IntPtr pointer, int goId, int life, SpriteRenderer body, bool allowNoBody)
    {
        try
        {
            if (archer == null || archer.gameObject == null || !archer.gameObject.activeInHierarchy) return false;
            if (archer.Pointer != pointer) return false;
            if (archer.gameObject.GetInstanceID() != goId) return false;
            if (HeroArcherRuntime.CurrentActorLife(archer) != life) return false;
            if (!allowNoBody && (body == null || body.gameObject == null)) return false;
            if (body != null && body.gameObject == null) return false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>当前世界上下文核对（绑定时的 world/gameLayer 指针 + 仍在 gameLayer 下）；不扫描。</summary>
    private static bool InCurrentWorld(Archer archer)
    {
        try
        {
            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            Transform layer = world != null ? world.gameLayer : null;
            if (world == null || layer == null) return false;
            if (world.Pointer != _worldPointer || layer.Pointer != _layerPointer) return false;
            return archer.transform.IsChildOf(layer);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void AppendCamera()
    {
        Line.Append(" camY=");
        try
        {
            Camera camera = Camera.main;
            Line.Append(camera != null ? Num(camera.transform.position.y, "F2") : "na");
        }
        catch (Exception)
        {
            Line.Append("na");
        }
    }

    private static void Append(string tag, Archer archer, SpriteRenderer body)
    {
        Line.Append(' ').Append(tag).Append("=(id=").Append(IdOf(archer));
        try
        {
            Transform root = archer.transform;
            Line.Append(", x=").Append(Num(root.position.x, "F2"))
                .Append(", y=").Append(Num(root.position.y, "F3"))
                .Append(", sy=").Append(Num(root.localScale.y, "F3"));
        }
        catch (Exception)
        {
            Line.Append(", root=na");
        }
        if (body == null)
        {
            Line.Append(", body=na)");
            return;
        }
        try
        {
            Line.Append(", en=").Append(body.enabled ? "1" : "0")
                .Append(", by=").Append(Num(body.transform.position.y, "F3"))
                .Append(", bb=[").Append(Num(body.bounds.min.y, "F3")).Append("..").Append(Num(body.bounds.max.y, "F3")).Append(']')
                .Append(", sp=").Append(body.sprite != null ? body.sprite.name : "null")
                .Append(')');
        }
        catch (Exception)
        {
            Line.Append(", body=na)");
        }
    }

    private static int IdOf(Archer archer)
    {
        try
        {
            return archer != null && archer.gameObject != null ? archer.gameObject.GetInstanceID() : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string Num(float value, string format)
    {
        return float.IsFinite(value) ? value.ToString(format, CultureInfo.InvariantCulture) : "nan";
    }

    // ============================================================
    // 收尾
    // ============================================================

    /// <summary>结束当前会话（保留预算/去重键）。dedupeLog 只影响日志行，绝不影响“必须结束”本身。</summary>
    private static void Stop(string reason, bool dedupeLog = false)
    {
        if (!dedupeLog || Logged.Add(reason))
            Log("[HeroGuardDiag] stop reason=" + reason + " batches=" + _batches + " neighbors=" + _bound);
        ClearSession();
    }

    private static void Fail(string key, Exception e)
    {
        if (Logged.Add(key)) Log("[HeroGuardDiag] " + key + " failed: " + e.GetType().Name);
        Stop("error", true);   // 重复同 key 也必须结束会话：只做日志去重
    }

    private static void Log(string message)
    {
        try
        {
            KingdomEnhancedPlugin.Instance?.LogSource?.LogInfo(message);
        }
        catch (Exception)
        {
        }
    }
}
