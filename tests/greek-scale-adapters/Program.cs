using System.Collections;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;
using Scope=KingdomEnhancedMod.GreekScaleScope;
using BagPatch=KingdomEnhancedMod.PatchEconomy_CurrencyBag;

static class Program
{
 static int passed,failed;
 static void Eq(float expected,float actual,string why="scale"){if(MathF.Abs(expected-actual)>.00001f)throw new Exception($"{why}: expected {expected}, got {actual}");}
 static void True(bool value,string why){if(!value)throw new Exception(why);}
 static void Vector(Vector3 expected,Vector3 actual){for(int a=0;a<3;a++)Eq(expected[a],actual[a],"axis "+a);}
 static void World(int index){BiomeHolder.Inst=new(){BiomeIndex=index};Scope.Tick();}
 static void Test(string name,Action body)
 {
  ((IDictionary)typeof(Scope).GetField("Requests",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Clear();
  ModConfig.Enabled=new();BiomeHolder.Inst=new();Managers.Inst=new();BiomeData.Swap=null;Time.frameCount+=500;Scope.Tick();
  try{body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}
 }
 static CurrencyBag Bag(Vector3? nativeRoot=null,Vector3? nativeContainer=null)
 {
  var b=new GameObject().AddComponent<CurrencyBag>();b.transform.localScale=nativeRoot??Vector3.one;
  b._container=new GameObject().transform;b._container.localScale=nativeContainer??Vector3.one;return b;
 }
 static BagCurrency Coin(CurrencyBag bag,Vector3 incoming,Vector3? nativePrefab=null)
 {
  var prefab=new GameObject().AddComponent<BagCurrency>();prefab.transform.localScale=nativePrefab??Vector3.one;
  Managers.Inst.currency.Data[CurrencyType.Coins]=new(){BagPrefab=prefab};
  var coin=new GameObject().AddComponent<BagCurrency>();coin.bag=bag;coin.transform.localScale=incoming;return coin;
 }
 static void ScaleBag(CurrencyBag bag)=>BagPatch.SetCurrencyBag_Postfix(bag);
 static bool Reset(BagCurrency coin,int nth=1){bool stack=false;BagPatch.ResetVisuals_Prefix(coin,false,nth,ref stack);return stack;}
 static void Main()
 {
  Test("fresh inherited coin 2 restores native1",()=>{var b=Bag();ScaleBag(b);var c=Coin(b,new(2,2,1));Reset(c);Vector(Vector3.one,c.transform.localScale);World(1);Vector(Vector3.one,c.transform.localScale);Vector(Vector3.one,b.transform.localScale);Vector(Vector3.one,b._container.localScale);});
  Test("repeat Reset and reuse never divide coin to .5",()=>{var b=Bag();ScaleBag(b);var c=Coin(b,new(2,2,1));for(int i=0;i<5;i++)Reset(c);World(1);Vector(Vector3.one,c.transform.localScale);World(3);for(int i=0;i<5;i++)Reset(c);World(1);Vector(Vector3.one,c.transform.localScale);});
  Test("nonunit swapped prefab and native container are respected",()=>{var b=Bag(nativeContainer:new(-2,1.5f,2));ScaleBag(b);var c=Coin(b,new(-1.6f,2.4f,.6f));var swap=new GameObject().AddComponent<BagCurrency>();swap.transform.localScale=new(-.8f,1.2f,.6f);BiomeData.Swap=swap;Reset(c);Vector(Vector3.one,c.transform.localScale);World(1);Vector(new(.4f,.8f,.3f),c.transform.localScale);});
  Test("foreign-first arbitrary coin2 has no transform write and survives roundtrip",()=>{World(1);var b=Bag();ScaleBag(b);var c=Coin(b,new(2,2,2));int writes=c.transform.Writes;Reset(c);Eq(writes,c.transform.Writes,"foreign writecount");World(3);Vector(Vector3.one,c.transform.localScale);World(1);Vector(new(2,2,2),c.transform.localScale);});
  Test("Greek runtime1.7 does not match inherited scale and restores1.7",()=>{var b=Bag();ScaleBag(b);var c=Coin(b,new(1.7f,1.7f,1));Reset(c);World(1);Vector(new(1.7f,1.7f,1),c.transform.localScale);});
  Test("unknown index records indirect parent writes for foreign cleanup",()=>{var b=Bag();ScaleBag(b);World(-1);var c=Coin(b,new(2,2,1));int writes=c.transform.Writes;Reset(c);Eq(writes,c.transform.Writes,"unknown must not write");World(1);Vector(Vector3.one,c.transform.localScale);Vector(Vector3.one,b._container.localScale);});
  Test("missing currency source changes no coin and stacklimit remains600",()=>{var b=Bag();ScaleBag(b);var c=Coin(b,new(2,2,1));Managers.Inst.currency.Missing=true;int writes=c.transform.Writes;True(Reset(c,599),"stack599");True(!Reset(c,600),"no stack600");Eq(writes,c.transform.Writes,"source unavailable failclosed");});
  Test("null biome source preserves existing ownership until known world",()=>{var b=Bag();ScaleBag(b);var c=Coin(b,new(2,2,1));Reset(c);BiomeHolder.Inst=null;Scope.Tick();int writes=c.transform.Writes;True(Reset(c,599),"stack remains");Eq(writes,c.transform.Writes,"no source no write");World(1);Vector(Vector3.one,c.transform.localScale);});
  Test("two bag types retain independent signed nonuniform native vectors",()=>{var a=Bag(new(-.8f,1.2f,1));var b=Bag(new(1.4f,-.9f,.7f));ScaleBag(a);ScaleBag(b);ScaleBag(a);Vector(new(-1.6f,2.4f,1),a.transform.localScale);Vector(new(2.8f,-1.8f,.7f),b.transform.localScale);World(1);Vector(new(-.8f,1.2f,1),a.transform.localScale);Vector(new(1.4f,-.9f,.7f),b.transform.localScale);});
  Test("StartShow notification does not reapply legacy position offset",()=>{var b=Bag();b.transform.position=new(4,5,6);BagPatch.StartShow_Postfix(b);Vector(new(4,5,6),b.transform.position);});
  Console.WriteLine($"RESULT {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
 }
}
