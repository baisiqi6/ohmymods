# 自动补货仅白天候选

基线 DLL：63EA50ADDE922CC1600EF11B8D2C4CFE10FBB12C555D83914CED55DA7DBD354A（不是 Git commit）。候选 DLL：96566DEF78BC6F990234BA1FEAB2CA45273B539118FA8B86FFF4EEE9B23DA202，build=9.0.0-restock-daylight-20260917，公开版本保持9.0.0。

最终独立复核PASS后，已确认游戏关闭、备份并安装此精确候选到既定E盘独立测试副本；候选/旧DLL/备份/安装后哈希核验通过，28份原生档/附加档/配置哈希保持，未启动游戏。详见receipts/install.json，项目checklist验证通过，实机验收仍为doing。

全部9类自动采购统一按原版Kingdom.isDaytime判断白天，不使用系统时间、固定钟点或Director.IsNight反值。夜间不规划/借用采购助手/发补货动画币/提交自动扣款，未付订单撤回并释放预算和助手；已付订单只收尾，不退款或再次购买。天亮后按现有taxTick及扫描节奏重新计算缺口。夜间暂停时保留暂停语义，恢复运行后清理未付订单，暂停期间不推进交易。

复用现有Reset(true)归还采购助手，保留故障目标回执跨夜，普通收税和正常银行存取、玩家手动购买不受限制。保留Greek-only/host、双倍价、库存与阈值、选中/网络等原规则。FireTower弹药、火药桶及火枪同样受限，火枪直接自动采购最终仍经过白天扣款门。

计数刷新/订单推进后、规划借人前、借人返回/传送前后、动画币生成前后及最终扣款准备后重新检查day。若动画币生成回调内入夜，已经开始的那次Spawn不能逆转，但产物按原有fake+despawn收尾，不再继续MoveTo或扣款；不把表现币当金库退款。已经提交的原生交易按原paid/faulted语义完成，不尝试回滚或重复。

面板标注“白天补货”，GetSummary实时夜间显示“夜间暂停，等待天亮”，关闭/其他世界的说明优先。只新增只读条件和有限订单收尾，无新原生hook、全场扫描或配置项。

验证：161 auto-restock +35 Greek银行 +24真实税收助手回归 =220项全部通过；包含九类夜间冷启动/日出恢复、Approach/SendingCoins/FinalWait撤回、正常已付单不二次/不退款、借人/传送/移动回调翻夜、最终Prime后翻夜拒扣与手动/银行既有路径。实际2.4完整Debug构建0警告0错误，禁用自动部署。方法审计3644保持/10修改/8新增/2移除，限补货模块/银行自动扣款口/UI帮助/build标记，8PNG全同、Harmony类型集保持，火枪0.9及伤害可靠性保持。见receipts及独立review.md。

root修正两个测试夹具：九类恢复测试初始金库设为100（原夹具0币必然买不了）；Managers.Kingdom错误大小写改kingdom。没有放宽期待结果或更改生产交易流程。初轮辅助musketeer-restock项目缺快照共享stub，其实际测试对象是未改动的职业身份，因此不作为本次必需验证；真正火枪自动采购由auto-restock中的真实Shop/Restock源覆盖。

实现worker为本机OMP18.1.19，实际deepseek/deepseek-flash、max；file-only工具与源码白名单核验通过，root执行全部构建测试。未提交/发布/启动游戏、修改存档或配置；安装状态以receipts/install.json为准。真实夜幕撤单、次日补足、余额表现及相关主客机仍待游戏验证，任务保持doing。
