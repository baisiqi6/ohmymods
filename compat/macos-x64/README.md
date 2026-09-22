# macOS x86_64 / Rosetta 2 固定构建兼容归档

本目录保存已在本地使用的加载修复，不是正式 Mac 安装包。目标为 Kingdom Two Crowns 2.4.0 r23485 / Unity 6000.0.61f1、BepInEx 6.0.0-be.788+5b766a3，Dobby 固定 `888d971214900374edbca6206fad6ded8a2c1311`。版本见 [versions.json](versions.json)。

## 修复范围

- x64 短条件跳转扩宽后按新旧 next-IP 正确计算位移。
- Mach 固定地址预留不覆盖既有映射，禁用已有映射零字节代码洞后备路径。
- 近地址搜索处理窗口边界/单页空隙，首次分配尝试最多16页连续预留，遇冲突即停。
- CodePatch 保留可观察的入口页权限；Cpp2IL 属性读取守卫与 IL2CPP 模块查找 helper 随源码归档。

**已知限制：** Rosetta 可能在 Mach 查询中将已执行的 guest RWX 页呈现为 RX；修复无法据此还原隐藏权限。`tests/known-limit-rosetta-tracked-page.c` 是仍失败的独立反例，不包含在绿色探针中。错误路径故障注入和其他指令形态没有完整覆盖；不可用于证明所有动态 Hook 安全。

## 构建与验证

需要 Xcode Command Line Tools、CMake、.NET SDK 8、net6 reference pack 缓存，以及自行准备的匹配 BepInEx core。第三方程序集、游戏与存档不在仓库内。Dobby 源须为上述 commit 的干净 checkout。

```sh
./build.sh --dobby-src /path/to/clean/Dobby \
  --bepinex-core /path/to/BepInEx/core \
  --dotnet /path/to/dotnet --cmake /path/to/cmake
./verify.sh
./verify-memory.sh /path/to/cmake
```

`build.sh` 只写本目录 `artifacts/`，拒绝未知产物目录，不部署或启动游戏。使用本地依赖，不下载游戏/框架。Intel Mac 原生运行 x64 探针，Apple Silicon 需 Rosetta 2。构建仅选 `dobby` target，不构建不相关 ObjC 插件。`verify.sh` 可用 `--dobby /path/to/libdobby.dylib` 指定已有库。

四个探针覆盖短跳转、200次安装卸载/20,000次调用、可观察RX/RWX权限、1,536次密集Hook；额外 Mach 空隙/预留共9场景。有限历史结果见 [验证摘要](VALIDATION.md)。不会执行上面的已知失败反例；运行它应单独隔离进程。

## 来源与公开材料

Dobby patch 适用上述固定commit，许可证见 [Dobby-LICENSE](patches/Dobby-LICENSE)。[上游反馈](UPSTREAM.md)与本地验证独立。工具含少量固定游戏入口指纹，用于输入一致性校验；没有上传完整游戏、反编译参考源码或原生 dump。原始实验日志保留本地，公开仅有验证摘要与必要hash。

[历史hash](evidence/hashes.json) 对应早期固定输入及当时产物，不能当成最终库hash；构建后的 `artifacts/build-info.json` 标识此次输出。构建时间戳等可能改变二进制hash，不声称位级可复现。Mac ARM64 是[独立实验链](../macos-arm64/README.md)，Windows 不使用本目录产物。
