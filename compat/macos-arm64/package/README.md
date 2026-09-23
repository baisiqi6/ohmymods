# OhMyMods Mac ARM64 发行包（Kingdom Two Crowns）

这是供 **Apple Silicon Mac** 使用的 Mod 实验包。把解压得到的文件夹放在游戏旁边，
双击 `launcher.command` 启动；玩家无需安装开发 SDK，也不必每次手输终端命令。
仅支持下述已锁定的 Kingdom Two Crowns `2.4.0 r23485` 实验构建。

`game-lock.json` 锁定的 GameAssembly SHA256：
`738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`。
其他游戏版本会被启动器明确拒绝（不猜测、不降级、不静默兼容）。

---

## 一、玩家指南

### 适用范围（先读）

- 本包只支持**已锁定的 DMG 实验构建**：游戏的四处指纹
  （GameAssembly / 可执行文件 / `global-metadata.dat` / `Info.plist`）必须与
  包内 `game-lock.json` 逐一匹配，任一不同都会被拒绝启动。锁定的 GameAssembly
  SHA256 为 `738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`。
- **当前本机 Steam 版游戏与该实验构建不同，本包不支持**（尚未适配 Steam 构建）；
  也不要假设任意 `2.4.0` 都能用——以指纹校验为准。
- 本包**不含游戏本体**，只分发 Mod 与所需加载依赖；请自行准备上述锁定构建，并只从
  本项目 **GitHub Releases** 获取本包（发布说明给出 ZIP 的 SHA-256，可用
  `shasum -a 256 <下载的.zip>` 核对）。本包当前是**未做 Developer ID 签名和 Apple 公证**的实验发行。
- 本包的精确 Mod 版本以 `package-manifest.json` 和对应 GitHub Release 为准；加载器仍只支持上面锁定的游戏构建。

### 本轮整合的玩法变化

相对 9.5.13，整合了火枪手举旗编队攻击传送门、弩手不被举旗误招、税收官瞬移时保持地面高度、武士冲刺后返队、换岛召回留在旧岛的狗、宠物与隐士防抓及被抓找回、骑士风格分配面板。宠物与隐士防抓的新开关**默认关闭**；升级后要继续保护隐士，需要在 F5 面板主动开启。

新功能在 Mac 上的实际验收范围以本次 Release 附带的验收文件为准。编译、自动回归或加载成功不代表换岛、战斗、存读档和联机玩法均已人工验证。

### 系统要求

- Apple Silicon Mac（M 系列），原生 arm64；当前验证环境为 macOS 26.6.2，其他系统版本未单独验收。
- 上述已锁定指纹的 Kingdom Two Crowns DMG 实验构建。
- **首次启动需要联网**：包内不含 interop 程序集与 Unity 基础库，BepInEx 首启会
  从 `unity.bepinex.dev` 下载基础库并在本地生成兼容层，可能耗时数分钟；
  之后启动不再需要该下载。缓存位于包内 `BepInEx/interop`、`BepInEx/unity-libs`。

### 安装位置（两种任选其一）

1. **包在游戏旁边**（推荐）：解压 ZIP 得到 `OhMyMods-Mac-ARM64/` 文件夹，把它放到
   `KingdomTwoCrowns.app` 所在的同一目录：
   ```
   某目录/
     KingdomTwoCrowns.app
     OhMyMods-Mac-ARM64/   ← 启动器在这里
   ```
2. **游戏在包里面**：把 `KingdomTwoCrowns.app` 拖入 `OhMyMods-Mac-ARM64/` 内。

两种布局中应仅有一份游戏；两处都有时会报歧义，需要用 `--game` 明确指定。**包必须放在当前用户可写的位置**（不要放在只读 DMG 或系统
目录），否则启动器会明确报错。

### 数据别名（启动器唯一会在包外创建的东西）

为让加载器找到游戏资源，首次启动会在**游戏所在目录**创建一个目录别名，
指向 `.app` 内的原始数据；不会复制或修改游戏内容：

```
KingdomTwoCrowns_Data -> KingdomTwoCrowns.app/Contents/Resources/Data
```

- 这是纯别名，不是游戏文件，删除它不影响游戏本体；不再使用本包时可手动删除。
- 若该位置已被其他内容占用（真实目录、指向别处的链接、悬空链接），启动器会
  **拒绝替换**并提示手动处理——绝不破坏未知内容。
- GameAssembly 无需别名：启动器通过 `BEPINEX_GAME_ASSEMBLY_PATH` 直接指向
  `.app/Contents/Frameworks/GameAssembly.dylib` 绝对路径；游戏 Frameworks 目录
  也会前置到 `DYLD_LIBRARY_PATH` 供原生短名查找。

