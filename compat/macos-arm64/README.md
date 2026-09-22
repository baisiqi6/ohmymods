# Mac ARM64 固定游戏构建兼容实验

目标 Kingdom Two Crowns 2.4.0 r23485 / Unity 6000.0.61f1。GameAssembly SHA256 `738fb98871dd6e2136474325ea3f7f4f81f2094873a6bc88f7a942d268660b1a`，Dobby基线 `888d971214900374edbca6206fad6ded8a2c1311`。

前序独立ARM64副本已进入游戏并加载Mod，用户基础游玩未反馈异常。该反馈发生在此前v9.4.5候选，不是v9.5.12共享修复的最终验收。本目录是实验源码归档，不是正式安装包或通用ARM64支持承诺。

## 修复链与输入约束

1. x64目录的模块发现helper仍用于Mac dyld模块定位；先具备这个依赖。不得混用x64原生库。
2. `tools/patcher`保留Console writer，跳过当前MonoMod不兼容的Console.SetOut拦截；这是局部实验妥协。
3. `trampoline-patcher`令typed GenerateTrampoline直接Prepare + Marshal，省略未使用proxy。
4. `entry-shim` + `entry-patcher`将4B runtime_invoke导出跳转解析到固定函数体；hash、dyld、TEXT/RVA及短指纹守卫。
5. `injection-shim` + `injection-patcher`固定5个IL2CPP注入目标，适配实际单参GenericMethod ABI。
6. `thunk-patcher`只让X86/X64进入x86导出跳转解析，避免将ARM64 E9序言误识别。
7. 原生Dobby入口改为4B near分支，未取得可达位置时失败退出，不退回覆盖邻接短函数的长入口；`near-patcher`检查native返回码及空original。
8. 最近空隙选择以完整可达页为单位，下方空隙取尾部最多16页，保留64次预算与无覆盖分配。

工具拒绝未知输入。必要阶段hash见 [managed-inputs-and-stages.json](evidence/managed-inputs-and-stages.json)，历史最终产物见 [candidate-manifest.json](evidence/candidate-manifest.json)。它们是固定实验的来源记录，不是当前PR新构建证明。

## 构建与测试边界

ARM64工具仍保留固定实验路径 `/Applications/ohmymods-arm64-lab` 的引用，包括多个csproj、thunk测试和ABI probe。`native-probe/build.sh`亦依赖实验目录布局；**干净checkout不能据此一键重建完整加载链**。参数化安装/依赖获取不在本PR范围，不能为使构建通过而删除输入守卫。

原生补丁可在干净Dobby基线上 `git apply --check` 后应用到独立副本。使用CMake参数：

```sh
cmake -S /path/to/patched/Dobby -B /path/to/arm64-build \
  -DCMAKE_SYSTEM_NAME=Darwin -DCMAKE_SYSTEM_PROCESSOR=arm64 \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0 -DDOBBY_DEBUG=OFF -DDOBBY_GENERATE_SHARED=ON
cmake --build /path/to/arm64-build --target dobby
sh tests/nearest-gap/run.sh /path/to/patched/Dobby/source
```

`tests/near-fixture`是五种邻接短函数合成布局；`tools/native-probe`、`tools/abi-probe`保存原生调用/ABI检查。具体历史覆盖见 [VALIDATION.md](VALIDATION.md)。托管shim为net6，补丁工具为net8；ARM64 CoreCLR还需匹配的BepInEx托管依赖。尚未提供完整端到端复现脚本，不能把单个补丁应用成功当成完整加载链可重建。

## 公开内容与限制

两种架构各自保留补丁，不相互覆盖。[Dobby许可证](patches/Dobby-LICENSE)随patch保留。工具中的短入口指纹和12字节thunk测试fixture来自固定构建，服务于拒绝错误输入；本归档不包含完整游戏/反编译参考源码、原生dump、程序集缓存或存档。详细实验日志保留本地，公开仅保留必要hash和摘要。

Harmony可选stack-trace修复仍有PermissionDenied告警；并非所有MonoMod路径支持ARM64。近地址分配可能安全拒绝Hook；复杂玩法、真实存读档/切岛/联机仍待验证。上游联系见 [upstream/README.md](upstream/README.md)，不等待上游接受才继续本地Mod验收。
