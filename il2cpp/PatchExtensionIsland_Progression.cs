using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.Runtime;

namespace KingdomEnhancedMod;

/// <summary>
/// 主线进度窄防护（native-travel/PROGRESSION-GUARD-CONTRACT.md，ABI 修订版）：
/// 附加希腊岛（物理 land11）正常返航时，原生 <c>CampaignSaveData.UnlockNextLand()</c> 会把
/// furthestUnlockedLand 推到 <c>min(currentLand + 1, maxIslands - 1) == 10</c>，并让
/// <c>Game._Run</c> 随后请求 <c>IslandSaveData.TryDelete(campaign, 10, challengeId)</c>。
///
/// 实现是**单一目标的 native ABI adapter**，不是 Harmony detour：
/// - 原生函数按 8 字节 Nullable&lt;int&gt; bits 单寄存器返回（布局 {hasValue@byte0, value@dword1}）；
/// - detour delegate 为 <c>ulong (native_this, MethodInfo*)</c>，与目标 native ABI 逐位一致；
/// - 命中守卫时返回 <c>0UL</c>（真正的 None bits）且绝不调用原函数；
/// - 其余 context 调用 original trampoline 并**原样返回完整 64 bits**（Some 保持 Some），
///   不做 classproxy / <c>Il2CppObjectBaseToPtr</c> 转换，不伪造对象指针。
///
/// 守卫条件（全部满足才命中，否则按原合同透传）：
/// 1) <c>ExtensionIslandRuntime.TryScope</c>：普通 Greek 非挑战离线 + current==owner 同一实例；
/// 2) native this 恰是该 owner 实例；
/// 3) <c>owner.CurrentLand == 11</c>；
/// 4) 当前场景 <c>Managers.Inst.game.currentLand == 11</c>。
/// owner/land11 一经确证，后续任何读取失败或异常都停止并返回 None（绝不执行危险的原 unlock）；
/// 未确证 owner 时异常按原合同透传。original trampoline 不可用（理论不可达）时也返回 None 保守停止，
/// 不伪造“原结果”。
///
/// 安装：反射先核 interop 方法形状与 NativeMethodInfoPtr 字段/name/参数/token，再
/// <see cref="INativeDetour"/> Create → GenerateTrampoline → Apply；幂等，失败锁存且 IsReady 保持 false。
/// <c>Game.SetCurrentLand</c> 的 void postfix 仅引导 <see cref="ExtensionIslandProgressionGuard.EnsureInstalled"/>
/// （不拦原方法）；backend 也可在 EnsureReady/TryHandleConfig11 前直接调用该桥。
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.SetCurrentLand), new[] { typeof(int) })]
internal static class PatchExtensionIsland_ProgressionBootstrap
{
    /// <summary>void postfix：只引导安装，原方法照常执行。</summary>
    [HarmonyPostfix]
    private static void After()
    {
        try { ExtensionIslandProgressionGuard.EnsureInstalled(); }
        catch (Exception) { }
    }
}

/// <summary>native ABI adapter 的安装/判定宿主；lifetime 由静态字段强持有。</summary>
internal static class ExtensionIslandProgressionGuard
{
    /// <summary>
    /// 目标 native 签名：<c>Nullable&lt;int&gt; UnlockNextLand(CampaignSaveData* this, MethodInfo* method)</c>。
    /// 8-byte 值类型在 arm64/x64 都经单个寄存器返回，故 ulong 与 struct bits 逐位等价；
    /// original trampoline 与 detour 共用此形状。
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ulong UnlockNextLandNative(IntPtr nativeThis, IntPtr methodInfo);

    private const string InteropReturnTypeDefinition = "Il2CppSystem.Nullable`1";
    private const string LogPrefix = "[ExtensionIsland] UnlockNextLand-guard";

    private static readonly object Gate = new object();
    private static readonly System.Collections.Generic.HashSet<string> LoggedKeys =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    private static INativeDetour _detour;
    private static UnlockNextLandNative _original;
    private static bool _attempted;
    private static bool _installed;

    /// <summary>只有确认 Create+Prepare+Apply 全部成功后才为 true；失败/未知状态一律 false。</summary>
    internal static bool IsReady
    {
        get { lock (Gate) { return _installed && _detour != null && _original != null; } }
    }

