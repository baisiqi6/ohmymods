using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>Test double: only the Unity surface the production module and the harness need.</summary>
    public class Object
    {
        public string name = "object";
        public IntPtr Pointer;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public string tag = "";
        public bool enabled = true;

        public T GetComponent<T>() where T : Component
        {
            return gameObject != null ? gameObject.GetComponent<T>() : null;
        }
    }

    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour { }

    public sealed class Transform : Component
    {
        public Transform parent;

        public bool IsChildOf(Transform other)
        {
            if (other == null) return false;
            Transform node = this;
            while (node != null)
            {
                if (ReferenceEquals(node, other)) return true;
                node = node.parent;
            }
            return false;
        }
    }

    public struct Scene
    {
        public int handle;
        public bool IsValid() { return handle != 0; }
    }

    public sealed class GameObject : Object
    {
        private static int _nextPointer = 90000;

        private readonly List<Component> _components = new List<Component>();
        private int _instanceId;

        public bool activeInHierarchy = true;
        public Scene scene;

        public GameObject(string goName)
        {
            name = goName;
            Pointer = new IntPtr(++_nextPointer);
            var transform = new Transform();
            transform.gameObject = this;
            transform.transform = transform;
            transform.name = goName;
            transform.Pointer = new IntPtr(++_nextPointer);
            _components.Add(transform);
        }

        public Transform transform { get { return (Transform)_components[0]; } }

        public void AssignInstanceId(int id) { _instanceId = id; }

        public int GetInstanceID() { return _instanceId; }

        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T();
            component.gameObject = this;
            component.transform = transform;
            component.name = name + ":" + typeof(T).Name;
            component.Pointer = new IntPtr(++_nextPointer);
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : Component
        {
            for (int i = 0; i < _components.Count; i++)
            {
                if (_components[i] is T typed) return typed;
            }
            return null;
        }
    }
}
