# 上游贡献

- https://github.com/BepInEx/Dobby/pull/13 — x64短条件跳转扩宽的next-IP计算；独立160用例，只有macOS x64实测。未声称修复所有相对跳转。
- https://github.com/BepInEx/Dobby/issues/14 — Mac近地址分配失败后代码洞重叠；当前完整实验补丁仍有失败处理待完善。
- Cpp2IL空值问题已有 https://github.com/SamboyCoding/Cpp2IL/issues/471 ，本机guard是已有问题的验证/适配，不冒称首次发现。

本地主线继续维护，不等待上游回复或合并。所有公开材料只含自有复现、补丁、日志摘要，不上传游戏二进制和用户存档。

- https://github.com/BepInEx/Dobby/issues/15 — CodePatch 破坏 JIT 代码/数据页写权限；包含可独立编译探针及 Rosetta guest 权限仍失败的反例，不冒称修复完整。
