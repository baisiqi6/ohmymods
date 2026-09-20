# 骑士独立附加档本机候选验收

2026-09-14本机9881275D/build7.6.5-knight-identity-20260914：骑士GUID/style独立附加档，精确完整岛快照匹配；旧档首次迁移、新招募补少，registered后nonce主客确认，池复用/坏slot/容量/版本保护。37项目回归、36archive+25runtime+48network+9整合断言、0W0E/2609API/2004无关方法保持/两名review通过。游戏关闭时原子安装，旧9F1F备份，当前save57E54166和configA3E3A0B8保持；未启动/提交/发布。真实保存读档与联机待验，原版重存失配可重新建身份，匿名换岛逐人延续未实现。

## 交付范围

新增 KnightIdentityArchive.cs（独立文件/原子写/备份/容量与格式校验）、KnightIdentityRuntime.cs（生命周期/固定类型/均衡/原生保存载入薄桥）、KnightIdentityNetwork.cs（注册后的 nonce 请求响应/尾巴兼容/槽位保护）。PatchRoles_KnightStyle 只将分配与巡检接到固定收据；JSONSerializeModule 引用和本机 build 标识同步。原生存档、name、MaskIndex、全局世界换皮表均未写入新身份字段；未迁移既有 Hermes 存储。

三名实施 worker 均为本机 OMP 18.1.19 deepseek/deepseek-v4-flash thinking=max；provider native model events fallback=false，见 worker-identities.json。runtime 首轮 deadline、storage 首修 malformed DSML 均恢复原 session 后完成，没有换内置 worker。Operator 做现有文件接线和修订，以及跨模块联合回归。所有相关 OMP 进程已结束。

Reviewer：knight_identity_design_review 与 samurai_visual_audit 最终只读复核通过。主要修复包括真实 GetID 窗口抓岛、load scope 与 life 单调、主机唯一身份、新招募现场存活计数、部分映射拒写、文件版本/容量保护、旧nonce拦截、失效slot保留delegate强root直到Flush、不确定Add失败保护、队列不重入删除。Worker 原文只属于中间成果，最终源码/回归与本验收优先；特别是旧“Knight无Damageable”说法已由2.4实际interop属性核验纠正。

## 证据层级

- 干净完整生产 build：0 warning / 0 error，build-final.txt。
- 37 测试项目全部通过，见 test-results.json 与各 tests.txt；包含36存储、25runtime、48网络，以及真实Archive/Runtime/Network联合编译的9条握手断言。原生边界仍是模拟，不能代表真实联机。
- 2609 Unity managed调用路径对正确2.4interop检查无unstripping失败，all-interop-audit.txt。
- 2004 原有方法体保持；只7个既有方法在预期接线/版本范围内改动；比较归一化编译器迭代器编号，不忽略指令内容。unrelated-methods.json。
- 本次新增桥使用的长原生入口已只读映射且same_slots=1（native-summary.json）；本次没有启动游戏检验detour或JSON真实往返。
- 候选DLL SHA256：9881275DCE3A5FF9BB04471B08EAD72AB04CF1EE15728B7AF126713D5F056072。
- 正确游戏安装路径：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll。
- 旧DLL备份：E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-knight-identity-20260914-164046-725.bak。
- 安装前后当前用户save：57E54166C813EEA47B85AC44DC47D779A7AAA5D41B73651B1F00AED33C4E2AAB；config：A3E3A0B876BB5D421B681FFF5919FCE100BBAAEC043F083A36933F3114EA5439。没有回滚较早FAA426FC存档或7AB25105配置。

## 必须保留的限制／后续实测

1. 真实2.4 Save→Load完整JSON指纹稳定性没有实测；日志需观察 [KnightIdentity] save 与 load-match，再比相同GUID/type。
2. 原版不会覆盖独立文件；原版继续游玩并重存若完整快照改变，无法可靠认人，安全重新建立身份。每scope八代是有限journal，原生档和附加档不是原子双文件事务。
3. 原生carry按数量重建随行单位，本次不承诺匿名换岛逐人类型延续。非随行留岛对象仍依赖精确已保存快照。
4. 主客同版本真实联机、反复重连/池复用、关闭功能/新招募、缺资产恢复、真实备份回退均待游戏验证。
5. 未启动游戏、未提交、未公开发布。公开7.6.5不变；本任务保持doing追踪实测。
