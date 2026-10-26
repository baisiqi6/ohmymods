# Windows .281.3 实际安装回执

本轮用户明确要求重试Windows连接，连上后先备份再安装，由玩家测试出航、保存退出与冷重进。上次SSH未送达的失败窗口保留；本次使用新指纹、epoch、TTL与精确执行审查。

- 正常独立玩家包：`E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.v2.4.3-P2P`；未写Steam、Native、launcher、配置或存档，未创建账户或修改系统执行策略。
- 实装版本：`10.9.39-win243-diag.281.3`；代码修复commit `d3da7c9ee421485cdd46b2f1344141dae9f9bb35`，仍为原Windows冻结基线的四源增量，没有混入其它主线功能。
- 新DLL SHA256 `63d43ff17ff601d1c0f95f26beeafdde21ddffae4e73419d640119c8060f32ae`，4,550,656 bytes；配套清单SHA `2a51b8da0ed29000871dcdb420026c89bffa02e6f5ab98baaad8db7ff6140cf5`，83,870 bytes。
- 安装前完整备份：`E:\ohmymods-backups\issue281\install-2813-retry-20261026T053309Z`。25份原生存档/Mod附档/配置、旧DLL/旧清单及可选日志均已复制并校验；独立postflight证实全部25份正常数据与其备份逐项一致，旧部署备份也匹配。
- 新script SHA `edd9008c7eb7518d496e239050a212b356c4df28195466b60c302eb668d0435b`；wrapperSHA `08fc28610a61e968ec3e77c18a7ec2870deca1339ce47ed0652d0287c8f2b512`；TTL `2026-10-26T06:11:36Z`。39函数及主执行尾部与既有最终模板同体，仅本轮四个常量变化；不复用旧one-shot/TTL/Apply批准。
- 独立准备审查通过、targeted19/19；实际Windows PS5.1.19041.6456 Parser0errors、staged三个文件hash/size匹配。新GLM5.3 max精确执行审通过，公开模型事件provider/model已核。
- 实际Apply exit0，完成时间 `2026-10-26T05:43:00Z`。实际原launcher `-CheckOnly` exit0/版本确认；末核443固定文件保持，独立只读postflight核444项payload、25数据与backup相符、游戏进程0。

启动入口仍为同目录 `ohmymods-win243-preview.cmd`。本轮没有自动启动游戏或恢复/覆盖正常存档。玩家下一步：正常开船至第一坐骑岛，检查生成，保存退出，再用同一入口新进程重进确认仍在岛上。若四选一弹窗再次出现，停在首个弹窗取证；本轮安装通过不等于原生存读档已通过。PR #282仍Draft，Issue #281开放；不合并、不发正式版。

证据根：`transport-contract/install-retry-20261026T053309Z/`，包括fresh preflight、independent-install-review、19项检查、实际PSParser、公开模型事件与新execution verdict、原始执行stdout/stderr、installed-receipt、postflight及operator-acceptance。产品验证按候选、282项冻结源码、4项变更源和38资源哈希独立确认无漂移后复用，见`reconnect-artifact-audit.md`；没有重复不变产品测试。
