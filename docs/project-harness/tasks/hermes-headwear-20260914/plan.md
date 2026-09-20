# 赫尔墨斯转化小怪随机头饰

目标：仅新法杖转化FriendlyTroll主机一次30%抽选，30款五world常规0..5+14周年外观，未抽中/旧存档无数据保持原貌；纯视觉，不改原MaskIndex、血量、toughTroll、永久控制或反制行为。面板战斗页开关，默认on/30%；关闭保留决定、只清理自有视觉。原帽消失只是玩家观察，本任务不声称修复。

当前基线：E独立副本1F111CD5/build6.1.5-archer-options-20260914；银行、缩放、鹿、补货、便捷与弓箭修改全部保留。游戏运行时不替换；不覆盖用户存档，不提交/发布，不写D Steam，不改Mono。

实现worker：两个独立本机OMP deepseek/deepseek-v4-flash max，core/visual分别仅新模块+其测试；root整合配置UI和验证。内置hermes_hats_reviewer只读设计/native/review。

正常ObjectData FriendlyTrollData JSON扩展外观receipt（schema/随机GUID/明确choice），在TryCreateOrFind恢复，不重写原生压缩存档、无sidecar位置匹配、无永久NetID/InstanceID假设。保存/读取不由开关门控，miss与legacy保留原样也有明确结果。网络独立组件尾部用于catchup，新增RPC仅全部原生注册完追加末槽；仅已caughtup时发当前生命周期状态，避免finalgrab先于poolspawn。只接受主机决定，主客同版mod协议；旧/坏数据安全退回原生。

自有SpriteRenderer挂实际animated Head节点，使用raw sprite的原始pivot/PPU，继承朝向。仅隐藏并恢复自有接管的native mask renderer，不改变native数据，不操作Hermes翼饰/粒子。资产44种离线叠图已核；游戏中视觉/动画/遮挡另验。

验收：core/visual直链行为回归含序列化往返/原生JSON与字节字段不变/未抽中与旧记录/重复Init/池复用/关闭保存与恢复/世界切换/hostclient消息与catchup；实际2.4 native唯一长入口、Unity/API核查；完整构建与旧24套回归、独立review。按安全边界受控E精确候选启动，保留存档配置银行；无法实际验证的法杖遭遇/真实保存读档与换岛/两机一致性如实待实测，不置done。

证据：C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hermes-headwear-20260914。contract.md、review-updates.md、visual-anchor-followup.md为实现契约修订。已核native目标与资源：actual-api.json、native-methods.json、native-disassembly.txt、sprite-catalog.json、asset-previews/headwear-head-anchor-preview.png。实现与本机候选安装完成，当前46AD1CF9；真实玩法/存档往返/两机验收仍doing，详见acceptance.md。

保存最终实现为Save/GetID的同快照薄桥，ObjectData构造器Harmony方案已撤销。
