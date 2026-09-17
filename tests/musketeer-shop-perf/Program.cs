using System;
using System.Collections.Generic;
using KingdomEnhancedMod;

// 火铳铺购买路径性能契约（生产 MusketeerShopAssets.MusketeerBowCachePolicy 原样编译）：
//  * 支付路径只读缓存，绝不发现/加载资源（DiscoveryAttempts 不变）；
//  * 世界/PoolManager/biome/池重建任一身份变化 → 旧捕获不可用（不会用陈旧 prefab 生成）；
//  * 缓存不可用 = 付款门直接拒绝（先拒绝再谈收币，原生退款路径不被触发）；
//  * 重试有界（每 1.5s 至多一次），前置条件恢复后可安全重新捕获。

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    checks++;
}

static bool Gate(MusketeerBowCachePolicy policy, in MusketeerBowCachePolicy.Context context, Func<string, long> resolve)
    => policy.HasBinding && policy.TryUse(in context, resolve);

var policy = new MusketeerBowCachePolicy();
var ctx = new MusketeerBowCachePolicy.Context
{ WorldReady = true, PoolManager = 71L, World = 72L, Biome = 73L, BiomeIndex = 5 };
var pools = new Dictionary<string, long>();
long Resolve(string name) => pools.TryGetValue(name, out long value) ? value : 0L;

// 1) 未绑定时不能服务付款，读操作绝不发现。
Check(!policy.HasBinding, "starts unbound");
Check(!policy.TryUse(in ctx, Resolve), "unbound capture unusable");
Check(!Gate(policy, in ctx, Resolve), "unbound gate blocks payment");
Check(policy.DiscoveryAttempts == 0, "reads do not discover");

// 2) 有界重试：同一窗口至多一次，跨窗口才允许下一次。
Check(policy.TryBeginAttempt(0f), "first attempt allowed");
Check(!policy.TryBeginAttempt(0.5f), "attempt inside window rejected");
Check(!policy.TryBeginAttempt(1.49f), "attempt just before window rejected");
Check(policy.TryBeginAttempt(1.5f), "attempt at window boundary allowed");
Check(policy.DiscoveryAttempts == 2, "attempts counted, still bounded");

// 3) 前置条件出现后捕获成功，同一读取立即生效。
pools["ToolBow"] = 900L;
Check(!policy.TryUse(in ctx, Resolve), "still unbound before capture");
policy.Capture(500L, 1234, "ToolBow", 900L, in ctx);
Check(policy.HasBinding, "capture binds");
Check(policy.MatchesPrefab(500L, 1234), "capture matches prefab identity");
Check(!policy.MatchesPrefab(500L, 999), "instance id guards address reuse");
Check(!policy.MatchesPrefab(501L, 1234), "prefab pointer must match");
Check(policy.TryUse(in ctx, Resolve), "capture usable in exact context");
Check(Gate(policy, in ctx, Resolve), "gate serves payment from the capture");

// 4) 反复购买只读缓存：不触发任何发现/加载计数。
for (int i = 0; i < 500; i++) Check(policy.TryUse(in ctx, Resolve), "repeat use " + i);
Check(policy.DiscoveryAttempts == 2, "payment-path reads never discovered");
Check(!policy.TryBeginAttempt(100f), "a bound policy refuses discovery");

// 5) 世界/PoolManager/biome 任一身份变化 → 旧捕获不可用。
var moved = ctx;
moved.World = 999L;
Check(!policy.TryUse(in moved, Resolve), "world change invalidates capture");
moved = ctx;
moved.PoolManager = 70L;
Check(!policy.TryUse(in moved, Resolve), "pool manager change invalidates capture");
moved = ctx;
moved.Biome = 74L;
Check(!policy.TryUse(in moved, Resolve), "biome identity change invalidates capture");
moved = ctx;
moved.BiomeIndex = 3;
Check(!policy.TryUse(in moved, Resolve), "biome index change invalidates capture");
moved = ctx;
moved.WorldReady = false;
Check(!policy.TryUse(in moved, Resolve), "unreadable context invalidates capture");

// 6) 池重建：同名解析到新池实例（或池消失）都不是旧捕获。
pools["ToolBow"] = 901L;
Check(!policy.TryUse(in ctx, Resolve), "rebuilt pool is not the capture");
pools.Remove("ToolBow");
Check(!policy.TryUse(in ctx, Resolve), "missing pool is not the capture");
pools["ToolBow"] = 900L;
Check(policy.TryUse(in ctx, Resolve), "the exact original pool still matches");

