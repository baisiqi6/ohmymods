# 五帧红焰预览候选

2026-09-17 侧边转交红色枪口喷焰，用户继而要求四五帧预览。最终5帧各50ms，Fire总.25秒保持，射速/装填/弹丸/伤害不改。现有可编辑像素source派生：root draw_preview.py只替换Fire口部红焰，worker负责67帧表/anchor/既有测试；备用worker生成器未使用。atlas尺寸672x192/56x32/PPU32/pivot31,2/.9保持，Fire36..40，Reload41/Lower53/Retreat59；new0..35及new41..66映射old40..65逐像素保持，身体枪手脚及x55空边保护，旧十字移除无叠层。预览候选bca6aaeb / build=9.0.0-musketeer-red-muzzle-20260917；79runtime、actual2.4接口/完整build0W0E、67anchor/9clip与像素保护及独立review通过；仅Atlas资源变化，其他7PNG和daylight/伤害功能保持。用户后续采用预览后，已闭游戏备份安装，28份原生档/附加档/配置hash保持。未启动游戏、改存档配置、commit/push/publish，公开9.0.0保持。五帧红焰.gif为素材预览非游戏录像，实机观感/低帧率采样待验，任务doing。
