using System;
using HarmonyLib;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// 狗跨岛召回：原生 TryApplyDog 只在"狗状态记录的 land == 当前岛"时生成狗
/// （CampaignSaveData.TryApplyDog 的 land/position 双早退）。狗不会死亡
/// （Dog.cs 无死亡路径），但玩家换岛后狗会留在旧岛——当前岛从此没有狗。
/// 本补丁在 TryApplyDog 因 land 不匹配跳过时把狗召回当前岛：
/// SetDogStatus(Roaming, CurrentLand) 后按原生同款路径生成（holder 预制件
/// + SpawnNearP1 + SetupDog + 颜色）。Stolen（被偷）状态不召回——那是
/// 原生玩法状态，等待原生恢复路径；Locked（尚未解锁）同样不动。
///
/// 2.4.0 签名验证（interop）：CampaignSaveData.TryApplyDog(Dog.DogStatus, int)
/// 为 private 实例方法（Harmony 可挂）；SetDogStatus/GetDogStatus/CurrentLand/
/// SpawnNearP1&lt;T&gt;/Holder.dogPrefab/wolfPupPrefab/Dog.SetupDog 均在 2.1 参考
/// 与 interop 一致（构建门核验）。
/// </summary>
[HarmonyPatch(typeof(CampaignSaveData))]
public static class PatchRoles_DogRecall
{

    [HarmonyPatch("TryApplyDog")]
    [HarmonyPostfix]
    private static void TryApplyDog_Postfix(CampaignSaveData __instance, Dog.DogStatus dogStatus, int dogId)
    {
        try
        {
            if (!ModConfig.Enabled.Value || !NetworkBigBoss.HasWorldAuth) return;

            // 原生已处理（同岛且 Roaming）或尚未拥有（Locked）/被偷（Stolen）：不介入。
            if (dogStatus.land == __instance.CurrentLand) return;
            if (dogStatus.position != Dog.DogPosition.Roaming) return;

            // 狗在别的岛且处于 Roaming：召回至当前岛。
            // interop 差异：可选参数 Color?/DogType? 暴露为 Il2CppSystem.Nullable<T>（先例 PatchRoles_Castle.cs:122）。
            __instance.SetDogStatus(Dog.DogPosition.Roaming, __instance.CurrentLand,
                new Il2CppSystem.Nullable<UnityEngine.Color>(dogStatus.color),
                new Il2CppSystem.Nullable<Dog.DogType>(dogStatus.type),
                dogStatus.preferedPlayer, dogId);

            // 复审 P1 守卫：窄角场景（GreedQueen 类 CanEmbarkDogs=false 的出航组）会把滞留狗
            // 留在岛快照里，回岛时 TryPopObjectsToScene 先还原狗 GO——此时状态对齐即可、
            // 不得再生成第二条（同 dogId 双注册 RPC 922/964）。语义对齐 TryApplyHermit 的同款守卫。
            var kingdom = Managers.Inst != null ? Managers.Inst.kingdom : null;
            var dogs = kingdom != null ? kingdom.dogs : null;
            if (dogs != null)
            {
                for (int i = 0; i < dogs.Count; i++)
                {
                    Dog d = dogs[i];
                    if (d != null && d.gameObject != null && d.gameObject.activeInHierarchy && d.dogId == dogId)
                    {
                        KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                            "[DogRecall] dog " + dogId + " already present on this land (snapshot-restored); status aligned only");
                        return;
                    }
                }
            }

            var holder = Managers.Inst != null ? Managers.Inst.holder : null;
            if (holder == null || holder.dogPrefab == null) return;
            Dog dog = CampaignSaveData.SpawnNearP1<Dog>(
                dogStatus.type == Dog.DogType.Dog ? holder.dogPrefab : holder.wolfPupPrefab,
                1, CampaignSaveData.CarryForwardToolType.None);
            if (dog == null) return;
            dog.SetupDog(dogId);
            dog.color = dogStatus.color;

            // 复审 P2-a：每次召回各记一条（频率至多每岛换一次，无需 once 节流）。
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                "[DogRecall] dog " + dogId + " recalled to current land " + __instance.CurrentLand +
                " (was on land " + dogStatus.land + ")");
        }
        catch (Exception e)
        {
            try { KingdomEnhancedPlugin.Instance?.LogSource.LogError("[DogRecall] " + e); } catch { }
        }
    }
}
