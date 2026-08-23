## 2026-08-24 — 弩手皮肤修正：死地士兵（用户指正）

- 用户指正：弩手皮肤应为死地骑士小队随从（士兵姿态），非死地猎人。侦查实锤原生机制：Archer.ConvertToSoldier
  （入队/EnterGuardSlot上塔/OnEmbarkStart上船三路调用）= 动画控制器换成 soldierAnimator 的 biome 换皮
  + 权威端旗帜色染衣（CoatOfArms 主/副色）；ConvertToHunter（离队/下塔/死亡清理）反向。资产确认
  archer_soldier_deadlands 控制器+全套士兵动画（idle/walk/run/shoot/shoot_prep）在 2.4.0 resources.assets。
- 冲突核实结论（答用户"白天打猎会不会冲突"）：不冲突——行为（_knight==null 猎人例程）与控制器（外观）解耦，
  原生塔上弓箭手就是"猎人行为+士兵皮肤"；两套控制器由同一 Archer.cs 驱动，触发器接口一致。
- 实现：常量改 archer_soldier_deadlands；Apply/IntegrityPass 加 ApplyBannerColors（复刻原生染衣块，
  直接写 outfitColor 属性带 spriteFX 刷新，_isWearingBannerColor 幂等标记与原生共用）；Strip 改走原生
  biome swap（GetAssetSwapForThis(hunterAnimator)，顺带修掉跨世界恢复错控制器的隐患）。
- build=2.2.0-xbow2 已部署 E 盘（SHA 前16=7b4201b19b340679）。游戏内验收仍待实测。

