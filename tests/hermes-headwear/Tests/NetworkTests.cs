using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace HermesHeadwearTests
{
    /// <summary>线路：尾巴序列化、live RPC 槽位所有权、后期加入、过期/跨世代/坏包防御。</summary>
    internal static class NetworkTests
    {
        internal static void Run()
        {
            Console.WriteLine("Networking (tail + owned RPC slot)");

            Case.Run("network.tailAndLiveRpcAgreeAcrossPeers", () =>
            {
                UnityEngine.Random.SetSequence(0, 12);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                Check.Equal(1, host.SlotIndex, "own slot appended after the native slot");

                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(1, host.Header.CallMethodRemotelyCalls, "one live RPC after Init");
                Check.Equal(host.SlotIndex, host.Header.LastRemoteFunctionId, "own slot id used");
                Check.True(HermesHeadwearCodec.TryReadTail(host.Header.LastPayload, out HermesHeadwearCodec.Tail live),
                    "live payload is the versioned tail");
                Check.Equal(12, live.Choice, "live payload carries the choice");

                byte[] livePayload = (byte[])host.Header.LastPayload.Clone();
                byte[] serializedPayload = NativeFlow.SerializeHostPayload(host.Troll);
                Check.Equal(HermesHeadwearCodec.TailBytes, serializedPayload.Length - 9,
                    "tail appended right after the three native fields");
                Check.True(NativeFlow.TryReadSerializedTail(serializedPayload, out HermesHeadwearCodec.Tail serialized),
                    "serialized payload carries the tail");
                Check.Equal(live.Token, serialized.Token, "same generation token on both paths");
                Check.Equal(live.Choice, serialized.Choice, "same choice on both paths");
                Check.Equal(4, serializedPayload[0], "native health field still leading the payload");
                Check.Equal(2, serializedPayload[5], "native maskIndex field still third");

                // 客户端 = 另一个进程/另一个场景：带上 host 的 bytes 后切场景。
                Fixture.ResetWorld(host: false);
                UnityEngine.Random.SetSequence();
                TrollFixture client = TrollFixture.Create();
                client.RegisterHeader();
                NativeFlow.DeliverRpc(client.Header, client.SlotIndex, livePayload);
                Check.Equal(0, UnityEngine.Random.CallCount, "client applies the host decision with no local random");
                Check.Equal(12, HermesHeadwearVisuals.LastKind("Apply").Choice, "client shows the host choice");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "client tracks one generation");

                TrollFixture lateJoiner = TrollFixture.Create();
                NativeFlow.DeserializeClientPayload(lateJoiner.Troll, serializedPayload);
                Check.Equal(12, HermesHeadwearVisuals.LastKind("Apply").Choice, "late joiner restores the same choice");
                Check.Equal(2, lateJoiner.Troll._maskIndex, "late joiner still gets the native mask index");
                Check.Equal(2, PatchDivine_HermesHeadwear.TrackedStateCount, "both client objects are tracked now");
            });

            Case.Run("network.lateJoinTailFollowsHostEnabledState", () =>
            {
                UnityEngine.Random.SetSequence(0, 5);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 1);

                ModConfig.HermesHeadwearEnabled.Value = false;
                Fixture.Tick();
                byte[] offPayload = NativeFlow.SerializeHostPayload(host.Troll);
                Check.True(NativeFlow.TryReadSerializedTail(offPayload, out HermesHeadwearCodec.Tail offTail),
                    "host-off tail parses");
                Check.True(!offTail.HostEnabled, "tail carries the host enabled flag");
                Check.True(offTail.Revision >= 2, "tail revision advanced with the toggle");

                ModConfig.HermesHeadwearEnabled.Value = true;
                Fixture.Tick();
                byte[] onPayload = NativeFlow.SerializeHostPayload(host.Troll);
                Check.True(NativeFlow.TryReadSerializedTail(onPayload, out HermesHeadwearCodec.Tail onTail),
                    "host-on tail parses");
                Check.Equal(offTail.Token, onTail.Token, "same generation token across toggles");
                Check.True(onTail.Revision > offTail.Revision, "revision stays monotonic");

                // 后期加入者 = 新进程/新场景，按顺序重放两条 payload。
                Fixture.ResetWorld(host: false);
                TrollFixture lateJoiner = TrollFixture.Create();
                NativeFlow.DeserializeClientPayload(lateJoiner.Troll, offPayload);
                Check.Equal(0, HermesHeadwearVisuals.CountKind("Apply"), "late joiner applies nothing while the host is off");

                NativeFlow.DeserializeClientPayload(lateJoiner.Troll, onPayload);
                Check.Equal(5, HermesHeadwearVisuals.LastKind("Apply").Choice, "late joiner replays the frozen choice");
            });

            Case.Run("network.legacyPayloadNeverRollsNorClears", () =>
            {
                NetworkBigBoss.HasWorldAuth = false;
                TrollFixture client = TrollFixture.Create();
                client.RegisterHeader();

                ByteBuffer.PrepWriteBuffer();
                ByteBuffer.Write(6);
                ByteBuffer.Write(true);
                ByteBuffer.Write(2);
                byte[] legacy = ByteBuffer.Finalise();

                NativeFlow.DeserializeClientPayload(client.Troll, legacy);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "legacy payload never invents a receipt");
                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "legacy payload never touches visuals");
                Check.Equal(0, UnityEngine.Random.CallCount, "legacy payload never rolls");
                Check.Equal(2, client.Troll._maskIndex, "native maskIndex still restored by the native part");

                UnityEngine.Random.SetSequence(0, 8);
                NetworkBigBoss.HasWorldAuth = true;
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 1);
                Fixture.Tick();

                NetworkBigBoss.HasWorldAuth = false;
                NativeFlow.DeliverRpc(client.Header, client.SlotIndex, host.Header.LastPayload);
                Check.Equal(8, HermesHeadwearVisuals.LastKind("Apply").Choice, "client adopted the host choice");

                NativeFlow.DeserializeClientPayload(client.Troll, legacy);
                Check.Equal(8, HermesHeadwearVisuals.LastKind("Apply").Choice,
                    "a legacy payload never wipes an existing receipt");
            });

            Case.Run("network.preRegistrationDefersThenSendsOnce", () =>
            {
                UnityEngine.Random.SetSequence(0, 6);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);

                Fixture.Tick();
                Check.Null(NetworkPostbox.Instance.GetHeaderFromObject(fx.Owner, false),
                    "no header registered yet, so no send can happen");

                fx.RegisterHeader();
                Check.Equal(1, fx.SlotIndex, "own slot appended after the native slot");
                Fixture.Tick();
                Check.Equal(1, fx.Header.CallMethodRemotelyCalls, "deferred decision sent exactly once after registration");
                Check.Equal(fx.SlotIndex, fx.Header.LastRemoteFunctionId, "send uses the appended tail slot");
                Check.True(HermesHeadwearCodec.TryReadTail(fx.Header.LastPayload, out HermesHeadwearCodec.Tail tail),
                    "deferred payload parses");
                Check.Equal(6, tail.Choice, "deferred payload keeps the original choice");
                Check.Equal(1, tail.Revision, "deferred payload keeps the original revision (no re-decide)");
            });

            Case.Run("network.clientRejectsCrossGenerationAndStaleRevision", () =>
            {
                UnityEngine.Random.SetSequence(0, 4);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 1);
                Fixture.Tick();
                byte[] hostPayload = (byte[])host.Header.LastPayload.Clone();
                Check.True(HermesHeadwearCodec.TryReadTail(hostPayload, out HermesHeadwearCodec.Tail hostTail),
                    "host tail parses");

                NetworkBigBoss.HasWorldAuth = false;
                TrollFixture client = TrollFixture.Create();
                client.RegisterHeader();
                NativeFlow.DeliverRpc(client.Header, client.SlotIndex, hostPayload);
                Check.Equal(4, HermesHeadwearVisuals.LastKind("Apply").Choice, "client adopted the host choice");
                int applied = HermesHeadwearVisuals.CountKind("Apply");

                NativeFlow.DeliverRpc(client.Header, client.SlotIndex,
                    Tail(Guid.NewGuid(), 33, true, 9));
                Check.Equal(4, HermesHeadwearVisuals.LastKind("Apply").Choice, "cross-generation update rejected");
                Check.Equal(applied, HermesHeadwearVisuals.CountKind("Apply"), "rejected update applies nothing");

                NativeFlow.DeliverRpc(client.Header, client.SlotIndex,
                    Tail(hostTail.Token, 20, true, 0));
                Check.Equal(4, HermesHeadwearVisuals.LastKind("Apply").Choice, "stale revision rejected");
                Check.Equal(applied, HermesHeadwearVisuals.CountKind("Apply"), "stale revision applies nothing");

                int cleared = HermesHeadwearVisuals.CountKind("Clear");
                NativeFlow.DeliverRpc(client.Header, client.SlotIndex,
                    Tail(hostTail.Token, 20, false, hostTail.Revision + 1));
                Check.Equal(applied, HermesHeadwearVisuals.CountKind("Apply"),
                    "newer same-generation update wins (host off applies no headwear)");
                Check.Equal(cleared + 1, HermesHeadwearVisuals.CountKind("Clear"),
                    "host-off decision clears the visual");

                NativeFlow.DeliverRpc(client.Header, client.SlotIndex,
                    Tail(hostTail.Token, 20, true, hostTail.Revision + 2));
                Check.Equal(20, HermesHeadwearVisuals.LastKind("Apply").Choice,
                    "ordered replay reaches the newest host decision");
                Check.Equal(applied + 1, HermesHeadwearVisuals.CountKind("Apply"),
                    "exactly one apply for the newest decision");
            });

            Case.Run("network.retiredTokenRejectedAfterPoolReuse", () =>
            {
                UnityEngine.Random.SetSequence(0, 18);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 1);
                Fixture.Tick();
                byte[] payload = (byte[])host.Header.LastPayload.Clone();

                // 客户端 = 另一个进程/场景
                Fixture.ResetWorld(host: false);
                TrollFixture client = TrollFixture.Create();
                client.RegisterHeader();
                NativeFlow.DeliverRpc(client.Header, client.SlotIndex, payload);
                Check.Equal(18, HermesHeadwearVisuals.LastKind("Apply").Choice, "client adopted the host choice");
                int applied = HermesHeadwearVisuals.CountKind("Apply");
                int cleared = HermesHeadwearVisuals.CountKind("Clear");

                PatchDivine_HermesHeadwear.HandlePoolSpawn(client.Owner);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "generation released on pool reuse");
                Check.True(HermesHeadwearVisuals.CountKind("Clear") > cleared, "visual released on pool reuse");
                int clearsAfterReuse = HermesHeadwearVisuals.CountKind("Clear");

                NativeFlow.DeliverRpc(client.Header, client.SlotIndex, payload);
                Check.Equal(applied, HermesHeadwearVisuals.CountKind("Apply"),
                    "a late update for the retired generation never applies");
                Check.Equal(clearsAfterReuse, HermesHeadwearVisuals.CountKind("Clear"),
                    "a late update for the retired generation never clears");
            });

            Case.Run("network.foreignDelegateInOldSlotNeverReceivesOurSend", () =>
            {
                UnityEngine.Random.SetSequence(0, 14);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(1, host.Header.CallMethodRemotelyCalls, "first decision sent");
                int oldSlot = host.Header.LastRemoteFunctionId;
                int nativeSlotDelegateCalls = host.Header.RemoteMethodList[0].InvokeCalls;

                // 原生把我们的旧槽位回收给了别的 RPC（原生 RegisterRPC 会复用空槽）：
                // 旧槽位现在装着“别人的委托”。
                NetworkPostbox.DynAction foreign = new NetworkPostbox.DynAction(delegate { });
                host.Header.RemoteMethodList[oldSlot] = foreign;

                ModConfig.HermesHeadwearEnabled.Value = false;
                Fixture.Tick();
                Check.Equal(2, host.Header.CallMethodRemotelyCalls, "decision re-sent after losing the slot");
                Check.Equal(0, foreign.InvokeCalls, "the foreign delegate in the old slot is never invoked");
                Check.Equal(nativeSlotDelegateCalls, host.Header.RemoteMethodList[0].InvokeCalls,
                    "native slot untouched");

                int newSlot = host.Header.LastRemoteFunctionId;
                Check.True(newSlot != oldSlot, "resend went to a newly appended tail slot");
                Check.Equal(host.Header.RemoteMethodList.Count - 1, newSlot,
                    "the appended slot is the last entry (native slots/holes preserved)");
                Check.True(host.Header.RemoteMethodList[newSlot].Pointer != foreign.Pointer,
                    "the new slot is not the foreign delegate");
                Check.True(HermesHeadwearCodec.TryReadTail(host.Header.LastPayload, out HermesHeadwearCodec.Tail tail),
                    "resend payload parses");
                Check.Equal(14, tail.Choice, "resend keeps the frozen choice");
            });

            Case.Run("network.bindingsSurviveWorldChangeWithoutExtendingSlots", () =>
            {
                UnityEngine.Random.SetSequence(0, 16);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                int slotsAfterRegister = host.Header.RemoteMethodList.Count;
                Check.Equal(2, slotsAfterRegister, "native slot + our appended tail slot");

                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(1, host.Header.CallMethodRemotelyCalls, "decision sent in the current world");

                // 世代释放（死亡/回收），但 header/绑定仍在
                PatchDivine_HermesHeadwear.HandleNativeResetAndDespawn(host.Troll);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "generation released");

                // 换世界：绑定必须保留（原生 header 槽没有 Flush）。推进到扫描+淘汰窗口之后。
                Managers.Inst.world = new World { Pointer = new IntPtr(987654321) };
                Fixture.TickAfter(6f);
                Check.Equal(slotsAfterRegister, host.Header.RemoteMethodList.Count,
                    "world change never re-appends our slot");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "old-world generations are gone after the sweep");

                // 同 header 再 RegisterComponents / 再 Init：槽位数不增长
                PatchDivine_HermesHeadwear.HandleHeaderRegistered(host.Header, host.Owner, true);
                Check.Equal(slotsAfterRegister, host.Header.RemoteMethodList.Count,
                    "re-registration reuses the existing binding");

                UnityEngine.Random.SetSequence(0, 21);
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(slotsAfterRegister, host.Header.RemoteMethodList.Count,
                    "a new generation still uses the same appended slot");
                Check.Equal(slotsAfterRegister - 1, host.Header.LastRemoteFunctionId,
                    "send uses the existing appended slot after the world change");
            });

            Case.Run("network.foreignPayloadBytesNotConsumed", () =>
            {
                NetworkBigBoss.HasWorldAuth = false;
                TrollFixture client = TrollFixture.Create();
                client.RegisterHeader();

                byte[] foreign = new byte[HermesHeadwearCodec.TailBytes];
                foreign[0] = 0xDE;
                foreign[1] = 0xAD;
                foreign[2] = 0xBE;
                foreign[3] = 0xEF;

                ByteBuffer.PrepWriteBuffer();
                ByteBuffer.Write(foreign);
                ByteBuffer.RewindHeader();
                client.Header.CallMethodLocally((byte)client.SlotIndex);

                Check.Equal(0, ByteBuffer.PollIndex(), "foreign payload bytes must not be consumed");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "foreign payload never creates a generation");
                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "foreign payload never touches visuals");
            });

            Case.Run("network.sendGateWaitsForCaughtUpThenResendsAfterRejoin", () =>
            {
                NetworkBigBoss.IsClientPresent = true;
                NetworkBigBoss.HasClientCaughtUp = false; // 对端尚未追平：只能保留待发

                UnityEngine.Random.SetSequence(0, 22);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(0, host.Header.CallMethodRemotelyCalls, "no send before the client caught up");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "pending decision kept");
                Check.Equal(2, UnityEngine.Random.CallCount, "no re-roll while waiting");

                NetworkBigBoss.HasClientCaughtUp = true;
                Fixture.Tick();
                Check.Equal(1, host.Header.CallMethodRemotelyCalls, "decision sent once the client caught up");
                Check.True(HermesHeadwearCodec.TryReadTail(host.Header.LastPayload, out HermesHeadwearCodec.Tail first),
                    "caught-up payload parses");
                Check.Equal(22, first.Choice, "caught-up payload keeps the frozen choice");
                Check.Equal(1, first.Revision, "caught-up payload keeps the original revision");

                // 客户端掉线/重进：先离场再追平，已有的活收据必须重新排发（含曾发送过的）。
                NetworkBigBoss.HasClientCaughtUp = false;
                Fixture.Tick();
                Check.Equal(1, host.Header.CallMethodRemotelyCalls, "no send while the client is away");

                NetworkBigBoss.HasClientCaughtUp = true;
                Fixture.Tick();
                Check.Equal(2, host.Header.CallMethodRemotelyCalls, "live receipts are re-sent after rejoin");
                Check.True(HermesHeadwearCodec.TryReadTail(host.Header.LastPayload, out HermesHeadwearCodec.Tail again),
                    "rejoin payload parses");
                Check.Equal(first.Token, again.Token, "rejoin keeps the same generation token");
                Check.Equal(22, again.Choice, "rejoin keeps the same choice");
                Check.Equal(2, UnityEngine.Random.CallCount, "rejoin never re-rolls");
            });

            Case.Run("network.poolReuseClearsPendingSend", () =>
            {
                NetworkBigBoss.IsClientPresent = true;
                NetworkBigBoss.HasClientCaughtUp = false;

                UnityEngine.Random.SetSequence(0, 29);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(0, host.Header.CallMethodRemotelyCalls, "pending while not caught up");

                PatchDivine_HermesHeadwear.HandlePoolSpawn(host.Owner);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "pool reuse releases the generation");

                NetworkBigBoss.HasClientCaughtUp = true;
                Fixture.Tick();
                Check.Equal(0, host.Header.CallMethodRemotelyCalls,
                    "a released generation never sends its stale decision after catch-up");
            });

            Case.Run("network.malformedTailNeverMovesCursorNorRolls", () =>
            {
                UnityEngine.Random.SetSequence(0, 17);
                TrollFixture host = TrollFixture.Create();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 2);
                byte[] valid = NativeFlow.SerializeHostPayload(host.Troll);
                UnityEngine.Random.SetSequence(); // 之后客户端的任何抽选都会被记数

                Fixture.ResetWorld(host: false);
                TrollFixture client = TrollFixture.Create();

                // 1) 未知版本
                byte[] badVersion = (byte[])valid.Clone();
                badVersion[9 + 4] = 2;
                NativeFlow.DeserializeClientPayload(client.Troll, badVersion);
                Check.Equal(9, ByteBuffer.PollIndex(), "bad version must not consume the tail bytes");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "bad version never creates a generation");

                // 2) 非法 choice
                byte[] badChoice = (byte[])valid.Clone();
                badChoice[9 + 21] = 44;
                NativeFlow.DeserializeClientPayload(client.Troll, badChoice);
                Check.Equal(9, ByteBuffer.PollIndex(), "bad choice must not consume the tail bytes");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "bad choice never creates a generation");

                // 3) 长度不足：原生 3 字段之后没有尾巴
                ByteBuffer.PrepWriteBuffer();
                ByteBuffer.Write(3);
                ByteBuffer.Write(false);
                ByteBuffer.Write(1);
                byte[] shortPayload = ByteBuffer.Finalise();
                NativeFlow.DeserializeClientPayload(client.Troll, shortPayload);
                Check.Equal(9, ByteBuffer.PollIndex(), "short payload must not consume anything");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "short payload never creates a generation");

                // 4) magic 不符（外来的 30 字节）
                byte[] foreign = new byte[HermesHeadwearCodec.TailBytes];
                foreign[0] = 0xDE;
                NativeFlow.DeserializeClientPayload(client.Troll, foreign);
                Check.Equal(9, ByteBuffer.PollIndex(), "foreign payload must not consume anything");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "foreign payload never creates a generation");

                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "malformed tails never touch visuals");
                Check.Equal(0, UnityEngine.Random.CallCount, "malformed tails never roll locally");
            });

            Case.Run("network.staleBufferTailIsNotRead", () =>
            {
                NetworkBigBoss.HasWorldAuth = false;
                TrollFixture client = TrollFixture.Create();

                // 缓冲里先留一条更长的旧消息：末尾是合法尾巴（残留在静态缓冲里）。
                ByteBuffer.PrepWriteBuffer();
                ByteBuffer.Write(9);
                ByteBuffer.Write(false);
                ByteBuffer.Write(4);
                ByteBuffer.Write(Tail(Guid.NewGuid(), 33, true, 7));
                ByteBuffer.RewindHeader();

                // 再写入当前这条更短的、没有尾巴的消息：读上限之后紧跟着旧尾巴。
                ByteBuffer.PrepWriteBuffer();
                ByteBuffer.Write(3);
                ByteBuffer.Write(false);
                ByteBuffer.Write(1);
                ByteBuffer.RewindHeader();
                Check.Equal((byte)0x4B, ByteBuffer.bufferAccess(9),
                    "stale tail magic really sits right after the read limit");

                client.Troll._trollHealth = ByteBuffer.ReadInt();
                client.Troll._toughTroll = ByteBuffer.ReadBool();
                client.Troll._maskIndex = ByteBuffer.ReadInt();
                PatchDivine_HermesHeadwear.HandleNativeDeserialize(client.Troll);

                Check.Equal(9, ByteBuffer.PollIndex(), "stale bytes beyond the read limit are never consumed");
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "stale buffer content never becomes a receipt");
                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "stale buffer content never touches visuals");
                Check.Equal(0, UnityEngine.Random.CallCount, "stale buffer content never rolls");
            });

            Case.Run("network.hostNeverAcceptsClientDecisions", () =>
            {
                UnityEngine.Random.SetSequence(0, 25);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 1);
                int applied = HermesHeadwearVisuals.CountKind("Apply");

                NativeFlow.DeliverRpc(host.Header, host.SlotIndex, Tail(Guid.NewGuid(), 41, true, 99));
                Check.Equal(applied, HermesHeadwearVisuals.CountKind("Apply"), "host ignores client-provided decisions");
                Check.Equal(25, HermesHeadwearVisuals.LastKind("Apply").Choice, "host keeps its own choice");
            });

            Case.Run("network.flushDropsOwnershipAndReappends", () =>
            {
                UnityEngine.Random.SetSequence(0, 13);
                TrollFixture host = TrollFixture.Create();
                host.RegisterHeader();
                NativeFlow.ConvertByHermes(host.Troll, 4, false, 1);
                Fixture.Tick();
                Check.Equal(1, host.Header.CallMethodRemotelyCalls, "first decision sent");

                host.Header.Flush();
                PatchDivine_HermesHeadwear.HandleHeaderFlushed(host.Header);
                Check.Equal(0, host.Header.RemoteMethodList.Count, "native flush cleared the list");

                ModConfig.HermesHeadwearEnabled.Value = false;
                Fixture.Tick();
                Check.Equal(2, host.Header.CallMethodRemotelyCalls, "decision re-sent after ownership was dropped");
                Check.Equal(host.Header.RemoteMethodList.Count - 1, host.Header.LastRemoteFunctionId,
                    "resend uses a freshly appended tail slot");
                Check.True(HermesHeadwearCodec.TryReadTail(host.Header.LastPayload, out HermesHeadwearCodec.Tail tail),
                    "resend payload parses");
                Check.True(!tail.HostEnabled, "resend carries the changed enabled state");
                Check.Equal(13, tail.Choice, "resend keeps the frozen choice");
            });
        }

        private static byte[] Tail(Guid token, int choice, bool hostEnabled, int revision)
        {
            byte[] buffer = new byte[HermesHeadwearCodec.TailBytes];
            Check.True(HermesHeadwearCodec.WriteTail(buffer, token, choice, hostEnabled, revision), "craft tail");
            return buffer;
        }
    }
}
