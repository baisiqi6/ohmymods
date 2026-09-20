# 弓箭可选增强验收

已实现并仅安装正确E独立测试副本：1F111CD5 / build=6.1.5-archer-options-20260914。

F5「弓箭」页，三个独立默认off开关：散射总箭数1–5（含原生主箭，默认3）；射速1–2倍（默认1.5、步长0.25）；纯视觉火焰命中特效。按已说明假设全部世界可手动启用；银行和原缩放仍Greek-only。战斗修改由单机/主机权威执行；特效当前仅单机/主机画面，不含新客机FX RPC。大规模齐射可能因预算少发额外箭，不删除/挪动原生箭。

参考作者DLL仅Mono.Cecil只读分析；没有执行或安装该DLL。两个实现worker的provider-native session均核验deepseek/deepseek-v4-flash max，各仅改自己的新production文件，canonical由root整合；root最后修复异常回执ID读取保留、spawn后立刻登记及缺body保留ledger，新增3项回归。内置reviewer完成多轮独立code/native复核，最终要求已落实。

24套测试通过：既有21套、combat75、visual36、实际scope27。完整IL2CPP强制Rebuild0警告0错误；278实际Unity可达方法无unstripping失败。1580个旧方法中1575完全不变，5处变化仅Plugin标记、ModConfig.Init、ModPanel.Update/DrawControls/.cctor；无旧方法删除、全部旧Harmony属性保持。六个native目标均核对实际2.4唯一长入口，Shoot.MoveNext复用既有target，未新增短getter/Dispose钩子。实际Harmony PatchSorter次序+模拟normal/exception共3项通过（非.NET8 managed detour实测）。

精确候选受控启动PID27652约110秒，chainloader及RunningGame成功；隐士短getter17原字节保持。save 3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A保持，配置逐字节恢复，银行5709→5709。只见既有NpcShieldUser.SetShieldEnabled NRE，本任务无新错误。测试结束先恢复基线00FA75CA，再原子安装同一候选并核对源清单。未commit/push/更新公开版本，未写D盘Steam，不修改Mono，不回滚用户存档。

仍待真实玩法验证：主动开启后的散射轨迹与命中火焰观感、射速倍率手感/死地增益/弩手转职、密集弓箭手持续负载、换岛读档和两机同步。此次实机启动使用默认关闭的新开关，不能声称已完成以上正例；checklist保持doing。作者逐命中新建Mesh的实现改为16槽三层LineRenderer复用，视觉为同类效果复刻，并非逐像素一致。

证据：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/archer-options-20260914。build.txt、test-results/archer-combat.trx、clean-test-results.json、unity-audit.txt、native-methods.json、native-disassembly.txt、harmony-order.txt、worker-receipts.json、verification.json、permanent-receipt.json、installation.json、task.diff。早期独立.NET8进程尝试旧MonoMod managed detour导致CLR自检失败，未执行游戏/改变游戏状态；随后改为实际PatchSorter纯排序核验。最终游戏IL2CPP启动单独通过。
