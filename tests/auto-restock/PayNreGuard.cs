// 2026-09-24 Issue #49 实机根因回归：PayableShop.Pay 的 ShopScythe/ShopForge 统计行在
// interactingPlayer 为 null 时 NRE，且该异常被 Il2CppInterop trampoline 吞掉——原生调用方
// 与我们的托管 catch 都看不到，Pay 死在 CreateItem 之前（GetItemCount 不变）而 FinalizePurchase
// 照记成功 = 扣款不出货死循环。三道闸：付前确保玩家附着（无玩家取消不扣款）、付后实物验证
// （不出货即 fault 拉黑本店）。
using System;
using KingdomEnhancedMod;
namespace AutoRestockTests;
internal static class PayNreGuard
{
    public static void Run()
    {
        Program.Run("nre_guard_attaches_player_for_offline_pay", () => { var e = Program.NewEnv(); Program.Role(0, true, 3); AutoRestockCounts.SetLive(0, 2);
            var s = e.MakeShop(0, 4); e.Banker._stashedCoins = 20;
            Program.Eq(null, s.interactingPlayer, "precondition: no player attached");
            Player seenDuringPay = null;
            s.TransactionCompleteF = _ => seenDuringPay = s.interactingPlayer;
            e.Tick();
            Program.Ok(Program.PumpUntil(e, () => s.TransactionCompleteCalls == 1 && Program.Reserved == 0, 20),
                "offline pay completes with our attached player");
            Program.Ok(seenDuringPay != null, "the pay carried an attached player");
            Program.Eq(1, s.Items, "native stock incremented");
        });
        Program.Run("nre_guard_cancels_without_crowned_player", () => { var e = Program.NewEnv(); Program.Role(0, true, 3); AutoRestockCounts.SetLive(0, 2);
            var s = e.MakeShop(0, 4); e.Banker._stashedCoins = 20;
            e.Kingdom.CrownFinder = _ => null;
            e.Tick();
            Program.Ok(Program.PumpUntil(e, () => Program.Reserved == 0, 20), "order resolves");
            Program.Eq(0, s.TransactionCompleteCalls, "no native pay without a player");
            Program.Eq(0, Program.Spend, "no debit without a player");
        });
        Program.Run("nre_guard_paid_no_item_blacklists_shop", () => { var e = Program.NewEnv(); Program.Role(0, true, 3); AutoRestockCounts.SetLive(0, 2);
            var s = e.MakeShop(0, 4); e.Banker._stashedCoins = 20;
            s.SuppressItemSpawn = true;          // native Pay died before CreateItem
            e.Tick();
            Program.Ok(Program.PumpUntil(e, () => s.TransactionCompleteCalls == 1, 20), "first pay attempted");
            for (int i = 0; i < 6; i++) { Time.time += 3; e.Tick(); }
            Program.Eq(1, s.TransactionCompleteCalls, "paid-no-item faults blacklist the shop");
            Program.Eq(1, Program.Spend, "exactly one debit: the leak fuse holds");
        });
    }
}
