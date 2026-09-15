// 测试专用最小 UnityEngine 替身（只覆盖 HeroArcherNativePose 的 Unity 桥所需成员）。
//
// 重要：Animator.StringToHash 在本替身里是**任意**映射 —— 运行时真值由引擎的同一函数给出，
// 桥调用的就是引擎函数本身（不手抄魔数）。本替身验证的是桥的行为契约：
// 读哪些成员、layer 取几、current/next 如何选择、hash → 动作 → 帧的映射与失败收敛。
// 本文件绝不进入游戏程序集（只在 tests/native-pose 参与编译）。

using System;

namespace UnityEngine
{
    /// <summary>AnimatorStateInfo 替身：只保留桥读取的两个字段。</summary>
    public struct AnimatorStateInfo
    {
        public int shortNameHash;
        public float normalizedTime;

        public AnimatorStateInfo(int shortNameHash, float normalizedTime)
        {
            this.shortNameHash = shortNameHash;
            this.normalizedTime = normalizedTime;
        }
    }

    /// <summary>
    /// Animator 替身：可控的 current/next/转场/异常/启用状态，并记录桥请求的 layer。
    /// </summary>
    public class Animator
    {
        public bool isActiveAndEnabled = true;
        public AnimatorStateInfo Current;
        public AnimatorStateInfo Next;
        public bool InTransition;
        /// <summary>true → 任何读取都抛异常（模拟 Animator 半销毁/不可读）。</summary>
        public bool ThrowOnRead;
        /// <summary>最近一次读取的 layer（断言桥只用 base layer 0）。</summary>
        public int LastLayer = -1;
        /// <summary>GetNextAnimatorStateInfo 被读取的次数（断言非转场帧不读 next）。</summary>
        public int NextReads;

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer)
        {
            LastLayer = layer;
            if (ThrowOnRead) throw new InvalidOperationException("animator unreadable");
            return Current;
        }

        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer)
        {
            LastLayer = layer;
            NextReads++;
            if (ThrowOnRead) throw new InvalidOperationException("animator unreadable");
            return Next;
        }

        public bool IsInTransition(int layer)
        {
            LastLayer = layer;
            if (ThrowOnRead) throw new InvalidOperationException("animator unreadable");
            return InTransition;
        }

        /// <summary>状态名 → 任意但稳定的 hash（真值由引擎函数给出；见文件头说明）。</summary>
        public static int StringToHash(string name)
        {
            switch (name)
            {
                case "Stand": return 1001;
                case "Walk": return 1002;
                case "Run": return 1003;
                case "Prepare": return 1004;
                case "Shoot": return 1005;
                case "Ghost Die": return 1006;
                case "Spawn": return 1007;
                default: return 9000;
            }
        }
    }
}
