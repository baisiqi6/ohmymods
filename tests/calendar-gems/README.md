# 当前战役储存钻石 HUD 回归

产品 `PatchUI_CalendarGems` 和实际 `CalendarHud` 分别直接链接到 helper / HUD console 工程。测试 doubles 不替代原生加载或游戏验收。

口径：当前**离线普通战役**的 `storedGems`，标题“储存钻石”，共享库存只显示一次，不读或合计钱袋。每半秒取已加载的 `GlobalSaveData._loaded`，按 `_currentCampaign` 的显式边界读取 `campaigns[slot]`。不调用可能创建存档的 `loaded` getter、隐式选择器或磁盘存档，不保留 Native 对象。联网/挑战来源尚未验收，显示“—”；未就绪、无效槽位、空对象、负余额或 getter 异常也是未知，可信0才显示0。

验证覆盖取存更新、同global切档、不同global重载、无效slot不fallback0、null/zeroPointer/读取故障、联机/挑战门、清缓存、双本机不合计、无钱包读写和无存档创建调用。HUD集成同时覆盖半秒节拍、Draw只读缓存、金库/日历/墙状态保持、加载/菜单/世界切换、视口范围及异常后的GUI状态恢复。

```sh
dotnet run --project tests/calendar-gems/CalendarGemsTests.csproj
dotnet run --project tests/calendar-gems/CalendarGemsHudTests.csproj
```

保持原 `Refresh(Kingdom, Transform)` 调用签名，kingdom不参与计算；移动端现有调用不改，已暂停的Android实现/构建/安装不在本次范围。Android冻结源码hash清单后续应在其授权恢复时同步新来源，不能把本轮PC测试当作手机实机验收。
