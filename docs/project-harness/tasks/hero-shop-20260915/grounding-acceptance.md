# 英雄驿站高度修复验收

2026-09-15 英雄驿站沉地修复已闭游戏备份安装E盘：d005c4e1 / build=8.0.0-hero-shop-grounding-20260915。前版f8c25095已真实通过owner预检、ready与purchasecompleted，但用户截图下半部被地面挡。实际94商店资源rootY0.875/0.88、rootbody bottompivot0/PPU32，旧自有root错用GameLayer.y。现仅取当前world活动Bow/Hammer/Scythe、根body可用且pivot0的worldY，主体/旗/币槽一起抬升，缺参考延后；保留自有pivot2px及底边约1px草沿，不改PNG/横向选址/z/付款。Core39/Invoker7/actualinterop与完整build0W0E、独立资源/代码review通过；对f8审计2868旧方法全同，仅Create和buildstamp改变、2新增方法、4PNG保持。备份/安装hash与全部用户数据hash通过，未启动/提交/发布；新高度仍待用户截图实测，跨岛仍待。

## 原因与证据

用户截图见receipts/grounding/before.png，原版资源提取见resource-grounding.json。93对象Y=0.875、1对象Y≈0.88，94body root/pivot0/32PPU。自有alpha bbox(2,14,124,79)、pivot底2px不变；最低可见边在根下约1px，不把这个正常草沿余量与旧28px整体沉地混为一谈。

## 实施与审查

OMP18.1.19 DeepSeekV4Flash/max worker在授权HeroShop/tests范围实现，model_change非fallback。worker自身bash权限拒绝后由operator直接完成全部验证。Operator按review收窄为Bow/Hammer/Scythe及rootbody/pivot条件，独立reviewer重读冻结版本通过。外部worker已结束，无继续后台实施。

Core39、Invoker7、actualinterop与完整构建通过；精确DLL方法对照见receipts/grounding/dll-audit.json。没有改变原生对象、购买身份/战斗、同root局部旗帜位置和币槽配置，没有额外hook。

## 本机安装

目标：`E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll`

备份：`E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-grounding-fix-20260915-200717-743.bak`

安装时进程关闭，前后用户文件hash一致，没有操作游戏启动。该高度修正还需下一次真实截图验证；此前ready和purchase只证明owner修复及实际商店付款正例。
