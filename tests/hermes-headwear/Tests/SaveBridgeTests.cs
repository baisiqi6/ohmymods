using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using UnityEngine;

namespace HermesHeadwearTests
{
    /// <summary>
    /// 存档薄桥（Save prefix/finalizer scope + GetID 后缀关联 + Save 后缀注入）的行为回归：
    /// 全部走“原生 Save 调用过程”的模拟，而不是直接调 HandleObjectDataBuilt。
    /// </summary>
    internal static class SaveBridgeTests
    {
        internal static void Run()
        {
            Console.WriteLine("Save bridge (scoped capture + GetID correlation + postfix inject)");

            Case.Run("savebridge.realSaveFlowWritesReceiptAndSurvivesIdChange", () =>
            {
                UnityEngine.Random.SetSequence(0, 12);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 6, true, 5);

                IslandSaveData island = NativeFlow.RunNativeSave(new List<Persistent> { fx.Persistent });
                Check.Null(IslandSaveData.CurrentlySavingIsland,
                    "native finally cleared the static before the postfix ran");
                IslandSaveData.ObjectData record = island.objects[0];
                HermesHeadwearCodec.ReceiptInfo first =
                    HermesHeadwearCodec.ReadComponentMetadata(NativeFlow.FindFriendlyComponent(record).data);
                Check.True(first.Recognized, "the receipt was injected through the save bridge");
                Check.Equal(12, first.Choice, "the frozen choice is persisted");
                Check.Contains(NativeFlow.FindFriendlyComponent(record).data, "\"maskIndex\":5",
                    "native maskIndex bytes preserved by the bridge");
                Check.Contains(NativeFlow.FindFriendlyComponent(record).data, "\"trollHealth\":6",
                    "native health bytes preserved by the bridge");
                Check.True(!PatchDivine_HermesHeadwear.HasActiveSaveCapture,
                    "the finalizer released the scope");

                // 原生 uniqueID 变了（InstanceID 变化）：仍从 managed receipt 写同一个 Guid/choice。
                IslandSaveData.IdSuffixForTest = "-regen";
                IslandSaveData islandAgain = NativeFlow.RunNativeSave(new List<Persistent> { fx.Persistent });
                HermesHeadwearCodec.ReceiptInfo second = HermesHeadwearCodec.ReadComponentMetadata(
                    NativeFlow.FindFriendlyComponent(islandAgain.objects[0]).data);
                Check.True(islandAgain.objects[0].uniqueID != record.uniqueID,
                    "the native uniqueID really changed between the two saves");
                Check.Equal(first.Token, second.Token, "same guid token despite the native id change");
                Check.Equal(12, second.Choice, "same choice despite the native id change");
                Check.Equal(2, UnityEngine.Random.CallCount, "no re-roll across the two saves");
            });

            Case.Run("savebridge.nonFriendlyAndUnscopedGetIdAreSideEffectFree", () =>
            {
                // 非 Friendly 宿主：GetID 被调用也不该进 capture
                TrollFixture plain = TrollFixture.Create(withFriendly: false);
                IslandSaveData island = NativeFlow.RunNativeSave(new List<Persistent> { plain.Persistent });
                Check.Equal(1, island.objects.Count, "native record exists for the plain object");
                Check.Equal(0, island.objects[0].componentData2.Count,
                    "no FriendlyTrollData component for a non-friendly object");
                Check.True(!PatchDivine_HermesHeadwear.HasActiveSaveCapture, "no scope leak after the save");

                // 无 scope 的 GetID：零副作用（不建 scope、不产生关联）
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);
                int statesBefore = PatchDivine_HermesHeadwear.TrackedStateCount;
                string id = IslandSaveData.GetID(fx.Persistent);
                Check.NotNull(id, "GetID still returns the native id");
                Check.True(!PatchDivine_HermesHeadwear.HasActiveSaveCapture, "GetID never opens a scope");
                Check.Equal(0, PatchDivine_HermesHeadwear.CapturedOwnerCount, "no capture without a scope");
                Check.Equal(statesBefore, PatchDivine_HermesHeadwear.TrackedStateCount,
                    "GetID never changes tracked generations");
            });

