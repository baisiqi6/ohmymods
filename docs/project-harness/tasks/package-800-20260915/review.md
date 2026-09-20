# 8.0.0 本地完整包独立审查

Reviewer：内置 /root/package800_review，只读且独立于文档与打包工具worker。最终verdict：审查通过，无阻塞。

Reviewer使用单独内存Python直接读取最终ZIP，没有调用打包worker的verify脚本：39,642,577字节/314项，全CRC通过，无大小写重名、目录、符号链接或路径逃逸。ZIP SHA256 571d278bff72a9ac2c1c7989df028f5b2441d3ad569296e18e9498cd6e6d2765。

306项runtime与已验正式7.6.5 ZIP逐字节一致，布局精确为306runtime+1DLL+5docs+2manifest。没有个人cfg/save/sidecar/log/cache/interop或原生游戏DLL。DLL SHA9C63FF5DE2EA4FC7C558EE6541CD0A276DB1528AE8A763784A1A73ECA113EF79与快照构建输入相同。

SOURCE-MANIFEST SHA D40AE61924141C57137212FED7D16179E7044D226A3D5284AEFAC1FE4952DEE3，117项与canonical及frozen source逐项一致，无漂移。manifest明确8.0.0、local-not-published、uncommitted-snapshot，BaseCommit不是精确源码版本，复现还需冻结源码/构建配方/依赖。Candidate Compile/两嵌入PNG均指向task/source，真实2.4 interop引用，没有部署target。

Reviewer独立读取4份TRX：88+73+146+85=392 executed/passed，failed=0；总68项目正确分为56 run、4 test、8 build。旧独立interop项目缺ImageConversion引用已修并复跑；不把compile-only记为真实运行。

五文档逐字节匹配最终canonical，已抽对ModConfig默认值、功能世界/主客机范围、英雄与头饰/独立档限制；当前版本文字不再与旧全局散射/纯视觉火效文案冲突。英雄/围巾/联机等待验边界保留，不称Latest或全部实机完成。两处私人测试岛说明按review建议删除，独立历史版本文档不变。

Operator的Cecil审计进一步确认相对已装7DD53270，2674方法体完全相同，仅Init的版本/build日志与对应字面容量常量变化，两张嵌入PNG相同。安装、发布、提交均未执行；Reviewer没有修改文件或操作游戏。
