using System;
using System.Collections.Generic;
using KingdomArcherOptions.Combat.Tests;
using UnityEngine;

namespace Coatsink.Common
{
    /// <summary>Interop-style native interface wrapper (Cast&lt;T&gt; comes from Il2CppObjectBase).</summary>
    public class IHaglet : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
    {
    }

    /// <summary>Concrete haglet (coroutine host) the native Shoot routine runs in.</summary>
    public class Haglet : IHaglet
    {
        public bool started;
    }
}

/// <summary>Native-boundary stubs for the game types that PatchArcher_Options.cs touches.</summary>
public class Archer : MonoBehaviour
{
    public enum AttackMode
    {
        Ranged = 0,
        Melee = 1,
        Shield = 2
    }

    /// <summary>Modeled native <c>Archer._Shoot_d__225</c> iterator: only the back-reference matters here.</summary>
    public sealed class _Shoot_d__225
    {
        public Archer __4__this;

        public bool MoveNext() => true;
    }

    private float _shootPrepTime = 0.4f;

    /// <summary>Test injection: models a native write that throws (IL2CPP trampoline failure).</summary>
    public bool ThrowOnPrepWrite;

    public float shootPrepTime
    {
        get => _shootPrepTime;
        set
        {
            if (ThrowOnPrepWrite) throw new InvalidOperationException("simulated native shootPrepTime write failure");
            _shootPrepTime = value;
        }
    }

    public Vector2 _shootIntervalRange = new Vector2(0.35f, 0.65f);
    public Vector2 _shootIntervalRangeFormation = new Vector2(0.5f, 0.9f);
    public float _cooldown;
    public IUnitController _unitController;
    public Character _character;
    public Damageable _damageable;
    public Coatsink.Common.IHaglet shoot;
    // 英雄射程模块要读写的原生基线/扫描器字段（2.4 interop 可读可写，已在有界 interop 编译中验证）。
    public float shootRange = 8f;
    public ArrowAttack _arrowAttack, _fireArrowAttack;
    public ArrowAttack ActiveArrowAttack;
    public Scanner _enemyScanner;
    public AttackMode _attackMode = AttackMode.Ranged;
    public AttackMode _desiredAttackMode = AttackMode.Ranged;

    /// <summary>Set by the modeled native Update body so tests can assert the early return ran.</summary>
    public bool NativeBodyRan;
    public Knight _knight;
    public GameObject _shootingTarget;
    public bool harmless, IsCrossbowForTests, IsNorseForTests;

    public bool ShouldPlayerControl() => _unitController != null;

    public _Shoot_d__225 NewShootIterator() => new _Shoot_d__225 { __4__this = this };
}

public interface IUnitController
{
}

public class Character : MonoBehaviour
{
    public bool inert;
    public bool grabbed;
}

public class Damageable : MonoBehaviour
{
    public bool isDead;
    public bool DamagedByArrows = true;
    public bool IsDamagedBy(DamageSource source) => DamagedByArrows;
}

public class Arrow : MonoBehaviour
{
    public GameObject archer;
    public SpriteRenderer _spriteRenderer;
    public void ReceiveInitialise() { }
    public bool isFireArrow;
    public bool Perfect;
    public bool ThrowOnPerfectShot;

    /// <summary>Test injection: models a transient interop read failure on the hit flag.</summary>
    public bool ThrowOnHasHitRead;

    private bool _hasHitValue;

    public bool _hasHit
    {
        get
        {
            if (ThrowOnHasHitRead) throw new InvalidOperationException("simulated transient Arrow._hasHit read failure");
            return _hasHitValue;
        }
        set => _hasHitValue = value;
    }

    public void PerfectShot()
    {
        if (ThrowOnPerfectShot) throw new InvalidOperationException("cached perfect shot initialization failed");
        Perfect = true;
    }
}

