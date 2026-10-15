# Issue200 / PR222 当前主线集成验证（2026-10-15）

PR202已经merge da840f6进入release，PR222已由其旧feature base显式改为release/v9.5.13，再吸收该基线。仅progress.md存在历史追加冲突，保留双方段落。产品实现与原PR222逐字节一致；左岛像素、紧凑布局、右岛宽湾弯月形/独立mask/锚点均保持。已实装功能本轮只做源码收尾。

本轮实际测试：compact127、art variant94、source-linked VariantProbe161、shore101、缺少B资源反例3；全部PASS。完整公共net8/net6、Android真实interop D/R均0警告0错误，完整Adapter1264/0。tests源/fixture/资源输入逐SHA对应最终组合；本机证据`.local/tasks/release-consolidation-20261015/pr222/`。禁自动部署，未改变正常DLL或启动游戏。

旧RuntimeProbe S1–S15仍含单岛16资源及旧私有字段断言，历史不是全绿；本轮不改产品凑旧断言，也不冒称旧全场景已通过。当前实际契约由VariantProbe/纯规则/资源反例覆盖，见其README。Windows/Android设备和动态原生显示/领取等玩家后验继续如实单列；按用户要求，全部对应代码PR合并且必要验证通过后关闭Issue200，后续新问题另Issue/PR。
