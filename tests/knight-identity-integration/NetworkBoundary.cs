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
using KingdomEnhancedMod;

namespace Il2CppInterop.Runtime
{
    /// <summary>真实 DelegateSupport.ConvertDelegate&lt;T&gt; 的等价物（只支撑 DynAction）。</summary>
    internal static class DelegateSupport
    {
        internal static T ConvertDelegate<T>(Action action) where T : class
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
    internal class Type
    {
    }
}

/// <summary>Knight：本模块只依赖 parentHeaderRef 与两个序列化入口。</summary>


internal sealed class TestRpcList : List<NetworkPostbox.DynAction>
{
    internal bool ThrowAfterAdd;
    internal new void Add(NetworkPostbox.DynAction action)
    {
        base.Add(action);
        if (ThrowAfterAdd) { ThrowAfterAdd = false; throw new InvalidOperationException("inserted but acknowledgment failed"); }
    }
}

internal sealed class CRPCHeader
{
    internal readonly TestRpcList RemoteMethodList = new TestRpcList();
    internal GameObject referencedGO;
    internal IntPtr Pointer;

    /// <summary>测试注入：模拟原生 RPC 发送抛出。</summary>
    internal bool ThrowOnRemoteCall;

    internal int RegisterComponentsCalls;

    internal bool RegisterComponents(GameObject owner)
    {
        RegisterComponentsCalls++;
        referencedGO = owner;
        return true;
    }

    internal void Flush()
    {
        RemoteMethodList.Clear();
    }

    internal void CallMethodRemotely(int slotIndex)
    {
        if (ThrowOnRemoteCall) throw new InvalidOperationException("mock native RPC failure");
        MockNetwork.Record(this, slotIndex);
    }
}

internal sealed class NetworkPostbox
{
    internal sealed class DynAction
    {
        private static int _nextPointer;
        private readonly Action _callback;

        internal readonly IntPtr Pointer;

        internal DynAction(Action callback)
        {
            _callback = callback;
            Pointer = new IntPtr(++_nextPointer);
        }

        internal void Invoke()
        {
            if (_callback != null) _callback();
        }
    }

    internal static NetworkPostbox Instance;

    private readonly Dictionary<GameObject, CRPCHeader> _headers = new Dictionary<GameObject, CRPCHeader>();
    private readonly Dictionary<CRPCHeader, GameObject> _owners = new Dictionary<CRPCHeader, GameObject>();

    internal int GetHeaderCalls;

    internal CRPCHeader GetHeaderFromObject(GameObject owner, bool createIfMissing)
    {
        GetHeaderCalls++;
        if (owner == null) return null;
        return _headers.TryGetValue(owner, out CRPCHeader header) ? header : null;
    }

    internal void Bind(GameObject owner, CRPCHeader header)
    {
        _headers[owner] = header;
        _owners[header] = owner;
        header.referencedGO = owner;
    }

    internal void DeregisterObject(CRPCHeader header)
    {
        if (header == null) return;
        if (_owners.TryGetValue(header, out GameObject owner)) _headers.Remove(owner);
        _owners.Remove(header);
    }
}



/// <summary>共享 ByteBuffer 的等价语义（读游标 + 写缓冲）。</summary>
internal static class ByteBuffer
{
    private static readonly List<byte> Read = new List<byte>();
    private static readonly List<byte> WriteBuffer = new List<byte>();
    private static int _pollIndex;

    internal static int PollDataAvailableLength()
    {
        return Read.Count - _pollIndex;
    }

    internal static int PollIndex()
    {
        return _pollIndex;
    }

    internal static byte bufferAccess(int index)
    {
        if (index < 0 || index >= Read.Count) throw new ArgumentOutOfRangeException(nameof(index), "mock bufferAccess out of range");
        return Read[index];
    }

    internal static byte ReadByte()
    {
        if (_pollIndex >= Read.Count) throw new InvalidOperationException("mock ReadByte past end");
        return Read[_pollIndex++];
    }

    internal static void Write(byte value)
    {
        WriteBuffer.Add(value);
    }

    internal static void PrepWriteBuffer()
    {
        WriteBuffer.Clear();
    }

    /// <summary>测试观察：自上次 PrepWriteBuffer 以来写入的字节。</summary>
    internal static byte[] WrittenBytes()
    {
        return WriteBuffer.ToArray();
    }

    /// <summary>测试工具：把一段载荷放到读游标前面（模拟原生把收到的包准备好）。</summary>
    internal static void FeedForRead(IList<byte> payload, int startIndex, int count)
    {
        Read.Clear();
        for (int i = 0; i < count; i++) Read.Add(payload[startIndex + i]);
        _pollIndex = 0;
    }

    internal static void FeedForRead(byte[] payload)
    {
        FeedForRead(payload, 0, payload.Length);
    }

    /// <summary>测试工具：在现有读缓冲末尾再追加一段（模拟“native 前缀 + 多个 mod 尾巴”）。</summary>
    internal static void AppendForRead(byte[] payload)
    {
        Read.AddRange(payload);
    }

    /// <summary>测试工具：逐例隔离。</summary>
    internal static void Reset()
    {
        Read.Clear();
        WriteBuffer.Clear();
        _pollIndex = 0;
    }
}

/// <summary>收到的 RPC 投递记录（模拟对端看到的东西）。</summary>
internal static class MockNetwork
{
    internal sealed class RpcDelivery
    {
        internal IntPtr HeaderPointer;
        internal int SlotIndex;
        internal byte[] Payload;
    }

    internal static readonly List<RpcDelivery> Sent = new List<RpcDelivery>();

    internal static void Record(CRPCHeader header, int slotIndex)
    {
        Sent.Add(new RpcDelivery
        {
            HeaderPointer = header.Pointer,
            SlotIndex = slotIndex,
            Payload = ByteBuffer.WrittenBytes(),
        });
    }
}




