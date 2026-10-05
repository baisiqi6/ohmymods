# Issue #152 合并与 Mac 安装记录

2026-10-05，用户明确授权合并和安装。

- PR [#154](https://github.com/baisiqi6/ohmymods/pull/154) 已合并，reviewed head `8b92838f884348dd62a4d4ab5aee55a3eb1824da`，merge `0e3a2332774b7d5a61c341b071b44dca0ec8305c`；Issue #152 代码交付关闭。
- 从合并提交重新构建：275个源码/资源输入与review候选相同；实际ARM64 161引用、34PNG，0警告0错误。Cecil比较与review产物无方法变化或新增，资源保持。
- 安装DLL SHA256 `81d931505e06839d2e62de818badfc58ce0789ef08da67322bf4f303dfb48bb9`；旧DLL `1c00e9b2cda9c983a91a52d4ab3d589d624d216d3dce6b64189cddb3248fdbca` 已备份。
- 保留当前累计测试版本10.9.38，本次不是正式release/tag；地图条目下一发布账目按README建议去重，不对内部返修重复计分。审查产物97a92a31与合并源码重新构建的安装产物81d93150的hash不同；完整输入与IL等价已核对。
- 安装前、替换前后游戏进程均已关闭。只替换ARM64运行环境DLL与对应SHA256SUMS条目；488个受保护运行文件/存档/配置/偏好文件hash保持，启动器`--check-only`退出0。
- Operator未启动游戏。地图详情、当前高亮原生岛总览四资源、重复开关、换岛、分辨率与可读性由玩家实测；未探索physical11不显示资源的规则保持。

备份、固定输入/引用manifest、安装gate与原始receipt保存在私有`.local/tasks/map-icons-inside-islands-20261005/`，未公开用户存档、配置或原始日志。本记录不覆盖其它进行中修复。
