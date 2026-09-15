// 测试替身：原生/IL2CPP 网络边界的“同签名假体”。
// 只实现生产模块用到的表面，行为按实际语义建模：
//  - ByteBuffer：PollDataAvailableLength = 剩余长度；PollIndex + bufferAccess 可 peek；
//    ReadByte 推进游标；Write 追加到写缓冲；PrepWriteBuffer 清空写缓冲。
//  - CRPCHeader：RemoteMethodList 末尾追加槽位；CallMethodRemotely 把当前写缓冲当作
//    网络载荷记录/投递（模拟主机 → 客户端的字节流）。
//  - NetworkPostbox：owner → header 的注册表（只查不建）。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Il2CppInterop.Runtime
{
    /// <summary>真实 DelegateSupport.ConvertDelegate&lt;T&gt; 的等价物（只支撑 DynAction）。</summary>
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(Action action) where T : class
        {
            if (typeof(T) == typeof(NetworkPostbox.DynAction))
            {
                return (T)(object)new NetworkPostbox.DynAction(action);
            }
            throw new NotSupportedException("mock DelegateSupport only supports NetworkPostbox.DynAction");
        }
    }
}

namespace Il2CppSystem
{
    public class Type
    {
    }
}

/// <summary>Knight：本模块只依赖 parentHeaderRef 与两个序列化入口。</summary>
public sealed class Knight : UnityEngine.MonoBehaviour
{
    public CRPCHeader parentHeaderRef;

    public void GetSerializationData(out Il2CppSystem.Type type)
    {
        type = null; // 原生 out 参数：本模块从不修改
    }

    public void DeserializeFromData()
    {
    }
}

public sealed class TestRpcList : List<NetworkPostbox.DynAction>
{
    public bool ThrowAfterAdd;
    public new void Add(NetworkPostbox.DynAction action)
    {
        base.Add(action);
        if (ThrowAfterAdd) { ThrowAfterAdd = false; throw new InvalidOperationException("inserted but acknowledgment failed"); }
    }
}

public sealed class CRPCHeader
{
    public readonly TestRpcList RemoteMethodList = new TestRpcList();
    public GameObject referencedGO;
    public IntPtr Pointer;

    /// <summary>测试注入：模拟原生 RPC 发送抛出。</summary>
    public bool ThrowOnRemoteCall;

    public int RegisterComponentsCalls;

    public bool RegisterComponents(GameObject owner)
    {
        RegisterComponentsCalls++;
        referencedGO = owner;
        return true;
    }

    public void Flush()
    {
        RemoteMethodList.Clear();
    }

    public void CallMethodRemotely(int slotIndex)
    {
        if (ThrowOnRemoteCall) throw new InvalidOperationException("mock native RPC failure");
        MockNetwork.Record(this, slotIndex);
    }
}

public sealed class NetworkPostbox
{
    public sealed class DynAction
    {
        private static int _nextPointer;
        private readonly Action _callback;

        public readonly IntPtr Pointer;

        public DynAction(Action callback)
        {
            _callback = callback;
            Pointer = new IntPtr(++_nextPointer);
        }

        public void Invoke()
        {
            if (_callback != null) _callback();
        }
    }

    public static NetworkPostbox Instance;

    private readonly Dictionary<GameObject, CRPCHeader> _headers = new Dictionary<GameObject, CRPCHeader>();
    private readonly Dictionary<CRPCHeader, GameObject> _owners = new Dictionary<CRPCHeader, GameObject>();

    public int GetHeaderCalls;

    public CRPCHeader GetHeaderFromObject(GameObject owner, bool createIfMissing)
    {
        GetHeaderCalls++;
        if (owner == null) return null;
        return _headers.TryGetValue(owner, out CRPCHeader header) ? header : null;
    }

    public void Bind(GameObject owner, CRPCHeader header)
    {
        _headers[owner] = header;
        _owners[header] = owner;
        header.referencedGO = owner;
    }

    public void DeregisterObject(CRPCHeader header)
    {
        if (header == null) return;
        if (_owners.TryGetValue(header, out GameObject owner)) _headers.Remove(owner);
        _owners.Remove(header);
    }
}

public static class NetworkBigBoss
{
    public static bool HasWorldAuth;
    public static bool IsOnline;
    public static bool IsClientPresent;
    public static bool HasClientCaughtUp;
}

/// <summary>共享 ByteBuffer 的等价语义（读游标 + 写缓冲）。</summary>
public static class ByteBuffer
{
    private static readonly List<byte> Read = new List<byte>();
    private static readonly List<byte> WriteBuffer = new List<byte>();
    private static int _pollIndex;

    public static int PollDataAvailableLength()
    {
        return Read.Count - _pollIndex;
    }

    public static int PollIndex()
    {
        return _pollIndex;
    }

    public static byte bufferAccess(int index)
    {
        if (index < 0 || index >= Read.Count) throw new ArgumentOutOfRangeException(nameof(index), "mock bufferAccess out of range");
        return Read[index];
    }

    public static byte ReadByte()
    {
        if (_pollIndex >= Read.Count) throw new InvalidOperationException("mock ReadByte past end");
        return Read[_pollIndex++];
    }

    public static void Write(byte value)
    {
        WriteBuffer.Add(value);
    }

    public static void PrepWriteBuffer()
    {
        WriteBuffer.Clear();
    }

    /// <summary>测试观察：自上次 PrepWriteBuffer 以来写入的字节。</summary>
    public static byte[] WrittenBytes()
    {
        return WriteBuffer.ToArray();
    }

    /// <summary>测试工具：把一段载荷放到读游标前面（模拟原生把收到的包准备好）。</summary>
    public static void FeedForRead(IList<byte> payload, int startIndex, int count)
    {
        Read.Clear();
        for (int i = 0; i < count; i++) Read.Add(payload[startIndex + i]);
        _pollIndex = 0;
    }

    public static void FeedForRead(byte[] payload)
    {
        FeedForRead(payload, 0, payload.Length);
    }

    /// <summary>测试工具：在现有读缓冲末尾再追加一段（模拟“native 前缀 + 多个 mod 尾巴”）。</summary>
    public static void AppendForRead(byte[] payload)
    {
        Read.AddRange(payload);
    }

    /// <summary>测试工具：逐例隔离。</summary>
    public static void Reset()
    {
        Read.Clear();
        WriteBuffer.Clear();
        _pollIndex = 0;
    }
}

/// <summary>收到的 RPC 投递记录（模拟对端看到的东西）。</summary>
public static class MockNetwork
{
    public sealed class RpcDelivery
    {
        public IntPtr HeaderPointer;
        public int SlotIndex;
        public byte[] Payload;
    }

    public static readonly List<RpcDelivery> Sent = new List<RpcDelivery>();

    public static void Record(CRPCHeader header, int slotIndex)
    {
        Sent.Add(new RpcDelivery
        {
            HeaderPointer = header.Pointer,
            SlotIndex = slotIndex,
            Payload = ByteBuffer.WrittenBytes(),
        });
    }
}

public sealed class World
{
    public UnityEngine.Transform gameLayer;
    public IntPtr Pointer;
}

public sealed class Managers
{
    public static Managers Inst;
    public World world;
}
