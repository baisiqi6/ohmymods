# 出航修复的主线接入（Source，未实机验收）

固定基线 `668cc27697546e49183309b0200ac53b63c71cd3`，由 PR271 交接与 PR273 地图代码本地合并形成；远端 release 未合并。仅迁移已确认的单请求/native flags、departure 历史时间域、curBiomeData 身份、boxed IList LandMapData 写回与对象池代次失效。移除测试桥、追踪器和写审计；未接通既有空岛自动恢复、补发或假解锁。Refs #260，旧空岛恢复范围仍未完成，不能关整个 Issue。

私有 net6/net8 构建均0W0E（281 Compile/161 refs/38 assets），新生产类型纯测试17/0、结果交接22/230；独立 Reviewer 对11个allowlist文件逐字核对、复跑17项，批准Source范围。模型：OMP deepseek/deepseek-flash/max，无fallback；阶段方案审为实际GLM5.3/max，合并冲突另经独审。原始命令、双TFM日志、模型与diff证据保存在本机 `.local/tasks/map-detail-visual-20261020/product-integration/`；正式测试入口 `tests/mount-island-transition/`。

net6 DLL `a2c62489581ee8905d4e9894a496f6b683ea501d21e34cef082c2ccb4852a395`；net8 DLL `94dcc753fb57f8e7a01ffabfdd23798f8f7786d554c1020895d24c7a6dd31aee`。关闭审计后的候选尚未实机，不能继承旧H结果；真实原生 marshalling、departure时序、池重建和最终地图/保存往返须在同一最终候选联合验证。尚未合并release、安装或改正常存档。
