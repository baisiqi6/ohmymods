using System;
using UnityEngine;

/// <summary>仅本模块用到的原生游戏类型边界（字段/方法名与 2.4 interop 一致）。</summary>
public class Arrow : MonoBehaviour
{
    public SpriteRenderer _spriteRenderer;
    public bool Perfect;

    private bool _isFireArrow;
    public bool ThrowOnIsFireArrowRead;
    public bool ThrowOnPerfectShot;

    public bool isFireArrow
    {
        get
        {
            if (ThrowOnIsFireArrowRead) throw new InvalidOperationException("simulated Arrow.isFireArrow read failure");
            return _isFireArrow;
        }
        set => _isFireArrow = value;
    }

    public void PerfectShot()
    {
        PerfectShotCalls++;
        if (ThrowOnPerfectShot) throw new InvalidOperationException("simulated native PerfectShot failure");
        Perfect = true;
    }

    public int PerfectShotCalls;
}

public class NetworkSoftSimulator : MonoBehaviour
{
    public int SendCount;
    public Vector2 LastVelocity;
    public float LastAngularVelocity;

    public void SendVelocity(Vector2 velocity, float angularVelocity)
    {
        SendCount++;
        LastVelocity = velocity;
        LastAngularVelocity = angularVelocity;
    }
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth = true;
}

public class BiomeHolder
{
    public static BiomeHolder Inst;
    public int BiomeIndex = 3;
}

public class Game
{
    public bool playingOrInMenuWithClient = true;
}

public class World : MonoBehaviour
{
    public Transform gameLayer;
}

public class Managers : MonoBehaviour
{
    public static Managers Inst;
    public World world;
    public Game game;
}

/// <summary>
/// 真实 ByteBuffer 语义的内存模型（2.4 interop：index/maxSafeReadIndex 为 short，PollIndex/
/// PollDataAvailableLength/bufferAccess/ReadByte/ReadBool/Write 签名一致），因此生产代码的
/// peek/消费路径被真实执行；越界读模型为 IL2CPP 数组异常。
/// </summary>
public static class ByteBuffer
{
    public const int BufferLength = 8192;

    public static short index;
    public static short maxSafeReadIndex = -1;
    public static int PrepWriteCalls;
    public static int OverreadWarnings;
    public static bool ThrowOnBufferAccess;
    /// <summary>第 N 次 ReadByte 抛异常（1-based；0 = 关闭）。</summary>
    public static int ThrowOnReadCall;
    /// <summary>true = 该次 ReadByte 先移动游标再抛（模型"移动后抛"）。</summary>
    public static bool ThrowAfterAdvance;
    public static int ReadByteCalls;

    private static readonly byte[] Buffer = new byte[BufferLength];

    public static void PrepWriteBuffer()
    {
        index = 0;
        PrepWriteCalls++;
    }

    public static int PollIndex() => index;

    public static short PollDataAvailableLength() => (short)(maxSafeReadIndex - index);

    public static byte bufferAccess(int i)
    {
        if (ThrowOnBufferAccess) throw new InvalidOperationException("simulated native buffer access failure");
        return Buffer[i];
    }

    public static byte ReadByte()
    {
        ReadByteCalls++;
        if (ThrowOnReadCall != 0 && ReadByteCalls == ThrowOnReadCall)
        {
            if (ThrowAfterAdvance) index++;
            throw new InvalidOperationException("simulated native read failure at call " + ReadByteCalls);
        }
        if (maxSafeReadIndex > 0 && index >= maxSafeReadIndex) OverreadWarnings++;
        return Buffer[index++];
    }

    public static bool ReadBool() => ReadByte() > 0;

    public static void Write(bool value) => Write(value ? (byte)1 : (byte)0);

    public static void Write(byte value) => Buffer[index++] = value;

    // ---------- 测试装载/读取（模型网络层：运动信息 prepend + 收包 PrepReadBuffer） ----------

    public static void ResetForTests()
    {
        index = 0;
        maxSafeReadIndex = -1;
        PrepWriteCalls = 0;
        OverreadWarnings = 0;
        ThrowOnBufferAccess = false;
        ThrowOnReadCall = 0;
        ThrowAfterAdvance = false;
        ReadByteCalls = 0;
        Array.Clear(Buffer, 0, Buffer.Length);
    }

    /// <summary>装载一份可读字节（收包侧语义）并把读游标放在 cursor 处（运动信息已消费）。</summary>
    public static void LoadReadableForTests(byte[] bytes, int cursor)
    {
        Array.Clear(Buffer, 0, Buffer.Length);
        Array.Copy(bytes, Buffer, bytes.Length);
        maxSafeReadIndex = (short)bytes.Length;
        index = (short)cursor;
    }

    public static byte[] WrittenForTests(int from, int count)
    {
        byte[] result = new byte[count];
        Array.Copy(Buffer, from, result, 0, count);
        return result;
    }
}
