# 本轮 worker 与 reviewer

用户按北京时间工作时段选择worker的规则沿用AGENTS。午间派发OMP，14:00后新轮次使用内置worker；运行中的OMP轮次自行结束，无强制杀进程。

- perf OMP session `01a0add2-aece-707e-879d-ef49be196887`。
- defense OMP session `01a0add2-d751-75b5-bf90-c695263b20bb`。
- drop OMP session `01a0add2-ffe7-7489-85a0-32d3486d19b0`。
- knight OMP session `01a0addb-f5f6-70e4-a84a-f7b51d147953`。

各native JSONL在operator工作目录 `musketeer-live-fixes-20260917`；安全状态脚本只读取公开模型/session/tool/final事件，未读取私有思考。已核验OMP四任务实际 `deepseek/deepseek-v4-flash`、fallback=false、thinking=max。限read/edit/write和task allowlist，无shell、二次委派、配置/安全设置、Git、部署或用户数据写权限。

北京时间14:00派发 `/root/defense_followup_worker` 与 `/root/knight_context_worker`，默认继承主agent；实现互不重叠，后续复核修订沿用同worker。Worker可运行自己slice测试，不得部署、启动游戏、操作用户档或提交发布。Root统一主构建和安装。

独立内置 `/root/musketeer_reviewer` 仅只读本轮新增slice，逐轮反馈修正，未重试旧任务被阻止的全火铳身份归档终审。该旧缺口保持，不由本轮局部通过掩盖。
