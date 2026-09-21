namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public struct Bounds { public Vector3 min,max; public Bounds(Vector3 min,Vector3 max){this.min=min;this.max=max;} }
    public class Transform
    {
        private Vector3 _position;
        public int Writes;
        public Vector3 position{get=>_position;set{_position=value;Writes++;}}
    }
    public class GameObject { public bool activeInHierarchy = true; }
    public class Sprite { }
    public class SpriteRenderer
    {
        public Sprite sprite=new();
        public bool enabled=true;
        public float alpha=1f;
        public Transform Owner;
        public Vector3 LocalMin,LocalMax;
        public bool EmptyWorldOrigin;
        public Bounds bounds=>EmptyWorldOrigin?new(new(0,0,0),new(0,0,0)):new(Owner.position+LocalMin,Owner.position+LocalMax);
    }
    public class Camera
    {
        public bool orthographic=true;
        public int pixelWidth=1280,pixelHeight=720;
        public float orthographicSize=2.8125f,aspect=16f/9f;
        public Vector3 Position=new(-20f,0f,-10f);
        public Vector3 WorldToViewportPoint(Vector3 p)=>new(.5f+(p.x-Position.x)/(2*orthographicSize*aspect),.5f+(p.y-Position.y)/(2*orthographicSize),p.z-Position.z);
        public Vector3 ViewportToWorldPoint(Vector3 p)=>new(Position.x+(p.x-.5f)*(2*orthographicSize*aspect),Position.y+(p.y-.5f)*(2*orthographicSize),Position.z+p.z);
    }
}
public class InterfaceCamera { public UnityEngine.Camera InterfaceCam=new(); }
public class Player { public UnityEngine.GameObject gameObject = new(); }
public class CurrencyBag
{
    public enum FadeState { FadingIn,Opening,Open,Closing,FadingOut,Hidden }
    public static bool DebugHideCurrencyBag;
    public FadeState CurrentFadeState = FadeState.Hidden;
    public bool enabled = true, _showingCurrency;
    public Player player = new();
    public UnityEngine.GameObject gameObject = new();
    public InterfaceCamera CachedInterfaceCam=new();
    public UnityEngine.Transform transform=new();
    public UnityEngine.SpriteRenderer _front,_back,_closed;
}
