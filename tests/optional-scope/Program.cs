using KingdomEnhancedMod;
using UnityEngine;
public class BiomeHolder { public static BiomeHolder Inst; public int BiomeIndex; }
internal class Program
{
    static int checks;
    static void Check(bool value) { checks++; if (!value) throw new Exception("Scope check " + checks); }
    static void Main()
    {
        Check(!OptionalQoLScope.IsActive);
        BiomeHolder.Inst = new BiomeHolder();
        foreach (int biome in new[] { 0, 1, 2, 3, 4, 5, 6 }) { BiomeHolder.Inst.BiomeIndex = biome; Check(OptionalQoLScope.IsActive); }
        BiomeHolder.Inst.BiomeIndex = -1; Check(!OptionalQoLScope.IsActive);
        BiomeHolder.Inst.BiomeIndex = 4;
        ModConfig.Enabled.Value = false; Check(!OptionalQoLScope.IsActive);
        ModConfig.Enabled = null; Check(!OptionalQoLScope.IsActive);
        Check(!OptionalQoLScope.IsCurrent(null));
        var layerGo = new GameObject { Scene = new Scene { handle = 7 } };
        var layer = new Transform(); layerGo.Attach(layer);
        Managers.Inst = new Managers { world = new World { gameLayer = layer } };
        var go = new GameObject { Scene = layerGo.Scene };
        var child = new Transform { Parent = layer }; go.Attach(child);
        Check(OptionalQoLScope.IsCurrent(child));
        child.Parent = null; Check(!OptionalQoLScope.IsCurrent(child)); child.Parent = layer;
        go.Scene = new Scene { handle = 8 }; Check(!OptionalQoLScope.IsCurrent(child)); go.Scene = layerGo.Scene;
        go.Active = false; Check(!OptionalQoLScope.IsCurrent(child)); go.Active = true;
        layerGo.Active = false; Check(!OptionalQoLScope.IsCurrent(child));
        Console.WriteLine("PASS actual all-world scope: " + checks + " checks");
    }
}
