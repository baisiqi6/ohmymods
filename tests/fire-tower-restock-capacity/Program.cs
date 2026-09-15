using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;
using UnityEngine;

/// <summary>
/// FireTowerRestockCapacity 边界回归：编译真实 helper 源码，注入 stub 的原生对象/字段，
/// 断言 role7 采购门的每个判定分支与精确 reason 文案。
/// </summary>
internal static class Program
{
    // 契约文案（helper 常量之外的独立字面量，防止文案悄悄漂移）
    private const string Full = "弹药已满，等待消耗";
    private const string NotReady = "弹药容量未就绪";

    private static int Passed, Failed;

    private static void Eq<T>(T want, T got, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(want, got))
            throw new Exception($"{why}: expected {want}, got {got}");
    }

    private static void Check(bool condition, string why)
    {
        if (!condition) throw new Exception(why);
    }

    private static void Test(string name, Action body)
    {
        try { body(); Passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { Failed++; Console.WriteLine("FAIL " + name + ": " + e.GetBaseException().Message); }
    }

    /// <summary>一座塔：GO + FireTower(容量/当前/假罐数组槽数) + 已 Init(owner) 的 PayableComponent。</summary>
    private static (PayableComponent Component, FireTower Tower, GameObject Go) Site(
        int maxFireJars, int activeNum, int? arraySlots)
    {
        var go = new GameObject();
        FireTower tower = go.AddComponent<FireTower>();
        tower._maxFireJars = maxFireJars;
        tower._fireJarsActiveNum = activeNum;
        tower._fakeFireJars = arraySlots is int slots ? ReadySlots(slots) : null;
        PayableComponent component = go.AddComponent<PayableComponent>();
        component._owner = tower;
        return (component, tower, go);
    }

    private static Il2CppReferenceArray<GameObject> ReadySlots(int count)
    {
        var result = new Il2CppReferenceArray<GameObject>(count);
        for (int i = 0; i < count; i++) result[i] = new GameObject();
        return result;
    }

    private static void Allows(Payable target, string why)
    {
        bool ok = FireTowerRestockCapacity.CanPurchase(target, out string reason);
        Check(ok, why + "：应可买，实际 reason=" + (reason ?? "null"));
        Eq<string>(null, reason, why + "：成功时 reason 必须为 null");
    }

    private static void Blocks(Payable target, string expected, string why)
    {
        bool ok = FireTowerRestockCapacity.CanPurchase(target, out string reason);
        Check(!ok, why + "：应被拒绝");
        Eq(expected, reason, why + "：拒绝原因");
    }

    public static int Main()
    {
        Test("目标槽对象缺失或销毁时禁止采购", () =>
        {
            var site = Site(9, 8, 9);
            site.Tower._fakeFireJars[8] = null;
            Blocks(site.Component, NotReady, "array slot unbuilt");
            site.Tower._fakeFireJars[8] = new GameObject { Destroyed = true };
            Blocks(site.Component, NotReady, "array slot destroyed");
            site.Tower._fakeFireJars[8] = new GameObject();
            Allows(site.Component, "slot rebuilt");
        });
        Test("契约常量文案", () =>
        {
            Eq(Full, FireTowerRestockCapacity.ReasonFull, "已满文案");
            Eq(NotReady, FireTowerRestockCapacity.ReasonNotReady, "未就绪文案");
        });

        Test("阈值12/单塔容量9：8/9 可买（容量门不看阈值）", () =>
        {
            var (component, tower, _) = Site(9, 8, 9);
            Allows(component, "8/9");
            Eq(8, tower._fireJarsActiveNum, "门控只读：不改当前数");
            Eq(9, tower._maxFireJars, "门控只读：不改容量");
            Eq(9, tower._fakeFireJars.Length, "门控只读：不改假罐数组");
        });

        Test("阈值12/单塔容量9：9/9 已满（不再进入采购-原生付款循环）", () =>
        {
            var (component, _, _) = Site(9, 9, 9);
            Blocks(component, Full, "9/9");
        });

        Test("多塔独立：各塔按自己的字段判定，互不影响", () =>
        {
            var a = Site(9, 8, 9);
            var b = Site(9, 9, 9);
            var c = Site(9, 0, 9);
            Allows(a.Component, "A 8/9");
            Blocks(b.Component, Full, "B 9/9");
            a.Tower._fireJarsActiveNum = 9;                     // 模拟 A 原生 OnPay ++ 一发
            Blocks(a.Component, Full, "A 补满后 9/9");
            Eq(9, b.Tower._fireJarsActiveNum, "B 不受 A 影响");
            Allows(c.Component, "C 0/9 仍可买");
        });

        Test("owner 必须是同 GO 的这座塔（Pointer 精确相等）", () =>
        {
            var (component, tower, go) = Site(9, 0, 9);
            var otherSite = Site(9, 0, 9);
            component._owner = otherSite.Tower;                 // 池化复用式错挂：指向另一座塔
            Blocks(component, NotReady, "owner 指向其它塔");
            component._owner = null;
            Blocks(component, NotReady, "owner 未初始化");
            component._owner = tower;
            Allows(component, "owner 修正");

            // 同 GO 无塔：owner 是别的 GO 上的塔 → 不是这座组件
            var bareGo = new GameObject();
            PayableComponent bare = bareGo.AddComponent<PayableComponent>();
            bare._owner = tower;
            Blocks(bare, NotReady, "同 GO 无 FireTower");
            Check(bareGo.GetComponent<FireTower>() == null, "夹具：bareGo 不应有塔");
        });

        Test("其它 role 的形状（非 PayableComponent / 无塔 GO）一律未就绪", () =>
        {
            var plainGo = new GameObject();
            Payable plain = plainGo.AddComponent<Payable>();
            Blocks(plain, NotReady, "非 PayableComponent 目标");

            var compGo = new GameObject();
            PayableComponent orphan = compGo.AddComponent<PayableComponent>();
            Blocks(orphan, NotReady, "无 owner/无塔的 PayableComponent");
        });

        Test("假罐数组尚未生成 / 空数组 → 未就绪", () =>
        {
            var missing = Site(12, 0, null);
            Blocks(missing.Component, NotReady, "数组 null");
            var empty = Site(12, 0, 0);
            Blocks(empty.Component, NotReady, "数组 0 槽");
        });

        Test("数组槽数 < max：有效容量取 min（数组长度是安全上界）", () =>
        {
            var (component, tower, _) = Site(12, 4, 5);
            Allows(component, "max12/slots5 且 4<5");
            tower._fireJarsActiveNum = 5;
            Blocks(component, Full, "5 槽用满即已满");
            tower._fireJarsActiveNum = 6;
            Blocks(component, NotReady, "6>5 超限属非法数据");
        });

        Test("数组槽数 > max：有效容量取 max", () =>
        {
            var (component, tower, _) = Site(9, 8, 12);
            Allows(component, "max9/slots12 且 8<9");
            tower._fireJarsActiveNum = 9;
            Blocks(component, Full, "9/9 已满");
        });

        Test("换数组立即生效：绝不缓存旧容量", () =>
        {
            var (component, tower, _) = Site(12, 8, 9);
            Allows(component, "旧数组 9 槽且 8<9");
            tower._fakeFireJars = ReadySlots(3);
            Blocks(component, NotReady, "换小数组后 8>3 立即拒绝（缓存旧容量会误判可买）");
            tower._fakeFireJars = ReadySlots(12);
            Allows(component, "换回大数组后 8<12 再次可买");
            tower._fakeFireJars = null;
            Blocks(component, NotReady, "数组清空后未就绪");
        });

        Test("current 负值 / 超限均属非法数据", () =>
        {
            var (component, tower, _) = Site(9, -1, 9);
            Blocks(component, NotReady, "current=-1");
            tower._fireJarsActiveNum = 10;
            Blocks(component, NotReady, "current=10>9");
            tower._fireJarsActiveNum = 0;
            Allows(component, "current 恢复 0");
        });

        Test("禁用 / 非激活 / 销毁 fail-closed", () =>
        {
            var (component, tower, go) = Site(9, 0, 9);
            component.enabled = false;
            Blocks(component, NotReady, "组件禁用");
            component.enabled = true;
            tower.enabled = false;
            Blocks(component, NotReady, "塔禁用");
            tower.enabled = true;
            go.activeInHierarchy = false;
            Blocks(component, NotReady, "GO 非激活");
            go.activeInHierarchy = true;
            tower.Destroyed = true;
            Blocks(component, NotReady, "塔已销毁");
            tower.Destroyed = false;
            go.Destroyed = true;
            Blocks(component, NotReady, "GO 已销毁");
            go.Destroyed = false;
            Allows(component, "恢复后再次可买");
            tower.ThrowOnRead = true;                            // 原生对象已销毁：字段读取抛异常
            Blocks(component, NotReady, "字段读取异常");

            var gone = Site(9, 0, 9);
            gone.Component.Destroyed = true;
            Blocks(gone.Component, NotReady, "目标自身已销毁");
        });

        Test("只读：连续门控调用不改动任何原生弹药字段", () =>
        {
            var (component, tower, _) = Site(9, 8, 9);
            int maxBefore = tower._maxFireJars, curBefore = tower._fireJarsActiveNum;
            for (int i = 0; i < 3; i++) Allows(component, "8/9 第" + i + "次");
            tower._fireJarsActiveNum = 9;
            Blocks(component, Full, "9/9");
            Eq(maxBefore, tower._maxFireJars, "容量未被改写");
            Eq(9, tower._fireJarsActiveNum, "current 只被测试自己改过");
            Eq(9, tower._fakeFireJars.Length, "数组未被替换");
        });

        Console.WriteLine($"RESULT passed={Passed} failed={Failed}");
        return Failed == 0 ? 0 : 1;
    }
}
