# Issue200 / PR202 当前主线集成验证（2026-10-15）

已实装的双坐骑岛（各约8个）源码接入release `a612267d28657183691e45782e54ad54dee1e065`。本次三方自动合并干净，未新增产品修改；201的储存钻石兼容及236/238当前主线保留。更紧凑布局/右侧弯月形由关联PR222交付，Issue200须两PR均合并才关。

实际验证：cross-world-mounts 353；split-policy 163；map 286；fresh在当前Assembly-CSharp元数据交叉核对下177项、0失败；runtime全部PASS。5个suite的源、项目、fixture逐SHA与最终合并树相同。公共完整net8/net6、Android实际interop Debug/Release均0警告0错误；完整Adapter对实际Release DLL 1264通过、0失败。禁自动部署，未新增Android注册。

初次私有obj重定向引入旧obj生成属性重复，已保留错误日志；相同tracked源码导出到无生成物的私有镜像后通过，未改产品/测试来掩盖错误。证据位于 `.local/tasks/release-consolidation-20261015/pr202/`（test-input-binding.json、各suite回执、build/full-builds.json）。未启动游戏、未安装，未声称Windows/Android设备或新玩家测试。按本轮授权，后续实机问题另开Issue/PR。
