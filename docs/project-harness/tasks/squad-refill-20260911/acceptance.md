# 诊断部署通过；补员根因仍待现场验证
用户再次确认白天回城附近有弓手也不补。静态代码和9/9存档不能证明当前缺员原因，不能仅以距离解释；名册失配、真实资格过滤、走散等仍待区分。

本轮仅清理和定位：删除旧PatchRoles_CrossbowmanTowerDiag.cs（三个纯诊断hook；9/9日志中产生280/481行）。新增Fetch/资格/Archer禁用事件样本最多30行，每style30s+全局3s，每次最多256候选额外读取；不完整标truncated。原生循环被观察但不被重跑。无native参数、返回值、异常或招募规则更改。

既有KnightStyle巡检已获取的数组传入SquadRosterSnapshot，最多5轮/120s，每轮<=64knights+1024archers，缓存人数与名册/距离按style汇总，不新增场景扫描、资源加载、Update钩子或协程。含异常共最多36诊断行/进程。预算耗尽后不会再观测新事件；缓存差异和readMs（不含logger/整帧成本）不能被误当成名册损坏或卡顿定因。

最终Diag source4180F6C3246F178D354F12734088FF11C3CC8BA6CC8E4C7A35EF0ED3475A6731，Roster90D6D750FD16E485D67D431F0261DD77EA513228D57D3774B0EF170CBEF4D98D。ZCode sess_5cdd9ce4-15c7-461a-b1e9-5c83eb280cc2，nativeevent证明bigmodel/GLM-5.3，max请求未单独证明。其初案有编译/节流/异常问题，operator整理后独立36/36及重跑通过；实际引用0W/0E、26Unity可达方法/3hookwrapper通过，实际原生地址唯一性详见native-addresses。独立review PASS。

E本机部署DLL 8BA3448F1FF6A91B1726E28474913CFAF3992A3D69D1F1D678E5006241DEAD29，build4.5.0-squad-refill-diag-20260911，从EA5D1001...备份原子替换。受控启动2026-09-11T22:21:45.6985274+08:00至2026-09-11T22:22:37.0934084+08:00，PID34912为脚本自己启动并停止；恢复场景到ClockDiag，启动日志无Error。存档前后95FAC56BF198963A145574DE3963F5B891E53433D21E9AC86F1FA1C73D70A398一致。测试恢复暂停场景，不是伤亡或补员实战验收。

既有希腊火/幕府返队/银行HUD/120秒4人保留。未改用户配置或存档，未提交推送发布，公开4.5.0ZIP不变。后续需玩家正常战斗并在白天缺员时继续片刻，读取SquadRefill和SquadRoster判因，再实施对应修复。任务保持doing，不能宣称补员故障已修好。