### 启动

双击 `launcher.command`。**通过浏览器从 GitHub Releases 下载**的 ZIP 通常会被 macOS 打上「下载隔离」
标记；本包是**未经过 Developer ID 签名和 Apple 公证的实验发行**，因此首次启动可能需要你明确放行：

1. 直接双击试试。若系统允许，即可直接启动。
2. 若系统提示无法打开/来源未知，请在「系统设置 → 隐私与安全性」中**只放行
   `launcher.command` 本身**（不要把整个文件夹加入任何例外，也不要在终端里对文件夹
   递归清除属性）。
3. 若启动器报「检测到本包原生库带有系统下载隔离属性」，说明 dyld 会拒绝加载这些
   动态库。启动器**不会自动清除**任何属性，而是给出一条一次性的终端命令，例如：

   ```sh
   cd /path/to/OhMyMods-Mac-ARM64
   ./launcher.command --trust-package
   ```

   该命令会：再次列出将被处理的本包原生库、说明风险范围，并要求输入大写 `TRUST`
   确认；确认后只清除**这些本包原生库**的 `com.apple.quarantine` 属性——不递归处理
   目录、不 `sudo`、不清除其他扩展属性、不重签二进制、不改系统安全设置、不启动游戏。
   完成后重新双击 `launcher.command` 即可。

只有在**确认本包来自本项目 GitHub Releases（或你本机本人的构建）** 时才执行
`--trust-package`：它会移除系统对该包原生库的下载隔离标记。包内 SHA256 只能核对包
内容是否被混入旧文件，**不能证明来源可信**。请勿使用 `xattr -cr` / `xattr -dr` 递归
清除整个文件夹，也不要关闭 Gatekeeper——那会波及游戏与无关文件。

进入游戏后按 **F5**（部分 Mac 键盘需 **Fn+F5**）或 **Ctrl+F10** 打开 Mod 面板。
以下终端用法仅供需要指定路径或诊断时使用：

```sh
cd /path/to/OhMyMods-Mac-ARM64
./launcher.command                      # 常规启动
./launcher.command --check-only         # 只读预检，不启动游戏（隔离检测同样非零退出，不清除任何属性）
./launcher.command --trust-package      # 一次性显式信任本包原生库（需输入 TRUST 确认，不启动游戏）
./launcher.command --game "/path/KingdomTwoCrowns.app"   # 显式指定游戏
./launcher.command -screen-width 1280 -screen-height 720 # 游戏参数原样转发
```

`--trust-package` 与 `--check-only`、游戏参数互斥（可以用 `--game` 指定目标）；同一
命令行里重复写 `--trust-package` 会被拒绝。该模式可重复运行：已清洁时报告「无可处理
文件」并以 0 退出，上次部分失败时只处理仍被隔离的文件。

保留参数会被拒绝（防止覆盖加载器 target/runtime/interop/config/程序集指向）：
`--doorstop` 及其任意破折/下划线变体（含 `=value` 形态）、`--unhollowed-path`
（含 `=value` 形态）。

### 每次启动的校验（有少量哈希开销，属预期）

启动器在任何写入之前都会：

1. 用包内 `SHA256SUMS` 校验全部不可变 payload（core/dotnet/doorstop/mod/
   defaults/文档），防混包或旧文件残留；
2. 校验游戏四指纹（GameAssembly / 可执行文件 / `global-metadata.dat` /
   `Info.plist`），对照 `game-lock.json`；
3. 只读检查固定名单内的本包原生库（14 个 `*.dylib`，与构建输入清单一致）：
   必须是物理包内的真实文件（符号链接/缺失即拒绝），且不得带下载隔离属性
   `com.apple.quarantine`（带隔离即拒绝启动并给出 `--trust-package` 指引，
   绝不会自动清除或修改任何扩展属性）；
4. 检查既有 `BepInEx.cfg` 的 loader 关键键兼容性（见下）。

大文件哈希带来少量启动期开销，这是刻意设计：后来混入的旧 DLL 能被每次启动
发现，而不是只在首次播种时校验。

### 安全承诺

- 不修改 `KingdomTwoCrowns.app`、不 sudo、不 chmod/chown、不删除任何缓存；
  预加载日志经 `BEPINEX_PRELOADER_LOG` 固定在包内 `preloader.log`（默认行为
  会写进 .app 的 MacOS 目录，本包显式改道）。
- 配置只在缺失时播种默认值；已存在的 `BepInEx/config/BepInEx.cfg` 与 Mod 配置
  **永不覆盖**（升级只迁移你自己的配置，见下）。