public class ArrowAttack : ScriptableObject
{
    public Arrow _arrowPrefab;
    // 英雄射程克隆只允许改动这两个 magnitude，其余字段必须逐字段保留（测试逐项核对）。
    public float _shotMagnitude = 12f;
    public float _boostedShotMagnitude = 12f;
    public float _arrowGravity = -30f;
    public Vector2 _arrowOriginOffset = new Vector2(0.15f, 0.5f);

    /// <summary>原生 ArrowAttack.Range 公式（Util.GetProjectileRange）。</summary>
    public float Range => _arrowGravity >= 0f ? 0f : _shotMagnitude * _shotMagnitude / -_arrowGravity;
}

/// <summary>原生 Scanner：range/rangeBehind 是公开字段（原生会按 shootRange 覆写）；
/// 替身做成属性只是为了能模拟"写入抛异常"这一失败点。</summary>
public class Scanner
{
    private static int _nextPointer = 0x7000;

    /// <summary>interop 身份：换实例即换指针（模块据此放弃认领）。</summary>
    public IntPtr Pointer = new IntPtr(_nextPointer += 16);

    private float _range;
    private float _rangeBehind;

    public bool ThrowOnRangeWrite;

    public float range
    {
        get => _range;
        set
        {
            if (ThrowOnRangeWrite) throw new InvalidOperationException("simulated enemy scanner write failure");
            _range = value;
        }
    }

    public float rangeBehind
    {
        get => _rangeBehind;
        set
        {
            if (ThrowOnRangeWrite) throw new InvalidOperationException("simulated enemy scanner write failure");
            _rangeBehind = value;
        }
    }
}

public class NetworkSoftSimulator : MonoBehaviour
{
    public Vector2 LastVelocity;
    public float LastAngularVelocity;
    public int SendCount;
    public byte[] LastPayload;

    public void SendVelocity(Vector2 vel, float angularVelocity)
    {
        LastVelocity = vel;
        LastAngularVelocity = angularVelocity;
        SendCount++;
        LastPayload = ByteBuffer.Written.ToArray();
        FakeOps.Add("send");
    }
}

public class BiomeData : ScriptableObject
{
    public static BiomeData Current;

    private readonly Dictionary<GameObject, GameObject> _swapForTests = new Dictionary<GameObject, GameObject>();

    public void RegisterSwapForTests(GameObject from, GameObject to) => _swapForTests[from] = to;

    public void ClearSwapsForTests() => _swapForTests.Clear();

    /// <summary>Identity in production; tests register swaps to model a biome asset swap.</summary>
    public T GetAssetSwapForThis<T>(T asset) where T : UnityEngine.Object
    {
        if (asset is GameObject go && _swapForTests.TryGetValue(go, out GameObject mapped)) return (T)(object)mapped;
        return asset;
    }
}

public class World : MonoBehaviour
{
    public Transform gameLayer;
}

public class Managers : MonoBehaviour
{
    public static Managers Inst => SingletonMonoBehaviour<Managers>.Inst;
    public World world;
}

public class SingletonMonoBehaviour<T> where T : class
{
    public static T Inst;
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
    public static bool IsOnline;
    public static bool HasClientCaughtUp = true;
    public static bool IsClientPresent;
}

/// <summary>Ordered log of native boundary calls, used to assert the native call sequences.</summary>
public static class ByteBuffer
{
    public static readonly List<byte> Written = new List<byte>();
    public static byte[] Incoming = Array.Empty<byte>();
    public static int Position;
    public static short index { get => (short)Position; set => Position = value; }
    public static void PrepWriteBuffer() { Written.Clear(); FakeOps.Add("prep"); }
    public static void Write(bool value) { Written.Add(value ? (byte)1 : (byte)0); FakeOps.Add("write:" + value); }
    public static void Write(byte value) { Written.Add(value); FakeOps.Add("write-byte:" + value); }
    public static int PollDataAvailableLength() => Incoming.Length - Position;
    public static int PollIndex() => Position;
    public static byte bufferAccess(int index) => Incoming[index];
    public static byte ReadByte() => Incoming[Position++];
    public static bool ReadBool() => ReadByte() != 0;
}
