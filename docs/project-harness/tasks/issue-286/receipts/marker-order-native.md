# 六个小盾标记：私有单变量对照（2026-10-24）

用户红框已确认为 `extra_seat_shield`（屋檐四枚）和 `purchase_shield`（柜前两枚），不是原生付款圆点。原图白十字仍视为无关截图技能。

- A 为已归档 `b5161fe4…c7410`；B 全新从同345项source/相同Probe构建，仅 `HeavyShieldShopShell.CreatePoint` 的 Greek marker `front+1` 改为 `front`，非Greek保留+1。材质、z、位置、PNG、支付及身份源码均不改。B只在本机私有副本，不包含PR287生成身份修复。
- B DLL SHA256 `6212a955af9c4c2119fac9d13671fd6ca65db1b0cf6287659c8501bbb91e3f4f`；NoDeploy 0W0E，275项资源实际PE回读与A一致。Root逐源核对只有一行差异，未重跑无变化的626项身份检查。
- 一次性replay准备增量独立审查通过，8项负例覆盖过期decision、错误产品/检查点、额外post文件、写批准后故障及外来批准不误删；实际模型留证为OMP DeepSeek Flash/max（实现）和GLM5.3/max（阶段决策），无fallback。
- Native case `issue286-marker-b-20261024-01`：现有私有ARM64环境重放同17文件检查点，先归档A后像，使用新精确闭包/900秒单次凭据。正常数据全6对象与71物理游戏文件保持，未恢复/安装正常版。
- 实际日志有远/近两组共12项marker：均active/enabled/order0、z1.499、共享stencil8/0/1；店身/state/merchant排序和材质保持。远离图六标记可见，两个朝向近身图未见超出人物实体轮廓的额外大圆弧，也未见柜台/店员新增遮挡。
- **不接受“图层修复已通过”的结论**：A普通接近也未稳定复现额外裁切，B看起来正常不足以证明改这一行解决旧故障。实际MPB/实例pass与原异常仍未绑定；不改共享材质，不新增每帧重排，默认源码未采纳B。
- UI自然保存退出：returncode0/errors[]/signal0、242.2秒，实际OS身份与终态保护通过；私有后像22项归档，新批准已精确撤回，无剩余游戏进程。既有Harmony权限和MountIslandFresh/CrossWorldMount报错原样保留，不宣称全日志零Error。
- 当前隔离source8盾卫 `左2/2、右2/2`、Unknown=false、quotaUnknown=false、economyFault=false、无待领Bow，因此该窗口不再卖盾是满席限制，**不是正常land11缺行问题的复现/验收**。
- 正常档仍是已访问land11且缺盾卫行；PR287仅防以后首次生成错绑，不能自动回填它。当前快照无法证明该岛历史零付款责任，不能猜配NativeId、补空行或清Unknown放款。仍需同岛权威责任链或另行核准的明确迁移契约；实际用户拒绝分支仍待目标副本验证。

本机证据集中在 `.local/tasks/heavy-shield-shop-20261024/marker-order/`：`native-outcome.json`、`native-render-samples.txt`、三张原始截图、private postimage、prepare/archive与当前模型verdict。原始存档/日志/身份不入Git。#285/#286及PR287保持未验边界，未合并/安装图层候选。
