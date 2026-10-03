using System;
using System.Collections.Generic;
using UnityEngine;

namespace Il2CppSystem
{
    public class Object
    {
        public T TryCast<T>() where T : class => this as T;
    }
    public class Action<T>
    {
        private readonly System.Action<T> _callback;
        public Action(System.Action<T> callback) => _callback = callback;
        public void Invoke(T value) => _callback(value);
    }
}

namespace Coatsink.Common
{
    [Flags]
    public enum SaveLoadResult { Save = 8, Success = 64, Failure = 128 }
    public static class Routine
    {
        public sealed class Return<T>
        {
            public T value;
        }
    }
}

public class PrefsSaveData : UnityEngine.Object
{
    public Il2CppSystem.Collections.Generic.Dictionary<string, string> contents = new();
    public void SetString(string key, string value) => contents[key] = value;
}

public class GlobalSaveData : UnityEngine.Object
{
    public static GlobalSaveData _loaded;
    public static string filename = "global-v35";
    public PrefsSaveData prefs = new();
    public List<CampaignSaveData> campaigns = new();
    public List<CampaignSaveData> challenges = new();
    public int currentCampaign;
    public int currentChallenge;

    public bool ThrowOnCurrentRead;
    public CampaignSaveData GetCurrentCampaign() {
        if (ThrowOnCurrentRead) throw new InvalidOperationException("current campaign read");
        return currentChallenge == 0 ? campaigns[currentCampaign] : challenges[currentChallenge - 1];
    }
    public void SaveAsync(Il2CppSystem.Action<Coatsink.Common.SaveLoadResult> callback) { }
    // Catalog mutation surface referenced by the hero rights patch classes.
    public CampaignSaveData CreateNewCampaign() => new CampaignSaveData();
    public void TryDeleteCampaignAsync() { }
    public void DeleteChallenge(int challenge) { }
    public sealed class __TryDeleteCampaign_d__91 { public int __1__state; public bool MoveNext() => false; }
    public sealed class __TryDeleteChallenge_d__94 { public int __1__state; public bool MoveNext() => false; }

    public sealed class _Save_d__89
    {
        public int __1__state;
        public GlobalSaveData __4__this;
        /// <summary>Test-only counter: how many times a gate wrote the boxed return.</summary>
        public int ReturnWrites;
        private Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult> _stored = new();
        public Coatsink.Common.Routine.Return<Coatsink.Common.SaveLoadResult> @return
        {
            get => new() { value = _stored.value }; // boxed copy
            set { ReturnWrites++; _stored = new() { value = value.value }; }
        }
        public bool MoveNext() => false;
    }
}

public class CampaignSaveData : UnityEngine.Object
{
    public static CampaignSaveData current;
    public List<IslandSaveData> _islands = new();
    public IslandSaveData CurrentIsland;
}

public class Persistent : UnityEngine.Component { }

public class BankerData : Il2CppSystem.Object
{
    public int stashedCoins;
}

public class IslandSaveData : UnityEngine.Object
{
    public int land;
    public List<ObjectData> objects = new();
    public static IslandSaveData CurrentlySavingIsland;
    public static bool isSavingGame;
    public void Save(int campaign, int land, int challenge) { }
    public string GetID(Persistent root) => "";
    public bool TryPopObjectsToScene() => true;
    public Persistent TryCreateOrFind(ObjectData row) => null;
    public void UpdateSavedWithRevisions() { }

    public sealed class ObjectData : UnityEngine.Object
    {
        public string uniqueID;
        public int netID;
        public List<ComponentData> componentData2 = new();
    }
    public sealed class ComponentData : UnityEngine.Object
    {
        public new string name;
        public string type, data;
    }
}
