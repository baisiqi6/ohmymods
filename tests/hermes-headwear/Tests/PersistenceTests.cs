using System;
using KingdomEnhancedMod;
using UnityEngine;

namespace HermesHeadwearTests
{
    /// <summary>盘上收据：原生字段保真、稳定 token/choice、关闭与未知 schema 的 fail-closed 行为。</summary>
    internal static class PersistenceTests
    {
        internal static void Run()
        {
            Console.WriteLine("Persistence (ObjectData component extension)");

            Case.Run("persistence.conversionMintsReceiptAndKeepsNativeFields", () =>
            {
                UnityEngine.Random.SetSequence(10, 7); // 10 < 30 → 命中；7 → choice 7
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 5, true, 3);

                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "one tracked generation");
                Check.Equal(7, HermesHeadwearVisuals.LastKind("Apply").Choice, "host applies the chosen code");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                IslandSaveData.ObjectData.ComponentData component = NativeFlow.FindFriendlyComponent(saved);
                Check.NotNull(component, "native FriendlyTrollData component present");

                HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(component.data);
                Check.True(receipt.Recognized, "v1 receipt recognized");
                Check.Equal(7, receipt.Choice, "choice persisted");
                Check.True(receipt.Token != Guid.Empty, "guid token persisted");

                Check.Contains(component.data, "\"maskIndex\":3", "native maskIndex bytes preserved");
                Check.Contains(component.data, "\"trollHealth\":5", "native health bytes preserved");
                Check.Contains(component.data, "\"toughTroll\":true", "native tough bytes preserved");
                Check.Equal(3, fx.Troll._maskIndex, "native mask field untouched");
                Check.Equal(5, fx.Troll._trollHealth, "native health field untouched");
                Check.True(fx.Troll._toughTroll, "native tough flag untouched");
            });

