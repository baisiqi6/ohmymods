# #272 详情视觉增量（2026-10-20）

Source-only，基于 `d886565fbe2e4beb1df4eadd4fe05401ba70d3cf`；PR #273 仍 Draft，未合并、未安装。

详情替换岸线后，船/涟漪仍继承模板位置；在自有隐藏clone发布前初始化整个船子树，保持原x、尺寸、scale/rotation，在真实岸线框北侧留4UI水距。所需资源未读取到且未写时保留原生fallback；未知写后仅接受已读回恢复或回滚销毁。原生0..9、overview船位和保存/授予链不改。具体动画是否改写根transform仍需Native，不能由host推定。

两PNG只改RGB，source alpha逐字节一致；使用原共享nearest分桶，A/B分别映到原生189崖/227平台/245浅岸/255高光，主档均227。真实Prepare的228×84、bbox/aspect、alpha、Outline、Mask、PlacementMask完全相同。新源SHA：A `44c7463fed7e6cb222dfae8c701e492f000b26a21b7d37541fdc76fa994b1479`；B `a056d1a3ff81ea13c5d9e065abc71d734e45226df2829184e1bd183606a79357`。B placement黄金hash保持不变，仅更新受RGB影响的资源/shore黄金。

缺剪影根因未定：出航前私有旧档只有9项地图记录，能解释未访问时空白；但用户记得截图发生在首次成功登岛后，且A30实际已有land11/count8构建日志，不能据前者给本图定因。产品仅在原数据读取处产生status，并用普通事件固定key记录数据/holder状态；无新tick扫描、强制显示、历史回填或解锁变更。完整可见性/裁切/层序由A33私有诊断另取证，不在本回执宣称已解决。

## 验证与审查

- 两worker实际模型均 `deepseek/deepseek-flash` / max，公开model_change事件resolvedModelIsFallback=false；命中北京时间工作日20点后的其余时段。
- 阶段决策 `zhipu-coding-plan/glm-5.3` / max，无fallback。首个有工具审查12分钟到时无verdict，恢复同session收敛后修正通过；没有把超时当批准。
- 独立builtin Reviewer初审5项P2，R2全关闭：恢复readback、native wrapper身份、实际读取status/Unknown、完整写前catch、逐节点finite；删去重复的产品focus/Canvas/Mask诊断。
- source-linked Variant 280/0、接线360/0；材质variant96/0、shore101/0。host依赖managed doubles/兼容桥，不是完整producer创建或Unity执行证明。
- RuntimeProbe1040 checks仍39失败，与改前基线逐行同集合，不称全绿。未变化的15个字典方法/既有故障证据按源码差异复用，未重跑保存链矩阵。
- Root真实冻结interop私有wrapper：275 Compile、161 references、38 assets，net6/net8均0W0E；NoDeploy=true、BepInExPluginsPath空、禁证书、输出/CLI/NuGet/TMP私有。Root初次runner的MSBuild属性时机错误已纠正并保留失败日志，不是产品错误。
- R2代码binary diff SHA `b40f8eb368ad96992e0695f47e7078993d7186d62a568a21882980d13c060eb3`；MapMountIcons SHA `3a996a449a6180f4773368ae679f5045d30841e7e6ba7d3df42aa14c7d3097bc`。
- Root输出net6 SHA `b83c411040fe4ceabde16ec68959953797ba75f3792fc0c2feb6ab67f6b183d0`；net8 `242389d3d7b56fd4b741ab34d605761745c9958d7e1596f7c683f1b365e46457`。仅编译，不是已装DLL或Native候选。

私有raw locator：`.local/tasks/map-detail-visual-20261020/`；`palette/out/palette-receipt.json`、`palette/logs/`、`source/*-r2.log`、`root-build/product-*-r2.json/log`、`review-session/`及`worker-session/`公开模型事件。PNG原图、原始玩家数据/日志与interop不提交。复跑入口仍为现有 `tests/map-island-art-variants/Tests.csproj`、`tests/native-map-shore-art/ShoreArtTests.csproj`、`tests/native-map-runtime/VariantProbe.csproj`、`tests/mount-island-map/Tests.csproj`，构建使用任务私有bin/obj。

实际native pointer别名、Unity ImageConversion、船/涟漪动画、详情剪影渲染未由本轮静态证据验收；Windows2.4.3、Android设备和联机不外推。A33仍用旧H候选只诊断剪影，不混入此视觉增量；新Source/Native身份与审查分别记录。
