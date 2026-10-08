using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
#if ANDROID
// Android（Il2CppInterop namespace-prefix 模式）把 Assembly-CSharp 的全局类型放在 Il2Cpp.* 下；
// 本文件与 PC 共用同一份诊断逻辑，仅在此映射游戏类型（World 由 android/GlobalAliases.cs 提供），
// 并在观察入口加纯托管门。PC 分支逐字保留，预处理输出不变。
using Beggar = Il2Cpp.Beggar;
using BeggarCamp = Il2Cpp.BeggarCamp;
#endif

namespace KingdomEnhancedMod;

// Read-only evidence for the reported Beggar fall-through. Runs inside the existing
// governor reconciliation, never scans a scene or changes physics/position. First observed
// is deliberately not called "spawn": loaded actors and native spawns share that boundary.
internal static class PopulationGrounding
{
    private const int SampleBudget = 12;
    private static IntPtr _world, _layer;
    private static int _generation, _firstSamples, _spawnSamples, _fallSamples;
    private static bool _readFailureLogged;
    private static readonly HashSet<(long Pointer, int Instance, int Epoch)> Fallen = new();

    internal static void Begin(World world, Transform layer, int generation)
    {
        Reset();
        try
        {
            if (world == null || layer == null) return;
            _world = world.Pointer; _layer = layer.Pointer; _generation = generation;
        }
        catch { Reset(); } // Diagnostic setup never changes scene/governor success.
    }

    internal static void Reset()
    {
        _world = _layer = IntPtr.Zero;
        _firstSamples = _spawnSamples = _fallSamples = 0;
        _readFailureLogged = false; Fallen.Clear();
    }