            Case.Run("persistence.missWritesExplicitChoiceNone", () =>
            {
                UnityEngine.Random.SetSequence(99); // 99 >= 30 → 未命中
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);

                Check.Equal(0, HermesHeadwearVisuals.CountKind("Apply"), "no headwear on a miss");
                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo receipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(saved).data);
                Check.True(receipt.Recognized, "miss still writes a valid receipt");
                Check.Equal(HermesHeadwearCodec.ChoiceNone, receipt.Choice, "explicit -1 persisted");
            });

            Case.Run("persistence.reloadKeepsChoiceAndNeverRerolls", () =>
            {
                UnityEngine.Random.SetSequence(0, 42);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 3);

                IslandSaveData.ObjectData first = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo firstReceipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(first).data);

                first.uniqueID = "regenerated-native-object-id";
                IslandSaveData.ObjectData second = NativeFlow.BuildSaveObject(fx.Persistent);
                HermesHeadwearCodec.ReceiptInfo secondReceipt =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(second).data);

                Check.Equal(firstReceipt.Token, secondReceipt.Token, "guid token stable across saves");
                Check.Equal(firstReceipt.Choice, secondReceipt.Choice, "choice stable across saves");

                // 读档 = 新进程/新场景：先有盘上 bytes，再切场景，避免把上一个 live 世代算进来。
                Fixture.ResetWorld();
                UnityEngine.Random.SetSequence(); // 之后任何抽选都会被记数
                TrollFixture loaded = NativeFlow.LoadFromSave(second);
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "receipt bound to the loaded object");
                Check.Equal(0, UnityEngine.Random.CallCount, "loading must never roll");
                Check.Equal(42, HermesHeadwearVisuals.LastKind("Apply").Choice, "visual replayed with the persisted choice");
                Check.Equal(3, loaded.Troll._maskIndex, "native maskIndex restored from the saved payload");
                Check.Equal(4, loaded.Troll._trollHealth, "native health restored from the saved payload");
            });

            Case.Run("persistence.legacySaveStaysNative", () =>
            {
                TrollFixture fixture = TrollFixture.Create();
                IslandSaveData.ObjectData legacy = new IslandSaveData.ObjectData(fixture.Persistent);
                const string legacyPayload = "{\"maskIndex\":-1,\"trollHealth\":2,\"toughTroll\":false}";
                legacy.componentData2.Add(new IslandSaveData.ObjectData.ComponentData(
                    "FriendlyTroll", "FriendlyTrollData", legacyPayload));

                PatchDivine_HermesHeadwear.HandlePoolSpawn(fixture.Owner);
                PatchDivine_HermesHeadwear.HandleTryCreateOrFind(legacy, fixture.Persistent);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount, "legacy save never creates a receipt");
                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "legacy save keeps native visuals");

                NativeFlow.ApplyNativeDataJson(fixture.Troll, legacyPayload);
                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "ApplyData path leaves legacy trolls alone");

                PatchDivine_HermesHeadwear.HandleObjectDataBuilt(legacy, fixture.Persistent, false);
                Check.Equal(legacyPayload, legacy.componentData2[0].data, "legacy payload never rewritten");

                NativeFlow.InitTroll(fixture.Troll);
                Check.Equal(0, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "Init outside a Hermes conversion never rolls");
                Check.Equal(0, UnityEngine.Random.CallCount, "no roll anywhere in the legacy path");
            });

            Case.Run("persistence.unknownSchemaNeverShowsNorRollsButStaysOpaque", () =>
            {
                string foreign = "{\"v\":2,\"id\":\"" + Guid.NewGuid().ToString("N") + "\",\"choice\":5}";
                string payload = "{\"maskIndex\":2,\"trollHealth\":3,\"toughTroll\":true,"
                    + "\"kemHermesHeadwear\":" + foreign + "}";

                TrollFixture fixture = TrollFixture.Create();
                IslandSaveData.ObjectData objectData = new IslandSaveData.ObjectData(fixture.Persistent);
                objectData.componentData2.Add(new IslandSaveData.ObjectData.ComponentData(
                    "FriendlyTroll", "FriendlyTrollData", payload));

                PatchDivine_HermesHeadwear.HandleTryCreateOrFind(objectData, fixture.Persistent);
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "unknown schema is kept as an opaque record on the object");
                Check.Equal(0, HermesHeadwearVisuals.ApplyCalls + HermesHeadwearVisuals.ClearCalls,
                    "unknown schema never touches visuals");
                Check.Equal(0, UnityEngine.Random.CallCount, "unknown schema never rolls");

                NativeFlow.ConvertByHermes(fixture.Troll, 3, true, 2);
                Check.Equal(0, UnityEngine.Random.CallCount, "repeated Init also preserves an opaque future receipt");
                var reinitialized = NativeFlow.BuildSaveObject(fixture.Persistent);
                Check.Equal(foreign, HermesHeadwearCodec.ExtractMetadataValue(NativeFlow.FindFriendlyComponent(reinitialized).data),
                    "repeated Init does not replace unknown metadata");

                PatchDivine_HermesHeadwear.HandleObjectDataBuilt(objectData, fixture.Persistent, false);
                Check.Equal(payload, objectData.componentData2[0].data,
                    "unknown schema kept verbatim when the same ObjectData is written again");
            });

            Case.Run("persistence.unknownSchemaSurvivesARealResave", () =>
            {
                // 关键：native RetrieveData 只会给出 3 个原生字段，未知扩展在原生 JSON 往返里会消失，
                // 必须由核心从世代状态逐字注回——所以这里用**全新的 ObjectData**（模拟真实保存）。
                string foreign = "{\"v\":3,\"future\":true,\"id\":\"" + Guid.NewGuid().ToString("N") + "\"}";
                string payload = "{\"maskIndex\":2,\"trollHealth\":3,\"toughTroll\":true,"
                    + "\"kemHermesHeadwear\":" + foreign + "}";

                TrollFixture fixture = TrollFixture.Create();
                IslandSaveData.ObjectData loaded = new IslandSaveData.ObjectData(fixture.Persistent);
                loaded.componentData2.Add(new IslandSaveData.ObjectData.ComponentData(
                    "FriendlyTroll", "FriendlyTrollData", payload));
                PatchDivine_HermesHeadwear.HandleTryCreateOrFind(loaded, fixture.Persistent);
                NativeFlow.ApplyNativeDataJson(fixture.Troll, payload); // 原生 ApplyData：原生 3 字段照常恢复

                IslandSaveData.ObjectData resaved = NativeFlow.BuildSaveObject(fixture.Persistent);
                IslandSaveData.ObjectData.ComponentData component = NativeFlow.FindFriendlyComponent(resaved);
                Check.NotNull(component, "native component present in the fresh save");
                Check.Contains(component.data, "\"kemHermesHeadwear\":" + foreign,
                    "opaque extension is re-injected verbatim on a real resave");
                Check.Contains(component.data, "\"maskIndex\":2", "native maskIndex untouched");
                Check.Contains(component.data, "\"trollHealth\":3", "native health untouched");
                Check.Contains(component.data, "\"toughTroll\":true", "native tough untouched");

                // 关闭功能保存同样保留 opaque
                ModConfig.HermesHeadwearEnabled.Value = false;
                IslandSaveData.ObjectData resavedDisabled = NativeFlow.BuildSaveObject(fixture.Persistent);
                Check.Contains(NativeFlow.FindFriendlyComponent(resavedDisabled).data,
                    "\"kemHermesHeadwear\":" + foreign,
                    "opaque extension survives a save while the feature is off");
            });

            Case.Run("persistence.disableKeepsReceiptAndStableAfterReload", () =>
            {
                ModConfig.HermesHeadwearChancePercent.Value = 30;
                UnityEngine.Random.SetSequence(0, 9);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 6, true, 5);
                Check.Equal(9, HermesHeadwearVisuals.LastKind("Apply").Choice, "conversion applied the choice");

                ModConfig.HermesHeadwearEnabled.Value = false;
                Fixture.Tick();
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Clear"), "host-off clears the owned visual");
                Check.Equal(1, HermesHeadwearVisuals.CountKind("Apply"), "host-off never re-decides");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "receipt survives the toggle");

                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                AssertReceiptChoice(saved, 9, "receipt written while the feature is off");

                ModConfig.HermesHeadwearEnabled.Value = true;
                Fixture.Tick();
                Check.Equal(9, HermesHeadwearVisuals.LastKind("Apply").Choice, "same choice re-applied after re-enable");
                Check.Equal(2, HermesHeadwearVisuals.CountKind("Apply"), "exactly one re-apply, no re-roll");

                // 读档 = 新进程/新场景
                Fixture.ResetWorld();
                UnityEngine.Random.SetSequence();
                TrollFixture loaded = NativeFlow.LoadFromSave(saved);
                Check.Equal(0, UnityEngine.Random.CallCount, "reload never rolls");
                Check.Equal(9, HermesHeadwearVisuals.LastKind("Apply").Choice, "reload keeps the frozen choice");
                Check.Equal(1, PatchDivine_HermesHeadwear.TrackedStateCount, "reloaded generation tracked");
                Check.NotNull(loaded.Troll, "loaded troll exists");
            });

            Case.Run("persistence.chanceChangeOnlyAffectsFutureConversions", () =>
            {
                UnityEngine.Random.SetSequence(0, 3);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 1);
                Check.Equal(3, HermesHeadwearVisuals.LastKind("Apply").Choice, "first conversion rolled a choice");

                ModConfig.HermesHeadwearChancePercent.Value = 0;
                Fixture.TickAfter(1.2f);
                Check.Equal(1, HermesHeadwearVisuals.ApplyCalls, "chance change must not re-decide or re-apply");
                IslandSaveData.ObjectData saved = NativeFlow.BuildSaveObject(fx.Persistent);
                AssertReceiptChoice(saved, 3, "existing receipt unchanged after the chance change");

                UnityEngine.Random.SetSequence(0, 40);
                TrollFixture second = TrollFixture.Create();
                NativeFlow.ConvertByHermes(second.Troll, 4, false, 1);
                Check.Equal(0, UnityEngine.Random.CallCount, "chance 0 short-circuits before any roll");
                IslandSaveData.ObjectData savedSecond = NativeFlow.BuildSaveObject(second.Persistent);
                AssertReceiptChoice(savedSecond, HermesHeadwearCodec.ChoiceNone,
                    "new conversions follow the new chance");
            });

            Case.Run("persistence.networkDataVariantNeverTouched", () =>
            {
                UnityEngine.Random.SetSequence(0, 11);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 1);

                IslandSaveData.ObjectData networkSave = NativeFlow.BuildSaveObject(fx.Persistent, useNetworkData: true);
                Check.Equal(1, networkSave.componentData2.Count, "network save payload present");
                IslandSaveData.ObjectData.ComponentData component = networkSave.componentData2[0];
                Check.Equal("IRPCData", component.type, "network variant keeps the IRPCData shape");
                Check.True(component.data.IndexOf(HermesHeadwearCodec.MetadataKey, StringComparison.Ordinal) < 0,
                    "network-data variant never receives the receipt property");
            });

            Case.Run("persistence.malformedPayloadLeftUntouched", () =>
            {
                UnityEngine.Random.SetSequence(0, 11);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 1);

                const string malformed = "{\"maskIndex\":1,";
                IslandSaveData.ObjectData objectData = new IslandSaveData.ObjectData(fx.Persistent);
                objectData.componentData2.Add(new IslandSaveData.ObjectData.ComponentData(
                    "FriendlyTroll", "FriendlyTrollData", malformed));

                PatchDivine_HermesHeadwear.HandleObjectDataBuilt(objectData, fx.Persistent, false);
                Check.Equal(malformed, objectData.componentData2[0].data,
                    "malformed component payload must stay byte-identical");
            });
        }

        private static void AssertReceiptChoice(IslandSaveData.ObjectData objectData, int expected, string message)
        {
            IslandSaveData.ObjectData.ComponentData component = NativeFlow.FindFriendlyComponent(objectData);
            Check.NotNull(component, "native component present");
            HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(component.data);
            Check.True(receipt.Recognized, "receipt recognized");
            Check.Equal(expected, receipt.Choice, message);
        }
    }
}
