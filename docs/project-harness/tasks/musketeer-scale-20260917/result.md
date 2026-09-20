# 火枪手0.9外观候选

基线284B78A0（9.0.0-combat-reliability-20260917），候选63EA50ADDE922CC1600EF11B8D2C4CFE10FBB12C555D83914CED55DA7DBD354A，build=9.0.0-musketeer-scale-20260917，公开版本仍9.0.0。

用户确认已保存退出后，已检查进程关闭、备份并安装此精确候选至既定E盘测试路径，安装与备份哈希核对通过，28份原生档/附加档/配置哈希保持，未启动游戏。见receipts/install.json。退出完整日志另存logs/*-exit.log，保持此前冻结片段不覆盖。

MusketeerAtlas.AppearanceScale统一0.9。仅自有KEM_MusketeerSprite子物体localScale绝对设(.9,.9,1)，保留原脚点pivot；人物与手持枪全帧一起缩小，枪口localXY乘同系数，原朝向翻转保持。不改actor/root/native renderer缩放、碰撞、移速、动画时钟、伤害/射程/装填、工具/商店/弹丸尺寸、职业保存或全局Greek缩放。原8PNG不变。

79个runtime用例通过，包括所见缩放/枪口与原有伤害可靠性路径；actual2.4接口、完整Debug构建0警告0错误，构建禁用自动部署。DLL审计3653方法体保持，仅Plugin.Init、MusketeerVisuals.Apply、MusketeerCombat.TryComputeMuzzle三方法变化，无方法新增/删除、无Harmony类型变化、8嵌入PNG逐字节保持。生产修改精确4文件（含常量文件与build标记），见receipts。

worker：北京时间工作日19点，本地OMP18.1.19，实际session metadata deepseek/deepseek-flash、thinking=max，仅read/grep/edit/write（内部可glob），源码隔离。root整合与执行测试，独立review见review.md。无提交或公开发布。

日志结论见log-findings.md：本次23名火枪身份读档/保存报告正常，marker注册正常且未见伤害错误；另有11条城堡盾牌店InvalidNetID、2个Archer穿地被搬回、英雄快速Stand/Walk切态，根因/具体职业未确认。无错误日志不等于逐弹扣血或性能实测通过。

候选DLL和更新说明在本机operator/musketeer-scale-20260917/candidate。游戏运行期间不得替换DLL，安装是否完成以receipts/install.json为准。真实缩小后的观感与枪口对应仍需游戏验收，任务保持doing；不因纯视觉缩放宣称修复穿地、原有动作问题或上述日志异常。
