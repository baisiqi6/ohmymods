# 火枪短促红色枪口喷焰

侧边会话01a0af42-73c3-76b0-9f99-293c80ae69ec转来用户明确委托主会话的需求：现橙黄小闪光改红色火焰束。该ephemeral任务的read_thread不支持历史读取，按明确转交的内容实施，不扩为持续喷火/激光或新增伤害。

发现原有editable像素绘制source build_animation.py与v8部件，当前atlas与原artifacts逐字节一致。沿用户已批准的本地素材工作流做小范围source像素修改：仅Fire36/37两帧枪口ROI，红色喷出与收束，其他64帧/身体/武器/脚点/尺寸/时序保持；不增加运行时效果对象，沿现NotifyShot播放，既有0.9与左右翻转保持，无旧闪光双层叠加。

worker按20点规则本机OMP Flash max，隔离file-only编写可重跑局部绘制/验证/预览脚本；root审查后执行、目视并核全部非目标像素、注册资源/构建及独立review，只集成改动PNG和build标记。基线96566def含刚安装的白天补货；运行时不换DLL，不动存档配置，不提交发布。

用户随后明确要求开火看得更久，约四五帧。最终采用5帧各50ms（原Fire总时长仍.25秒），覆盖点燃/喷出/拉长/收束/余焰，替代上述两帧方案。atlas有效帧67，Fire36..40，之后Reload/Lower/Retreat整体后移1，非Fire图像按映射逐像素保持，尺寸/pivot/.9/所有非Fire时长与FireSeconds保持。只调整MusketeerAnimation中的表/锚点与必要既有测试，不修改游戏射击/伤害/冷却行为。先提供动图预览。
