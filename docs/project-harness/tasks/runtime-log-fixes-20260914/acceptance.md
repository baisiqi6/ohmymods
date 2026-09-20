# 运行日志局部修复候选验收

2026-09-14本机1D63533A/build7.6.5-runtime-log-fixes-20260914：火塔满仓明确等待消耗并核真实可用槽，保留原CanPay/扣款；Rewired关闭后只跳Menu.SetMenuInput(false)失效输入写，保留其余退出清理。120补货+36弹药+14容量+8菜单、2interop编译、0W0E/2617API/2242无关方法保持/独立review通过。闭游戏安装备份9881275D，save57E54166/configA3E3A0B8保持，未启动/提交/发布；保留骑士独立档及所有旧修复，实际退出/补货与附加档保存读档仍待验。

## 实现与证据

FireTowerRestockCapacity只读校验role7的真实_owner、active/enabled、_maxFireJars与_fakeFireJars长度、当前索引以及即将使用的槽对象非null；新增已满等待和未就绪原因。ShopBuyable在原生CanPay前增加此门控，支付前仍复核，无弹药/容量/价格写入。实际native FireTower.CanPay只有16B，未钩它。

PatchLifecycle_MenuInput只在关闭输入且Rewired.ReInput.isReady明确false时跳过Menu.SetMenuInput；开启输入/已就绪/读取失败均放行原生，日志每类一次。不是吞整个OnDisable异常；原有动画、材质和事件清理不被跳过。新增Rewired_Core引用指向正确E实际2.4interop（开发deps与游戏interop哈希不同，最终生产构建与独立编译均使用正确目标）。Menu.SetMenuInput原生0x603030/208B/same_slots1，未新增短getter hook。

两名OMP实施worker均deepseek/deepseek-v4-flash thinking=max，最终身份见worker-identities.json。菜单worker在写最终说明前达到6分钟deadline；代码、8用例测试、实际interop和完整构建的verification-receipt已全部产出且退出码0，Operator重新运行canonical测试及干净构建验收，没有改用内置worker。内置knight_identity_design_review只读最终复核通过；其建议的空槽检查已补并回归。

6个针对项目：120补货服务、36弹药统计、14火塔helper、8菜单guard；另2个实际interop编译门。完整构建0W0E；2617 Unity调用路径无unstripping失败；方法体对比2242既有方法保持，仅Plugin.Init、AutoRestock.ShopBuyable(out reason)、AutoRestock静态原因表初始化变动。骑士附加档/网络/武士/希腊火矢实现保持。

## 只定位、未修改的线索

- 旧Player.log的10次CastleShieldShop InvalidNetID0之后有10对Tower Knight_greece代理同步请求和立即注册。当前存档为4个城堡旗NetID926/927/929/928，以及10个NetID0的附属旗；原生TowerKnight.SpawnShield使用syncSelf=false，PayableShield.ApplyData三帧后恢复mediator。高度符合原生分阶段代理同步；同名日志无instanceID，不能逐对象证明对应关系，因此不强行分配新ID、不更改注册。
- 单条Crossbowman scale drift不能证明持续漂移，也不能证明已自愈；同数量后续日志可能去重，保留观察项。尚未确认更晚动画写入者。
- DefensePerf峰值无归因；不宣称与箭数量直接因果或已优化。
- 原生卸载音频警告没有新的致命堆栈依据，既有shield-audio guard保持；不笼统屏蔽日志。
- 武士延至1秒仅讨论，DashTimeout不是实际持续时间（旧记录0.15/0.10s，完整尾迹清理到0.362/0.294s）；延长战斗可能延长无敌与新目标命中窗口，本次未改。

## 部署与未实测

DLL 1D63533A4D74B9F8B60572FD595A58D8FC228C28DC860BEEB4179916C1665B1A，路径 E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll。
备份 E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-runtime-log-fixes-20260914-171341-863.bak。
当前save 57E54166C813EEA47B85AC44DC47D779A7AAA5D41B73651B1F00AED33C4E2AAB 和config A3E3A0B876BB5D421B681FFF5919FCE100BBAAEC043F083A36933F3114EA5439 前后一致；未覆盖存档/配置，未启动游戏、未提交发布。
原始日志留operator任务目录，不自动复制完整日志到repo/公开材料。旧日志为samurai-diag构建，不能证明identity或本轮guard实际运行。真实退出、火塔消耗后补货、主客机仍待实测，任务保留doing。
