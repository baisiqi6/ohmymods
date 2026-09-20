# 独立侧枪架代码结果（2026-09-17，已冻结）

仅修改三份生产源码：MusketeerShop.cs、MusketeerShopRules.cs、MusketeerGunVisuals.cs。没有修改Identity、Archive、Persistence或存档格式、PNG、价格、容量、职业行为、补货服务、版本、游戏与用户文件。图集与build整合由root处理，游戏运行中未部署。

## 布局

- 4帧图集704×80，每帧176×80，32 PPU，pivot为原店铺像素(64,2)，保留原128像素店铺/枪匠的位置。
- 最终槽位采用root校正后的 `(2.8125,.25/.5/.75,-.002)`，没有使用早期 `.40625/.65625/.9375`。新枪的原生工具本体直接生成在槽位；取消GunVisuals架枪额外y+.23，显示中心与本体一致，架枪排序为店铺所在层+前一绘制顺序，离架后继续沿原生工具排序。
- 整体占地中心相对原shop root为+.75、半宽2.75。Find返回整体中心后减.75得到原店root，付款排除中心同样+.75；indicatorOffset/playerPayPointOffset仍为0，付款点和投币保留原店中央。
- 空位依据整数stockSlot 0..2，三个槽位即使同X也不混淆。重复、越界、缺失读取统一不放行新购买。

## 旧枪兼容与移动门

沿现有Tick每0.5秒，仅读取已绑定Identity的自有Careers注册表，无新增全场/资源扫描或Harmony hook。独立布局就绪门不依赖CanPurchase，因此旧存档已满三把也能迁到侧架；新购买在布局未确认期间暂停，不收费。

商店、当前世界/layer、单机Playing、非暂停非保存及原有手动商店身份状态门都必须满足。ReadRackItems先读取并检查整批架枪槽位，重复/非法或架枪读取未知时零移动；与架枪无关的未绑定历史Unit不参与陈列判断。真正写位置前再检查当前state、career、life、toolPointer、instance、shop与layer、精确StockClaimProven、未pickedUp、无friendlyClaimer/enemyClaimer，并要求已有原生架枪物理恢复完成（kinematic）。不会修改collider、claim、pickedUp或物理字段。

一次放置凭据包含career/state引用、life、pointer、instance、shop、layer，并按slot存放。只有位置写入及回读吻合后记为完成；失败仍保留责任，后续同世界低频重试。同一凭据已成功后不因外部位置变化每帧拖回。换life/身份/state/店/layer需要新的确认；Clear清理凭据及三格缓存。只有3个复用Item/委托，不在每帧为各枪重新分配闭包。

stockSlot=-1的死亡掉枪从候选中排除，已拾取、敌方认领或友军认领中的枪不会被重锚。友军放弃认领且其他证据仍成立后可以恢复处理。旧附加档仍记录原0..2槽位，原生保存记录真实工具位置；不更改档案schema。

## 17:43 性能与手动购买语义收口

Shop 完全不再调用 TryGetRestockCounts；不把自动补货的全人口覆盖门引入手动店铺。RackContextReady直接核Current.Ready/ReadOnly/Unresolved/HasBaseline/Epoch/StockRestores，并用Identity既有逐帧TryContext缓存验证context/world；世界/暂停/保存/商店状态仍即时重验，未缓存true绕开门控。原手动Identity.CanPurchase和TryRegisterPaidGun事务保持，MusketeerRestock自动采购adapter原全量计数闸门保持。

ReadRackItems只做自有career名单的managed字段筛选：Unit及stockSlot=-1先continue；合法槽位和duplicate位掩码在任何StockClaimProven原生调用之前判定。因此每次ReadRackItems最多检查3把不同架枪的原生身份/claim，没有对普通火枪人物读取scene/transform/damageable。陈列维护仍0.5秒，已放置凭据继续避免反复写位置。

新增check-shop-wiring.py的28项静态接线检查验证无全量统计依赖、Unit先跳过、至多3个合法不同槽位在native proof前验证、既有手动事务/自动统计门保留、移动前即时claim/生命检查及移动后回读。它是源码结构证据，不冒称Unity运行性能计时。

## 验证

输出均保存在本task的logs目录：

| 检查 | 结果 | 范围 |
|---|---|---|
| tests/musketeer-side-rack/check-shop-wiring.py | 28 checks通过 | 实际生产适配接线/调用边界，logs/shop-wiring.txt；非Unity运行 |
| tests/musketeer-side-rack/Tests.csproj | 52 assertions通过 | 真实生产槽位与MusketeerRackLayout类；0..3库存/所有缺位、异常重复slot、满三把重锚、claimed、同pointer新life、各stamp身份改变、未知ready零写、一次性/失败/异常回读与重试、占地/pivot/最高层资产几何 |
| tests/musketeer-shop/Tests.csproj | 18 assertions通过 | 保留手动4币凭据/重复付款门，旧float槽位测试适配整数槽位 |
| tests/musketeer-shop-perf/PerfTests.csproj | 550 assertions通过 | 原Bow缓存/资源发现策略 |
| tests/auto-restock/AutoRestockTests.csproj | 147 passed / 0 failed | 原真实服务与自动采购adapter联合回归，native Shop环境仍为既有替身 |
| tests/musketeer-shop-perf/actual-interop/MusketeerShopInteropCheck.csproj | 0 warnings / 0 errors | 真实2.4 interop编译实际Shop、GunVisuals和自动采购adapter；不运行游戏 |

测试界限：52项是实际生产纯值布局/凭据类的行为回归，未在Unity运行Shop.ReadRackItems/AnchorRackGun的原生方法。后两者接线通过源码复核和真实2.4成员编译核对；最高层拾取、三把trigger重叠时单人/多人交接、旧存档真实读入后的重定位和画面，仍需实机验收。最高槽工具trigger底边与已核普通Peasant顶边理论交叠.125，仅是资产几何事实，不能当作实机拾取通过。

仅受接口影响的既有测试改动：musketeer-shop/Program.cs更新整数槽位断言；musketeer-shop-perf/actual-interop/Stubs.cs补Identity内部读取形状与Access.Enabled，工程加入真实GunVisuals及Harmony引用；性能收口补Current状态字段/逐帧TryContext的stub形状。未修改其他测试断言。

收口后重跑52项布局、18项商店与actualinterop0W0E；手动核心编译的4个CS0649警告来自该测试不使用布局Item字段，真实interop无警告。147项自动补货及550项Bow缓存结果沿用本任务前轮：本轮未改其生产代码或规则。全量主工程构建由root负责，本worker未重复运行。游戏仍运行，未安装或启动/操控游戏、未Git操作或发布。

冻结源码SHA256：

- MusketeerShop.cs：D3EF223BEE8395AB2BC3B8E3EE4ADCEBFD56516FC4EF569568A159EA3B3A09C7
- MusketeerShopRules.cs：F9DF5DE73EA727E2812FC80F3B9702FC56CA24983DF8651FCEFDC43E93D7CAB1
- MusketeerGunVisuals.cs：A41D1D74D77E70BBD371A837D5E9C672BB5D7836950C63AD712E6581B7175907
