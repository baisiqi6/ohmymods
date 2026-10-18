using System;namespace SavedBoundsTests {
struct IntRange {public int min,max;}
class IslandSaveData {public int Min=-552,Max=519,Reads;public bool Throw;public IntRange LevelEdges {get{Reads++;if(Throw)throw new Exception();return new(){min=Min,max=Max};}}}
class Level {public int Reads;public bool Throw;public IntRange _levelEdges {get{Reads++;if(Throw)throw new Exception();return new();}}}
class Transform {} class World {}
static partial class Actual {internal sealed class Capture {internal IslandSaveData Island;internal int SavedLevelMin,SavedLevelMax;} private sealed class PlacementModel {internal float LevelMin,LevelMax;}}
class Program {static int count,fail;static void Check(bool ok,string name){count++;if(!ok){fail++;Console.WriteLine("FAIL "+name);}}
static int Main(){
var level=new Level{Throw=true};Check(Actual.Model(level,-552,519,out _),"saved valid/cache zero before Tile");Check(level.Reads==0,"model never reads Level cache");
foreach(var pair in new[]{(0,0),(5,-5),(16777216,16777217),(int.MaxValue-1,int.MaxValue)}){var i=new IslandSaveData{Min=pair.Item1,Max=pair.Item2};Check(!Actual.Read(i,out _,out _,out _),"invalid saved "+pair);Check(i.Reads==1,"one local snapshot "+pair);}
var good=new IslandSaveData();Check(Actual.Read(good,out int min,out int max,out _)&&min==-552&&max==519&&good.Reads==1,"valid saved exact single read");
var capture=new Actual.Capture{Island=good,SavedLevelMin=min,SavedLevelMax=max};Check(Actual.Match(capture,out _),"captured equals live");good.Min=-551;Check(!Actual.Match(capture,out _),"live min drift");Check(capture.SavedLevelMin==-552,"capture primitive unchanged");good.Min=-552;good.Max=520;Check(!Actual.Match(capture,out _),"live max drift");good.Max=519;good.Throw=true;Check(!Actual.Match(capture,out _),"live read exception");Check(!Actual.Read(null,out _,out _,out _),"missing island");Check(!Actual.Match(null,out _),"missing capture");
Check(!Actual.Match(new Actual.Capture{Island=new IslandSaveData()},out _),"late valid input cannot grant absent freeze");
var precision=new IslandSaveData{Min=16777217,Max=16777220};Check(!Actual.Match(new Actual.Capture{Island=precision,SavedLevelMin=16777216,SavedLevelMax=16777220},out _),"raw integer drift despite same float");
Console.WriteLine($"{count-fail}/{count} saved bounds SOURCE prefix/helpers checks; native values stubbed");return fail==0?0:1;
}}
}
