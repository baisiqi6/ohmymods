// Host-only adapters for APIs added after the original single-island probe.
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public class Animator : Behaviour { }
    public class Animation : Behaviour { }
}
namespace UnityEngine.Events
{
    public sealed class UnityAction
    {
        private readonly Action _action;
        private UnityAction(Action action) { _action = action; }
        public static explicit operator UnityAction(Action action) => new UnityAction(action);
        public void Invoke() => _action();
    }
}
namespace KingdomEnhancedMod
{
    public static partial class ExtensionIslandMap
    {
        public static readonly Dictionary<int, object> WorldTokens = new Dictionary<int, object>();
        public static readonly Dictionary<int, object> DetailTokens = new Dictionary<int, object>();
        public static bool TryGetVisualOwnerTokenForLand(int physical, out object token)
        {
            if (WorldTokens.TryGetValue(physical, out token)) return token != null;
            token = physical == 11 ? VisualOwnerToken : null;
            return token != null;
        }
        public static bool TryGetDetailTokenForLand(int physical, out object token)
        {
            if (DetailTokens.TryGetValue(physical, out token)) return token != null;
            token = physical == 11 ? VisualOwnerToken : null;
            return token != null;
        }
        public static bool IsActiveMenu(MapTimelineMenuGreece menu)
            => IsVisualOwnerMenu(menu) && Menu.Inst != null && ReferenceEquals(Menu.Inst.ActiveMap, menu);
        public static bool TryGetLiveTuple(object token, UILand land, MapTimelineMenuGreece menu, UIMainMap map, out object tuple)
        {
            tuple = null;
            if (!IsActiveMenu(menu) || !TryGetPhysicalLandIndex(land, out int physical) ||
                !TryGetDetailTokenForLand(physical, out object actual) || !ReferenceEquals(token, actual)) return false;
            tuple = token;
            return true;
        }
    }
}
