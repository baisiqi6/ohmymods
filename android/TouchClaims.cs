using System.Collections.Generic;
namespace OhMyMods.AndroidProbe;
internal sealed class TouchClaims
{
 private readonly HashSet<int> owned = new();
 private readonly HashSet<int> ending = new();
 private int lastFrame=-1;
 public bool Claim(int frame,int id,bool begins,bool ends,bool hits)
 {
  if (frame!=lastFrame) { foreach(int key in ending) owned.Remove(key); ending.Clear(); lastFrame=frame; }
  if(begins) { if(hits) owned.Add(id); else owned.Remove(id); }
  bool result=owned.Contains(id);
  if(ends&&result) ending.Add(id);
  return result;
 }
 public void Reset(){owned.Clear();ending.Clear();lastFrame=-1;}
}
