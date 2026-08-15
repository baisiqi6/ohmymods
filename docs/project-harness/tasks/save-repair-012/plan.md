# save-repair-012 — land 7 普通居民一次性减员

## 授权与纠正

- 用户明确授权在当前`global-v35`中删除350名普通居民，以缓解land 7人口过多导致的卡顿。
- 先前“Worker约458名”的统计错误地信任了对象池残留`name`；当前存档按`prefabPath`与`WorkerData`复核只有14名真正工匠。421个名称以Worker开头的对象实际仍是Peasant，加载时也按Peasant prefab恢复。本任务禁止按`name`判职业。
- 经用户再次确认，目标是删除350名Peasant，不删除任何真正Worker。

## 当前即时指纹与候选

- 游戏进程未运行。
- 输入：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35`
- 长度：748,730 bytes；SHA-256=`2C681C5C2CA01E6BBCBB5F05BDEA32FC63A0D86EA563F68325D12C08D088F87A`。
- gzip JSON：version16、currentCampaign=1、currentLand=7、land7 objects=2046。
- 真正Worker=14；Peasant=733，其中希腊`Prefabs/Characters/Peasant`为638，北境Peasant为95。
- 383名希腊Peasant满足最窄候选，且必须逐对象全部满足，否则fail closed而不凑数：`componentData2`恰好各1个`Character::CharacterData`、`Damageable::DamageableData`、`GenderAnimatorSelector::GenderSelectorSaveData`、`Petrifiable::PetrifiableSaveData`、`Wallet::WalletData`，没有任何其他组件；Character的`inert/isGrabbed`均false；Damageable的`hitPoints=0/invulnerable=false`；Petrifiable的`IsPetrified=false/RemainingHP=0`（RemainingDuration只要求有限，不强制为0）；Wallet的`usesCurrencySystem=true`、legacy coins/gems=0、CurrencyMap必须恰含Candle/Coins/Crown/Egg/Gems/Merchandise/Shades/Skulls且全0。
- 对象层还必须满足：`parentObject.linkedObjectID`空、`hierarchyPath=Level/GameLayer/`、`mode=0`、`linkOrder=0`、`decayHint=0`、`decayResistanceDays=-1`、`decayedVersionPrefabPath`空、`crpcType=1`；uniqueID非空、当前岛唯一且在完整JSON中只出现一次；netID为正数、候选内netID唯一且当前岛`(crpcType, netID)`复合键唯一；createOrder为正数且候选内唯一。当前岛允许不同crpcType合法复用同一裸netID，禁止要求裸netID跨type唯一，也禁止重编号。禁止按对象`name`判职业。

## 选择规则

- 在上述383名候选中按`createOrder`升序、再按uniqueID ordinal排序；保留最低加载顺序的33名，删除最高加载顺序的350名。`createOrder`由层级/sibling顺序计算，不代表年龄，脚本绝不重编号或宣传为新旧对象。
- `createOrder`在候选中383/383唯一，范围20264～21335；低33组左右位置为17/16，高350组为182/168，不会把人口只清空在单侧。
- 删除后预期：land7 objects=1696；Peasant总数383；希腊Peasant=288；北境Peasant=95；真Worker仍14；Beggar仍10且左右5/5。

## 写入门禁

- 基于`repair-beggar-overflow.ps1`的已审查安全框架制作独立脚本；默认dry-run，真正写入必须显式`-Apply`、ExpectedSHA256和ExpectedLength。
- 开始、candidate生成后、备份后及`File.Replace`紧邻前反复确认游戏未运行且源hash/长度/schema/计数不变。
- 使用System.Text.Json DOM；nested component data只读解析。candidate对象数组必须精确等于before对象数组过滤350个ID，所有幸存对象逐个DeepEquals且顺序不变；另对before/after两棵独立root clone的唯一目标岛`objects`都替换为同一个语义的空数组后执行整棵root DeepEquals，证明其他root/campaign/island字段零改动。
- dry-run只写系统temp并清理；Apply才在源目录写同卷CreateNew candidate。严格UTF-8、gzip复读、Flush(true)。
- 新建不覆盖备份并验证输入hash；仅允许四参数`File.Replace(..., false)`。写后验证失败时优先verified rollback，必要时从verified BackupPath构造同卷restore temp原子恢复；不允许Move-Force或删源降级。
- 存档与备份绝不进入Git；只提交脚本、plan、review证据与receipt。

## 验收

1. worker实现和真实dry-run通过，独立reviewer给出`APPLY_APPROVED`后才可写入。
2. dry-run必须精确报告Worker14、Peasant733→383、Greek638→288、Norse95不变、removed350、Beggar10/5+5不变；源hash不变且dry-run不创建BackupPath。
3. Apply输出hash必须与dry-run candidate一致；备份hash必须与输入一致；独立复读验证所有预期计数和非目标DeepEquals证据。
4. 游戏内只用独立测试副本读档；若失败立即退出不保存并恢复本任务新备份。
5. harness、修复脚本与receipt经review后commit/push；旧候选ZIP继续排除。
