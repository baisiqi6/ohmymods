# 决策对抗审查回执：协作协议 2026-09-18 两次修订

按 collaboration-protocol.md 规则 9 落盘。审查者模型证据：两次均为 operator 主 agent（ZCode，builtin:bigmodel-coding-plan/GLM-5.3）派发的内置 subagent，继承主 agent 配置（GLM 5.3，thinking 强度随主 agent max 档）；内置分支依协议以"主 agent 为 GLM 5.3 max + 继承配置"作模型证据。

## R1：新增 operator 对抗审查规则（用户 2026-09-18 原话 → 协议落盘）

- 被审决策：如何把用户新规则写入 collaboration-protocol.md（blockquote 形式、决策定义、规则 9/10、流程图、模板、AGENTS.md 摘要）。
- 第一轮 verdict：**需修订后执行**，7 条意见（用户原话须逐字引用并与执行解释分离；决策枚举不完备可被博弈，补侦查诊断/worker 异常处置/测试范围/版本账目并加"边界从疑"兜底；规则 2 需加注记防误读；修正后必须复审+僵持两轮上限；紧急止损窄豁免；审查强制落记录否则等于口头自述；决策审查者删除对内置 subagent 的无条件 GLM 5.3 承诺、补外部路由兜底；素材路由补 file-only 双轨）。
- 修订后复审 verdict：**通过**（七条全部落盘、用户原话逐字符一致、全文自洽；3 处轻微不闭合为可选打磨）。打磨意见（决策链定义、流程图示意注、规则 6 术语含决策审查者）已当场采纳。
- 结果：collaboration-protocol.md 顶部引用块+执行解释节+角色表行+双流程图关口+规则 2 注记+规则 9/10+决策对抗审查委派模板；AGENTS.md 摘要新增；progress.md 记录。

## R2：素材路由规则 10 事实修订（用户同日指正 deepseek-flash 即 V4.1 Flash）

- 被审决策：规则 10 及联动文本的事实修正方案。
- 审查者独立核验：官方定价页与 changelog fetch 复核（deepseek-flash=V4.1 Flash 原生多模态；deepseek-v4-flash / deepseek-v4-flash-vision-exp 已下线为临时别名；deepseek-v4-pro 不支持图像）、本机 `omp models` 只读实测（18.1.19，images 标志与官方相反）、全仓 grep 同步点。
- verdict：**修订后执行**，6 条修订全部采纳落实：①L34 加能力标志例外与规则 10 交叉引用（防自相矛盾）；②OMP 更新/目录修正挂用户授权+未授权期替代路径（内置 subagent / text-only）；③事实句加"截至 2026-09-18"时间戳+补 deepseek-v4-pro 不支持图像；④progress.md 原地改但保留用户指正事实不静默改写；⑤AGENTS.md 压缩版保留对抗交互/双审与指针、删已下线别名示例；⑥本回执文件本身（补 R1 的落盘缺口）。
- 教训留痕：R1 的 7 条意见没有抓出"images=yes 选型会选中已下线别名"这一事实错误，由用户指正发现——对抗审查不能替代对权威事实源的核对，规则 10 已写入"以官方文档+实际模型事件核验"。

## 关联

- 协议正文：docs/project-harness/collaboration-protocol.md（L3-L4 引用块、L34 例外、执行解释节、规则 9/10、决策对抗审查模板）。
- 摘要：AGENTS.md 协作规范节。
- 未做（待用户授权）：本机 OMP 更新或模型目录修正；未做消耗额度的空探测。
