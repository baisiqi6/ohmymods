# 农民与攻城弹药自动补货

用户新增Farmer（镰刀种田职业，区别已有Peasant面包）、投石车火药桶（原价5）、Greek FireTower弹药（原价2）独立可设置阈值自动补货，并要求左上人数HUD显示两类现有弹药数量。保持自动采购仅Greek，所有世界HUD可显示真实本岛库存（host/单机；客机不可用不报0）。新项默认false/目标15，1–200全岛阈值；已有配置保持。新弹药暂沿用现有自动2x价，已向用户询问可按回复调整。

基线B8200382/6.1.5-optional-qol-20260914。保留以前所有未提交热修、禁写D Steam、冻结Mono、不commit/push/发布；只允许正确E独立测试副本安装。OMP18.1.19 realtime model目录验证DeepSeek Flash max，两个隔离worker：service-worker扩既有同一订单队列+Farmer计数；ammo-worker新只读SiegeAmmoCounts缓存。root统一配置/HUD、actual2.4 ABI/native/资源审计、回归和受控启动。用户允许内置reviewer，supply_reviewer只读复核。

库存口径：未消耗可用弹药，而非购买次数。投石车=实际运输桶+queued+真实仍装在spoon上的OilBarrel（包括buff生成的这一发，不把buff当无限储备；不按当前buffflag猜来源）；FireTower只用_fireJarsActiveNum，原生已含装填中。不算已发射弹、假展示sprite或未付款订单；订单reserved仅用于阈值覆盖。多站点去重，交易前force读真实库存，原生CanPay/owner/Greekbank/网络/玩家占用/容量门保持。不直接增弹、不创建第二扣款队列。

验证：新增直接链接production的Farmer/两ammo/多站点/交接/发射/buff/部分异常/世界及authority变化/关闭/阈值/超购/扣款回归；原20组回归、actualAPI/native唯一长hook、独立review、正确E受控启动且save/config/bank保持。实际入店购买/滚桶装填/火塔消耗及联机尚待时不能标done。

证据：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/restock-supplies-20260914。
