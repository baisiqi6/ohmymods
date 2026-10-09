# #199 钻石库存 HUD

用户要求保留现有字体/样式，把“随身钻石”统计改为“储存钻石”。当前离线普通战役读取已经加载的GlobalSaveData._loaded，严格按_currentCampaign现有槽位读取CampaignSaveData.storedGems。未知显示—，可信0显示0；共享库存只显示一次，不合计两个钱袋，不读磁盘存档、不写币或存档。

修复点在读取来源：移除wallet读取链，避开GlobalSaveData.loaded可能构造存档的getter。当前锁定游戏的x64 slice/metadata/interop重哈希与原生叶子证据一致，确认getter构造分支与storedGems原生更新字段；联网与挑战来源未完成验收，明确显示—，不猜本机存档为主机余额。保持Refresh(Kingdom,Transform)调用签名，不改变共享调用者；Android未恢复构建/安装，其冻结来源hash清单待授权恢复时同步。

验证：真实产品链接helper55断言、CalendarHud集成53断言均通过，独立复跑通过；Mac当前interop累计候选构建0 warnings/0 errors，284输入/161引用逐哈希匹配，只变两份HUD产品源码与构建包装器路径，原有素材与其他系统保持。git diff --check通过，独立最终审查Approve。初次集成fixture把new对象误设为同Native指针，失败记录保留，改为不同world/scene/director身份后通过，产品菜单行为未更改。

代码交付、候选安装与玩家存取/跨岛/读档观感分别记录。尚未验证实际运行中的存取余额变化、联网/挑战库存；本记录不声称玩法完成。仅本HUD任务，坐骑分岛方案和付款圆点诊断独立。
