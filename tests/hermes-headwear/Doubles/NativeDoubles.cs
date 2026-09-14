using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================
// 游戏原生类型替身（全局命名空间，和 interop Assembly-CSharp 的命名一致）。
// 只实现生产代码真正用到/断言用到的成员；语义按 2.1 反编译源码 + 2.4 反汇编核对。
// ============================================================================

public sealed class FriendlyTroll : MonoBehaviour
{
    // Attribute target names only; NativeFlow models the native boundary explicitly.
    public void Init(int health, bool tough, int mask = -1) => throw new NotSupportedException();
    public void ApplyData(object data) => throw new NotSupportedException();
    public void DeserializeFromData() => throw new NotSupportedException();
    public void GetSerializationData(out Type type) => throw new NotSupportedException();
    public void ResetAndDespawn() => throw new NotSupportedException();
    /// <summary>原生字段：本功能必须永不改动，测试直接断言其值不变。</summary>
    public int _maskIndex = -1;
    public int _trollHealth;
    public bool _toughTroll;

    public int SpawnMaskCalls;

    /// <summary>原生 SpawnMask：本功能只在之后重新 Apply，不改 mask 字段。</summary>
    public void SpawnMask()
    {
        SpawnMaskCalls++;
    }

    public GameObject GetGO => gameObject;
}

public sealed class Persistent : MonoBehaviour
{
    public void OnDisable() => throw new NotSupportedException();
    public bool persistInactive;
    public string path = "Prefabs/FriendlyTroll";
}

public sealed class Pool : MonoBehaviour
{
    /// <summary>仅用于 <c>nameof(Pool.FastSpawn)</c> 编译期存在性；行为由 core seam 驱动。</summary>
    public GameObject FastSpawn(Vector3 position = default, Quaternion rotation = default,
        Transform parent = null, short netID = -1, bool syncReceipt = false)
    {
        throw new NotSupportedException("pool spawn is driven through the core seam in tests");
    }
}

public class HermesStaff
{
    /// <summary>HermesStaff.StartAbilityRoutine 的编译器生成迭代器类（转化上下文标记点）。</summary>
    public sealed class _StartAbilityRoutine_d__17
    {
        public void MoveNext()
        {
        }
    }
}

public sealed class NetworkBigBoss
{
    public static bool HasWorldAuth;
    public static bool IsOnline;
    public static bool IsClientPresent;
    public static bool HasClientCaughtUp;
}

public sealed class Managers
{
    public static Managers Inst;
    public World world;
}

public sealed class World
{
    public IntPtr Pointer;
}

public sealed class CRPCHeader
{
    private static int nextPointer = 0x20000;
    public IntPtr Pointer { get; } = new IntPtr(nextPointer += 16);
    public int netID;
    public GameObject referencedGO;
    public List<NetworkPostbox.DynAction> RemoteMethodList = new List<NetworkPostbox.DynAction>();

    public int CallMethodRemotelyCalls;
    public int LastRemoteFunctionId = -1;
    public byte[] LastPayload;

    public bool RegisterComponents(GameObject obj)
    {
        referencedGO = obj;
        // 原生：FriendlyTroll.BeginRegisteringRPCs 先注册自己的 RevertToTroll 槽位（index 0）。
        if (RemoteMethodList.Count == 0)
        {
            RegisterRPC(new NetworkPostbox.DynAction(delegate { }));
        }
        return true;
    }

    /// <summary>原生语义：先复用第一个 null 槽，否则追加到末尾。</summary>
    public int RegisterRPC(NetworkPostbox.DynAction rpc)
    {
        for (int i = 0; i < RemoteMethodList.Count; i++)
        {
            if (RemoteMethodList[i] == null)
            {
                RemoteMethodList[i] = rpc;
                return i;
            }
        }
        RemoteMethodList.Add(rpc);
        return RemoteMethodList.Count - 1;
    }

    public void CallMethodRemotely(int functionID)
    {
        CallMethodRemotelyCalls++;
        LastRemoteFunctionId = functionID;
        LastPayload = ByteBuffer.Finalise();
    }

    public void CallMethodLocally(byte functionID)
    {
        if (functionID >= RemoteMethodList.Count)
        {
            throw new InvalidOperationException("rpc id out of range: " + functionID);
        }
        RemoteMethodList[functionID]?.Invoke();
    }

    public void Flush()
    {
        RemoteMethodList.Clear();
    }
}

public sealed class NetworkPostbox
{
    public static NetworkPostbox Instance;

    private readonly Dictionary<int, CRPCHeader> _headers = new Dictionary<int, CRPCHeader>();

    public CRPCHeader GetHeaderFromObject(GameObject obj, bool allowCreate = false)
    {
        if (obj == null || obj.Destroyed) return null;
        return _headers.TryGetValue(obj.GetInstanceID(), out CRPCHeader header) ? header : null;
    }

    /// <summary>测试夹具：模拟原生 RegisterObject 建立 header 关联。</summary>
    public CRPCHeader AttachHeaderForTest(GameObject obj)
    {
        CRPCHeader header = new CRPCHeader();
        _headers[obj.GetInstanceID()] = header;
        return header;
    }

    public void ForgetAll()
    {
        _headers.Clear();
    }

    public sealed class DynAction
    {
        private static int _nextPointer = 1;

        internal readonly Action Handler;

        public DynAction(Action handler)
        {
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Pointer = new IntPtr(_nextPointer++);
        }

        public IntPtr Pointer { get; }

        /// <summary>被原生调用（CallMethodLocally 或直调）的次数：用来证明“旧槽位没有被误调”。</summary>
        public int InvokeCalls { get; private set; }

        public void Invoke()
        {
            InvokeCalls++;
            Handler();
        }
    }
}

