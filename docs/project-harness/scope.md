# ohmymods — 项目范围

## 项目是什么

《王国：两位君主：奥林匹斯的召唤》(Kingdom Two Crowns: Call of Olympus) 的 UnityModManager (UMM) + Harmony mod。
以希腊世界为基准，把北境（norselands）的兵种体系引入希腊：狂战士、忍者、北境工匠（带盾）、北境居民，
同时保持两种形象的视觉一致性和原生行为（盾牌防御、挥砍、拾取）。

## 长期目标

- 希腊世界可原生招募北境兵种（狂战士、忍者），无需每局 hack 生成。
- 北境工匠出生自带盾牌（希腊无盾牌商店，槽位被狂战士商店占用）。
- 单位缩放：北境形象与希腊形象最终视觉高度一致；狂战士/鹿/小动物有差异化缩放。
- 性能：单位缩放零每帧扫描（y 轴守护替代 FindObjectsOfType 轮询）。
- 代码可维护：patch 按职责拆文件（Patch_Castle/Patch_ShopPlanner/Patch_Worker...），决策落盘到本 harness。

## Non-Goals（明确不做）

- 不改游戏资源文件（prefab/贴图/动画），一切通过运行时 Harmony patch + 代码逻辑。
- 不做联机专用的新协议（沿用游戏原生 RPC/序列化，仅必要时注册 sync 池）。
- 不碰希腊原版兵种的行为（除缩放对齐外）。
- 不把反编译的 Assembly-CSharp 源码纳入本仓库（那是游戏的只读参考，放 E 盘游戏目录）。
- 不引入第三方依赖（只用 UMM + Harmony v1.2 + 游戏原生 API）。
- 不做 UI/菜单（mod 开关在 UMM 的 Main.Enabled）。

## 环境约束

- 语言：C# 5（Framework 4.7.2 csc.exe 命令行编译，无 NuGet）。
- 框架：UnityModManager + Harmony v1.2（不是 BepInEx）。
- 游戏版本：Kingdom Two Crowns: Call of Olympus 2.0.1（P2P 版，Mono，Revision 21960，coo-day0）。
- 编译：build.bat → MyMod.dll → 拷贝到游戏 Mods/MyMod/。
- 源码参考：`game-source/Assembly-CSharp/`（版本标注见 game-source/README.md）。
- 协作流程：见 `collaboration-protocol.md`（Operator/Worker/Reviewer 顺位与模型约定）。
