# 长按续买覆盖弹药：worker结果（已冻结）

生产只修改 `il2cpp/PatchPlayer_HoldPurchase.cs`，保留原开关、所有世界OptionalQoL范围、默认关闭与现有原生付款链。没有修改价格、钱包、弹药库存、容量、角色、图片或侧枪架；没有新增Harmony hook、全场扫描，也没有调用SiegeAmmoCounts。

## 实现

- 新白名单仅包含 `PayableWorkshopBarrel`，及同GameObject上活动FireTower精确拥有的PayableComponent（`component._owner.Pointer == tower.Pointer`）。其他PayableComponent即使带有工具店tag也不会绕过owner门。
- Payable本身须enabled、GameObject活动。**不要求FireTower AI enabled**：AI停用不等于付款owner无效，客机仍可走原生付款。此处依据2.1参考与现有项目边界谨慎保留，不声称已经实测2.4客机Awake。
- 弹药hold记录捕获原付款owner指针。Tick、Prefix等待RPC之前/绑定之后、加速和续买之前、Postfix及PerformPay回执均重新核对目标实例、当前world/layer/scene、动态白名单和owner。更换成另一合法FireTower owner也不能继承上一owner的hold；停用/移出当前层/读失败等结束旧会话。
- Bind若在分类成功后、第二阶段原生类型/owner读取时异常，明确清掉ShopIsGoods，防止留下goods=true却没有动态proof的半张凭据。
- 首币与原Holding阈值保持；连续按住0.6秒后仍只临时调整金币间隔，下一次购买仍由原UpdatePayState合成按下路径发起。库存、设施、钱包、距离、暂停、本地输入权限、客机forceBlock/RPC成功回执和拒绝退款均沿原逻辑，不直接调用Pay/Spawn/TransactionComplete或PerformPay，不写价格或库存。

## 验证

- `tests/hold-purchase/HoldPurchaseTests.csproj`：**65 scenarios全部通过**，原25+新40。日志 `logs/hold-purchase.txt`。
- 新场景包含5币桶/2币火塔连续购买与钱币守恒；首次币和0.6秒门；关闭时与未打补丁的模拟原生轨迹一致；满库存、CanPay设施未就绪、钱不足、松开、off、暂停、Payable禁用、GO停用、距离、本地权限丢失均无额外合成购买/扣款；客机等待不多花钱、拒绝退款不重试；AI disabled但合法的客机塔可在真实成功回执后续买。
- owner负例覆盖null、错误owner、另一合法owner指针、读异常、回池停用再启用、外国layer、等待RPC期间换owner，以及Bind第二阶段读取异常。核对金币间隔归还与无额外购买。
- **测试范围说明**：生产HoldPurchase文件直接链接运行；`NativeSim.cs`仍是基于2.1参考的付款状态机模型，补有项目既存2.4主客回执分支，不是运行游戏或完整2.4行为实测。弹药设施就绪/容量通过模型的原生CanPay结果驱动，不伪称检查了真实塔/投石车场景。
- 新 `tests/hold-purchase/actual-interop/Interop.csproj`直接针对真实2.4程序集编译完整生产HoldPurchase：**0 warnings / 0 errors**。日志 `logs/actual-interop.txt`。验证TryCast、Payable.enabled、FireTower、PayableComponent._owner.Pointer及已有原生成员形状，不运行IL2CPP。

源码freeze SHA256：`FC85D406A72C896FFF3D59BAAEC7922D5676C26FF947560BC8E0C9F492F79940`。

测试改动限定tests/hold-purchase：新增AmmoTests与actual-interop；Stubs增加两种弹药native类型表面和故障注入；NativeSim增加弹药fixture，原付款模拟算法未改；旧测试入口仅接入新增场景，旧断言未弱化。

全量build、版本/面板文案整合及DLL审计由root执行。本worker未改这些集成文件，未操作游戏、配置/存档、Git、安装或发布。正式9.0及本机侧架功能保持，实际游戏弹药长按购买、库存上限及双机回包仍待玩家验证。
