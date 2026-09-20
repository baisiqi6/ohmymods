# 友好巨魔头饰伪装与44款轮换

用户授权：戴原生/跨世界/周年面具或派对帽的Hermes友好巨魔不被主动选敌，范围伤害保留；头饰改逐一轮换。保留既有30%概率与已有actor收据，不更改原生血量/无敌/碰撞/伤害，不给敌方单位加保护。所有世界、Mod总开关控制；已有起手/飞行攻击允许完成。头饰显示开关仅控制新增装饰，原生可见面具也有保护。

OMP deepseek/deepseek-v4-flash thinking=max两worker分别新增FriendlyTrollDisguise和HermesHeadwearCycle，Operator接既有追击/注入与头饰/面板接口，内置archer_reviewer只读审查。新增两个TargetCacher nearest长入口Postfix只在protected result时按原候选/谓词重选，列表不改。已核2.4 native464/384字节且各1slot。

独立全局游标存BepInEx/config/KingdomEnhancedMod/ModSave/hermes-headwear-cycle.v1.json，仅主机概率命中时消费，0..43循环，已有actor Init/读档不消费；原子写成功才发放。全本机存档共享轮换序列，不写游戏原生存档，不迁移旧Hermes actor持久化。

验证资格、目标后备与不改伤害、44顺序/重启/失败原子写、真实头饰receipt整合/读档网络、实际interop/native/API与既有方法体保持。游戏关闭才备份安装本机候选，不启动、不提交发布，不覆盖存档配置。真实玩法、AOE、换岛与联机均待实测，保持doing。
