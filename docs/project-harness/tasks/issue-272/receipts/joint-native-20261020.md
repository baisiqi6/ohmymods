# 坐骑岛地图与保存重进联合验证（2026-10-20）

本记录取代早轮“缺剪影根因未定、船位待验”的当前状态，不改写失败原件。最终产品源码 `0b17095a6a4e7683988765f52f96e773e52a9169`；主线净迁移11个业务源，排除旧候选27个无关/诊断差异、Day32桥和审计宏。正式候选B：net6 SHA `25d9ffef5ad20436bf49c8f7f7c7fdb395e16a86f2bae63900ee6df0a8f4c156`；net8 `74d7a2f184c20104fa74fee90d886fb476462d1209041a429c050e18fdfc0b6d`。两者281 Compile /161 refs /38 assets，0W0E，NoDeploy。候选日志10.9.39，不是新增公开release或独立返修计分。

## 根因与修改

- 坐标采样错误在诊断器：managed `Vector3[]` 隐式复制到native，原数组仍全零；改显式 `Il2CppStructArray<Vector3>(4L)`，原生填同数组后读回。不是产品坐标全零，未对产品加几何补偿。
- 八个原生锁定剪影实际已渲染，旧岛地几乎同色。实际2.4素材与同帧像素独立比对 IoU .94954 / Dice .97412，反向对照不匹配。采用原版地图灰阶层级校色后八剪影可见；未强制发现、解锁或补历史。
- 详情船位A40实际仍在岛内：`GameObject.transform as RectTransform` 的managed基类wrapper导致初始化前返回。改用原生组件 `GetComponent<RectTransform>()`；actual asset确认船根自身RectTransform32×32。保留隐藏clone发布前一次初始化、归属、footprint/岸线/page边界、回滚，未加持续覆写。新增basewrapper反例红测46处失败；修后ship92/full304，默认工程依赖同步补齐，独审通过。

## 同候选实机

Mac ARM64 / IL2CPP 2.4.0 r23485，独立HOME/save/config，正常存档不替换。A41 F1冷读真实先前自然保存的完整16文件；A39 F2只读A41刚写出的完整16文件，不重用出航前档代替重进。

两实例均真实 Loading→Playing11、八地图记录及八对应场景设施；稳定详情八剪影完整可见，overview与detail船均在岸外，无旧位置双船，往返后保持。详情船实际 x=-48、y=58.3684，shoreTop=38.368、32×32footprint，水距4。配色与岛形保持。两次UI“保存＋退出”均由原samePopen自然exit0/wait0、soleReaper/kernelGone、零signal/stop/foreign，不是超时杀进程。

F1 native SHA `447f4bb099db9a86b1c365e3142da8b7e76cc844f0057d3d901edf98f9fe2a30`，3518对象；F2 `045e4b58d177dc3233f7e8dc801f9d9e9c623d69c0f66b122bab336e923484a8`，3524对象，八marker仍[21,22,23,24,25,26,13,6]。完整正常closure两轮均 `ec6453f57d93445c9168501ba7b69575684668e369e046a295099c8dcb360144`，未restore。启动最早一次前台查询遇NSApplication尚未就绪，未发permit/未操作；同实例180秒固定启动界限内成功观测就绪后仅发布一次新permit，未绕过身份守卫。

## 如实保留的边界

旧岛8对象均保持，playTimeDays每次原生保存增加一个double ULP（3.552713678800501e-15），原值/hex均保留，不以epsilon伪称bit-exact。原生存读档会重建UID，核每档八root路径与UID唯一，未要求跨实例UID相同；角色Unknown/Unregistered未伪称Exact。已有NoEnvelope/Abort/早期pool、Harmony sandbox及网络代理提示仍逐项留原日志，不称所有Error消失。

原生出航与首次生成证据来自此前H候选；B无审计正式候选本轮实际验证的是已有八设施的冷读、地图与再次保存，不冒称B重新首次出航已测。第二岛13本轮仅资源/布局/初始化日志，不称已自然抵达；Windows2.4.3、Android设备、联机未验。

Issue260中**正常旧档已访问空岛的补发/回填仍未实现**，该Issue继续开放。此修复不会把第32天测试档安装回正常档，也不承诺自动补出旧空岛坐骑。

## 证据与阶段

Source/Native逐阶段审查与新凭据分开；既有未变测试复用，增量host、真实两轮截图、完整16文件/receipt/cold/manifest及模型公开事件留本机 `.local/tasks/map-detail-visual-20261020/` 和 `.local/tasks/mount32-fresh-validation-20261019/native-a41-mapshipfinal/`、`native-a39-mapreentry/`。诊断器、原始玩家资料和interop不提交GitHub。最终Native独审与GLM合并安装审查完成后，按#271→更新并合并#273收尾；此记录写入时尚未合并、安装，安装回执另记。
