# Android 引擎 API 缺口全量扫描与修复计划 — 2026-10-25

- 触发：gameplay 批次前确认“当前 Android 构建产物是否仍存在会执行到却缺 ICall 的引擎成员”。Worker F（只读）对候选 DLL `/…/visual-family-20261025/root-build/release/OhMyMods.AndroidProbe.dll`（SHA `b1c9b623…acfa`，2,281,472 B）做全量 MemberRef 扫描；报告与可复现脚本在 `.local/tasks/android-profession-archery-20261014/dsh-workers-20261025b/api-sweep/`（本地证据，不入库）。
- pin：`libil2cpp.so` `774871434d39fe5145f05c9dc51101d973f0bf7dc63bba0f062d7c0848104157`；`global-metadata.dat` `ef1cc0cc…ae19`（v31，17,547 types / 129,838 methods）；interop 生成输入与两 pin SHA 一致。
- 扫描覆盖：候选 MemberRef 全表 3,930 条（3,893 有 IL 调用点）；分类：loader/Il2CppInterop 托管 176；UnityEngine 无 native 分发 348（316 hollow-dump-resolved + 4 纯托管 + 28 struct/字段）；引擎 native ICall 分发 25（17 已注册 + **8 缺口**）；范围外其它托管 3,381。`.so` 归一化注册名 2,106。

## 已知缺失族（零引用复核）

`forceRenderingOff`、`Renderer.Get/SetPropertyBlock`、`SpriteRenderer.flipY`、`Time.unscaledTimeAsDouble`、`Texture2D.GetPixels32/GetPixels/SetPixels32`、`LineRenderer.useWorldSpace` 在候选 IL MemberRef 与 DLL 原始字节中**全部 0 引用**（不是不可达分支，而是完全无引用），与既有 `AndroidUnityCompat`/`AndroidTexturePixels`/HeavyShield skip/mesh ribbon 处理一致。

## 确认缺口 8 条（native 名 0/1=0 且 pinned metadata 方法定义缺失）

| # | 成员 | 调用点（Android 编译集） | 可触发性 | 处理状态 |
|---|---|---|---|---|
| P1 | `Physics2D.GetIgnoreCollision` | `HeroArcherWallPierce.cs:245/:404`（弩矢/英雄箭穿墙；`CrossbowmanBoltWallPierce` 经 Apply） | 当前 cfg 开启的弩/英雄箭路径；catch 后永久 fail-closed ⇒ 穿墙静默失效 | **已修（分支 `dsh/engine-api-gaps-wallpierce-20261025` @ `e90423f`，待集成）**：`#if ANDROID` 跳过缺失读、按“本模块唯一 IgnoreCollision 写者”显式契约复用既有 PierceLedger（先记账后写、只归还已认领 pair、Probe 重探 PC 保留），首次触发有界 info；PC 逐字不变 |
| P2 | `Animator.GetFloat(string)` | `HeavyShieldRuntime.cs:688` 的 `GetFloat("Speed")` | 已有 velocity fallback，仅异常开销 | **已修（同上分支）**：Android 用缓存 `Animator.StringToHash("Speed")` + `GetFloat(int)`；PC 字符串重载逐字 |
| P3 | `Physics2D.Linecast(ContactFilter2D, List<RaycastHit2D>)` | `MusketeerCombat.cs:1504`（256 饱和回退） | 仅 List 满 256 才可达 | 修复中（Worker H）：改用已验证数组/NonAlloc 路径；`LinecastArray_Internal_Injected`=1 |
| P4 | `Physics2D.OverlapArea(…, List<Collider2D>)` | `WallEngineerRuntime.cs:1271` | 需 WallEngineer 开关（activation4 未开） | 修复中（Worker H）：`OverlapBoxArray_Internal_Injected`=1 |
| P5 | `Collider2D.ClosestPoint` | `PatchArcher_GreekImpact.cs:1363` | 需 GreekImpact 开关（未开） | 修复中（Worker H）：`Bounds.ClosestPoint_Injected`+`Collider2D.get_bounds_Injected`=1，近似并记录语义差 |
| P6 | `Animator.get_speed` | `PatchRoles_DeadlandsPowers`（不在当前 Android ResolveOwners / HarmonyDontPatchAll） | 当前不可达 | 仅记录 |
| P7 | `Animator.GetCurrentAnimatorClipInfo` | 同上 | 当前不可达 | 仅记录 |
| P8 | `AnimatorClipInfo.clip` | 同上（另 `il2cpp/ModPanel.cs` 不在 Compile 集；Android ModPanel shim 仅 IsShown） | 当前不可达 | 仅记录 |

## 修复分支证据（Worker G，P1/P2）

- 仅本地 commit，未 push/merge：`dsh/engine-api-gaps-wallpierce-20261025` @ `e90423f`（base `1b9bf32`），4 文件 +77/−4；Android 编译闸 exit 0（产物 `d5223bec…e04568`，2,281,984 B，未部署）；产物 MethodRef 对 `Physics2D.GetIgnoreCollision`、`Animator.GetFloat(string)` 0 引用；pin 名称串 0/0/1/1/1 自证；P1 Android 契约 harness 22/22 PASS；PC 1144 断言不变；Android 5→6。
- P1 已接受语义差：不可读既有 ignore 状态按未知=false（本模块是编译集内唯一 IgnoreCollision 写者；游戏原生侧写入静态不可完全排除，低概率残余风险需实机观察）；info 每 world 世代有界一次。
- P3–P5 分支尚未完成；P6–P8 不修，待 DeadlandsPowers/采集面板移植决策时再评估。

## 下一步

1. 集成 G/H → 构建新候选（G-only 已使候选 SHA 变为 `d5223bec…`；H 合并后再次变化）→ 全量受影响 managed 套件 + Android 编译闸 + 新候选的 MemberRef 缺口复扫。
2. 候选变化后按既有链重建 gameplay addon binding / review / pins / case（不能只改单个常量），用新候选重跑 gameplay readiness 作为回归。
3. 实机/玩法正例与画面验收仍单列；Issue #232/#262 保持 open，PR #263 保持 Draft。
