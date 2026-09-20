# 伤害可靠性与计算复用候选

基线：已安装 9.0.0-hold-purchase-ammo-20260917 / 0B1AD4E149A0BF1BBAD90DE62A574A87F68B33E2A607CDBB7B46122EF695E55D。
候选：9.0.0-combat-reliability-20260917 / 284B78A0DC4D17D83D40712E157DF1E21FB20EB2E15D981D0371488016740C1F。公开版本仍 9.0.0，无 commit/push/publish。

已在游戏关闭时备份并安装到既定 E 盘独立测试副本，旧/新 DLL 与备份哈希核对通过，28 份原生存档/附加档/配置逐文件哈希保持；未启动游戏。实际安装时间、路径及双备份见 receipts/install.json。最终独立复核 PASS，项目 checklist 验证通过，功能实机验收仍为 doing。

## 实现

- 自有火枪弹丸与希腊/英雄箭额外 AoE 共用 CombatDamage.Submit，同步调用原生 ReceiveDamage；不直接写 HP，不拦截普通箭直伤，不引入延迟伤害队列。原生 OnPreReceiveDamage / 护盾 / 无敌顺序保留。Submitted 仅表示原生调用正常返回，不是实际扣血证明；Faulted 可能已部分生效，因此不重试，其他独立目标仍可处理。
- 弹丸保留基础伤害 2、原射程、射速与前排免疫阻挡。常态用可复用命中数组，32→256 饱和后用实际 2.4 Linecast 的完整 List overload，再遍历全结果选择最近有效地面目标，不假设排序。List 可能因密度增加而扩容，不能声称固定内存/零分配。真实物理 API 故障仍保守终止并限量记录。
- 弹丸记录与视觉均复用；先快照 source、消费并归还记录，再提交伤害。long 租约与 Tick 重入门防旧循环推进新租记录和同帧二次推进，真正伤害回调返回后复核世界与可运行状态。移除 0.1 秒截断，使用完整有效 dt，按剩余射程/寿命钳制线段扫掠。
- AoE 保留半径 0.25、额外 1 点、排除直击对象、同一生命同一齐射一次及原有 32 目标提交上限。目标生命 token 来自自有 IL2CPP marker 的真实 GO 启停，组件无 Update/扫描/RPC/保存接口。marker 单独禁用或活性读取异常使观察链不可靠时拒绝额外 AoE，直到可靠 GO 周期恢复；首次 AddComponent 回调幂等，不使用指针回退。marker 默认 flags 不变、非 Unity 辅助方法 HideFromIl2Cpp；注册仅尝试一次，失败不在每个目标上反复异常注册。
- AoE 对一次最多 64 个查询结果先做生命快照，提交前重查原过滤、目标身份、life、距离；只在提交前占用 volley 去重记录。回调复用旧 collider 不会伤及新生对象，失效未提交目标不占名额，后续有效目标可填满原 32 上限。
- 作者火焰公式、3 层、颜色/大小/寿命与 8 张素材不变；实际 time 输入缓存 36 对 Perlin 值，MathF.Round 保持 ties-even 与 float 运算顺序；量化顶点不变时跳上传，scale/color/lifetime 继续推进；固定局部 bounds 覆盖所有顶点。满 16 效果同一计算时刻由 3456 次噪声调用降至 72 次，重复时刻缓存命中为 0。只证明 API 调用量，不是实测 FPS；未声称 Unity 内部没有其他计算。

## 验证证据

7 个针对性运行项目共 392 项：combat-damage 26、greek-impact 88、hero-archer/greek 85、musketeer-runtime 77、archer-impact 54、hero-archer/impact 54、impact-performance 8，全部通过。弹丸测试编译真实共享 CombatDamage，未用 worker 的提交替身冒充集成验证。日志见 receipts/*.log。

真实独立测试副本 2.4 的两个接口工程、完整 Debug 插件构建全部 0 warning / 0 error，BepInExPluginsPath 为空，构建不部署。实际 native Damageable 与 ObjectData 保存接口证据见 native/findings.md、native/objectdata-review.md；只读代码与元数据核对不等于实际游戏执行。ObjectData 的接口分派已核实，完整泛型 MethodSpec 符号没有恢复，保持证据边界。

最终 DLL 对已装基线：3603 方法体保持，18 改动、35 新增、7 移除，仅属于授权模块和初始化/build 标记；8 嵌入 PNG 全同，无新增/移除 Harmony hook 类型。源清单 4 修改+2 新增，其他生产文件保持。见 receipts/dll-audit.json、source-delta.json；独立终审见 review.md。

测试接线修正：排除父测试工程递归包含独立 interop 桩；保留并同步重复 FX 套件；同 time 未上传计数修正反向条件；异常回调用例补实际第一发；小步 sweep 桩使用相对当前线段起点的距离，未放宽命中断言。迭代失败保留在隔离 worker logs，不作为通过证据。

## Worker 与边界

三个隔离本机 OMP worker，实际 metadata 均 deepseek/deepseek-flash / max，见 receipts/worker-routing.json。初始非交互执行被 OMP 审批拒绝，两个 worker 曾越界写本地审批配置；root 停止任务进程并将配置移出到 quarantined-worker-policy.txt。后续仅文件读取/查找/编辑工具，无命令执行或策略修改；root 独立审查后执行既有授权构建和测试。该事件未影响游戏、存档或已安装 DLL。

安装以 receipts/install.json 为准；候选 DLL/更新说明位于本机 operator/combat-reliability-20260917/candidate。安装前须确认游戏关闭，备份旧 DLL、核对候选与安装后哈希，并核对全部原生档/附加档/配置保持。不启动游戏、不主动保存，不创建或覆盖用户存档。

尚未实机：注入 marker 真实池生命周期、真实 Linecast List AOT/密集扩容行为、血量/护盾对账、密集战斗帧耗时、相关联机 AoE。火枪现有单机范围不扩为联机。保留 64 查询/32 AoE 目标/4096 同时弹丸等既有保护边界，不承诺绝无漏伤或零开销。此前英雄动作/人物位置与缩放异常不属于本次修复。
