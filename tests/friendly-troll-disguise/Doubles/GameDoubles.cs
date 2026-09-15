using System;
using UnityEngine;

// ============================================================================
// 游戏原生类型替身（全局命名空间，与 interop Assembly-CSharp 一致）。
// 字段/属性签名按真实 E interop（Build 22992091）逐条核对：
//   FriendlyTroll : MonoBehaviour { int _maskIndex; SpriteRenderer _mask; }
//   Damageable    : MonoBehaviour { bool isDead; GameObject gameObject; Transform transform; }
//   TargetCacher  : MonoBehaviour { List<Damageable> _trollPriorityTargets / _trollLowPriorityTargets;
//                                    delegate bool SearchConditionDelegate(Damageable);
//                                    Damageable GetClosestPriorityTargetWithinRange(float, float, SearchConditionDelegate, SearchConditionDelegate);
//                                    Damageable GetClosestLowPriorityTargetWithinRange(float, float, SearchConditionDelegate); }
// interop 把字段生成为属性（get/set），故替身也用属性 + 写计数，用于断言生产代码从不写原生字段。
// ============================================================================

public sealed class FriendlyTroll : MonoBehaviour
{
    private int _maskIndexValue = -1;
    private SpriteRenderer _maskValue;

    /// <summary>原生字段（真实 interop 为 get/set 属性）：生产代码只读不写。</summary>
    public int _maskIndex
    {
        get { return _maskIndexValue; }
        set
        {
            MaskIndexWrites++;
            _maskIndexValue = value;
        }
    }

    /// <summary>原生 mask renderer 归属（真实 interop 为 get/set 属性）：生产代码只读不写。</summary>
    public SpriteRenderer _mask
    {
        get
        {
            if (ThrowOnMaskRead) throw new InvalidOperationException("interop mask read boom");
            return _maskValue;
        }
        set
        {
            MaskWrites++;
            _maskValue = value;
        }
    }

    public int MaskIndexWrites;
    public int MaskWrites;

    /// <summary>模拟原生字段读取失败（interop 包装层抛错）：资格判定必须回退 false。</summary>
    public bool ThrowOnMaskRead;
}

public sealed class Damageable : MonoBehaviour
{
    public bool isDead;
    public bool invulnerable;
}

public sealed class TargetCacher : MonoBehaviour
{
    public delegate bool SearchConditionDelegate(Damageable obj);

    public Il2CppSystem.Collections.Generic.List<Damageable> _trollPriorityTargets =
        new Il2CppSystem.Collections.Generic.List<Damageable>();

    public Il2CppSystem.Collections.Generic.List<Damageable> _trollLowPriorityTargets =
        new Il2CppSystem.Collections.Generic.List<Damageable>();

    public Damageable GetClosestPriorityTargetWithinRange(float pos, float range,
        SearchConditionDelegate conditionDelegate = null, SearchConditionDelegate ignoreDelegate = null)
    {
        throw new NotSupportedException("原生选择由后缀接管，替身不重放");
    }

    public Damageable GetClosestLowPriorityTargetWithinRange(float pos, float range,
        SearchConditionDelegate conditionDelegate = null)
    {
        throw new NotSupportedException("原生选择由后缀接管，替身不重放");
    }
}
