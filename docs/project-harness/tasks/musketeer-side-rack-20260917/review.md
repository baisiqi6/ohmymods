# 火铳铺侧枪架独立只读审核

最终结论：bounded PASS，17:46性能收口已复核，无剩余候选阻断。仅最终6E2D候选可在确认游戏关闭后按既有授权交付；旧FDBE为pre-performance候选，不交付。最高层/多人拾取或旧档加载仍未实机验收，公开 v9.0.0 不因此变更。

审核对象：最终候选 DLL `6E2D89F1B0F35A504257CA187828C7B09C01336A7E2E3A2ADFE590D0934AA8BE`，基线公开 v9.0.0 DLL `641E79A938B16EE5A64962225F5BF36181A38760DD4931DD92383AD11756D7ED`。生产范围为 Shop、Rules、GunVisuals，以及 root 的 build 标记和 ShopPNG 整合；MusketeerIdentity 源码与 v9.0.0 相同。未重审旧身份档全流程或此前被拒的其他诊断任务。

## 独立复核

- 从 v9.0.0 Git blob 与实际磁盘文件比较三份源码，未依赖旧 canonical HEAD 的 diff；这些文件在旧 HEAD 为 untracked，直接 tag→working diff 的 deleted 展示不代表实际删除。
- 176×80、pivot(64,2)、PPU32 与三层槽 `(2.8125,.25/.5/.75,-.002)` 一致。整体选址中心减 .75 得到原店 root；排除区中心 +.75、半宽2.75；付款点和投币仍为原 root0。
- ReadRackItems 先校验全批整数槽 0..2，重复/非法/未知不移动。满三把仍可走独立布局门，未被 CanPurchase 的容量门误挡。只读当前已绑定职业名册，无新增场景扫描或 Harmony hook。
- 位置写入前复核 career/state/life/tool pointer/实例/shop/layer、StockClaimProven、未拾取且无友军/敌方认领，并要求原生已恢复 kinematic。slot=-1掉枪不参与。成功回读后才保存一次放置凭据；失败保留责任，换life/店/world可重试；同一凭据已成功后不因外部位移不断拉回。Clear 释放三格缓存及凭据。
- GunVisuals 去掉旧架枪额外 y+.23；真实工具本体与显示中心同步。架枪排序采用店铺层与 order+1，离架回原工具排序。原生 collider、pickup policy、价格4/自动8、容量3与付款凭据逻辑未改。
- 独立像素比较：新图 SHA `3bf36bb268c0726ae49ca546bc9ed4a290f75d82bca7a79dc1558d1719e6ca2c`，704×80、binary alpha；四帧各自前128×80与公开 v9 原店铺/枪匠 RGBA逐字节一致。预览确为右侧独立木板。
- 独立 Cecil 对比实际候选：3576方法体不变，11改变、40新增、1旧float槽位签名移除，变动仅在本轮 Shop/Rules/RackLayout/GunVisuals/Plugin.Init。比较包含 locals、InitLocals、MaxStack、EH及FilterStart。Harmony类型集合不变；8资源中7项逐字节保持，唯一变更 ShopPNG 与上面的候选图精确一致。Assembly仍为9.0.0.0。

## 执行证据与限制

已读取 worker logs：52侧架政策断言、18付款/槽位/暂停断言、550资源缓存断言、147自动补货联合回归通过；真实2.4 Shop/GunVisuals/自动adapter联合interop编译0警告0错误。root receipts另有147补货、72真实Identity/restock通过及完整构建0警告0错误。Reviewer未重新执行这些测试，独立执行的是源码、PNG和候选DLL只读比较。

52项直链真实纯值MusketeerRackLayout类，不能代替Unity内Shop.ReadRackItems/AnchorRackGun的实际运行。旧满架真实加载迁移、最高层居民拾取、三层trigger重叠时单人/多人交接与最终画面仍待实机。

几何证据仅证明普通Peasant与最高枪具trigger存在理论 .125 垂直交叠。2.1原生参考中 Peasant.OnTriggerEnter2D 检查 activeSelf/pickedUp，HandleToolPickup先标 pickedUp，再由Character.Promote同步回收原居民，支持保留既有防重复边界；不能当作2.4多人拾取已验，当前无证据要求新增拾取拦截。

临时认领或布局未确认期间，商店保守暂停购买，不收费；友军放弃认领后可恢复重锚。该等待不是身份丢失或自动退款。

Reviewer未修改生产源码、测试、素材或用户数据，未启动/操控/安装游戏，未Git mutation或发布；按追加授权仅写本审核证据。

## 17:39 性能补查

FDBE候选的 Tick 每帧 CanPurchase→RackCount→ReadRackItems→RackContextReady→TryGetRestockCounts，对所有职业记录（包括人物）读取绑定、active、scene、transform、health等native字段；旧 CopyGuns 对人物kind直接跳过。虽然不是全场扫描，也没有本轮实测耗时证据，但新增native读取没有受到 .5 秒维护节流。root已要求将完整身份快照限制到既有 .5 秒维护与真实出货边界，逐帧CanPurchase及单枪Anchor保留廉价实时上下文和至多3枪精确证明；不得用缓存true绕过即时状态门。当时该修订尚未纳入旧FDBE候选与其审计，最终状态见下节。

## 17:46 最终性能收口验证

上段为已淘汰FDBE的历史问题，最终6E2D已关闭：Shop彻底移除TryGetRestockCounts依赖，而非缓存其true结果。RackContextReady每次读取Current.Ready/ReadOnly/Unresolved/HasBaseline/Epoch/StockRestores与当前shop、world、Playing、暂停、保存门，并核现有逐帧TryContext结果的context/world。无关未绑定历史Unit不新增锁定手动购买，自动补货adapter原有完整计数门保持。

ReadRackItems对Unit和slot=-1仅作managed跳过；整数槽重复/非法先于StockClaimProven拒绝，每次读取最多对3把不同架枪做native证明。CanPurchase仍即时读取枪具及匹配当前Stamp，Anchor写前再次核对，未使用布局ready缓存绕过即时门。自有managed名册遍历仍存在，但本轮新增人物native读取已消除；这属于静态调用边界复核，不是实机耗时测量。

最终Shop源码SHA `D3EF223BEE8395AB2BC3B8E3EE4ADCEBFD56516FC4EF569568A159EA3B3A09C7`与root构建所用一致。已读取17:46最终52布局/18商店重跑、28生产接线静态检查及联合actualinterop0警告0错误；28项不称作Unity运行测试。root最终完整构建0警告0错误。Reviewer对6E2D重新独立执行Cecil和资源比较：仍3576同/11改/40新增/1旧签名移除、Harmony集保持、7PNG保持且唯一ShopPNG精确3bf36bb2。本轮源码选集相对v9仅Shop/Rules/GunVisuals、Pluginbuild与ShopPNG五项改变，无新增生产文件。
