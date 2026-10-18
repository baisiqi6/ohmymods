using System;
using System.Collections.Generic;

namespace Il2CppSystem.Collections.Generic
{
    internal class List<T> : System.Collections.Generic.List<T> { }
}

namespace ScopeAdapter
{
    internal enum ContentLayers { GameLayer = 7 }

    internal class IslandSaveData
    {
        private int _land;
        internal static int LandReads;
        internal static bool ThrowLand;
        internal int land
        {
            get
            {
                LandReads++;
                if (ThrowLand) throw new InvalidOperationException("island boom");
                return _land;
            }
            set { _land = value; }
        }
        internal List<object> objects = new List<object> { new object() };
    }

    internal class GlobalSaveData { }

    internal class CampaignSaveData
    {
        internal static CampaignSaveData current;
        private int _currentLand;
        internal static int CurrentLandReads;
        internal static bool ThrowCurrentLand;
        internal int CurrentLand
        {
            get
            {
                CurrentLandReads++;
                if (ThrowCurrentLand) throw new InvalidOperationException("current boom");
                return _currentLand;
            }
            set { _currentLand = value; }
        }
        internal ReignInfo currentReign;
        internal Il2CppSystem.Collections.Generic.List<IslandSaveData> _islands;
        internal int reign;
        internal class LandMapData { }
        internal class ReignInfo
        {
            internal Il2CppSystem.Collections.Generic.List<LandMapData> landData;
        }
    }

    internal static class ExtensionIslandRuntime
    {
        internal static Func<GlobalSaveData> GlobalFactory;
        internal static Func<int> SelectedFactory;
        internal static Func<CampaignSaveData> OwnerFactory;
        internal static int Calls;
        internal static bool TryResolveRawOwner(out GlobalSaveData global, out int selected,
            out CampaignSaveData owner)
        {
            Calls++;
            global = GlobalFactory != null ? GlobalFactory() : null;
            selected = SelectedFactory != null ? SelectedFactory() : 0;
            owner = OwnerFactory != null ? OwnerFactory() : null;
            return global != null && owner != null;
        }
    }

    internal class Scene
    {
        internal bool valid = true;
        internal bool isLoaded = true;
        internal int handle = 1;
        internal string name = "main";
        internal bool IsValid() { return valid; }
    }

    internal class GameObject { internal Scene scene = new Scene(); }

    internal class Quaternion { internal float x = 0f, y = 0f, z = 0f, w = 1f; }
    internal class Vector3 { internal float x = 1f, y = 1f, z = 1f; }

    internal class Transform
    {
        internal GameObject gameObject = new GameObject();
        internal Quaternion rotation = new Quaternion();
        internal Vector3 lossyScale = new Vector3();
    }

    internal class World { internal Transform gameLayer = new Transform(); }

    internal class Level
    {
        internal GameObject gameObject = new GameObject();
        internal int cachedCurrentLevelSeed = 11;
        internal static Func<Level, GameObject> LayerFactory;
        internal static int GetLayerCalls;
        internal GameObject GetLayer(ContentLayers layer)
        {
            GetLayerCalls++;
            if (LayerFactory != null) return LayerFactory(this);
            return null;
        }
    }

    internal class Managers
    {
        internal static Managers Inst;
        internal Level level = new Level();
        internal World world = new World();
    }

    internal class BiomeHolder
    {
        internal static BiomeHolder Inst;
        internal const int GreeceBiomeIndex = 1;
        internal int BiomeIndex = 1;
    }

    internal static class CrossWorldMountDependencies
    {
        internal static ulong PointerOfList(Il2CppSystem.Collections.Generic.List<CampaignSaveData.LandMapData> value)
            => RuntimeState.Ptr(value);
    }

    internal static class RuntimeState
    {
        internal static ulong Ptr(object value)
        {
            if (value == null) return 0;
            return (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value) + 1;
        }
    }

    internal static partial class MountIslandRecoveryRuntime
    {
        internal static ulong PointerOf(object value) => RuntimeState.Ptr(value);
    }

    internal static partial class OldMountIslandRecoveryRuntime
    {
        internal static ulong PointerOf(object value) => RuntimeState.Ptr(value);
    }
}
