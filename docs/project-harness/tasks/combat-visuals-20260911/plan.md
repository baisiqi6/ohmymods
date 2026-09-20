# 武士残影与两项实战缺陷修复
用户批准：武士冲刺浅白残影，近/中/远不透明度45%/25%/10%，约0.2秒淡出；前冲和返队共用，人物白光只在实际无敌burst期间。一起修希腊随从未获得火焰附魔、中世纪剑风MissingMethodException。

现已确认：Renderer.sortingLayerName实际C#恢复shim引用不存在ReadOnlySpan.GetPinnableReference，改用native sortingLayerID数字路径。资源实物Norse Archer prefab的_fireArrowAttack=NULL，尽管fireSO资源存在；Greek style随从可能本体Norse而只是换皮，因此当前guard跳过。补实例缺失fireSO，使用当前世界映射且验证现有正确池，双端同处理，不移除安全guard、不更改共享资产或随机注册网络ID。

残影采用独立4个自有SpriteRenderer：3历史pose残影+1跟随本体的白色叠影，复用当前sprite/sharedMaterial及自身MPB _Overlay，原source材质/MPB不动。定额重用，不逐帧Instantiate，不新增全场扫描。MotionLease token控制Begin/End，旧lease不能结束新特效；普通跑步不保持白光。配置关闭、风格变化、失活/换岛清理，尾迹淡出不延长无敌。旧GlowOverlay会抢原生overlay协程且短时变淡，移除该调用。

实施边界：本机ZCode优先visual worker；subagent Greek资产worker、独立API/review、独立managed tests；operator集成与真实依赖审计。IL2CPP/E独立副本；保留当前8BA3448F补员诊断与既有功能，游戏正在运行时不替换DLL、不终止玩家进程。只本地部署，不提交/推送/发布/修改存档或配置。

验收：真实Unity6接口及对象池映射、0W/0E、motion76回归与视觉token/透明度/生命周期/有界对象数测试、Greek原70逻辑+空资源映射案例、独立review、受控启动。渲染外观/主客机/真战斗到期不能仅凭managed测试通过宣称完成。
