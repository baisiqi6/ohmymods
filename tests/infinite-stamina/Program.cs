// 坐骑无限体力回归：真链接生产源码 il2cpp/PatchRide_InfiniteStamina.cs，其余面用本目录 stub，
// 经 Harness.cs 的迷你 Harmony 反射执行"被包裹的原生模拟"。断言都落在可观察结果上
// （坐骑字段终值、patch 写入账本、__state 是否接管/持有、异常透传、actionState 走向）。
using System;
using System.Collections.Generic;
using KingdomEnhancedMod;
using H = HarmonyHarness;

internal static class Program
{
    private static int _pass;
    private static int _fail;
    private static int _assertions;

    private static void Check(bool ok, string label)
    {
        _assertions++;
        if (!ok) throw new Exception(label);
    }

    private static void Eq(float expected, float actual, string label)
        => Check(Math.Abs(expected - actual) < 1e-6f, $"{label}: expected {expected}, got {actual}");

    private static void EqI(int expected, int actual, string label)
        => Check(expected == actual, $"{label}: expected {expected}, got {actual}");

    private static bool Wrote(Steed steed, string field, float value)
    {
        for (int i = 0; i < steed.Writes.Count; i++)
            if (steed.Writes[i].Field == field && Math.Abs(steed.Writes[i].Value - value) < 1e-6f) return true;
        return false;
    }

    private static int WriteCount(Steed steed, string field)
    {
        int count = 0;
        for (int i = 0; i < steed.Writes.Count; i++)
            if (steed.Writes[i].Field == field) count++;
        return count;
    }

    private static bool WroteAny(Steed steed, params string[] fields)
    {
        for (int i = 0; i < steed.Writes.Count; i++)
            for (int j = 0; j < fields.Length; j++)
                if (steed.Writes[i].Field == fields[j]) return true;
        return false;
    }

    private static Player NewPlayer(float run = -0.4f, float walk = -0.05f, float stand = 0.3f, float glide = -0.6f)
    {
        var player = new Player();
        var steed = new Steed { Label = "S" };
        steed.runStaminaRate = run;
        steed.walkStaminaRate = walk;
        steed.standStaminaRate = stand;
        steed.glideStaminaRate = glide;
        player.Ride(steed);
        OptionalQoLScope.Current.Add(player);
        OptionalQoLScope.Current.Add(steed);
        return player;
    }

    private static void Test(string name, Action body)
    {
        OptionalQoLScope.IsActive = true;
        OptionalQoLScope.Current.Clear();
        ModConfig.InfiniteSteedStamina.Value = false;
        Steed.RecordWrites = false;
        H.AfterPostfix = null;
        Player.DebugInfiniteStamina = false;
        Player.DebugInfiniteStaminaWrites = 0;
        KingdomEnhancedPlugin.Logger.Errors.Clear();
        UnityEngine.Time.time = 100f;
        UnityEngine.Time.deltaTime = 1f / 60f;
        try
        {
            body();
            Check(KingdomEnhancedPlugin.Logger.Errors.Count == 0, "生产代码未写错误日志");
            _pass++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            _fail++;
            Console.WriteLine("FAIL " + name + ": " + exception.Message);
        }
    }

