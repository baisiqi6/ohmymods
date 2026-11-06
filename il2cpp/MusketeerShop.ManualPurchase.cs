using System;
using UnityEngine;

namespace KingdomEnhancedMod;

#if !MUSKETEER_SHOP_CORE_ONLY
internal static partial class MusketeerShop
{
    /// <summary>
    /// The rejection boundaries in the real <see cref="OnPay"/> call this exact hop after they
    /// have already proved <c>_payable</c> is the player's completing payable. It lives in this
    /// partial (the one linked by the hold-purchase test project) so the production notification
    /// path stays source-linked. The callee never throws and never mutates wallet/stock/refund.
    /// </summary>
    internal static void NotifyPaymentRejected(Player player)
        => PatchPlayer_HoldPurchase.OnMusketeerPaymentRejected(player, _payable);

    /// <summary>
    /// Read-only identity query for the hold-purchase patch: true only for the exact Payable
    /// this mod currently owns. It deliberately does not consult CanPurchase/CanPlayerPurchase
    /// (purchase eligibility) or AutoRestock classification; callers keep the native CanPay,
    /// CanSelect, forceBlockPayment and save-identity gates separately.
    ///
    /// The static _payable/_owner/_object/_layer fields are the current shop instance. Pool or
    /// scene reuse must not let an old hold inherit a new shop: this checks the payable pointer,
    /// the shop GameObject instance, the owner pointer, the owner/root GameObject relationship
    /// and that the payable still lives under the current world GameLayer. Any read failure is
    /// a conservative false.
    /// </summary>
    internal static bool IsCurrentManualPayable(Payable payable)
    {
        try
        {
            if (payable == null || _clearing || _retiring) return false;

            PayableComponent current = _payable;
            MusketeerShopOwner owner = _owner;
            GameObject root = _object;
            Transform layer = _layer;
            if (current == null || owner == null || root == null || layer == null) return false;
            if (root.Pointer == IntPtr.Zero || layer.Pointer == IntPtr.Zero) return false;
            if (!root.activeInHierarchy || !current.enabled || !payable.enabled) return false;

            if (payable.Pointer == IntPtr.Zero || payable.Pointer != current.Pointer) return false;

            int rootId = root.GetInstanceID();
            GameObject payableObject = payable.gameObject;
            GameObject currentObject = current.gameObject;
            GameObject ownerObject = owner.gameObject;
            if (payableObject == null || payableObject.Pointer != root.Pointer
                || payableObject.GetInstanceID() != rootId) return false;
            if (currentObject == null || currentObject.Pointer != root.Pointer
                || currentObject.GetInstanceID() != rootId) return false;
            if (ownerObject == null || ownerObject.Pointer != root.Pointer
                || ownerObject.GetInstanceID() != rootId) return false;

            if (current._owner == null || current._owner.Pointer != owner.Pointer) return false;

            Transform self = payable.transform;
            if (self == null || !self.IsChildOf(layer)) return false;

            Managers managers = Managers.Inst;
            World world = managers != null ? managers.world : null;
            Transform currentLayer = world != null ? world.gameLayer : null;
            return currentLayer != null && currentLayer.Pointer == layer.Pointer;
        }
        catch
        {
            return false;
        }
    }
}
#endif
