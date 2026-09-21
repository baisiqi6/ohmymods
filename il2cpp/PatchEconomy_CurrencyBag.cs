using System;
using Coatsink.Common;
using UnityEngine;
using HarmonyLib;

namespace KingdomEnhancedMod;

/// <summary>
/// 赫尔墨斯钱袋（CurrencyBagType.Hermes）解锁 + 扩容 + UI 视觉适配。
/// 迁移自 Mono Patch_CurrencyBag.cs（UMM + Harmony 1.2）。
///
/// 2.4.0 签名验证结果（get_type_members.py 核对 interop Assembly-CSharp.dll）：
///   - CurrencyBagHandler.OnGameStartHandler(): private void —— 存在，签名一致。
///   - CurrencyBagHandler.ChangeCurrencyBag(CurrencyBagType, int): public void —— 存在，签名一致。
///   - CurrencyBagHandler.SetCurrencyBag(CurrencyBagType, int): public CurrencyBag —— 存在，签名一致（返回 CurrencyBag，postfix 用 __result）。
///   - CurrencyBagType 枚举：{ Bag, Hermes, EggBasket } —— 存在，新增 EggBasket（鸡蛋篮子，2.4.0 新增）。
///   - CurrencyBag.Awake(): private void —— 存在，签名一致。
///   - CurrencyBag._container: Transform 字段 —— 存在（interop 暴露为 public 属性，替代 Mono 反射）。
///   - Wallet.TotalCapacity: int 字段 —— 存在（interop 暴露为 public 属性）。
///   - Player.wallet: Wallet 属性 —— 存在；Kingdom.playerOne/playerTwo: Player —— 存在。
///
/// 版本漂移（重要）：
///   - BagCurrency.Reset(int nthCoin, bool stack) → 2.4.0 改名 + 加参：
///     ResetVisuals(bool backLayer, int nthCoin, bool stack = true)。本 patch 改为 Hook ResetVisuals。
///   - 2.4.0 钱包重做为多币种 CurrencyMap（Wallet._currencyAmount / _allowedCurrencies /
///     GetCurrency/SetCurrency）。TotalCapacity 字段仍存在但语义作用未经运行时验证（IL2CPP 方法体不可读），
///     扩容效果待 Operator 决策（见 notes-economy.md）。
/// </summary>
[HarmonyPatch(typeof(CurrencyBag))]
public static class PatchEconomy_CurrencyBag
{
    private const int HermesCapacity = 2000;
    private const int BagCapacity = 1000;
    private const int VisualCoinLimit = 600;          // 原版硬编码 300
    private const float BagScaleMultiplier = 2.0f;    // 钱袋 UI 放大倍率
    private const float BagPositionOffsetX = 3.70f;   // 钱袋 UI 向屏幕右侧微移
    private const float BagPositionOffsetY = -1.50f;  // 钱袋 UI 向屏幕下方微移

