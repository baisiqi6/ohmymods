// 测试专用最小 UnityEngine 替身（只为在无 Unity 环境下驱动 HeroVisualPriority 的纯深度规则；
// 真实 interop 由 operator 统一构建）。本文件绝不进入游戏程序集。
//
// 简化约定（覆盖本测试所需即可）：旋转只出现在 gameLayer 自身的 localRotation（绕 Y/X 轴），
// 任何父节点不旋转；缩放可出现在任意层级。TRS 合成按此假设实现。

using System;

namespace UnityEngine
{
    internal struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    internal struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { x = 0f, y = 0f, z = 0f, w = 1f };

        public static Quaternion AngleAxis(float degrees, Vector3 axis)
        {
            float rad = degrees * (float)Math.PI / 180f;
            float s = (float)Math.Sin(rad / 2f);
            float len = (float)Math.Sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
            return new Quaternion { x = axis.x / len * s, y = axis.y / len * s, z = axis.z / len * s, w = (float)Math.Cos(rad / 2f) };
        }

        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            float ix = q.w * v.x + q.y * v.z - q.z * v.y;
            float iy = q.w * v.y + q.z * v.x - q.x * v.z;
            float iz = q.w * v.z + q.x * v.y - q.y * v.x;
            float iw = -q.x * v.x - q.y * v.y - q.z * v.z;
            return new Vector3(
                ix * q.w + iw * -q.x + iy * -q.z - iz * -q.y,
                iy * q.w + iw * -q.y + iz * -q.x - ix * -q.z,
                iz * q.w + iw * -q.z + ix * -q.y - iy * -q.x);
        }

        public Quaternion Inverse => new Quaternion { x = -x, y = -y, z = -z, w = w };
    }

    internal sealed class GameObject
    {
        public string name;
        public GameObject(string name) { this.name = name; }
    }

    /// <summary>最小 Transform：局部 TRS + 父链；position/TransformPoint 走 TRS 合成（父不旋转）。</summary>
    internal sealed class Transform
    {
        internal Transform parent;
        internal GameObject gameObject;
        internal Vector3 localPosition = Vector3.zero;
        internal Quaternion localRotation = Quaternion.identity;
        internal Vector3 localScale = Vector3.one;
        internal bool ThrowOnNextPositionSet; // 一次性失败桩：下一次 position setter 抛异常（用后自清）

        internal Transform(GameObject go) { gameObject = go; }

        internal void SetParent(Transform p, bool worldPositionStays)
        {
            parent = p; // 本替身不做 worldPositionStays 差异（生产调用点都用 false，从零局部开始）
        }

        internal Vector3 lossyScale
        {
            get
            {
                Vector3 s = localScale;
                Transform p = parent;
                while (p != null) { s = Vector3.Scale(s, p.localScale); p = p.parent; }
                return s;
            }
        }

        internal Vector3 position
        {
            get
            {
                Vector3 lp = Vector3.Scale(localRotation * localPosition, lossyScale);
                return parent == null ? lp : lp + parent.position;
            }
            set
            {
                if (ThrowOnNextPositionSet)
                {
                    ThrowOnNextPositionSet = false;
                    throw new InvalidOperationException("stub position setter failed (one-shot)");
                }
                if (parent == null) { localPosition = value; return; }
                Vector3 ps = parent.lossyScale;
                Vector3 d = parent.localRotation.Inverse * (value - parent.position);
                localPosition = new Vector3(
                    ps.x == 0f ? d.x : d.x / ps.x,
                    ps.y == 0f ? d.y : d.y / ps.y,
                    ps.z == 0f ? d.z : d.z / ps.z);
            }
        }

        internal Vector3 TransformPoint(Vector3 local)
        {
            return position + Vector3.Scale(localRotation * local, lossyScale);
        }
    }
}
