# 正常 Mac 岛11登记恢复：副本证据与剩余门（2026-10-24）

用户要求副本先验购买、换岛、保存冷重进，之后才合并安装；正常数据先不动。用户明确该岛从未成功购买盾卫。

- W追上最新主线c1e9e5c，合入任务分支e21dd2e；只解决progress顶部追加冲突并保留双方记录。新base商店付款/适配372检查、盾卫运行1144检查通过；它们不代替Native。
- 正常global `611d67a2…93cedd0`、正常DLL `055368a3…59fcaf`与17份原始数据副本完整核对。首次Native诊断确认原生Load成功而mod `missing-current-row→paid-proof-failed→Unknown`，本岛claims/pending/completed均0，共享三权益在；普通Save仍无11行。共享付费不代表本岛买过兵。
- 精准一次性copy-only修复：仅改解压JSON中HeavyShield的唯一val token，新增slot0/challenge0/land11/HashKind2/claims[]；生产算法从原岛raw重算 `930c6b83…e6a64cf8`，与Native读数精确相同。全部13岛raw、其它prefs、两个campaign既有字段、三收据和0/4/8四条责任保持；原17文件不改，不移植8兵、不猜ID/退款，不泛化paid缺行放行。
- 新global `b6755d2ede76b0373bb0ff47e48ab79238f107658250fbcc81f1f99d1a1a9a9d`、新primary musk pair `6e0fe4c444788a36a302727625614442874d1e7c65c6bae07d587383f3c27df5`。pair绑定整gzip，已由生产TryExtract/PairedBackup.Save从新实包重建；正确pair恢复通过、新旧交叉拒绝，Musk原key/archive不变，历史bak不改。工具60项检查/17负例及输出祖先symlink防护通过；Root另验prefix/suffix字节及整体JSON唯一差异。
- 私有QA `fe5c75f2…98b21b`在首修复case `issue286-normal11-repair-20261024-01` 实际Load Exact，EndLoad Unknown=0/QuotaUnknown=0。UI自然SaveQuit，controller0/errors[]；11行真实保存更新hash为 `4b5d0eb6…ee94e63d`，Musk pair精确对应实际落盘。
- cold-01握手KERN_PROCARGS2读取EIO5触发保守终止，未取得Unity/读档证据；原日志保留、私人输入无变化、新凭据撤销，不称游戏崩溃。按完整新case有界重试cold-02成功取得握手/OS身份，精确冷载该真实postimage（没有重播repair seed），Unknown/QuotaUnknown仍0，UI退出0/errors[]，登记保存与冷重进已验。
- **购买尚未验收**：两次正常11场景均无盾店ready，新增只读Create状态确认停在 `find-position / 领地中段暂时没有盾具店空位`。身份已恢复不代表有位建店；正在仅采已有Find/Collect实际边界/结果，不先改排他/搜索或凭Tower AABB定因。付款、工具领取、转职、购后冷载和11↔8仍未完成。
- Tower可视merged AABB跨原点与店铺Find不是同一输入，已有塔基回收影响另记；未据此修改Tower。PR287的generation slot源修复已有host证据，但本例历史首次漏写的完整原生时序仍缺；未冒称额外坐骑岛首次生成桥已Native验。
- 本机原始数据/日志/身份只在 `.local/tasks/heavy-shield-shop-20261024/normal-purchase-investigation/`；副本修复、原始day40、诊断day41及真实冷载postimage分别保留，不将QA进度当作正常结果。各次全normal6对象/71game检查通过、批准撤销；未改正常数据/DLL/Steam，未合并安装，PR287/#252/#286继续开放。

实际OMP模型事件：DeepSeek Flash/max实现、GLM5.3/max集中阶段审；独立增量及Root接受另留本机回执。已变化证据按实际范围复核，不重跑未变化的626项身份检查。正常2文件数据回写与安装仍须在完整验收后安排具体收尾窗口。

## 2026-10-25 离线对齐：后续选址取证与暂停检查点

- `placement-01` 和 `boundary-01` 均正常UI保存退出，controller returncode0/errors[]/实际OS身份通过；后者运行569.97s，未超时杀进程。原始审批与失败中间体各自保留，所有单次凭据撤销、桌面焦点已释放。
- 当前合法中心域[-23.55,178.86]、半宽2；两轮各504次原生fits全部false。补测同一Collect生成的166个内部临界点，均false，trunc/fault/bad均0；全正距集合clip覆盖长度等于202.41域长。只证明这些实际快照没有被原生接受的候选，不能说未来任何状态无位置，不能以保守集合覆盖单独证明原生类型过滤。
- 已证实Find确有内部相切点漏检的独立host反例，但本岛166点全部被拒，不能把该缺口当作本次NoSpace主因；独立Issue #290处理，不混入PR287或放宽排除半径。
- 未完成树/墙付款、盾具购买、领取、转职或换岛；自动移动未能稳定停到墙基，已留真实私有检查点，等用户有空配合清场/扩墙后继续。登记Exact/Unknown0、多次保存回读保持；新保存的Musk pair精确绑定实际global，盾文档仅本岛snapshotHash推进，岛8四责及其它岛行/共享权益保持。
- 日志不是零Error：五轮10条Error逐行Counter完全相同。四条CrossWorldMount错误已用原生日志与实际2.4资源定位为映射前置缺失，另立Issue #291；五条MountIslandFresh主要为冷读无departure观察分类，其中unsupported状态仍需保留未知；Harmony stacktrace权限错误独立，未取得具体失败调用。不能用controller errors[]冒充产品日志全绿。
- 私有游玩已推进天数、部分非盾单位发生自然战斗变化，检查点只用于QA，绝不回填正常。原day40修复候选与完整原始备份另行保留。正常18份运行/数据文件本轮离线hash再核一致；未合并/安装，#252/#286及PR287继续开放。
