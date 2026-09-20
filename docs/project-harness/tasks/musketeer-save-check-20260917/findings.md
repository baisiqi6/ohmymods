# 2026-09-17 火枪保存与日志只读核对

结论：最后一轮保存成功，18条均为已转职火枪手（KindUnit），不是枪架道具。日志4→15→18条，最后18条bound18；使用生产MusketeerArchive.Decode、ContextKey、IslandHash只读核验原生global-v35，当前context匹配且完整归一化岛指纹命中18条快照。18个GUID/18个nativeId唯一，所有nativeId在本岛原生objects中恰好出现一次。原文件读取前后字节相同。见save-proof.json和probe/Program.cs；没有调用任何Save/Commit。

证据边界：这是保存与静态对应确认，尚未发生下一次真实加载18人的验收。最新可读日志最后修改13:44:03/04，build=8.0.0-musketeer-20260916（LogOutput:16）；当前EC80BF3C / musketeer-hud-restock候选15:06:26才安装，不能将本轮旧日志当最新修复实测。原生global-v35最后写入13:44:03，MOD sidecar13:44:02。退出尾部有Input System正常Shutdown，无本次异常中止证据。

## 其他发现

| 证据 | 观察 | 状态 |
|---|---|---|
| LogOutput:1256 / save-proof.json | saved records18 bound18，18个单位精确匹配原生快照 | 本次保存已确认；重进恢复待实测 |
| LogOutput:646–750 | actor -93020，253.897–262.453秒内102次Walk/Run交替，nt=0，显示槽10/16；487.566–497.128另有相同模式 | 可能造成碎步/姿势跳变，未确认驱动速度来源。换携弓贴图本身未解决原生频繁切态，不冒称已修 |
| LogOutput:588/593/602 | 三次Character.DropItem Nullable trampoline NRE | 旧构建已知缺陷，后续安装已撤钩子；新版实机待验 |
| LogOutput:49/131–132 | 骑士load-mismatch后22身份重种 | 旧构建已知问题，后续稳定context/epoch修订已安装，原历史类型恢复仍不能保证 |
| LogOutput:613 | 一次Crossbowman scale drift，7名marked中1名y=1 | 仍未定位具体写入者，与英雄因果未证实 |
| Player:1273/1291/1309/1327/1345/1363 | 6名不同Beggar跌出地面，被引擎移回原点上方 | 可作为先前人物位置跳动的调查线索，不能断言由英雄缩放造成 |
| Player:564等 | CastleShieldShop Invalid NetID共11次 | 原生对象同步警告，未证明本轮存档损坏或单机功能失败 |
| Player:1591等 | Zpix非动态字体BestFit警告20次 | UI适配警告；尚无丢存档关联 |
| Player:5404–5410及其他3处 | Stats.ObtainUserId以Error打印账号字符串 | 没有伴随throw异常证据；后续原生文件和18条快照均已落盘，不把Error级别直接解释成保存失败 |
| Player:4499 | 一个CrownStealer没有wave mid | 已记录，影响未确定 |

本轮没有改生产源码/DLL/存档/配置，也未启动游戏。独立辅助日志审查调用因模型容量失败；结论由root直接核对原日志、生产指纹算法和文件内容得出，不伪称额外复核通过。
