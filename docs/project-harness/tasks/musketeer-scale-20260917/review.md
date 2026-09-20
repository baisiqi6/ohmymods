# 日志与 0.9 外观缩放复核

**最终结论：本轮有界 PASS，无阻断项。** 精确候选 SHA256：`63EA50ADDE922CC1600EF11B8D2C4CFE10FBB12C555D83914CED55DA7DBD354A`，基线为 `284B78A0DC4D17D83D40712E157DF1E21FB20EB2E15D981D0371488016740C1F`。未运行游戏、修改生产/用户档或安装。

## 0.9 缩放候选

- `MusketeerAnimation.cs:133` 新共用常量 AppearanceScale=.9；`MusketeerVisuals.cs:342–347` 仅给自有 KEM_MusketeerSprite 子物体绝对赋值 (.9,.9,1)，父链、脚点 pivot、localPosition 和 localRotation 保持，不会重复乘而越变越小。人物和手持枪同在该图集，随同缩小。
- `MusketeerCombat.cs:906–918` 仅将枪口本地 XY 偏移乘同一系数，原 flipX 与父链朝向规则保持；没有改弹丸尺寸、伤害、射程、速度或伤害可靠性流程。Plugin 仅改 build 标记。
- 对 source-before.json 独立重算：严格只有上述三个源文件与 Plugin 改动；四项当前 hash 与 source-delta.json 相符。
- 独立用 Mono.Cecil 对 before.dll 与精确候选比较方法指令、locals、stack/init 状态及异常边界：**3653 方法保持，3 个改变，无新增或删除**。改变仅 Visuals.Apply、Combat.TryComputeMuzzle 与 Plugin.Init；Harmony 类型不变，**8 张嵌入 PNG 全部字节一致**。无 actor 物理、全局 Greek 缩放、商店/地面工具或持久化变更。
- 已读 79 项 runtime 通过日志及新增现有套件断言：自有子物体 XY/Z 与父挂点、枪口缩放、renderer.flipX 与左朝向均覆盖。实际 2.4 interop 和完整 build 为 0 warnings / 0 errors。reviewer 未重复运行套件，未将 stub 几何断言当作实机观感验证。

候选本身未在游戏中加载；真实外观大小与枪口观感待正常退出后安装、下次游玩验证。冻结日志全部来自缩放前的 combat-reliability 版。

## 本次日志可以证明什么

- `logs/BepInEx-snapshot.log:16–17`：本次运行的是 `9.0.0-combat-reliability-20260917`，CombatTargetLifeMarker 注册成功。不是前一版日志，也不是尚未完成的 0.9 缩放版。
- 同日志 `68`：火枪身份 `loaded:exact:records=23:bound=23:unbound=0:pending=0:stock=0:conflict=False`；`271`：`saved:records=23:bound=23`。这支持本次恢复了 23 条已绑定火枪身份并执行了保存报告；本复核没有读取原生/附加档验证磁盘 hash，也不能代替下一次重载验证。
- `72` 等分配日志报告 `units=23 left=11 right=12`。共 32 条相同分配结果，说明记录到的分配数均衡；不是每个角色实际站位截图或性能计时。
- BepInEx 冻结文件没有 Error 行，未见 CombatDamage Faulted / CombatTargetLife degraded。只能说本段没有这些失败报告，不能推导逐弹扣血、高密度 List AOT、目标生命复用或主客机伤害已经实测通过，也不能据此证明帧率提升。

## 本次仍出现的现象

- `Player-snapshot.log:581–658`：**11 条** `Invalid NetID from CRPCStamp on 'CastleShieldShop(Clone)'!`，调用栈是 CRPCStamp 的 Persistent.IBehaviour.ApplyData → IslandSaveData.TryPopObjectsToScene。属于读档中的盾牌店网络标识告警；不能只凭它断言火枪身份丢失或原生保存失败。`893` 起有 11 组 TowerKnight mediated sync 请求/立即登记，但没有对象对应证据，不能称它们已经修复上述 11 个店。
- `1375 / 1393`：原生系统确认 Archer P41 [6A5]、P43 [6A7] 穿地后将其搬回原点上方。日志没有说明根因；仅凭 Archer 名字不能区分普通弓手、火枪职业或英雄。不得归因于本轮伤害/外观缩放。
- `339`：一次 `Unknown Character Tag: Archer`，调用点 Holder.GetCharacterByTag。没有直接证据证明造成上述 23 个火枪手损失。
- 字体提示共两类：**10 条** Zpix 非动态字体不支持 BestFit（如 `1621`），另 **5 条** font size/style overrides 仅支持动态字体（如 `409`）。此外有 **2 条** Spart Farticles 缺失脚本提示（如 `333`）。它们需与伤害/保存问题分开描述。
- `BepInEx-snapshot.log:439–444`：同一英雄在 t=150.855 到 151.323（0.468 秒）记录了六次 Walk/Stand 状态采样交替，nt/ct 均为 0；支持“仍有短窗反复切态”的观察，不足以确定动画阈值、守位逻辑或 FPS 为根因。本轮不修改该既有现象，也不重试此前 blocked Hero/Crossbow 诊断。
- `Player-snapshot.log:665 / 1366` 的 Stats.ObtainUserId 使用 LogError 打印用户名；后一处位于 RetrieveData → ObjectData ctor → Save 栈。这不是一条带失败说明/异常的保存错误，不能把它误读成存档失败。

冻结片段尚无退出尾声，结论只覆盖采集时已有内容；后续新日志和实际视觉效果未纳入本复核。
