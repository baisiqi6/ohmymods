# 实际2.4资源几何（只读，不等于游戏实测）

read-assets.py读取既定E测试副本resources.assets。普通Deer（GO19820）Wildlife层9，根y=.875，受击trigger CapsuleCollider2D中心(.0662009,.37930295)、尺寸(.91365695,.50860602)。其Physical Collider子项在17层，不是弹丸需要瞄准的受击体。Hind也是9层，因此仅按层/标签会误伤坐骑，必须确认Deer且排除Steed/Hind。

生产已有Deer_OnEnable把希腊鹿y设为.55；原大小受击体竖向相对脚点[.125,.633606]，希腊为[.06875,.348483]。火枪现Aim枪口pixel[48,14]、pivot[31,2]、PPU32、cell56x32，localXY=(.546875,.484375)，现外观.9后相对自身根Y=.4359375×实际根yScale。即使根yScale=1、双方脚点相同，希腊鹿顶部仍比水平枪口低约.08745。实际运行时地形/actor root/缩放可能不同，不能把静态推算冒充实际命中。

处理策略：不移动枪口、不扩大鹿collider、不改全局缩放。发射时只有合法鹿且水平线不能经过body范围，才向它当前真实受击body中心确定一次直线方向；之后不追踪鹿的位置。非鹿敌人保持水平，鹿原尺寸可水平相交时也保持水平。方向有限归一化；距离、射程与扫掠命中距离均按真实二维距离计算，地面交点截断，兔等不伤也不挡。

read-formation-asset.py复核Player GO19778/Formation97545，非空m_Name使此Mono header为44字节。类型PlayerFormation3，unitTypes=[11,5,5,0,0,0,0,5,3,3,3,3]，即FleetBoat1、Archer4、Pikemen4与Gap。spacing13项，Archer=.21875、Gap=.34375、FleetBoat=0，startOffset=0、overrideMoveSpeed=1.75、overrideShootCooldown=-1。新后排在低index，左右按原生side镜像；补偿startOffset以保留原位。

普通Deer组件85794的序列化header32后首int为3，与numCoinsDropped字段及原生HandleOnDeath循环对应。2.4 HandleOnDeath唯一入口0x9DB430，现有伤害仅通过原生ReceiveDamage进入死亡事件；不新增钱、经验或另外死亡回调。

待实机：希腊小鹿/其他世界鹿左右与近距离命中、鹿逃跑后无追踪弹、兔/坐骑免伤、夜晚停止打猎并回防、举旗后4+4+4位置/射击/收旗/读档。
