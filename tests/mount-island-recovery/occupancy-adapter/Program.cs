using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;
namespace KingdomEnhancedMod {
class Program {
 static int failed;static bool old;
 static GameObject Node(string n,float? a=null,float? b=null){var g=new GameObject(n);if(a.HasValue)g.Add<SpriteRenderer>().bounds=new(a.Value,b.Value);return g;}
 static GameObject Mark(string n,float a,float b){var g=Node(n,a,b);g.Add<Persistent>().path="synthetic-building";return g;}
 static Collider2D Solid(GameObject g,float a,float b){var c=g.Add<Collider2D>();c.Value=new(a,b);return c;}
 static void Case(string label,GameObject root,Func<bool,List<MountIslandRecoveryInterval>,Level,bool> expect){var cuts=new List<MountIslandRecoveryInterval>();var l=new Level();string reason;bool ok=old?Old.Collect(l,root.transform,cuts,out reason):Actual.Collect(l,root.transform,cuts,out reason);bool pass=expect(ok,cuts,l);if(!pass)failed++;Console.WriteLine(JsonSerializer.Serialize(new{label,version=old?"old":"current",ok,pass,reason,cuts=cuts.Select(x=>new{min=x.Min,max=x.Max}),overlapReads=l.OverlapReads}));}
 static bool Plan(List<MountIslandRecoveryInterval> cuts,float min=-100,float max=100)=>MountIslandRecoveryPlacementPolicy.TryPlan(new[]{new MountIslandRecoveryPlacementRequest("fixture",1)},new[]{new MountIslandRecoveryInterval(min,max)},cuts,min,max,x=>x,(x,w)=>true,out _,out _);
 static bool Has(List<MountIslandRecoveryInterval> c,float a,float b)=>c.Any(x=>x.Min==a&&x.Max==b);
 static GameObject Root(params GameObject[] nodes){var r=Node("GameLayer");foreach(var n in nodes)r.transform.Add(n);return r;}
 static void Main(string[] args){old=args.Contains("--old");
 var group=Node("DecorativeGroup",-100,100);group.transform.Add(Mark("building",-2,2));Case("pure-decor-large-parent-marked-child",Root(group),(ok,c,l)=>ok&&c.Count==1&&Has(c,-2,2)&&Plan(c));
 group=Node("Container");group.transform.Add(Mark("left",-100,-10));group.transform.Add(Mark("right",10,100));Case("separate-children-preserve-middle-gap",Root(group),(ok,c,l)=>ok&&c.Count==2&&Plan(c,-5,5));
 var solid=Node("solid-leaf");Solid(solid,-3,3);Case("unmarked-solid-leaf",Root(solid),(ok,c,l)=>ok&&Has(c,-3,3)&&!Plan(c,-2,2));
 group=Node("direct-solid-plus-descendant",-100,100);Solid(group,-30,-20);group.transform.Add(Mark("child",2,4));Case("direct-collider-and-marked-descendant",Root(group),(ok,c,l)=>ok&&c.Count==2&&Has(c,-30,-20)&&Has(c,2,4));
 group=Node("multiple-direct");Solid(group,-30,-20);Solid(group,20,30);Case("direct-colliders-preserve-gap",Root(group),(ok,c,l)=>ok&&c.Count==2&&Plan(c,-5,5));
 foreach(var mode in new[]{"trigger","inactive","disabled"}){var n=Node(mode);var c=Solid(n,-100,100);if(mode=="trigger")c.isTrigger=true;if(mode=="inactive")n.activeInHierarchy=false;if(mode=="disabled")c.enabled=false;Case(mode,Root(n),(ok,c,l)=>ok&&c.Count==0);}
 group=Node("LevelBlock",-100,100);group.Add<LevelBlock>();group.transform.Add(Mark("child",-2,2));Case("LevelBlock-recurses",Root(group),(ok,c,l)=>ok&&c.Count==1&&Has(c,-2,2));
 group=Node("unmarked-ancestor");solid=Node("solid-descendant");Solid(solid,8,12);group.transform.Add(solid);Case("solid-descendant-recurses",Root(group),(ok,c,l)=>ok&&c.Count==1&&Has(c,8,12));
 var ground=Node("Ground");Solid(ground,-100,100);Case("support-ground-remains-conservative",Root(ground),(ok,c,l)=>ok&&Has(c,-100,100)&&!Plan(c));
 var payable=Node("payable",-2,2);payable.Add<Payable>();Case("Payable-bounds-and-overlap",Root(payable),(ok,c,l)=>ok&&c.Count==2&&Has(c,-2,2)&&Has(c,20,24)&&l.OverlapReads==1);
 foreach(var n in new[]{"Persistent","ForestItem","ScatteredObject"}){var m=Node(n,-2,2);if(n=="Persistent")m.Add<Persistent>();if(n=="ForestItem")m.Add<ForestItem>();if(n=="ScatteredObject")m.Add<ScatteredObject>();m.transform.Add(Mark("child",4,6));Case("marked-stop-"+n,Root(m),(ok,c,l)=>ok&&c.Count==1&&Has(c,-2,6));}
 foreach(var path in new[]{MountIslandRecoveryGeometry.EligibleTilePaths[0],MountIslandRecoveryGeometry.StructureExclusions[0].PersistentPath}){var n=Node(path,-100,100);n.Add<Persistent>().path=path;n.transform.Add(Mark("child",-2,2));Case("typed-path-skip-"+path,Root(n),(ok,c,l)=>ok&&c.Count==0);}
 foreach(var mode in new[]{"nan","infinity","inverted","throw-bounds","throw-components","null-components","null-slot","missing-owner"}){var n=Node(mode);var c=Solid(n,-1,1);if(mode=="nan")c.Value=new(float.NaN,1);if(mode=="infinity")c.Value=new(-1,float.PositiveInfinity);if(mode=="inverted")c.Value=new(2,1);if(mode=="throw-bounds")c.ThrowBounds=true;if(mode=="throw-components")n.ThrowDirect=true;if(mode=="null-components")n.NullDirect=true;if(mode=="null-slot")n.NullSlot=true;if(mode=="missing-owner")c.gameObject=null;Case("unknown-reject-"+mode,Root(n),(ok,c,l)=>!ok);}
 Console.WriteLine($"RESULT version={(old?"old":"current")} failed={failed}");Environment.ExitCode=failed==0?0:1;
 }
}
}
