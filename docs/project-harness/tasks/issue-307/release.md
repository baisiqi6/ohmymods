# Windows 11.1.0 正式发布回执

**11.1.0（Windows 2.4.3 r23537 x64）已发布**：[v11.1.0](https://github.com/baisiqi6/ohmymods/releases/tag/v11.1.0)，GitHub release ID 429176001，发布时间 **2026-10-30 15:17:09 UTC**。读回为非 Draft、非 Prerelease、Latest；tag 指向 PR #308 合并提交 `4ca7898e76d8bf3dbf10044c803cd45fedda0116`。本页只同步事实，不改已发 tag 或资产。

| 产物 | 字节 | SHA256 |
|---|---:|---|
| 完整Windows ZIP | 65760247 | `16896af0d38f75ed7dee44b3bae7a31864f4eb83ef0a662c26437756fc7483c0` |
| 自含源码ZIP | 3890983 | `bbe134e5f9f12d2a583a2c81f672616b5794a7c72d1455019419b6ad9439eca7` |
| 包内Mod DLL | 4348416 | `7fffc4b36932920af8652c826e487d3c21623d136e2f4ea64b6b4377bc84ade7` |

发布共5附件：完整ZIP、ZIP.sha256、自含源码ZIP、RELEASE_MANIFEST.json、SHA256SUMS。先在draft核服务端digest，再公开；公开后重新核对5/5 digest、size、SUMS和tag commit，全部一致。manifest独立成附件，避免自身hash循环。预合并ZIP `fc7d4d70…`不是最终下载包。

## 验证与真实边界

- clean merge提交再export/build **0W0E**，DLL和源码ZIP逐字等于已审候选。源码闭包287源/38资源/162引用，源包333项；复建与严格业务IL/资源证明见 [verification.md](verification.md) 及发布manifest逐suite pass/fail/skip表。
- merge后包相对预合并仅 `BUILD-MANIFEST.txt` 和兼容旧名 `ohmymods-win243-preview.json` 的commit/UTC及其派生SHA变化；448项/446payload、435固定依赖+3bootstrap和CRC再核通过。preview文件名保留为启动器兼容名，内容是正式11.1.0。
- 严格fullIL B→候选7036差异全部归类、unexpected0；4个真实保存方法只剥离纯观察片段，控制流保持。R2和源ZIP复建增量fullIL0语义差、38资源同。
- Root新工具回归35/35和13/13；独立产品主机回归、三平台全源编译分开记录。Android AdapterTests仍1503/1，仅旧WallEngineerRuntime pin；旧stubs不完整的3套Host证明未假称绿。没有Android资产或设备通过声明。
- **本轮未安装/未启动游戏、未写正常存档。** 当前Windows私人性能对照U保持，不以发布动作替换它。PS5.1 Parser和内存文件系统CheckOnly夹具不冒充真实游戏目录实测。
- 第一岛出航、生成、剪影、保存退出和冷重进已有玩家证据独立复用；本次重构建未新增Native启动。第二岛/四世界商店全流程/联机未统一验证；本Windows链没有Mac400u宫廷；人口掉帧不声称修好。

## 收尾

#250/#282的必要增量已由#308接回主线，旧PR以superseded关闭、来源分支保留，避免历史recipe输入不可达；不是声称旧PR原样merged。#281代码任务关闭。首次出航Knight.EmbarkArchers与Forest观察转 [#309](https://github.com/baisiqi6/ohmymods/issues/309)；#248更广适配、#275/#276性能、#286旧档盾卫、#277回填各保留原边界。

GLM5.3公开模型事件核实、请求thinking=max，精确merge/tag/draft核验/publish链与仅2文件事实收尾决策均approved。独立源码、工具/包和fullIL审查分开留证；不以模型自述代替Root复核。

Coordinate已通过create-files登记、EXharness validator通过；**record半边待有效入口恢复后同步**。未裸改canonical JSON、未用本地DB替代生产记录、未伪造completion；该管理镜像边界不篡改已完成的产品发布事实。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Maintainer.
