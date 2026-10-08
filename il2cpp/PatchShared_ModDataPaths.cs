#if ANDROID
namespace KingdomEnhancedMod
{
    /// <summary>平台配置根：PC 由 BepInEx.Paths.ConfigPath 提供，Android 由 loader 的 MelonEnvironment.UserDataDirectory 提供。</summary>
    internal static class ModDataPaths
    {
        /// <summary>Android 数据根直接取自 loader 已建立的用户数据目录；本类不含 I/O、复制或额外持久化层。</summary>
        internal static string ConfigPath => MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
    }
}
#endif