- 包内可写路径（`BepInEx`、`config`、`cache`、`interop`、`unity-libs`、日志、
  实际 cfg 文件）上出现符号链接（含断链）或非目录占位时直接拒绝——防止配置
  外部指向被悄悄覆盖；目录逐级受控创建。
- 既有 `BepInEx.cfg` 只读检查四个 loader 关键键，语义与 BepInEx 实际解析一致
  （只看 `[IL2CPP]` 段；同键后值覆盖前值；两侧空格忽略；键缺失与显式空值区分）：
  `IL2CPPInteropAssembliesPath` 必须为 `{BepInEx}`、`GlobalMetadataPath` 必须为
  `{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat`、
  `UpdateInteropAssemblies` 必须为 `true`；
  `ScanMethodRefs` 必须为 `false`——它的默认值是 `true`，因此**已有 cfg 必须显式
  写出 `ScanMethodRefs = false`**（缺这一项会被拒绝；`true`/`TRUE` 等价）。显式
  空值、同段末值为外部路径、或仅在 `[IL2CPP]` 之外写安全值都会被拒绝。不兼容时
  启动器解释原因并拒绝，由你手动迁移配置；Mod 功能配置不受影响、不会被改写。
  没有 cfg 时播种本包默认（含 `ScanMethodRefs = false`）。
- 包内锁文件防止同包并发启动；启动器还会检测任何正在运行的
  Kingdom Two Crowns（按进程可执行文件名精确匹配，改名后的 `.app` 或裸可执行
  文件同样命中）并要求先退出（进程枚举失败同样拒绝启动）。
- 下载隔离（`com.apple.quarantine`）：普通启动与 `--check-only` 只做只读检测。
  唯一的清除路径是你显式运行的 `--trust-package`：全量只读预检 → 精确输入 `TRUST`
  确认 → 占包内锁 → 复检游戏未运行/路径真实/无硬链接/哈希未变 → 仅对固定名单内的
  本包原生库逐个执行 `xattr -d com.apple.quarantine` 并读回。不递归、不使用 `sudo`、
  不清除其他扩展属性（如 `com.apple.provenance`）、不重签二进制、不触碰游戏文件、
  不自动启动游戏；部分失败会如实报告，可重跑且只处理仍被隔离的文件。
- Ctrl-C / 终端关闭 / kill 时：先转发终止并等待游戏子进程退出，再释放锁，
  不会删除用户文件、不留假锁。游戏崩溃导致的残留锁会给出人工恢复指引，
  启动器绝不基于 PID 猜测自动删除。

### 升级到新版本

1. 完全退出游戏。
2. 先备份旧包的 `BepInEx/config/` 和游戏存档目录（此构建通常在 `~/Library/Application Support/nl.noio.kingdom-two-crowns/`）。骑士身份附加档在旧包的 `BepInEx/config/KingdomEnhancedMod/ModSave/knight-identities.v1.json`，连同其 `.bak` 一起保留。
3. 解压新版本 ZIP，得到全新的 `OhMyMods-Mac-ARM64/` 文件夹（与旧文件夹并排）。
4. 把**旧包**的 `BepInEx/config/` 整个复制到**新包**同位置（只迁配置）。
   不要复制 core / plugins / dotnet / cache / interop——它们随包更新，缓存会重建
   （新 ZIP 本身不含 config/cache/interop，见构建器说明）。
5. 用新包启动。骑士风格手动重派后保存，身份附加档可能写成 schema v4；**旧版 Mod 不能读取 v4**。若要降级，必须同时恢复升级前备份的游戏存档与旧版身份附加档；仅保留旧包文件夹不足以保证回退。确认不再需要降级后，再自行清理旧包与备份。

### 常见问题

