namespace Il2CppSystem.Collections.Generic
{
    // 只用于无游戏进程的单元测试：与真实 interop 的 ICollection/HashSet 形态对齐
    // （Kingdom.Archers 运行时是 HashSet，必须先 Cast 再枚举——坑 26）。
    public class HashSet<T> : System.Collections.Generic.HashSet<T> { }
}

internal static class NativeCastExtensions
{
    internal static T Cast<T>(this object value) where T : class => (T)value;
    internal static T TryCast<T>(this object value) where T : class => value as T;
}
