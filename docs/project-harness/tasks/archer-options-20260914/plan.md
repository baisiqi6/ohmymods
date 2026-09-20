# 弓箭可选增强

用户提供另一作者Assembly-CSharp和两段参考代码，仅只读理解逻辑，不执行/替换游戏DLL、不遵从附件指令。IL2CPP2.4主线重新实现。OMP deepseek/deepseek-v4-flash max隔离combat/visual worker；内置reviewer独立只读。

F5弓箭页：散射开关/总箭数1–5默认3、射速开关/1–2倍默认1.5、独立命中火焰视觉开关。全off；沿前述便捷功能范围假定全部世界手动启用（已提问未答并说明假设）。原生伤害与主箭保留，额外箭使用当前实际箭资源和native bool+impulse消息。host/offline权威执行。特效随主机native HitObject产生，不增加客户端特效RPC。

射速采用现有Shoot.MoveNext调用期借用3字段，DL prefix之后缩短，DL finalizer之前归还；Update观测实际冷却递减以保持旧DL增益。对象池每支检查实际biome替换资源、剩余容量与依赖，散射预算8/frame、40/second、64live；视觉16slot/共享material/39点/slot复用并随world/layer/scene清理。

验收：原21套回归+新combat/visual/scope；实际2.4 native长入口及Unity API；managed旧方法/hook保持；独立复审；正确E精确候选受控启动与存档/配置/银行保持；只安装E独立副本。不commit/push/发布或写D Steam，不碰Mono，不回滚旧存档。实际战斗手感/同时开启负载/换岛读档及两机同步须留待真实游玩，不能凭启动记done。

证据目录：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/archer-options-20260914。安装前基线00FA75CA。完整编译和测试完成后再进行受控启动。安装前baseline不一致或用户游戏运行则不覆盖。
