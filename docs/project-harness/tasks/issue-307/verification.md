# 发行准备验证（2026-10-30）

用户授权本会话 Windows 修复链正式发行。基线7eed434；源融合提交1d9a1a5（alias/构建）与f91d87b（航行/火枪策略）。最终合并、公开资产与发布状态由后续发布回执单列，本页不预付。

## 范围和出处

Windows源闭包以4543a79的280源为基底：271保持；PR282 d7694e3的7源、已合并dabbf4b的7源；ModPanel三处已玩增量和生成的MyPluginInfo，合计287。正式源码只额外统一插件/生成版本为11.1.0，关闭VOYAGE_SAVE_DIAGNOSTICS；B的DEBUG/TRACE/Optimize=true保持。38嵌入资源与31core+131interop编译引用逐SHA固定。完整包含43core+72Unity库+187runtime+133生成绑定、必要bootstrap/配置/启动器/文档；不含游戏本体、存档、私有配置或PDB。源包自含333项。

| 验证 | 结果与边界 |
|---|---|
| Root 最新全源正式csproj NoDeploy | Mac240、Win240、Win243均0W0E；不是发行闭包Native证明 |
| 独立源码融合审查 | 4alias归一后与原主线逐字同，Palace责任链保留；PR282三方合成一致；APPROVED |
| 独立回归 | runtime137/0、panel37/0、identity728、保存pipeline258 |
| 作者补充主机回归 | diag identity926、legacy507、gems78/0、musk-shop45；未冒充Root重跑 |
| Android独立回归 | 1503/1，仅原有WallEngineerRuntime pin失败，无新增pin漂移；probe1既有warning/0error，无设备验收 |
| Root发行工具回归 | recipe35/35（无skip）、package13/13，wronghash/缺件/路径/输出/SDK/并行PDB负例 |
| Root实际输入verify | 287源码/38资源/162引用，源7014419B、资源2010601B |
| B→已审发行候选fullIL | 7036差异精确分类，unexpected0；6type/40method/76field诊断删除，4真实保存方法剥离观察块后控制流一致；11拒绝夹具全通过 |
| R2及ZIP复建增量 | fullIL0语义差、38资源同；仅MVID/49静态字段RVA布局。sanitize仅清零125个debug字节，MVID/REPRO/业务保持 |
| 已装依赖读回 | 302库+133绑定+bootstrap及3游戏指纹一致；仅只读，游戏0 |
| Windows PS5.1 | Parser0；CheckOnly有效/坏hash内存文件系统fixture正确，不启动游戏，不冒充真实目录运行 |

## 审查与状态

GLM5.3公开模型事件已核实，请求thinking=max，阶段计划/精确闭包/固定生成绑定附录均通过；worker公开事件为DeepSeekFlash，请求max。独立source、recipe/package与fullIL审查分开。早期DebugType=None引入83202差异已弃用；R1工具SDK/PDB等5项问题经R2集中修复，不以失败产物替代发行物。Windows过长EncodedCommand及首次内存fixture自动加载模块问题保留，修正后Parser与正反fixture通过；未借此改产品启动器。

重要任务已通过Coordinate create-files受管入口登记，EXharness validator0warning；本会话没有可用RemoteMCP工具，未冒用本地DB代替生产record/completion。record半边须在有效入口可用时同步，未伪造完成凭据。

本页只记录代码/构建/工具证据。未重构建后启动游戏；已有玩家Windows第一岛出航、生成、剪影、保存与冷重进证据来自原实装链。第二岛、四新世界商店全流程、联机和人口掉帧不因此验收。已见Knight.EmbarkArchers集合变动与Forest容量日志保留后续，不以冷读未重现称修复。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Maintainer.
