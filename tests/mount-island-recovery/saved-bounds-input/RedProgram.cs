using System;namespace SavedBoundsTests {
struct IntRange {public int min,max;} class IslandSaveData{public IntRange LevelEdges=>new(){min=-552,max=519};}class Level{public IntRange _levelEdges;}class Transform{}class World{}
static partial class Actual {internal sealed class Capture {internal IslandSaveData Island;internal int SavedLevelMin,SavedLevelMax;}private sealed class PlacementModel{internal float LevelMin,LevelMax;}}
class Program{static int Main(){bool ok=Actual.Model(new Level(),-552,519,out var why);Console.WriteLine((ok?"PASS":"FAIL")+" saved-valid/cache-zero-before-downstream: "+why);return ok?0:1;}}
}
