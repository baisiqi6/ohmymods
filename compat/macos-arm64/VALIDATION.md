# ARM64 历史验证摘要

来源：Mac Operator 2026-09-20 实验记录。本次归档不启动游戏。

- Doorstop4.5 + CoreCLR6.0.36原生ARM64启动；最小插件架构自报和进程架构核验一致。
- Mover.Update Hook与注入HUD、未修改v9.4.5 Mod DLL加载、场景及F5面板通过；此前一次240秒运行受控结束。
- thunk误识别的20/20异常目标可由错误x86位移计算解释；架构门禁后的输入拒绝与静态检查通过。
- 五种合成布局（4B跳转、8B mov/ret、8B尾跳、float getter/setter）验证4B入口、原函数/替换函数、邻接字节及Destroy恢复。前三类覆盖近/远替换并在轮次间Destroy；不声称原位retargeting已测。
- 测试专用near分配失败注入验证Prepare失败、清空origin、不改邻接字节且Commit拒绝；不是穷尽真实地址空间压力。
- 最近页选择15项、五布局、原43项native检查通过；两次35秒完整启动，之后人工游玩基础反馈正常。此前near窗口边缘失败仍作为修复背景保留。

原始结果中的 `manual gameplay pending` 是当时快照，后续用户基础反馈不改写历史manifest。最终native库hash见 `evidence/candidate-manifest.json`；本PR不分发该库。Windows、长期性能、切岛/读档/联机及v9.5.12新候选均不由以上结果证明。
