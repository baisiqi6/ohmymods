using System;
#if ANDROID
using Il2CppCoatsink.Common;
#else
using Coatsink.Common;
#endif
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
///     GetCurrency/SetCurrency）。2.4.0 r23488 原生静态核验确认 TotalCapacity 参与
///     TryToGrabCurrency/SuckCurrency/SetCurrency 的容量限制；实际收币/掉币及读档仍须实机验收。
///     interop 是包装器，原生证据见 docs/project-harness/tasks/issue-247/verification.md。
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
        if (!GreekScaleScope.IsEnabledAllWorldsActive) return;
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
        if (!GreekScaleScope.IsEnabledAllWorldsActive) return;
        CurrencyBagViewport.KeepVisible(__instance);
    }

    /// <summary>开局强制解锁 Hermes 钱袋（两名玩家）。</summary>
    [HarmonyPatch(typeof(CurrencyBagHandler), nameof(CurrencyBagHandler.OnGameStartHandler))]
    [HarmonyPostfix]
    public static void OnGameStartHandler_Postfix(CurrencyBagHandler __instance)
    {
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return;
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
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return;
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
    /// 视觉堆叠参数：nthCoin &lt; 600（原版 300）；不等于实际钱包容量或已验证掉币阈值。
    /// 2.4.0 由 Reset 改名 ResetVisuals，新增 backLayer 参数（不参与逻辑）。
    /// </summary>
    [HarmonyPatch(typeof(BagCurrency), nameof(BagCurrency.ResetVisuals))]
    [HarmonyPrefix]
    public static void ResetVisuals_Prefix(BagCurrency __instance, bool backLayer, int nthCoin, ref bool stack)
    {
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return;
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
        try
        {
            // Register the wallet's desired scale even while disabled/unknown; the
            // scope replay writes it once the switch becomes active and capacity
            // work below stays gated on the enabled switch.
            ApplyBagScale(__instance);
            EnsureWalletCapacity();
        }
        catch (Exception e)
        {
            KingdomEnhancedPlugin.Instance?.LogSource.LogError(e);
        }
    }

    /// <summary>
    /// 在 Awake 时尽力补齐已建立的玩家钱包；管理器/钱包未就绪时不写。
    /// 这不证明覆盖了全部恢复时序；最终容量须在原生加载完成后独立核验。
    /// </summary>
    private static void EnsureWalletCapacity()
    {
        if (ModConfig.Enabled == null || !ModConfig.Enabled.Value) return;
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
        Vector3 nativeRoot = GreekScaleScope.NativeScale(bag.transform);
        if (!Finite(nativeRoot)) return;
        // XY only: Z stays authored by the native prefab at every level.
        GreekScaleScope.ApplyScaleXyAllWorlds(bag.transform,
            new Vector3(nativeRoot.x * BagScaleMultiplier, nativeRoot.y * BagScaleMultiplier, nativeRoot.z));

        Transform container = bag._container;
        if (container == null) return;
        Vector3 nativeContainer = GreekScaleScope.NativeScale(container);
        if (!Finite(nativeContainer)) return;
        GreekScaleScope.ApplyScaleXyAllWorlds(container,
            new Vector3(nativeContainer.x / BagScaleMultiplier, nativeContainer.y / BagScaleMultiplier,
                nativeContainer.z));
    }

    private static void ApplyCoinScale(BagCurrency coin)
    {
        if (coin == null) return;
        CurrencyBag bag = coin.bag;
        Transform container = bag != null ? bag._container : null;
        // No scene object/resource -> never guess a transform.
        if (container == null) return;

        Vector3 actualContainer = container.localScale;
        Vector3 nativeContainer = GreekScaleScope.NativeScale(container);
        if (!Finite(actualContainer) || !Finite(nativeContainer)) return;

        // Claim only what this mod's own parent scale caused. Without an owned
        // parent discrepancy an existing coin is left exactly as authored.
        bool ownsParentDifference = false;
        for (int axis = 0; axis < 3; axis++)
        {
            if (nativeContainer[axis] == 0f) return; // zero denominator: no guess
            ownsParentDifference |= nativeContainer[axis] != actualContainer[axis];
        }
        if (!ownsParentDifference) return;

        var currency = Managers.Inst != null ? Managers.Inst.currency : null;
        if (currency == null
            || !currency.TryGetData(coin.CurrencyType, out CurrencyConfig config)
            || config == null || config.BagPrefab == null) return;
        BagCurrency prefab = BiomeData.GetPrefabSwap(config.BagPrefab);
        if (prefab == null) return;

        Vector3 prefabScale = prefab.transform.localScale;
        Vector3 incoming = coin.transform.localScale;
        if (!Finite(prefabScale) || !Finite(incoming)) return;

        // SpawnCurrency instantiates under the bag, then reparents to _container
        // with worldPositionStays. Only when the incoming local scale proves it
        // inherited our parent difference do we adopt the native composition.
        Vector3 nativeCoin = default;
        for (int axis = 0; axis < 3; axis++)
        {
            if (actualContainer[axis] == 0f) return;
            float expectedIncoming = prefabScale[axis] / actualContainer[axis];
            if (!float.IsFinite(expectedIncoming)
                || !Mathf.Approximately(incoming[axis], expectedIncoming)) return;
            nativeCoin[axis] = prefabScale[axis] / nativeContainer[axis];
            if (!float.IsFinite(nativeCoin[axis])) return;
        }
        // Desired equals this proof's native composition, so the final world scale
        // stays nativeRoot * swapped prefab. The correction owns nothing afterwards.
        GreekScaleScope.ApplyOneShotAllWorlds(coin.transform, nativeCoin, nativeCoin);
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}
