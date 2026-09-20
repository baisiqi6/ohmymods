using KingdomEnhancedMod;
int checks=0;
void Check(bool value,string message) { if(!value)throw new Exception(message);checks++; }
Check(MusketeerShopRules.AtlasWidth==704&&MusketeerShopRules.FrameWidth==176&&MusketeerShopRules.FrameHeight==80,"atlas dimensions");
Check(Math.Abs(MusketeerShopRules.PivotX*176-64)<.0001&&MusketeerShopRules.PivotY*80==2,"preserve original shop pixel pivot");
Check(MusketeerShopRules.LayoutCenterX-MusketeerShopRules.HalfWidth==-2&&MusketeerShopRules.LayoutCenterX+MusketeerShopRules.HalfWidth==3.5f,"entire 176px footprint around old shop root");
for(int mask=0;mask<8;mask++)
{
 var slots=Enumerable.Range(0,3).Where(x=>(mask&(1<<x))!=0).ToArray();
 int expected=Enumerable.Range(0,3).FirstOrDefault(x=>(mask&(1<<x))==0,-1);
 Check(MusketeerShopRules.FirstFreeSlot(slots)==expected,"all zero-to-three stock combinations "+mask);
}
foreach(var bad in new[]{new[]{-1},new[]{3},new[]{0,0},new[]{2,1,2},new[]{int.MaxValue}})
 Check(MusketeerShopRules.FirstFreeSlot(bad)==-1,"invalid or duplicate metadata refuses sale");
var layout=new MusketeerRackLayout();int moves=0;var scope=new object();
MusketeerRackLayout.Item Item(int slot)
{
 return new(){Slot=slot,Unclaimed=true,Identity=new(new object(),scope,1,123+slot,456+slot,7,8),
  MoveAndVerify=(x,y,z)=>{moves++;Check(x==2.8125f&&y==.25f*(slot+1)&&z==-.002f,"true tool coordinates for slot "+slot);return true;}};
}
var full=new[]{Item(0),Item(1),Item(2)};
Check(layout.Reconcile(true,full)&&moves==3,"full rack reanchors despite no purchasable empty slot");
bool stable=true;for(int i=0;i<100;i++)stable &= layout.Reconcile(true,full);
Check(stable,"stable full rack ready across repeated maintenance");
Check(moves==3,"each proven life reanchors only once, external displacement not pulled back");
Check(!layout.Reconcile(false,full)&&moves==3,"pause/save/off/foreign/unknown context gate cannot move");
Check(layout.Reconcile(true,full)&&moves==3,"resume same shop retains receipts");
full[0].Unclaimed=false;Check(layout.Reconcile(true,full)&&moves==3&&layout.IsPlaced(full[0]),"claimed gun keeps receipt, layout stays complete during claim window");
full[0].Unclaimed=true;
foreach(string change in new[]{"life","career","state","pointer","instance","shop","layer"})
{
 var previous=full[0].Identity;
 full[0].Identity=change switch {
  "life"=>previous with{Life=previous.Life+1},"career"=>previous with{Career=new object()},
  "state"=>previous with{State=new object()},"pointer"=>previous with{Pointer=previous.Pointer+1},
  "instance"=>previous with{Instance=previous.Instance+1},"shop"=>previous with{Shop=previous.Shop+1},
  _=>previous with{Layer=previous.Layer+1}};
 int before=moves;Check(layout.Reconcile(true,full)&&moves==before+1,"new exact identity/scene requires one new placement "+change);
}
layout.Reset();moves=0;
var claimed=Item(1);claimed.Unclaimed=false;
Check(layout.Reconcile(true,new[]{claimed})&&moves==0,"claimed old stock skipped, never moved, layout stays complete");
claimed.Unclaimed=true;Check(layout.Reconcile(true,new[]{claimed})&&moves==1,"released claim can safely retry once");
Check(layout.Reconcile(true,Array.Empty<MusketeerRackLayout.Item>()),"empty snapshot retires old slot receipt");
Check(layout.Reconcile(true,new[]{claimed})&&moves==2,"removed stock cannot carry slot receipt into reused slot");
layout.Reset();moves=0;
var duplicate=new[]{Item(0),Item(0)};Check(!layout.Reconcile(true,duplicate)&&moves==0,"duplicate preflight prevents all movement");
var fallen=Item(0);fallen.Slot=-1;Check(!layout.Reconcile(true,new[]{fallen})&&moves==0,"dropped no-slot gun cannot be anchored");
var fail=Item(2);int attempts=0;fail.MoveAndVerify=(_,_,_)=>{attempts++;return false;};
Check(!layout.Reconcile(true,new[]{fail})&&!layout.IsPlaced(fail),"failed native write/readback never marked done");
fail.MoveAndVerify=(_,_,_)=>throw new Exception("native");Check(!layout.Reconcile(true,new[]{fail})&&!layout.IsPlaced(fail),"exception preserves responsibility");
fail.MoveAndVerify=(_,_,_)=>{attempts++;return true;};Check(layout.Reconcile(true,new[]{fail})&&layout.IsPlaced(fail),"successful retry records receipt");
Check(layout.Reconcile(true,new[]{fail})&&attempts==2,"no second successful move");
// This is a geometric compatibility bound, not a claim of a live-game pickup test.
float shopY=.875f,peasantTop=1.5f,toolRadius=.25f;
Check(peasantTop-(shopY+MusketeerShopRules.SlotY(2)-toolRadius)==.125f,"highest rack trigger overlaps serialized ordinary peasant by .125");
layout.Reset();moves=0;
// Mirror of MusketeerShop.RackCount's per-item predicate over ReadRackItems output: a claimed
// gun physically occupies its slot; only an unclaimed gun without a placed receipt fails closed.
int RackCount(IReadOnlyList<MusketeerRackLayout.Item> items)
{
 foreach(var item in items)
  if(item.Unclaimed&&!layout.IsPlaced(item))return MusketeerShopRules.RackCapacity;
 return items.Count;
}
var rack=new List<MusketeerRackLayout.Item>();
var gunA=Item(0);gunA.Unclaimed=false;rack.Add(gunA);
Check(RackCount(rack)==1,"claimed uncollected gun occupies one slot instead of locking the shop");
Check(layout.Reconcile(true,rack)&&moves==0,"claim window keeps layout complete even before first anchor");
var gunB=Item(1);rack.Add(gunB);
Check(layout.Reconcile(true,rack)&&moves==1&&RackCount(rack)==2,"second gun anchors and counts while first stays claimed");
gunA.Unclaimed=true;
Check(RackCount(rack)==MusketeerShopRules.RackCapacity,"released claim without receipt still fails the count closed");
Check(layout.Reconcile(true,rack)&&moves==2&&RackCount(rack)==2,"recovery: anchor after claim release restores counting");
gunA.Unclaimed=gunB.Unclaimed=false;var gunC=Item(2);gunC.Unclaimed=false;rack.Add(gunC);
Check(layout.Reconcile(true,rack)&&moves==2&&RackCount(rack)==3,"three claimed guns fill capacity and lock the shop");
Console.WriteLine($"PASS {checks} side-rack slot/layout/receipt assertions; live pickup remains untested");
