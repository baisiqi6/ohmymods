# 首次加载即种入骑士身份附加档验收

2026-09-14本机444E1611/build7.6.5-knight-load-seed-20260914：修首次迁移只在原生Save才写骑士附加档，现加载冻结来源、完整名单确定后由既有巡检整批写附加档，不保存重进也可恢复；保留精确快照/同life/双向唯一、Squire明确排除、失败等待/拒绝。53项目通过（seed34/runtime26含22名无Save真实接线重载/网络整合9）、0W0E/2750API/2340旧方法保持/review通过。闭游戏备份63105375安装，save57E54166/configAD2CB236及已有MOD附加文件保持；未启动/提交/发布。真实连续两次启动未Save验证仍待，头饰显示调查与英雄弓手美术准备仍排队。

## 现场事实与修复

用户报每次进入骑士类型仍重新分配。实际ModSave没有knight-identities.v1.json，原生global-v35最后写15:34早于identity16:40安装，内容SHA57E54166未变；上一游戏运行7D3D日志仅有KnightStyle persistent identity，无真正seed/save/load-match。当前岛camp1/land9有22条Knight/KnightData rank1记录。明确缺口是首次分配只在内存，未触发原生Save就没有外部身份档；并非用户没装功能。不能根据旧persistent identity字样宣称文件写出。

新增KnightIdentityLoadSeed仅在Missing/有效档但无精确snapshot时启动。Load prefix冻结scope/hash及精确记录ID/record pointer，不依赖之后排序/Decay或被置null的objects；TryCreate后捕获实际owner/GO/Knight指针、life和world。原生bool返回成功且无异常，名单完整才转pending；现有5s IntegrityPass的PrimeExisting/Apply循环后及既有Promote Prime后Flush，所有正式骑士都有收据后整批AppendSnapshot并读回核对。没有新native hook或全场扫描、没有修改原生存档数据/名字/游戏进度。

已知ID但recordptr不同立即拒整批；读取componentData2失败不能当非Knight。Squire共有KnightData，只有精确返回实际Squire才记Excluded，并参与owner/life双向冲突；缺回调/未知tag/Decay丢失不是排除，全部Squire不写空表。死亡/身份/角色/world变更丢批，inactive/读取异常等不确定性等待；新招募不加入旧来源快照，正常Save另生成其新快照。IO失败退避并保留pending，现有Archive不可变冲突规则与未知版本保护不放松。成功日志[KnightIdentity] seed/save，恢复load-match；普通上色日志改identity assigned避免混淆。

## 测试与独立核验

OMP deepseek/deepseek-v4-flash thinking=max，native会话fallback=false；首轮7分钟与修订4分钟deadline后恢复同会话完成报告，没有换内置实施worker。内置knight_identity_design_review只读审查三项必要修复和最终源码通过。
53个回归/接口编译项目全部通过：seed34覆盖无Save/22GUID/type恢复、部分late-ready不写半表、排序清空来源/false或exception/双向重复冲突/同native新life/Squire/未知或坏档/文件失败等；原runtime26包含Operator真实root接线22名无Save→sidecar→重载一致的用例，原始模拟文件bytes/mtime保持；Archive/Runtime/Network联合9断言等旧回归保留。
初轮root新增整合失败揭示旧stub把Knight/Persistent组件指针留0；按真实interop补非零指针后26/26通过。未通过删除生产指针保护来让测试变绿，初轮红绿记录保留。Worker临时legacy-suite未复制为正式重复测试项目；测试源链接改相对canonical快照，interop工程全部接口HintPath修正到正确E实际游戏（worker原声明实际路径但部分仍deps）。
完整生产构建0警告0错误；实际E接口全源编译通过；2750可达Unity接口无unstripping失败stub。2340个旧方法体保持，仅load桥/Finalizer返回值、既有巡检/Promote Flush、测试reset及日志版本/上色文字改变；旧End/Finally签名改动明确列入审计，其余删除不允许。新helper与worker冻结SHA均ADA1E467…，源码哈希与干净构建一致。

## 安装与限界

DLL 444E1611127D499B4E071B121E20336E22B1C8C1ADB7DFCDF0CDEF3B635E306E
备份 E:\Kingdom.Two.Crowns.Call.of.Olympus\Kingdom.Two.Crowns.Build.22992091\BepInEx\plugins\KingdomEnhancedMod\KingdomEnhancedMod.dll.before-knight-load-seed-20260914-212039-709.bak
save 57E54166C813EEA47B85AC44DC47D779A7AAA5D41B73651B1F00AED33C4E2AAB
config AD2CB23670AF6AD858D68E779B075F49A15E225B0668D46919E811A7689EB1CF

闭游戏原子备份安装，原生存档与配置哈希保持；已有Hermes游标和骑士附加档存在状态/哈希也保持（此刻身份文件尚不存在，要下次真实Load及巡检生成）。不启动游戏、不提交/发布；保留63105375稳定头饰配额与诊断、散射/火矢/骑士网络/银行等所有修复。更新TXT保留火矢半径0.25、直径0.5与受击宽0.375约1.33倍对比。

代码回归已通过，但实际首次进入→等待名单就绪巡检（默认5s）→不保存退出→再进的两次游戏验证未做。需看到真实seed entries=22及下一次load-match，再确认人数/类型。原版重存指纹改变、原生衰败导致缺失记录、匿名carry换岛仍沿严格边界，不承诺凭临时ID续人；此前未记录的历史某次随机分配无法恢复。任务保留doing。

后续：头饰显示/低比例调查需读63105375之后新诊断日志，不把配额修复当渲染修复。旁支原创英雄弓手美术准备已授权但要求当前修复验证妥善收尾后做，约束及来源见hermes-headwear-visible-20260914/plan.md，保持排队。
