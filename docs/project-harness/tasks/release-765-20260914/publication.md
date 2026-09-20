# v7.6.5 正式发布

发布页：https://github.com/baisiqi6/ohmymods/releases/tag/v7.6.5
完整包：https://github.com/baisiqi6/ohmymods/releases/download/v7.6.5/KingdomEnhancedMod_v7.6.5_IL2CPP.zip

用户把7.1.5口误更正为7.6.5后按最新指令发布。本次没有创建7.1.5。新tag解析到648ddf0417f58162ef10c42c4bfdfc5faa66edc4，release ID388144452，非draft/非prerelease且Latest读回一致；仅推送新tag，旧7.5.0和其他标签/资产、master均保持。

相对7.5新增：授权作者真实DLL的PixelFireAnimator像素网格效果替代自制光晕，运行时1x1白纹理；坐骑无限体力F5便捷首项/defaultoff/所有world本机骑乘控制权，覆盖移动及技能体力、不改现有CD/伤害/速度/饱食。全部7.5功能保留。私人存档恢复、rawgamefiles、个人cfg和临时工具均排除。

ZIP SHA256 64e8176f0f9117b225fd8fdd59132567c7392e5f897a3670564acb1bfebe076a，39591252字节、313项；DLL SHA256 e1e5947cc4953d45d65b211a022739c06b4bad1ca72c7ddec4b5bfc3bf6c7354，679936字节，assembly7.6.5.0/plugin7.6.5/build7.6.5。Cleanworktree精确提交构建0W0E，1945方法体与本机候选7614E05C相比仅Init版本/构建日志文本及对应字符串容量常量改变。2292reachableUnityAPI无unstrippingstub。ZIPCRC/路径与内容白名单/manifest/DLL一致，306运行依赖与历史正式包逐字节相同；独立reviewer重新核验这些事实。

29测试项目通过（28可执行回归+1 xUnit真正运行75/75），另1个Library接口验证project仅编译通过。测试runner已区分run/test/build；不是用dotnetrun或build的exit0代替xUnit执行。原作者特效44case与体力25case254assert通过。发布包5文档准确标注：像素FXnative几何有实机证据但观感待确认，体力实机仅defaultOFFstartup/4nativehooks确认，启用后长跑/技能/真实换骑/双机待验；旧头饰实际SaveReload/跨world/崩溃待定位不宣称修复。

OMP native session核验为deepseek/deepseek-v4-flash thinking=max；仅在隔离worker目录起草notes。Root纠正文案的旧光晕来源误述，并统一版本/安装说明。独立builtinreview通过后上传draft，验证远端size/digest后发布，再读回Latest与tagcommit。证据在operator release-765-20260914目录。

游戏保持关闭时将E独立副本7614E05C替换为精确正式包DLLe1e5947c，原DLL有旁路备份；save和个人cfg安装前后哈希保持。没有启动游戏、没有覆盖存档。正式7.6.5精确DLL未新启动，其方法体等价证据与之前候选运行证据分列，不冒充新实战验证。发布done和各功能尚待实测的doing状态分开。
