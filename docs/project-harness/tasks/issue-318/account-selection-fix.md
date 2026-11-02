# Issue 318 — 挑战账户选择根因与修复

2026-11-01，Mac Max / Codex。新资料含完整诊断 DLL、专用原因日志、原生档与配置，已只读冻结 520 文件 / 260 唯一内容。重复副本哈希相同；不把接收目录、文件名或时间推断为一次原子现场。原始日志和存档不进入 Git。

## 证据与根因

玩家 DLL 与交付的诊断候选一致。专用日志记录：英雄原生/文档目录均为 campaigns4/challenges6，hero-async 通过；随后 bank-async first=true 以 `ensure-refused:save-account` 拒绝。此前旧材料的英雄5/6假设不能作为本轮根因。

原生档活动 challenge=2，挑战目录实际顺序 `[10,9,4,8,1,2]`。`SharedBankNative.Account` 使用 `normalCount + currentChallenge - 1`，错误选中 ID9；当前原生对象实际是末尾 ID2。发行指纹匹配的 Windows2.4.3 `GetCurrentCampaign → GetCampaign → FindCampaignForChallengeId` 按 challengeId 扫描查找，挑战编号不是数组下标。

银行附加 key 缺失本身合法。独立真实源码链接复现排除了该前置故障：Ensure 健康 → 原 Account 返回 false → BeginSave 记录 save account → Prepare 拒绝 → 异步回调一次失败码136，pref 未改。该原因不属于既有可恢复白名单，故错误判定随后持续阻止保存。此证明定位了确定的代码缺陷，不预付所有运行时异常都已排除。

## 修复范围

只修改 `Account`：使用原生已经解析的当前对象，在整个缓存目录中要求唯一指针匹配，再核对普通/挑战分区及边界、空值、零指针。保留 owner、读取异常、捕获责任、余额和存档保护；不清空 fault、不重试、不补/裁 GUID、不修写玩家档，不合并账户。缺少当前对象的旧缓存仍拒绝，未加入推测性恢复。

原测试桩也复制了 enum-1 假设，本轮按实际原生 ID 查找与空行异常契约修正。新增测试链接完整生产保存链；全部 pref 键值含三个不透明购买哨兵前后比较，不只比较数量。

## 验证

- 原逻辑红测：7 通过、11 失败，失败在账户选择断言；修复后新回归18/0、英雄银行组合22/0、recovery5/0、capture5/0，均0 warning / 0 error。Root 在最终仓库源码上重跑，合计50项通过。
- 独立最终复审通过，当前仓库源码20/0；额外覆盖缺失当前缓存与既有不可恢复捕获故障仍拒绝，全部 pref 保持。
- 实际 Windows2.4.3 发行闭包完整构建0W0E；对诊断基线的196项 IL 记录差异全部属于 Account，方法外差异0，38资源保持。临时验证 DLL `26c8bf4b0a487542f92d5a72887998f71aaa877de45e80f8606086fc2c291a33` 仅为构建证据，未安装或交付给玩家。
- 已有 hotpath 工程的3个桩类型编译错误，在生产改动前后完全相同，未计为通过。Mac 全树在改动前离线恢复即缺少 BepInEx.PluginInfoProps，未安装新依赖，未宣称 Mac 全树通过。
- 未启动真实游戏、未写正常 DLL/配置/原生存档，玩家原生复测待交付新版后进行。既有 Windows11.1.0 源 recipe、公开资产及正式版本号保持；代码 PR 与后续发行、安装分别记录。

固定源码、原生反汇编、输入哈希、红绿测试、审查与构建回执在本机 `.local/tasks/player-win243-save-failure-20261031/player-material-20261101/`。Word 中只有游戏控制台输出，没有收集工具自身的失败栈；工具故障未定案，本轮资料已足够，不再要求玩家重复收集。
