# save-repair-011 — land 7 超额乞丐一次性存档修复

## 授权与范围

- 用户在运行时清理把158名乞丐全部判为protected、实际removed=0后，明确授权直接修改当前`global-v35`并砍掉一部分人口。
- 这是对`population-performance-010`“不直接改压缩存档”运行时策略的一次性、有界例外；不改变该补丁后续仍应走world-authority原生生命周期的原则。
- 只修当前campaign 1 / land 7 的Beggar对象：左右营地各保留距离最近的5名，删除其余148名。其他角色、岛屿、任务、NetID、carryForward、银行数据与配置零改动。

## 当前即时指纹

- 游戏进程未运行。
- 源文件：`%USERPROFILE%/AppData/LocalLow/noio/KingdomTwoCrowns/Release/global-v35`
- 长度：751,068 bytes；SHA-256=`68D4F779DA3CFA45A659D2082B2B15F135777699EC4A309F1F6AEAE14C724B16`；最后落盘2026-08-15 22:50:24。
- gzip JSON：`_currentCampaign=1`、campaign currentLand=7、land7 objects=2194、Beggar=158。
- 两营地坐标为-120与70；最近归属分组为136/22。158名均为非settler、非DespawnOnLoad、Baker link空、CharacterData存在、非inert/非grabbed；待删148个uniqueID在完整JSON中均只出现一次。

## 写入门禁

- 脚本默认dry-run；真正写入必须显式`-Apply`并传入上方完整ExpectedSHA256。
- 执行开始、生成candidate后及`File.Replace`前都重新确认游戏未运行、源长度/hash/schema/精确计数不变；任一不符立即停止且不写源文件。
- 不使用PowerShell`ConvertTo-Json`或double对象模型重写存档；使用`System.Text.Json` DOM/reader保留JSON数值token与属性顺序，仅从目标island objects数组移除精确对象。nested component `data`只读解析。
- dry-run在系统临时目录以CreateNew写candidate并在结束后清理；只有`-Apply`才在源文件同目录写同卷临时gzip。两种模式都在关闭并Flush到磁盘后独立解压、严格UTF-8与JSON复读，验证删除集合恰148、最终Beggar=10且5/5、幸存对象顺序不变、所有非目标岛对象与当前岛非目标uniqueID集合不变。
- 写入前新建不覆盖的输入备份并复算hash一致。最终替换只允许同卷`File.Replace(temp, source, rollback, false)`；不支持时停止，不降级为删源/Move-Force。
- 替换后再次完整验证；失败时优先使用已验证rollback通过同样原子方式恢复；rollback未通过验证或恢复失败时，从独立且已验证的BackupPath复制到源目录restore temp，再用`File.Replace`原子恢复。输出只记录阶段、计数、hash与备份/rollback路径，不打印148个ID或JSON正文。

## 验收

1. worker脚本语法/干跑通过，独立reviewer逐行APPROVED。
2. 新备份与输入hash一致；Apply后gzip/UTF-8/JSON均可读，current land7 Beggar恰10，两个营地各5。
3. 非目标对象与其他岛对象计数/身份不变；再次运行dry-run因输入hash或计数已变化而fail closed。
4. 修复后只启动独立测试副本，确认可读档、10名乞丐存在、约6秒硬cap逻辑不再新增到5以上，日志无存档/Pool/RPC异常。
5. 脚本、harness与修复收据commit/push；压缩存档本身绝不加入Git。
