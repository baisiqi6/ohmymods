# 英雄驿站当前岛候选验收

2026-09-15 英雄驿站当前岛候选完成，未安装/发布：8币训练现有弓手、每侧固定1席直到确认死亡；关闭/塔/丢弓转职/临时停用保留购买，真实回池不串人。独立sidecar精确快照+pin基线，修原生落盘失败回退锁槽、原版重存未知身份静默丢失、新岛初始化及OnEnable误解绑；未知身份保留名额并暂停收费。原创512x80四帧商店贴图，原英雄2PNG/围巾/金箭保留。build0W0E、87购买/81效果/31商店/146+85xUnit、actualinterop及独立93回归通过；DLL审计2665旧方法不变/10集成修改，候选SHA 70d53395. 跨岛名额范围待用户选择，运输身份桥和实机验收未完成；E盘仍80522bf1正式8.0.0，未操作游戏或用户数据。

候选目录：`C:\Users\ADMIN\Documents\Codex\2026-09-05\ohmymods-operator-2\hero-shop-20260915\candidate`。候选说明及SHA256与DLL一同保存。

## 已验证

- IL2CPP build 0 warnings 0 errors
- recruitment runtime/archive 87 assertions
- independent review 93 assertions (87 plus 6)
- hero effects 81 assertions
- shop core 31 assertions
- shop actual 2.4 interop compile
- combat xUnit 146
- Greek impact xUnit 85
- 2665 old methods unchanged; 10 reviewed integration changes; two old PNG resources unchanged

## 独立审查

reviewer两条隔离失败复现已修复：IslandSave完成/Global磁盘未提交后读旧零席快照；已购正常保存但未再次MOD读档就被原版重存。另核对新岛零基线、真实FastDespawn后解绑和非回池OnEnable身份保留。最终93断言通过。审查冻结HeroRecruitment SHA256 73DB1341EE69C2C48D78B3F353C16C2B5F698E94F3B4C9500A5740E67477E33F。

## 未完成

用户已收到名额范围选项：整个战役共2个，或每岛各2个并处理目标岛容量。尚未回复，不自行迁移匿名身份。原版CarryForward仅有人数与工具标志；跨岛运输身份桥尚未实现，不能说英雄随船跨岛已经可用。真实接口注入、投币/退款、选址/比例、生成岛、死亡和保存读档尚未实机验证。因此本任务doing，当前候选不安装、不发布。
