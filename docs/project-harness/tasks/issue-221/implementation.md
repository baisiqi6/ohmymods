# Issue 221 — Android 神器参数兼容

基线 d355d93b，沿用 LemonLoader/MelonLoader、Il2CppInterop、Harmony。共享 Hermes/Artemis 两源增加 ANDROID 分支；玩家公共中文页面新增默认关闭的“神器参数增强”，小浮球不变。首次开启需重启注册，关闭从下一次判定生效。

法杖 state=0 的原生单趟调用借转化上限32；神弓 DamageAffectedEnemies 调用借范围候选上限20。结束只归还仍由本次调用写入的值，不覆盖其它写入者，原异常原样返回。FriendlyTroll 仅在离线、当前世界和主控有效时阻止恢复敌对判定。原生追加冷却保持，范围×2未移植；其它神弓伤害分支保持，不称整箭固定20次伤害。已有 Android 法杖基础冷却仍独占 _itemCooldown。未引入 PC 范围字典、Awake永久写值或 SettingChanged场景扫描；无新对象、池、RPC、驱动、重试或状态镜像。默认OFF51目标，启用重启53（其它分组另计）。

2026-10-14 恢复：原未提交工作区路径不再存在，原因未定。通过原公开编辑记录，在独立工作区恢复17份已审文件，所有SHA逐字节一致；未执行历史设备/Git/canonical动作。重新真实 Android/PC编译0警告0错误，9配置/产物模式1114/0或954/0，生产wrapper边界57/0；PC1318类型、6934方法及37资源全语义MATCH，允许差异0。新旧Android DLL全语义MATCH，构建路径影响PE/MVID，不声称DLL字节相同。84强源哈希保持。

前次私有0.0.30包已一次保数据安装；默认OFF51和中文控件已观察。两次旧有限窗分别108.486s与140.864s在截图30s时效门退出，未发设置切换，原30配置/存档保持；失败保留，不称完成。开启/关闭冷启动仍待续。

17文件源码恢复和原审查证明代码边界；只有3个已选原生函数3628B/FDE/metadata证据，未读callee主体，不视同址唯一为detour安全/玩法证明。当前owner层级、真实转化/候选命中/容量32、读档换岛池重建、手机平板、联机和长期行为仍待验。Issue保持开放，canonical本轮尚未注册，新窗口需独立交接。

原始命令/日志/哈希保留在本机 .local/tasks/android-divine-parameters-20261011/，本轮恢复证据在 resume-20261014/。不上传游戏、loader、APK、interop或密钥。
