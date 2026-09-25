# 燕返第二轮修复候选

状态：修复、回归与独立复审已完成。测试游戏仍在运行，**尚未安装新候选**。

构建：`9.14.24-choreo-settle-20260925`  
MD5：`DED188D74A40D4F5A17C6E7A2CAE1FF1`  
SHA256：`A94D0D509707C0E8D769A5BDF2BF5EDF6916848600CBCE092D51FA1C3A95F703`

## 行为调整

- **收势**：每腿重放经过实际控制器验证的 `PowerSlash`；结束时发出它唯一的退出指令 `Land`，再由游戏接回待机/行走。删除已被零时长切换证伪的状态猜测和延迟自愈。取消、同帧启动失败和更换动画控制器均有专门清理测试。
- **距离**：出程目标固定为出发点 ±7 格，回程目标固定为同一次出发点。改为每帧重申目标，并用有方向的越点判断避免大帧间隔漏判。实际阻挡仍受每腿 1.2 秒与总计 3 秒的保护，超时明确记录，不假装完成。
- **取消额外突进**：删除独立的“追随从回撤冲刺”及其重试阶梯；脱队只以普通速度归队，不再混入两段燕返之外的突进。
- **去掉连续白拖尾**：删除本 MOD 对 TrailRenderer 的启用和寿命修改，保留用户已经确认可见的八槽、两秒白色定格残影。

本次现场日志可核对的 8 段出程为 6.87～7.02 格，符合 7 格目标与 0.25 格到点容差；还记录了 1 次独立追随从突进。旧日志没有每次回程终点，不能认定所有远退都来自同一原因。新异常日志增加 `home/outGoal/turnX/endX/elapsed`。

## 验证

| 套件 | 通过 / 失败 |
|---|---:|
| samurai-motion | 189 / 0 |
| samurai-visuals | 31 / 0 |
| samurai-night-formation | 24 / 0 |
| archer-night-band | 32 / 0 |
| samurai-retreat | 43 / 0 |
| samurai-diagnostics | 9 / 0 |

合计 **328 / 0**；实际 IL2CPP 构建 **0 警告、0 错误**。原有回撤突进和猜测姿态的测试按功能删除迁移，安全断言保留在燕返或普通归队上。

去掉 Land、恢复 0.3 秒重申、恢复旧到点判断，相关测试均先红后绿；动画替换与同帧延迟播放的四项边界也经过红绿验证。GLM 5.3/max 最终复审 **APPROVE**。

## 安装与待验

需要先退出 E 盘测试副本，再备份和替换 DLL。当前安装仍是用户已确认往返和白影有效的 `8E994A98`；它已另存到任务 `live-feedback/receipts/previous-8E994A98.dll.bak`。

新候选位置：
`C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix/il2cpp/bin/Debug/KingdomEnhancedMod.dll`

退出后只替换既定 E 盘测试副本，不写 Steam 正式目录；安装前后核对 DLL、存档和配置指纹。未 commit/push。

下一轮实机重点：回到起点后正常收势、没有额外追随突进、没有连续白拖尾，白色定格残影仍可见。联机、池化复生和异常日志的新终点字段尚未实测。

## 恢复入口

工作树：`C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix`  
分支：`win/samurai-choreo-hard-validity`  
任务证据：`docs/project-harness/tasks/samurai-slash-guard-20260925/live-feedback/`
