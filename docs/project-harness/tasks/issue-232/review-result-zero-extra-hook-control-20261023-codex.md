# CHANGES_REQUESTED — Batch A 零额外 Hook 加载对照方案

审查者：用户指定的原 Codex 会话，审查 DSH 后续增量；旧源码/旧运行的作者身份不因本轮审查变成全新独立证据。

审查范围：仅方案成立性、拟派生工具的契约/写入边界、结果判定及受管运行门。不审批实现、不授权本次 Native/构建/合并，也不宣布加载或玩法通过。

实际查看工作树 HEAD：`8d49205da9bcfa653d254dfa75287daa07eb2791`，分支 `dsh/android-economy-262-20261023`，审查前 clean。请求文件 `review-request-zero-extra-hook-control-20261023.md` SHA256：`aecca5f431c5dc485ec1ed8356f53dc8cb5d94707ec54a0cbfe67ea861af4d41`。请求页眉的 aa6ec19 是此前文档基线，不是本次实际 HEAD。

## 结论

**方向成立，但下列两项须在方案中明确后再批准。**复用冻结的609e观察器，保持候选c1e43/cfg16b/F35fae和新单次case，不另造观察器或运行框架，是合适的最小路线。不是要求重做既有源码/实际结果审查；修正后只复审这两项增量。

## 必须修正

### R1 / P2：最小派生须覆盖实际生效的身份、路径和写入目标

请求第56–61行当前仅列部分常量，不能把“复制后只替换这些值”当作完整派生合同：

- `candidate-fixture-control-controller/register.py:19–25,31–32,139–144,155–177` 还绑定旧owner/session、operation、旧前置plan SHA、BASE、packet路径、source报告、旧branch和历史reviewer。现DSH身份、已承接任务phase与新冻结HEAD须以当前实际回读绑定；不得复用历史reviewer名称冒充本轮审查。**既有task的split_operation若仍是当前合法identity应保留并核对，不能因换Operator而另造task/operation。**
- `next-candidate-fixture-control-case/packet/root-launch.py:12–18` 另有旧controller、case、objective及review路径，需一起重绑；实际执行的未变base/helper继续保留其固定引用与hash闭包，不因精简清单丢失transitive来源，也不为此复制一套库。
- **`.../packet/root-normal-readback.py:42–45` 有实际写入：向旧 `/Users/Admin/.codex/worktrees/android-economy-recovery/ohmymods/docs/project-harness/tasks/issue-232/plan.md` 执行 `mirror.write_bytes(p)`。**它不是纯读回。方案必须明确不写旧专项工作树：同步确有必要则仅同步DSH自有目标，并如实标为写入；不需要就移除该同步，保留canonical读回即可。不得靠旁路写入来满足新登记。

请在方案第5/6节列明这些生效绑定的处理边界，以及沿现行Coordinate生命周期登记/更新canonical的方式。保留前置字节、owner/phase、单次gate/source-review/window和保护文件核验，不通过改JSON、放宽assert或借旧批准绕过。

### R2 / P2：区分采集完备与本次加载结论，明确同段证据约束

`candidate-fixture-control-controller/controller.py:627–628` 复用 `loader-control-controller/controller.py:993–1004` 的 `evidence_reportable`：它只验证观察结束、postrun读取、清理及保护，**没有要求READY、没有排除同船NRE，也不解析产品InitPools日志**；前者`:935–936`及结尾退出码据此给出 `evidence-observed`/0。因此它可以在“完整采到失败”时为true，不能当作Batch A正结论。

请求第45–50/61行需明确：

- 保留原始诊断字段和退出码语义；`evidenceReportable=true`或进程exit0仅说明可报告，不说明加载成功。失败事实完整也应保留，不为“绿”改变其语义。
- 限定正结论必须来自**同一可信fresh observation segment**：实际候选SHA归属、609e INIT合同及 `controlValid` 成立，真实READY之后按原观察契约正常STOP、无同船NRE，再与同段产品 `[AndroidPoolLifecycle] native InitPools returned` 证据联合核对。probe UNKNOWN/提前结束、日志缺失/截断、归属不明时不能用READY或旧日志补成通过。
- 可直接复核已有原始日志和既有facts，不要求新增解析器、addon、扫描或另一套结果账本。产品日志来自 `android/PatchAndroid_ProfessionPoolLifecycle.cs:22–42` 的产品postfix，复用它是合理的；count及注册/银行错误原样列出，不推断九池、完整兼容或保存/玩法通过。
- 后续最小host/结果核对覆盖至少：完整采到NRE仍不是加载成功；READY缺产品return日志为unknown；错Main归属不能通过；正常配对证据才给限定启动期结论。不要求为本轮方案审查运行这些测试。

## 已接受的方案部分及未验边界

- 609e实际DLL SHA `609efed2b058d398f3333c06d39d2c8318a72bbc827f0b68fa781927932b74f8`、16384B；源码/工程不引入Harmony或Main引用，观察器catch局限自身回调读取。它不新增Pool/Boat detour，但这不等于完全无观察扰动或原生异常传播已验收。
- 新case/新有效window、DSH真实身份、maximumCases=1、自有AVD与临时overlay、真实wait/reap及保护收口的方向接受；当前没有批准新的执行参数或已经准备好的gate。
- 未执行controller/register/packet builder、host测试、构建、SSH mutation、ADB/Native、合并或安装。未改变产品源码或候选/夹具。只新增本审查结果文件。
- 本轮核对了请求、旧controller/register/packet读回与launch、609e源/receipt、产品日志产生处、相关历史窄证据；辅助只读核对与本人复核的两项发现一致。旧0.0.6实际结果及其false/unknown不改写。

## 增量复审方式

只修改方案中R1/R2涉及的说明并提供准确新HEAD/请求hash；无关已接受内容及有效冻结证据直接引用。随后按原计划提交工具派生的实际差异、source review/host证据，再处理新的受管门与单次运行。不要把本条方案反馈当成额外逐步骤审批或要求重写整个controller。
