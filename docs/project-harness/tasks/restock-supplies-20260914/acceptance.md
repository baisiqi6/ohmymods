# 农民与攻城弹药补货验收

本机正确E独立测试副本已安装00FA75CA / build=6.1.5-restock-supplies-20260914。F5自动补货新增农民·镰刀、投石车·火药桶、希腊火焰塔·弹药，各自开关与1–200全岛阈值，默认off/15；现有Peasant面包保留。自动采购仍仅Greek、同一银行与税收官订单队列、两单全局上限；沿用原价2倍（火药桶10、火塔弹药4），手动原价不变。左上常驻人数HUD新增两弹药数量，单机/主机所有world可读，不受自动开关影响。

21组回归全部通过：服务119、职业/商店缓存84、HUD32、弹药36；含旧bank32/真实助手17、长按25、植被31、scope17及其他既有套件。完整IL2CPP强制Rebuild 0W0E；310实际Unity可达方法无unstripping stub。1531旧方法中1481完全不变，变化仅本任务允许类；全部旧Harmony属性保留，仅新增PayableManager Add/Remove两个唯一长native目标（656/608B）。银行、缩放、兔子、鹿、隐士与三个QoL原有热修保持。独立内置review最终无阻断。

精确候选受控启动PID29096约110秒，Chainloader及RunningGame成功。原生弹药缓存日志：[Info   :KingdomEnhancedMod] [SiegeAmmoCounts] ready=true sites=0 barrels=0 fireJars=0。当前自动加载场景无弹药站点，已验证可信空库存与HUD启动，不能据此声称真实采购成功。存档3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A、配置逐字节与金库5709→5709保持；隐士短getter17原字节未改。仅既有NpcShieldUser.SetShieldEnabled错误，未新增本任务错误。测试结束先恢复基线再安装同一候选。

库存按当前可用实物，不按付款次数：transport（含delivered等待交接）+按Catapult身份去重的queued/实际spoon OilBarrel；buff提供的当前loaded一发也计入，不推算未来弹药。FireTower字段已含loaded，只读_fireJarsActiveNum。旧owner引用不抢去重键，counterpart缺失pending不发布低数；最终commit强制读真实库存，检查精确owner、Payable.enabled、原生CanPay、biome-resolved barrel pool与tower自己的jar RPC。八目标及两个版本逐项比较，避免整数拼接/哈希漏变化。

待正常游玩验证：真实农民购镰刀/拾取，投石车购买滚桶→装填→发射，火塔购买→消耗→再次补给、多站点均衡、换岛读档与两机联机同步、HUD实屏观感。checklist保持doing；未commit/push/更新公开版本，未写D盘Steam，未回滚旧存档。原价/双倍提问未获回复，按已说明的现有双倍规则实现，可按后续用户回复调整。

证据目录：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/restock-supplies-20260914。OMP18.1.19双独立worker实际metadata均deepseek/deepseek-v4-flash、thinking=max；operator集成修正，reviewer /root/supply_reviewer只读。worker-receipts.json保存模型/session/allowlist。