| 现象 | 含义与处理 |
| --- | --- |
| 「检测到本包原生库带有系统下载隔离属性」 | 下载隔离会让 macOS 拒绝加载这些原生库。确认包来自本项目 Releases 后，按提示运行一次 `launcher.command --trust-package`（终端里输入 `TRUST` 确认），再重新双击。 |
| 「包内文件校验失败（SHA256SUMS）」 | 包损坏或被混入旧文件。重新下载完整包。 |
| 「游戏版本与包锁定的版本不一致」等指纹错误 | 你的游戏与锁定的 DMG 实验构建不符（本包不支持当前 Steam 版）。请更换为四指纹匹配的构建。 |
| 「现有 BepInEx.cfg 的 … 与本包要求的不兼容」 | 你改过 loader 关键键。按提示手动改回后重试；Mod 功能配置不受影响。 |
| 「缺少 [IL2CPP] 的 ScanMethodRefs」 | BepInEx 对该键默认是 `true`，与本包要求（`false`）不同。请在 `[IL2CPP]` 段显式写 `ScanMethodRefs = false`。 |
| 「包内可写路径 … 是符号链接」 | 包内被手动做了链接（含断链）。移除后重试；这是防越界写保护。 |
| 「数据别名位置被占用」 | 游戏目录下已有 `KingdomTwoCrowns_Data` 且内容未知。确认无用后手动移走。 |
| 「发现残留锁文件」 | 上次启动未正常收尾（如整机断电）。确认游戏与终端都已退出后，按提示手动删除包内 `.launcher.lock`。启动器不会自动删它（避免 PID 复用误删）。 |
| 「检测到 Kingdom Two Crowns 已在运行」 | 先完全退出原版游戏（含 Steam 启动的实例）再用本包启动。 |
| 首启很慢/卡在生成 | 属正常：联网下载 Unity 基础库 + 生成 interop。请等待，日志见 `BepInEx/LogOutput.log`。 |
| 「包目录不可写」 | 把完整包移到用户可写目录（如「应用程序」或个人文件夹）。 |
| 拒绝 `--doorstop-*` / `--unhollowed-path` | 保留参数，不允许覆盖加载器/interop 指向。 |

---

## 二、构建者/操作员指南

### 文件与职责

| 文件 | 归属 | 说明 |
| --- | --- | --- |
| `launcher.command` | Worker | 启动器（bash 3.2 兼容，仅系统工具，无测试通道） |
| `build_package.py` | Worker | 确定性打包器（Python3 标准库，无测试放宽 flag） |
| `defaults/BepInEx.cfg` | Worker | 最小默认配置（仅缺失时播种；真实段 `[Logging.Disk]`） |
| `README.md` | Worker | 本文档 |
| `input-lock.example.json` | Worker | input-lock schema 示例（占位符哈希会被构建器拒绝） |
| `tests/` | Worker | 测试（mock-arch 注入 + import monkeypatch，见下） |
| `input-lock.json` | **Operator 独占** | 固化的构建输入清单（schema v2） |
| `third-party/` | **Operator 独占** | 许可/源码材料源目录 |
| `REBUILD.md`、`VALIDATION.md` | **Operator 独占** | 打包时经显式清单收录（package-material 类） |
| `tools/sanitize_codeview.py`、`metadata-sanitization-receipts/` | **其他 Worker** | 同上，仅在显式列入清单且哈希匹配时打包；构建器只读不改 |

### input-lock.json schema（v2）

见 `input-lock.example.json` 与 `build_package.py` 文档字符串。要点：

- `game` 四指纹：`assembly_sha256`（须等于契约固定值）、`executable_sha256`、
  `metadata_sha256`（= `Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat`）、
  `infoplist_sha256`（= `Contents/Info.plist`）。
- `files[]` 是唯一打包清单：路径、SHA256、mode（0644/0755）、source 三类
  （`input-root` / `package-material` / `notices`）。清单拒收重复/穿越/绝对/
  含换行路径；占位符哈希拒绝。
- 白名单子树（`BepInEx/core`、`dotnet`、`BepInEx/plugins/<mod>`、`defaults/`、
  notices 的 `third-party/`）内出现清单外文件会拒绝（二进制）或要求补清单；
  符号链接一律拒绝。操作员材料（REBUILD/VALIDATION/tools/receipts）可选，
  一旦列入即校验存在与哈希。

### 构建流程

```sh
# 说明：third-party/ 材料物理位于本 package/ 目录内，--notices-source 直接传
# 包目录自身（构建器从中读取 third-party/**，只读不改）。

# 1) 从实际输入扫描生成候选 lock（操作员复核并填 version 后固化为 input-lock.json）
python3 build_package.py \
  --input-root /Applications/ohmymods-arm64-lab \
  --notices-source . \
  --game-app "/Applications/ohmymods-arm64-lab/KingdomTwoCrowns.app" \
  --emit-lock-template candidate-lock.json

# 2) 用固化 lock 构建（输出已存在时默认拒绝，--force 覆盖）
python3 build_package.py \
  --input-root /Applications/ohmymods-arm64-lab \
  --notices-source . \
  --game-app "/Applications/ohmymods-arm64-lab/KingdomTwoCrowns.app" \
  --lock input-lock.json \
  --output /path/to/OhMyMods-Mac-ARM64.zip
```

