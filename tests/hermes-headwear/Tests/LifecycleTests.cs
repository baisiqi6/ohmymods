using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace HermesHeadwearTests
{
    /// <summary>生命周期：池回收世代重置、死亡/复原清理、失活/激活、换世界、有界重试、总开关。</summary>
    internal static class LifecycleTests
    {
        internal static void Run()
        {
            Console.WriteLine("Lifecycle (generation, cleanup, bounded retry)");

            Case.Run("lifecycle.poolReuseResetsGeneration", () =>
            {
                UnityEngine.Random.SetSequence(0, 12);
                TrollFixture fx = TrollFixture.Create();
                fx.RegisterHeader();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Fixture.Tick();
                byte[] oldGenerationPayload = NativeFlow.SerializeHostPayload(fx.Troll); // 原生 3 字段 + 旧世代尾巴

                PatchDivine_HermesHeadwear.HandlePoolSpawn(fx.Owner);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "previous generation released before Init");
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Clear"), "previous visual released");

                UnityEngine.Random.SetSequence(0, 20);
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Check.Equal(20, HermesHeadwearVisuals.LastKind("Apply").Choice, "reused instance rolls a fresh choice");
                Fixture.Tick();
                Check.Equal(2, fx.Header.CallMethodRemotelyCalls, "both generations sent their own decision");

                Check.True(NativeFlow.TryReadSerializedTail(oldGenerationPayload, out HermesHeadwearCodec.Tail oldTail),
                    "old payload parses");
                Check.True(HermesHeadwearCodec.TryReadTail(fx.Header.LastPayload, out HermesHeadwearCodec.Tail newTail),
                    "new payload parses");
                Check.True(oldTail.Token != newTail.Token, "pool reuse mints a new generation token");

                int applied = HermesHeadwearVisuals.CountKind("Apply");
                NativeFlow.DeserializeClientPayload(fx.Troll, oldGenerationPayload);
                Check.Equal(applied, HermesHeadwearVisuals.CountKind("Apply"),
                    "a retired generation never overwrites the live one");
                Check.Equal(20, HermesHeadwearVisuals.LastKind("Apply").Choice, "live choice untouched");
            });

            Case.Run("lifecycle.resetAndDespawnDropsReceipt", () =>
            {
                UnityEngine.Random.SetSequence(0, 23);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "generation tracked");

                PatchDivine_HermesHeadwear.HandleNativeResetAndDespawn(fx.Troll);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "reverted/despawned troll releases its receipt");
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Clear"), "visual released on despawn");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                Check.True(saved.componentData2[0].data.IndexOf(HermesHeadwearCodec.MetadataKey, StringComparison.Ordinal) < 0,
                    "a released generation is not persisted");
            });

            Case.Run("lifecycle.persistentDisableKeepsReceiptAndReactivates", () =>
            {
                UnityEngine.Random.SetSequence(0, 14);
                TrollFixture fx = TrollFixture.Create();
                fx.RegisterHeader();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Check.Equal(14, HermesHeadwearVisuals.LastKind("Apply").Choice, "conversion applied");

                PatchDivine_HermesHeadwear.HandlePersistentDisable(fx.Persistent);
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Clear"), "disable detaches the owned visual");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "receipt survives the disable");

                fx.Owner.activeInHierarchy = false;
                Fixture.TickThroughSweep();
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Apply"), "inactive object gets no owned visual");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo receipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(saved).data);
                Check.True(receipt.Recognized && receipt.Choice == 14, "receipt still written while disabled");

                fx.Owner.activeInHierarchy = true;
                Fixture.TickThroughSweep(); // 扫描发现重新活跃并排入重放（含下一帧补一次 Tick）
                Check.Equal(14, HermesHeadwearVisuals.LastKind("Apply").Choice,
                    "same choice replayed after reactivation");
                Check.Equal(2, HermesHeadwearVisuals.CountKind("Apply"), "exactly one replay, no re-roll");
            });

            Case.Run("lifecycle.destroyedTrollSweptFromRegistry", () =>
            {
                UnityEngine.Random.SetSequence(0, 27);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);

                fx.Owner.MarkDestroyed();
                Fixture.TickThroughSweep();
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "destroyed generation swept");
                Check.True(HermesHeadwearVisuals.TickCalls >= 1,
                    "visual layer is still ticked so it can prune its own states");
                Check.True(HermesHeadwearVisuals.ClearCalls >= 1,
                    "sweeping a dead generation also releases its owned visual");
            });

            Case.Run("lifecycle.worldChangeReleasesEverything", () =>
            {
                UnityEngine.Random.SetSequence(0, 16);
                TrollFixture fx = TrollFixture.Create();
                fx.RegisterHeader();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(1, fx.Header.CallMethodRemotelyCalls, "decision sent in the current world");

                Managers.Inst.world = new World { Pointer = new IntPtr(987654321) };
                Fixture.TickThroughSweep();
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "world/layer/scene replacement releases every generation");
                Check.True(HermesHeadwearVisuals.ClearAllCalls >= 1, "visual layer told to drop everything");

                int sent = fx.Header.CallMethodRemotelyCalls;
                Fixture.TickThroughSweep();
                Check.Equal(sent, fx.Header.CallMethodRemotelyCalls, "no sends after the world changed");
            });

            Case.Run("lifecycle.staleWorldGenerationVisualIsCleared", () =>
            {
                UnityEngine.Random.SetSequence(0, 26);
                TrollFixture oldWorld = TrollFixture.Create();
                NativeFlow.ConvertByHermes(oldWorld.Troll, 4, false, 2);
                Check.Equal(26, HermesHeadwearVisuals.LastKind("Apply").Choice, "old world generation applied");
                int clearsBefore = HermesHeadwearVisuals.CountKind("Clear");

                // 新世界已经有世代（因此不会走 ReleaseAll 的整场清空），旧世界对象仍然 active：
                // 旧世代必须自己被淘汰并撤掉外观，不能因为“新世界已有 state”而漏清。
                Managers.Inst.world = new World { Pointer = new IntPtr(555000111) };
                UnityEngine.Random.SetSequence(0, 31);
                TrollFixture newWorld = TrollFixture.Create();
                NativeFlow.ConvertByHermes(newWorld.Troll, 4, false, 2);
                Fixture.TickThroughSweep();

                Check.True(HermesHeadwearVisuals.CountKind("Clear") > clearsBefore,
                    "stale-world generation released its owned visual");
                Check.Equal(31, HermesHeadwearVisuals.LastKind("Apply").Choice,
                    "new-world generation keeps its own choice");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "only the current-world generation remains tracked");
            });

            Case.Run("lifecycle.visualFailureRetriesWithBackoffThenRecovers", () =>
            {
                UnityEngine.Random.SetSequence(0, 37);
                HermesHeadwearVisuals.ApplyFailuresRemaining = 100;
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);

                Check.Equal(1, HermesHeadwearVisuals.CountKind("Apply"), "initial attempt happened");
                Check.Equal(0, HermesHeadwearVisuals.CountKind("Clear"), "failure never clears the native look");

                Fixture.TickAfter(0.5f);
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Apply"), "backoff waits before the first retry");
                Fixture.TickAfter(0.6f);
                Check.Equal(2, HermesHeadwearVisuals.CountKind("Apply"), "first retry after ~1s");

                Fixture.TickAfter(1.0f);
                Check.Equal(2, HermesHeadwearVisuals.CountKind("Apply"), "second retry waits ~2s");
                Fixture.TickAfter(1.1f);
                Check.Equal(3, HermesHeadwearVisuals.CountKind("Apply"), "backoff doubled to ~2s");

                Fixture.TickAfter(0.9f);
                Check.Equal(3, HermesHeadwearVisuals.CountKind("Apply"), "third retry waits ~4s");
                Fixture.TickAfter(3.2f);
                Check.Equal(4, HermesHeadwearVisuals.CountKind("Apply"), "backoff doubled to ~4s");

                // 资源补齐（visual 层 30s 内就绪）：同一 choice 恢复，永不重抽
                HermesHeadwearVisuals.ApplyFailuresRemaining = 0;
                Fixture.TickAfter(8.0f);
                Check.Equal(5, HermesHeadwearVisuals.CountKind("Apply"), "retries continue until the visual is ready");
                Check.Equal(37, HermesHeadwearVisuals.LastKind("Apply").Choice, "recovery uses the frozen choice");
                Check.Equal(2, UnityEngine.Random.CallCount, "one chance roll + one choice roll only");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo receipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(saved).data);
                Check.True(receipt.Recognized && receipt.Choice == 37,
                    "receipt unaffected by the visual failure window");
            });

            Case.Run("lifecycle.visualFailureNeverGivesUpOnLongOutage", () =>
            {
                UnityEngine.Random.SetSequence(0, 43);
                HermesHeadwearVisuals.ApplyFailuresRemaining = 100;
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Apply"), "initial attempt happened");

                Fixture.TickAfter(1.1f);
                Fixture.TickAfter(2.1f);
                Fixture.TickAfter(4.1f);
                Fixture.TickAfter(8.1f);
                Fixture.TickAfter(16.1f);
                int attemptsAtCap = HermesHeadwearVisuals.CountKind("Apply");
                Check.Equal(6, attemptsAtCap, "backoff progression 1+2+4+8+16s");

                Fixture.TickAfter(29f);
                Check.Equal(attemptsAtCap, HermesHeadwearVisuals.CountKind("Apply"),
                    "capped backoff waits ~30s instead of spinning");
                Fixture.TickAfter(1.5f);
                Check.Equal(attemptsAtCap + 1, HermesHeadwearVisuals.CountKind("Apply"),
                    "retry continues at the 30s cap (never permanently abandoned)");

                Check.Equal(43, HermesHeadwearVisuals.LastKind("Apply").Choice, "always the same frozen choice");
                Check.Equal(0, HermesHeadwearVisuals.CountKind("Clear"), "no native look was ever cleared");
                Check.Equal(2, UnityEngine.Random.CallCount, "no re-roll across the whole outage");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo receipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(saved).data);
                Check.True(receipt.Recognized && receipt.Choice == 43,
                    "receipt stays valid while the visual layer is unavailable");
            });

            Case.Run("lifecycle.loadedReceiptSurvivesPopWindow", () =>
            {
                UnityEngine.Random.SetSequence(0, 21);
                TrollFixture source = TrollFixture.Create();
                NativeFlow.ConvertByHermes(source.Troll, 5, true, 4);
                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(source.Persistent);

                // 读档 = 新进程/新场景：先把盘上 bytes 拿在手里再切场景。
                Fixture.ResetWorld();
                // 读档窗口：对象还没挂回 gameLayer（身份闸门 false），原生仍在建场景。
                GreekBankScope.IsInCurrentLayerFunc = component => false;
                IslandSaveData.poppingObjectsToScene = true;
                TrollFixture loaded = TrollFixture.Create();
                PatchDivine_HermesHeadwear.HandlePoolSpawn(loaded.Owner);
                PatchDivine_HermesHeadwear.HandleTryCreateOrFind(saved, loaded.Persistent);
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "receipt bound during the pop window");

                Fixture.TickAfter(6f); // 超过 5s 保护窗口，但原生仍在建场景
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "native pop window protects a freshly loaded receipt");
                IslandSaveData.ObjectData savedDuringPop = NativeFlow.BuildSaveObject(loaded.Persistent);
                HermesHeadwearCodec.ReceiptInfo duringPop =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(savedDuringPop).data);
                Check.True(duringPop.Recognized && duringPop.Choice == 21,
                    "a save right after load still carries the receipt");

                // 加载结束、层身份就绪
                IslandSaveData.poppingObjectsToScene = false;
                GreekBankScope.IsInCurrentLayerFunc = component => component != null;
                UnityEngine.Random.SetSequence();
                Fixture.TickThroughSweep();
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "generation stays alive once the layer resolves");

                int appliesBefore = HermesHeadwearVisuals.CountKind("Apply");
                NativeFlow.ApplyNativeDataJson(loaded.Troll, NativeFlow.FindFriendlyComponent(saved).data);
                Check.Equal(21, HermesHeadwearVisuals.LastKind("Apply").Choice,
                    "post-load ApplyData replays the frozen choice");
                Check.Equal(appliesBefore + 1, HermesHeadwearVisuals.CountKind("Apply"),
                    "post-load ApplyData applies the visual exactly once");
                Check.Equal(0, UnityEngine.Random.CallCount, "the load path never rolls");
            });

            Case.Run("lifecycle.viewLossTriggersReapplySameChoice", () =>
            {
                UnityEngine.Random.SetSequence(0, 35);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Check.Equal(35, HermesHeadwearVisuals.LastKind("Apply").Choice, "conversion applied");
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Apply"), "one apply so far");

                // root/sprite 被销毁：视图消失，但核心侧仍记着“已显示”
                HermesHeadwearVisuals.SimulateViewLoss(fx.Troll);
                Fixture.TickThroughSweep();
                Check.Equal(2, HermesHeadwearVisuals.CountKind("Apply"), "view loss triggers exactly one re-apply");
                Check.Equal(35, HermesHeadwearVisuals.LastKind("Apply").Choice, "re-apply uses the same choice");
                Check.Equal(2, UnityEngine.Random.CallCount, "view loss never re-rolls");
                Check.Equal(0, HermesHeadwearVisuals.CountKind("Clear"), "view loss never clears");

                // 关闭时不复活
                ModConfig.HermesHeadwearEnabled.Value = false;
                Fixture.Tick();
                HermesHeadwearVisuals.SimulateViewLoss(fx.Troll);
                int applyAfterOff = HermesHeadwearVisuals.CountKind("Apply");
                Fixture.TickThroughSweep();
                Check.Equal(applyAfterOff, HermesHeadwearVisuals.CountKind("Apply"),
                    "no view-loss re-apply while the feature is off");
            });

            Case.Run("lifecycle.settingsCallbackOnlyFlagsDirty", () =>
            {
                UnityEngine.Random.SetSequence(0, 17);
                TrollFixture fx = TrollFixture.Create();
                fx.RegisterHeader();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Fixture.Tick();
                Check.Equal(1, fx.Header.CallMethodRemotelyCalls, "baseline decision sent");
                Check.Equal(17, HermesHeadwearVisuals.LastKind("Apply").Choice, "baseline applied");

                int unityBefore = UnityEngine.Time.AccessCount;
                int applyBefore = HermesHeadwearVisuals.CountKind("Apply");
                int clearBefore = HermesHeadwearVisuals.CountKind("Clear");
                int sendsBefore = fx.Header.CallMethodRemotelyCalls;

                ModConfig.HermesHeadwearEnabled.Value = false;
                PatchDivine_HermesHeadwear.OnSettingsChanged(); // 可能来自 file watcher 线程

                Check.Equal(unityBefore, UnityEngine.Time.AccessCount,
                    "settings callback must not touch Unity on the caller thread");
                Check.Equal(applyBefore, HermesHeadwearVisuals.CountKind("Apply"),
                    "settings callback does no visual work");
                Check.Equal(clearBefore, HermesHeadwearVisuals.CountKind("Clear"),
                    "settings callback does no visual work");
                Check.Equal(sendsBefore, fx.Header.CallMethodRemotelyCalls,
                    "settings callback sends nothing");

                Fixture.Tick(); // 主线程收敛
                Check.True(UnityEngine.Time.AccessCount > unityBefore,
                    "convergence happens on the main-thread Tick");
                Check.Equal(clearBefore + 1, HermesHeadwearVisuals.CountKind("Clear"),
                    "tick clears the owned visual after the toggle");
                Check.Equal(sendsBefore + 1, fx.Header.CallMethodRemotelyCalls,
                    "tick resends the changed enabled state");
                Check.True(HermesHeadwearCodec.TryReadTail(fx.Header.LastPayload, out HermesHeadwearCodec.Tail tail),
                    "post-toggle payload parses");
                Check.True(!tail.HostEnabled, "post-toggle payload carries hostEnabled=false");
                Check.Equal(17, tail.Choice, "post-toggle payload keeps the frozen choice");
            });

            Case.Run("lifecycle.globalSwitchOffClearsVisualKeepsReceipt", () =>
            {
                UnityEngine.Random.SetSequence(0, 19);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                Check.Equal(19, HermesHeadwearVisuals.LastKind("Apply").Choice, "conversion applied");

                ModConfig.Enabled.Value = false;
                Fixture.Tick();
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Clear"), "global switch off clears the visual");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "receipt kept while the mod is off");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo receipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(saved).data);
                Check.True(receipt.Recognized && receipt.Choice == 19, "receipt still written with the mod off");

                ModConfig.Enabled.Value = true;
                Fixture.Tick();
                Check.Equal(19, HermesHeadwearVisuals.LastKind("Apply").Choice, "same choice restored, never re-rolled");
            });
        }
    }
}
