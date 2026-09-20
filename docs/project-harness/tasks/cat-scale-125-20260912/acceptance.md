# 猫缩放 1.25

用户要求猫缩放由1.2改为1.25。唯一行为改动为CatScaleY常量1.2f→1.25f，沿用只调整y轴、ScaleRegistry注册，以及读档现有猫和新增猫共用EnsureCatScale。仍仅希腊农舍猫，每农舍4只，世界/联机门控及其他行为不变。

完整IL2CPP构建0警告0错误；直接读取候选DLL元数据确认CatScaleY=1.25；逐文件差异限制为上述常量/两处注释和Plugin构建标签，其他66个.cs与当前canonical相同。本机worker /root/cat_scale_125在独立副本修改，root核对和构建。此为低影响常量调整，无新增测试或额外游戏运行；未宣称目视验收。

已在无游戏进程时安装 `B33D56B83A0799F7D01D29AFF19323BD692E2BE70DC8BE80AC581769C9D6EB09`，build5.0.0-cat-scale-125-20260912，DLL哈希核对通过。下次加载希腊存档时现有猫也走统一缩放路径。save=7289D36982FFD7CB07B70140B03353EE8B923221321FE4ED3BFAF18295AEC669安装前后相同，bank=5408未动。此前双倍补货/HUD等改动完整保留。无commit/push/公开发布。
