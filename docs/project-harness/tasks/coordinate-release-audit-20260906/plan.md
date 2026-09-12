# v4.5.0 发布材料一致性审计

Task ID：`coordinate-release-audit-20260906`。

## 问题与目标

v4.5.0 发布包含 source commit、插件/程序集版本、ZIP/DLL 摘要、构建记录与实测边界。需要一份可复核的审计报告，区分这些已提交材料互相支持的事实、仍需实物复验的声明，以及任何不一致。此任务同时验证本项目新接入的 Coordinate 受管任务流程。

审计基线为已提交的 `2437c6d28de71849168884cc8e4ff9fb73fe8579`；该提交记录的 release tag 指向 `c2032185a5bc213f085a5831abedbbaacfab8b36`。两者用途不同，不能因为 receipt 后续提交推进而称 tag 指错。工作区中的后续游戏候选与用户未提交改动不在本次验收范围。

## 输入与唯一产出

输入限定为本任务隔离工作树中的已跟踪文件：

- `scripts/package_release.py`；
- `il2cpp/KingdomEnhancedMod.csproj`；
- `docs/project-harness/tasks/release-450-20260906/` 中的发布、构建、测试、metadata、package receipt 与接受边界记录；
- `release/` 中的玩家说明文档。

worker 只可新增或修订本目录的 `result.md`。harness 状态、配置、plan、Git 与部署动作归 Operator；worker 不修改其它文件。

## 验收标准

1. 报告列出实际读取的每个输入的仓库相对路径和 SHA-256，并说明读取的是哪一个 Git 基线；不使用绝对个人目录作可复现接口。
2. 逐项核对 tag/source commit、plugin/assembly version、ZIP/DLL size/hash、package receipt 与公开说明中的对应值，给出具体路径或字段定位。
3. 区分“本轮静态读取/重算已验证”“材料中记载但本轮未独立运行验证”“不一致/未知”。没有 ZIP/DLL 实物就不能宣称重新验过其哈希、CRC 或实际 IL；没有运行测试就不能把原测试记录当成本轮新测试。
4. 对发布脚本的文件边界做静态检查，记录已实现的排除/验证条件与无法仅靠源码证明的运行时事实；不执行打包脚本。
5. 保留联机、换岛、authority 等原有待实测边界。审计完成不改变这些游戏功能的 doing/done，不代表新的游戏版本验收。
6. 独立 reviewer 检查实际报告及输入，Operator 重新计算输入 hashes；出现问题则返工、生成新 review packet 并重新绑定 review SHA。
7. 当前实例 checklist validator 通过；已有游戏任务字段保持不变。完整过程经 Coordinate file/record、真实 Windows managed worker、正常 completion receipt 收口，不能用 repair-only 或伪造历史事件。

## 约束

不修改/构建/部署游戏代码或 DLL；不运行游戏；不访问 Steam 安装目录、测试游戏目录、配置、日志或存档；不发送 Discord/KOOK/其它消息；不创建游戏 Release/tag；不读取 credential；不直接改 checklist JSON。

## 交付与恢复

Operator 先发布 initial task 文件，再由 Remote MCP 记录。worker/report 和独立 review 完成后，先提交并部署 review-approved 文件，再准备 completion；done 文件部署后才 consume。同 operation/receipt 的网络重试保持幂等，新的审计修订使用独立 revision job key并保留旧证据。

最终 owned 工具、任务与进展记录合入主开发分支，持久主工作区及服务器读回一致后，才能退役本任务工作树。未能安全合入时保留工作树和明确的待集成状态，不覆盖原 Operator 的未提交材料。
