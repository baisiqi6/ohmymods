namespace KingdomEnhancedMod
{
    /// <summary>
    /// 编译门专用：确保正式 Native reader 及其 Observer 适配器在真实 interop 程序集表面上可解析。
    /// 本类型不被执行，也不证明运行期 class pointer / ABI / 字段复制语义。
    /// </summary>
    internal static class MountIslandNativeResultInteropCheck
    {
        internal static bool Probe(IslandSaveData.__TryLoad_d__54 instance)
        {
            var reader = new MountIslandNativeResultReader(instance);
            bool adapted = ((IMountIslandTerminalFlagsReader)reader).TryRead(out _, out _);
            bool direct = MountIslandNativeResult.TryRead(instance, out _, out _);
            return adapted && direct;
        }
    }
}