    /// <summary>幂等安装（失败锁存不重试，避免半安装/重复 patch）。</summary>
    internal static bool EnsureInstalled()
    {
        lock (Gate)
        {
            if (_installed) return true;
            if (_attempted) return false;
            _attempted = true;

            try
            {
                if (!TryVerifyTarget(out IntPtr nativeMethodPointer, out string detail))
                {
                    LogOnce("verify-failed", "not installed: " + detail);
                    return false;
                }

                UnlockNextLandNative callback = Callback;
                INativeDetour detour = INativeDetour.Create(nativeMethodPointer, callback);
                if (detour == null || !detour.IsValid)
                {
                    LogOnce("create-failed", "not installed: detour create returned invalid handle");
                    return false;
                }

                UnlockNextLandNative original = detour.GenerateTrampoline<UnlockNextLandNative>();
                if (original == null)
                {
                    LogOnce("trampoline-failed", "not installed: no original trampoline (detour not applied)");
                    return false;
                }

                // 先发布 original（Apply 后 callback 可能立刻触发，必须能原样透传）。
                _original = original;
                _detour = detour;

                detour.Apply();
                if (!detour.IsApplied)
                {
                    LogOnce("apply-failed", "not installed: detour Apply did not commit");
                    return false;
                }

                _installed = true;
                LogOnce("installed", "installed (" + detail + "); 0..10/unknown contexts pass raw bits through");
                return true;
            }
            catch (Exception e)
            {
                LogOnce("install-exception", "not installed: " + e.GetType().Name + " " + e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// native detour 回调。scope 判定只在 try/catch 内计算 shouldBlock 与"scope 读取失败的
    /// fail-closed 资格"；**唯一的 original 调用在 try/catch 之后**——不包装原调用、不重试、
    /// 不吞原异常、不伪造 original 结果。shouldBlock==true 时直接返回真 None bits（0UL）且
    /// 原函数未执行；其余 context 调 original trampoline 并原样返回其完整 64 bits。
    /// </summary>
    private static ulong Callback(IntPtr nativeThis, IntPtr methodInfo)
    {
        bool shouldBlock = false;
        bool failClosedOnScopeError = false;
        try
        {
            if (ExtensionIslandRuntime.TryScope(out CampaignSaveData owner))
            {
                if (IsSameInstance(nativeThis, owner))
                {
                    // scoped owner 实例已确证：此后任何 scope 读取失败都必须 fail-closed
                    //（宁可停止返回 None，也不再执行危险原 unlock）。
                    failClosedOnScopeError = true;

                    if (owner.CurrentLand == ExtensionIslandRuntime.LandIndex)
                    {
                        Managers managers = Managers.Inst;
                        Game game = managers != null ? managers.game : null;
                        if (game == null)
                        {
                            LogOnce("scene-unreadable", "owner land " + ExtensionIslandRuntime.LandIndex
                                + " confirmed but scene state unreadable; stopping with None (original not executed)");
                            shouldBlock = true;
                        }
                        else if (game.currentLand == ExtensionIslandRuntime.LandIndex)
                        {
                            LogOnce("guarded", "guarded on extension land " + ExtensionIslandRuntime.LandIndex
                                + ": returning None bits, original not executed");
                            shouldBlock = true;
                        }
                        // 场景不匹配（读取成功且 != 11）→ 按原合同透传（shouldBlock 保持 false）
                    }
                    // owner 不在 land11 → 透传
                }
            }
        }
        catch (Exception e)
        {
            if (failClosedOnScopeError)
            {
                LogOnce("guarded-failure", "scoped owner instance confirmed; failing closed to None bits, "
                    + "original not executed: " + e.GetType().Name + " " + e.Message);
                shouldBlock = true;
            }
            else
            {
                shouldBlock = false;
                LogOnce("passthrough-failure", "scope unknown; passing through to original: "
                    + e.GetType().Name + " " + e.Message);
            }
        }

        if (shouldBlock) return 0UL;

        // 唯一一次 original 调用：未包装，原异常按原样向上传播。
        return InvokeOriginal(nativeThis, methodInfo);
    }

    /// <summary>
    /// 调用 original trampoline 并原样返回其 bits（不 catch、不重试、不改写）。
    /// trampoline 不可用属理论不可达；该状态下保守返回 None 并显式记日志，不伪造 original 结果。
    /// </summary>
    private static ulong InvokeOriginal(IntPtr nativeThis, IntPtr methodInfo)
    {
        UnlockNextLandNative original = _original;
        if (original == null)
        {
            LogOnce("no-original", "original trampoline unavailable; failing closed to None bits");
            return 0UL;
        }
        return original(nativeThis, methodInfo);
    }

    private static bool IsSameInstance(IntPtr nativeThis, CampaignSaveData owner)
    {
        if (owner == null || nativeThis == IntPtr.Zero) return false;
        return owner.Pointer == nativeThis;
    }

    /// <summary>
    /// 安装前核验：interop 方法形状 + NativeMethodInfoPtr 字段存在 + 原生 MethodInfo 的
    /// name/参数个数/token/MethodPointer 与目标一致。不硬编码任何 native 地址/offset。
    /// </summary>
    private static bool TryVerifyTarget(out IntPtr nativeMethodPointer, out string detail)
    {
        nativeMethodPointer = IntPtr.Zero;
        detail = null;
        try
        {
            MethodInfo target = typeof(CampaignSaveData).GetMethod(
                "UnlockNextLand", BindingFlags.Public | BindingFlags.Instance);
            if (target == null)
            {
                detail = "interop method CampaignSaveData.UnlockNextLand missing";
                return false;
            }
            if (target.IsStatic || target.GetParameters().Length != 0)
            {
                detail = "unexpected shape: static=" + target.IsStatic
                    + " parameters=" + target.GetParameters().Length;
                return false;
            }
            if (!IsExpectedReturnType(target.ReturnType))
            {
                detail = "unexpected return type " + target.ReturnType;
                return false;
            }

            FieldInfo infoField = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(target);
            if (infoField == null || infoField.FieldType != typeof(IntPtr))
            {
                detail = "NativeMethodInfoPtr field missing or not IntPtr";
                return false;
            }
            IntPtr methodInfoPointer = (IntPtr)infoField.GetValue(null);
            if (methodInfoPointer == IntPtr.Zero)
            {
                detail = "NativeMethodInfoPtr is null";
                return false;
            }

            unsafe
            {
                var native = UnityVersionHandler.Wrap((Il2CppMethodInfo*)methodInfoPointer);
                string name = native.Name != IntPtr.Zero ? Marshal.PtrToStringUTF8(native.Name) : null;
                if (!string.Equals(name, target.Name, StringComparison.Ordinal))
                {
                    detail = "native name mismatch: " + (name ?? "<null>");
                    return false;
                }
                if (native.ParametersCount != 0)
                {
                    detail = "native parameter count " + native.ParametersCount;
                    return false;
                }
                if (native.Token == 0)
                {
                    detail = "native token is zero";
                    return false;
                }
                if (native.MethodPointer == IntPtr.Zero)
                {
                    detail = "native MethodPointer is null";
                    return false;
                }
                nativeMethodPointer = native.MethodPointer;
                detail = "name=" + name + " token=0x" + native.Token.ToString("X8")
                    + " method=0x" + nativeMethodPointer.ToInt64().ToString("X");
            }
            return true;
        }
        catch (Exception e)
        {
            detail = e.GetType().Name + " " + e.Message;
            return false;
        }
    }

    private static bool IsExpectedReturnType(Type type)
    {
        // 运行时 Type.FullName 对构造泛型是程序集限定形式，不能与元数据字符串直接比较。
        return type != null
            && type.IsGenericType
            && type.GetGenericArguments().Length == 1
            && type.GetGenericArguments()[0] == typeof(int)
            && type.GetGenericTypeDefinition().FullName == InteropReturnTypeDefinition;
    }

    private static void LogOnce(string key, string message)
    {
        try
        {
            if (!LoggedKeys.Add(key)) return;
            KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(LogPrefix + " " + message);
        }
        catch (Exception) { }
    }
}
