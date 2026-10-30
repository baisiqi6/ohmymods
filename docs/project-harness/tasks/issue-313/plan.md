# #313 宝石盾卫蓝紫格挡晶光

## 目标与范围

用户确认概念B“蓝紫折光”。成功Block/Break时全身短闪蓝紫晶体光泽并少量贴身像素亮点，恢复原色；保留原来脸型/装备和全部帧的alpha、pivot、PPU与脚线。基线release/v9.5.13@cb467d4c；本视觉不依赖PR312的数值变化，不改伤害、90耐久、反弹、购买、存档、Main或原生detour。先Issue→独立实现/验证/审查→PR；合并、安装和实机另记。

## 显示契约

- 既有CopyLook独占本体sprite/color/material/PropertyBlock/anchor/hop；新Patch_HeavyShieldBlockFx专用leaf只拥有其子renderer：1个同形全身palette叠层与固定3个微型晶光。原图集逐像素确定性着色，仅保留原亮暗及alpha，不AI重画人物，不写原贴图或材质属性。
- 新效果只沿已成功Block/Break事件，且独立于是否实际扣耐久；任何特效异常均封闭在装饰层，不许改Pose/Runtime返回值、隐藏盾卫本体或吞改伤害结果。
- 自然时钟候选0.28秒、接收间隔0.45秒、峰值alpha≤0.62。active/冷却内丢弃重复事件，不刷新起点/叠亮；暂停取消、恢复不补播。时钟未知/回退只取消装饰。晶光高度/宽度≤本体约1.1倍，不改原生scale/Mover/Collider。
- 仅owned readable FullRect PNG可读，精细/粗两尺寸固定；PC GetPixels32、Android既有ReadOwnedPng；SetPixels32/Apply写新owned纹理。最多2个identity cache槽，每槽≤509帧；只逐出零引用槽，两槽忙时新identity本life关FX，不逐出在用资源。无GPU readback、新shader、每单位Material或全场扫描。
- Register只建固定自有子层与状态；原图集已经可用，色图准备延至首个已资格可见Tick，每identity一次；每hit仅置状态、不新建/烘焙。inactive稳定态避免额外原生getter/每帧分配。帧、flip、hop、sorting/layer/sharedMaterial在Tick尾同帧镜像；root原只有Own+Driver，子FX使用Own.sortingOrder+1，不写Material/PropertyBlock。
- 每个hidden/Suspend/失格/死亡/解绑/读档/换岛/池复用出口显式关残光；Dispose释放本实例子层与cache引用，Clear只清零引用的自有资源，不破坏原可见性归还凭据。

## 实施与验收

Worker在本机task staging实现自己的leaf/聚焦tests与ActorVisuals最小接线候选，Root审后整合并统一显式Compile。先pure pulse/palette/pixel alpha不变量红绿；再真实Actor/Runtime显示接线、左右、隐藏、异常不取消block、高频不白屏、连续hits对象数恒定、cache identity/refcount测试。精细/粗原atlas hashes保持。

实际Mac240/Win243 net6/net8及Android NoDeploy编译，ANDROID host边界独立；独立审查固定diff/源码与可回读视觉预览。所有必需consumer通过方可Ready源码PR，不用删消费者或空实现掩盖失败。PC/Android Native视觉尚未验，不能用interop/host/概念代替实机。当前无正常安装或游戏操作权限。

## 协作和状态

GLM5.3/max方案r2及增量APPROVE；首轮只有确认文本未当作批准。北京时间2026-10-31周六凌晨按协议worker使用OMP deepseek/deepseek-flash/max并核模型事件。受管任务通过Coordinate file入口登记，record侧/合并部署后再沿同operation推进，不冒充done。原始材料在.primary .local/tasks/shield-block-gem-fx-20261030/，概念只作方向参考。
