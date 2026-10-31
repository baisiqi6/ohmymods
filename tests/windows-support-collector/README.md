# Windows 排查资料收集器合成测试

目标：在 Windows PowerShell 5.1 上，用临时合成目录验证
`release/support-tools/windows/OhMyMods-CollectDiagnostics.ps1` 的实际行为。
测试不读取真实游戏目录、真实 `%USERPROFILE%` 存档或真实输出目录。

## 运行

在 Windows 10/11 的普通用户 PowerShell 中，从仓库根目录执行：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\tests\windows-support-collector\Test-CollectDiagnostics.ps1
```

不需要 Pester、安装模块或管理员权限。进程探针在独立子 PowerShell 中模拟，
不启动同名假游戏，也不依赖或停止真实游戏。脚本自行创建
`%TEMP%\ohmymods-collector-<随机>`，结束后删除；调试时加 `-KeepFixtures`
可保留并在结尾打印路径。

退出码：全部通过 `0`，任一失败 `1`。每个 case 输出 `[PASS]` / `[FAIL]`。
收集器脚本不存在时打印 `RED` 并退出 `1`。

## 覆盖范围

- 中文与空格路径下的完整正向收集；校验 ZIP allowlist、逐项 size/SHA256、
  `manifest.json`、元数据（EXE/插件 DLL 只取版本与哈希，不复制）、结果 README。
- 采集前后源文件内容哈希与 mtime 不变。
- 重复运行生成新运行目录，不覆盖旧 ZIP。
- 缺少主日志/存档时仍生成 ZIP，退出码 `2`，必需类别缺口可见。
- 独占锁文件报错但不中止；普通 `FileShare.ReadWrite` 共享写者仍可读。
- 目录 junction/reparse 不跟随，不收集链接目标。
- 单文件、总字节、文件数、深度上限；拒绝高于默认值的测试参数。
- 无效/重叠输出根、非法三根 override（不得回退默认真实资料）。
- 游戏根缺少 EXE/BepInEx 时拒绝，不扫描父目录。
- 控制子进程中的 Get-Process 返回值，确认 false/true 时报告与退出码翻转。
  仅为探针模拟，不是游戏内验收。中文 ZIP 文件名通过默认 ZIP 读取器校验。

另有两个独立测试入口：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\windows-support-collector\Test-CollectorEdges.ps1 -CollectorPath <收集器绝对路径> -WorkRoot <新建的短 TEMP 目录>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\windows-support-collector\Test-CmdEntry.ps1 -CmdSource <CMD绝对路径>
```

Edges 覆盖祖先 junction、元数据读取限额、同 mtime 不同长度的变化、目录遍历上限。
CMD 测试使用子进程私有 PATH 中的 stub，验证中文路径、启动参数和 0/1/2 状态传递。
仅在新建 TEMP 中运行；不调用真实收集入口的默认档路径，不打开 Explorer。

## 边界

- 仅面向 Windows PowerShell 5.1；不承诺在 PowerShell 7 / 非 Windows 上运行。
- 合成测试证明脚本逻辑与 ZIP 契约，不证明真实游戏目录、真实存档权限或
  真实杀毒软件环境；这些需要维护者在 Windows 上额外冒烟。
- 测试会创建/删除自己的 TEMP fixture，不安装任何东西，不访问网络。
