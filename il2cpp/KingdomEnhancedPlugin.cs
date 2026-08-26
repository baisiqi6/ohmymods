using BepInEx;
using BepInEx.Logging;

#if IL2CPP
using System.Reflection;
using BepInEx.Unity.IL2CPP;
#endif

#if MONO
using BepInEx.Unity.Mono;
#endif

namespace KingdomEnhancedMod;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("KingdomTwoCrowns.exe")]
public class KingdomEnhancedPlugin :
#if IL2CPP
    BasePlugin
#else
    BaseUnityPlugin
#endif
{
    public static KingdomEnhancedPlugin Instance;
    public ManualLogSource LogSource
#if IL2CPP
        => Log;
#else
        => Logger;
#endif

#if IL2CPP
    public override void Load()
    {
        // 注意：本工程未引 KingdomMod.SharedLib。若某组迁移需要自定义
        // MonoBehaviour（如 Holder 的缩放注册表 GameObject），该组负责补
        // SharedLib 引用或 Il2CppInterop.Runtime 原生 ClassInjector 注册。
        Init();
    }
#else
    internal void Awake()
    {
        Init();
    }
#endif

    private void Init()
    {
        try
        {
            Instance = this;
            // 手动构建戳：日志里区分不同部署（改完记得更新）
            LogSource.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} build=3.0.1-dev5 loading...");

            // 配置（BepInConfig，替代 UMM Settings）
            ModConfig.Init(Config);

            // 全程序集 [HarmonyPatch] 自动注册
            var harmony = new HarmonyLib.Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll(typeof(KingdomEnhancedPlugin).Assembly);

            // 游戏内设置面板（Ctrl+F10 / F5 呼出）
            ModPanel.EnsureCreated();

            LogSource.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} loaded. Enabled={ModConfig.Enabled.Value}");
        }
        catch (System.Exception e)
        {
            LogSource.LogError(e);
            throw;
        }
    }
}
