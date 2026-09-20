# 希腊随从火矢范围爆发：本机候选交付

2026-09-14本机5CED0D25/build7.6.5-greek-impact-20260914：希腊style3骑士火焰窗口的随从火矢，半径0.25/1点Fire/直接目标排除/同轮散射去重，沿用F5弓箭ImpactEnabled默认off与作者像素动画。窄TryDamage保留原生直接伤害且不写原生字段；73核心+45FX+75散射及30测试项目/1interopbuild、0W0E/2354API/1937无关方法保持/独立review通过。闭游戏安装到正确E，旧DLL已备份，save/config哈希保持，未启动游戏或发布；实机/压力/联机仍待。见tasks/greek-fire-impact-20260914/acceptance.md。

实现及接入：il2cpp/PatchArcher_GreekImpact.cs、PatchArcher_Impact.cs、PatchArcher_Options.cs、ModPanel.cs、ModConfig.cs；KingdomEnhancedPlugin仅候选build日志。使用现有ImpactEnabled开关及默认false。角色风格决定资格，移植到其他世界的希腊骑士仍适用；未改变其他世界银行或缩放规则。

Worker是本机OMP deepseek/deepseek-v4-flash / thinking=max（native metadata fallback=false，session 01a09e87-25fe-73e2-a2a6-b8dfb8a16540），隔离副本，原有cs零改动；Operator统一接入并修复事务世代/回调异常等问题，独立内置reviewer最后复审通过。实际wrapper使用一个复合__state，未采用worker示例中无效的额外__greekState参数。

验证：
- clean候选build0W0E；30测试项目（28执行程序、2真实xUnit：75+73）与1接口编译项目最终通过。首轮散射测试夹具using顺序编译问题修正后，75/75重测通过；原始和复测日志均保留。
- 45FX回归保留作者尺寸/动画/资源池，并新增非合资格箭不画FX。
- Operator异常用例对未修worker源码有6个预期失败；修后核心73/73，证明命中票据、回调复用、状态变化、spawn身份失败的回归能捕获真实问题。
- 2354全模块Unity路径审计无unstripping stub；1937无关方法IL一致，允许变更为UI/config/日志/生命周期与FX接入。
- 新TryDamage入口0x4c98e0，368B/same_slots1；实际2.4直接伤害分支逐指令核对。OnEnable/HitObject/FireArrowInternal均独立长入口；无新增短getter钩子。

边界：
- 不改任何Arrow原生字段；仅当前命中事务、同箭生命、同直接目标替代TryDamage持续灼烧分支，保留authority/null/immune/perfect/source/return语义。所有其他调用走原生。游戏升级须重新核这段2.4分支。
- 256在空volley、1280箭lease、每query64collider、每volley32目标，满时限量/原版fallback并计数。已关闭的FireArrowInternal scope若箭仍在空，仍占volley容量，所以256上限不只由嵌套深度决定。
- 同轮去重只针对额外AoE，各箭原生直接伤害照常；直接命中目标排除当前箭的AoE，后到达的另一支箭仍可正常直接命中。
- 沿用主机/单机本地像素FX；主机调用原生ReceiveDamage，未新增RPC。没有实机验证Harmony native detour、真实物理、外观、保存读档/换岛、压力帧率或两机一致性，不宣称已经通过。

安装：正确E独立测试副本，hash 5CED0D2525D110FB42B5FBCDB8A937B62FDFE044BC175709EF0DB1C408062CC5，备份与保护文件哈希见install-receipt.json。未启动游戏；未提交或发布。配置与存档原样。

后续实机验收：F5→弓箭→希腊随从火矢爆发，检验火焰窗口内外的普通/随从箭、邻近小怪范围/直接伤害/无DOT、散射去重、开关/换岛/池复用、密集战斗和主客机。公开7.6.5不变。
