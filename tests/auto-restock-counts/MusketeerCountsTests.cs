using KingdomEnhancedMod;
using UnityEngine;
internal static class MusketeerCountsTests
{
    internal static void Run()
    {
        Test.Run("musketeer_identity_late_bind_and_release_reclassifies_exact_actor", () =>
        {
            var e = new Env(); var c = e.MakeCharacter("Archer"); var a = c.GetComponent<Archer>();
            Test.Assert(e.R(),"ready"); Test.Eq(AutoRestockCounts.LiveCount(1),1,"ordinary archer");
            int scans=e.Enums; MusketeerIdentity.Units.Add(a); MusketeerIdentity.Notify(c);
            Test.Assert(e.R(),"late bind ready"); Test.Eq(AutoRestockCounts.LiveCount(1),0,"gunner excluded");
            Test.Eq(e.Enums,scans,"no roster rescan on bind");
            MusketeerIdentity.Units.Remove(a); MusketeerIdentity.Notify(c);
            Test.Assert(e.R(),"release ready"); Test.Eq(AutoRestockCounts.LiveCount(1),1,"ordinary restored");
            Test.Eq(e.Enums,scans,"no roster rescan on release");
        });
        Test.Run("musketeer_gun_excluded_from_bow_stock_with_exact_identity_event", () =>
        {
            var e=new Env(); var shop=e.MakeShop("Bow",1); e.SetPlaced(shop);
            var go=Fabric.NewGo(Env.Next(),1,e.Layer); var gun=new DroppableTool { Pointer=Env.Next(), tag="Bow" }; go.Add(gun);
            shop.SetItems(new Droppable[]{gun}); Test.Assert(e.R(),"ready");
            Test.Eq(AutoRestockCounts.StockCount(1),1,"plain bow");
            MusketeerIdentity.Guns.Add(gun); MusketeerIdentity.NotifyGun(gun); Test.Assert(e.R(),"identity change");
            Test.Eq(AutoRestockCounts.StockCount(1),0,"paid gun not bow coverage");
        });
    }
}
