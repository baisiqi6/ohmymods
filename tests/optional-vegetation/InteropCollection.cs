namespace Il2CppSystem.Collections.Generic
{
    public class HashSet<T> : System.Collections.Generic.HashSet<T> { }
}

internal static class NativeCastExtensions
{
    internal static T Cast<T>(this object value) where T : class => (T)value;
    internal static T TryCast<T>(this object value) where T : class => value as T;
}
