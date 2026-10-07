# Calendar HUD 随身钻石

```sh
dotnet run -c Release --project tests/calendar-gems/CalendarGemsTests.csproj
dotnet run -c Release --project tests/calendar-gems/CalendarGemsHudTests.csproj
```

两个工程分别直接链接产品 `PatchUI_CalendarGems.cs` 和产品 `CalendarHud.cs`，无测试专用产品分支。

- helper：40 项断言，覆盖 0/更新/更换钱包、本机/远端/当前世界、双本机、缺钱包/错误归属/负值/读取故障、清理与只读性。本机输入转交给另一 controllable 时仍显示该君主的钱包。
- HUD：46 项断言，覆盖原日期/金库/外墙同屏、0.5 秒缓存、Draw 不查询钱包、故障不遮掉日历、双人槽位标识、显示开关/换世界、四档屏幕尺寸的列布局、GUI 状态归还。

测试使用受控 Unity/游戏 API doubles；GUI 测试核对调用矩形及状态，不证明真实字体字宽、像素观感、联网角色初始化或玩家拾取/付款已实测。真实 2.4 ARM API 编译与原生身份绑定链证据另见 `docs/project-harness/tasks/issue-175/`。
