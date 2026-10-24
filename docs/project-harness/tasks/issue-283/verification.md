# #283 验证记录

本次代码/美术候选已完成审查及下列host/纯编译验证；未合并、未安装或启动游戏，不表示Native验收完成。基线98259af，精确源码/DLL/资源hash见[回执](receipts/verification.json)，素材像素证明见[art](receipts/art.json)，[离线预览](preview.png)。

## 改动与审查

左岛两组疏林，右岛低砂岩草石，岸外小礁石和短海纹；沿用满意的岛形/比例、原始岸线PNG及base alpha/outline/双mask。装饰为原shore下独立不可点击静态Image，复用原缓存与owner清理，不进原生输入采样或岛屿生成/存档。4个高对比陆地框进入总览/详情规划，优先八个图标。

GLM5.3/max阶段决策复审通过。先前从grep缺失推出原生矩形命中的结论经实际2.4.3 Image采样路径审计撤销，未沿用该假设。实际实现由OMP DeepSeek Flash/max，Operator和独立Astra复核；upper-bound恢复、同owner颜色更新及创建失败责任遗漏均已按定向红例修正，最终hash绑定approved。原生借用Image未知原值/身份/迟到owner保护保留。

## 验证结果

| 范围 | 结果 | 证明边界 |
|---|---|---|
| actual source-linked VariantProbe | 647检查通过，构建0W0E | 两入口显示/8图/owner、故障清理、exact blocker排除及原输入字段保持 |
| ShoreArtTests | 123通过，构建0W0E | 资产、真实Prepare、边界负例、缓存/借用租约 |
| Art variants | 105通过，0W0E | 原始decoded alpha及prepared geometry goldens；最终仅租约增量后复用 |
| optional B缺失 | 3通过 | B底图不被可选装饰失败破坏 |
| 独立两入口避让 | 198通过，0W0E | 去掉任一入口调用均准确碰撞失败 |
| exact SanitizeClone方法体提取host | 31通过，1W0E | 只清clone自有节点、母体/原生保留；去名负例有效；唯一CS0169来自既有测试桩未用字段 |
| Mac240/Win240/Win243完整输入闭包包装器 | 各0W0E | 官方Compile/277资源闭包，私有NoDeploy输出；Win243单独PR250 alias，非原生产工程Build |
| DLL内资源逐一读回 | 三套各277/277 SHA一致 | 含两张最终R4装饰，未只验项目清单 |

额外运行旧RuntimeProbe全套：98259af原始源码/原测试桩与本次候选均1040检查、39失败，失败文本和顺序逐条相同，无新增。**这套旧全量测试未通过，不能写成全绿**；其旧单岛/16图等断言维护不在本次装饰范围。原始对照日志及hash保留任务私有证据，回执记录这一限制。

## 未验与交付边界

- 尚无Native窗口的原尺度、选岛输入、原生Selectable tint/裁剪、实际Instantiate/延迟销毁、draw-call/FPS验收；host和三平台引用编译不能替代实机。
- 两纹理共享，每个地图owner最多4个静态显示view；未新增Update、每帧层级扫描或输入过滤器，不声称零渲染开销。
- Android当前显式编译/资源/pin闭包不含本次地图源/资源且不导入本props；未改Android、未重跑无变化设备/玩法，不称设备通过。
- 原岸线两PNG未改；无Main/build/Mono、Steam、游戏、正常存档/配置或运行DLL写入。Windows #281诊断候选保持。未安装/合并，Issue #283保持开放；后续Native窗口和合并安装另留实际证据。
- EXharness checklist validator通过0警告；未直接修改canonical JSON/DB，未伪造受管完成状态。
