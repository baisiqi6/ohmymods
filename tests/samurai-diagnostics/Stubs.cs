// 直接源测试的最小 Unity/BepInEx 边界桩：只覆盖 SamuraiDashDiagnostics 用到的成员。
// 原生边界用 ContextReads 计数、ThrowOnContext 模拟故障，用于验证"限流早于原生读取"。
namespace UnityEngine
{
    public class Object
    {
        private static long next;
        public IntPtr Pointer { get; set; } = (IntPtr)Interlocked.Increment(ref next);
        public static implicit operator bool(Object obj) => obj is not null;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => Pointer.GetHashCode();
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0f, float z = 0f) { this.x = x; this.y = y; this.z = z; }
    }

    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector3 position;
    }

    public class GameObject : Object
    {
        private readonly Transform own;
        public int Id;
        public int ContextReads;
        public bool ThrowOnContext;
        public GameObject() { Id = (int)Pointer; own = new Transform { gameObject = this }; }
        public Transform transform
        {
            get
            {
                ContextReads++;
                if (ThrowOnContext) throw new InvalidOperationException("native transform");
                return own;
            }
        }
        public int GetInstanceID()
        {
            ContextReads++;
            if (ThrowOnContext) throw new InvalidOperationException("native instance id");
            return Id;
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }

    public class MonoBehaviour : Component { }
}

namespace KingdomEnhancedMod
{
    // 生产里 Knight : MonoBehaviour（Assembly-CSharp 的 IL2CPP 包装类型）。
    public class Knight : UnityEngine.MonoBehaviour { }

    public sealed class LogSourceStub
    {
        public readonly List<string> Lines = new();
        public Action<string> OnInfo; // 测试用它注入抛错 sink；抛错时同真实日志一样不留行
        public void LogInfo(string line)
        {
            OnInfo?.Invoke(line);
            Lines.Add(line);
        }
    }

    public sealed class PluginStub { public LogSourceStub LogSource = new(); }

    // 生产里是 BepInEx BasePlugin 单例；诊断只用 Instance?.LogSource.LogInfo(string)。
    public static class KingdomEnhancedPlugin { public static PluginStub Instance; }
}
