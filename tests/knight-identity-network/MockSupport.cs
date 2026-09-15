// 逐例隔离 + world/knight/header 构造工具 + 握手包装载构造/投递。

using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace KnightIdentityNetworkTests
{
    internal static class MockScene
    {
        internal const int SceneHandle = 4242;
        internal const int OtherSceneHandle = 999;

        internal static Transform Layer;

        private static int _nextGameObjectId = 1000;
        private static int _nextHeaderPointer = 5000;

        internal static void Setup()
        {
            var layerGo = new GameObject("gameLayer");
            layerGo.scene.handle = SceneHandle;
            layerGo.AssignInstanceId(NextGameObjectId());
            Layer = layerGo.transform;
            Managers.Inst = new Managers { world = new World { gameLayer = Layer, Pointer = new IntPtr(777) } };
        }

        internal static int NextGameObjectId() { return ++_nextGameObjectId; }

        internal static int NextHeaderPointer() { return ++_nextHeaderPointer; }

        internal static Knight SpawnKnight(string tag, bool inLayer, bool withHeader, int instanceId)
        {
            var go = new GameObject("knight-" + tag);
            go.AssignInstanceId(instanceId != 0 ? instanceId : NextGameObjectId());
            go.scene.handle = SceneHandle;
            if (inLayer && Layer != null) go.transform.parent = Layer;
            Knight knight = go.AddComponent<Knight>();
            knight.tag = tag;
            if (withHeader) AttachHeader(knight);
            return knight;
        }

        internal static Knight SpawnKnight()
        {
            return SpawnKnight("Knight", true, true, 0);
        }

        internal static CRPCHeader AttachHeader(Knight knight)
        {
            var header = new CRPCHeader { Pointer = new IntPtr(NextHeaderPointer()) };
            NetworkPostbox.Instance.Bind(knight.gameObject, header);
            knight.parentHeaderRef = header;
            return header;
        }

        /// <summary>模拟原生 RegisterComponents + 我们的 Harmony 后缀。</summary>
        internal static CRPCHeader Register(Knight knight)
        {
            CRPCHeader header = knight.parentHeaderRef;
            if (header == null) return null;
            bool registered = header.RegisterComponents(knight.gameObject);
            KnightIdentityNetwork.HandleHeaderRegistered(header, knight.gameObject, registered);
            return header;
        }
    }

    internal static class MockNet
    {
        /// <summary>主机上下文：auth + 在线 + 对端在场 + 已追平。</summary>
        internal static void HostReady()
        {
            NetworkBigBoss.HasWorldAuth = true;
            NetworkBigBoss.IsOnline = true;
            NetworkBigBoss.IsClientPresent = true;
            NetworkBigBoss.HasClientCaughtUp = true;
        }

        /// <summary>客户端上下文：无 auth + 在线 + 已追平（对端在场与否与客户端无关）。</summary>
        internal static void Client()
        {
            NetworkBigBoss.HasWorldAuth = false;
            NetworkBigBoss.IsOnline = true;
            NetworkBigBoss.IsClientPresent = false;
            NetworkBigBoss.HasClientCaughtUp = true;
        }

        internal static void NotCaughtUp()
        {
            NetworkBigBoss.HasClientCaughtUp = false;
        }

        // ------------------------------------------------------------------ 包构造

        internal static byte[] Request(long nonce)
        {
            var payload = new byte[KnightIdentityNetwork.RequestBytes];
            if (!KnightIdentityNetwork.WriteRequest(payload, nonce)) throw new Exception("mock request write failed");
            return payload;
        }

        internal static byte[] Response(long nonce, Guid id, int style, long hostLifetime)
        {
            var payload = new byte[KnightIdentityNetwork.ResponseBytes];
            if (!KnightIdentityNetwork.WriteResponse(payload, nonce, id, style, hostLifetime)) throw new Exception("mock response write failed");
            return payload;
        }

        internal static long NonceOf(byte[] request)
        {
            if (!KnightIdentityNetwork.TryReadRequest(request, out long nonce)) throw new Exception("mock request decode failed");
            return nonce;
        }

        internal static long NonceOfDelivery(int index)
        {
            byte[] payload = MockNetwork.Sent[index].Payload;
            long nonce;
            if (KnightIdentityNetwork.TryReadRequest(payload, out nonce)) return nonce;
            if (KnightIdentityNetwork.TryReadResponse(payload, out nonce, out _, out _, out _)) return nonce;
            throw new Exception("delivery " + index + " is neither request nor response");
        }

        internal static bool IsRequest(byte[] payload)
        {
            return KnightIdentityNetwork.TryReadRequest(payload, out _);
        }

        /// <summary>把包放进读游标并触发原生槽调用（模拟对端 RPC 到达）。</summary>
        internal static void Deliver(CRPCHeader header, byte[] payload)
        {
            ByteBuffer.FeedForRead(payload);
            NetworkPostbox.DynAction slot = header.RemoteMethodList[header.RemoteMethodList.Count - 1];
            slot.Invoke();
        }

        internal static byte[] Tail(Guid id, int style, long lifetime)
        {
            var payload = new byte[KnightIdentityNetwork.TailBytes];
            KnightIdentityNetwork.WriteTail(payload, id, style, lifetime);
            return payload;
        }

        internal static byte[] NativePrefix(int length, byte seed)
        {
            var payload = new byte[length];
            for (int i = 0; i < length; i++) payload[i] = (byte)(seed + i);
            return payload;
        }

        /// <summary>别的 mod 的 30 字节尾巴（Hermes 'KHM1'）：必须不被本模块消费。</summary>
        internal static byte[] ForeignTail()
        {
            var payload = new byte[30];
            payload[0] = 0x4B; // 'K'
            payload[1] = 0x48; // 'H'
            payload[2] = 0x4D; // 'M'
            payload[3] = 0x31; // '1'
            for (int i = 4; i < payload.Length; i++) payload[i] = (byte)(0x80 + i);
            return payload;
        }

        internal static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            for (int i = 0; i < parts.Length; i++) total += parts[i].Length;
            var payload = new byte[total];
            int offset = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                Array.Copy(parts[i], 0, payload, offset, parts[i].Length);
                offset += parts[i].Length;
            }
            return payload;
        }

        internal static Guid Id(byte seed)
        {
            var bytes = new byte[16];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(seed + i);
            bytes[15] = (byte)(seed | 0x40); // 非空且稳定
            return new Guid(bytes);
        }

        internal static List<MockNetwork.RpcDelivery> Deliveries { get { return MockNetwork.Sent; } }
    }

    internal static class MockReset
    {
        /// <summary>逐例隔离：模块静态状态 + 全部替身。</summary>
        internal static void All()
        {
            KnightIdentityNetwork.ResetForProcessBoundary();
            KnightIdentityRuntime.Reset();
            ByteBuffer.Reset();
            MockNetwork.Sent.Clear();
            NetworkBigBoss.HasWorldAuth = false;
            NetworkBigBoss.IsOnline = false;
            NetworkBigBoss.IsClientPresent = false;
            NetworkBigBoss.HasClientCaughtUp = false;
            NetworkPostbox.Instance = new NetworkPostbox();
            Managers.Inst = null;
            MockScene.Setup();
            MockLog.Clear();
        }
    }
}
