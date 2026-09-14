using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    static int checks;
    static void Check(bool result) { checks++; if (!result) throw new Exception("Archer scope check " + checks); }
    static void Main()
    {
        Check(!ArcherOptionsScope.IsActive);
        BiomeHolder.Inst = new BiomeHolder { BiomeIndex = 4 };
        Managers.Inst = new Managers();
        Check(!ArcherOptionsScope.IsActive);
        Managers.Inst.game = new Game();
        Check(!ArcherOptionsScope.IsActive);
        Managers.Inst.game.playingOrInMenuWithClient = true;
        for (int biome = 0; biome <= 6; biome++) { BiomeHolder.Inst.BiomeIndex = biome; Check(ArcherOptionsScope.IsActive); }
        BiomeHolder.Inst.BiomeIndex = -1; Check(!ArcherOptionsScope.IsActive);
        BiomeHolder.Inst.BiomeIndex = 4;
        Check(!ArcherOptionsScope.TryGetContext(out _, out _, out _));
        var root = new Transform { Pointer = (IntPtr)21, gameObject = new GameObject { scene = new Scene { handle = 7 } } };
        Managers.Inst.world = new World { Pointer = (IntPtr)11, gameLayer = root };
        Check(ArcherOptionsScope.TryGetContext(out var world, out var layer, out var scene));
        Check(world == (IntPtr)11 && layer == (IntPtr)21 && scene == 7);
        var child = new Transform { Parent = root, gameObject = new GameObject { scene = new Scene { handle = 7 } } };
        Check(ArcherOptionsScope.IsCurrent(child));
        child.Parent = null; Check(!ArcherOptionsScope.IsCurrent(child)); child.Parent = root;
        child.gameObject.scene = new Scene { handle = 8 }; Check(!ArcherOptionsScope.IsCurrent(child));
        child.gameObject.scene = new Scene { handle = 7 };
        root.gameObject.activeInHierarchy = false; Check(!ArcherOptionsScope.TryGetContext(out _, out _, out _));
        Check(!ArcherOptionsScope.IsCurrent(child)); root.gameObject.activeInHierarchy = true;
        Managers.Inst.world = new World { Pointer = (IntPtr)12, gameLayer = root };
        Check(ArcherOptionsScope.TryGetContext(out var nextWorld, out _, out _) && nextWorld != world);
        root.Pointer = (IntPtr)22; root.gameObject.scene = new Scene { handle = 9 };
        Check(ArcherOptionsScope.TryGetContext(out _, out var nextLayer, out var nextScene) && nextLayer != layer && nextScene != scene);
        root.Pointer = IntPtr.Zero; Check(!ArcherOptionsScope.TryGetContext(out _, out _, out _)); root.Pointer = (IntPtr)22;
        Managers.Inst.game.playingOrInMenuWithClient = false; Check(!ArcherOptionsScope.TryGetContext(out _, out _, out _));
        Managers.Inst.game.playingOrInMenuWithClient = true; ModConfig.Enabled.Value = false;
        Check(!ArcherOptionsScope.IsActive); Check(!ArcherOptionsScope.TryGetContext(out _, out _, out _));
        ModConfig.Enabled = null; Check(!ArcherOptionsScope.IsActive);
        Check(!ArcherOptionsScope.IsCurrent(null));
        Console.WriteLine($"PASS actual archer scope: {checks} checks");
    }
}
public class BiomeHolder { public static BiomeHolder Inst; public int BiomeIndex; }
public class Game { public bool playingOrInMenuWithClient; }
public class World { public IntPtr Pointer; public Transform gameLayer; }
public class Managers { public static Managers Inst; public World world; public Game game; }
namespace KingdomEnhancedMod
{
    internal class Entry { public bool Value = true; }
    internal static class ModConfig { internal static Entry Enabled = new(); }
}
namespace UnityEngine
{
    public class Component { public GameObject gameObject; public Transform transform => this as Transform; }
    public class Transform : Component
    {
        public IntPtr Pointer;
        public Transform Parent;
        public bool IsChildOf(Transform parent) { for (var p = this; p != null; p = p.Parent) if (p == parent) return true; return false; }
    }
    public struct Scene { public int handle; }
    public class GameObject { public bool activeInHierarchy = true; public Scene scene; }
}
