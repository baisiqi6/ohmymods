# Issue162 / PR163 合并源码完整资源冷安装

PR163 head `0238a9c507e82a697e97790668bba18279c8f3c0`，merge `14f44a54eacffd01f1d8baa46407482253bcb9d0` 已合并；Issue162代码交付关闭。合并canonical全源与固定候选相同，实际ARM0W0E，6809方法/338 Harmony目标/34PNG相对审查候选保持，资源逐项与shared props名和字节hash精确一致；安装输入冻结274项和161真实引用。

UTC `20261006T084726Z` 冷安装10.9.38测试候选，SHA256 `c6ca09b2bfeced325ccc176c59939e51546021857a499e5788f08ca01123a0f9`；旧缺26PNG的DLL `c1d1f17e114176ef77cdeb652474fbd4e5549a5f12e3bba0063626e7ae228528` 已备份。仅运行DLL和SHA256SUMS对应行变化，488个受保护文件哈希保持，launcher check-only返回0，安装前后游戏关闭，无游戏启动或存档配置写入。

本机常用私有wrapper也已保留备份后接入共享props，Compile与Import成对指向本次精确canonical源，PluginInfo/包装器版本恢复10.9.38；该入口真实构建0W0E。不再从历史8项列表生产DLL；它仍不负责SDK/core/interop安装或正式发布。

原版岛保持原生流程，最新英雄/长按续买等源码未回退。玩家地图画面/素材观感、Windows和联机待验；未创建正式release/tag，无新玩法版本增量。
