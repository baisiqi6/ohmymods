# v9.0.0 独立发布物审核

结论：PASS，无发布阻断；可按用户已授权范围创建新 v9.0.0 标签、上传草稿并核远端摘要后公开。审核不等于已上传或已发布，远端 tag/资产/Latest 读回仍由 root 完成。

审核范围仅为此次发布的源码选集、构建等价性、ZIP 和发布说明；未实施或重试此前被拒的 Hero/Crossbow 诊断，不重新给旧火枪完整身份存档流程出具通过结论。

独立读取验证：

- 精确提交 `21f7ffa136d8c690993bca38c97e954c00c7fb10`，父提交为公开 v8 的 `dd5c86fccb132be65aa9fca204d8cd3be1035121`，clean checkout 无修改。
- 546 条源码选集覆盖 173 条实际变更；新增选集没有内部任务记录、个人原生/附加档、配置、日志、原生 DLL、游戏源码或被排除的 hero-gait / hero-save-recovery / hero-save-fingerprint。隐私扫描中的 global-v35 命中为用户指南标准路径和合成测试标签；HeroRecruitment 保留 223 个合成断言，未引用私人 fixture 输入。既有公开父提交内容不扩查。
- 对照 local-baseline.json，canonical HEAD、branch、index SHA 保持；546 个选定原工作树文件的 SHA 与选集冻结记录全部相符。
- ZIP `43702cb6be45a8e7bb92b6b87747f265f2a18c881e2f14e8c59bf19acac1c6c2`，39,785,588 bytes、313 项。独立 CRC 全通过，无大小写重复和路径穿越；精确条目白名单匹配。
- 公开 v8 ZIP 已先核 SHA `1095ceb2dbc587fc60366835e7b7c360b87f3408a6005184f5325c446d78db8f`。全部 306 项引导/运行时依赖的路径集合与内容均逐字节相同。
- 包内 DLL 与 clean 构建 DLL 逐字节一致：`641e79a938b16ee5a64962225f5bf36181a38760dd4931dd92383ad11756d7ed`，1,208,832 bytes。BUILD-MANIFEST 的 9.0.0、GitCommit、DllSHA256、DllSize 全部对应实际产物。五份包内说明逐字节匹配所选源码。
- 独立重新使用 Cecil 对比候选 `135825E1F62652B1538DA3803C8AA923B4B0358449018FE3AAEAB176620E17C8`：3587 个方法体不变；唯一改变为 Plugin.Init 的两条版本/build 字符串和插值字符串长度常量（71→32）。方法元数据、InitLocals、MaxStack、locals、异常处理器含 FilterStart 均保持，程序集引用保持。8 个内嵌资源的集合与字节完全相同。Assembly 为 9.0.0.0，BepInPlugin 为 9.0.0。
- 读取 clean-build 与 test-results 执行证据：实际接口构建 0 warnings / 0 errors；91 项 exit=0，分为72个可执行回归、4个真正xUnit项目（89+73+146+85=393/393）、15个接口编译。Reviewer 未重新运行测试，未将编译当作游戏实测。

说明与 `github-body.md` 已核：手动4币/自动金库8币、希腊单机补货、英雄和火枪仅单机与默认关闭、未知身份不按零采购等边界一致。五份说明及 GitHub 文案均保留英雄走跑切换、乞丐穿地、个别弩手缩放未解决；人口日志为诊断；18人静态匹配不代表真实重载、跨岛或完整存档流程已验证；卡顿改善、视觉、长期稳定性和联机待验均未扩写为已通过。

Reviewer 全程仅读取源码、日志、Git 状态、ZIP 和 DLL；按 root 追加授权只新写本审核证据。未改生产/测试/玩家说明，未操作游戏、用户数据、Git提交/标签或上传发布。
