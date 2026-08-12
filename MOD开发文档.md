# 王国：两位君主 Mod 开发文档

## 一、项目概述

本文档详细介绍如何在 **UnityModManager (UMM)** 框架下，使用 **Harmony** 库为游戏《王国：两位君主》开发 Mod。

### 游戏信息
- 游戏名称：Kingdom Two Crowns: Call of Olympus
- Mod 框架：UnityModManager + Harmony v1.2
- 开发语言：C# 5.0

---

## 二、环境配置

### 2.1 需要安装的工具

1. **.NET Framework 4.7.2** (或 4.8)
   - 下载地址：https://dotnet.microsoft.com/download/dotnet-framework/net472

2. **C# 编译器 (csc.exe)**
   - 通常位于：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
   - 或安装 Visual Studio Build Tools

### 2.2 创建 Mod 项目结构

```
Mods/
└── MyMod/
    ├── MyMod.cs          # 主代码文件
    ├── compile_now.bat    # 编译脚本
    └── MyMod.dll         # 编译输出（生成）
```

### 2.3 编译脚本 (compile_now.bat)

```batch
@echo off
pushd E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Call.of.Olympus-P2P\KingdomTwoCrowns_Data\Managed
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /out:E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Call.of.Olympus-P2P\Mods\MyMod\MyMod.dll /reference:UnityEngine.dll /reference:UnityEngine.CoreModule.dll /reference:UnityEngine.Physics2DModule.dll /reference:UnityEngine.IMGUIModule.dll /reference:UnityEngine.PhysicsModule.dll /reference:Assembly-CSharp.dll /reference:UnityModManager\UnityModManager.dll /reference:UnityModManager\0Harmony-1.2.dll /reference:netstandard.dll /reference:System.dll E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Call.of.Olympus-P2P\Mods\MyMod\MyMod.cs
popd
echo Exit code: %ERRORLEVEL%
pause
```

> **注意**：路径需要根据实际游戏安装目录修改。

---

## 三、完整源代码

### 3.1 主代码文件 (MyMod.cs)

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;
using Harmony;
using Coatsink.Common;

namespace MyMod
{
    // ============================================================
    // 第一部分：Mod 主类
    // ============================================================
    public static class Main
    {
        public static bool Enabled { get; private set; }
        public static UnityModManager.ModEntry ModEntry { get; private set; }

        // Mod 设置项
        public static bool infiniteMoney = false;
        public static int speedMultiplier = 2;
        public static bool fastBuild = false;
        public static float mapSizeMultiplier = 1f;
        public static float enemyCountMultiplier = 1f;
        public static float enemyTimelineSpeed = 1f;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            Debug.Log("[MyMod] === Load() started ===");
            ModEntry = modEntry;
            Enabled = modEntry.Active;

            try
            {
                var harmony = HarmonyInstance.Create(modEntry.Info.Id);

                // 1. 商店补丁 - 显示所有世界的商店
                Patch_ShopPlanner.Initialize(harmony);

                // 2. 移动速度补丁
                Patch_Mover.Initialize(harmony);

                // 3. 快速建造补丁
                Patch_Construction.Initialize(harmony);

                // 4. 地图扩展补丁
                Patch_Kingdom.Initialize(harmony);

                // 5. Holder 角色初始化补丁（跨生物群落）
                Patch_Holder.Initialize(harmony);

                // 6. FriendlyTroll 不攻击飞行怪补丁
                Patch_FriendlyTroll.Initialize(harmony);

                // 7. 怪物数量和速度补丁
                Patch_EnemyManager.Initialize(harmony);

                // 8. Berserker 跟随骑士补丁
                Patch_Knight.Initialize(harmony);

                // 9. 共享银行补丁
                Patch_Banker.Initialize(harmony);

                Debug.Log("[MyMod] Harmony patches applied successfully!");
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Failed to apply harmony patches: " + e.ToString());
                return false;
            }

            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            return true;
        }

