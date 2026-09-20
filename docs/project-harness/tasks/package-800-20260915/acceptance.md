# 8.0.0 本地包验收

2026-09-15 8.0.0本地完整包已完成：release/KingdomEnhancedMod_v8.0.0_IL2CPP.zip，39642577bytes/314项/SHA 571d278b，DLL 9C63FF5D。117项冻结源码按真实2.4构建0W0E，2674旧方法与已装7DD53270相同，仅Init版本/build文字改变，两PNG保持；68项目全过（56run/4真xunit392/392/8compile），唯一旧interop测试缺ImageConversion引用已补并复跑。306运行依赖与正式7.6.5逐字节一致，CRC/白名单/双manifest/5docs/独立ZIP复核通过。包明确local-not-published与uncommitted-snapshot，BaseCommit仅基底；未commit/tag/push/release/安装/启动游戏。当前游戏仍7DD53270的7.6.5候选，canonical源码版本8.0.0；各功能实机/联机doing不变。

完整包：C:/Users/ADMIN/projects/ohmymods/release/KingdomEnhancedMod_v8.0.0_IL2CPP.zip
ZIP SHA256：571d278bff72a9ac2c1c7989df028f5b2441d3ad569296e18e9498cd6e6d2765
DLL SHA256：9C63FF5DE2EA4FC7C558EE6541CD0A276DB1528AE8A763784A1A73ECA113EF79
行为基线：7DD53270DA06AD11C201633C879E2A46E05169AE01FB54A50CB22C3583570026。

可复核源码、完整真实2.4构建配方、编译输出和全部测试记录保留于本机task/source、task/build及task/tests。本地包包含SOURCE-MANIFEST.json和摘要，不含完整源码，不能仅靠BaseCommit重建当前DLL。原canonical发布脚本clean门禁未改变；后续正式发布须另行授权并从确定源码修订构建/审核，不能将当前本地包误标为已公开发布。

唯一回归初始失败为测试项目knight-load-seed/interop缺少新英雄PNG所需UnityEngine.ImageConversionModule引用；修复测试项目后重新编译0W0E，生产代码未因此更改。xUnit的392项为真实执行TRX，8个Library项目仅编译，不记为运行验收。

玩家说明已统一为当前8.0，独立历史V*文档保留。英雄仅单机、头饰/火矢/围巾等未实测边界、独立档精确快照限制、半径0.25与小怪受击宽0.375参照均保留。未把私人测试岛恢复写作功能；没有宣称全部闪退/原帽消失/联机问题已修。


后续状态：用户追加授权后，2026-09-15正式v8.0.0已发布并同步本机，见 ../release-800-20260915/publication.md。原本地快照ZIP571d278b另存于本机release-800-20260915/archives，仓库release同名文件现为正式包1095ceb2；本文件前述记录属于此前只打包阶段。
