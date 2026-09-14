using System;
using System.Collections.Generic;
using System.Text.Json;
using KingdomEnhancedMod;
using UnityEngine;

namespace HermesHeadwearTests
{
    internal static class Check
    {
        internal static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        internal static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception(message + " [expected=" + expected + " actual=" + actual + "]");
            }
        }

        internal static void NotNull(object value, string message)
        {
            if (value == null) throw new Exception(message);
        }

        internal static void Null(object value, string message)
        {
            if (value != null) throw new Exception(message);
        }

        internal static void Contains(string haystack, string needle, string message)
        {
            if (haystack == null || haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
            {
                throw new Exception(message + " [haystack=" + haystack + "]");
            }
        }
    }

    /// <summary>一条用例：共享静态（ByteBuffer / core / 视觉替身 / 配置）逐个复位后串行执行。</summary>
    internal static class Case
    {
        internal static int Passed;
        internal static int Failed;
        internal static readonly List<string> Failures = new List<string>();

        internal static void Run(string name, Action body)
        {
            Fixture.ResetWorld();
            try
            {
                body();
                Passed++;
                Console.WriteLine("  PASS  " + name);
            }
            catch (Exception e)
            {
                Failed++;
                Failures.Add(name + " -> " + e.Message);
                Console.WriteLine("  FAIL  " + name);
                Console.WriteLine("        " + e.Message);
            }
        }
    }

    internal sealed class TrollFixture
    {
        internal GameObject Owner;
        internal FriendlyTroll Troll;
        internal Persistent Persistent;
        internal CRPCHeader Header;
        internal int SlotIndex = -1;

        internal static TrollFixture Create(bool withPersistent = true, bool withFriendly = true)
        {
            TrollFixture fixture = new TrollFixture();
            fixture.Owner = new GameObject("FriendlyTroll(Clone)");
            if (withPersistent) fixture.Persistent = fixture.Owner.AddComponent(new Persistent());
            if (withFriendly) fixture.Troll = fixture.Owner.AddComponent(new FriendlyTroll());
            return fixture;
        }

        /// <summary>模拟原生 RegisterObject → CRPCHeader.RegisterComponents → mod 后缀追加自有槽位。</summary>
        internal void RegisterHeader()
        {
            Header = NetworkPostbox.Instance.AttachHeaderForTest(Owner);
            Header.RegisterComponents(Owner);
            PatchDivine_HermesHeadwear.HandleHeaderRegistered(Header, Owner, true);
            SlotIndex = Header.RemoteMethodList.Count - 1;
        }
    }

    /// <summary>把原生调用序列摊平成可调用的测试夹具（原生字段读写 + core 后缀）。</summary>
    internal static class NativeFlow
    {
        /// <summary>原生 Init：写 health/tough/mask + SpawnMask，然后 core 后缀。</summary>
        internal static void InitTroll(FriendlyTroll troll, int health = 4, bool tough = false, int maskIndex = 2)
        {
            troll._trollHealth = health;
            troll._toughTroll = tough;
            troll._maskIndex = maskIndex;
            troll.SpawnMask();
            PatchDivine_HermesHeadwear.HandleNativeInit(troll);
        }

        /// <summary>HermesStaff.StartAbilityRoutine MoveNext 期间的转化（转化上下文深度标记）。</summary>
        internal static void ConvertByHermes(FriendlyTroll troll, int health = 4, bool tough = false, int maskIndex = 2)
        {
            Check.True(PatchDivine_HermesHeadwear.IsConversionContextActive() == false,
                "conversion context must start closed");
            PatchDivine_HermesHeadwear.EnterConversionContext();
            try
            {
                InitTroll(troll, health, tough, maskIndex);
            }
            finally
            {
                PatchDivine_HermesHeadwear.ExitConversionContext();
            }
            Check.True(PatchDivine_HermesHeadwear.IsConversionContextActive() == false,
                "conversion context must be released by the finalizer path");
        }

        /// <summary>主机侧 GetSerializationData：原生 3 字段 + 30 字节尾巴，返回完整 payload。</summary>
        internal static byte[] SerializeHostPayload(FriendlyTroll troll)
        {
            ByteBuffer.PrepWriteBuffer();
            ByteBuffer.Write(troll._trollHealth);
            ByteBuffer.Write(troll._toughTroll);
            ByteBuffer.Write(troll._maskIndex);
            PatchDivine_HermesHeadwear.HandleNativeSerialization(troll);
            return ByteBuffer.Finalise();
        }

        /// <summary>从“原生 3 字段（9 字节）+ 尾巴”的完整 payload 里取尾巴（序列化/读档路径）。</summary>
        internal static bool TryReadSerializedTail(byte[] payload, out HermesHeadwearCodec.Tail tail)
        {
            tail = default;
            if (payload == null || payload.Length < 9 + HermesHeadwearCodec.TailBytes) return false;
            return HermesHeadwearCodec.TryReadTail(
                new ReadOnlySpan<byte>(payload, 9, HermesHeadwearCodec.TailBytes), out tail);
        }

        /// <summary>客户端侧 DeserializeFromData：先原生读 3 字段 + SpawnMask，再 core 后缀读尾巴。</summary>
        internal static void DeserializeClientPayload(FriendlyTroll troll, byte[] payload)
        {
            ByteBuffer.PrepWriteBuffer();
            ByteBuffer.Write(payload);
            ByteBuffer.RewindHeader();
            troll._trollHealth = ByteBuffer.ReadInt();
            troll._toughTroll = ByteBuffer.ReadBool();
            troll._maskIndex = ByteBuffer.ReadInt();
            troll.SpawnMask();
            PatchDivine_HermesHeadwear.HandleNativeDeserialize(troll);
        }

        /// <summary>客户端收到一次 live RPC：原生把载荷放进 ByteBuffer 后 CallMethodLocally。</summary>
        internal static void DeliverRpc(CRPCHeader header, int slotIndex, byte[] payload)
        {
            ByteBuffer.PrepWriteBuffer();
            ByteBuffer.Write(payload);
            ByteBuffer.RewindHeader();
            header.CallMethodLocally((byte)slotIndex);
        }

        /// <summary>原生 RetrieveData → FriendlyTrollData 组件 JSON（只含原生 3 字段）。</summary>
        internal static IslandSaveData.ObjectData.ComponentData BuildNativeComponentData(Persistent persistent)
        {
            FriendlyTroll troll = persistent.GetComponent<FriendlyTroll>();
            if (troll == null) return null;
            string nativeJson = "{\"maskIndex\":" + troll._maskIndex
                + ",\"trollHealth\":" + troll._trollHealth
                + ",\"toughTroll\":" + (troll._toughTroll ? "true" : "false") + "}";
            return new IslandSaveData.ObjectData.ComponentData("FriendlyTroll", "FriendlyTrollData", nativeJson);
        }

        /// <summary>
        /// 模拟 native <c>ObjectData(Persistent, bool)</c> 在 Save 内的一次组装：native ctor 内调用 GetID
        /// （走 core 的 GetID 后缀 seam）→ 原生组件 JSON → 挂进 island.objects。
        /// </summary>
        internal static IslandSaveData.ObjectData RegisterNativeRecord(IslandSaveData island, Persistent persistent)
        {
            IslandSaveData.ObjectData record = new IslandSaveData.ObjectData(persistent, false);
            record.uniqueID = IslandSaveData.GetID(persistent);
            IslandSaveData.ObjectData.ComponentData component = BuildNativeComponentData(persistent);
            if (component != null) record.componentData2.Add(component);
            if (island.objects == null) island.objects = new List<IslandSaveData.ObjectData>();
            island.objects.Add(record);
            return record;
        }

        /// <summary>
        /// 模拟一次原生 <c>IslandSaveData.Save(campaign, land, challengeId)</c>（**不落盘**）：
        /// prefix 建立 scope → 逐个 persistent 组装 ObjectData（native ctor 内 GetID 会走 core 后缀）
        /// → 原生 finally 清 CurrentlySavingIsland → Save postfix 注入 → Harmony finalizer 恢复 scope。
        /// </summary>
        internal static IslandSaveData RunNativeSave(IReadOnlyList<Persistent> persistents)
        {
            IslandSaveData island = new IslandSaveData();
            var state = PatchDivine_HermesHeadwear.BeginSaveCapture();
            IslandSaveData._currentlySavingIsland = island; // 原生 Save 内设置（native finally 会清）
            try
            {
                island.objects = new List<IslandSaveData.ObjectData>();
                for (int i = 0; i < persistents.Count; i++)
                {
                    RegisterNativeRecord(island, persistents[i]);
                }
            }
            finally
            {
                IslandSaveData._currentlySavingIsland = null; // 原生 Save 的 finally
            }

            PatchDivine_HermesHeadwear.ApplySaveCapture(state);      // Save postfix
            PatchDivine_HermesHeadwear.EndSaveCapture(null, state);  // Harmony finalizer
            return island;
        }

        /// <summary>
        /// 模拟异常逃出原生 Save：不跑 postfix（不注入），finalizer 必须恢复 scope 且原样返回异常。
        /// 原生方法自行捕获的异常仍正常返回并运行 postfix，不属于本测试路径。
        /// 返回捕获的 island（其记录里不应出现我们的扩展）。
        /// </summary>
        internal static IslandSaveData RunNativeSaveThatThrows(IReadOnlyList<Persistent> persistents, out Exception thrown)
        {
            IslandSaveData island = new IslandSaveData();
            var state = PatchDivine_HermesHeadwear.BeginSaveCapture();
            IslandSaveData._currentlySavingIsland = island;
            thrown = null;
            try
            {
                island.objects = new List<IslandSaveData.ObjectData>();
                if (persistents.Count > 0)
                {
                    Persistent persistent = persistents[0];
                    IslandSaveData.ObjectData record = new IslandSaveData.ObjectData(persistent, false);
                    record.uniqueID = IslandSaveData.GetID(persistent);
                    IslandSaveData.ObjectData.ComponentData component = BuildNativeComponentData(persistent);
                    if (component != null) record.componentData2.Add(component);
                    island.objects.Add(record);
                    throw new InvalidOperationException("native save failure");
                }
            }
            catch (Exception e)
            {
                thrown = e;
            }
            finally
            {
                IslandSaveData._currentlySavingIsland = null;
            }

            Exception returned = PatchDivine_HermesHeadwear.EndSaveCapture(thrown, state);
            Check.True(ReferenceEquals(thrown, returned),
                "the finalizer must return the native exception instance unchanged");
            return island;
        }

        /// <summary>模拟原生 ObjectData(Persistent, bool) 构造：填 FriendlyTrollData 组件后跑 core 后缀。</summary>
        internal static IslandSaveData.ObjectData BuildSaveObject(Persistent persistent, bool useNetworkData = false)
        {
            IslandSaveData.ObjectData objectData = new IslandSaveData.ObjectData(persistent, useNetworkData);
            FriendlyTroll troll = persistent.GetComponent<FriendlyTroll>();
            if (troll != null)
            {
                if (useNetworkData)
                {
                    // 网络数据流：name=IRPCable 类型全名，type=IRPCData（不是 FriendlyTrollData）。
                    objectData.componentData2.Add(new IslandSaveData.ObjectData.ComponentData(
                        "FriendlyTroll", "IRPCData",
                        "{\"typeName\":\"FriendlyTroll\",\"data\":[4,0,0,0,0,3,0,0,0]}"));
                }
                else
                {
                    objectData.componentData2.Add(BuildNativeComponentData(persistent));
                }
            }

            PatchDivine_HermesHeadwear.HandleObjectDataBuilt(objectData, persistent, useNetworkData);
            return objectData;
        }

        /// <summary>
        /// 客户端/主机读档：池生成 → TryCreateOrFind → 原生 ApplyData（只解析原生 3 个字段，
        /// 其余属性由 JsonUtility 语义忽略）→ core 后缀。
        /// </summary>
        internal static TrollFixture LoadFromSave(IslandSaveData.ObjectData objectData)
        {
            TrollFixture fixture = TrollFixture.Create();
            PatchDivine_HermesHeadwear.HandlePoolSpawn(fixture.Owner);
            PatchDivine_HermesHeadwear.HandleTryCreateOrFind(objectData, fixture.Persistent);

            IslandSaveData.ObjectData.ComponentData component = FindFriendlyComponent(objectData);
            if (component != null && component.type == "FriendlyTrollData")
            {
                ApplyNativeDataJson(fixture.Troll, component.data);
            }
            return fixture;
        }

        internal static IslandSaveData.ObjectData.ComponentData FindFriendlyComponent(IslandSaveData.ObjectData objectData)
        {
            for (int i = 0; i < objectData.componentData2.Count; i++)
            {
                IslandSaveData.ObjectData.ComponentData component = objectData.componentData2[i];
                if (component != null && component.name == "FriendlyTroll" && component.type == "FriendlyTrollData")
                {
                    return component;
                }
            }
            return null;
        }

        /// <summary>按 JsonUtility 语义读回原生字段（未知属性被忽略），再跑 SpawnMask + core 后缀。</summary>
        internal static void ApplyNativeDataJson(FriendlyTroll troll, string componentJson)
        {
            using (JsonDocument document = JsonDocument.Parse(componentJson))
            {
                JsonElement root = document.RootElement;
                troll._trollHealth = root.GetProperty("trollHealth").GetInt32();
                troll._toughTroll = root.GetProperty("toughTroll").GetBoolean();
                troll._maskIndex = root.GetProperty("maskIndex").GetInt32();
            }
            troll.SpawnMask();
            PatchDivine_HermesHeadwear.HandleNativeApplyData(troll);
        }
    }

    internal static class Fixture
    {
        private static int _worldCounter = 1000;

        /// <summary>单调时钟：只增不减，跨用例也不回拨（core 里的 _nextSweepAt 等计时因此不会失步）。</summary>
        private static float _clock = 500f;

        /// <summary>
        /// 模拟“新进程 / 新场景”：core 的世代、队列、header 槽所有权、扫描调度全部重来。
        /// 需要跨对端/跨读档断言时，先取到 bytes/metadata，再调用本方法切到新 peer 场景。
        /// </summary>
        internal static void ResetWorld(bool host = true, bool online = true)
        {
            PatchDivine_HermesHeadwear.ResetForProcessBoundary();
            ByteBuffer.ResetForTest();
            HermesHeadwearVisuals.ResetForTest();
            ModConfig.ResetForTest();
            GreekBankScope.ResetForTest();
            UnityEngine.Random.SetSequence();
            // Controlled test sampler only; production uses an independent cryptographic RNG.
            PatchDivine_HermesHeadwear.SampleOverride = UnityEngine.Random.Range;
            _clock += 10000f;
            UnityEngine.Time.time = _clock;
            NetworkBigBoss.HasWorldAuth = host;
            NetworkBigBoss.IsOnline = online;
            NetworkBigBoss.IsClientPresent = true;
            NetworkBigBoss.HasClientCaughtUp = true;
            IslandSaveData.poppingObjectsToScene = false;
            IslandSaveData.IdSuffixForTest = "";
            IslandSaveData._currentlySavingIsland = null;
            NetworkPostbox.Instance = new NetworkPostbox();
            Managers.Inst = new Managers { world = new World { Pointer = new IntPtr(_worldCounter++) } };

            // 基线收敛：让 core 记录当前世界指针与配置基线，再把视觉调用记录清零，
            // 于是用例只观察自己触发的调用。
            PatchDivine_HermesHeadwear.Tick();
            HermesHeadwearVisuals.ResetForTest();
        }

        internal static void AdvanceTime(float seconds)
        {
            _clock += seconds;
            UnityEngine.Time.time = _clock;
        }

        internal static void Tick()
        {
            PatchDivine_HermesHeadwear.Tick();
        }

        /// <summary>推进时间并跑 Tick，确保跨过 1s 扫描/重试窗口。</summary>
        internal static void TickAfter(float seconds)
        {
            AdvanceTime(seconds);
            Tick();
        }

        /// <summary>
        /// 推进到“真实发生过一次扫描”为止（扫描间隔 1s，这里给 2.5s 余量），
        /// 再补一次 Tick 让扫描排入的重放生效。断言扫描行为请用本方法，不要用 1.2s。
        /// </summary>
        internal static void TickThroughSweep()
        {
            AdvanceTime(2.5f);
            Tick();
            Tick();
        }
    }
}
