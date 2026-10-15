# Issue #200 — 双坐骑岛与紧凑弯月地图：堆叠PR主线合并收尾

## 现阶段身份

此项此前未在canonical/mirror登记；本轮正常首次登记现阶段源码集成/合并收尾，不伪造此前工作流事件。GitHub Issue已存在。owner同Main，session=codex-consolidation-200-20261015，branch=codex/mount-island-overview-scale-20261009，worktree=/Users/Admin/projects/WorkTrees/ohmymods-mount-island-overview-scale-20261009。

## 本项范围

PR202 headc6f7524d5858974717fbfd033b56877b735b8395 与 PR222 head3482b3edd7b3a475773effb69389d12041c24794 均已在正常累计版。本轮完整收尾两PR；202先merge-commit保留祖先，222在202进release后retarget至release再合，不能只把222合进功能分支便关单。222工作树/branch为/Users/Admin/.codex/worktrees/mount-map-compact-20261013/ohmymods、codex/mount-map-compact-20261013。
保持8+8目录/旧已付设施、普通岛比例/真实详情分页、保持当前完整图形/固定状态包围盒驱动的紧凑下排与已选比例，不设固定缩放百分比，并保留右宽湾弯月素材/锚点/失败回收；仅处理当前主线真实集成交集，未发现必须修改业务算法的证据。
必要验证：split-policy/runtime/fresh/map、cross-world-mounts、variant与missing-B独立产物、shore/compact，保留已知RuntimeProbe all-mode历史不通过及对应baseline对照；全源当前interop编译及39资源保全。

## 本轮共同行为与交付边界

用户2026-10-15明确授权：已实装PR经必要审查验证合并收尾，236/237检查后合并安装正常版，由用户后续实测；新问题另建Issue/PR。源码审查/编译/host、合并、安装、玩家实机各自留证。未发生的native/Windows/联机不记通过，不再把玩家后测当作代码Issue关闭前置。所有本任务PR进release/v9.5.13且必要代码验证/独审通过后正常关GitHub Issue与受管代码任务。

初始release基准5d931e0fa1700879e41620cf8d48cf2719012bc8；因依赖PR按序合并，每次操作前冻结真实head/base及差异，新增交集重新验证，不把旧绿色结果外推。产品/测试修改仅本任务必要hunk，其他来源/私有累计内容保留。阶段infra phase=ready，owner=mac-codex-ohmymods-main-operator；normal CLI管理task/plan/assignment/closeout，保留foreign rows/order/plans/leases，不裸改JSON/DB或force。原历史状态/失败/验收边界保留，当前记录明确是首次登记或当前阶段修订。

所有构建关闭自动部署(BepInExPluginsPath为空)，真实依赖与source pins留本机。正常Mac安装由issue234的累计候选最终关口统一执行，当前任务不独立覆盖正常DLL；不安装Android、不启动游戏，不改存档/config/prefs/Steam。Android232已交接所列Compile/精准pin/消费兼容hunk供Main，其他产品入口/R诊断/状态不触碰；其native执行不纳入本任务。

## 审查与完成标准

1. 修在错误产生处，状态写入责任清楚；不补fake产品或放宽原断言凑绿。
2. 按实际source/test/define/ref变化执行必要回归及全源接口构建；旧baseline已有失败保留并对照，不冒称全部suite通过。
3. 独立审查所有新集成交集；实质决策按GLM5.3/max，具体merge按最终head/base gate。
4. 正常merge commit保留祖先；整批关联PR未齐不得关本Issue。player/native待验原样附，后续发现另Issue/PR。
5. closeout packet逐任务串行独审/准确摘要/正常receipt消费，doctor/validator通过，释放本任务lease并保全外项。
