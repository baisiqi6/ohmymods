using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KingdomEnhancedMod;
using UnityEngine;
using Policy = KingdomEnhancedMod.PatchRoles_PetGuard;

static class Program
{
    static int passed, failed, assertions;
    static IDictionary Tracked(Type type) => (IDictionary)type.GetField("Tracked", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static CampaignSaveData Save => CampaignSaveData.current;

    static void Eq<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
    }
    static void Check(bool value, string label) { assertions++; if (!value) throw new Exception(label); }

    static void ResetType(Type type)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (field.IsInitOnly) continue;
            if (field.FieldType == typeof(bool)) field.SetValue(null, false);
            else if (field.FieldType == typeof(int)) field.SetValue(null, 0);
            else if (field.FieldType == typeof(float)) field.SetValue(null, 0f);
            else if (field.FieldType == typeof(IntPtr)) field.SetValue(null, IntPtr.Zero);
        }
    }

    static void Test(string name, Action action)
    {
        Tracked(typeof(Policy)).Clear();
        Tracked(typeof(PatchRoles_Hermit)).Clear();
        ResetType(typeof(Policy));
        ResetType(typeof(PatchRoles_Hermit));
        Managers.Inst = new Managers();
        NetworkBigBoss.HasWorldAuth = true;
        ModConfig.Enabled.Value = true;
        ModConfig.PetGuardEnabled.Value = true;
        Time.unscaledTime = 0;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        CampaignSaveData.current = new CampaignSaveData();
        CampaignSaveData.Spawns.Clear();
        CampaignSaveData.SpawnPrefabs.Clear();
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }

    static Droppable NewDroppable()
    {
        var go = new GameObject();
        go.transform.parent = Managers.Inst.world.gameLayer;
        go.scene = Managers.Inst.world.gameLayer.gameObject.scene;
        return go.AddComponent<Droppable>();
    }

    static Droppable NewDog(PickUpPolicy policy = PickUpPolicy.Anybody, int dogId = 0)
    {
        var d = NewDroppable();
        d.NativePolicy(policy); d.NativeOriginal(policy);
        var dog = d.gameObject.AddComponent<Dog>(); dog.dogId = dogId;
        return d;
    }

    static Droppable NewHermit(PickUpPolicy policy = PickUpPolicy.Anybody)
    {
        var d = NewDroppable();
        d.NativePolicy(policy); d.NativeOriginal(policy);
        d.gameObject.AddComponent<Hermit>();
        return d;
    }

    // 生产环境里 Hermit 与 PetGuard 两个 postfix 同时挂在 Droppable.OnEnable 上；OnDisable 两个 prefix 同理。
    static void Enable(Droppable d)
    {
        d.OnEnable();
        Droppable_OnEnable_HermitPickupPolicy_Patch.Postfix(d);
        Droppable_OnEnable_PetGuard_Patch.Postfix(d);
    }
    static void Disable(Droppable d)
    {
        Droppable_OnDisable_HermitPickupPolicy_Patch.Prefix(d);
        Droppable_OnDisable_PetGuard_Patch.Prefix(d);
        d.OnDisable();
    }
    // 生产驱动：ModPanel.Update 每帧同时调两个 Tick（ModPanel.cs:49 + PetGuard 驱动行）。
    // 隐士 receipt 的还原在 PatchRoles_Hermit.Tick 里——只驱动 PetGuard 会漏掉
    // "共享开关关闭→隐士还原"路径（首轮 1 fail 的根因）。
    static void Tick(float time = .5f) { Time.unscaledTime = time; Policy.Tick(); PatchRoles_Hermit.Tick(); }

    static void Parent(Droppable d, Transform parent) => d.transform.parent = parent;

    static void Main()
    {
        Test("PetGuard hooks are exactly the two long Droppable lifecycle methods", () =>
        {
            var hooks = typeof(Policy).Assembly.GetTypes()
                .SelectMany(t => t.GetCustomAttributes<HarmonyPatch>().Select(a => (type: t, patch: a)))
                .Where(x => x.type.Name.Contains("PetGuard")).ToArray();
            Eq(2, hooks.Length, "exact hook count");
            Check(hooks.All(x => x.patch.Target == typeof(Droppable)), "target type");
            Eq(true, hooks.Any(x => x.patch.Method == "OnEnable"), "enable hook");
            Eq(true, hooks.Any(x => x.patch.Method == "OnDisable"), "disable hook");
            Eq(false, hooks.Any(x => x.patch.Method == "CanBePickedUpByEnemy"), "short getter not hooked");
            var postfix = typeof(Droppable_OnEnable_PetGuard_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            var prefix = typeof(Droppable_OnDisable_PetGuard_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            Eq(true, postfix.IsDefined(typeof(HarmonyPostfix)), "enable postfix");
            Eq(true, prefix.IsDefined(typeof(HarmonyPrefix)), "disable prefix");
            var hermitHooks = typeof(Policy).Assembly.GetTypes()
                .Where(t => t.Name.Contains("HermitPickupPolicy"))
                .SelectMany(t => t.GetCustomAttributes<HarmonyPatch>()).ToArray();
            Eq(2, hermitHooks.Length, "hermit hook pair unchanged");
        });

        Test("Non-dog droppables register no pet receipt and receive no writes", () =>
        {
            var plain = NewDroppable();
            Enable(plain); Tick();
            Eq(0, Tracked(typeof(Policy)).Count, "no receipt for a plain droppable");
            Eq(0, plain.EnemyWrites, "no write");
            var hermit = NewHermit();
            Enable(hermit); Tick();
            Eq(0, Tracked(typeof(Policy)).Count, "no dog receipt for a hermit");
            Eq(PickUpPolicy.Nobody, hermit.CurrentEnemyPolicy, "hermit receipt still protects");
            Eq(1, hermit.EnemyWrites, "single hermit write");
        });

        Test("Dog Anybody/EnemyOnly becomes Nobody and the exact original is restored", () =>
        {
            foreach (var original in new[] { PickUpPolicy.Anybody, PickUpPolicy.EnemyOnly })
            {
                ModConfig.PetGuardEnabled.Value = true;
                var d = NewDog(original);
                Enable(d);
                Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protected");
                Eq(1, d.EnemyWrites, "single protection write");
                ModConfig.PetGuardEnabled.Value = false; Tick();
                Eq(original, d.CurrentEnemyPolicy, "exact original restored");
                Eq(2, d.EnemyWrites, "one restore write");
                Eq(original, d._originalEnemyPolicy, "native original unchanged");
                Eq(0, d.OriginalWrites, "never writes native original");
                Eq(0, d.GeneralWrites, "general player pickup untouched");
                Eq(1, Tracked(typeof(Policy)).Count, "receipt retained until OnDisable");
                Disable(d);
                Eq(0, Tracked(typeof(Policy)).Count, "retired after disable");
            }
        });

        Test("Global switch off suppresses protection even with PetGuard left on", () =>
        {
            ModConfig.Enabled.Value = false;
            ModConfig.PetGuardEnabled.Value = true;
            var d = NewDog();
            Enable(d); Tick();
            Eq(0, d.EnemyWrites, "no protection while the global switch is off");
            ModConfig.Enabled.Value = true; Tick(.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protection resumes when global returns");
            ModConfig.Enabled.Value = false; Tick(.52f);
            Eq(PickUpPolicy.Anybody, d.CurrentEnemyPolicy, "global off restores the original again");
        });

        Test("Duplicate enables and pool generations keep one receipt and the original", () =>
        {
            var d = NewDog(PickUpPolicy.EnemyOnly); Enable(d);
            for (int i = 0; i < 20; i++) Enable(d);
            Eq(1, Tracked(typeof(Policy)).Count, "one generation");
            Eq(1, d.EnemyWrites, "duplicate enable no writes");
            Disable(d);
            Eq(0, Tracked(typeof(Policy)).Count, "retired on disable");
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "native reset restored its own original");
            d.NativePolicy(PickUpPolicy.EnemyOnly); d.NativeOriginal(PickUpPolicy.EnemyOnly); Enable(d);
            ModConfig.PetGuardEnabled.Value = false; Tick();
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "new pool generation captured its own original");
        });

        Test("External dog policy ownership is never overwritten", () =>
        {
            var d = NewDog(); Enable(d);
            d.NativePolicy(PickUpPolicy.Blocked);
            ModConfig.PetGuardEnabled.Value = false; Tick();
            Eq(PickUpPolicy.Blocked, d.CurrentEnemyPolicy, "external policy preserved");
            ModConfig.PetGuardEnabled.Value = true; Tick(.51f);
            ModConfig.PetGuardEnabled.Value = false; Tick(.52f);
            Eq(1, d.EnemyWrites, "contested generation never reclaims");
        });

        Test("Client registers a dog receipt but never reads or writes policy", () =>
        {
            NetworkBigBoss.HasWorldAuth = false;
            Managers.Inst.game.state = Game.State.NetworkClientPlaying;
            var d = NewDog();
            d.ThrowRead = true;
            Enable(d); Tick();
            Eq(1, Tracked(typeof(Policy)).Count, "client registration");
            Eq(0, d.EnemyReads, "no client policy reads");
            Eq(0, d.EnemyWrites, "no client writes");
            d.ThrowRead = false;
            NetworkBigBoss.HasWorldAuth = true;
            Managers.Inst.game.state = Game.State.Playing;
            Tick(.51f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "host takeover protects");
        });

        Test("Null config entries are inert instead of throwing", () =>
        {
            ModConfig.Enabled = null;
            var d = NewDog();
            Enable(d); Tick();
            Eq(0, d.EnemyWrites, "no writes with a null global entry");
            ModConfig.Enabled = new ModConfig.Flag();
            ModConfig.PetGuardEnabled = null;
            Tick(.51f);
            Eq(0, d.EnemyWrites, "no writes with a null pet guard entry");
            ModConfig.PetGuardEnabled = new ModConfig.Flag();
            Tick(1f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protection resumes");
        });

        Test("Hermit receipts share the switch: off restores, global off wins", () =>
        {
            var d = NewHermit(PickUpPolicy.EnemyOnly);
            Enable(d);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "hermit protected by the shared switch");
            ModConfig.PetGuardEnabled.Value = false; Tick();
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "hermit restored when the shared switch turns off");
            ModConfig.PetGuardEnabled.Value = true; Tick(.51f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "re-protected on the same receipt");
            ModConfig.Enabled.Value = false; Tick(1f);
            Eq(PickUpPolicy.EnemyOnly, d.CurrentEnemyPolicy, "global off restores even with PetGuard left on");
        });

        Test("Owned dog aboard a boat keeps native Nobody when the switch turns off", () =>
        {
            var d = NewDog();
            Enable(d);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "protected");
            var boatGo = new GameObject();
            boatGo.transform.parent = Managers.Inst.world.gameLayer; // 原生：船挂在 world.gameLayer 下
            var boat = boatGo.AddComponent<Boat>();
            var bodyGo = new GameObject();
            bodyGo.transform.parent = boatGo.transform;
            boat.body = bodyGo.transform;
            Parent(d, bodyGo.transform);
            ModConfig.PetGuardEnabled.Value = false; Tick(.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "aboard: no restore");
            Eq(1, d.EnemyWrites, "no restore write");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt retained for the deferred restore");
            Parent(d, Managers.Inst.world.gameLayer);
            Tick(.52f);
            Eq(PickUpPolicy.Anybody, d.CurrentEnemyPolicy, "off boat restores the original");
            Eq(2, d.EnemyWrites, "one restore write");
        });

        Test("Dog leaving the game layer retires the receipt without a restore", () =>
        {
            var d = NewDog();
            Enable(d);
            Parent(d, new GameObject().transform); // 世界层外的挂点（非常规船体）
            ModConfig.PetGuardEnabled.Value = false; Tick(.01f);
            Eq(0, Tracked(typeof(Policy)).Count, "retired on layer departure");
            Eq(1, d.EnemyWrites, "never restores outside the world layer");
        });

        Test("BoatBody tag alone also defers the restore", () =>
        {
            var d = NewDog();
            Enable(d);
            var taggedBoat = new GameObject();
            taggedBoat.transform.parent = Managers.Inst.world.gameLayer;
            var body = new GameObject();
            body.tag = "BoatBody"; // 无 Boat 组件：只靠原生 BoatBody 标签判定
            body.transform.parent = taggedBoat.transform;
            Parent(d, body.transform);
            ModConfig.PetGuardEnabled.Value = false; Tick(.01f);
            Eq(PickUpPolicy.Nobody, d.CurrentEnemyPolicy, "tagged body defers");
            Eq(1, Tracked(typeof(Policy)).Count, "receipt still retained");
            Parent(d, Managers.Inst.world.gameLayer);
            Tick(.52f);
            Eq(PickUpPolicy.Anybody, d.CurrentEnemyPolicy, "restored after leaving the boat");
        });

        Test("Off->on edge recalls a stolen dog to the current land", () =>
        {
            Save.CurrentLand = 9;
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 4, color = new Color(0.2f, 0.4f, 0.6f), type = Dog.DogType.Dog, preferedPlayer = 1 };
            ModConfig.PetGuardEnabled.Value = false; Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "switch off leaves the stolen state to native paths");
            Eq(Dog.DogPosition.Stolen, Save.GetDogStatus()[0].position, "stolen preserved while off");
            Eq(0, Save.DogWrites.Count, "no status write while off");
            ModConfig.PetGuardEnabled.Value = true; Tick(.02f);
            Eq(1, CampaignSaveData.Spawns.Count, "one recalled dog");
            var dog = (Dog)CampaignSaveData.Spawns[0];
            Eq(0, dog.DogId, "dog id");
            Eq(1, dog.SetupDogCalls, "SetupDog called once");
            Eq(0, dog.LastSetupDogId, "SetupDog got the recalled id");
            Check(dog.color.r == 0.2f && dog.color.g == 0.4f && dog.color.b == 0.6f, "color preserved");
            Eq(Dog.DogPosition.Roaming, Save.GetDogStatus()[0].position, "status rewritten to Roaming");
            Eq(9, Save.GetDogStatus()[0].land, "land rewritten to the current land");
            Check(Save.DogWrites.Any(w => w.StartsWith("0:Roaming:9:")), "SetDogStatus used the current land");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Info.Any(l => l.Contains("dog 0")), "one recall log");
        });

        Test("Cross-island stolen wolf pup is recalled to the current land with its type", () =>
        {
            Save.CurrentLand = 9;
            Save.dog1 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 3, color = new Color(1f, 0.5f, 0f), type = Dog.DogType.WolfPup };
            Tick(.01f);
            Eq(1, CampaignSaveData.Spawns.Count, "recalled while the switch was already on");
            Check(ReferenceEquals(Managers.Inst.holder.wolfPupPrefab, CampaignSaveData.SpawnPrefabs[0]), "wolf pup prefab used");
            Eq(9, Save.GetDogStatus()[1].land, "cross-island land rewritten");
            Eq(Dog.DogType.WolfPup, Save.GetDogStatus()[1].type, "type preserved");
        });

        Test("dog0 and dog2 recover independently", () =>
        {
            Save.dog1 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 2, type = Dog.DogType.Dog };
            Tick(.01f);
            Eq(1, CampaignSaveData.Spawns.Count, "only the stolen dog spawns");
            Eq(1, ((Dog)CampaignSaveData.Spawns[0]).LastSetupDogId, "dog id 1");
            Eq(Dog.DogPosition.Roaming, Save.GetDogStatus()[1].position, "dog2 roaming");
            Eq(Dog.DogPosition.Locked, Save.GetDogStatus()[0].position, "dog0 untouched");
            Eq(1, Save.DogWrites.Count, "one status write");
        });

        Test("Recall is idempotent across repeated ticks and new world generations", () =>
        {
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            Tick(.01f);
            Eq(1, CampaignSaveData.Spawns.Count, "first recall");
            Tick(5f);
            Eq(1, CampaignSaveData.Spawns.Count, "second tick adds nothing");
            Managers.Inst.world = new World();
            Tick(6f);
            Eq(1, CampaignSaveData.Spawns.Count, "a fresh load generation does not spawn a second dog");
            Eq(1, Save.DogWrites.Count, "no second status write");
        });

        Test("Stolen dog with a live same-id instance is deferred, never duplicated", () =>
        {
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            var liveGo = new GameObject();
            liveGo.transform.parent = Managers.Inst.world.gameLayer;
            var live = liveGo.AddComponent<Dog>();
            live.dogId = 0;
            Managers.Inst.kingdom.dogs.Add(live);
            Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "no duplicate spawn");
            Eq(0, Save.DogWrites.Count, "no rewrite while the instance lives");
            Eq(Dog.DogPosition.Stolen, Save.GetDogStatus()[0].position, "status deferred with the instance");
            Managers.Inst.world = new World(); Tick(6f);
            Managers.Inst.world = new World(); Tick(12f);
            Eq(1, KingdomEnhancedPlugin.Instance.LogSource.Info.Count(l => l.Contains("recall deferred")), "bounded deferral log");
            Managers.Inst.kingdom.dogs.Remove(live);
            liveGo.activeInHierarchy = false;
            Managers.Inst.world = new World(); Tick(18f);
            Eq(1, CampaignSaveData.Spawns.Count, "recovered once the instance is gone");
            Eq(Dog.DogPosition.Roaming, Save.GetDogStatus()[0].position, "status rewritten then");
        });

        Test("Non-stolen dog states are never touched", () =>
        {
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Roaming, land = 9 };
            Save.dog1 = new Dog.DogStatus { position = Dog.DogPosition.PickedUp, land = 9 };
            Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "no spawn");
            Eq(0, Save.DogWrites.Count, "no status writes");
            Save.dog1 = new Dog.DogStatus { position = Dog.DogPosition.Locked };
            Managers.Inst.world = new World(); Tick(6f);
            Eq(0, Save.DogWrites.Count, "still no writes");
            Eq(0, CampaignSaveData.Spawns.Count, "still no spawn");
        });

        Test("Stolen hermit is recovered to Roaming on the current land with its player kept", () =>
        {
            Save.CurrentLand = 9;
            Save.hermitStatuses[(int)Hermit.HermitType.Baker] = new Hermit.HermitStatus { position = Hermit.HermitPosition.Stolen, player = 1, land = 3 };
            Tick(.01f);
            Eq(1, CampaignSaveData.Spawns.Count, "one hermit spawn");
            var hermit = (Hermit)CampaignSaveData.Spawns[0];
            Eq(Hermit.HermitType.Baker, hermit.Type, "baker prefab produced a baker");
            Check(ReferenceEquals(Managers.Inst.holder.hermits[(int)Hermit.HermitType.Baker], CampaignSaveData.SpawnPrefabs[0]), "typed prefab used");
            var status = Save.GetHermitStatus(Hermit.HermitType.Baker);
            Eq(Hermit.HermitPosition.Roaming, status.position, "roaming");
            Eq(1, status.player, "player kept");
            Eq(9, status.land, "land rewritten to the current land");
            Check(Save.HermitWrites.Any(w => w == (int)Hermit.HermitType.Baker + ":Roaming:1:9"), "full overwrite kept player and current land");
            Check(KingdomEnhancedPlugin.Instance.LogSource.Info.Any(l => l.Contains("hermit " + (int)Hermit.HermitType.Baker)), "recall log");
        });

        Test("Hermit of a type already in scene defers the recall", () =>
        {
            Save.hermitStatuses[(int)Hermit.HermitType.Horn] = new Hermit.HermitStatus { position = Hermit.HermitPosition.Stolen, land = 3 };
            var liveGo = new GameObject();
            var live = liveGo.AddComponent<Hermit>();
            live.Type = Hermit.HermitType.Horn;
            Managers.Inst.kingdom.hermits.Add(live);
            Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "no duplicate hermit");
            Eq(0, Save.HermitWrites.Count, "no rewrite while the instance lives");
            Eq(Hermit.HermitPosition.Stolen, Save.GetHermitStatus(Hermit.HermitType.Horn).position, "status deferred with the instance");
        });

        Test("Hermit positions other than Stolen are never written or spawned", () =>
        {
            Save.hermitStatuses[(int)Hermit.HermitType.Horse] = new Hermit.HermitStatus { position = Hermit.HermitPosition.GemLocked };
            Save.hermitStatuses[(int)Hermit.HermitType.Horn] = new Hermit.HermitStatus { position = Hermit.HermitPosition.CoinLocked };
            Save.hermitStatuses[(int)Hermit.HermitType.Ballista] = new Hermit.HermitStatus { position = Hermit.HermitPosition.Roaming, land = 9 };
            Save.hermitStatuses[(int)Hermit.HermitType.Baker] = new Hermit.HermitStatus { position = Hermit.HermitPosition.PickedUp, land = 9 };
            Save.hermitStatuses[(int)Hermit.HermitType.Knight] = new Hermit.HermitStatus { position = Hermit.HermitPosition.Passenger, player = 1, land = 0 };
            Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "no spawn");
            Eq(0, Save.HermitWrites.Count, "no status writes");
            Eq(Hermit.HermitPosition.Passenger, Save.GetHermitStatus(Hermit.HermitType.Knight).position, "carried passenger untouched");
            Eq(Hermit.HermitPosition.CoinLocked, Save.GetHermitStatus(Hermit.HermitType.Horn).position, "locked ownership untouched");
            Managers.Inst.world = new World(); Tick(6f);
            Eq(0, Save.HermitWrites.Count, "still untouched after a new generation");
        });

        Test("Recall runs on the off->on edge and new world generations only", () =>
        {
            Tick(.01f);
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            Tick(.02f); Tick(1f);
            Eq(0, CampaignSaveData.Spawns.Count, "no recall without a trigger");
            ModConfig.PetGuardEnabled.Value = false; Tick(2f);
            ModConfig.PetGuardEnabled.Value = true; Tick(3f);
            Eq(1, CampaignSaveData.Spawns.Count, "off->on edge recalls");
        });

        Test("Loading marks a stale generation and the recall runs when playing returns", () =>
        {
            Tick(.01f);
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            Managers.Inst.game.state = Game.State.Loading; Tick(1f);
            Eq(0, CampaignSaveData.Spawns.Count, "no recall while loading");
            Managers.Inst.game.state = Game.State.Playing; Tick(2f);
            Eq(1, CampaignSaveData.Spawns.Count, "recall after the load completes");
        });

        Test("Missing spawn prerequisites defer without a partial status rewrite", () =>
        {
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            Managers.Inst.holder.dogPrefab = null;
            Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "no spawn without a prefab");
            Eq(0, Save.DogWrites.Count, "no partial rewrite");
            Managers.Inst.holder.dogPrefab = new Dog();
            Managers.Inst.kingdom.playerOne = null;
            Managers.Inst.world = new World(); Tick(6f);
            Eq(0, Save.DogWrites.Count, "no rewrite without P1");
            Managers.Inst.kingdom.playerOne = new Player();
            Managers.Inst.world = new World(); Tick(12f);
            Eq(1, CampaignSaveData.Spawns.Count, "recovers once the prerequisites exist");
            Eq(Dog.DogPosition.Roaming, Save.GetDogStatus()[0].position, "status rewritten on the successful pass");
        });

        Test("Clients never recall stolen pets", () =>
        {
            NetworkBigBoss.HasWorldAuth = false;
            Managers.Inst.game.state = Game.State.NetworkClientPlaying;
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            Tick(.01f); Tick(2f);
            Eq(0, CampaignSaveData.Spawns.Count, "no client spawn");
            Eq(0, Save.DogWrites.Count, "no client status writes");
            NetworkBigBoss.HasWorldAuth = true;
            Managers.Inst.game.state = Game.State.Playing;
            Tick(3f);
            Eq(1, CampaignSaveData.Spawns.Count, "host regain recalls");
        });

        Test("Recall faults are contained and recovery resumes", () =>
        {
            Save.dog0 = new Dog.DogStatus { position = Dog.DogPosition.Stolen, land = 0 };
            Save.ThrowDogStatus = true;
            Tick(.01f);
            Eq(0, CampaignSaveData.Spawns.Count, "no spawn on failure");
            Eq(1, KingdomEnhancedPlugin.Instance.LogSource.Warning.Count, "one bounded warning");
            Save.ThrowDogStatus = false;
            Managers.Inst.world = new World(); Tick(6f);
            Eq(1, CampaignSaveData.Spawns.Count, "later trigger recovers");
        });

        Console.WriteLine($"RESULT: {passed} passed, {failed} failed, {assertions} checks");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
