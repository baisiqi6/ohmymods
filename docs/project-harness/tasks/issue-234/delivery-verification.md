# Issue234 / PR237 当前完整主线交付验证（2026-10-15）

在已合入PR226的release `59cbec5e8e7670f06f6ab882c715de6e7a91dce4` 上完成盾卫玩法集成：两金币举旗观察完成后选同侧最近1名合格盾卫，站在有效重装长矛兵前方；收旗或上下文失效结束绑定，不在同次旗令自动补人。格挡耐久上限40；旧v1(3)/v2-v3(4)按剩余比例迁移到v4，保留原保存键和fingerprint3、不补付费身份。脱战6秒后到盾具店内铁匠处排队免费维修，工时8秒；威胁、离开工位、迁址/世界/生命变化取消旧工时，未知完成保留屏障。移动与身份提交保持原单一owner。

集成保留201/202/222/207/228/233/236/238以及226库存/照明。Runtime测试工程合并时保留真实HookQuery与RuntimeOwnerSourceRoot可配置Integration路径；Integration fixture补真实三个成员并新增顺序/异常断言。Android显式编入5真实共享helper，但原生Banner能力明确不支持：不注册/编入两个observer，不进入激活/选兵/取队形指令路径，不伪造事务guard安全。PC原body与guard保留。该Android边界经过专门GLM决策及独审，未移植3Formation系统或NativeKeyReader、无新配置/注册。

当前组合实际：公共完整net8/net6 0W0E；Android实际interop Debug/Release各0错误、1个CS0169未使用_nextActivation提示（不隐藏），两真实DLL分别完整Adapter1264/0，metadata各15/0。source-linked：Runtime1140、Integration48、Identity136+228、Save122+30+295、Shop372、Banner279/20场景、Maintenance2152/36场景、ANDROID canary49、MusketeerNativeSave41/producer33，全部通过；10套已记录输入与最终W对应。源码侧平台边界的前/后PC双TFM rawIL/EH/locals/资源零变化与坏样本反例证据已独审。未声称任何新native执行。

本机B=`.local/tasks/release-consolidation-20261015`，当前证据pr237/build、tests、test-input-binding、metadata日志；精确compat补丁与原生边界证据workers/shop-shield-compat/independent-final-review。首次工具选错TFM/旧obj卷入的失败保留并仅纠正调用；未改产品或放宽oracle。候选正常包由cumulative/从现装911b源构成单独构建审查，保留Palace/NativeReader/39资源，不能用这里的公共验证DLL安装。

用户明确要求必要代码审查验证后合并安装正常版，玩家后测新问题另Issue/PR；本轮未启动游戏，密集围攻、真实举旗/修补/存读档、Windows/Android设备仍未实测。v4产生后的回退不能只换旧DLL冒称兼容；安装须留当前软件与进度备份。源码Issue在所需PR合并后正常关闭，安装与玩法证据分别记录，不伪造native批准。
