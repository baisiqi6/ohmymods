# Issue 162：资源生产处独立审查

**Verdict：Approve（资源构建范围）**。未发现需要返修的可行动问题；运行时地图失败保护不在本审查范围。

修复点正确：故障输入来自私有 Mac wrapper 手写资源清单落后于官方项目，因此把官方原有 34 个资源声明提取到 `EmbeddedAssets.props`，让官方与 Mac wrapper 共同导入，在产物生成处消除清单分叉。没有为地图另造资源 fallback 或运行期反复恢复。

独立证据（审查期间仅在本 `resource-review/` 下生成 probe 与测试文件）：

- `review-v1/hashes.json` 的四个候选文件 SHA256 全部匹配。
- 对照实际 baseline csproj：34 个 `LogicalName` 和逐文件 SHA256 完全一致；新增 Include 前缀 `$(MSBuildThisFileDirectory)` 正确把路径绑定到共享 props，而不是调用者工作目录。
- 除资源声明及其空 ItemGroup、新增资源 Import 外，官方 XML 结构和属性完全不变。隔离复制中真实 MSBuild 评估的 Compile（234）、Reference（19）、PackageReference（1）、Content（0）均保持一致，路径差异按各自源码根规范化。
- 使用实际旧 ARM wrapper 独立迁移后，PropertyGroup、Compile、Reference、Target 原文结构保持一致；真实 MSBuild 得到 34 个与官方逐字节一致的资源。
- Compile 指向新 canonical 源但资源 Import 指向旧源时，check-only 独立确认拒绝。工具要求输出与输入相邻且不同，保留相对引用基准；拒绝缺文件、条件资源、Remove 操作、重复名字、畸形 XML 和无法安全替换的复杂资源形式。
- 在隔离副本重新运行全部 12 个 wiring tests：12 passed、0 skipped。包含旧 8/inline wrapper 拒绝、未来资源添加无需修改 wrapper、不同 cwd 与带空格源码路径的真实 MSBuild 验证。
- 读取 Operator 已完成的实际 ARM 构建日志：0 warning、0 error；scope audit 相对 baseline 的 6809 个已有方法、338 个 Harmony 目标及全部 34 PNG 均语义/bytes 不变。这些为 Operator 的实际编译/产物证据，本 reviewer 未重复构建游戏产品。

实现边界合理：脚本只支持现有 literal source Compile glob 与简单显式资源清单；没有扩展为通用 MSBuild 解释器。没有固定 34 数量写进脚本；34 是当前验收基线，未来资源只需修改共享 props。官方项目的非资源职责和私有 wrapper 的版本、引用、输出、部署职责未迁移。

验证边界：以上证明构建资源来源和产物完整性，不能代替游戏重新启动后的地图视觉验收，也不评价另一 reviewer 所负责的运行时失败撤销路径。

详情：`probe.py`、`probe-result.json`、`tests.log`。
