# 两处已升级普通塔残留：根因与本机修复

当前代码与E DLL已更新：`2BD2055C1554DE7D0C5BA0910E974C209C2FEA418CE69FDD5EC3CD5C8B3EFB8B`，build `5.0.0-hud-tower-refill-20260912`。公开5.0未更新，未commit/push。

用户已将旧Tower1升级到Tower2。实际待处理对象是Tower4/Ballista@166.66和Tower2/FireTower@176.68。只读probe证实双方建造完成、无付款/脚手架保护、独立SemiStatic，普通塔弓箭手仍有双向岗位引用。不是建造条件误判。

根因：当前2.4原生ExitGuardSlot0x4b13e0确实在末尾清_guardSlot；退出单人后GuardSlot.ExitArcher -> Kingdom.AddGuardSlot0x599ec0 -> DistributeTowerArchers0x59da10同步补人。逐个退出时，后一个人的退出会把前一个人重新分配回旧塔。即刻日志三人public/privateLinked均false但slot已有另一个archer，最终复查旧塔三个岗位又满，旧逻辑正确保守退出但不能完成清理。

修复：只在同步TryRemove期间、只对精确Kingdom实例，Harmony prefix暂缓DistributeTowerArchers，finally必清并拒绝重入。所有身份/离线权威/同址completed typed见证/付款/施工/脚手架/原生撤员/角色存活与层级/双引用验证保持。撤员验证完成后按既有流程注销精确header、DontPersist、SetActive(false)；在Destroy前通过native RemoveGuardSlot0x5a66d0同步移除已空旧岗位，关闭帧末销毁前再次补人窗口。完整保留塔在所有停用前失败路径不丢岗位注册。停用后异常会尝试排队Destroy，沿用-2已停用返回语义。无强改_guardSlot、无新全场扫描/定时器/人口变化，无永久抑制正常补岗。

验证：109项通过（原105+4针对同步补岗/精确实例/重入/失败清理），关闭抑制的负对照精确触发2项失败。实际依赖构建0W0E；144Unity可达调用无unstrip stub；新增DistributeTowerArchers hook在当前native same_slots=1。独立review发现帧末风险后修正，复审PASS。两位reviewer与塔worker实际response模型GLM-5.3；请求max未核验，HUDworker早期native文件已被工具保留策略清除，不夸大当前证据。

实际受控启动：`2026-09-12T13:52:55.5749645+08:00` 至 `2026-09-12T13:53:43.7723097+08:00`，自建PID 36700已停止。原生日志确认 `removed ordinary duplicate x=166.66 witness=Tower Ballista_greece(Clone)`、`x=176.68 witness=Tower_upgrade_Fire_greece(Clone)`、`removed=2`，占用根70降68。所有撤员和存活核验通过才可能到达该日志。BepInEx无Error/Fatal，save前后 `52DEF381398BE2AF8C235B9538B41DF081730E139EF857F52A14CEE7174867DD` 相同。检查没保存新世界：用户正常读档后仍由新DLL自动清理，后续长时间游戏/保存再读和画面反馈待确认；不把未做的持久化验收写成done。

HUD像素改版同包保留，AutoRestock采购逻辑/配置本次不改，实际购买未证实。回滚可使用startup.json中精确backup，须游戏退出后操作；不要拿旧probe/build覆盖新版HUD。当前任务pending项仅正常游玩与保存再读确认，不再是等待用户退出或等待只读probe。
补充日志核验：最终Player.log与上一508CC2启动均2条LogError签名，新增0，见player-log-comparison.json。
