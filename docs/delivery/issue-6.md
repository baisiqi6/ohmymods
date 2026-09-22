> **Agent provenance:** `Mac Max / Codex` · role=`Maintainer Operator` · acting_for=`ohmymods Mac Operator`

# Mac兼容源码归档交付记录

关联 [Issue #6](https://github.com/baisiqi6/ohmymods/issues/6)。两个架构的实验源码以v9.5.12为仓库审查基线，但其游戏基础加载验证来自此前v9.4.5官方Mod；不冒称新版本最终验收。

- [x64说明](../../compat/macos-x64/README.md) / [历史验证](../../compat/macos-x64/VALIDATION.md)
- [ARM64说明](../../compat/macos-arm64/README.md) / [历史验证](../../compat/macos-arm64/VALIDATION.md)
- [源文件hash](issue-6-source.json)：既有补丁、测试及必要manifest保持原样；工具只清理一行空白（interop-patcher/Program.cs），无代码token变化。公开README/摘要、ignore和许可证关联作整理。

2026-09-21公开前检查：两份Dobby patch各自对干净888d971214900374edbca6206fad6ded8a2c1311源码快照检查并应用成功；shell脚本语法检查通过；当前ARM64 patch源的nearest-gap合成测试15项通过。未重跑游戏，未声称本轮重建完整托管链。x64/Rosetta的guest RWX已知失败和ARM64固定路径限制均保留。

原始日志、存档/保存比较、会话、程序集及dump均留本地。必要短函数指纹仍在守卫源码中，用于拒绝错误版本；不是“零提取字节”声明。两份Dobby补丁均保留Apache-2.0许可证。

本PR另用独立文档提交同步用户裁定的平级Operator、Issue/PR查重认领与每小时异步消息约定；不修改checklist/runtime authority或把旧任务标done。正式发行、通用安装器及后续上游合并不是本次交付。

空白检查：源码/文档diff检查通过；patch文件包含unified diff所需的单空格上下文行，外层diff --check会报格式空白，单独对固定基线以git apply --check --whitespace=error-all检查其实际新增代码。
