# 当前 2.4 原生入口核对

只读实际独立测试副本的 interop / GameAssembly.dll，见 input-hashes.json、native-addresses.json、native-disassembly.txt。inspect-damage.ps1 由 PowerShell 7 执行成功；首次旧 Windows PowerShell 的空输出失败不能用作证据。解码每方法最多 2048 字节，长方法不能据此宣称全函数行为已核完。

- Damageable.OnDisable 与 Reset 指向同一个 RVA 0x9bcbd0（两个 method slot），本轮不 detour 这两个方法，也不新增任何原生生命周期/getter detour。
- 三参数 ReceiveDamage（token 100667240，RVA 0x9bce60）将参数交给虚方法四参数入口（token 100667241，RVA 0x9bcf00）。继续调用三参数入口可保留派生类的原生处理。
- 四参数入口已解码前段保留 authority / active 检查，事件回调位于后续无敌字段判断之前。共同 helper 不提前过滤 invulnerable，不替换护盾/事件、不直接写 HP。
- 生命序号采用自有无 Update 的 IL2CPP marker，通过真正 GO 生命周期分配；当前组件注入是否在真实池复用中按预期收到消息，仍须游戏验证。只禁用 marker 或活性读取失败造成的观察缺口明确拒绝额外 AoE，直到完整可信生命周期恢复。
- 实际 UnityEngine.Physics2DModule metadata 包含 ContactFilter2D + Il2CppSystem.Collections.Generic.List<RaycastHit2D> 的 Linecast overload，以及 queriesHitTriggers getter。真实接口编译验证是独立门，不能替代游戏内 List AOT 调用验证。

本轮不操作游戏进程或存档，不运行此前自动审批拒绝的英雄/弩手诊断修改。