/// <summary>
/// ByteBuffer 的原生语义替身（8192 字节静态缓冲 + index/maxSafeReadIndex，小端）。
/// 尾巴的写入/读取/边界探测全部走真实语义，才能证明生产代码没有消费别家数据。
/// </summary>
public static class ByteBuffer
{
    private const int BufferLength = 8192;

    private static readonly byte[] Buffer = new byte[BufferLength];
    private static short _index;
    private static short _maxSafeReadIndex = -1;

    public static void ResetForTest()
    {
        Array.Clear(Buffer, 0, Buffer.Length);
        _index = 0;
        _maxSafeReadIndex = -1;
    }

    public static void PrepWriteBuffer()
    {
        _index = 0;
    }

    public static void PrepReadBuffer(byte[] data, short safeReadOverride = -1)
    {
        int length = data?.Length ?? 0;
        _maxSafeReadIndex = safeReadOverride > -1 ? safeReadOverride : (short)length;
        _index = 0;
        if (length > 0) Array.Copy(data, Buffer, length);
    }

    public static void RewindHeader()
    {
        _maxSafeReadIndex = _index;
        _index = 0;
    }

    public static int PollIndex()
    {
        return _index;
    }

    public static short PollDataAvailableLength()
    {
        return (short)(_maxSafeReadIndex - _index);
    }

    public static short PollCurrentWriteLength()
    {
        return _index;
    }

    public static byte bufferAccess(int index)
    {
        return Buffer[index];
    }

    public static void Write(byte value)
    {
        Buffer[_index] = value;
        _index++;
    }

    public static void Write(bool value)
    {
        Write(value ? (byte)1 : (byte)0);
    }

    public static void Write(byte[] bytes)
    {
        int length = bytes?.Length ?? 0;
        for (int i = 0; i < length; i++) Buffer[_index + i] = bytes[i];
        _index += (short)length;
    }

    public static void Write(short value)
    {
        Write((byte)value);
        Write((byte)(value >> 8));
    }

    public static void Write(int value)
    {
        Write((byte)value);
        Write((byte)(value >> 8));
        Write((byte)(value >> 16));
        Write((byte)(value >> 24));
    }

    public static byte ReadByte()
    {
        byte value = Buffer[_index];
        _index++;
        return value;
    }

    public static bool ReadBool()
    {
        return ReadByte() > 0;
    }

    public static short ReadShort()
    {
        int low = ReadByte();
        int high = ReadByte();
        return (short)(low | (high << 8));
    }

    public static int ReadInt()
    {
        int b0 = ReadByte();
        int b1 = ReadByte();
        int b2 = ReadByte();
        int b3 = ReadByte();
        return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
    }

    /// <summary>对齐原生 <c>Finalise()</c>：拷出 [0, index) 的内容。</summary>
    public static byte[] Finalise()
    {
        byte[] result = new byte[_index];
        Array.Copy(Buffer, result, _index);
        return result;
    }
}

public sealed class IslandSaveData
{
    /// <summary>原生存档静态窗口标志：读档/建场景期间为 true。</summary>
    public static bool poppingObjectsToScene;

    /// <summary>原生存档对象（组件 payload 的容器）。</summary>
    public sealed class ObjectData
    {
        public string uniqueID = "native-id";
        public List<ComponentData> componentData2 = new List<ComponentData>();

        public ObjectData()
        {
        }

        public ObjectData(Persistent persistent, bool useNetworkData = false)
        {
            uniqueID = persistent == null ? "native-id" : "native-id-" + persistent.GetInstanceID();
        }

        public sealed class ComponentData
        {
            public string name;
            public string type;
            public string data;

            public ComponentData()
            {
            }

            public ComponentData(string name, string type, string data)
            {
                this.name = name;
                this.type = type;
                this.data = data;
            }

            public ComponentData Clone()
            {
                return new ComponentData(name, type, data);
            }
        }

        public ObjectData Clone()
        {
            ObjectData copy = new ObjectData { uniqueID = uniqueID };
            for (int i = 0; i < componentData2.Count; i++) copy.componentData2.Add(componentData2[i].Clone());
            return copy;
        }
    }

    public static Persistent TryCreateOrFind(ObjectData objectData)
    {
        return null;
    }

    // ---------------------------------------------------------------- 原生存档面（Save 桥）

    /// <summary>原生 <c>_currentlySavingIsland</c>（interop 公开属性）。</summary>
    public static IslandSaveData _currentlySavingIsland;

    /// <summary>原生 <c>CurrentlySavingIsland</c> getter：返回上面那个 static。</summary>
    public static IslandSaveData CurrentlySavingIsland => _currentlySavingIsland;

    /// <summary>本次 island 的 ObjectData 列表（原生私有字段 objects）。</summary>
    public List<ObjectData> objects;

    /// <summary>测试可调：模拟 native uniqueID 变化（instanceID 变化后的新 id）。</summary>
    public static string IdSuffixForTest = "";

    /// <summary>
    /// 原生 <c>GetID(Persistent)</c>：id = 名字 + 实例 ID（这里用替身实例 ID）；
    /// 走一遍 core 的 GetID 后缀 seam，和真实 native 调用点一致。
    /// </summary>
    public static string GetID(Persistent forObject)
    {
        string uniqueId = "FriendlyTroll-" + (forObject == null ? 0 : forObject.GetInstanceID()) + IdSuffixForTest;
        KingdomEnhancedMod.PatchDivine_HermesHeadwear.HandleGetId(forObject, uniqueId);
        return uniqueId;
    }

    /// <summary>原生 <c>Save(int,int,int)</c>：测试不落盘，只保留签名（patch 目标存在性）。</summary>
    public static void Save(int campaign, int land, int challengeId)
    {
        throw new NotSupportedException("native save is simulated by the test harness");
    }
}