    /// <summary>
    /// 原生每次重算位置后施加固定视觉偏移。由于原生 RecalcPosition 会先覆盖基准位置，
    /// 此处不会累积漂移，也不触碰金币容器、物理或钱包容量。
    /// </summary>
    [HarmonyPatch(typeof(CurrencyBag), nameof(CurrencyBag.RecalcPosition))]
    [HarmonyPostfix]
    public static void RecalcPosition_Postfix(CurrencyBag __instance)
    {
        if (!GreekScaleScope.IsActive) return;
        try
        {
            Vector3 position = __instance.transform.position;
            position.x += BagPositionOffsetX;
            position.y += BagPositionOffsetY;
            __instance.transform.position = position;
            CurrencyBagViewport.KeepVisible(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>显示开始时校正视口边界，不重算原生位置或重复施加固定偏移。</summary>
    [HarmonyPatch(typeof(CurrencyBag), nameof(CurrencyBag.StartShow))]
    [HarmonyPostfix]
    public static void StartShow_Postfix(CurrencyBag __instance)
    {
        if (!GreekScaleScope.IsActive) return;
        CurrencyBagViewport.KeepVisible(__instance);
    }

    /// <summary>开局强制解锁 Hermes 钱袋（两名玩家）。</summary>
    [HarmonyPatch(typeof(CurrencyBagHandler), nameof(CurrencyBagHandler.OnGameStartHandler))]
    [HarmonyPostfix]
    public static void OnGameStartHandler_Postfix(CurrencyBagHandler __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            __instance.ChangeCurrencyBag(CurrencyBagType.Hermes, 0);
            __instance.ChangeCurrencyBag(CurrencyBagType.Hermes, 1);
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo("[Economy] Hermes currency bag unlocked");
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>钱袋切换后按类型设置对应玩家钱包容量（幂等，每局重设）。</summary>
    [HarmonyPatch(typeof(CurrencyBagHandler), nameof(CurrencyBagHandler.ChangeCurrencyBag))]
    [HarmonyPostfix]
    public static void ChangeCurrencyBag_Postfix(CurrencyBagType currencyBagType, int playerIndex)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            int capacity = (currencyBagType == CurrencyBagType.Hermes) ? HermesCapacity : BagCapacity;
            var kingdom = SingletonMonoBehaviour<Managers>.Inst.kingdom;
            if (kingdom == null) return;

            Player player = (playerIndex == 0) ? kingdom.playerOne : kingdom.playerTwo;
            if (player != null && player.wallet != null)
            {
                player.wallet.TotalCapacity = capacity;
                KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(
                    "[Economy] Player" + (playerIndex + 1) + " wallet capacity = " + capacity + " (" + currencyBagType + ")");
            }
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 视觉堆叠上限：nthCoin &lt; 600 堆叠显示，超出散落（原版 300）。
    /// 2.4.0 由 Reset 改名 ResetVisuals，新增 backLayer 参数（不参与逻辑）。
    /// </summary>
    [HarmonyPatch(typeof(BagCurrency), nameof(BagCurrency.ResetVisuals))]
    [HarmonyPrefix]
    public static void ResetVisuals_Prefix(BagCurrency __instance, bool backLayer, int nthCoin, ref bool stack)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            stack = nthCoin < VisualCoinLimit;
            ApplyCoinScale(__instance);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>钱袋 UI 放大 + 容量保障（读档恢复的钱袋不经过 ChangeCurrencyBag，在此补齐）。</summary>
    [HarmonyPatch(typeof(CurrencyBag), nameof(CurrencyBag.Awake))]
    [HarmonyPostfix]
    public static void Awake_Postfix(CurrencyBag __instance)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            ApplyBagScale(__instance);
            EnsureWalletCapacity();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 读档恢复的钱袋不触发 ChangeCurrencyBag/OnGameStartHandler（那些是新游戏开局流程），
    /// 容量保持存档原值 → "钱袋变大没生效"。此处按钱袋 Awake 时机补齐（mod 目标即大容量钱包）。
    /// </summary>
    private static void EnsureWalletCapacity()
    {
        var kingdom = SingletonMonoBehaviour<Managers>.Inst?.kingdom;
        if (kingdom == null) return;
        if (kingdom.playerOne != null && kingdom.playerOne.wallet != null)
            kingdom.playerOne.wallet.TotalCapacity = HermesCapacity;
        if (kingdom.playerTwo != null && kingdom.playerTwo.wallet != null)
            kingdom.playerTwo.wallet.TotalCapacity = HermesCapacity;
        KingdomEnhancedPlugin.Instance?.LogSource.LogDebug(
            "[Economy] Wallet capacity ensured = " + HermesCapacity + " (save-load path)");
    }

    /// <summary>每次切换钱袋后重设 scale（防 Awake 设置被显示流程覆盖；幂等）。</summary>
    [HarmonyPatch(typeof(CurrencyBagHandler), nameof(CurrencyBagHandler.SetCurrencyBag))]
    [HarmonyPostfix]
    public static void SetCurrencyBag_Postfix(CurrencyBag __result)
    {
        if (!ModConfig.Enabled.Value) return;
        try
        {
            ApplyBagScale(__result);
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 每实例从本 mod 缩放前的向量计算目标，重复调用不会累乘或跨钱袋污染。
    /// 金币容器（_container）反向缩放 1/倍率——抵消父级继承，金币保持原大小。
    /// 2.4.0：_container 字段由 interop 暴露为 public 属性，直接访问替代 Mono 反射。
    /// </summary>
    private static void ApplyBagScale(CurrencyBag bag)
    {
        if (bag == null) return;
        Vector3 s = GreekScaleScope.NativeScale(bag.transform);
        s.x *= BagScaleMultiplier;
        s.y *= BagScaleMultiplier;
        GreekScaleScope.ApplyScale(bag.transform, s);

        Transform container = bag._container;
        if (container != null)
        {
            GreekScaleScope.ApplyScale(container,
                new Vector3(1f / BagScaleMultiplier, 1f / BagScaleMultiplier, 1f));
        }
    }

    private static void ApplyCoinScale(BagCurrency coin)
    {
        if (coin == null) return;
        CurrencyBag bag = coin.bag;
        Transform container = bag != null ? bag._container : null;
        if (container == null)
        {
            GreekScaleScope.ApplyScale(coin.transform, Vector3.one);
            return;
        }
        Vector3 nativeContainer = GreekScaleScope.NativeScale(container);
        Vector3 actualContainer = container.localScale;
        if (nativeContainer.x == actualContainer.x && nativeContainer.y == actualContainer.y
            && nativeContainer.z == actualContainer.z)
        {
            // No parent scale owned by this mod: preserve this instance's native
            // or third-party scale instead of replacing it with a prefab guess.
            GreekScaleScope.ApplyScale(coin.transform, Vector3.one);
            return;
        }
        var currency = Managers.Inst != null ? Managers.Inst.currency : null;
        if (currency == null || BiomeHolder.Inst == null
            || !currency.TryGetData(coin.CurrencyType, out CurrencyConfig config)
            || config == null || config.BagPrefab == null) return;
        BagCurrency prefab = BiomeData.GetPrefabSwap(config.BagPrefab);
        if (prefab == null) return;

        // SpawnCurrency instantiates under the bag, then reparents to _container
        // with worldPositionStays. Our enlarged parent can already have doubled
        // this incoming local scale, so it is not a valid native-size snapshot.
        Vector3 nativeCoin = prefab.transform.localScale;
        Vector3 incoming = coin.transform.localScale;
        bool matchesInheritedScale = true;
        for (int axis = 0; axis < 3; axis++)
        {
            if (!float.IsFinite(nativeContainer[axis]) || nativeContainer[axis] == 0f
                || !float.IsFinite(actualContainer[axis]) || actualContainer[axis] == 0f) return;
            float expectedIncoming = nativeCoin[axis] / actualContainer[axis];
            matchesInheritedScale &= Mathf.Approximately(incoming[axis], expectedIncoming);
            nativeCoin[axis] /= nativeContainer[axis];
            if (!float.IsFinite(nativeCoin[axis])) return;
        }
        if (matchesInheritedScale)
            // The parent discrepancy proves our contribution. Record even during
            // loading so a later foreign-world transition can undo that inheritance.
            GreekScaleScope.ApplyInheritedScale(coin.transform, Vector3.one, nativeCoin);
        else
            GreekScaleScope.ApplyScale(coin.transform, Vector3.one);
    }
}