            Case.Run("savebridge.missAndOpaqueAreWrittenWhileFeatureOff", () =>
            {
                // 未命中（choice=-1）在功能关闭时也必须保存
                UnityEngine.Random.SetSequence(99);
                TrollFixture miss = TrollFixture.Create();
                NativeFlow.ConvertByHermes(miss.Troll, 4, false, 2);
                ModConfig.HermesHeadwearEnabled.Value = false;

                IslandSaveData missIsland = NativeFlow.RunNativeSave(new List<Persistent> { miss.Persistent });
                HermesHeadwearCodec.ReceiptInfo missReceipt = HermesHeadwearCodec.ReadComponentMetadata(
                    NativeFlow.FindFriendlyComponent(missIsland.objects[0]).data);
                Check.True(missReceipt.Recognized, "a miss still writes a valid receipt while off");
                Check.Equal(HermesHeadwearCodec.ChoiceNone, missReceipt.Choice, "explicit -1 persisted while off");

                // 未知 schema：功能关闭时逐字保留
                string foreign = "{\"v\":9,\"future\":true}";
                string payload = "{\"maskIndex\":3,\"trollHealth\":4,\"toughTroll\":false,"
                    + "\"kemHermesHeadwear\":" + foreign + "}";
                TrollFixture opaque = TrollFixture.Create();
                IslandSaveData.ObjectData loaded = new IslandSaveData.ObjectData(opaque.Persistent);
                loaded.componentData2.Add(new IslandSaveData.ObjectData.ComponentData(
                    "FriendlyTroll", "FriendlyTrollData", payload));
                PatchDivine_HermesHeadwear.HandleTryCreateOrFind(loaded, opaque.Persistent);
                NativeFlow.ApplyNativeDataJson(opaque.Troll, payload); // 原生 3 字段照常恢复

                IslandSaveData opaqueIsland = NativeFlow.RunNativeSave(new List<Persistent> { opaque.Persistent });
                string opaqueData = NativeFlow.FindFriendlyComponent(opaqueIsland.objects[0]).data;
                Check.Contains(opaqueData, "\"kemHermesHeadwear\":" + foreign,
                    "opaque extension passes through the save bridge verbatim while off");
                Check.Contains(opaqueData, "\"maskIndex\":3", "opaque path keeps the native fields");
            });

            Case.Run("savebridge.nestedScopesDoNotCrossContaminate", () =>
            {
                UnityEngine.Random.SetSequence(0, 7);
                TrollFixture outer = TrollFixture.Create();
                NativeFlow.ConvertByHermes(outer.Troll, 4, false, 2);
                UnityEngine.Random.SetSequence(0, 25);
                TrollFixture inner = TrollFixture.Create();
                NativeFlow.ConvertByHermes(inner.Troll, 4, false, 2);

                // 外层 scope 开始并登记外层对象
                var outerScope = PatchDivine_HermesHeadwear.BeginSaveCapture();
                IslandSaveData outerIsland = new IslandSaveData { objects = new List<IslandSaveData.ObjectData>() };
                IslandSaveData._currentlySavingIsland = outerIsland;
                IslandSaveData.ObjectData outerRecord = NativeFlow.RegisterNativeRecord(outerIsland, outer.Persistent);
                Check.Equal(1, PatchDivine_HermesHeadwear.CapturedOwnerCount, "outer scope keeps its own capture");

                // 内层 Save 在期间发生：自己的 scope、自己的 island
                var innerScope = PatchDivine_HermesHeadwear.BeginSaveCapture();
                IslandSaveData innerIsland = new IslandSaveData { objects = new List<IslandSaveData.ObjectData>() };
                IslandSaveData._currentlySavingIsland = innerIsland;
                IslandSaveData.ObjectData innerRecord = NativeFlow.RegisterNativeRecord(innerIsland, inner.Persistent);
                PatchDivine_HermesHeadwear.ApplySaveCapture(innerScope);
                PatchDivine_HermesHeadwear.EndSaveCapture(null, innerScope);

                Check.Contains(NativeFlow.FindFriendlyComponent(innerRecord).data,
                    "\"choice\":25", "inner save injected its own receipt");
                Check.True(NativeFlow.FindFriendlyComponent(outerRecord).data
                        .IndexOf(HermesHeadwearCodec.MetadataKey, StringComparison.Ordinal) < 0,
                    "inner save never touches the outer island's record");
                Check.Equal(1, PatchDivine_HermesHeadwear.CapturedOwnerCount,
                    "the outer scope is restored with only its own capture");

                // 外层继续完成
                IslandSaveData._currentlySavingIsland = outerIsland;
                PatchDivine_HermesHeadwear.ApplySaveCapture(outerScope);
                PatchDivine_HermesHeadwear.EndSaveCapture(null, outerScope);
                Check.Contains(NativeFlow.FindFriendlyComponent(outerRecord).data,
                    "\"choice\":7", "outer save injected its own receipt after the inner scope ended");
                Check.True(NativeFlow.FindFriendlyComponent(innerRecord).data.IndexOf("\"choice\":7", StringComparison.Ordinal) < 0,
                    "outer save never touches the inner island's record");
                Check.True(!PatchDivine_HermesHeadwear.HasActiveSaveCapture, "both scopes released");
            });

