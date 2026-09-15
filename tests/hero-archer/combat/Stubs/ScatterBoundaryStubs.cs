using System;
using UnityEngine;

public enum DamageSource { Arrow }
public static class Layers { public const string Enemies = "Enemies"; }
public class Knight : MonoBehaviour
{
    public Damageable _damageable;
    public int StyleForTests;
    public bool StyleReady = true;
}
public class FriendlyTroll : MonoBehaviour { }

namespace KingdomEnhancedMod
{
    internal static class PatchRoles_KnightStyle
    {
        internal static bool TryGetResolvedStyleIndex(Knight knight, out int style)
        { style = knight != null ? knight.StyleForTests : -1; return knight != null && knight.StyleReady; }
    }
    internal static class PatchRoles_Crossbowman
    { internal static bool IsCrossbowman(Archer archer) => archer.IsCrossbowForTests; }
    internal static class PatchRoles_NorseSquad
    { internal static bool IsNorseArcherInstance(Archer archer) => archer.IsNorseForTests; }
}
namespace UnityEngine
{
    public static class LayerMask { public static int NameToLayer(string name) => name == "Enemies" ? 8 : name == "Wildlife" ? 9 : -1; }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white => new Color(1,1,1,1);
        public static Color Lerp(Color a, Color b, float t) => new Color(a.r+(b.r-a.r)*t,a.g+(b.g-a.g)*t,a.b+(b.b-a.b)*t,a.a+(b.a-a.a)*t);
        public static bool operator ==(Color a, Color b) => a.Equals(b);
        public static bool operator !=(Color a, Color b) => !a.Equals(b);
        public override bool Equals(object other) => other is Color c && r==c.r && g==c.g && b==c.b && a==c.a;
        public override int GetHashCode() => HashCode.Combine(r,g,b,a);
    }
    public class SpriteRenderer : Behaviour
    { public Color color = Color.white; }
}
