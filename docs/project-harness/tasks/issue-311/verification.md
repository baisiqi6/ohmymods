# #311 — 宝石盾卫耐久与格挡反弹

基线 `release/v9.5.13@cb467d4c1a23474f9678794bb42c3f12cfd4b5cf`，分支 `codex/heavy-shield-combat-311`。[PR #312](https://github.com/baisiqi6/ohmymods/pull/312) 已创建；本轮为源码、自动测试、接口编译与 PR 交付，安装与玩家实机单独记录。

## 行为

- 耐久上限90。首次有效正面格挡扣1，此后与上一次实际扣耗至少间隔1自然秒才再扣1；游戏加速不缩短间隔。窗口内仍成功格挡和反弹。每名角色、每次生命独立，不按整秒桶分组，不积攒扣耗额度；维修和重复挂载不重置窗口。异常或倒退时钟按旧逐次扣耗，不更新时戳。原有背击、范围、特殊敌人和身份门保留。
- Schema5：v1按3、v2/v3按4、v4按40先验证原范围，再向上取整比例迁移到90；v5原样。20/40→45/90，1/40→3/90，0保持破盾；购买、未知/隔离、原生身份、fingerprint版本3与Prefs key保持。维修原6游戏秒安全期、8游戏秒工作保持，完成回90；磨损分档68/46/23。
- 普通Troll有效正面格挡后，复用原生碰撞本次创建的PauseMotion，把恢复速度设为实例knightBounceForce与localScale.xy逐分量相乘再×1.2。保留原生有符号向量，X必须离开盾卫；方向/字段未知则保留原生恢复速度。固定1.2是名义基准+20%；原版还有0.8–1.2随机，距离仍需实机确认。箭不弹开发射者，不增加伤害、随机数消耗、协程、刚体或坐标写入。

## 原生责任与审查纠偏

当前Mac2.4.0 ARM64证据：Troll.TryDamage `0x90a930..0x90ab74` 依据目标surface/举盾读取knightBounceForce。ApplyCollisionDamage `0x90baa4..0x90bc10` 在普通目标分支的ReceiveDamage之后创建/启动PauseMotion；blocking/surface1/2分支则尾调用ReceiveDamage、不创建新pause。MoveNext `0x911650..0x9117a4` 首tick进入state1等待，随后绝对写velocityAfter。

采用已有碰撞Postfix与伤害Finalizer。成功格挡凭据绑定同impact、同paid career和source/target life；不依赖本次是否扣耐久。伤害链正常结束时记录pause指针，碰撞正常返回后只消费一次；当前pause须同时不同于碰撞入口与伤害链结束时的pause，且类型/owner/state1/1⁄15秒/有限值均吻合。null可以记录为已知0；读取失败或异常完成保持unknown，后续成功不能洗掉。这样外层无pause分支不能借用同Troll内层的对象。

Operator纠正了候选以归属Side代替当前朝向、擅自归一X方向和未格挡完成后仍能迟到领凭据的偏差。独立B审查另以真实native分支加受控同步重入获得3个所有权红例；Root修复后保留全部反例与普通分支正向控制。该证据证明合约边界，未证明默认Archer prefab实机已触发无pause分支。

## 验证

| 项目 | 实际结果 |
|---|---|
| Root相关回归 | 13组通过；policy 8 scenarios，save 142+68+30+594，identity 136+328，runtime最终1601断言，maintenance 36 tests/2260 checks；其余包含integration、visuals、leap、banner、promotion/quota |
| 反弹归属回归 | 82断言/0失败；包括原3个child误借反例、普通外层、异常外层、同target正常/异常重入、unknown/exception粘性、正常格挡skip后的真实Finalizer接线 |
| Mac2.4.0实际interop | 完整352 Compile /275资源 /161引用，net6/net8 NoDeploy编译均0W0E |
| Windows2.4.3实际interop | 完整352 Compile /275资源 /162引用，net6/net8 NoDeploy编译均0W0E |
| Android实际interop | 显式清单补新leaf；0 errors/1既有未用Banner字段warning；ANDROID host平台契约6断言通过 |
| 独立审查 | A固定17文件哈希、五组冻结回归APPROVE；B首轮REQUEST_CHANGES，修复后增量复审APPROVE；独立同驱动旧3产品源精确3红、新源56全绿 |

测试使用本机SDK10，按各测试项目声明的net6/net8编译、在SDK10 runtime以Major roll-forward执行；这些host边界和接口编译不代表游戏或设备运行。最初私有构建漏递归Palace源、SDK换TFM复用assets、测试启动路径/参数及污染后的双fixture前提错误均保留原日志，只修验证入口，没有用产品补偿掩盖。

## 边界与版本

未运行游戏、未安装、未写正常DLL/配置/存档。当前Mac正常累计版基线是c0d7aa08…，和本次完整public source验证产物分别留证。Windows原生coroutine时序、各平台实机反弹距离、密集怪群体验、暂停/加速及存读档仍待对应平台验证。

本PR不正式发版或单独递增版本。Schema5写出的记录不能由旧schema4 DLL读取，后续安装保留与游戏保存配对的备份，回滚版本时按配对恢复；发布批次由VERSIONING规则另判。Issue在PR未合并前保持开放；canonical checklist现已通过Coordinate `task create-files`登记 `heavy-shield-combat-311`（operation `81ec8cbe-7044-48ee-afbc-1dd4d57f6ab3`，workspace `ohmymods`）；文件侧为todo待集成，合并部署后沿同operation做record侧，不冒充managed done、未裸改JSON/DB。

本机原始证据位于 `.local/tasks/heavy-shield-combat-feedback-20261030/`：native-reference报告SHA `d027e9529fd1277e72eac8040aee0f84017e9da83b7f249a28d7e36140d2d299`；worker-a/b保留红绿日志、补丁和模型事件；root-tests/root-build与independent-review-a/b保留独立结果。方案与纠偏均由GLM5.3/max请求审查，公开事件核实zhipu-coding-plan/glm-5.3；北京时间周六凌晨按协议派发OMP deepseek/deepseek-flash/max，两worker公开model/thinking_level事件核实。

Provenance: Mac Max / Codex · Maintainer Operator · acting_for=ohmymods Mac Operator.
