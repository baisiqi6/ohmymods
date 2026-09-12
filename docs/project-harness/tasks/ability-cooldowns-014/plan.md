# ability-cooldowns-014 — 法杖与 Cerberus 召唤冷却微调

## 目标

- 把 2.4.0 HermesStaff 的实际冷却由 30 秒降为 22.5 秒。
- 把 Cerberus 四支亡灵小队全部消亡后开始计算的冷却由 30 秒降为 22.5 秒。
- 只缩短冷却，不改变法杖范围/控制数量/永久控制，也不改变亡灵数量、两套行为、持续时间、回收或网络池。

## 已核对事实

- 2.4.0 `resources.assets` 中 HermesStaff 的基础冷却为 30 秒，每只转化目标附加冷却为 0 秒；
  原生公式仍是“基础冷却 + 每只目标附加值”。
- 2.4.0 `Cerberus Greece` 的召唤能力冷却为 30 秒；原生在最后一名亡灵离场后才启动冷却协程。
- 两项都采用 0.75 倍，即 22.5 秒；不修改 `_nextActivationTime` 的网络协议或添加 RPC。

## 实现边界

- HermesStaff 在 Awake、可用性检查与实际触发前应用 Enabled 对应的 22.5/30 秒配置。
- Cerberus 在 Activate、RemoveActiveGhost 与 DespawnUnits 的公开入口前应用 Enabled 对应的
  22.5/30 秒配置，确保最后一名亡灵离场时读取的是正确冷却。
- 不 patch 私有冷却协程，不修改 Mono，不触碰 Steam 正式目录或共享存档。

## 验证

- IL2CPP Debug 禁部署构建必须 0 warning / 0 error。
- 静态确认 HermesStaff 和 Cerberus 的实际资源值均为30秒，且所有会启动 Cerberus 冷却的公开调用链均覆盖。
- 游戏退出后只部署独立测试副本；构建、部署、zip 内 DLL 三方哈希一致并刷新候选包。
- 实机分别验证法杖释放后约22.5秒可再次使用，以及四支亡灵全部消失后约22.5秒恢复坐骑技能。

