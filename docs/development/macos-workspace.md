# Mac 开发与测试目录规范

本规范对应 [Issue #15](https://github.com/baisiqi6/ohmymods/issues/15)。它规定本机整理后的目标布局；迁移、构建和实机验证结果由执行者分别记录，不以目录存在代表验收通过。Windows 的构建与运行路径保持原有约定。

## 项目与本地资料

项目开发入口为 `~/projects/ohmymods`，`il2cpp/` 是 Mod 源码主线。游戏本体不是项目源码；`game-source/` 只用于按需保存只读反编译参考，本次 Mac 整理前没有该目录，不要求为了补齐目录而复制游戏资料。

```text
~/projects/ohmymods/
├── il2cpp/                         # Mod 主线源码
├── compat/                         # 加载兼容补丁及公开说明
├── docs/development/
├── game-source/                    # 可选，只读参考，不提交
└── .local/                         # 本机资料，不提交
    ├── build/Integration.csproj     # 本机构建包装器
    ├── toolchains/dotnet/           # 指向保留的 SDK
    ├── releases/<tag>/             # 原发行 ZIP、验收文件、校验和
    └── archive/
        ├── legacy-work/            # 历史工作目录，含旧仓库与 worktree
        └── runtimes/arm64-lab/     # 旧 ARM64 环境回滚资料
```

`.gitignore` 排除根目录 `.local/` 和各层级的 `game-source/` 游戏参考目录。游戏文件、生成的 interop 程序集、存档、原始玩家日志和私人机器信息不得提交到 GitHub。公开报告只保留必要的版本、提交、哈希和脱敏验收摘要。

## 运行目录

`/Applications/ohmymods/` 作为本机 Mod 游玩和玩家包测试入口，并保证当前用户可写。同一锁定游戏构建只保留一份物理 `.app`，不同环境通过目录发现或相对符号链接复用它，不为每次测试复制游戏。

```text
/Applications/ohmymods/
├── KingdomTwoCrowns.app/           # 此实验环境唯一物理游戏副本
├── KingdomTwoCrowns_Data -> KingdomTwoCrowns.app/Contents/Resources/Data
├── arm64/                         # 默认原生运行环境，包内容直接放这里
│   ├── launcher.command
│   ├── BepInEx/
│   └── dotnet/
├── x86_64/                        # 原 x64/Rosetta 环境
│   ├── run_bepinex.sh
│   ├── KingdomTwoCrowns.app -> ../KingdomTwoCrowns.app
│   ├── KingdomTwoCrowns_Data -> KingdomTwoCrowns.app/Contents/Resources/Data
│   ├── GameAssembly.dylib -> KingdomTwoCrowns.app/Contents/Frameworks/GameAssembly.dylib
│   ├── BepInEx/
│   └── dotnet/
└── player-tests/
    └── <tag>/
        ├── KingdomTwoCrowns.app -> ../../KingdomTwoCrowns.app
        └── OhMyMods-Mac-ARM64/     # 原发行 ZIP 解压目录
            ├── launcher.command
            ├── BepInEx/
            └── dotnet/
```

每个环境分别保留自己的 core、plugins、config、interop、cache 和运行日志，不在架构或候选版本之间共享可写目录。`arm64/` 放包内容本身，不再多套一层 `OhMyMods-Mac-ARM64/`；其启动器从父目录发现游戏。玩家测试目录保留 ZIP 原布局，启动器从父目录发现 `.app` 链接。

根目录的 `KingdomTwoCrowns_Data` 有加载器消费者，必须保留正确指向；它不属于可随意清理的旧 x64 残留。迁移后逐个检查链接实际指向，并运行各 ARM64 入口的 `--check-only`，不能仅凭目录树推断启动器接受链接。

ARM64 是 M 系列 Mac 的默认入口；`x86_64/` 保留原 x64/Rosetta 运行环境。移动 x64 文件、检查脚本语法与二进制架构只证明路径和文件关系，没有代替迁移后的实机验收。两个平台的通过记录不可互相代用。

原 x64 启动脚本依赖当前工作目录，保留原脚本时应先进入运行目录：

```sh
cd /Applications/ohmymods/x86_64
./run_bepinex.sh
```

不能从项目根用脚本绝对路径调用，也不能从上一级直接执行 `x86_64/run_bepinex.sh`；两者都不会自动切换工作目录。这是原脚本的调用约束，本次目录整理不修改脚本。

**这些目录不隔离游戏存档。** ARM64、x64 和玩家测试包仍使用同一游戏持久化目录；版本差异可能影响同一份进度。任何实机启动测试都先备份当前存档与配置，记录实际运行环境及产物哈希，不并行启动，不用旧备份覆盖用户后续进度。

本布局只服务已锁定的实验游戏构建。当前本机 Steam 构建不在此 ARM64 包支持范围内，Steam 安装保持独立且不修改；未来支持其他构建时另行核验指纹与验收，不能替换根目录游戏后沿用旧通过结论。

## 玩家测试版本与清理

每次待发布候选或正式发行包使用独立目录 `player-tests/<tag-or-candidate-id>/`。从原 ZIP 解压，记录源码提交、ZIP SHA-256、游戏指纹以及冷启动、热启动、玩法的实际验证范围。需要重测已写入配置或缓存的版本时，使用不同 attempt 标识并保留前次结果；不要混装 DLL 后继续称其为原发行包。

当前发行基线为 [v9.5.13-mac-arm64-preview.1](https://github.com/baisiqi6/ohmymods/releases/tag/v9.5.13-mac-arm64-preview.1)：

- 源码提交：`0d359ee5ac8d9cedc62a2fe1d53bc19d87790ef3`。
- ZIP：`OhMyMods-9.5.13-mac-arm64-preview.1.zip`。
- ZIP SHA-256：`06d7e15bebab95c1acbc596c938eea4f56ae932ecace50336b363a4cf6749cb5`。

将原 ZIP、`mac-arm64-acceptance.json`、`SHA256SUMS` 及来源记录保存在 `.local/releases/<tag>/`，不会因整理本地目录修改已发布资产。首次信任与玩家安装步骤沿用 [包内指南](../../compat/macos-arm64/package/README.md)，目录迁移不代表系统信任状态已验证。

清理过期测试目录前，先保留验收摘要、必要日志、独有配置与回滚材料，再核对待删内容。重复 `.app` 必须逐文件确认与保留副本一致，额外日志先归档；不得凭文件名、目录大小或单个 GameAssembly 哈希就删除整包。原始安装介质、Steam 游戏、存档和未提交源码不纳入重复测试文件清理。APFS 可能共享文件数据，不将逻辑字节总和宣称为实际释放空间。

## 历史目录与本机构建

旧工作目录整体保存在 `.local/archive/legacy-work/`，其中旧仓库的脏文件、分支和关联 worktree 一并保全。旧位置的 `work` 仅作为兼容符号链接，让历史报告、Git worktree 路径和协作监控游标继续解析；后续开发从新项目入口开始。迁移前后核对各 worktree 的 HEAD、状态与未提交文件哈希，不用 `git reset`、`clean` 或删除未提交内容来修正迁移差异。

旧 ARM64 实验环境的非游戏内容保存在 `.local/archive/runtimes/arm64-lab/`，用于追溯和回滚；归档位置不承诺能直接运行。历史兼容工具仍可能引用旧实验路径，不能把归档移动当成这些工具已完成参数化。

本机 SDK 使用 `.local/toolchains/dotnet` 链接到归档中的 `legacy-work/mac-probe/sdk`。本机构建包装器 `.local/build/Integration.csproj` 引用当前项目的 `il2cpp/` 以及新 `arm64/BepInEx/` 的 core、interop 等依赖；它沿用 Mac 验证所需的 net6 目标，构建只输出到本地，不自动安装：

```sh
cd ~/projects/ohmymods
.local/toolchains/dotnet/dotnet build .local/build/Integration.csproj -c Release
```

该包装器和 SDK 是本机准备的资料，不随 Git 分发。**干净 clone 没有 `.local/`，不能直接运行上述命令。** 新宿主先阅读 [ARM64 兼容构建边界](../../compat/macos-arm64/README.md) 与 [x64 兼容说明](../../compat/macos-x64/README.md)，准备匹配的 SDK、加载依赖、游戏指纹和引用程序集；目前未提供从干净 clone 一键重建整个加载链的承诺。

Mac 与 Windows 维护者地位平等。任何一端推进修改前在 GitHub Issue 对齐目标、基线、分支和修改范围，并由接收需求方负责汇报；共享目标和验收标准，但 Mac ARM64、Mac x64/Rosetta、Windows 各自记录测试，不用一端通过替代另一端结果。
