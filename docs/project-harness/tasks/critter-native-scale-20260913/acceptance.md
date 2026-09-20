# 取消Critter小动物缩放

按用户要求取消兔子等Critter小动物的y=1.8缩放：完整删除Critter.OnEnable补丁及缩放登记，由游戏原版控制大小，不强写1或添加新钩子。鹿0.55、工匠、猫、盾牌和隐士热修保留。编译0警告0错误；与已安装6A9A4546比较，1317个方法体完全一致，仅删除Critter方法与更新构建日志。E已安装92D4E207 / build=6.1.5-native-critters-20260913，存档/config/bank逐项保持。下次正常启动生效，本轮未启动游戏，未把静态验证冒称目视验收；未commit/push或更新公开6.1.5。

## 构建与安装身份

- DLL SHA256: 92D4E207D91ECEEA24CED64DDD29481F1C08448EB7AC2598DED8FC0C8F34F385
- Source Worker SHA256: 9D549F1F057408AB5A51BB35B2EFEE6BDE39D9F6F993A2311483860C4268EFE9
- 本项验收范围为删除缩放、构建等价与本机安装；没有新增运行时逻辑，不要求额外启动游戏。
- 原始构建、Cecil验证与安装回执保留本机critter-scale-native-20260913目录，不提交私人数据。
