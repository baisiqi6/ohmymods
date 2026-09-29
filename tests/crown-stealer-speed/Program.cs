using KingdomEnhancedMod;
using UnityEngine;
using D = KingdomEnhancedMod.PatchRoles_CrownStealer;

// 直调生产的 Awake 前缀。本套件不模拟 Unity 生命周期：Awake 每实例只跑一次、
// 池复生只跑 OnEnable，因此这里不把“重复调用”当作池复用证据（见 README 限制）。
static class Program
{
    static int pass, fail, assertions;

    static void Check(bool value, string label)
    {
        assertions++;
        if (!value) throw new Exception(label);
    }

    static void Eq(float expected, float actual, string label)
    {
        assertions++;
        if (actual != expected) throw new Exception($"{label}: expected {expected}, got {actual}");
    }

    static CrownStealer New() => new CrownStealer
    {
        walkSpeed = 4f,
        runSpeed = -2f,
        jumpSpeed = 3f,
        chargeSpeed = -5f,
        wallJumpForce = new Vector2(2f, 7f),
        chargeJumpForce = new Vector2(5.5f, 2.3f),
        attackRange = 6f,
        damage = 2f,
        jumpCooldown = 0.8f,
    };

    static void Test(string name, Action act)
    {
        KingdomEnhancedPlugin.Instance = new KingdomEnhancedPlugin();
        ModConfig.Enabled.Value = true;
        try
        {
            act();
            Check(KingdomEnhancedPlugin.Instance.LogSource.Errors.Count == 0, "unexpected production error log");
            pass++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            fail++;
            Console.WriteLine("FAIL " + name + ": " + e.Message);
        }
    }

    static void Main()
    {
        Test("enabled: six horizontal quantities scaled by exactly 0.75, signs preserved", () =>
        {
            var c = New();
            D.Awake_Prefix(c);
            Eq(3f, c.walkSpeed, "walkSpeed");
            Eq(-1.5f, c.runSpeed, "runSpeed");
            Eq(2.25f, c.jumpSpeed, "jumpSpeed");
            Eq(-3.75f, c.chargeSpeed, "chargeSpeed");
            Eq(1.5f, c.wallJumpForce.x, "wallJumpForce.x");
            Eq(4.125f, c.chargeJumpForce.x, "chargeJumpForce.x");
        });

        Test("enabled: both Vector2 y components bit-identical", () =>
        {
            var c = New();
            D.Awake_Prefix(c);
            Eq(7f, c.wallJumpForce.y, "wallJumpForce.y");
            Eq(2.3f, c.chargeJumpForce.y, "chargeJumpForce.y");
        });

        Test("enabled: unrelated members untouched", () =>
        {
            var c = New();
            Mover mover = c._mover;
            D.Awake_Prefix(c);
            Eq(6f, c.attackRange, "attackRange");
            Eq(2f, c.damage, "damage");
            Eq(0.8f, c.jumpCooldown, "jumpCooldown");
            Check(ReferenceEquals(mover, c._mover), "_mover reference identity");
        });

        Test("disabled: all six quantities bit-identical", () =>
        {
            var c = New();
            ModConfig.Enabled.Value = false;
            D.Awake_Prefix(c);
            Eq(4f, c.walkSpeed, "walkSpeed");
            Eq(-2f, c.runSpeed, "runSpeed");
            Eq(3f, c.jumpSpeed, "jumpSpeed");
            Eq(-5f, c.chargeSpeed, "chargeSpeed");
            Eq(2f, c.wallJumpForce.x, "wallJumpForce.x");
            Eq(7f, c.wallJumpForce.y, "wallJumpForce.y");
            Eq(5.5f, c.chargeJumpForce.x, "chargeJumpForce.x");
            Eq(2.3f, c.chargeJumpForce.y, "chargeJumpForce.y");
        });

        Test("hot off-switch: no restore and no rescale path for an already-scaled instance", () =>
        {
            var c = New();
            D.Awake_Prefix(c);              // enabled: 4 → 3
            ModConfig.Enabled.Value = false;
            D.Awake_Prefix(c);              // disabled: must not restore/alter
            Eq(3f, c.walkSpeed, "walkSpeed stays scaled while off");
            Eq(-1.5f, c.runSpeed, "runSpeed stays scaled while off");
        });

        Test("null instance: safe no-op without error log", () =>
        {
            D.Awake_Prefix(null);
        });

        Console.WriteLine($"RESULT: {pass} passed, {fail} failed; {assertions} assertions");
        Environment.ExitCode = fail > 0 ? 1 : 0;
    }
}
