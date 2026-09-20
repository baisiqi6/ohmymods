# 人数HUD验收 — 2026-09-13

已集成canonical并部署E独立副本，DLL SHA256 `ffdd0e89a1401cd58ac4d1ce7dbd0c535b6a56a1fd649e9df56d50b63847e82f`，build=6.0.0-population-hud-20260913。保留剑风修复；未commit/push或覆盖公开6.0.0安装包。

默认左上角无框文本：八职业人数、骑士总数及中世纪/死地/幕府/希腊/北境。F5→人口新增独立开关。只数当前岛屿活体的真实组件，不按残留对象名/库存/订单计数；1秒缓存，初启/上下文或原生角色事件加最多两次延迟重建，稳定期无全场扫描。

## 验证

- 隔离及canonical直接编译生产Counts/HUD：30/30，分类与双native身份去重、death/inactive/外层排除、转职事件、延迟风格/组件、换岛、暂停/菜单/开关、缓存枚举边界、故障退避与partial隐藏、GUI状态恢复及窄屏检查。
- 原自动补货计数81/81，既有Add/Remove hook只增加safe dirty标记，无新native detour。
- 完整IL2CPP build0W0E，实际2.4 Unity API可达177方法无unstripping失败。独立reviewer PASS，确认客户端P2关闭。
- 正式候选FFDD0E89受控启动已读出真实职业名册，五项自动采购临时关闭仍正常统计。首次日志骑士尚未解析，后续1秒缓存完成风格解析。
- 可见游戏原生截图 `game-hud.png` 已目视核对：中文无缺字，列内无截断，未遮挡顶部时间/金库。实际截图：工匠35、弓箭手133、农民25、长枪兵18、忍者14、狂战士10、无业村民19、乞丐19；骑士22=中世纪5+死地7+幕府6+希腊2+北境2。
- Windows窗口捕获失败两次（SetIsBorderRequired 0x80004002）；改用临时隔离验收DLL AE9C6727，在现有ModPanel回调里一次WaitForEndOfFrame、CaptureScreenshotAsTexture/EncodeToPNG。两份Population源码哈希与正式DLL源码逐字一致。截图代码已完全移出交付：E恢复FFDD0E89，Cecil确认无NativeHudCapture类型，canonical也无该源文件。
- 可见验收自建PID36624，2026-09-13T01:44:41.9116772+08:00至2026-09-13T01:46:55.8436193+08:00，停止原因UI verification complete。save前后4E0E22D9951774D45B90B57A0F9D9A6C245BF7E1BA86BC05F06CB93022F6C468一致，bank 5305→5326→恢复5305，配置字节恢复，无游戏进程遗留。

## 边界

实际2.4 Character.OnEnable在0x9800D5调用HasWorldAuth(0x62e1a0)，false分支0x980270直接返回，仅true分支0x98025B调用Kingdom.AddCharacter(0x5997c0)。因此这份名册只支持单机/联机主机。客机清旧数并显示“联机客机人数暂不可用”，重获权威立即重建；不发布假0。本轮未验证真实双端联机切换。

组件若超过最多两次延迟重建后才出现，需要下一次角色事件重新分类；已知角色的parent/活性/death与骑士风格继续每秒更新。截图只验证当前分辨率/当前岛，F5开关逻辑由直接源码回归覆盖，未声称Windows工具实际点按成功。

详细私有证据：本机population-hud-20260913下WORKER.md、population-regression/canonical-population-regression、restock-regression/canonical-restock-regression、build-results、unity-api-audit、roster-native及native-input-hashes、各runtime-receipt/log与game-hud.png。窗口启动器第一次按不带process前缀路径误选同名Mono副本，立即停掉；用process:完整路径后选对E IL2CPP。无源/DLL写入Mono，存档哈希保持。该工具事故不作为功能验收证据。
