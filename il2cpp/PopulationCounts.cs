using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>Read-only current-world roster cache. No scene scans, subscriptions, or gameplay writes.</summary>
internal static class PopulationCounts
{
    internal const int WorkerRole = 0, ArcherRole = 1, FarmerRole = 2, PikemanRole = 3;
    internal const int NinjaRole = 4, BerserkerRole = 5, PeasantRole = 6, BeggarRole = 7;
    internal const int RoleCount = 8, KnightRole = 8, StyleCount = 5;
    private const int MaxFailures = 3, DelayedRebuilds = 2;

    private sealed class Entry
    {
        internal Character Character;
        internal GameObject Object;
        internal Damageable Damageable;
        internal Knight Knight;
        internal IntPtr CharacterPtr, ObjectPtr;
        internal int ObjectId, Role;
    }

    private static readonly List<Entry> Entries = new();
    private static readonly HashSet<IntPtr> CharacterPointers = new(), ObjectPointers = new();
    private static readonly int[] Roles = new int[RoleCount], Styles = new int[StyleCount];
    private static IntPtr _kingdom, _world, _layer;
    private static int _scene;
    private static bool _hasContext, _dirty = true, _rebuild, _latched, _faultLogged;
    private static int _remainingRebuilds, _failures;
    private static float _nextRead, _retryAfter;
    internal static bool Ready { get; private set; }
    internal static bool ClientUnavailable { get; private set; }
    internal static long Version { get; private set; }
    internal static int Knights { get; private set; }
    internal static int UnknownKnights { get; private set; }
    internal static int Role(int index) => Ready && (uint)index < RoleCount ? Roles[index] : 0;
    internal static int Style(int index) => Ready && (uint)index < StyleCount ? Styles[index] : 0;

    // Existing Add/Remove hooks call this even when automatic restocking is disabled.
    // Never touch a native object or logger in the hook; multiple events collapse into one rebuild.
    internal static void NotifyRosterChanged() => _dirty = true;

    internal static void Reset()
    {
        Entries.Clear(); CharacterPointers.Clear(); ObjectPointers.Clear();
        Array.Clear(Roles, 0, Roles.Length); Array.Clear(Styles, 0, Styles.Length);
        Knights = UnknownKnights = 0;
        Ready = _hasContext = _rebuild = _latched = false;
        ClientUnavailable = false;
        _dirty = true;
        _kingdom = _world = _layer = IntPtr.Zero;
        _scene = _remainingRebuilds = _failures = 0;
        _nextRead = _retryAfter = 0;
        Version++;
    }

    internal static bool Refresh(Managers managers, bool enabled, float now)
    {
        if (!enabled)
        {
            if (_hasContext || Ready || Entries.Count != 0) Reset();
            return false;
        }
        // This also throttles failures while obtaining the native context itself.
        if (now < _retryAfter) return false;
        try
        {
            Kingdom kingdom = managers != null ? managers.kingdom : null;
            World world = managers != null ? managers.world : null;
            Game game = managers != null ? managers.game : null;
            Transform layer = world != null ? world.gameLayer : null;
            if (kingdom == null || world == null || game == null || layer == null)
            { ClearUnavailableContext(); return false; }

            IntPtr kingdomPtr = kingdom.Pointer, worldPtr = world.Pointer, layerPtr = layer.Pointer;
            int scene = layer.gameObject.scene.handle;
            bool sameContext = _hasContext && kingdomPtr == _kingdom && worldPtr == _world
                && layerPtr == _layer && scene == _scene;
            Game.State state = game.state;
            bool pausedInKnownWorld = state == Game.State.Menu && sameContext;
            if (state != Game.State.Playing && state != Game.State.NetworkClientPlaying && !pausedInKnownWorld)
            { ClearUnavailableContext(); return false; }

            if (!sameContext)
            {
                Reset();
                _hasContext = true;
                _kingdom = kingdomPtr; _world = worldPtr; _layer = layerPtr; _scene = scene;
            }
            // Native Character.OnEnable registers the kingdom roster only with world authority.
            // A client's empty/incomplete roster is not a trustworthy zero population.
            if (!NetworkBigBoss.HasWorldAuth)
            {
                if (!ClientUnavailable)
                {
                    Entries.Clear(); CharacterPointers.Clear(); ObjectPointers.Clear();
                    Array.Clear(Roles, 0, Roles.Length); Array.Clear(Styles, 0, Styles.Length);
                    Knights = UnknownKnights = 0;
                    Ready = false;
                    ClientUnavailable = true;
                    Version++;
                }
                return false;
            }
            if (ClientUnavailable)
            {
                ClientUnavailable = false;
                _dirty = true;
                _nextRead = _retryAfter = 0;
                Version++;
            }
            if (_dirty)
            {
                _dirty = false;
                _rebuild = true;
                _remainingRebuilds = DelayedRebuilds;
                _failures = 0;
                _latched = false;
                // Keep the last complete snapshot until sampling; Add/Remove bursts must not flicker the HUD.
            }
            if (_latched || now < _nextRead) return Ready;
            _nextRead = now + 1f;
            if (_rebuild)
            {
                Rebuild(kingdom);
                _rebuild = _remainingRebuilds > 0;
                if (_remainingRebuilds > 0) _remainingRebuilds--;
            }
            Sample(layer);
            Ready = true;
            _failures = 0;
            Version++;
            return true;
        }
        catch (Exception ex)
        {
            // Hide invalid/partial data; at most three attempts per incident, one second apart.
            Ready = false;
            _rebuild = true;
            _nextRead = now + 1f;
            _retryAfter = _nextRead;
            if (++_failures >= MaxFailures) _latched = true;
            if (!_faultLogged)
            {
                _faultLogged = true;
                try { KingdomEnhancedPlugin.Instance?.LogSource.LogWarning("[PopulationHUD] counts unavailable: " + ex.GetType().Name); }
                catch { }
            }
            return false;
        }
    }