        public static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            return true;
        }

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("=== 王国两位君主 Mod ===");
            GUILayout.Label("功能：所有世界的兵种商店都会出现");
            GUILayout.Space(10);
            GUILayout.Label("--- 测试功能 ---");

            bool newInfiniteMoney = GUILayout.Toggle(infiniteMoney, "无限金币");
            if (newInfiniteMoney != infiniteMoney)
            {
                infiniteMoney = newInfiniteMoney;
                Wallet.InfiniteMoney = newInfiniteMoney;
            }

            GUILayout.Label("移动速度倍率: " + speedMultiplier + "x");
            speedMultiplier = (int)GUILayout.HorizontalSlider(speedMultiplier, 1, 5);

            bool newFastBuild = GUILayout.Toggle(fastBuild, "快速建造 (约2秒)");
            if (newFastBuild != fastBuild)
            {
                fastBuild = newFastBuild;
                ConstructionBuildingComponent.AllAutoBuild = newFastBuild;
            }

            GUILayout.Label("地图大小: " + mapSizeMultiplier.ToString("F1") + "x");
            mapSizeMultiplier = GUILayout.HorizontalSlider(mapSizeMultiplier, 1f, 5f);

            GUILayout.Label("怪物数量: " + enemyCountMultiplier.ToString("F1") + "x");
            enemyCountMultiplier = GUILayout.HorizontalSlider(enemyCountMultiplier, 1f, 5f);

            GUILayout.Label("怪物出现速度: " + enemyTimelineSpeed.ToString("F1") + "x");
            enemyTimelineSpeed = GUILayout.HorizontalSlider(enemyTimelineSpeed, 1f, 5f);

            GUILayout.Space(10);
            GUILayout.Label("作者：YourName");
        }
    }

    // ============================================================
    // 第二部分：Worker 工匠缩放功能
    // ============================================================

    /// <summary>
    /// Worker 工匠缩放补丁类
    /// 功能：让 Worker 的 localScale 始终保持目标缩放值
    /// 实现方式：挂载 Mover.Update 的 Postfix，每帧检查并修正 Worker 的缩放
    /// </summary>
    public static class Patch_WorkerScale
    {
        // 目标缩放值 - 修改这里可以调整工匠的大小
        // 推荐值：1.05f ~ 1.5f
        private const float TARGET_SCALE = 1.075f;

        public static void Initialize(HarmonyInstance harmony)
        {
            // 挂载 Mover.Update Postfix
            var moverType = typeof(Mover);
            var moverUpdateMethod = moverType.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            if (moverUpdateMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_WorkerScale).GetMethod("MoverUpdate_Postfix"));
                harmony.Patch(moverUpdateMethod, null, postfix);
                Debug.Log("[MyMod] Patched Mover.Update Postfix for Worker scaling");
            }
        }

        /// <summary>
        /// Mover.Update 后置补丁
        /// 每帧检查如果是 Worker，则确保其 localScale 为目标值
        /// </summary>
        public static void MoverUpdate_Postfix(Mover __instance)
        {
            if (!Main.Enabled) return;
            if (__instance == null) return;

            try
            {
                // 检查这个 Mover 是否属于 Worker
                Worker worker = __instance.GetComponent<Worker>();
                if (worker != null)
                {
                    Vector3 currentScale = worker.transform.localScale;

                    // 如果 Y 分量不是目标值，重新设置
                    if (Math.Abs(currentScale.y - TARGET_SCALE) > 0.01f)
                    {
                        float signX = (currentScale.x >= 0) ? 1f : -1f;
                        worker.transform.localScale = new Vector3(signX * TARGET_SCALE, TARGET_SCALE, 1f);
                    }
                }
            }
            catch
            {
                // 忽略错误
            }
        }
    }

    // ============================================================
    // 第三部分：Berserker 狂战士生成功能
    // ============================================================

    /// <summary>
    /// Berserker 生成补丁类
    /// 功能：在希腊世界（BiomeIndex=5）生成 Berserker 狂战士
    /// </summary>
    public static class Patch_Kingdom
    {
        // 追踪已生成的 Kingdom 实例，防止重复生成
        private static readonly System.Collections.Generic.HashSet<int> spawnedKingdomInstances = new System.Collections.Generic.HashSet<int>();

        public static void Initialize(HarmonyInstance harmony)
        {
            var kingdomType = typeof(Kingdom);
            var onLevelLoadedMethod = kingdomType.GetMethod("OnLevelLoaded", BindingFlags.Public | BindingFlags.Instance);
            if (onLevelLoadedMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Kingdom).GetMethod("OnLevelLoaded_Postfix"));
                harmony.Patch(onLevelLoadedMethod, null, postfix);
                Debug.Log("[MyMod] Patched Kingdom.OnLevelLoaded");
            }
        }

        public static void OnLevelLoaded_Postfix(Kingdom __instance)
        {
            if (!Main.Enabled) return;

            try
            {
                // 扩大地图范围
                if (Main.mapSizeMultiplier > 1f)
                {
                    float original = __instance.minKingdomExtents;
                    __instance.minKingdomExtents = original * Main.mapSizeMultiplier;
                }

                int kingdomId = __instance.GetHashCode();
                __instance.StartCoroutine(DelayedBerserkerSpawn(__instance, kingdomId));
            }
            catch (Exception e)
            {
                Debug.LogError("[MyMod] Error in Kingdom patch: " + e.ToString());
            }
        }

        /// <summary>
        /// 延迟生成狂战士，等待 Workers 生成
        /// </summary>
        private static System.Collections.IEnumerator DelayedBerserkerSpawn(Kingdom kingdom, int kingdomId)
        {
            yield return null;
            yield return null;
            yield return null;

            if (spawnedKingdomInstances.Contains(kingdomId))
            {
                yield break;
            }

            // 等待最多 50 帧
            for (int i = 0; i < 50; i++)
            {
                Worker[] workers = UnityEngine.Object.FindObjectsOfType<Worker>();
                if (workers.Length > 0)
                {
                    Debug.Log("[MyMod] Workers found after " + i + " frames");

                    // 生成 Berserkers
                    bool spawned = SpawnBerserkersInGreece(kingdom);
                    if (spawned)
                    {
                        spawnedKingdomInstances.Add(kingdomId);
                    }

                    // 生成 Ninjas
                    bool spawnedNinjas = SpawnNinjasInGreece(kingdom);
                    if (spawnedNinjas)
                    {
                        spawnedKingdomInstances.Add(kingdomId + 1); // 不同的 key
                    }

                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// 在希腊世界生成 Berserkers
        /// </summary>
        private static bool SpawnBerserkersInGreece(Kingdom kingdom)
        {
            // 希腊世界的 BiomeIndex = 5
            if (BiomeHolder.Inst.BiomeIndex != 5)
            {
                return false;
            }

            Debug.Log("[MyMod] Greece biome detected, spawning Berserkers...");

            var existingBerserkers = SingletonMonoBehaviour<Managers>.Inst.kingdom.Berserkers;
            Worker[] workers = UnityEngine.Object.FindObjectsOfType<Worker>();

            int maxBerserkers = 4;
            int maxLeaders = 4;
            int maxTotal = maxBerserkers + maxLeaders;

            int remainingSlots = maxTotal - existingBerserkers.Count;
            if (remainingSlots <= 0) return false;

            int berserkerCount = 0;
            int leaderCount = 0;

            foreach (Worker worker in workers)
            {
                if (worker == null) continue;
                if (berserkerCount + leaderCount >= remainingSlots) break;

                Character character = worker.GetComponent<Character>();
                if (character.CompareTag("Berserker") || character.CompareTag("BerserkerLeader")) continue;

                if (berserkerCount < maxBerserkers)
                {
                    ReplaceWithBerserker(worker, false);
                    berserkerCount++;
                }
                else if (leaderCount < maxLeaders)
                {
                    ReplaceWithBerserker(worker, true);
                    leaderCount++;
                }
            }

            Debug.Log("[MyMod] Spawned " + berserkerCount + " Berserkers and " + leaderCount + " Leaders");
            return berserkerCount > 0 || leaderCount > 0;
        }

        /// <summary>
        /// 在希腊世界生成 Ninjas
        /// </summary>
        private static bool SpawnNinjasInGreece(Kingdom kingdom)
        {
            // 希腊世界的 BiomeIndex = 5
            if (BiomeHolder.Inst.BiomeIndex != 5)
            {
                return false;
            }

            Debug.Log("[MyMod] Greece biome detected, spawning Ninjas...");

            Worker[] workers = UnityEngine.Object.FindObjectsOfType<Worker>();
            int maxNinjas = 10;
            int ninjaCount = 0;

            foreach (Worker worker in workers)
            {
                if (worker == null) continue;
                if (ninjaCount >= maxNinjas) break;

                Character character = worker.GetComponent<Character>();
                if (character.CompareTag("Ninja")) continue;

                ReplaceWithNinja(worker);
                ninjaCount++;
            }

            Debug.Log("[MyMod] Spawned " + ninjaCount + " Ninjas");
            return ninjaCount > 0;
        }

        /// <summary>
        /// 将 Worker 替换为 Berserker
        /// </summary>
        private static bool ReplaceWithBerserker(Worker worker, bool isLeader)
        {
            try
            {
                Vector3 position = worker.transform.position;
                Character berserkerPrefab = null;
                Holder holder = UnityEngine.Object.FindObjectOfType<Holder>();

                if (holder != null)
                {
                    string tag = isLeader ? "BerserkerLeader" : "Berserker";
                    holder.tagCharacterPairs.TryGetValue(tag, out berserkerPrefab);
                }

                if (berserkerPrefab == null) return false;

                worker.gameObject.SetActive(false);
                GameObject berserkerGO = UnityEngine.Object.Instantiate(berserkerPrefab.gameObject, position, Quaternion.identity);
                berserkerGO.SetActive(true);

                Debug.Log("[MyMod] Berserker created at: " + position);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MyMod] Error replacing worker with Berserker: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 将 Worker 替换为 Ninja
        /// </summary>
        private static bool ReplaceWithNinja(Worker worker)
        {
            try
            {
                Vector3 position = worker.transform.position;
                Character ninjaPrefab = null;
                Holder holder = UnityEngine.Object.FindObjectOfType<Holder>();

                if (holder != null)
                {
                    holder.tagCharacterPairs.TryGetValue("Ninja", out ninjaPrefab);
                }

                if (ninjaPrefab == null) return false;

                worker.gameObject.SetActive(false);
                GameObject ninjaGO = UnityEngine.Object.Instantiate(ninjaPrefab.gameObject, position, Quaternion.identity);
                ninjaGO.SetActive(true);

                Debug.Log("[MyMod] Ninja created at: " + position);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MyMod] Error replacing worker with Ninja: " + ex.Message);
                return false;
            }
        }
    }

    // ============================================================
    // 第四部分：FriendlyTroll 不攻击飞行怪
    // ============================================================

    /// <summary>
    /// FriendlyTroll 补丁类
    /// 功能：让 FriendlyTroll 不追逐/攻击 CrownStealer（飞行怪）
    /// 实现方式：使用 Transpiler 修改 MoveToTargetRoutine 方法的 IL 代码
    /// </summary>
    public static class Patch_FriendlyTroll
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var friendlyTrollType = typeof(FriendlyTroll);
            var moveToTargetMethod = friendlyTrollType.GetMethod("MoveToTargetRoutine",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (moveToTargetMethod != null)
            {
                var transpiler = new HarmonyMethod(typeof(Patch_FriendlyTroll).GetMethod("Transpiler_MoveToTargetRoutine"));
                harmony.Patch(moveToTargetMethod, null, null, transpiler);
                Debug.Log("[MyMod] Patched FriendlyTroll.MoveToTargetRoutine with transpiler");
            }
        }

        /// <summary>
        /// Transpiler：修改 MoveToTargetRoutine 的 IL 代码
        /// 在 TryGetComponent 成功后检查是否是 CrownStealer，如果是则跳转到下一个目标
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler_MoveToTargetRoutine(
            IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var instrs = instructions.ToList();
            var result = new List<CodeInstruction>();
            Label continueLabel = il.DefineLabel();
            bool foundContinueTarget = false;

            // 找到 foreach 循环的 continue 跳转目标
            for (int i = 0; i < instrs.Count; i++)
            {
                if (!foundContinueTarget && i < instrs.Count - 1)
                {
                    CodeInstruction instr = instrs[i];
                    if (instr.opcode == OpCodes.Br || instr.opcode == OpCodes.Br_S)
                    {
                        if (instr.operand != null && instr.operand.GetType() == typeof(Label))
                        {
                            Label targetLabel = (Label)instr.operand;
                            for (int j = 0; j < i; j++)
                            {
                                if (instrs[j].labels.Contains(targetLabel))
                                {
                                    instrs[j].labels.Add(continueLabel);
                                    foundContinueTarget = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (!foundContinueTarget && instrs.Count > 0)
            {
                instrs[0].labels.Add(continueLabel);
            }

            // 在 TryGetComponent 成功后注入 CrownStealer 检查
            for (int i = 0; i < instrs.Count; i++)
            {
                CodeInstruction instr = instrs[i];
                result.Add(instr);

                if (i > 0 &&
                    instr.opcode == OpCodes.Stloc_S &&
                    instrs[i - 1].opcode == OpCodes.Callvirt &&
                    instrs[i - 1].operand != null &&
                    instrs[i - 1].operand.ToString().Contains("TryGetComponent"))
                {
                    // 如果 enemy 是 CrownStealer，跳到 continue
                    result.Add(new CodeInstruction(OpCodes.Ldloc_0));
                    result.Add(new CodeInstruction(OpCodes.Isinst, typeof(CrownStealer)));
                    result.Add(new CodeInstruction(OpCodes.Brtrue_S, continueLabel));
                }
            }

            return result;
        }
    }

    // ============================================================
    // 第五部分：其他补丁类
    // ============================================================

    public static class Patch_ShopPlanner
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var shopPlannerType = typeof(ShopPlanner);
            var initMethod = shopPlannerType.GetMethod("InitializeShopTypePrefabPairs",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (initMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_ShopPlanner).GetMethod("Postfix"));
                harmony.Patch(initMethod, null, postfix);
                Debug.Log("[MyMod] Patched ShopPlanner.InitializeShopTypePrefabPairs");
            }
        }

        public static void Postfix(ShopPlanner __instance)
        {
            if (!Main.Enabled) return;

            var shopTypePrefabPairsField = typeof(ShopPlanner).GetField("shopTypePrefabPairs",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (shopTypePrefabPairsField == null) return;

            var shopTypePrefabPairs = shopTypePrefabPairsField.GetValue(__instance)
                as Dictionary<PayableShop.ShopType, GameObject>;
            if (shopTypePrefabPairs == null) return;

            var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings",
                BindingFlags.Public | BindingFlags.Instance);
            if (biomePathStringsField == null) return;

            var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
            if (biomePathStrings == null) return;

            int addedCount = 0;

            for (int i = 0; i < biomePathStrings.Length; i++)
            {
                var path = biomePathStrings[i];
                if (string.IsNullOrEmpty(path)) continue;

                var biomeData = Resources.Load<BiomeData>(path);
                if (biomeData == null) continue;

                var assets = biomeData.biomeSpecificAssets;
                if (assets == null) continue;

                var uniqueShops = assets.uniqueShopPrefabs;
                if (uniqueShops == null) continue;

                foreach (var shopTag in uniqueShops)
                {
                    if (shopTag == null) continue;

                    PayableSidedShop payableSidedShop;
                    PayableWorkshop payableWorkshop;

                    if (shopTag.TryGetComponent<PayableSidedShop>(out payableSidedShop))
                    {
                        var leftType = PayableSidedShop.GetSidedShopType(payableSidedShop.shopType, Side.Left);
                        var rightType = PayableSidedShop.GetSidedShopType(payableSidedShop.shopType, Side.Right);

                        if (!shopTypePrefabPairs.ContainsKey(leftType))
                        {
                            shopTypePrefabPairs.Add(leftType, shopTag.gameObject);
                            addedCount++;
                        }
                        if (!shopTypePrefabPairs.ContainsKey(rightType))
                        {
                            shopTypePrefabPairs.Add(rightType, shopTag.gameObject);
                            addedCount++;
                        }
                    }
                    else if (shopTag.TryGetComponent<PayableWorkshop>(out payableWorkshop))
                    {
                        if (!shopTypePrefabPairs.ContainsKey(PayableShop.ShopType.WorkshopLeft))
                        {
                            shopTypePrefabPairs.Add(PayableShop.ShopType.WorkshopLeft, shopTag.gameObject);
                            addedCount++;
                        }
                        if (!shopTypePrefabPairs.ContainsKey(PayableShop.ShopType.WorkshopRight))
                        {
                            shopTypePrefabPairs.Add(PayableShop.ShopType.WorkshopRight, shopTag.gameObject);
                            addedCount++;
                        }
                    }
                    else
                    {
                        if (!shopTypePrefabPairs.ContainsKey(shopTag.type))
                        {
                            shopTypePrefabPairs.Add(shopTag.type, shopTag.gameObject);
                            addedCount++;
                        }
                    }
                }
            }

            Debug.Log("[MyMod] Shop loading complete, added " + addedCount + " new shops");
        }
    }

    public static class Patch_Mover
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var moverType = typeof(Mover);
            var updateMethod = moverType.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

            if (updateMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Mover).GetMethod("Postfix"));
                harmony.Patch(updateMethod, null, postfix);
            }
        }

        public static void Postfix(Mover __instance)
        {
            if (!Main.Enabled || Main.speedMultiplier <= 1) return;

            try
            {
                var player = __instance.GetComponent<Player>();
                if (player == null) return;

                var moveSpeedField = typeof(Mover).GetField("_moveSpeed",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (moveSpeedField == null) return;

                float moveSpeed = (float)moveSpeedField.GetValue(__instance);
                if (moveSpeed > 0 && moveSpeed < 5f)
                {
                    float newSpeed = Mathf.Min(moveSpeed * Main.speedMultiplier, 15f);
                    moveSpeedField.SetValue(__instance, newSpeed);
                }
            }
            catch { }
        }
    }

    public static class Patch_Construction
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var buildType = typeof(ConstructionBuildingComponent);
            var initBuildMethod = buildType.GetMethod("InitializeBuild",
                BindingFlags.Public | BindingFlags.Instance);

            if (initBuildMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Construction).GetMethod("Prefix"));
                harmony.Patch(initBuildMethod, prefix, null);
            }
        }

        public static void Prefix(ConstructionBuildingComponent __instance)
        {
            if (!Main.Enabled || !Main.fastBuild) return;

            try
            {
                var autoBuildRateField = typeof(ConstructionBuildingComponent)
                    .GetField("_autoBuildRate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (autoBuildRateField != null)
                {
                    autoBuildRateField.SetValue(__instance, 50f);
                }
            }
            catch { }
        }
    }

    public static class Patch_Holder
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var holderType = typeof(Holder);
            var initTagMethod = holderType.GetMethod("InitializeTagCharacterPairs",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (initTagMethod != null)
            {
                var postfix = new HarmonyMethod(typeof(Patch_Holder).GetMethod("Postfix"));
                harmony.Patch(initTagMethod, null, postfix);
                Debug.Log("[MyMod] Patched Holder.InitializeTagCharacterPairs");
            }
        }

        public static void Postfix(Holder __instance)
        {
            if (!Main.Enabled) return;

            var tagCharacterPairsField = typeof(Holder).GetField("tagCharacterPairs",
                BindingFlags.Public | BindingFlags.Instance);
            if (tagCharacterPairsField == null) return;

            var tagCharacterPairs = tagCharacterPairsField.GetValue(__instance)
                as Dictionary<string, Character>;
            if (tagCharacterPairs == null) return;

            var biomePathStringsField = typeof(BiomeHolder).GetField("biomePathStrings",
                BindingFlags.Public | BindingFlags.Instance);
            if (biomePathStringsField == null) return;

            var biomePathStrings = biomePathStringsField.GetValue(BiomeHolder.Inst) as string[];
            if (biomePathStrings == null) return;

            int addedCount = 0;

            for (int i = 0; i < biomePathStrings.Length; i++)
            {
                var path = biomePathStrings[i];
                if (string.IsNullOrEmpty(path)) continue;

                var biomeData = Resources.Load<BiomeData>(path);
                if (biomeData == null) continue;

                var assets = biomeData.biomeSpecificAssets;
                if (assets == null) continue;

                var uniqueChars = assets.uniqueCharacters;
                if (uniqueChars == null) continue;

                foreach (var character in uniqueChars)
                {
                    if (character == null) continue;

                    if (!tagCharacterPairs.ContainsKey(character.tag))
                    {
                        tagCharacterPairs.Add(character.tag, character);
                        addedCount++;
                    }
                }
            }

            Debug.Log("[MyMod] Holder.Postfix: Added " + addedCount + " cross-biome characters");
        }
    }

    public static class Patch_EnemyManager
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var enemyManagerType = typeof(EnemyManager);

            var addEnemiesMethod = enemyManagerType.GetMethod("AddEnemies",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (addEnemiesMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_EnemyManager).GetMethod("AddEnemies_Prefix"));
                harmony.Patch(addEnemiesMethod, prefix, null);
            }

            var getEnemiesMethod = enemyManagerType.GetMethod("GetEnemies",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new Type[] { typeof(Wave), typeof(int), typeof(int), typeof(int), typeof(bool) }, null);
            if (getEnemiesMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_EnemyManager).GetMethod("GetEnemies_Prefix"));
                harmony.Patch(getEnemiesMethod, prefix, null);
            }
        }

        public static bool AddEnemies_Prefix(EnemyType type, AnimationCurve curve, int targetDay,
            ref float multiplier, List<EnemyBlueprint> list, ref string log)
        {
            if (!Main.Enabled || Main.enemyCountMultiplier <= 1f) return true;

            try
            {
                multiplier *= Main.enemyCountMultiplier;
            }
            catch { }

            return true;
        }

        public static bool GetEnemies_Prefix(Wave wave, ref int targetDay, ref int multiplierDay,
            ref int daysOnCurrentIsland, bool logsEnabled)
        {
            if (!Main.Enabled) return true;

            try
            {
                if (Main.enemyTimelineSpeed > 1f)
                {
                    targetDay = Mathf.RoundToInt(targetDay * Main.enemyTimelineSpeed);
                    multiplierDay = Mathf.RoundToInt(multiplierDay * Main.enemyTimelineSpeed);
                    daysOnCurrentIsland = Mathf.RoundToInt(daysOnCurrentIsland * Main.enemyTimelineSpeed);
                }
            }
            catch { }

            return true;
        }
    }

    public static class Patch_Knight
    {
        public static void Initialize(HarmonyInstance harmony)
        {
            var knightType = typeof(Knight);
            var recruitMethod = knightType.GetMethod("TryRecruitAdditionalFollowers",
                BindingFlags.Public | BindingFlags.Instance);

            if (recruitMethod != null)
            {
                var prefix = new HarmonyMethod(typeof(Patch_Knight).GetMethod("TryRecruitAdditionalFollowers_Prefix"));
                harmony.Patch(recruitMethod, prefix, null);
            }
        }

        public static bool TryRecruitAdditionalFollowers_Prefix(Knight __instance, int amount)
        {
            if (!Main.Enabled) return true;

            try
            {
                if (amount < 1) return true;

                List<Berserker> list = new List<Berserker>(
                    SingletonMonoBehaviour<Managers>.Inst.kingdom.Berserkers);
                list.Sort((Berserker a, Berserker b) =>
                    a.transform.position.x.CompareTo(b.transform.position.x));

                int num = 0;
                int num2 = 0;
                while (num2 < list.Count && num < amount)
                {
                    if (list[num2].IsAvailableForJob() && list[num2].TryRecruit(__instance))
                    {
                        num++;
                        var field = typeof(Knight).GetField("_additionalFollowers",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            var additionalFollowers = field.GetValue(__instance) as List<Berserker>;
                            if (additionalFollowers != null)
                            {
                                additionalFollowers.Add(list[num2]);
                            }
                        }
                    }
                    num2++;
                }
            }
            catch { }

            return false;
        }
    }

    public static class Patch_Banker
    {
        private const string SHARED_STASH_KEY = "MyMod_SharedBankStash";
        private static int sharedStash = -1;

        public static void Initialize(HarmonyInstance harmony)
        {
            var bankerType = typeof(Banker);

            var methods = new[]
            {
                ("FinaliseEmerge", "FinaliseEmerge_Postfix"),
                ("HandleOnDayStart", "HandleOnDayStart_Postfix"),
                ("DropOff", "DropOff_Postfix"),
                ("Hide", "Hide_Postfix"),
                ("Payout", "Payout_Postfix"),
                ("OpenCastleDoor", "OpenCastleDoor_Postfix")
            };

            foreach (var (methodName, postfixName) in methods)
            {
                var method = bankerType.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    var postfix = new HarmonyMethod(typeof(Patch_Banker).GetMethod(postfixName));
                    harmony.Patch(method, null, postfix);
                }
            }

            Debug.Log("[MyMod] Patched Banker methods");
        }

        private static void EnsureLoaded()
        {
            if (sharedStash < 0)
            {
                sharedStash = PlayerPrefs.HasKey(SHARED_STASH_KEY)
                    ? PlayerPrefs.GetInt(SHARED_STASH_KEY)
                    : 500;
            }
        }

        private static void SaveSharedStash(int value)
        {
            sharedStash = value;
            PlayerPrefs.SetInt(SHARED_STASH_KEY, value);
            PlayerPrefs.Save();
        }

        public static void FinaliseEmerge_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            EnsureLoaded();
            var stashField = typeof(Banker).GetField("_stashedCoins",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stashField != null)
            {
                stashField.SetValue(__instance, sharedStash);
            }
        }

        public static void HandleOnDayStart_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            EnsureLoaded();
            var stashField = typeof(Banker).GetField("_stashedCoins",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stashField != null)
            {
                stashField.SetValue(__instance, sharedStash);
            }
        }

        public static void DropOff_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            EnsureLoaded();
            var stashField = typeof(Banker).GetField("_stashedCoins",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stashField != null)
            {
                int currentStash = (int)stashField.GetValue(__instance);
                if (currentStash > sharedStash)
                {
                    SaveSharedStash(currentStash);
                }
            }
        }

        public static void Hide_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            EnsureLoaded();
            var stashField = typeof(Banker).GetField("_stashedCoins",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stashField != null)
            {
                int currentStash = (int)stashField.GetValue(__instance);
                if (currentStash > sharedStash)
                {
                    SaveSharedStash(currentStash);
                }
            }
        }

        public static void Payout_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            EnsureLoaded();
            var stashField = typeof(Banker).GetField("_stashedCoins",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stashField != null)
            {
                int currentStash = (int)stashField.GetValue(__instance);
                SaveSharedStash(currentStash);
            }
        }

        public static void OpenCastleDoor_Postfix(Banker __instance)
        {
            if (!Main.Enabled) return;
            EnsureLoaded();
            var stashField = typeof(Banker).GetField("_stashedCoins",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (stashField != null)
            {
                stashField.SetValue(__instance, sharedStash);
                Castle castle = SingletonMonoBehaviour<Managers>.Inst.kingdom.castle;
                if (castle != null)
                {
                    castle.SetStash(sharedStash);
                }
            }
        }
    }
}
```

---

## 四、功能说明

### 4.1 Worker 工匠缩放

**原理**：
- 游戏代码中 `Worker.Behaviour()` 协程会不断重置 `localScale.y = 1f`
- 之前的每帧更新方案效率低
- 最终方案：挂载 `Mover.Update` 的 Postfix（每帧调用），检查并修正 Worker 的缩放

**关键代码**：
```csharp
// TARGET_SCALE 控制缩放倍数
private const float TARGET_SCALE = 1.075f;

