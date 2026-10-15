# Issue #199 — 储存钻石HUD：当前主线兼容与源码收尾

## 现阶段身份

此项此前未在canonical/mirror登记；本轮正常首次登记现阶段源码集成/合并收尾，不伪造此前工作流事件。GitHub Issue已存在。owner同Main，session=codex-consolidation-199-20261015，branch=codex/hud-gem-storage-20261009，worktree=/Users/Admin/projects/WorkTrees/ohmymods-hud-gem-storage-20261009。

## 本项范围

PR201 head66c222ddf4367a43176e7c16408628f4bdf5097c 已在正常累计包中；本轮合入当前release并收尾。保持已加载GlobalSaveData/currentCampaign只读storedGems口径，钱包不合计、不写保存；PC标题储存钻石。只处理新交集：CalendarHud formatter实际WallEngineerRules纯源的host闭包与重复fixture类型；Android MobileCalendar对共用helper的实际口径/文案消费者核对及此唯一源pin更新。不得把Android仅pin变化当行为验收。
必要验证：CalendarGems与实际HUD源链接；Android当前真实Debug/Release与完整Adapter消费；当前IL2CPP公共全源net8/net6禁部署，旧31设置保持。

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