ZIP 特性：单一根目录 `OhMyMods-Mac-ARM64/`；条目按路径排序；固定时间戳
2020-01-01T00:00:00；`launcher.command` 保留 0755；不含 interop / unity-libs /
cache / config / 日志 / 游戏本体；同内容字节级可复现。包内自动生成：

- `game-lock.json`：启动器运行时核验的游戏四指纹（本身被 SHA256SUMS 覆盖）；
- `package-manifest.json`：payload 逐文件 `sha256/size/mode/source` 对账 +
  生成物单列；
- `SHA256SUMS`：全部不可变 payload + 两个生成 JSON 的校验清单，供启动器每启
  用系统 `shasum --check` 核验。

三者是**防混包对账**，不是防恶意替换的签名（同目录无密钥，明说不冒充签名）。

### 测试

```sh
cd compat/macos-arm64/package
bash tests/run_tests.sh
```

测试不修改生产代码行为：需要观察启动命令时用 PATH 前置的 mock `arch(1)`
（测试自有脚本）捕获 argv/env；构建器经 `tests/builder_driver.py` 以 import
方式运行、契约常量 monkeypatch 为夹具哈希（生产脚本因此不含任何测试放宽
通道，套件内有静态断言）。不测「运行中游戏」门的用例还会注入一个 ps 替身
（只在枚举进程时过滤宿主机上的同名游戏），使套件在真机有游戏在跑时同样确定；
该门本身用真实进程单独覆盖。下载隔离用两类夹具：真实 `xattr`（合成文本文件，
验证其他属性/字节保持、取消/EOF、部分失败与读回）与仅对合成夹具脚本副本注入的 mock `xattr`
（严格记录 argv，可注入列举失败/删除失败/删除无效）。生产启动器固定调用
`/usr/bin/xattr`，不接受 PATH 或环境变量替换该命令。

覆盖：SHA256SUMS 每启校验（缺失/篡改 payload/篡改 game-lock）、游戏发现、
四指纹、架构、保留参数（含 `--unhollowed-path`）、cfg 兼容检查、锁与信号
清理（TERM 动态验证 + INT 在信号列表中的静态断言；后台作业的 SIGINT 会被
POSIX 置为忽略，故 INT 用静态断言）、可写路径 fail-closed、别名、配置哨兵
保全、`.app` 零改动、参数与双 `-e` DYLD 转发、`BEPINEX_PRELOADER_LOG` 包内、
运行中游戏检测、原生库下载隔离（普通/只读拒绝且零写入、固定名单防漂移、
`suggested command` 含空格路径可直接执行、参数重复/互斥、取消/EOF 零写入、
只清除名单内被隔离项且严格 argv、其他属性与字节保持、部分失败准确报告与
可重入、列举失败 fail-closed、删除无效时读回判定失败、文件/父路径符号链接
与硬链接拒绝、名单外文件不碰）、构建器（确定性/内容/权限/SHA256SUMS/manifest/
排除项/operator 材料/各类拒绝/契约常量强制/模板）。

**全部基于合成夹具，不接触真实游戏，不等于实机启动验收**——实际发布 ZIP 的
下载、首次双击、`--trust-package` 清隔离后的真实加载与最终发布仍是 Operator
gate。

### 已知边界（如实保留）

- 每次启动对 payload 与游戏大文件做 SHA256，有少量启动期开销（刻意设计）。
- 启动器为中文消息；不覆盖 Windows/x64/联机场景。
- 「运行中游戏」检测基于 `ps` comm 精确后缀匹配，无法阻止用户在本包启动后
  再手动开启原版游戏；进程枚举失败时 fail-closed 拒绝启动。
- 残留锁需人工删除（设计取舍，防 PID 复用误删）。
- 首启依赖 `unity.bepinex.dev` 可访问。
- 文件内容在校验后被并发修改的竞态不构造全系统安全保证，范围限定为启动期
  一致性检查。
- 本包**未做 Developer ID 签名和 Apple 公证**：`--trust-package` 只是把用户对「本包原生库」的一次性
  信任决定落到删除 `com.apple.quarantine` 上，不改变来源可信性判断。启动器保留
  其他所有扩展属性；若处理隔离后系统仍拒绝加载，请按提示把 `xattr` 完整输出反馈给维护者，
  不要使用 `xattr -cr` / `xattr -dr` 或关闭 Gatekeeper。
- `--trust-package` 只覆盖固定名单（14 个原生库）且要求文件为物理包内真实文件、
  无硬链接（nlink=1）；名单同步由测试断言（launcher 名单 == build 输入清单的
  `*.dylib` 集合）保证，漂移会在套件里直接失败。
