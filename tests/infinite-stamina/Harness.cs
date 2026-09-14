// 迷你 Harmony 替身：只做本用例需要的事 —— 用反射读生产文件的 [HarmonyPatch] 标注（类级带目标类型/
// 方法名，或方法级带），按参数名绑定 __instance / __state / __exception，复刻 Harmony 的执行序与异常语义：
//   prefix(可多个) → 原生体（仅当前缀正常返回）→ postfix（仅当原生体正常返回）→ finalizer（总是执行）
//   finalizer 返回的 Exception 即最终抛出物（返回 __exception 原样 = 不吞；返回 null = 吞掉）；
//   站点没有 finalizer 时，原生异常原样抛出。
// 这不是 Harmony 本身：不生成动态方法、不做 IL2CPP detour，只验证托管语义与标注正确性。
using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomEnhancedMod;

internal static class HarmonyHarness
{
    internal sealed class Hook
    {
        internal MethodInfo Method;
        internal ParameterInfo[] Parameters;
    }

    internal sealed class Site
    {
        internal string Key;
        internal MethodInfo Native;
        internal Type StateType;
        internal readonly List<Hook> Prefixes = new List<Hook>();
        internal readonly List<Hook> Postfixes = new List<Hook>();
        internal readonly List<Hook> Finalizers = new List<Hook>();
    }

    internal sealed class RunResult
    {
        internal Exception Thrown;
        internal bool NativeRan;
        internal object State;
    }

    internal static readonly Dictionary<string, Site> Sites = new Dictionary<string, Site>();
    internal static string DiscoveryError;

    /// <summary>测试注入点：模拟"本 patch 的 Postfix 与 Finalizer 之间"其他逻辑的写入。</summary>
    internal static Action AfterPostfix;