    private static void ClearUnavailableContext()
    {
        if (_hasContext || Ready || Entries.Count != 0) Reset();
    }

    private static void Rebuild(Kingdom kingdom)
    {
        if (kingdom._characters == null) throw new InvalidOperationException("Population roster is not ready");
        Entries.Clear(); CharacterPointers.Clear(); ObjectPointers.Clear();
        foreach (Character character in kingdom._characters)
        {
            if (character == null) continue;
            IntPtr pointer = character.Pointer;
            if (pointer == IntPtr.Zero || !CharacterPointers.Add(pointer)) continue;
            GameObject go = character.gameObject;
            if (go == null) continue; // AddCharacter may precede full pool attachment; delayed seeds retry.
            IntPtr objectPtr = go.Pointer;
            if (objectPtr == IntPtr.Zero || !ObjectPointers.Add(objectPtr)) continue;
            Knight knight = go.GetComponent<Knight>();
            int role = Classify(go, knight);
            if (role < 0) continue; // Unknown NPCs never become unemployed villagers by default.
            Damageable damageable = character._damageable;
            if (damageable == null) damageable = go.GetComponent<Damageable>();
            Entries.Add(new Entry
            {
                Character = character, Object = go, Damageable = damageable, Knight = knight,
                CharacterPtr = pointer, ObjectPtr = objectPtr, ObjectId = go.GetInstanceID(), Role = role
            });
        }
    }

    private static int Classify(GameObject go, Knight knight)
    {
        if (knight != null) return KnightRole;
        if (go.GetComponent<Berserker>() != null) return BerserkerRole;
        if (go.GetComponent<Ninja>() != null) return NinjaRole;
        if (go.GetComponent<Pikeman>() != null) return PikemanRole;
        if (go.GetComponent<Farmer>() != null) return FarmerRole;
        if (go.GetComponent<Archer>() != null) return ArcherRole;
        if (go.GetComponent<Worker>() != null) return WorkerRole;
        if (go.GetComponent<Beggar>() != null) return BeggarRole;
        if (go.GetComponent<Peasant>() != null) return PeasantRole;
        return -1;
    }

    private static void Sample(Transform layer)
    {
        Array.Clear(Roles, 0, Roles.Length); Array.Clear(Styles, 0, Styles.Length);
        Knights = UnknownKnights = 0;
        foreach (Entry entry in Entries)
        {
            Character character = entry.Character;
            GameObject go = entry.Object;
            if (character == null || go == null || character.Pointer != entry.CharacterPtr
                || go.Pointer != entry.ObjectPtr || go.GetInstanceID() != entry.ObjectId) continue;
            if (!go.activeInHierarchy || go.scene.handle != _scene || !go.transform.IsChildOf(layer)) continue;
            // Missing liveness data must not turn an incompletely initialized profession into a false zero.
            if (entry.Damageable == null) throw new InvalidOperationException("Population damageable is not ready");
            if (entry.Damageable.isDead) continue;
            if (entry.Role != KnightRole) { Roles[entry.Role]++; continue; }
            Knights++;
            if (entry.Knight != null && PatchRoles_KnightStyle.TryGetResolvedStyleIndex(entry.Knight, out int style)
                && (uint)style < StyleCount) Styles[style]++;
            else UnknownKnights++;
        }
    }
}
