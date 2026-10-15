# Issue #206 — 字体与保存/盾卫分配优化：跨平台兼容后合并收尾

## 现阶段身份

此项此前未在canonical/mirror登记；本轮正常首次登记现阶段源码集成/合并收尾，不伪造此前工作流事件。GitHub Issue已存在。owner同Main，session=codex-consolidation-206-20261015，branch=codex/font-performance-20261010，worktree=/Users/Admin/projects/WorkTrees/ohmymods-font-performance-20261010。

## 本项范围

PR207 head535870b0453b925bda69a6ab8e90073d44e652f4 已实装；依赖202，202merge后retarget到release。保持静态字体BestFit产生处修复、每次fresh Harmony登记查询、PC Native UTF16键比较与hash复制减少。
必要最小compat：Android显式Compile只按真实依赖加入PatchPerf_CanonicalHashBuffer.cs与PatchPerf_HeavyShieldHookQuery.cs；HeavyShieldPersistence.IsNativeKeyCurrent末比较通过ANDROID编译条件保留原managed prefs.contents[Key]==owner.ExpectedRaw，其余所有owner/global/prefs/generation/ContainsKey守卫共享，PC继续NativeKeyReader。Android不编/绑定这次未验证的native reader，不加假reader或强制unsupported/新catch。两个新musketeer legacy host及其他实际受影响consumer补helper Compile，原oracle保持。精确pin仅更新审批后的本任务变更项，其余保持。
必要验证：font policy/regression、save hash三consumer、NativeReader生命周期/失败/线程、Harmony正常与diag；实际HeavyShieldPersistence Android末调用保持managed的静态/host证明；全Android D/R与完整Adapter；新legacy测试及当前保存/盾卫消费者；全源net8/net6禁部署。旧fixture失败按baseline对照保留。

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