// 7) 显式失效后：重新发现可以先立即开始，但连续失败仍受 1.5s 窗口限速；重新捕获后恢复资格。
policy.Invalidate();
Check(!policy.HasBinding && !policy.TryUse(in ctx, Resolve), "invalidate drops capture");
Check(policy.TryBeginAttempt(2.9f), "rediscovery may start immediately after invalidation");
Check(!policy.TryBeginAttempt(3.9f), "failed rediscovery stays rate-limited");
Check(policy.TryBeginAttempt(4.4f), "next window allows one more attempt");
policy.Capture(501L, 4321, "ToolBow", 900L, in ctx);
Check(policy.TryUse(in ctx, Resolve), "fresh capture is usable");
Check(policy.MatchesPrefab(501L, 4321) && !policy.MatchesPrefab(500L, 1234), "capture replaced wholesale");

// 8) 捕获本身 fail-closed：任何缺失身份都不绑定（付款继续被拒）。
var strict = new MusketeerBowCachePolicy();
var cold = ctx;
cold.WorldReady = false;
strict.Capture(1L, 1, "ToolBow", 1L, in cold);
Check(!strict.HasBinding, "capture needs a ready context");
strict.Capture(0L, 1, "ToolBow", 1L, in ctx);
Check(!strict.HasBinding, "zero prefab rejected");
strict.Capture(1L, 1, "", 1L, in ctx);
Check(!strict.HasBinding, "empty prefab name rejected");
strict.Capture(1L, 1, "ToolBow", 0L, in ctx);
Check(!strict.HasBinding, "zero pool rejected");
Check(!Gate(strict, in ctx, Resolve), "failed capture keeps payment blocked");

// 9) 上下文不可读 + 无人发现：连续读取也不会产生发现（无逐帧全场扫描）。
var blocked = new MusketeerBowCachePolicy();
bool served = false;
for (int i = 0; i < 1000; i++) served |= blocked.TryUse(in cold, Resolve);
Check(!served, "unreadable context never serves payment");
Check(blocked.DiscoveryAttempts == 0, "cold reads never discover");

// 10) 长期速率上界：29.9s 内 0.1s 步进只允许每 1.5s 一次 = 20 次。
var rate = new MusketeerBowCachePolicy();
int allowed = 0;
for (int step = 0; step < 300; step++)
    if (rate.TryBeginAttempt(step * 0.1f)) allowed++;
Check(allowed == 20, "retry rate capped at one per retry window");

// 11) 生产接线不变式（本轮真实缺陷的回归）：prefab 身份必须来自单一来源——
//     GameObject 指针。组件包装器（DroppableTool.Pointer）与 GameObject 指针是不同 native 指针；
//     捕获与校验混用两种来源时，捕获必须被判为不可用（修复前 TryDiscoverBow 捕获 GO 指针、
//     BowUsable 却校验组件指针，导致任何真实购买都过不了门）。
var identity = new MusketeerBowCachePolicy();
pools["ToolBow"] = 900L;
long goPointer = 500L;         // 生产：prefab.gameObject.Pointer
long componentPointer = 501L;  // 生产：prefab.Pointer（组件包装器，另一个 native 指针）
identity.Capture(goPointer, 1234, "ToolBow", 900L, in ctx);
Check(identity.MatchesPrefab(goPointer, 1234), "capture consumed the GameObject identity");
Check(!identity.MatchesPrefab(componentPointer, 1234), "component wrapper is not the captured identity");
Check(identity.TryUse(in ctx, Resolve), "same-source identity validates");
var mixed = new MusketeerBowCachePolicy();
mixed.Capture(componentPointer, 1234, "ToolBow", 900L, in ctx);
Check(!mixed.MatchesPrefab(goPointer, 1234), "cross-source identity is rejected (the fixed bug)");

// 12) 自检失败后的限速：DeferRetry 只向前推窗口、不回退，重复失败不会逐帧重扫。
var defer = new MusketeerBowCachePolicy();
Check(defer.TryBeginAttempt(0f), "defer: attempt one");
defer.DeferRetry(0.5f, 1.5f);
Check(!defer.TryBeginAttempt(1.0f), "defer holds the window open longer");
Check(defer.TryBeginAttempt(2.0f), "deferred window boundary allows an attempt");
defer.Invalidate();
defer.DeferRetry(5f, 1.5f);
Check(!defer.TryBeginAttempt(6.4f), "deferred invalidation stays rate-limited");
Check(defer.TryBeginAttempt(6.5f), "deferred invalidation window boundary");
Check(defer.DiscoveryAttempts == 3, "DeferRetry itself is not a discovery attempt");

Console.WriteLine($"PASS {checks} musketeer shop-perf policy assertions");
