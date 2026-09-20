# 三个可选便捷开关

用户要求：长按起初原版、随后快速投槽且同店商品连续购买；灌木密度增加一倍，关闭只让额外批次快速枯萎，全部清完前不能再开启；砍树后森林消退加速也独立开关。均默认off，保留原版体验。

用户已确认：三个新开关均在所有世界可启用，森林消退采用3倍。此前银行及缩放仅Greek的范围保持。Operator已核实Player.UpdatePayState、Grass/World thicket与ForestItem原生fade链；actual2.4 interop cctor原始token和候选native地址在证据目录。不得以2.1参考源代替实际ABI验证。

基线本机2B27CCC0 / 6.1.5-greek-bank-scope-20260914，保持此前银行仅Greek、缩放、兔子原大小、鹿3倍与隐士安全热修等所有未提交源码。不commit/push/发布，不写D盘Steam，不停止用户游戏、不恢复旧存档。

执行：两个隔离OMP18.1.19 workers均deepseek/deepseek-v4-flash thinking=max，分别新建长按购买与植被模块，不能改ModConfig/Panel/Plugin；operator统一接口、native审计、直接回归和集成。独立内置reviewer只读检查（用户已允许）。

验收：off时各世界原版、on时所有世界可用；连续hold安全白名单、原生完整付款不重扣、松开/换店/钱不足/满货架/暂停/两玩家；原生与extra灌木身份、关闭阶段保留原生和回收完成门、未知/失权/异常/世界往返；forest仅缩短正在消退链，不砍活树。新直接回归、既有相关回归、实际Unity API及native target审计、独立review、正确E启动。真实长按/枯萎/连续砍树/联机未测不能标done。

证据：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/optional-qol-20260914。
