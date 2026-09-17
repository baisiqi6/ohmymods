using KingdomEnhancedMod;
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var pay=new MusketeerShopPayment();
Check(!pay.Consume(1,true,4),"unarmed payment rejected");
pay.Arm(1);Check(!pay.Consume(2,true,4),"another payer rejected");
Check(!pay.Consume(1,false,4),"partial transaction rejected");
Check(!pay.Consume(1,true,3),"underpayment rejected");
Check(!pay.Consume(1,true,8),"hero price cannot enter firearm transaction");
Check(pay.Consume(1,true,4),"four paid coins accepted");
Check(!pay.Consume(1,true,4)&&pay.AlreadySettled(1),"duplicate completion cannot spawn another gun");
pay.Clear();Check(!pay.AlreadySettled(1)&&!pay.IsArmed(1),"cancel clears receipt");
Check(MusketeerShopRules.FirstFreeSlot(Array.Empty<int>())==0,"first rack position");
Check(MusketeerShopRules.FirstFreeSlot(new[]{0,1,2})==-1,"full rack");
for(int gap=0;gap<3;gap++)
{
    var remaining=Enumerable.Range(0,3).Where(i=>i!=gap).ToArray();
    Check(MusketeerShopRules.FirstFreeSlot(remaining)==gap,"replenish actual missing slot "+gap);
}
Check(MusketeerShopRules.FirstFreeSlot(new[]{3})==-1,"unreadable slot prevents charge");
Check(MusketeerShopRules.FirstFreeSlot(new[]{1,1})==-1,"duplicate slot rejects purchase");
var probe=new HeroShopRetention.Probe{FeatureEnabled=true,Offline=true,HasShop=true,SameScene=true,SameWorld=true,SceneAlive=true,HeaderOk=true,Menu=true};
Check(HeroShopRetention.Decide(probe)==HeroShopRetention.Outcome.Keep&&!HeroShopRetention.CanServe(probe),"pause retains shop but prevents payment");
probe.Menu=false;probe.Playing=true;Check(HeroShopRetention.CanServe(probe),"playing permits eligible native transaction");
probe.SameWorld=false;Check(!HeroShopRetention.CanServe(probe),"world change blocks payment");
Console.WriteLine($"PASS {checks} shop payment/rack/retention assertions");
