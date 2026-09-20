# 玩家举旗火枪后排与大型猎物猎手

用户已采用五帧红焰，2026-09-17闭游戏安装BCA6AAEB且28份用户数据保持。新需求：原4重装步兵/4弓手以外新增最多4名已购火枪手排后；白天只狩猎普通Deer，排除兔子/其他小动物/坐骑，原伤害2、射速、射程及原生死亡掉钱不变，不新增全场扫描。

当前北京时间夜间，实现worker为本机OMP deepseek/deepseek-flash max，file-only隔离deer/banner两slice；reviewer独立内置。deer只改MusketeerRuntime/Combat及必要新模块；banner只改现FleetBoatFormation数组owner与新模块，不能各自抢数组或动原4+4与船队。root整合、actual2.4接口/原生地址/资源几何和测试/打包。无commit/push/publish、无存档覆盖、运行时不换DLL。

复用PlayerFormation（不是ShieldWallTotem），native TryRecruit/Register/OnLeave与移动。附加槽合法enum Gap封闭，需证明定向招募不占原弓位、其他弓不占火枪位，native后续异常的半完成招募可回滚，收旗/死亡/世界/失效/开启关闭正确归还。保持现offline开关范围。

狩猎只替换已有wildlife scanner deny-all为与既有条件AND的Deer条件；敌方scanner缓存保持。发射和命中最终复核昼夜/当前source及Deer身份；敌方夜战不受影响，非鹿小动物不伤不吸弹。现32→256→完整List查询与伤害同步native链保持。

实际2.4资源：普通鹿triggerCapsule offset(.0662009,.379303),size(.913657,.508606)，Greek现有y=.55使顶部低于火枪口；计划仅鹿的水平线不交实际碰撞体时，初始直线向body瞄准，不变伤害/射程/敌人水平弹道，不放大hitbox。计算证据不是实机命中验证。

验证：生产逻辑回归、native ABI/新hook地址/完整2.4构建、无关方法及所有8PNG保持、独立review。真实举旗/两侧后排/收旗/鹿命中与逃跑/猎后天黑回防需游戏观察；无实机证据不称已通过。游戏仍关闭时仅候选交付，安装授权按当前用户范围执行。