    internal static void Discover()
    {
        try
        {
            Assembly assembly = typeof(PatchRide_InfiniteStamina).Assembly;
            foreach (Type type in assembly.GetTypes())
            {
                if (!type.Name.StartsWith("PatchRide_InfiniteStamina", StringComparison.Ordinal)) continue;

                Type classTarget = null;
                string classMethod = null;
                foreach (object attribute in type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false))
                {
                    var patch = (HarmonyLib.HarmonyPatch)attribute;
                    classTarget = patch.Target;
                    classMethod = patch.MethodName;
                }

                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    bool isPrefix = method.GetCustomAttribute<HarmonyLib.HarmonyPrefix>() != null;
                    bool isPostfix = method.GetCustomAttribute<HarmonyLib.HarmonyPostfix>() != null;
                    bool isFinalizer = method.GetCustomAttribute<HarmonyLib.HarmonyFinalizer>() != null;
                    if (!isPrefix && !isPostfix && !isFinalizer) continue;

                    Type target = classTarget;
                    string name = classMethod;
                    foreach (object attribute in method.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false))
                    {
                        var patch = (HarmonyLib.HarmonyPatch)attribute;
                        if (patch.Target != null) target = patch.Target;
                        if (patch.MethodName != null) name = patch.MethodName;
                    }
                    if (target == null || string.IsNullOrEmpty(name))
                    { DiscoveryError = $"{type.Name}.{method.Name} 无法确定目标方法"; return; }

                    Site site = SiteFor(target, name);
                    if (site == null) return;
                    var hook = new Hook { Method = method, Parameters = method.GetParameters() };
                    if (isPrefix) site.Prefixes.Add(hook);
                    if (isPostfix) site.Postfixes.Add(hook);
                    if (isFinalizer) site.Finalizers.Add(hook);
                    if (site.StateType == null) site.StateType = StateTypeOf(hook);
                }
            }

            foreach (string expected in new[]
            {
                "Player.UpdateActionState",
                "SteedAbility.Activate",
                "GlideMovementSteedAbility.Activate",
                "RunningAttackSteedAbility.OnPushedObjects"
            })
            {
                if (!Sites.TryGetValue(expected, out Site site))
                { DiscoveryError = "缺少 patching site: " + expected; return; }
                if (site.Prefixes.Count == 0 || site.Postfixes.Count == 0)
                { DiscoveryError = expected + " 缺少 Prefix/Postfix"; return; }
            }
        }
        catch (Exception exception)
        {
            DiscoveryError = exception.ToString();
        }
    }

    private static Site SiteFor(Type target, string methodName)
    {
        string key = target.Name + "." + methodName;
        if (Sites.TryGetValue(key, out Site existing)) return existing;
        MethodInfo native = target.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (native == null) { DiscoveryError = "目标方法不存在: " + key; return null; }
        var site = new Site { Key = key, Native = native };
        Sites[key] = site;
        return site;
    }

    private static Type StateTypeOf(Hook hook)
    {
        int index = StateIndex(hook);
        if (index < 0) return null;
        Type type = hook.Parameters[index].ParameterType;
        return type.IsByRef ? type.GetElementType() : type;   // out __state 的参数类型是 Borrow&
    }

    /// <summary>完整跑一次"被 Harmony 包裹的原生调用"。</summary>
    internal static RunResult RunSite(string key, object instance, params object[] nativeArgs)
    {
        if (!Sites.TryGetValue(key, out Site site))
            throw new ArgumentException("未知 patching site: " + key);

        var result = new RunResult();
        // 真实 Harmony 的 out __state 初值是 default：值类型是零值结构体，引用类型是 null。
        object state = site.StateType == null || !site.StateType.IsValueType
            ? null
            : Activator.CreateInstance(site.StateType);
        Exception pending = null;

        try
        {
            for (int i = 0; i < site.Prefixes.Count; i++)
            {
                object[] args = Bind(site.Prefixes[i], instance, state, null);
                try
                {
                    Steed.RecordWrites = true;
                    site.Prefixes[i].Method.Invoke(null, args);
                }
                finally
                {
                    Steed.RecordWrites = false;
                    // 前缀若中途抛出，out __state 未赋值 → 保持默认（与 Harmony 生成代码一致）。
                    int index = StateIndex(site.Prefixes[i]);
                    if (index >= 0) state = args[index];
                }
            }

            result.NativeRan = true;
            site.Native.Invoke(instance, nativeArgs);

            for (int i = 0; i < site.Postfixes.Count; i++)
            {
                object[] args = Bind(site.Postfixes[i], instance, state, null);
                try
                {
                    Steed.RecordWrites = true;
                    site.Postfixes[i].Method.Invoke(null, args);
                }
                finally { Steed.RecordWrites = false; }
            }
        }
        catch (TargetInvocationException invocation)
        {
            pending = invocation.InnerException;
        }

        result.State = state;

        if (site.Finalizers.Count == 0)
        {
            result.Thrown = pending;
            return result;
        }

        if (AfterPostfix != null)
        {
            Action injection = AfterPostfix;
            AfterPostfix = null;
            injection();
        }

        for (int i = 0; i < site.Finalizers.Count; i++)
        {
            object[] args = Bind(site.Finalizers[i], instance, state, pending);
            object returned;
            try
            {
                Steed.RecordWrites = true;
                returned = site.Finalizers[i].Method.Invoke(null, args);
            }
            catch (TargetInvocationException invocation)
            {
                pending = invocation.InnerException;
                continue;
            }
            finally { Steed.RecordWrites = false; }
            pending = returned as Exception;
        }

        result.Thrown = pending;
        return result;
    }

    /// <summary>Player.UpdateActionState 的便捷入口。</summary>
    internal static RunResult Run(Player player, int direction, bool startSprint, bool stopSprint,
        bool sprintKeyPressed, bool sprintKeyDoubleTap)
        => RunSite("Player.UpdateActionState", player,
            direction, startSprint, stopSprint, sprintKeyPressed, sprintKeyDoubleTap);

    private static int StateIndex(Hook hook)
    {
        for (int i = 0; i < hook.Parameters.Length; i++)
            if (hook.Parameters[i].Name == "__state") return i;
        return -1;
    }

    private static object[] Bind(Hook hook, object instance, object state, Exception pending)
    {
        var args = new object[hook.Parameters.Length];
        for (int i = 0; i < args.Length; i++)
        {
            switch (hook.Parameters[i].Name)
            {
                case "__instance": args[i] = instance; break;
                case "__state": args[i] = state; break;
                case "__exception": args[i] = pending; break;
                default: throw new NotSupportedException("harness 不支持的参数: " + hook.Parameters[i].Name);
            }
        }
        return args;
    }

    // ------------------------------------------------------- __state 观测

    internal static bool Entered(object state)
        => state is PatchRide_InfiniteStamina.Borrow;

    internal static bool AbilityEntered(object state)
        => state is PatchRide_InfiniteStamina.AbilityBorrow;

    internal static bool OwnsRate(object state, string rate)
    {
        if (!(state is PatchRide_InfiniteStamina.Borrow borrow)) return false;
        switch (rate)
        {
            case "Run": return borrow.Rates.RunOwned;
            case "Walk": return borrow.Rates.WalkOwned;
            case "Stand": return borrow.Rates.StandOwned;
            case "Glide": return borrow.Rates.GlideOwned;
            default: throw new ArgumentException("未知速率: " + rate);
        }
    }
}
