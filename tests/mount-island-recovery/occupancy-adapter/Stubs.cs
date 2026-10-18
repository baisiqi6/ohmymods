// Unity/native boundary only. Occupancy and placement algorithms are extracted production.
using System;using System.Collections.Generic;using System.Linq;
namespace KingdomEnhancedMod {
public class Component {public GameObject gameObject;}
public class Persistent:Component {public string path;}
public class Payable:Component {}
public class ForestItem:Component {}
public class ScatteredObject:Component {}
public class LevelBlock:Component {}
public struct Vector3 {public float x,y,z;public Vector3(float a){x=a;y=z=0;}}
public struct Bounds {public Vector3 min,max;public Bounds(float a,float b){min=new(a);max=new(b);}}
public struct Rect {public float xMin,xMax;}
public class SpriteRenderer:Component {public bool enabled=true;public Bounds bounds;}
public class Collider2D:Component {public bool enabled=true,isTrigger;public bool ThrowBounds;public Bounds Value;public Bounds bounds {get {if(ThrowBounds)throw new InvalidOperationException("native bounds unavailable");return Value;}}}
public class Level {public int OverlapReads;public Rect GetOverlapRegion(GameObject p){OverlapReads++;return new(){xMin=20,xMax=24};}}
public class GameObject {
 public string name;public bool activeInHierarchy=true,ThrowDirect,NullDirect,NullSlot;public Transform transform;public List<Component> cs=new();
 public GameObject(string n){name=n;transform=new(this);}
 public T Add<T>()where T:Component,new(){var x=new T{gameObject=this};cs.Add(x);return x;}
 public T GetComponent<T>()where T:Component=>cs.OfType<T>().FirstOrDefault();
 public T[] GetComponents<T>()where T:Component {if(ThrowDirect)throw new InvalidOperationException("native components unavailable");return NullDirect?null:NullSlot?new T[]{null}:cs.OfType<T>().ToArray();}
 public T[] GetComponentsInChildren<T>(bool includeInactive)where T:Component {var r=new List<T>();void Visit(GameObject g){r.AddRange(g.cs.OfType<T>());foreach(var c in g.transform.children)Visit(c.gameObject);}Visit(this);return r.ToArray();}
}
public class Transform {public GameObject gameObject;public List<Transform> children=new();public int childCount=>children.Count;public Transform(GameObject g){gameObject=g;}public Transform GetChild(int i)=>children[i];public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();public void Add(GameObject g)=>children.Add(g.transform);}
}
