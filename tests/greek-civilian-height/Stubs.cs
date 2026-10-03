// GreekCivilianHeight 套件的边界替身：只满足真实 GreekScaleScope.cs 编译与驱动所需
// （Unity 假 null 语义、scope 判定、日志缝）。没有 Unity 运行时，也没有游戏进程。
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        private static int _next;
        public IntPtr Pointer = new IntPtr(++_next);
        public int GetInstanceID() => (int)Pointer;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public class GameObject : Object
    {
        public string name;
        public bool activeInHierarchy = true;
        public Scene scene = new Scene { valid = true };
        public Transform transform;
        private readonly List<Component> _components = new List<Component>();

        public GameObject(string name = "actor")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
        }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in _components)
                if (component is T typed) return typed;
            return null;
        }
    }

    public struct Scene
    {
        public bool valid;
        public bool IsValid() => valid;
    }

    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float this[int index]
        {
            get => index == 0 ? x : index == 1 ? y : z;
            set
            {
                if (index == 0) x = value;
                else if (index == 1) y = value;
                else z = value;
            }
        }

        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public class Transform : Component
    {
        public Vector3 localScale = new Vector3(1f, 1f, 1f);
    }

    public static class Time
    {
        public static int frameCount;
    }
}

public class Mover : UnityEngine.Component
{
    public bool enabled = true;
}

public class Embarkee : UnityEngine.Component
{
    public bool IsEmbarked;
    public bool IsTargetingEmbarkable;
}

public class BiomeHolder
{
    public static BiomeHolder Inst = new BiomeHolder();
    public const int GreeceBiomeIndex = 5;
    public int BiomeIndex;
}

namespace KingdomEnhancedMod
{
    internal static class ModConfig
    {
        public sealed class Setting
        {
            public bool Value = true;
        }

        public static Setting Enabled = new Setting();
    }

    public sealed class KingdomEnhancedPlugin
    {
        public static KingdomEnhancedPlugin Instance = new KingdomEnhancedPlugin();

        public LogSink LogSource = new LogSink();

        public sealed class LogSink
        {
            public void LogInfo(string message) { }
            public void LogWarning(string message) { }
        }
    }

    internal static class ScaleRegistryHolder
    {
        public static void RetryPendingCreation() { }
    }
}
