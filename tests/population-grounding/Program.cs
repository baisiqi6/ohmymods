using KingdomEnhancedMod;
using UnityEngine;
int checks=0;
void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
var layer=new GameObject().transform;var world=new World{gameLayer=layer};Managers.Inst=new(){world=world};
var log=KingdomEnhancedPlugin.Instance.LogSource;
World.GroundCollider=new(){gameObject=new GameObject{layer=0,name="Ground"},bounds=new(){max=new(100,.875f,0)}};
Beggar Actor(int number=1)
{
    var root=new GameObject{name="Beggar P"+number,layer=18};root.transform.parent=layer;root.transform.position=new(3,1,0);
    root.Body=new(){gameObject=root,velocity=new(){y=-3}};
    root.Colliders=new Collider2D[]{new(){gameObject=root,isTrigger=true},new(){gameObject=new(){name="Physical Collider",layer=17}}};
    return new(){gameObject=root,parentHeaderRef=new(){NetID=number}};
}
PopulationGrounding.Begin(world,layer,1);var a=Actor();
PopulationGrounding.Observe(a,null,1,true);Check(log.Messages.Count==1,"first admission");
Check(log.Messages[0].Contains("first-observed")&&!log.Messages[0].Contains("source=spawn"),"no false birth attribution");
Check(log.Messages[0].Contains("ignoreGround=False"),"audited physical terrain pair visible");
int reads=a.gameObject.DetailReads;
for(int i=0;i<1000;i++)PopulationGrounding.Observe(a,null,1,false);
Check(a.gameObject.DetailReads==reads,"steady reconcile has no body/collider enumeration");
a.transform.position=new(3,-5,0);PopulationGrounding.Observe(a,null,1,false);
Check(log.Messages.Count==2&&log.Messages[1].Contains("below-layer"),"one falling event");
for(int i=0;i<1000;i++)PopulationGrounding.Observe(a,null,1,false);
Check(log.Messages.Count==2,"same life does not spam falling");
Check(a.transform.position.y==-5&&a.gameObject.Body.velocity.y==-3,"diagnostic never corrects position or velocity");
PopulationGrounding.Observe(a,null,2,false);Check(log.Messages.Count==3,"new incarnation can report");
PopulationGrounding.Reset();log.Messages.Clear();PopulationGrounding.Begin(world,layer,2);
for(int i=0;i<100;i++){var b=Actor(i);PopulationGrounding.Observe(b,null,1,true);PopulationGrounding.Observe(b,null,1,false,true);b.transform.position=new(1,-9,0);PopulationGrounding.Observe(b,null,1,false);}
Check(log.Messages.Count==36,"whole-world sample caps twelve per event family");
PopulationGrounding.Reset();log.Messages.Clear();PopulationGrounding.Begin(world,layer,3);
foreach(string gate in new[]{"off","auth","pause","foreign","inactive","layer"})
{
 var b=Actor();ModConfig.Enabled.Value=true;NetworkBigBoss.HasWorldAuth=true;Time.timeScale=1;Managers.Inst.world=world;
 if(gate=="off")ModConfig.Enabled.Value=false;if(gate=="auth")NetworkBigBoss.HasWorldAuth=false;
 if(gate=="pause")Time.timeScale=0;if(gate=="foreign")Managers.Inst.world=new World{gameLayer=layer};
 if(gate=="inactive")b.gameObject.activeInHierarchy=false;if(gate=="layer")b.transform.parent=null;
 PopulationGrounding.Observe(b,null,1,true);Check(b.gameObject.DetailReads==0,gate+" does not inspect native details");
}
ModConfig.Enabled.Value=true;NetworkBigBoss.HasWorldAuth=true;Time.timeScale=1;Managers.Inst.world=world;
var bad=Actor();bad.gameObject.ThrowDetails=true;
PopulationGrounding.Observe(bad,null,1,true);PopulationGrounding.Observe(bad,null,2,true);
Check(log.Messages.Count==1,"read exceptions isolated and warning bounded");
log.Throw=true;PopulationGrounding.Observe(Actor(),null,1,true);log.Throw=false;
Check(true,"logger exception cannot break governor");
Console.WriteLine($"PASS {checks} grounding diagnostics assertions; physics and positions unchanged");
