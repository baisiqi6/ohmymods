# 独立复审

内置 `/root/archer_reviewer` 最终只读确认：登记与预算计数已移至生成成功后、初始化前；缺body时保留账本并停止本批；旧Retire已删除，新增异常缓存测试存在。本轮代码复审无剩余阻断。启用后的实际玩法效果仍需实测。

此前阻断全部落实：关闭散射保留live预算、读失败不丢账、逐箭核对实际biome-resolved依赖和pool余量、初始ptr/InstanceID绑定恢复回执、身份未知重试、死亡来源不增箭、Update成功路径Finalizer建模。视觉world/layer/scene清理、失败预算退避和部分构建资源归属通过。

native/实际interop静态复核：六个目标均单槽长方法；共享16B ShouldPlayerControl只调用不hook。Harmony参数匹配、Pool.Spawn<Arrow>实际已有AOT实例、每箭bool+impulse消息与原生链一致。Root独立受控IL2CPP启动及存档/配置/银行保持证据见acceptance.md。
