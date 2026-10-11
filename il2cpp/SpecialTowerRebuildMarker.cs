using System;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Marker added to supported special-tower prefab assets before their CRPC
/// components are enumerated.  It deliberately carries no state and implements
/// neither IRPCable nor Persistent.IBehaviour; the native PayableUpgrade added
/// beside it owns the original payment, networking and save-data lifecycle.
/// </summary>
public sealed class SpecialTowerRebuildMarker : MonoBehaviour
{
    public SpecialTowerRebuildMarker(IntPtr pointer) : base(pointer)
    {
    }
}

