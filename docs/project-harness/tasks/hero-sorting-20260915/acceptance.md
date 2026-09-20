# 英雄显示优先级候选验收

2026-09-15 本机 758B5990 / 7.6.5-hero-sorting-20260915：新增公共HeroVisualPriority，英雄身体位于GameLayer参考深度-0.05，双飘带随后0.002且同body排序，使已知普通角色（含Greek z0）重叠时英雄优先；可能盖住场景贴图，UI代码不改。仅改MOD自有表现，不动角色原生位置/碰撞/伤害。失败预检/部分写回滚与整组撤销、非有限/倾斜/shear/非单位z缩放门；未来英雄可复用。29深度+33布料+65生命周期通过，普通与真实2.4构建0W0E、2627旧方法保持/3授权修改、独立review通过。检测游戏已关闭后备份301FF296安装，save/config不变，未启动/提交/发布；真实遮挡及此前未见英雄根因仍待实测，原有特殊动作/联机未完成。

SHA256：`758B5990DFD6B4404E52398CBA02B6F5940421AAE3FA63FF6DF9671B4F08741F`

Worker：ZCode0.16.5，session sess_3b7413ef-2ebb-46dd-8ae1-df6e74905f1e；公开native model事件确认bigmodel/GLM-5.3，配置reasoningLevel=max，运行强度未单独回显。edit模式拒绝Bash，Operator运行测试；没有回落模型。最终worker源码哈希与合入版本匹配，未复制worker的Python镜像测试。29项回归编译真实helper配Unity替身；callsite归还经代码审查、完整真实interop编译，并不代表实机验证。

用户后续明确回复：英雄优先，允许盖住重叠的场景贴图，界面不变；与已安装候选一致。当前特殊动作未完成与此前看不到英雄是不同待验事项，本次不宣称确定根因。
