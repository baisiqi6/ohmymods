# candidate-package-007 — 2026-08-15 综合候选包

## 目标

- 把当前已提交的 IL2CPP 综合候选构建打成游戏根目录直装 zip，供用户实测酿酒师缩放、银行助手、主船容量等候选功能。
- 包内 DLL 必须与构建产物及独立测试副本完全同哈希，并包含最新中文使用说明、能力路线和更新修复日志。

## 边界

- 这是测试候选包，不把尚未实机关闭的功能任务误标为正式稳定版。
- 只从 `il2cpp/bin/Debug/KingdomEnhancedMod.dll` 打包；不得从旧 zip 或开发环境残留 DLL 取文件。
- zip 根层必须直接包含 doorstop、root dotnet、BepInEx、安装说明和三份中文文档；禁止版本目录套层和 `BepInEx/dotnet` 重复 runtime。
- 不修改 Steam、共享存档或 Mono；不把反编译参考源码纳入包或 Git。

## 验收

1. 打包脚本完成全部 required entries、UTF-8 和结构 smoke check。
2. 构建 DLL、独立副本 DLL、zip 内 DLL SHA-256 三方一致。
3. zip 中插件 DLL 恰好 1 个，root dotnet 存在，`BepInEx/dotnet` 为 0，版本顶层目录为 0。
4. 独立 reviewer 对包结构、manifest、候选状态文案和哈希给出 APPROVED。

## 当前交接

- 友好巨魔与视觉微调刷新后的 zip SHA-256=
  `4C44CDCC79B4CF30E58EE6CA20087692B797FA629055D02D61CACC436744832C`，40,565,824 bytes / 312 entries；
  manifest commit=`b875c10ca421fe96106c83dfac913c1bd4778f9f`、Dirty=false。
- 构建、独立副本与包内 DLL SHA-256 均为 `E8B06EC90772390262F5D3B1325059097391EBE0D04E6CC5E479BE66DBECB8BD`。
- 插件 DLL 恰 1、root dotnet 187、BepInEx/dotnet 0、版本顶层目录 0、required entries 无缺失、
  反编译源码与调试文件条目 0；20 个常规文本项及 `.doorstop_version` 严格 UTF-8。独立 reviewer
  APPROVED。用户可用该包实测外观和行为；
  运行时功能任务继续保持 doing，不因生成候选 zip 自动关闭。
