using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
Archer Actor(bool hero=true)=>new GameObject().Add(new Archer{Purchased=hero});
GuardSlot Slot(Archer a=null){var s=new GameObject().Add(new GuardSlot{archer=a});if(a!=null){a._guardSlot=s;a.inGuardSlot=true;}return s;}
bool Prefix(string nested,Archer actor,object job)=> (bool)typeof(HeroArcherTowerPolicy).GetNestedType(nested,BindingFlags.NonPublic)!.GetMethod("Before",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new[]{actor,job})!;
bool Available(Archer actor,GameObject job,bool original){object[] args={actor,job,original};typeof(HeroArcherTowerPolicy).GetNestedType("AvailabilityPatch",BindingFlags.NonPublic)!.GetMethod("After",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);return(bool)args[2];}
void Reset(){HeroArcherRuntime.Enabled=false;HeroArcherTowerPolicy.Observe(null);HeroArcherRuntime.Enabled=true;Time.unscaledTime+=5;}
Reset();var hero=Actor();var ordinary=Actor(false);var slot=Slot();var otherJob=new GameObject();
Check(!Available(hero,slot.gameObject,true),"hero refused at native availability gate");
Check(!Available(ordinary,slot.gameObject,false),"native refusal never promoted to true");
Check(Available(ordinary,slot.gameObject,true),"ordinary tower candidate unchanged");
Check(Available(hero,otherJob,true),"non-tower job unchanged");
Check(!Prefix("AssignmentPatch",hero,slot.gameObject),"inlined AssignJob tower path blocked");
Check(Prefix("AssignmentPatch",ordinary,slot.gameObject),"ordinary native assignment proceeds");
Check(Prefix("AssignmentPatch",hero,otherJob),"hero non-tower assignment proceeds");
Check(!Prefix("SetSlotPatch",hero,slot),"direct SetGuardSlot blocked");
Check(!Prefix("EnterSlotPatch",hero,slot),"direct EnterGuardSlot blocked");
Check(Prefix("SetSlotPatch",ordinary,slot)&&Prefix("EnterSlotPatch",ordinary,slot),"ordinary direct tower paths unchanged");
var knightJob=new GameObject();knightJob.Add(new Knight());
Check(Available(hero,knightJob,true)&&Prefix("AssignmentPatch",hero,knightJob),"knight following unchanged");
knightJob.Add(new GuardSlot());Check(Prefix("AssignmentPatch",hero,knightJob),"native knight precedence preserved for mixed components");
Check(Prefix("AssignmentPatch",hero,null)&&Prefix("SetSlotPatch",hero,null),"null input belongs to native caller");
var unknown=new GameObject{ThrowOnRead=true};Check(Prefix("AssignmentPatch",hero,unknown),"unreadable job fails open");
hero.ThrowOnPurchase=true;Check(Prefix("AssignmentPatch",hero,slot.gameObject),"unknown ownership fails open");hero.ThrowOnPurchase=false;
Check(Prefix("AssignmentPatch",null,slot.gameObject),"null actor fails open");
HeroArcherRuntime.Enabled=false;
Check(Available(hero,slot.gameObject,true)&&Prefix("AssignmentPatch",hero,slot.gameObject)&&Prefix("SetSlotPatch",hero,slot)&&Prefix("EnterSlotPatch",hero,slot),"off restores all native tower gates");
var oldSlot=Slot(hero);HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==0&&hero._guardSlot==oldSlot,"off does not evict purchased unit");
HeroArcherRuntime.Enabled=true;HeroArcherTowerPolicy.Observe(hero);
Check(hero.ExitCount==1&&hero._guardSlot==null&&!hero.inGuardSlot&&oldSlot.archer==null,"reenable exits exact prior slot through native cleanup");
HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==1,"no repeated exit when already clear");
var normalSlot=Slot(ordinary);HeroArcherTowerPolicy.Observe(ordinary);Check(ordinary.ExitCount==0&&normalSlot.archer==ordinary,"normal occupied tower untouched");
Reset();hero=Actor();oldSlot=Slot(hero);int nested=0;
hero.NativeExit=()=>{nested++;oldSlot.archer=null;HeroArcherTowerPolicy.Observe(hero);Check(!Available(hero,oldSlot.gameObject,true)&&!Prefix("AssignmentPatch",hero,oldSlot.gameObject),"synchronous distribution cannot reassign hero");Check(Available(ordinary,oldSlot.gameObject,true),"synchronous ordinary refill permitted");oldSlot.archer=ordinary;hero._guardSlot=null;hero.inGuardSlot=false;};
HeroArcherTowerPolicy.Observe(hero);Check(nested==1&&hero.ExitCount==1&&oldSlot.archer==ordinary,"reentry safe while native normal refill is preserved");
Reset();hero=Actor();oldSlot=Slot();hero._guardSlot=oldSlot;oldSlot.archer=ordinary;
HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==0&&oldSlot.archer==ordinary,"stale hero link cannot evict other occupant");
Reset();hero=Actor();hero.inGuardSlot=true;HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==1&&!hero.inGuardSlot,"orphan local guard flag repaired only by native exit");
Reset();hero=Actor();oldSlot=Slot(hero);hero.NativeExit=()=>throw new Exception("native failure");
for(int i=0;i<20;i++)HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==1,"failure is throttled instead of repeated each frame");
for(int i=0;i<10;i++){Time.unscaledTime+=2;HeroArcherTowerPolicy.Observe(hero);}Check(hero.ExitCount==3,"three-attempt bound on same native assignment");
HeroArcherRuntime.Enabled=false;HeroArcherTowerPolicy.Observe(hero);HeroArcherRuntime.Enabled=true;hero.NativeExit=null;HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==4&&hero._guardSlot==null,"toggle permits safe retry after native failure");
Reset();for(int i=0;i<9;i++){var stale=Actor();Slot(stale);stale.NativeExit=()=>throw new Exception();HeroArcherTowerPolicy.Observe(stale);stale.Purchased=false;}
hero=Actor();Slot(hero);HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==1&&hero._guardSlot==null,"bounded stale release registry never blocks new living hero");
Reset();hero=Actor();hero.gameObject.activeInHierarchy=false;Slot(hero);HeroArcherTowerPolicy.Observe(hero);Check(hero.ExitCount==0,"inactive or off-world ownership does not mutate actor");
Console.WriteLine($"PASS {passed} assertions (real tower policy and hook bodies)");