    private static void Main()
    {
        H.Discover();

        Test("hook discovery: 四个 patching site 的标注与钩子数量", () =>
        {
            Check(H.DiscoveryError == null, "harness 发现失败: " + H.DiscoveryError);
            foreach (string key in new[]
            {
                "Player.UpdateActionState",
                "SteedAbility.Activate",
                "GlideMovementSteedAbility.Activate",
                "RunningAttackSteedAbility.OnPushedObjects"
            })
            {
                Check(H.Sites.ContainsKey(key), "缺少 site: " + key);
                var site = H.Sites[key];
                Check(site.Prefixes.Count == 1, key + ": 恰有一个 Prefix（不重复接管）");
                Check(site.Postfixes.Count == 1, key + ": 恰有一个 Postfix");
                Check(site.Prefixes[0].Method.IsStatic && site.Postfixes[0].Method.IsStatic, key + ": 钩子静态");
            }

            var actionState = H.Sites["Player.UpdateActionState"];
            Check(actionState.Finalizers.Count == 1, "UpdateActionState 有 finalizer 归还速率");
            Check(actionState.StateType != null, "UpdateActionState 用 __state 携带借用凭据");
            foreach (string abilityKey in new[]
            {
                "SteedAbility.Activate",
                "GlideMovementSteedAbility.Activate",
                "RunningAttackSteedAbility.OnPushedObjects"
            })
            {
                Check(H.Sites[abilityKey].Finalizers.Count == 0, abilityKey + ": 只 refill，无 finalizer");
                Check(H.Sites[abilityKey].StateType != null, abilityKey + ": 用 __state 固定本次 entry 身份");
                Check(H.Sites[abilityKey].StateType.IsClass, abilityKey + ": 身份 state 是引用类型（可空、off 不介入）");
            }
            Check(actionState.StateType.IsClass, "UpdateActionState 的 Borrow 是引用类型（只归还一次）");
        });

        Test("off/排除条件：零接管零写入，原生照常扣减", () =>
        {
            var off = NewPlayer();
            off.actionState = Player.ActionState.Run;
            off.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            var result = H.Run(off, 1, false, false, false, false);
            Check(result.Thrown == null && result.NativeRan, "off: 原生照常执行");
            Check(!H.Entered(result.State), "off: 前缀不接管");
            Check(off._steed.Writes.Count == 0, "off: patch 零写入");
            Eq(0.8f, off._steed.Stamina, "off: 原生照常消耗体力");

            void NoTakeover(string label, Action<Player, Steed> mutate)
            {
                ModConfig.InfiniteSteedStamina.Value = true;
                OptionalQoLScope.IsActive = true;
                var player = NewPlayer();
                var steed = player._steed;
                mutate(player, steed);
                var run = H.Run(player, 1, false, false, false, false);
                Check(!H.Entered(run.State), label + ": 不接管");
                Check(steed.Writes.Count == 0, label + ": patch 零写入");
            }

            NoTakeover("远端玩家(无本机控制权)", (player, _) => player.hasLocalAuthority = false);
            NoTakeover("主菜单/无世界", (_, __) => OptionalQoLScope.IsActive = false);
            OptionalQoLScope.IsActive = true;
            NoTakeover("旧 scene", (player, _) => OptionalQoLScope.Current.Remove(player));
            NoTakeover("无坐骑", (player, _) => player._steed = null);
            NoTakeover("坐骑不在当前 world 层", (_, steed) => OptionalQoLScope.Current.Remove(steed));
            NoTakeover("骑乘关系不匹配(他人骑乘)", (_, steed) => steed.Rider = new Player());
        });

        Test("四个速率：只临时归零负值、原样归还，正值从不写入", () =>
        {
            var player = NewPlayer(-0.4f, 0.05f, 0.3f, -0.6f);
            var steed = player._steed;
            steed.Stamina = 0.2f;
            ModConfig.InfiniteSteedStamina.Value = true;
            UnityEngine.Time.deltaTime = 0.02f;
            var result = H.Run(player, 0, false, false, false, false);

            Check(result.Thrown == null && H.Entered(result.State), "已接管");
            Check(Wrote(steed, "runStaminaRate", 0f), "负 run 速率当次归零");
            Check(Wrote(steed, "glideStaminaRate", 0f), "负 glide 速率当次归零");
            EqI(0, WriteCount(steed, "walkStaminaRate"), "正 walk 速率从不写入");
            EqI(0, WriteCount(steed, "standStaminaRate"), "正 stand 速率从不写入");
            Check(Wrote(steed, "Stamina", 1f), "体力拉满");
            Check(Wrote(steed, "_tiredTimer", 0f), "疲劳计时清 0");
            Check(H.OwnsRate(result.State, "Run") && H.OwnsRate(result.State, "Glide")
                && !H.OwnsRate(result.State, "Walk") && !H.OwnsRate(result.State, "Stand"),
                "__state 只持有被改写的字段");

            Eq(-0.4f, steed.runStaminaRate, "run 速率归还");
            Eq(0.05f, steed.walkStaminaRate, "walk 速率原样");
            Eq(0.3f, steed.standStaminaRate, "stand 速率原样");
            Eq(-0.6f, steed.glideStaminaRate, "glide 速率归还");
            Eq(1f, steed.Stamina, "Postfix 后体力为满");

            // 负 walk（部分坐骑步行也掉体力）：同样只当次归零后归还
            var walker = NewPlayer(-0.4f, -0.2f, 0.3f, -0.6f);
            ModConfig.InfiniteSteedStamina.Value = true;
            H.Run(walker, 0, false, false, false, false);
            Check(Wrote(walker._steed, "walkStaminaRate", 0f), "负 walk 速率当次归零");
            Eq(-0.2f, walker._steed.walkStaminaRate, "walk 速率归还");
        });

        Test("Run 极端 delta：对照组会扣穿疲劳，开启后不枯竭不疲劳", () =>
        {
            var control = NewPlayer(-2f, -0.2f, 0.3f, -0.6f);
            control.actionState = Player.ActionState.Run;
            control.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 3f;
            H.Run(control, 1, false, false, false, false);
            EqI(1, control._steed.BecomeTiredCalls, "对照组：单帧 3s 确实触发原生疲劳");
            Eq(0f, control._steed.Stamina, "对照组：体力被扣穿");
            Check(control._steed.IsTired, "对照组：进入疲劳");
            Check(control.actionState == Player.ActionState.Walk, "对照组：原生降级为 Walk");

            var player = NewPlayer(-2f, -0.2f, 0.3f, -0.6f);
            ModConfig.InfiniteSteedStamina.Value = true;
            player.actionState = Player.ActionState.Run;
            player.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 3f;
            var result = H.Run(player, 1, false, false, false, false);

            Check(result.Thrown == null, "无异常");
            EqI(0, player._steed.BecomeTiredCalls, "开启后不触发原生疲劳");
            Eq(1f, player._steed.Stamina, "开启后体力为满");
            Check(!player._steed.IsTired, "开启后非疲劳");
            Check(player.actionState == Player.ActionState.Run, "开启后保持 Run 不降级");
            Eq(-2f, player._steed.runStaminaRate, "极端帧后速率仍归还");
        });

        Test("Glide 极端 delta：对照组扣穿，开启后不枯竭", () =>
        {
            var control = NewPlayer(-0.4f, -0.05f, 0.3f, -2f);
            control.actionState = Player.ActionState.Glide;
            UnityEngine.Time.deltaTime = 3f;
            H.Run(control, 0, false, false, false, false);
            Eq(0f, control._steed.Stamina, "对照组：滑翔单帧扣穿");

            var player = NewPlayer(-0.4f, -0.05f, 0.3f, -2f);
            ModConfig.InfiniteSteedStamina.Value = true;
            player.actionState = Player.ActionState.Glide;
            UnityEngine.Time.deltaTime = 3f;
            var result = H.Run(player, 0, false, false, false, false);
            Check(result.Thrown == null, "无异常");
            Eq(1f, player._steed.Stamina, "开启后滑翔不枯竭");
            Eq(-2f, player._steed.glideStaminaRate, "速率归还");
        });

        Test("已疲惫的坐骑：开启即恢复、冲刺不再被 IsTired 挡住", () =>
        {
            var control = NewPlayer();
            control._steed.Stamina = 0f;
            control._steed._tiredTimer = UnityEngine.Time.time + 5f;
            control.actionState = Player.ActionState.Stand;
            H.Run(control, 1, true, false, false, false);
            Check(control.actionState == Player.ActionState.Rear, "对照组：疲劳时冲刺只 Rear");

            var player = NewPlayer();
            ModConfig.InfiniteSteedStamina.Value = true;
            player._steed.Stamina = 0f;
            player._steed._tiredTimer = UnityEngine.Time.time + 5f;
            player.actionState = Player.ActionState.Stand;
            var result = H.Run(player, 1, true, false, false, false);

            Check(result.Thrown == null, "无异常");
            Check(player.actionState == Player.ActionState.Run, "开启后直接 Run（疲劳被消除）");
            Eq(1f, player._steed.Stamina, "开启后立即满体力");
            Check(!player._steed.IsTired, "开启后 IsTired=false");
            Eq(0f, player._steed._tiredTimer, "_tiredTimer 清 0");
        });

        Test("步行/站立原生恢复路径不被改动", () =>
        {
            var player = NewPlayer(-0.4f, -0.05f, 0.3f, -0.6f);
            ModConfig.InfiniteSteedStamina.Value = true;
            player.actionState = Player.ActionState.Stand;
            UnityEngine.Time.deltaTime = 0.02f;
            H.Run(player, 0, false, false, false, false);

            Eq(0.3f, player._steed.standStaminaRate, "stand 恢复速率原样保留");
            EqI(0, WriteCount(player._steed, "standStaminaRate"), "stand 速率从未被写");
            EqI(0, WriteCount(player._steed, "WellFedTimer"), "饱食 timer 从未被写");
            EqI(0, WriteCount(player._steed, "walkSpeed"), "步行速度从未被写");
            EqI(0, WriteCount(player._steed, "runSpeed"), "奔跑速度从未被写");
            EqI(0, Player.DebugInfiniteStaminaWrites, "未写全局 DebugInfiniteStamina");
        });

        Test("关闭后：无干预、按现体力自然消耗、不回滚旧体力/旧疲劳", () =>
        {
            var player = NewPlayer(-0.4f, -0.05f, 0.3f, -0.6f);
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.Stamina = 0.2f;
            steed._tiredTimer = UnityEngine.Time.time + 5f;
            player.actionState = Player.ActionState.Run;
            player.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.1f;
            H.Run(player, 1, false, false, false, false);
            Eq(1f, steed.Stamina, "开启时体力拉满");
            Eq(0f, steed._tiredTimer, "开启时疲劳清除");

            ModConfig.InfiniteSteedStamina.Value = false;
            steed.ClearWrites();
            H.Run(player, 1, false, false, false, false);
            Eq(0.96f, steed.Stamina, "关闭后按原生速率消耗（不回滚到 1）");
            Eq(0f, steed._tiredTimer, "关闭后不回滚旧疲劳值");
            EqI(0, steed.Writes.Count, "关闭后 patch 零写入");
            Eq(-0.4f, steed.runStaminaRate, "关闭后速率保持原值");
        });

        Test("中途换坐骑：不碰新坐骑，旧坐骑速率照还", () =>
        {
            var player = NewPlayer();
            var first = player._steed;
            var second = new Steed { Label = "B" };
            second.runStaminaRate = -0.9f;
            second.glideStaminaRate = -0.7f;
            OptionalQoLScope.Current.Add(second);
            ModConfig.InfiniteSteedStamina.Value = true;
            first.MidCall = () =>
            {
                player._steed = second;
                second.Rider = player;
                first.Rider = null;
            };

            var result = H.Run(player, 0, false, false, false, false);
            Check(result.Thrown == null && H.Entered(result.State), "已接管旧坐骑");
            Eq(-0.4f, first.runStaminaRate, "旧坐骑 run 速率归还");
            Eq(-0.6f, first.glideStaminaRate, "旧坐骑 glide 速率归还");
            EqI(0, second.Writes.Count, "新坐骑零写入（不补满、不改速率）");
            Eq(-0.9f, second.runStaminaRate, "新坐骑速率未被归零");
        });

        Test("中途开关关闭/离开 world：不补满，但速率照还", () =>
        {
            void MidChange(string label, Action<Player, Steed> mutate)
            {
                var player = NewPlayer();
                var steed = player._steed;
                ModConfig.InfiniteSteedStamina.Value = true;
                player.actionState = Player.ActionState.Run;
                player.PrimePreviousDirection(1);
                UnityEngine.Time.deltaTime = 0.5f;
                steed.MidCall = () =>
                {
                    mutate(player, steed);
                    steed.Stamina = 0.42f;      // 原生执行期间被写入的值：Postfix 不得覆盖它
                };
                var result = H.Run(player, 1, false, false, false, false);
                Check(result.Thrown == null && H.Entered(result.State), label + ": 开始时接管过");
                Eq(0.42f, steed.Stamina, label + ": 条件失效后不再补满");
                Eq(-0.4f, steed.runStaminaRate, label + ": 速率仍归还");
                Eq(-0.6f, steed.glideStaminaRate, label + ": glide 速率仍归还");
            }

            MidChange("开关中途关闭", (_, __) => ModConfig.InfiniteSteedStamina.Value = false);
            MidChange("中途离开当前 world", (player, _) => OptionalQoLScope.Current.Remove(player));

            // 正向对照：条件仍成立时，同样的中途写入必须被 Postfix 拉满
            var player2 = NewPlayer();
            var steed2 = player2._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            player2.actionState = Player.ActionState.Run;
            player2.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            steed2.MidCall = () => steed2.Stamina = 0.42f;
            H.Run(player2, 1, false, false, false, false);
            Eq(1f, steed2.Stamina, "条件成立时 Postfix 兜底拉满");
        });

        Test("联机：客户端本机玩家生效、远端玩家不受影响、同机双人互不干扰", () =>
        {
            // 客户端本机玩家（本机没有世界权威）仍必须生效：patch 不得用 HasWorldAuth 排除
            NetworkBigBoss.HasWorldAuth = false;
            var client = NewPlayer();
            ModConfig.InfiniteSteedStamina.Value = true;
            client.actionState = Player.ActionState.Run;
            client.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            H.Run(client, 1, false, false, false, false);
            Eq(1f, client._steed.Stamina, "客户端本机玩家生效");
            Eq(-0.4f, client._steed.runStaminaRate, "客户端本机玩家速率归还");

            // 远端玩家（本机无控制权）：不接管
            var remote = NewPlayer();
            remote.hasLocalAuthority = false;
            remote.actionState = Player.ActionState.Run;
            remote.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            var remoteResult = H.Run(remote, 1, false, false, false, false);
            Check(!H.Entered(remoteResult.State), "远端玩家不接管");
            Eq(0.8f, remote._steed.Stamina, "远端玩家照常原生消耗");
            EqI(0, remote._steed.Writes.Count, "远端玩家零写入");
            NetworkBigBoss.HasWorldAuth = true;

            // 同机分屏：P1 与 P2 各自生效，互不触碰对方坐骑
            var p1 = NewPlayer(-2f, -0.2f, 0.3f, -0.6f);
            p1.playerId = 0;
            var p2 = NewPlayer(-2f, -0.2f, 0.3f, -0.6f);
            p2.playerId = 1;
            ModConfig.InfiniteSteedStamina.Value = true;
            p1.actionState = Player.ActionState.Run;
            p1.PrimePreviousDirection(1);
            p2.actionState = Player.ActionState.Run;
            p2.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 3f;
            H.Run(p1, 1, false, false, false, false);
            Check(p2._steed.Writes.Count == 0, "P1 的调用没有碰 P2 的坐骑");
            Eq(-2f, p2._steed.runStaminaRate, "P2 坐骑速率原样");
            H.Run(p2, 1, false, false, false, false);
            Eq(1f, p1._steed.Stamina, "P1 坐骑仍满");
            Eq(1f, p2._steed.Stamina, "P2 坐骑满");
            EqI(0, p1._steed.BecomeTiredCalls + p2._steed.BecomeTiredCalls, "同机双人都不疲劳");
        });

        Test("原生异常：原样透传不吞，速率照还且随后自愈", () =>
        {
            var player = NewPlayer();
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            var boom = new InvalidOperationException("native boom");
            steed.MidCall = () => throw boom;

            var result = H.Run(player, 0, false, false, false, false);
            Check(ReferenceEquals(result.Thrown, boom), "原生异常原样透传（finalizer 未吞、未包装）");
            Eq(-0.4f, steed.runStaminaRate, "异常路径：run 速率归还");
            Eq(-0.05f, steed.walkStaminaRate, "异常路径：walk 速率归还");
            Eq(-0.6f, steed.glideStaminaRate, "异常路径：glide 速率归还");

            steed.MidCall = null;
            player.actionState = Player.ActionState.Run;
            player.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            var second = H.Run(player, 1, false, false, false, false);
            Check(second.Thrown == null, "异常后再次调用正常");
            Eq(1f, steed.Stamina, "异常后功能仍生效");
            Eq(-0.4f, steed.runStaminaRate, "异常后速率仍归还");
        });

        Test("重入：内层看到已归零的速率不误接管，外层统一归还", () =>
        {
            var player = NewPlayer();
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            H.RunResult inner = null;
            object innerState = null;
            int depth = 0;
            steed.MidCall = () =>
            {
                if (depth++ > 0) return;   // 只重入一层：模拟同步回调里再调一次原生体
                inner = H.Run(player, 0, false, false, false, false);
                innerState = inner.State;
            };

            var outer = H.Run(player, 0, false, false, false, false);
            Check(outer.Thrown == null && inner != null && inner.Thrown == null, "两层都正常");
            Check(H.Entered(innerState), "内层接管（疲劳已被外层清掉）");
            Check(!H.OwnsRate(innerState, "Run") && !H.OwnsRate(innerState, "Walk")
                && !H.OwnsRate(innerState, "Stand") && !H.OwnsRate(innerState, "Glide"),
                "内层不误接管外层已归零的速率");
            Check(H.OwnsRate(outer.State, "Run") && H.OwnsRate(outer.State, "Glide"),
                "外层持有速率凭据");
            Eq(-0.4f, steed.runStaminaRate, "外层结束后 run 速率归还");
            Eq(-0.05f, steed.walkStaminaRate, "外层结束后 walk 速率归还");
            Eq(-0.6f, steed.glideStaminaRate, "外层结束后 glide 速率归还");
            Eq(1f, steed.Stamina, "两层结束后体力为满");
        });

        Test("外部逻辑改写速率：CAS 保留他人写入，未被动过的照还", () =>
        {
            var player = NewPlayer(-0.4f, -0.2f, 0.3f, -0.6f);
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.MidCall = () =>
            {
                steed.glideStaminaRate = 0.05f;    // 例：GlideMovementSteedAbility 改写滑翔速率
                steed.walkStaminaRate = -0.9f;     // 例：别的平衡逻辑改写步行速率
            };

            var result = H.Run(player, 0, false, false, false, false);
            Check(result.Thrown == null, "无异常");
            Eq(0.05f, steed.glideStaminaRate, "外部写入的 glide 速率被保留");
            Eq(-0.9f, steed.walkStaminaRate, "外部写入的 walk 速率被保留");
            Eq(-0.4f, steed.runStaminaRate, "未被外部改写的 run 速率仍归还");
        });

        Test("技能 Activate：消耗照扣、CD/排程/消耗值保持", () =>
        {
            var control = NewPlayer();
            var controlAbility = new SteedAbility { _steed = control._steed };
            UnityEngine.Time.time = 100f;
            H.RunSite("SteedAbility.Activate", controlAbility);
            Eq(0.75f, control._steed.Stamina, "对照组：原生扣掉 _staminaCost");
            Eq(130f, controlAbility._nextActivationTime, "对照组：原生按 _cooldown 排程");
            EqI(1, controlAbility.ActivateCalls, "对照组：原生 Activate 执行");
            EqI(0, controlAbility.Writes.Count, "对照组：patch 未写技能字段");

            var player = NewPlayer();
            var steed = player._steed;
            var ability = new SteedAbility { _steed = steed };
            ability._nextActivationTime = float.NegativeInfinity;
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.Stamina = 1f;
            steed._tiredTimer = UnityEngine.Time.time + 5f;
            steed.ClearWrites();
            var result = H.RunSite("SteedAbility.Activate", ability);

            Check(result.Thrown == null, "无异常");
            Eq(1f, steed.Stamina, "开启后技能消耗被补回，体力为满");
            Eq(0f, steed._tiredTimer, "开启后疲劳计时清 0");
            Eq(130f, ability._nextActivationTime, "技能 CD 排程与原生一致");
            Eq(0.25f, ability._staminaCost, "_staminaCost 保持原值");
            Eq(30f, ability._cooldown, "_cooldown 保持原值");
            Eq(2f, ability._duration, "_duration 保持原值");
            EqI(0, ability.Writes.Count, "patch 未写任何技能字段");
            EqI(0, Player.DebugInfiniteStaminaWrites, "未写全局 DebugInfiniteStamina");
        });

        Test("滑翔 Activate：体力门槛不再挡住滑翔（独立实现不调 base）", () =>
        {
            var control = NewPlayer();
            var controlAbility = new GlideMovementSteedAbility { _steed = control._steed };
            control._steed.Stamina = 0.1f;
            UnityEngine.Time.time = 100f;
            H.RunSite("GlideMovementSteedAbility.Activate", controlAbility);
            EqI(1, controlAbility.RearCalls, "对照组：体力不足 → 原生 Rear 放弃滑翔");
            Eq(0.1f, control._steed.Stamina, "对照组：提前返回未扣体力");

            var player = NewPlayer();
            var steed = player._steed;
            var ability = new GlideMovementSteedAbility { _steed = steed };
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.Stamina = 0.1f;
            steed.ClearWrites();
            var result = H.RunSite("GlideMovementSteedAbility.Activate", ability);

            Check(result.Thrown == null, "无异常");
            EqI(0, ability.RearCalls, "开启后不再因体力门槛 Rear");
            Eq(1f, steed.Stamina, "开启后滑翔消耗被补回");
            Eq(130f, ability._nextActivationTime, "滑翔 CD 排程与原生一致");
            EqI(0, ability.Writes.Count, "patch 未写技能字段");
            Eq(-0.1f, ability._staminaGlideRegen, "_staminaGlideRegen 未被改写");
        });

        Test("冲撞 OnPushedObjects：扣体力/加体力后都补满", () =>
        {
            var control = NewPlayer();
            var controlAbility = new RunningAttackSteedAbility { _steed = control._steed };
            H.RunSite("RunningAttackSteedAbility.OnPushedObjects", controlAbility);
            Eq(0.8f, control._steed.Stamina, "对照组：未撞到目标 → 原生扣 _attackStaminaCost");

            var player = NewPlayer();
            var steed = player._steed;
            var ability = new RunningAttackSteedAbility { _steed = steed };
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.ClearWrites();
            var result = H.RunSite("RunningAttackSteedAbility.OnPushedObjects", ability);
            Check(result.Thrown == null, "无异常");
            EqI(1, ability.PushCalls, "原生冲撞逻辑照常执行");
            Eq(1f, steed.Stamina, "未撞到目标（原生扣）后补满");

            ability.HitPushedLayer = true;
            steed.Stamina = 0.8f;
            H.RunSite("RunningAttackSteedAbility.OnPushedObjects", ability);
            Eq(1f, steed.Stamina, "撞到目标（原生加）后仍为满");
            Eq(0.2f, ability._attackStaminaCost, "_attackStaminaCost 未改写");
            Eq(0.1f, ability._attackStaminaGain, "_attackStaminaGain 未改写");
            EqI(0, ability.Writes.Count, "patch 未写技能字段");
            EqI(2, ability.PushCalls, "两次冲撞都执行了原生逻辑");
        });

        Test("技能消耗发生在 UpdateActionState 内：Postfix 先补满，紧接的 Run 分支不疲劳", () =>
        {
            var control = NewPlayer(-0.4f, -0.2f, 0.3f, -0.6f);
            var controlSteed = control._steed;
            var controlAbility = new RunningAttackSteedAbility
            { _steed = controlSteed, _attackStaminaCost = 1.5f };
            control.actionState = Player.ActionState.Run;
            control.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            controlSteed.MidCall = () => H.RunSite("RunningAttackSteedAbility.OnPushedObjects", controlAbility);
            H.Run(control, 1, false, false, false, false);
            EqI(1, controlSteed.BecomeTiredCalls, "对照组：这一笔技能消耗把坐骑打进疲劳");

            var player = NewPlayer(-0.4f, -0.2f, 0.3f, -0.6f);
            var steed = player._steed;
            var ability = new RunningAttackSteedAbility { _steed = steed, _attackStaminaCost = 1.5f };
            ModConfig.InfiniteSteedStamina.Value = true;
            player.actionState = Player.ActionState.Run;
            player.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            steed.MidCall = () => H.RunSite("RunningAttackSteedAbility.OnPushedObjects", ability);
            var result = H.Run(player, 1, false, false, false, false);

            Check(result.Thrown == null, "无异常");
            EqI(0, steed.BecomeTiredCalls, "技能 Postfix 补满后紧接的 Run 分支不疲劳");
            Eq(1f, steed.Stamina, "体力回到满");
            Check(player.actionState == Player.ActionState.Run, "仍在 Run，未被降级为 Walk");
            Eq(-0.4f, steed.runStaminaRate, "外层速率照还");
            Eq(-0.6f, steed.glideStaminaRate, "外层 glide 速率照还");
        });

        Test("技能入口的排除条件：零补满、原生消耗照常", () =>
        {
            void NoRefill(string label, Action<Player, SteedAbility> mutate)
            {
                OptionalQoLScope.IsActive = true;
                var player = NewPlayer();
                var steed = player._steed;
                var ability = new SteedAbility { _steed = steed };
                ModConfig.InfiniteSteedStamina.Value = true;
                steed.Stamina = 0.4f;
                steed.ClearWrites();
                mutate(player, ability);
                var result = H.RunSite("SteedAbility.Activate", ability);
                Check(result.Thrown == null, label + ": 原生照常执行");
                Check(steed.Stamina < 0.4f, label + ": 原生消耗照常发生、未被补满");
                Check(steed.Writes.Count == 0, label + ": patch 零写入");
            }

            NoRefill("开关关", (_, __) => ModConfig.InfiniteSteedStamina.Value = false);
            NoRefill("远端玩家（本机无控制权）", (player, _) => player.hasLocalAuthority = false);
            NoRefill("玩家不在当前 world", (player, _) => OptionalQoLScope.Current.Remove(player));
            NoRefill("坐骑不在当前 world", (_, ability) => OptionalQoLScope.Current.Remove(ability._steed));
            NoRefill("骑乘关系不匹配", (_, ability) => ability._steed.Rider = new Player());
            NoRefill("坐骑已被换下（player._steed 指向别处）",
                (player, ability) => player._steed = new Steed { Rider = player });
            NoRefill("主菜单/无世界", (_, __) => OptionalQoLScope.IsActive = false);
            NoRefill("ability 不在当前 world（旧 scene）",
                (_, ability) => OptionalQoLScope.Current.Remove(ability));
        });

        Test("串线防护：远端/旧 player 的 _steed 误指本地坐骑时被拒", () =>
        {
            var local = NewPlayer();
            var steed = local._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.Stamina = 0.5f;
            steed.ClearWrites();

            var remote = new Player { playerId = 1, hasLocalAuthority = false };
            remote._steed = steed;
            remote.actionState = Player.ActionState.Run;
            remote.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            var r1 = H.Run(remote, 1, false, false, false, false);
            Check(!H.Entered(r1.State), "远端 player 不接管（即使 _steed 误指本地坐骑）");
            EqI(0, steed.Writes.Count, "本地坐骑零写入（patch 未介入）");
            Eq(0.3f, steed.Stamina, "本地坐骑只经历原生扣减 0.5 - 0.4*0.5");

            var stale = new Player { playerId = 0 };
            stale._steed = steed;
            stale.actionState = Player.ActionState.Run;
            stale.PrimePreviousDirection(1);
            var r2 = H.Run(stale, 1, false, false, false, false);
            Check(!H.Entered(r2.State), "不在当前 world 的旧 player 不接管");
            EqI(0, steed.Writes.Count, "旧 player 同样零写入");
            Eq(0.1f, steed.Stamina, "仍只有原生扣减 0.3 - 0.4*0.5");

            var rider = new Player { playerId = 0 };
            rider._steed = steed;
            rider.actionState = Player.ActionState.Stand;
            OptionalQoLScope.Current.Add(rider);   // 本机有控制权、在当前 world，但 Steed.Rider 是别人
            var r3 = H.Run(rider, 0, false, false, false, false);
            Check(!H.Entered(r3.State), "Steed.Rider 不匹配时不接管");
            EqI(0, steed.Writes.Count, "rider 不匹配零写入");
            Eq(0.25f, steed.Stamina, "rider 不匹配只有原生站立恢复 0.1 + 0.3*0.5");
        });

        Test("中途失权：Postfix 不再补满，但借用的速率照还", () =>
        {
            var player = NewPlayer();
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            player.actionState = Player.ActionState.Run;
            player.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            steed.MidCall = () =>
            {
                player.hasLocalAuthority = false;   // 本机失去该 player 的控制权
                steed.Stamina = 0.33f;              // 之后原生/别的逻辑写入的值
            };
            var result = H.Run(player, 1, false, false, false, false);
            Check(result.Thrown == null && H.Entered(result.State), "开始时接管过");
            Eq(0.33f, steed.Stamina, "失权后不再补满");
            Eq(-0.4f, steed.runStaminaRate, "失权后 run 速率仍归还");
            Eq(-0.6f, steed.glideStaminaRate, "失权后 glide 速率仍归还");
        });

        Test("prefix 写字段中途异常：已记凭据的字段归还、原生照常执行", () =>
        {
            var player = NewPlayer(-0.4f, -0.2f, 0.3f, -0.6f);
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            steed.ThrowOnWrite = "glideStaminaRate";   // 写第 4 个字段时抛
            steed.ClearWrites();
            var result = H.Run(player, 0, false, false, false, false);

            Check(result.NativeRan, "原生体照常执行（prefix 未把异常放出去）");
            Check(result.Thrown == null, "异常未外抛、原生未被吞掉");
            Eq(-0.4f, steed.runStaminaRate, "已写入的 run 速率归还");
            Eq(-0.2f, steed.walkStaminaRate, "已写入的 walk 速率归还");
            Eq(-0.6f, steed.glideStaminaRate, "抛异常字段保持原值（CAS 无副作用）");
            Eq(0.3f, steed.standStaminaRate, "正速率始终不动");
            EqI(1, KingdomEnhancedPlugin.Logger.Errors.Count, "前缀异常被记录一次");
            KingdomEnhancedPlugin.Logger.Errors.Clear();
        });

        Test("Postfix 归还后外部把速率改 0：Finalizer 不再次覆盖", () =>
        {
            var player = NewPlayer(-0.4f, -0.2f, 0.3f, -0.6f);
            var steed = player._steed;
            ModConfig.InfiniteSteedStamina.Value = true;
            player.actionState = Player.ActionState.Run;
            player.PrimePreviousDirection(1);
            UnityEngine.Time.deltaTime = 0.5f;
            H.AfterPostfix = () =>
            {
                steed.runStaminaRate = 0f;   // 本 patch Postfix 与 Finalizer 之间别的逻辑写入
                steed.ClearWrites();         // 之后只看 Finalizer 有没有再写
            };
            var result = H.Run(player, 1, false, false, false, false);
            Check(result.Thrown == null && H.Entered(result.State), "已接管");
            Eq(0f, steed.runStaminaRate, "外部写入的 0 被保留（Finalizer 未覆盖）");
            EqI(0, WriteCount(steed, "runStaminaRate"), "Finalizer 没有写 run 速率");
            Eq(-0.2f, steed.walkStaminaRate, "walk 保持第一次归还的结果");
        });

        Test("技能入口 off→on：Prefix 未介入则 Postfix 不无端补满", () =>
        {
            var player = NewPlayer();
            var steed = player._steed;
            var ability = new SteedAbility { _steed = steed };
            steed.ClearWrites();
            steed.MidCall = () => ModConfig.InfiniteSteedStamina.Value = true;   // 原生体内才开
            var result = H.RunSite("SteedAbility.Activate", ability);
            Check(result.Thrown == null, "无异常");
            Check(!H.AbilityEntered(result.State), "Prefix（off）未建 state");
            Eq(0.75f, steed.Stamina, "Postfix 未无端补满（原生消耗保留）");
            EqI(0, steed.Writes.Count, "patch 零写入");
        });

        Test("技能中途换 steed：只补前缀捕获的坐骑，不碰新 target", () =>
        {
            var player = NewPlayer();
            var captured = player._steed;
            var replacement = new Steed { Label = "R" };
            replacement.runStaminaRate = -0.9f;
            OptionalQoLScope.Current.Add(replacement);
            var ability = new SteedAbility { _steed = captured };
            ModConfig.InfiniteSteedStamina.Value = true;
            replacement.Stamina = 0.3f;
            replacement.ClearWrites();
            captured.ClearWrites();
            captured.MidCall = () => ability._steed = replacement;
            var result = H.RunSite("SteedAbility.Activate", ability);
            Check(result.Thrown == null && H.AbilityEntered(result.State), "Prefix 已介入");
            Eq(0.3f, replacement.Stamina, "新 target 体力未被改写");
            EqI(0, replacement.Writes.Count, "新 target 零写入");
            Eq(-0.9f, replacement.runStaminaRate, "新 target 速率未被改写");
            Eq(1f, captured.Stamina, "前缀捕获的坐骑仍被补满");
        });

        Console.WriteLine($"RESULT: {_pass} passed, {_fail} failed; {_assertions} assertions");
        Environment.ExitCode = _fail > 0 ? 1 : 0;
    }
}