public static void MoverUpdate_Postfix(Mover __instance)
{
    Worker worker = __instance.GetComponent<Worker>();
    if (worker != null)
    {
        Vector3 currentScale = worker.transform.localScale;
        if (Math.Abs(currentScale.y - TARGET_SCALE) > 0.01f)
        {
            float signX = (currentScale.x >= 0) ? 1f : -1f;
            worker.transform.localScale = new Vector3(signX * TARGET_SCALE, TARGET_SCALE, 1f);
        }
    }
}
```

**修改缩放值**：修改 `TARGET_SCALE` 常量即可
- 推荐值：1.05f ~ 1.5f

---

### 4.2 FriendlyTroll 不攻击飞行怪

**原理**：
- 使用 Harmony Transpiler 修改 `FriendlyTroll.MoveToTargetRoutine` 的 IL 代码
- 在 foreach 循环中，当 `TryGetComponent` 成功后，检查目标是否是 `CrownStealer`
- 如果是，跳到下一个目标（continue）

**关键代码**：
```csharp
// 注入检查：如果是 CrownStealer，跳到 continue
result.Add(new CodeInstruction(OpCodes.Ldloc_0));
result.Add(new CodeInstruction(OpCodes.Isinst, typeof(CrownStealer)));
result.Add(new CodeInstruction(OpCodes.Brtrue_S, continueLabel));
```

---

### 4.3 Berserker 狂战士生成

**原理**：
- 在希腊世界（BiomeIndex=5）的 `OnLevelLoaded` 时触发
- 查找所有 Worker，将部分替换为 Berserker
- 使用 `Holder.tagCharacterPairs` 获取 Berserker 的 prefab

**关键代码**：
```csharp
// BiomeIndex = 5 是希腊世界
if (BiomeHolder.Inst.BiomeIndex != 5) return false;

