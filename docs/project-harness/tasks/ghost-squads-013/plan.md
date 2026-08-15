# ghost-squads-013 — 希腊坐骑召唤四支亡灵小队

## 目标

- 把 Cerberus 坐骑的一次召唤从原生 `1 名骑士 + 4 名弓箭手`扩展为四支完整小队，共
  `4 名骑士 + 16 名弓箭手`。
- 四队中两队保留希腊亡灵外观，另外两队使用北境亡灵外观。
- 四队全部沿用希腊坐骑召唤单位的战斗、编队、生命周期和回收逻辑。

## 原生证据与设计决策

- 2.4.0 Cerberus 资源配置为一名 `Warrior_Ghost_Leader_Greece` 与四名
  `Warrior_Ghost_Greece`。
- 原生召唤协程只维护一个共享队长引用。直接把两个数量字段改成 4/16，会让十六名弓箭手全部尝试
  归属最后生成的队长，不能形成四支独立小队。
- 北境神器使用 `Warrior_Ghost_Leader_norselands` / `Warrior_Ghost_norselands`，但其 AI 与希腊
  子类并不相同：北境单位跟随玩家并使用固定持续时间；希腊单位主动向外作战并按离玩家距离回收。
- 因此保留原生希腊 1+4，另补三支各自持有局部队长引用的完整 1+4 队伍；其中两支使用“希腊逻辑、
  北境动画/精灵”的视觉 prefab，而不是直接生成北境行为 prefab。

## 安全边界

- 仅 Steam 2.4.0 IL2CPP、仅 Greece、仅 world-authority 发起补充生成；Mono 不修改。
- 不修改原生 Cerberus 的 1/4 配置、冷却、能力 RPC、雾效或原生首队生成协程。
- 补充单位必须通过原生同步 Pool 生成，设置同一个 `IGhostHolder`/Summoner，调用原生编队和死亡倒计时，
  并加入能力自己的 active ghost 列表，使原生 Deactivate/DespawnUnits 能完整回收二十名单位。
- 两个视觉池在主客双方确定性构建，固定使用预留 syncID 30130/30131；通用池分配器必须跳过该区间。
  不新增自定义 RPC 或序列化协议。
- 视觉 prefab 必须从 inactive Greece clone 构建，保留 Greece 具体组件布局，仅复制唯一精确北境 prefab
  的 animator/sprite/material。源资源缺失、重复、池冲突、网络前置不完整时 fail closed，不回退成错误外观。
- 原生首队未在有限等待内完成、任一补充单位生成失败、失去 authority、关闭 Mod 或能力失活时，立即停止
  后续补充；不得打印虚假的四队完成日志。已经由原生池成功生成的单位继续按原生希腊生命周期回收。

## 验收

1. Debug 构建 0 warning / 0 error，`git diff --check` 与 harness validator 通过。
2. Pool 重建后日志只出现一次 30130/30131 ready，且无 duplicate syncID、unknown pool、RPC 或 prefab 错误。
3. Cerberus 每次有效召唤最终恰好 4 名队长、16 名弓箭手；队伍分别为 2 支希腊外观、2 支北境外观。
4. 每名队长只带自己的四名弓箭手，不出现十六名弓箭手集中到同一队长。
5. 四队都执行希腊向外作战/距离回收行为；再次召唤、下坐骑、换岛、读档时没有残留 ghost 或重复追踪。
6. 联机只由 authority 生成，客户端能正确解析两个固定池并看到相同外观、编队和回收结果。

## 当前交接

- 源码、静态自审和文档同步已完成；IL2CPP Debug 构建 0 warning / 0 error，DLL SHA-256=
  `A52B5285C2A09B6F46AEDA20382C34F4CAC13667A218EFDF689FC1EF26B62DA4`。
- 源码提交 `c81adf1` 已推送；游戏退出后从该干净提交重建并部署独立副本，构建/部署 DLL SHA-256
  均为 `16A0206C8258CB6F2686FCF2B20EE3F4C7E95C9A11653AC43E5BFEB08FE8AE56`。
- 正在提交部署状态并刷新候选包；运行时四队编制、外观、编队和回收门禁仍未验证。
