# 第三方与游戏内容授权范围

## MIT 授权的范围

根目录 [MIT License](LICENSE) 只覆盖本项目维护者有权授权的自有 Mod 代码和文档。文件、目录或所附发行包已有其他许可声明时，应保留并遵守原声明；不能用根目录 MIT 许可证覆盖第三方内容。

## Mod 依赖与上游修改

本仓库的不同构建可能使用 Unity Mod Manager、Harmony/HarmonyX、BepInEx、Il2CppInterop、UnityDoorstop、Dobby、.NET 及其他组件。实际使用的版本和组件以对应构建配置及发行包清单为准，不能从本索引推断某个发行包必然包含全部组件。

- 对 BepInEx、Il2CppInterop、Doorstop、Dobby 等上游组件的补丁和修改，保留适用的上游授权及版权声明，不改标为本项目 MIT。
- 分发包含第三方组件的发行包时，应携带其所需的许可、版权和 NOTICE 等文件；适用 LGPL 等许可的组件还应按其要求提供对应源码、修改内容及重新构建或替换所需材料。
- 本文不替代各组件的原始许可证，也不代表已完成每种历史发行组合的授权审计。

## 游戏和 Unity 内容

Kingdom Two Crowns 的游戏本体、游戏程序集、反编译参考源码、模型、纹理、音频、Unity 库以及从游戏生成的 interop，不因被 Mod 引用或用于开发而获得本项目的 MIT 授权。第三方素材须有单独、明确的使用与分发依据。

用户应使用自己合法取得的游戏和开发引用；发布 Mod 或安装包时，应另行核对所含内容的再分发权限。

## 历史发行包

仓库既有的 `release/KingdomEnhancedMod_v2.4.0_IL2CPP.zip` 包含 BepInEx、.NET、Unity 等组件，但包内未附 LICENSE、NOTICE 或 COPYING 文件。它的随包许可材料尚未补齐；本次自有源码 MIT 授权不代表该历史 ZIP 的全部内容已获得再分发授权。使用或重新发布该包前，应分别补齐组件许可证、适用的对应源码及游戏/Unity 内容的分发依据。
