using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

namespace KingdomScatterTint.Tests
{
    /// <summary>
    /// 按 Harmony 的真实派发方式调用生产 patch 包装类：prefix 返回 false → 原生方法体不执行；
    /// 返回 true → 原生方法体执行（模型见 <see cref="Fixture.NativeReceiveBody"/>）。
    /// </summary>
    internal static class PatchBridge
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;

        internal static MethodInfo PrefixMethod
        {
            get
            {
                MethodInfo method = typeof(Arrow_ReceiveInitialise_Tint_Patch).GetMethod("Prefix", Flags);
                if (method == null) throw new Exception("production patch class has no Prefix");
                return method;
            }
        }

        internal static bool CallPrefix(Arrow arrow) => (bool)PrefixMethod.Invoke(null, new object[] { arrow });

        /// <summary>钩子面契约：唯一目标必须是 Arrow.ReceiveInitialise，且是返回 bool 的 HarmonyPrefix。</summary>
        internal static void AssertPatchContract()
        {
            var attribute = (HarmonyLib.HarmonyPatchAttribute)Attribute.GetCustomAttribute(
                typeof(Arrow_ReceiveInitialise_Tint_Patch), typeof(HarmonyLib.HarmonyPatchAttribute));
            if (attribute == null) throw new Exception("patch class is missing [HarmonyPatch]");
            if (attribute.DeclaringType != typeof(Arrow)) throw new Exception("patch targets " + attribute.DeclaringType);
            if (attribute.MethodName != "ReceiveInitialise") throw new Exception("patch targets " + attribute.MethodName);
            MethodInfo method = PrefixMethod;
            if (method.ReturnType != typeof(bool)) throw new Exception("prefix must return bool");
            if (Attribute.GetCustomAttribute(method, typeof(HarmonyLib.HarmonyPrefixAttribute)) == null)
                throw new Exception("prefix is missing [HarmonyPrefix]");
        }
    }

    /// <summary>一个测试用例的原生世界：当前 world/layer、配置、权威、时钟与两块缓冲语义。</summary>
    internal sealed class Fixture
    {
        internal const int MotionPrefixLength = 24;   // softsim.SendVelocity 的 float + vec2 + vec3

        internal int SceneHandle = 7;
        internal int NativeBodyRuns;
        internal int NativeLengthErrors;

        internal Managers Managers;
        internal World World;
        internal Transform Layer;

        internal Fixture()
        {
            ByteBuffer.ResetForTests();
            ScatterArrowTint.ResetForTests();
            TestLog.Reset();

            KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
            BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 3 };
            Managers = new Managers { game = new Game { playingOrInMenuWithClient = true } };
            Managers.Inst = Managers;
            Layer = NewLayer();
            World = new World { gameLayer = Layer };
            Managers.world = World;
            ModConfig.Enabled.Value = true;
            ModConfig.ArcherScatterEnabled.Value = true;
            NetworkBigBoss.HasWorldAuth = true;
            Time.unscaledTime = 0f;
        }

        internal Transform NewLayer()
        {
            var gameObject = new GameObject { scene = new Scene { handle = SceneHandle }, activeInHierarchy = true };
            var transform = new Transform { gameObject = gameObject };
            transform.Pointer = gameObject.Pointer;
            return transform;
        }

        /// <summary>新造一支箭（含同 GO 的 SpriteRenderer），模型池 Spawn 出来的活跃对象。</summary>
        internal Arrow NewArrow(bool fireArrow = false, bool withRenderer = true, Color? color = null)
        {
            var gameObject = new GameObject { scene = new Scene { handle = SceneHandle } };
            var transform = new Transform { gameObject = gameObject, Parent = Layer };
            gameObject.AddComponentForTests(transform);

            var arrow = new Arrow { gameObject = gameObject, transform = transform, isFireArrow = fireArrow };
            gameObject.AddComponentForTests(arrow);

            if (withRenderer)
            {
                var renderer = new SpriteRenderer { color = color ?? DefaultBase };
                gameObject.AddComponentForTests(renderer);
                renderer.transform = transform;
                arrow._spriteRenderer = renderer;
            }
            return arrow;
        }

        internal static readonly Color DefaultBase = new Color(0.25f, 0.5f, 0.75f, 1f);

        /// <summary>本模块写色公式的独立复算：lerp(base.rgb, (1,.92,.68), 0.65)，alpha 保持。</summary>
        internal static Color ExpectedTint(Color baseColor)
        {
            const float mix = 0.65f;
            return new Color(
                baseColor.r + (1f - baseColor.r) * mix,
                baseColor.g + (0.92f - baseColor.g) * mix,
                baseColor.b + (0.68f - baseColor.b) * mix,
                baseColor.a);
        }

        internal static bool SameColor(Color a, Color b)
        {
            const float eps = 1e-4f;
            return Math.Abs(a.r - b.r) <= eps && Math.Abs(a.g - b.g) <= eps
                && Math.Abs(a.b - b.b) <= eps && Math.Abs(a.a - b.a) <= eps;
        }

        /// <summary>发送侧：原有顺序 PrepWriteBuffer → 写 payload；返回 softsim 会发出去的 payload 字节。</summary>
        internal byte[] PrepareAndCapturePayload(bool perfectShot, bool tinted)
        {
            ByteBuffer.PrepWriteBuffer();
            ScatterArrowTint.WriteInitialisePayload(perfectShot, tinted);
            return ByteBuffer.WrittenForTests(0, ByteBuffer.PollIndex());
        }

        /// <summary>收包侧：运动信息（24B，已由 softsim.ReceiveVelocity 消费）之后是自有 payload。</summary>
        internal void LoadWire(byte[] payload)
        {
            var wire = new byte[MotionPrefixLength + payload.Length];
            for (int i = 0; i < MotionPrefixLength; i++) wire[i] = 0xAB;
            Array.Copy(payload, 0, wire, MotionPrefixLength, payload.Length);
            ByteBuffer.LoadReadableForTests(wire, MotionPrefixLength);
        }

        /// <summary><c>Float + Vector2 + Vector3</c> 之后原生 Arrow.ReceiveInitialise 的真实方法体。</summary>
        internal void NativeReceiveBody(Arrow arrow)
        {
            NativeBodyRuns++;
            if (arrow.isFireArrow) return;
            short remaining = ByteBuffer.PollDataAvailableLength();
            if (remaining != 1)
            {
                NativeLengthErrors++;
                return;
            }
            if (ByteBuffer.ReadBool()) arrow.PerfectShot();
        }

        /// <summary>Harmony 派发：prefix true → 原生体；prefix false → 跳过原生体。返回原生体是否执行。</summary>
        internal bool DispatchReceive(Arrow arrow)
        {
            if (PatchBridge.CallPrefix(arrow))
            {
                NativeReceiveBody(arrow);
                return true;
            }
            return false;
        }
    }
}
