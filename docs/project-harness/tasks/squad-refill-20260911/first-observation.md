# 首次玩家诊断日志 — 2026-09-11 22:28
已归档运行日志于operator workspace squad-refill-20260911/player-first-probe.log，未停止仍运行的游戏，未改DLL或存档。

新build正确加载。一个SquadRoster sample=1/5：style0 3骑士12/12 alive12 mismatch0 far10=3；style1 4骑士16/16 alive16 mismatch0 far10=0；style2 7骑士28/28 alive28 mismatch0 far10=4；style3 2骑士8/8 alive8 mismatch0 far10=0；style4 6骑士24/24 alive24 mismatch0 far10=0。合计22骑士，名册88/容量88，存活归属88。truncated=False，readMs=2.28，仅快照读取/格式化，不是总帧或logger成本。

目前没有Fetch完成事件；只有一个Norse disabled事件dead=False，处于加载转化阶段，不能视为战死。稍后普通follower diag仍withKnight88。末尾ClockDiag停在t14.19 timeScale0，游戏处于暂停。

该样本没有复现缺员或名册人数失配；发现中世纪3、幕府4名随从离自己骑士>10格，但采样发生在读档早期，不能单次判定持续返队bug或替代用户死亡不补员报告。下一次稳定采样/真实伤亡需要继续运行。未实施猜测性招募规则或跟随修复。
