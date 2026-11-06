# #294 — 11.0.0 Mac ARM64 发布批次

用户指定一次性 11.0.0 里程碑，并选择本批只正式发布 Mac ARM64。后续按 VERSIONING 的整批最高变更类型递增。此前历史编号与原审查材料保留。

来源：已安装 Mac default-net6 DLL `055368a3964d307b9a3155160d6a2036cd554f8765c7ea79bdac198b6e59fcaf` 的623输入与 `fa54574426a528f135e79b2f9d9bf4c1c9a80e15` / reviewed `c7579228` 全部匹配；不从集成HEAD借源码。发布源码提交 `e94342e38c922f593b6152be3b43bec0d9e3cf5a`，SDK8.0.425 / net6 Release，161精确引用、275资源、DedicatedR7V1保持。最终tag的il2cpp tree须与该源码提交一致。

本批仅版本/出处元数据整理，无未安装玩法候选：两行版本戳；三个JSON中18个作者路径prefix改相对引用；闭合palette→registry→approval→index哈希链与唯一bootstrap常量。精确5资源变270同（267 PNG、layout、actors manifest与MD不变）；原审查日期/状态保留，releaseMetadataRevision另记摘要重算。初版raw `6f1dd87f...` 因资源内个人路径被原sanitizer拒绝，未发布；由新源码重建替换，不复用该产物验收。

- 最终raw DLL：`73e76e2886cc37b82826996fac2e8a9e47119623c930cce3d5434b69f9144982`。
- 发行DLL：`8b47537731c0c166ac22634729f73a29d4f7cde0733ed7105015f0561d380b85`，9,100,288 bytes。
- ZIP：`OhMyMods-Mac-ARM64-v11.0.0.zip`，SHA256 `706363ab3e27be946b1f147e1d16a2c5af4489726a32359914925ba27fd19370`。两次字节相同；281条目/278payload/280SUMS，230 runtime仅Mod改变。

验证：旧0553→control全程序集1,298,970条记录零语义差，1411byte差均解释为PE/PDB布局；control→初11仅11精确版本差；初11→最终raw仅9精确出处/hash/SourceRevision记录差，unexpected0；raw→发行DLL零语义/资源/MVID差，145bytes仅RSDS字段。实际18pointer/哈希链独审、source623/refs161清单与公开wrapper等价核验。267 PNG逐字节同，无新增玩法源码。

0W0E；Capital真实源链接885通过；authoring validator86assets/91hashes/358frames/452placements通过。sanitizer31=30pass/1可选历史四DLL整合skip，原CLI接受新pin且不放宽guard；新锁表三负测拒绝且零产物；最终解压launcher --check-only exit0且零包写入，正常受保护274路径hash不变。未启动游戏、未安装正常版、未写玩家存档/Steam。

既有测试限制：production-source套件修改前后均build0/run-6、FAIL layout hash，原因是R1 ff18 pin落后已安装R2 b6f7；只同步受本次影响RegistryHash，不修改布局断言来假绿，不能说全测试通过。Capital885已覆盖当前production parser和完整性链。

审查：实现准备、元数据方案及实际18/semantic-digest增量均由公开事件核实GLM5.3、请求max；worker实际DeepSeek Flash、请求max，周日路由；独立源码/资源/打包审查与发布/tag关口另留精确产物回执。source与rawpin分两commit交付，避免把含rawSHA的commit反向嵌入自身raw形成循环；最终il2cpp tree相同是硬门。

下载/实际发布状态与最终tag SHA见 [v11.0.0 Release](https://github.com/baisiqi6/ohmymods/releases/tag/v11.0.0) 的RELEASE_MANIFEST.json。本批不提供Windows/Android资产；未装#250/#276/#282/#284/#287/#292/#293等不混入。新包未新增Native验收，同源既有玩法/宫廷诊断实机边界如包内VALIDATION；旧岛盾卫购买、技能池准备日志、第二岛/四新世界/联机待验保持独立后续任务。