// 从 Holder 获取 Berserker prefab
holder.tagCharacterPairs.TryGetValue("Berserker", out berserkerPrefab);

// 禁用原 Worker，创建新的 Berserker
worker.gameObject.SetActive(false);
GameObject berserkerGO = Instantiate(berserkerPrefab.gameObject, position, Quaternion.identity);
```

---

### 4.4 Ninja 忍者生成

**原理**：
- 与 Berserker 类似，但 Ninja 在游戏中的生成逻辑是通过拾取特定道具触发
- 我们直接用代码将 Worker 替换为 Ninja

**关键代码**：
```csharp
holder.tagCharacterPairs.TryGetValue("Ninja", out ninjaPrefab);
```

---

### 4.5 共享银行

**原理**：
- 拦截 Banker 的多个方法（FinaliseEmerge、HandleOnDayStart、DropOff 等）
- 使用 PlayerPrefs 持久化存储共享存款
- 所有岛屿共用一个银行余额

**关键代码**：
```csharp
// PlayerPrefs 存储
PlayerPrefs.SetInt("MyMod_SharedBankStash", value);
PlayerPrefs.Save();

// 加载时
sharedStash = PlayerPrefs.HasKey(SHARED_STASH_KEY)
    ? PlayerPrefs.GetInt(SHARED_STASH_KEY)
    : 500;
