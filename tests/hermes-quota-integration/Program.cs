using KingdomEnhancedMod;
using UnityEngine;
using System.Text.Json;
class Program
{
    static int checks;
    static void Check(bool value,string message) { if(!value)throw new Exception(message);checks++; }
    static FriendlyTroll Convert()
    {
        var go=new GameObject("FriendlyTroll");
        var troll=go.AddComponent(new FriendlyTroll());
        troll._trollHealth=7;troll._toughTroll=true;troll._maskIndex=-1;
        Init(troll);return troll;
    }
    static void Init(FriendlyTroll troll)
    {
        PatchDivine_HermesHeadwear.EnterConversionContext();
        try { PatchDivine_HermesHeadwear.HandleNativeInit(troll); }
        finally { PatchDivine_HermesHeadwear.ExitConversionContext(); }
    }
    static void World()
    {
        PatchDivine_HermesHeadwear.ResetForProcessBoundary();
        NetworkBigBoss.HasWorldAuth=true;NetworkBigBoss.IsOnline=false;
        Managers.Inst=new Managers { world=new World { Pointer=(IntPtr)1234 } };
        NetworkPostbox.Instance=new NetworkPostbox();
        PatchDivine_HermesHeadwear.SampleOverride=(_,_)=>throw new Exception("Quota must not use RNG");
        ModConfig.ResetForTest();HermesHeadwearVisuals.ResetForTest();
    }
    static void Main()
    {
        string dir=Path.Combine(Path.GetTempPath(),"kem-quota-integration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path=Path.Combine(dir,"hermes-headwear-cycle.v2.json");
            string legacy=Path.Combine(dir,"hermes-headwear-cycle.v1.json");
            const string old="{\"v\":1,\"kind\":\"hermes-headwear-cycle\",\"nextChoice\":8}";
            File.WriteAllText(legacy,old);
            HermesHeadwearCycle.PathOverride=path;
            HermesHeadwearCycle.AuthorityOverride=()=>NetworkBigBoss.HasWorldAuth;
            World();
            var assigned=new List<int>();
            for(int i=1;i<=10;i++)
            {
                if(i==4)World(); // first 3 misses persisted; process boundary must retain credit90
                var troll=Convert();
                bool expected=i==4||i==7||i==10;
                Check(PatchDivine_HermesHeadwear.HasDisguiseHeadwear(troll)==expected,"quota position "+i);
                if(expected)assigned.Add(HermesHeadwearVisuals.Calls.Last(c=>c.Kind=="Apply"&&c.Troll==troll).Choice);
                string before=File.ReadAllText(path);Init(troll);
                Check(File.ReadAllText(path)==before,"repeat Init must not advance quota");
                Check(troll._maskIndex==-1&&troll._trollHealth==7&&troll._toughTroll,"native fields intact");
            }
            Check(assigned.SequenceEqual(new[]{8,9,10}),"legacy next8 migration and ordered visuals");
            Check(File.ReadAllText(legacy)==old,"legacy file unchanged");
            string saved=File.ReadAllText(path);
            ModConfig.HermesHeadwearEnabled.Value=false;Convert();
            Check(File.ReadAllText(path)==saved,"disabled quota unchanged");
            ModConfig.HermesHeadwearEnabled.Value=true;NetworkBigBoss.HasWorldAuth=false;Convert();
            Check(File.ReadAllText(path)==saved,"client quota unchanged");
            NetworkBigBoss.HasWorldAuth=true;ModConfig.HermesHeadwearChancePercent.Value=0;Convert();
            Check(File.ReadAllText(path)==saved,"zero quota unchanged");
            using var doc=JsonDocument.Parse(saved);Check(doc.RootElement.GetProperty("credit").GetInt32()==0,"10th ends credit0");
            Console.WriteLine("PASS "+checks+" real core+cycle integration checks");
        }
        finally {Directory.Delete(dir,true);}
    }
}
