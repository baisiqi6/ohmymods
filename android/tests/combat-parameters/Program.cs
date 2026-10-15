// Issue196 worker host（Program.cs）：net10 控制台 + ANDROID define，直接驱动两份候选源。
// 覆盖：ANDROID 门 null/OFF/主开关 OFF 零写；ON 时四 float 与两 vector X 各 ×0.75 且 Vector2.y
// 逐位保持（负 x / 0 / NaN / +Inf，无新 clamp）；开关关闭后新实例保持原值（非旧实例 restore）；
// 既有 catch 的异常局限（walk 已乘保持、后续字段未改、仅一次日志、无重试/二次乘）；
// Knight 原 >= 谓词（<120→120、=120/>120/+Inf 保持、真 Knight NaN/-Inf→120、非 Knight 含 NaN 不改）。
// 明确边界：本 host 不调用原生 Awake、不建 Harmony/interop/对象池镜像；native 效果由 Root 独立验证。
using System;
using System.Reflection;
using KingdomEnhancedMod;
using UnityEngine;

internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static readonly MethodInfo KnightPrefix = typeof(PatchRoles_KnightRecruitRange)
        .GetNestedType("Kingdom_FetchArchersForJob_RecruitRange_Patch", BindingFlags.NonPublic | BindingFlags.Public)
        ?.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);

    private static int Main()
    {
        bool android;
#if ANDROID
        android = true;
#else
        android = false;
#endif
        Console.WriteLine("host: ANDROID define = " + android + " ; knight Prefix bind = " + (KnightPrefix != null));
        Console.WriteLine("host: 直接 Compile 绑定 candidates/PatchRoles_CrownStealer.cs 与 candidates/PatchRoles_KnightRecruitRange.cs");

        CrownGates();
        CrownScale();
        CrownFlagOffThenNewInstance();
        CrownExceptionLock(getter: true);
        CrownExceptionLock(getter: false);
        Knight();

        Console.WriteLine("SUMMARY pass=" + _pass + " fail=" + _fail);
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " :: " + detail);
    }

    private static string F(float v)
    {
        if (float.IsNaN(v)) return "NaN";
        if (float.IsPositiveInfinity(v)) return "+Inf";
        if (float.IsNegativeInfinity(v)) return "-Inf";
        return v.ToString("R");
    }

    private static bool Same(float expected, float actual)
    {
        if (float.IsNaN(expected)) return float.IsNaN(actual);
        return BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual);
    }

    private static void SetConfig(bool main, bool? crown, bool? knight)
    {
        ModConfig.Enabled = new ConfigEntry<bool> { Value = main };
        ModConfig.CrownStealerSlowdownEnabled = crown.HasValue ? new ConfigEntry<bool> { Value = crown.Value } : null;
        ModConfig.KnightRecruitRangeEnabled = knight.HasValue ? new ConfigEntry<bool> { Value = knight.Value } : null;
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
    }

    private static CrownStealer NewCrown(float w, float r, float j, float c, float wx, float wy, float cx, float cy)
    {
        return new CrownStealer
        {
            walkSpeed = w,
            runSpeed = r,
            jumpSpeed = j,
            chargeSpeed = c,
            wallJumpForce = new Vector2(wx, wy),
            chargeJumpForce = new Vector2(cx, cy),
        };
    }

    private static string Seeds(float w, float r, float j, float c, float wx, float wy, float cx, float cy)
    {
        return "seeds(" + F(w) + "," + F(r) + "," + F(j) + "," + F(c) + ";"
             + F(wx) + "," + F(wy) + ";" + F(cx) + "," + F(cy) + ") -> ";
    }

    private static string Describe(CrownStealer s)
    {
        return "walk=" + F(s.walkSpeed) + " run=" + F(s.runSpeed) + " jump=" + F(s.jumpSpeed)
             + " charge=" + F(s.chargeSpeed)
             + " wall=(" + F(s.wallJumpForce.x) + "," + F(s.wallJumpForce.y) + ")"
             + " cjump=(" + F(s.chargeJumpForce.x) + "," + F(s.chargeJumpForce.y) + ")";
    }

    private static bool CrownUntouched(CrownStealer s, float w, float r, float j, float c, float wx, float wy, float cx, float cy)
    {
        return Same(w, s.walkSpeed) && Same(r, s.runSpeed) && Same(j, s.jumpSpeed) && Same(c, s.chargeSpeed)
            && Same(wx, s.wallJumpForce.x) && BitConverter.SingleToInt32Bits(wy) == BitConverter.SingleToInt32Bits(s.wallJumpForce.y)
            && Same(cx, s.chargeJumpForce.x) && BitConverter.SingleToInt32Bits(cy) == BitConverter.SingleToInt32Bits(s.chargeJumpForce.y);
    }

    private static bool CrownScaledOnce(CrownStealer s, float w, float r, float j, float c, float wx, float wy, float cx, float cy)
    {
        return Same(w * 0.75f, s.walkSpeed) && Same(r * 0.75f, s.runSpeed)
            && Same(j * 0.75f, s.jumpSpeed) && Same(c * 0.75f, s.chargeSpeed)
            && Same(wx * 0.75f, s.wallJumpForce.x)
            && BitConverter.SingleToInt32Bits(wy) == BitConverter.SingleToInt32Bits(s.wallJumpForce.y)
            && Same(cx * 0.75f, s.chargeJumpForce.x)
            && BitConverter.SingleToInt32Bits(cy) == BitConverter.SingleToInt32Bits(s.chargeJumpForce.y);
    }

    private static void CrownGates()
    {
        const float w = 4f, r = 7f, j = 12f, c = 9f, wx = 3.5f, wy = 8.25f, cx = -2.5f, cy = 6.5f;

        SetConfig(true, null, null);
        CrownStealer s = NewCrown(w, r, j, c, wx, wy, cx, cy);
        PatchRoles_CrownStealer.Awake_Prefix(s);
        Check("crown ANDROID flag=null main=ON zero-write", CrownUntouched(s, w, r, j, c, wx, wy, cx, cy), Describe(s));

        SetConfig(true, false, null);
        s = NewCrown(w, r, j, c, wx, wy, cx, cy);
        PatchRoles_CrownStealer.Awake_Prefix(s);
        Check("crown ANDROID flag=OFF main=ON zero-write", CrownUntouched(s, w, r, j, c, wx, wy, cx, cy), Describe(s));

        SetConfig(false, true, null);
        s = NewCrown(w, r, j, c, wx, wy, cx, cy);
        PatchRoles_CrownStealer.Awake_Prefix(s);
        Check("crown main=OFF flag=ON zero-write (既有门在前)", CrownUntouched(s, w, r, j, c, wx, wy, cx, cy), Describe(s));
    }

    private static void CrownScale()
    {
        SetConfig(true, true, null);
        (string name, float w, float r, float j, float c, float wx, float wy, float cx, float cy)[] cases =
        {
            ("normal", 4f, 7f, 12f, 9f, 3.5f, 8.25f, -2.5f, 6.5f),
            ("negative-x-and-run", 6f, -8f, 12f, 9f, -4f, 5f, -6f, 7f),
            ("zeros", 0f, 7f, 0f, 9f, 0f, 5f, 0f, 7f),
            ("NaN-fields-no-clamp", float.NaN, 7f, 12f, float.NaN, float.NaN, float.NaN, 3f, 8f),
            ("+Inf", 5f, float.PositiveInfinity, 12f, 9f, float.PositiveInfinity, 5f, 2f, 9f),
        };
        foreach (var t in cases)
        {
            CrownStealer s = NewCrown(t.w, t.r, t.j, t.c, t.wx, t.wy, t.cx, t.cy);
            PatchRoles_CrownStealer.Awake_Prefix(s);
            Check("crown ON " + t.name + " x*0.75 & y bit-kept",
                CrownScaledOnce(s, t.w, t.r, t.j, t.c, t.wx, t.wy, t.cx, t.cy),
                Seeds(t.w, t.r, t.j, t.c, t.wx, t.wy, t.cx, t.cy) + Describe(s));
        }
    }

    private static void CrownFlagOffThenNewInstance()
    {
        SetConfig(true, true, null);
        CrownStealer a = NewCrown(4f, 7f, 12f, 9f, 3f, 5f, 2f, 4f);
        PatchRoles_CrownStealer.Awake_Prefix(a);

        SetConfig(true, false, null); // 热关闭独立开关
        CrownStealer b = NewCrown(4f, 7f, 12f, 9f, 3f, 5f, 2f, 4f);
        PatchRoles_CrownStealer.Awake_Prefix(b);
        Check("crown flag-off NEW instance keeps originals (非旧实例 restore)",
            CrownUntouched(b, 4f, 7f, 12f, 9f, 3f, 5f, 2f, 4f), Describe(b));
        Check("crown hot-off does NOT revert earlier instance (源注释已知边界)",
            CrownScaledOnce(a, 4f, 7f, 12f, 9f, 3f, 5f, 2f, 4f), Describe(a));
    }

    private static void CrownExceptionLock(bool getter)
    {
        SetConfig(true, true, null);
        CrownStealer s = NewCrown(4f, 7f, 12f, 9f, 3f, 5f, 2f, 4f);
        if (getter) s.ThrowOnRunSpeedGet = true; else s.ThrowOnRunSpeedSet = true;
        Logger log = KingdomEnhancedPlugin.Instance.LogSource;
        PatchRoles_CrownStealer.Awake_Prefix(s);

        // 异常路径已在上面走完；仅关闭桩注入点以便读回终值（不改候选、不重触发）。
        s.ThrowOnRunSpeedGet = false;
        s.ThrowOnRunSpeedSet = false;
        bool walkMultipliedExactlyOnce = Same(4f * 0.75f, s.walkSpeed) && !Same(4f * 0.75f * 0.75f, s.walkSpeed);
        bool runKept = Same(7f, s.runSpeed);
        bool restUntouched = Same(12f, s.jumpSpeed) && Same(9f, s.chargeSpeed)
            && Same(3f, s.wallJumpForce.x) && Same(5f, s.wallJumpForce.y)
            && Same(2f, s.chargeJumpForce.x) && Same(4f, s.chargeJumpForce.y);
        bool singleLog = log.Entries.Count == 1 && log.Entries[0] is InvalidOperationException;
        Check("crown exception lock runSpeed " + (getter ? "getter" : "setter"),
            walkMultipliedExactlyOnce && runKept && restUntouched && singleLog,
            Describe(s) + " logs=" + log.Entries.Count
            + (log.Entries.Count > 0 ? " first=" + log.Entries[0].GetType().Name : ""));
    }

    private static void Knight()
    {
        if (KnightPrefix == null)
        {
            Check("knight reflection bind", false, "Prefix not found via reflection");
            return;
        }
        Check("knight reflection bind", true, KnightPrefix.DeclaringType.FullName + "." + KnightPrefix.Name);

        Knight theKnight = new Knight();
        GameObject knightJob = new GameObject(theKnight);
        GameObject plainJob = new GameObject(null);
        GameObject otherJob = new GameObject(new object());
        Check("stub sanity GetComponent same-object", ReferenceEquals(knightJob.GetComponent<Knight>(), theKnight), "同实例语义");

        K("flag=OFF main=ON r=10 keep", true, false, knightJob, 10f, 10f);
        K("flag=null main=ON r=10 keep", true, null, knightJob, 10f, 10f);
        K("flag=ON main=OFF r=10 keep", false, true, knightJob, 10f, 10f);
        K("real knight 10 -> 120", true, true, knightJob, 10f, 120f);
        K("real knight =120 keep", true, true, knightJob, 120f, 120f);
        K("real knight >120 (500) keep", true, true, knightJob, 500f, 500f);
        K("real knight +Inf keep", true, true, knightJob, float.PositiveInfinity, float.PositiveInfinity);
        K("real knight NaN -> 120 (原 >= 谓词)", true, true, knightJob, float.NaN, 120f);
        K("real knight -Inf -> 120 (原 >= 谓词)", true, true, knightJob, float.NegativeInfinity, 120f);
        K("plain job (no component) keep", true, true, plainJob, 10f, 10f);
        K("other-component job keep", true, true, otherJob, 10f, 10f);
        K("null job keep", true, true, null, 10f, 10f);
        K("non-knight NaN keep NaN", true, true, plainJob, float.NaN, float.NaN);
    }

    private static void K(string name, bool main, bool? flag, GameObject job, float input, float expected)
    {
        SetConfig(main, null, flag);
        object[] args = { job, input };
        KnightPrefix.Invoke(null, args);
        float actual = (float)args[1]; // ref 参数经反射数组读回
        Check("knight " + name, Same(expected, actual),
            "in=" + F(input) + " out=" + F(actual) + " expected=" + F(expected));
    }
}
