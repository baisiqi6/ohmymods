using System;
using System.Collections.Generic;
using System.IO;
using KingdomEnhancedMod;

namespace KnightIdentityRuntimeTests
{
    internal static class LoadSeedWiringTests
    {
        internal static void Run()
        {
            Case.Run("real_load_bridge_seeds_22_without_native_save_then_restores_all", () =>
            {
                using (var f = new Fixture())
                {
                    var island = new IslandSaveData { land = 9,
                        objects = new Il2CppSystem.Collections.Generic.List<IslandSaveData.ObjectData>() };
                    for (int i = 0; i < 22; i++) island.objects.Add(NativeSim.NewKnight(31000+i).ToRecord());
                    var untouched = NativeSim.CloneIsland(island);
                    string nativePath = Path.Combine(f.Temp.Path, "original-native-island.json");
                    File.WriteAllText(nativePath, untouched.Json());
                    byte[] original = File.ReadAllBytes(nativePath);
                    DateTime originalTime = File.GetLastWriteTimeUtc(nativePath);
                    KnightIdentityRuntime.ResetForTests();
                    var units = new List<KnightUnit>();
                    NativeSim.RunLoad(island, (i,id) => { var u=NativeSim.NewKnight(32000+i);units.Add(u);return u; });
                    Check.False(f.SidecarExists, "no incomplete snapshot during load");
                    var receipts = new List<KnightIdentityReceipt>();
                    for (int i=0;i<21;i++)
                    {
                        Check.True(KnightIdentityRuntime.TryResolve(units[i].Knight,i%5,0,Styles.All,out _),"first21 resolve");
                    }
                    KnightIdentityLoadSeed.Flush();
                    Check.False(f.SidecarExists, "late 22nd receipt cannot write partial table");
                    Check.True(KnightIdentityRuntime.TryResolve(units[21].Knight,1,0,Styles.All,out _),"last resolve");
                    // Natural native load clears objects; the seed must keep the original source fingerprint.
                    island.objects = null;
                    KnightIdentityLoadSeed.Flush();
                    Check.True(f.SidecarExists, "first styles persisted without any native Save");
                    foreach(var unit in units) { Check.True(KnightIdentityRuntime.TryGetReceipt(unit.Knight,out var rec),"receipt available");receipts.Add(rec); }
                    KnightIdentityRuntime.ResetForTests();
                    NativeSim.ResetWorld(0x7777);
                    var second = new List<KnightUnit>();
                    NativeSim.RunLoad(NativeSim.CloneIsland(untouched),(i,id)=> {var u=NativeSim.NewKnight(33000+i);second.Add(u);return u;});
                    for(int i=0;i<22;i++)
                    {
                        Check.True(KnightIdentityRuntime.TryGetReceipt(second[i].Knight,out var rec),"loaded receipt "+i);
                        Check.Equal(receipts[i].Id,rec.Id,"GUID stable "+i);
                        Check.Equal(receipts[i].Style,rec.Style,"style stable "+i);
                    }
                    Check.Equal(Convert.ToBase64String(original),Convert.ToBase64String(File.ReadAllBytes(nativePath)),"native bytes unchanged");
                    Check.Equal(originalTime,File.GetLastWriteTimeUtc(nativePath),"native timestamp unchanged");
                }
            });
        }
    }
}
