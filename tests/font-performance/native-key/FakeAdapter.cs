using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// 纯测试 native 边界替身：只记录调用与返回可控值，不执行任何 native/IL2CPP。
internal sealed class FakeAdapter : INativeKeyAdapter
{
    internal int AcquireCalls;
    internal int ResolveCalls;
    internal int InvokeCalls;
    internal int RetainCalls;
    internal int ReleaseCalls;
    internal int ReadCalls;

    // 能力获取结论由测试显式给定：Unsupported=能力缺失（可锁存回退）、Failed=执行失败（当次 false 可重试）。
    internal NativeKeyAcquire AcquireResult = NativeKeyAcquire.Ready;
    internal IntPtr KeyHandle = (IntPtr)0x11;
    internal IntPtr KeyTarget = (IntPtr)0x1000;
    internal IntPtr ResultHandle = (IntPtr)0x22;
    internal IntPtr ResultTarget = (IntPtr)0x2000;
    internal bool HandleTargetOk = true;   // false => ResolveHandle 一律返回 0（handle 失效）
    internal bool InvokeOk = true;
    internal IntPtr InvokeResult = (IntPtr)0x2000;
    internal IntPtr InvokeExc = IntPtr.Zero;
    internal bool RetainOk = true;
    internal bool ReadOk = true;
    internal int ReadLength;
    internal IntPtr ReadChars = IntPtr.Zero;
    internal bool KeyReleaseFails;   // ReleaseNativeResources 的 free 未确认
    internal bool TempReleaseFails;  // 当次结果 handle 的 free 未确认
    internal readonly List<IntPtr> Released = new List<IntPtr>();

    public bool Faulted { get; private set; }
    public int PendingCount { get; private set; }

    public NativeKeyAcquire AcquireFixedKey(string key, out IntPtr keyHandle)
    {
        AcquireCalls++;
        keyHandle = AcquireResult == NativeKeyAcquire.Ready ? KeyHandle : IntPtr.Zero;
        return AcquireResult;
    }

    public IntPtr ResolveHandle(IntPtr handle)
    {
        ResolveCalls++;
        if (!HandleTargetOk || handle == IntPtr.Zero) return IntPtr.Zero;
        return handle == KeyHandle ? KeyTarget : ResultTarget;
    }

    public bool TryReleaseHandle(IntPtr handle)
    {
        ReleaseCalls++;
        Released.Add(handle);
        bool fail = (handle == KeyHandle && KeyReleaseFails) || (handle == ResultHandle && TempReleaseFails);
        if (!fail) return true;
        Faulted = true; // 释放未确认：bounded pending + 不再创建/不再重复 free
        PendingCount++;
        return false;
    }

    public IntPtr RetainObject(IntPtr objectPtr)
    {
        RetainCalls++;
        if (Faulted || !RetainOk) return IntPtr.Zero;
        return ResultHandle;
    }

    public bool TryInvokeGetter(IntPtr dictionaryPtr, IntPtr keyPtr, out IntPtr resultPtr, out IntPtr excPtr)
    {
        InvokeCalls++;
        resultPtr = InvokeResult;
        excPtr = InvokeExc;
        return InvokeOk;
    }

    public bool TryReadStringObject(IntPtr objectPtr, out int length, out IntPtr chars)
    {
        ReadCalls++;
        length = ReadLength;
        chars = ReadChars;
        return ReadOk;
    }
}
