# Issue225 / PR226 当前主线集成验证（2026-10-15）

正常Mac累计911b已装商店库存迁址/照明，本轮将原PR226接入release `01b3b3d1bf3b4ea3146c6a907dbf975390f7cf4e`，保留已合入的钻石、双岛、弯月地图、字体性能与233/236/238保存/平台改动。progress历史双方保留；identity测试工程的合并冲突保留真实HashBuffer及已有DisabledCustomShopStocks边界两依赖。Android只增3个真实stock/transit/lighting Compile，不加注册/设置/替身。

本轮真实组合：公共net8/net6、Android实际interop D/R全部0警告0错误；完整Adapter对实际Release DLL1264/0。source-linked测试：库存迁址213检查/28场景，照明201，真实shop runtime372，shield identity136+92、save30+76、runtime913，火枪identity348/restock72/defense127、native-save-adapter41与真实producer33，courier persistence54/bridge223；全部通过。13个suite在clean tracked镜像执行，其已记录source/fixture输入逐SHA与最终W对应。

一次前置断言失败后shell误继续造成旧ddc基线构建，保留在pr226/build并标注NOT_FINAL_BASELINE；当前有效组合构建在build-current。courier首次调用因任务runner全局覆盖DefaultItemExcludes卷入子工程失败，取消该错误命令参数后原项目/产品不变，54/0；保留失败日志。未放宽oracle或加入产品兜底凑绿。

本机证据`.local/tasks/release-consolidation-20261015/pr226/`。本轮不启动游戏或再次安装；商店重新上架后的自然领取、真实破墙迁址、四店夜间照明观感、Windows/Android设备仍由玩家后测，未宣称通过。按最新用户授权，源码审查验证且对应PR合并后关闭代码Issue；后续现象另Issue/PR。旧Draft/待实机不关单记录属于历史阶段。