            Case.Run("savebridge.nativeExceptionRestoresScopeAndPropagates", () =>
            {
                UnityEngine.Random.SetSequence(0, 31);
                TrollFixture fx = TrollFixture.Create();
                NativeFlow.ConvertByHermes(fx.Troll, 4, false, 2);

                IslandSaveData island = NativeFlow.RunNativeSaveThatThrows(
                    new List<Persistent> { fx.Persistent }, out Exception thrown);
                Check.NotNull(thrown, "native exception was simulated");
                Check.True(!PatchDivine_HermesHeadwear.HasActiveSaveCapture,
                    "finalizer restored the (empty) previous scope on the exception path");
                Check.True(NativeFlow.FindFriendlyComponent(island.objects[0]).data
                        .IndexOf(HermesHeadwearCodec.MetadataKey, StringComparison.Ordinal) < 0,
                    "an aborted save never injects");

                // 收据仍在：下一次成功的 save 照写
                IslandSaveData retry = NativeFlow.RunNativeSave(new List<Persistent> { fx.Persistent });
                HermesHeadwearCodec.ReceiptInfo receipt = HermesHeadwearCodec.ReadComponentMetadata(
                    NativeFlow.FindFriendlyComponent(retry.objects[0]).data);
                Check.True(receipt.Recognized && receipt.Choice == 31,
                    "the receipt survives the aborted save and is written on the next one");
                Check.Equal(2, UnityEngine.Random.CallCount, "the aborted save never re-rolled");
            });

            Case.Run("savebridge.saveWithoutTrackedObjectsIsNoop", () =>
            {
                TrollFixture plain = TrollFixture.Create(withFriendly: false);
                TrollFixture legacyTroll = TrollFixture.Create();
                NativeFlow.ApplyNativeDataJson(legacyTroll.Troll, "{\"maskIndex\":-1,\"trollHealth\":2,\"toughTroll\":false}");

                IslandSaveData island = NativeFlow.RunNativeSave(
                    new List<Persistent> { plain.Persistent, legacyTroll.Persistent });
                Check.Equal(2, island.objects.Count, "native records exist");
                for (int i = 0; i < island.objects.Count; i++)
                {
                    IslandSaveData.ObjectData.ComponentData component =
                        NativeFlow.FindFriendlyComponent(island.objects[i]);
                    if (component == null) continue;
                    Check.True(component.data.IndexOf(HermesHeadwearCodec.MetadataKey, StringComparison.Ordinal) < 0,
                        "receive-free objects (legacy/non-friendly) are never given an extension");
                }
                Check.True(!PatchDivine_HermesHeadwear.HasActiveSaveCapture, "scope released");
            });
        }
    }
}