```

---

## 五、编译和运行

### 5.1 编译步骤

1. 确保 `compile_now.bat` 中的路径正确
2. 双击运行 `compile_now.bat`
3. 如果看到 `Exit code: 0` 和 "编译成功" 提示，说明编译成功
4. 生成的 `MyMod.dll` 会在 `Mods/MyMod/` 目录下

### 5.2 安装 Mod

1. 确保游戏目录有 `UnityModManager` 目录
2. 将编译好的 `MyMod.dll` 放到游戏的 `Mods/MyMod/` 目录
3. 启动游戏，在 UnityModManager 中启用 Mod

### 5.3 调试

- 游戏日志位置：`C:\Users\<用户名>\AppData\LocalLow\noio\KingdomTwoCrowns\Player.log`
- 使用 `Debug.Log()` 输出调试信息
- 日志中搜索 `[MyMod]` 查看 Mod 的调试信息

---

## 六、常见问题

### Q1: 编译报错 "Label 类型不支持 as 操作符"
A: Label 是结构体，不能用 `as`。改用：
```csharp
if (instr.operand != null && instr.operand.GetType() == typeof(Label))
{
    Label targetLabel = (Label)instr.operand;
}
```

### Q2: Transpiler 获取的方法指令数太少（只有 6 条）
A: 可能是获取了协程的错误定义。协程的 IL 结构特殊，可以尝试：
- 使用基类的方法
- 使用 Prefix/Postfix 替代
- 挂载 Update/LateUpdate 等每帧调用的方法

### Q3: 每帧调用 FindObjectsOfType 卡顿
A: 尽量避免每帧调用。可以：
- 挂载游戏已有的每帧方法（Update、LateUpdate 等）
- 用标志位控制更新频率
- 批量处理替代逐个处理

### Q4: 游戏代码重置了我们设置的 localScale
A: 找到重置 localScale 的代码位置，用 Transpiler 修改 IL，或者在重置之后执行的时机用 Postfix 修正。

---

## 七、开发技巧

### 7.1 获取游戏类的私有方法

```csharp
var method = typeof(GameClass).GetMethod("MethodName",
    BindingFlags.NonPublic | BindingFlags.Instance);
```

### 7.2 获取游戏类的私有字段

```csharp
var field = typeof(GameClass).GetField("_fieldName",
    BindingFlags.NonPublic | BindingFlags.Instance);
```

### 7.3 使用 Harmony 挂载补丁

```csharp
// Prefix - 在原方法之前执行
harmony.Patch(method, new HarmonyMethod(typeof(MyPatch).GetMethod("Prefix")), null);

// Postfix - 在原方法之后执行
harmony.Patch(method, null, new HarmonyMethod(typeof(MyPatch).GetMethod("Postfix")));

// Transpiler - 修改 IL 代码
harmony.Patch(method, null, null, new HarmonyMethod(typeof(MyPatch).GetMethod("Transpiler")));
```

### 7.4 C# 5.0 限制

- 不支持 `?.` 空值检查操作符
- 不支持 ` nameof()`
- 不支持默认接口方法
- 使用 `if (obj != null && obj.GetType() == typeof(Type))` 替代 `is Type`

---

## 八、版本信息

- 文档版本：1.0
- 创建日期：2026-04-07
- 游戏版本：Kingdom Two Crowns: Call of Olympus
- Mod 框架：UnityModManager + Harmony v1.2
