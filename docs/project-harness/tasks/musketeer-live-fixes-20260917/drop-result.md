# 火铳手 Drop 边界 worker 结果（drop-worker，2026-09-17）

> Operator最终更正：生产实现使用本次Droppable.Drop的显式source参数（Harmony `__0`），不是对象上可能残留的`dropper`字段。测试增加null source+stale dropper，身份273断言及实际interop元数据审计通过。Nullable继承审计检查完整ValueType→Object→Il2CppObjectBase链，未执行游戏构造器或实机受击。下文是worker初版诊断记录；最终实现与验证以此更正及receipts为准。

状态：**代码与测试已改（本 worker 只有 read/edit/write：未构建、未运行测试、未安装、未启动游戏、未提交）**。
Operator 需跑 `tests/musketeer-identity`、`tests/musketeer-drop-interop` 和完整构建；实机验证见 §6。

## 0. 结论（decision-first）

1. **实机 3 次 `NullReferenceException at DMD<Character::DropItem>(Character this, Nullable`1 direction)` 是我们新加的 `Character.DropItem` Harmony detour 自身在调用原生方法体之前抛出的**，不是原生方法体内部空引用；detour 还把每次原生 `DropItem(null)` 调用整个吞掉（枪根本不会掉）。
2. 根因机制（对着实际随包版本 Il2CppInterop 1.5.1 / HarmonyX 2.10.2 源码逐段核对，见 §2）：`DropItem` 的唯一参数是 `Il2CppSystem.Nullable<Vector2>`；原生调用点传空 Nullable（`Grab()` / 受击死亡路径都调用 `DropItem(null)`）；HarmonyX 的 IL2CPP trampoline 把空指针合法地转成托管 `null`，但**补丁方法体是 interop stub 的副本**，它对“值类型→引用包装”的参数用 `Il2CppObjectBaseToPtrNotNull` 转换，该方法对 `null` 直接 `throw new NullReferenceException()`（`obj?.Pointer ?? throw ...`）。异常在生成的 `DMD<...>` 包装里抛出，堆栈只有两帧、没有任何用户帧——与实机日志完全一致。
3. **修复 = 去掉 `Character.DropItem` detour**（任务书授权的首选路径）；身份转移改到**已有的 `Droppable.Drop(GameObject,Vector2,Vector2,PickUpPolicy,bool,bool,bool)` postfix**：`Character.DropItem` 内部就是 spawn 出原生工具后立即调用这个 7 参重载（2.1 逻辑说明书 `Character.cs` DropItem；2.4 interop 该重载存在且本 mod 早已挂接），`dropper` 恰是本单位、被 drop 的恰是那只枪——**无近邻猜测、无计数分配、无原生压缩存档写入**，与旧 `__result` 转移的语义一一对应。
4. 顺带修正的语义：非 Bow 工具的 drop 不再“释放职业”（旧 `DropItem` postfix 的 drop-not-bow 分支）；现在职业留在单位身上，单位真正消失时仍由既有 despawn/sweep 释放为预约——更保守且不会在单位还活着时丢 owner。

## 1. 实机证据（冻结日志）

`tasks/musketeer-live-fixes-20260917/LogOutput.log`（build=8.0.0-musketeer-20260916，50aa3037）：

```
[Error  :Il2CppInterop] During invoking native->managed trampoline
Exception: System.NullReferenceException: Object reference not set to an instance of an object.
   at DMD<Character::DropItem>(Character this, Nullable`1 direction)
   at (il2cpp -> managed) DropItem(IntPtr , IntPtr , Il2CppMethodInfo* )
```
共 3 次，集中在夜间抓人/击杀窗口（t≈173.7 / 174.5 / 175.8）。同一日志里其它 `[Musketeer]` 事件（gun->unit、saved:records=4）正常，说明模块整体在工作，只有这一个边界每次都失败。

## 2. 机制证据链（逐条有可核对来源）

1. **`DMD<...>` 是 HarmonyX 生成的补丁方法名**（HarmonyX issue #82 的 `Generated patch (... DMD<...>?<random>::...)` 格式）；`(il2cpp -> managed) X(IntPtr, IntPtr, Il2CppMethodInfo*)` 是 HarmonySupport 的 native→managed trampoline（`Il2CppDetourMethodPatcher.GenerateNativeToManagedTrampoline`，方法名拼接可见于该 DLL 字符串）。两帧都只在 `DropItem` 被 patch 时存在；`Musketeer_DroppedGun_Patch` 是本构建里对 `Character.DropItem` 的唯一 patch。
2. **trampoline 的空参数是安全的**：`EmitConvertArgumentToManaged` → `EmitCreateIl2CppObject` 先 `Brtrue`，空指针走 `ldnull`（HarmonySupport v1.5.1 源码，实测该 DLL 版本 1.5.1+c6d7d3bb）。
2b. **`Il2CppSystem.Nullable<Vector2>` 是引用包装（本地日志即可证明）**：`TrampolineHelpers.NativeType()`（Il2CppInterop v1.5.1 `Injection/TrampolineHelpers.cs`）只在“x64 且 `IsSubclassOf(Il2CppSystem.ValueType)`”之外的分支返回 `IntPtr`；冻结日志的 trampoline 签名 `DropItem(IntPtr , IntPtr , Il2CppMethodInfo* )` 第二个参数就是 `IntPtr`，即该 nullable 是 `Il2CppObjectBase` 派生类（而非按值传递的 struct）。同一个类的 `(IntPtr)` 包装对 0 指针会抛 NRE（本仓库坑 15 同源，见 Il2CppInterop issue #182）。
3. **补丁方法体（stub 副本）对 nullable 参数不安全**：`CopyOriginal()` 复制 interop stub；stub 由 `Pass50GenerateMethods` 生成参数转换，`originalType.IsValueType()`（`System.Nullable<Vector2>` 是值类型）且重写类型是引用包装（上一条）时走 `EmitObjectToPointer` 的 `Il2CppObjectBaseToPtrNotNull` 分支（该分支无空值保护；`allowNullable` 只作用于引用类型分支，对值类型→引用转换不生效）。`Il2CppObjectBaseToPtrNotNull(Il2CppObjectBase obj) => obj?.Pointer ?? throw new NullReferenceException();`（Il2CppInterop v1.5.1, `IL2CPP.cs`）。
4. **空 Nullable 在原生侧就是真 null 指针**：Il2CppInterop issue #182 原文 “On the native side, null nullables are genuinely null (reference type pointing at IntPtr.Zero)”，且 `Il2CppSystem.Nullable<T>` 是 `Il2CppObjectBase` 派生包装（issue #240 展示其生成构造器；本仓库坑 15 也是同一 `CreateGCHandle` NRE）。
5. **游戏调用点确实传 null**：`game-source/Assembly-CSharp-2.1.0/Character.cs` 的 `Grab()`（`this.DropItem(null)`）与 `HandleOnReceiveDamage`（`if (this.dropItem != null) this.DropItem(null)`）；2.4 interop 签名同一形状（`actual-interop.txt`: `METHOD Droppable Character::DropItem(Il2CppSystem.Nullable`1<UnityEngine.Vector2>) PARAMS direction`）。
6. 返回值路径不是问题：`EmitPointerToObject` 对返回指针有 `Brtrue` 空值保护（stub 返回 null → 托管 null）。

**置信度与未声称**：上述链条（原生传 null → trampoline 合法转成托管 null → patch 方法体里的 stub 副本用 `Il2CppObjectBaseToPtrNotNull` 抛 NRE → 原生方法体根本没执行）由实机帧 + 随包同版本源码逐段对上，其中“nullable 是引用包装”由日志自己的 trampoline 签名（`IntPtr`）独立证明；**但没有做游戏内单变量复现**（需实际触发一次抓取并对照有/无该 detour 的日志，见 §5）。任务书所述“此前构建没有这个错误”按“此前没有对 `DropItem` 的 detour”解释，不据此推断原生方法体行为。

## 3. 改动

| 文件 | 改动 |
|---|---|
| `il2cpp/MusketeerIdentity.cs` | 删除 `Musketeer_DroppedGun_Patch`（`[HarmonyPatch(typeof(Character), "DropItem")]` postfix）及 `OnDropItem`；`OnDroppableDrop(Droppable)` 增加第二步 `TryTransferUnitDrop(droppable, tool)`：`TrackAllowed` + `droppable.dropper` 非空 + 该 root 未被职业占用 + dropper 是当前绑定的**单位**职业 + 掉落物 `tag=="Bow"` → `Bind(career, RootOf(droppable.gameObject,true), null,null,tool,NoStockSlot)`，日志键不变（`unit->gun`），失败仍 `drop-bind-failed`。货架撤销 `ReleaseStock` 行为不变。原 patch 位置留注释说明为何禁止再挂 `Character.DropItem`。 |
| `tests/musketeer-identity/Stubs.cs` | `Droppable` 增加 `dropper` 字段；`HarmonyPatch` 替身属性改为**记录** `TargetType/TargetMethod`（供 tripwire）。 |
| `tests/musketeer-identity/Program.cs` | 新增 `DropBoundaryTests()`：反射扫描被测程序集里所有 `HarmonyPatch` 目标，断言不存在 `(Character, "DropItem")` detour（防止重新引入）；`TransferTests`/`StockTests` 的转移用例改为在 `Droppable.Drop` 边界驱动（dropper=付费单位，断言旧单位身份结束、精确落枪、无货架位），并新增：无 dropper 不转移、陌生 dropper 不转移、失权不转移、非 Bow 掉落职业留在单位；owner 计数断言相应变为 4 units + 1 gun。 |
| `tests/musketeer-drop-interop/**`（新，可选 audit 工程） | Mono.Cecil 只读检查 E 盘 2.4 interop：`Character.DropItem` 唯一参数是 `Il2CppSystem.Nullable`1<UnityEngine.Vector2>`；该包装是引用类型且派生 `Il2CppObjectBase`；**实际 stub IL 用 `Il2CppObjectBaseToPtrNotNull` 转换 direction**（打印该指令）。缺 interop 时 exit code 2 = 未验证。运行：进入该目录 `C:/Users/ADMIN/dotnet8/dotnet.exe run`（或传 BepInEx 目录/KEM_IL2CPP_DEPS）。 |

契约影响（Operator 拥有 `contracts.md`）：identity 小节里 “`Character.DropItem` postfix exactdroppedBow transfer” 这条已被本任务授权替换为 “`Droppable.Drop` 7 参 postfix 的 exact dropper→dropped tool 转移”；其余导出面未变。

## 4. 测试与验证（Operator 执行）

1. `tests/musketeer-identity`：`dotnet run`（原有 267 断言 + 本次新增 5 条：1 条 DropItem detour tripwire、4 条 drop 边界行为；2 条既有转移用例改为新边界驱动）。重点：`old unit identity ends…`、`no Harmony detour on Character.DropItem`。
2. `tests/musketeer-drop-interop`：`dotnet run`（必须 PASS；“direction conversion … Il2CppObjectBaseToPtrNotNull” 一行是本次诊断的落地证据；若 FAIL，先回报实际 IL，不要凭本文档继续）。
3. 完整实际 2.4 构建 0W0E（编译核对 `droppable.dropper` 属性与 `MusketeerIdentity` 无悬空引用）。

## 5. 实机验收要点（构建后，未做）

- 触发一次付费火铳手被 Troll **抓起**或**受击降级**：日志应出现 `[Musketeer] unit->gun`；不再出现 `DMD<Character::DropItem>` 的 NRE；枪应正常掉落（detour 没吞掉原生调用）。
- 付费单位死亡/撤离后：`records` 保留、`bound` 相应减少（预约），不出现误绑他人。

## 6. 限制与残留风险

- **2.4 `DropItem` 是否仍走 7 参 `Droppable.Drop`**：2.1 逻辑如此且 2.4 该重载存在（既有货架撤销 patch 也是挂它），但未在 2.4 机内证明。若实机出现“付费单位掉枪但无 `unit->gun`、职业变预约”，说明 2.4 走了 2 参重载；fallback 需另定边界（任务书允许的 `Character.Demote()` 长边界，或同时挂 2 参重载但 2 参没有 dropper 信息）。
- **非 Bow 掉落语义**：以前会立刻释放职业，现在保留在单位上直到其真实消失；这是有意的保守选择。
- **`Droppable.dropper` 只在传入非空时写入**（2.1 语义），同一活体对象理论上可能带着旧 dropper 再被 drop；现有两道门（dropper 必须是当前活绑定单位、目标对象必须尚未被职业占用）限制影响面，未观察到需要更严门的需求。
- **离线限制不变**：`TrackAllowed=false` 时不转移、不释放（与旧 `OnDropItem` 相同）。
- 本 worker 未修改存档/配置/DLL/游戏，未 commit。身份最终复审缺口仍按任务记录保留。