    internal static void Observe(Beggar beggar, BeggarCamp owner, int epoch, bool firstObserved,
        bool centralSpawn = false)
    {
#if ANDROID
        // ANDROID：观察入口纯托管门；补员门关闭且无自有责任时零 native 读取。
        if (!PopulationPerformanceCoordinator.PopulationRequested) return;
#endif
        try
        {
            if (_world == IntPtr.Zero || !ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth
                || Time.timeScale <= 0f || beggar == null) return;
            var world = Managers.Inst?.world;
            var layer = world?.gameLayer;
            if (world == null || world.Pointer != _world || layer == null || layer.Pointer != _layer) return;
            var root = beggar.gameObject;
            if (root == null || !root.activeInHierarchy || root.scene.handle != layer.gameObject.scene.handle
                || !root.transform.IsChildOf(layer)) return;
            if (_fallSamples >= SampleBudget && (!firstObserved || _firstSamples >= SampleBudget)
                && (!centralSpawn || _spawnSamples >= SampleBudget)) return;
            Vector3 position = root.transform.position;
            bool low = !float.IsFinite(position.y) || position.y < layer.position.y - 2f;
            bool fall = low && _fallSamples < SampleBudget
                && Fallen.Add((beggar.Pointer.ToInt64(), root.GetInstanceID(), epoch));
            string source;
            if (fall) { _fallSamples++; source = "below-layer"; }
            else if (centralSpawn && _spawnSamples < SampleBudget) { _spawnSamples++; source = "central-spawn"; }
            else if (firstObserved && _firstSamples < SampleBudget) { _firstSamples++; source = "first-observed"; }
            else return;
            // Consume a bounded sample before reading native details. A bad wrapper cannot
            // trigger endless logs; the outer catch isolates diagnostics from the governor.
            var text = new StringBuilder("[PopulationGround] source=").Append(source)
                .Append(" generation=").Append(_generation).Append(" epoch=").Append(epoch)
                .Append(" t=").Append(F(Time.time)).Append(" name=").Append(root.name)
                .Append(" id=").Append(root.GetInstanceID())
                .Append(" net=").Append(beggar.parentHeaderRef == null ? "none" : beggar.parentHeaderRef.NetID.ToString())
                .Append(" pos=").Append(V(position)).Append(" local=").Append(V(root.transform.localPosition))
                .Append(" scale=").Append(V(root.transform.lossyScale)).Append(" layer=").Append(root.layer)
                .Append(" parent=").Append(root.transform.parent == null ? "none" : root.transform.parent.name)
                .Append(" nativeCamp=").Append(Camp(beggar.camp)).Append(" ownerCamp=").Append(Camp(owner));
            var body = root.GetComponent<Rigidbody2D>();
#if ANDROID
            // Android：actual metadata（Unity 6000.0.61）里 simulated/constraints/collisionDetectionMode 的
            // getter 被 strip（recon：get_simulated nativeMatches=0），这里不调用这些成员、固定输出
            // unavailable（不是 false/0，无 fallback/guard）；velocity 只读一次 getter 再取 x/y。
            if (body == null) text.Append(" body=missing");
            else
            {
                Vector2 velocity = body.velocity;
                text.Append(" body=").Append(body.bodyType)
                    .Append(" simulated=unavailable gravity=").Append(F(body.gravityScale))
                    .Append(" constraints=unavailable detect=unavailable")
                    .Append(" velocity=").Append(F(velocity.x)).Append(',').Append(F(velocity.y));
            }
#else
            if (body == null) text.Append(" body=missing");
            else text.Append(" body=").Append(body.bodyType).Append(" simulated=").Append(body.simulated)
                .Append(" gravity=").Append(F(body.gravityScale)).Append(" constraints=").Append(body.constraints)
                .Append(" detect=").Append(body.collisionDetectionMode)
                .Append(" velocity=").Append(F(body.velocity.x)).Append(',').Append(F(body.velocity.y));
#endif
            var ground = World.GroundCollider;
            text.Append(" ground=").Append(ground == null ? "missing" : ground.gameObject.name + ":" + ground.gameObject.layer
                + ":enabled=" + ground.enabled + ":trigger=" + ground.isTrigger + ":top=" + F(ground.bounds.max.y));
            var colliders = root.GetComponentsInChildren<Collider2D>(true);
            text.Append(" colliders=[");
            for (int i = 0; colliders != null && i < colliders.Length && i < 8; i++)
            {
                var collider = colliders[i];
                if (collider == null) continue;
                if (i != 0) text.Append(';');
                text.Append(collider.gameObject.name).Append(':').Append(collider.gameObject.layer)
                    .Append(":enabled=").Append(collider.enabled).Append(":active=").Append(collider.gameObject.activeInHierarchy)
                    .Append(":trigger=").Append(collider.isTrigger).Append(":minY=").Append(F(collider.bounds.min.y));
                // The audited prefab's physical layer is 17 and the terrain is layer 0.
                // Query only that evidenced pair; unrelated collision masks stay unobserved.
                if (ground != null && collider.gameObject.layer == 17 && ground.gameObject.layer == 0)
#if ANDROID
                    // Android：Physics2D.GetIgnoreLayerCollision 公共 API 与其 _Internal callee 都被 strip，
                    // 保持原配对条件与输出位置，改输出 unavailable（不新增查询）。
                    text.Append(":ignoreGround=unavailable");
#else
                    text.Append(":ignoreGround=").Append(Physics2D.GetIgnoreLayerCollision(17, 0));
#endif
            }
            text.Append(']');
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(text.ToString());
        }
        catch (Exception error)
        {
            if (_readFailureLogged) return;
            _readFailureLogged = true;
#if ANDROID
            // ANDROID：同一条一次性 warning 追加 ICall 失败的实际 message 与 stack（预算/内部 try-catch、
            // 成功路径、Budget/Reset、catch 范围均不变）；PC 原日志行在 #else 逐字保留。
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[PopulationGround] diagnostic read failed: " + error.GetType().Name + " message=" + error.Message + Environment.NewLine + error.StackTrace); }
            catch { }
#else
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[PopulationGround] diagnostic read failed: " + error.GetType().Name); }
            catch { }
#endif
        }
    }

    private static string Camp(BeggarCamp camp) => camp == null ? "none"
        : camp.gameObject.name + ":" + camp.gameObject.GetInstanceID() + ":" + V(camp.transform.position);
    private static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string V(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
}
