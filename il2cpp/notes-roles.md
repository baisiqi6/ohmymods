# 角色域迁移笔记（roles）

迁移目标：把 UMM+Harmony1.2 Mono 实现（2.1.0 GOG）迁到 Steam 2.4.0 IL2CPP（BepInEx 6 + HarmonyX）。
本组只添加 `PatchRoles_*.cs`（7 个）+ 本文件，未改动其他文件。

## 一、文件清单与功能

| 文件 | 功能 | 关键 Hook |
|---|---|---|
| PatchRoles_Holder.cs | 跨世界角色通用登记 + 希腊 Worker/Peasant 换北境 | Holder.InitializeTagCharacterPairs postfix |
| PatchRoles_Castle.cs | 希腊忍者/狂战士商店队列 + 工具池/角色池 + CreateItem 安全产出 | Castle.CatchupToLevel / ReQueueAllBuildings postfix；PayableShop.CreateItem prefix |
| PatchRoles_Knight.cs | 狂战士跨世界跟随骑士 | Knight.TryRecruitAdditionalFollowers prefix |
| PatchRoles_Character.cs | 希腊乞丐变北欧平民 | Character.Promote(string, IUnitController) prefix |
| PatchRoles_Worker.cs | 单位缩放注册表（Worker/Deer/Critter/Berserker/Peasant）+ 盾牌/拾取 | Worker/Mover/WarriorPeasant/Deer/Critter/Peasant OnEnable/Update postfix |
| PatchRoles_World.cs | 希腊世界自动生成草地 | World.OnLevelLoaded postfix |
| PatchRoles_BeggarCamp.cs | 乞丐生成间隔 90 秒 | BeggarCamp.Awake postfix |

## 二、2.4.0 签名验证汇总（interop Assembly-CSharp.dll，ilspycmd 8.2）

所有 Hook 点类/方法均存在。逐项核对结果已写进各文件头部注释，此处只列**签名差异**。

## 三、版本漂移（2.1.0 Mono → 2.4.0 IL2CPP）与处理

| # | 项 | 2.1.0 | 2.4.0 | 处理 |
|---|---|---|---|---|
| 1 | Castle.CatchupToLevel | 无参 | `(bool includePrevious)` | postfix 忽略新参数，无需改 |
| 2 | ShopPlanner.QueueNewShopForPlacement | `(ShopType, Side)` | `(ShopType, Il2CppSystem.Nullable<Side> side = null)` | 省略 side，靠 ShopType 左右编码推导（见待决策 #1） |
| 3 | ShopPlanner.HasPlacedShop | `(ShopType)` | `(ShopType, GameObject go = null)` | 传 type + go |
| 4 | ShopPlanner._placedShops | `GameObject[]` | `Il2CppReferenceArray<GameObject>` | 下标访问一致 |
| 5 | PayableShop.CreateItem | `(bool)` 返回 Droppable | `(bool blink = true)` 返回 Droppable | 签名一致 |
| 6 | SpriteRendererFX.BlinkOverlay(Color) | 存在 | **已移除**（改 BlinkRoutine/FlashRoutine 协程） | 本地闪烁省略，保留 Droppable.SendBlinkRequest（见待决策 #2） |
| 7 | Physics2D.OverlapArea(2+int) | 3 参存在 | **3 参移除** | 改 5 参 `(a,b,mask,minDepth,maxDepth)`，深度传 ±∞ |
| 8 | Character._skinColor/_outfitColor | 私有字段（反射） | 公开属性 `skinColor/outfitColor` | 直接赋值，免反射 |
| 9 | Character.PickOutfitColor | `(string, Color?)` | `(string, Il2CppSystem.Nullable<Color> = null)` | 省略第二参 |
| 10 | BiomeHolder.biomePathStrings | `string[]` | `Il2CppStringArray` | 下标访问一致 |
| 11 | World._grass | `HashSet<Grass>`（反射） | `ICollection<Grass>` | 改用 `World.HasGrass()` 公开方法 |
| 12 | World.worldBounds | `Bounds` 风格 | `Sided<float>`（.left/.right） | 字段访问一致 |
| 13 | Resources.LoadAll\<T\> | `T[]` | `Il2CppArrayBase<T>` | 下标/长度一致 |
| 14 | PoolManager.cachedPools/cachedNamePoolPairs/cachedSyncIdPoolPairs | 私有（反射） | 公开属性 | 直接读写，免反射 |
| 15 | Knight._additionalFollowers | 私有（反射） | 公开 `List<Berserker>` | 直接 Add，免反射 |
| 16 | Worker.npcShieldUser / NpcShieldUser.regenWait | 私有（反射） | 公开字段 | 直接读写，免反射 |
| 17 | List\<T\>.Sort(Comparison\<T\>) | .NET 委托 | Il2CppSystem.Comparison 委托 | Knight 改用托管 List 排序，避开委托转换 |
| 18 | ConditionalWeakTable\<Mover,\> | 弱引用自动清理 | Il2CppObjectBase 包装身份不稳定 | 改托管 Dictionary\<int,float\>，键 = GetInstanceID()，挂 ScaleRegistryHolder |

## 四、ScaleRegistryHolder（自定义 MonoBehaviour）

`PatchRoles_Worker.cs` 内定义 `ScaleRegistryHolder : MonoBehaviour`，按 docs §5.3 三步用**原生** `Il2CppInterop.Runtime.Injection.ClassInjector` 注册（无 SharedLib）：
1. `public ScaleRegistryHolder(IntPtr ptr) : base(ptr) {}` IntPtr 构造
2. `EnsureCreated()` 里 `ClassInjector.RegisterTypeInIl2Cpp(typeof(ScaleRegistryHolder))`（幂等，先查 `IsTypeRegisteredInIl2Cpp`）
3. `new GameObject(...)` + `DontDestroyOnLoad` + `AddComponent<ScaleRegistryHolder>()`

每帧恢复仍由 `Mover.Update` postfix 完成（保证每个 Mover 写回 localScale 之后立即恢复，避免 Update 顺序问题）；Holder 只持注册表。

## 五、编译

```bash
cd C:/Users/ADMIN/Projects/ohmymods/il2cpp
C:/Users/ADMIN/dotnet8/dotnet.exe build -c Debug \
  -p:BaseOutputPath=bin/roles/ \
  -p:BaseIntermediateOutputPath=obj/roles/ \
  -p:DefaultItemExcludes='**/obj/**'
```

- 结果：**0 警告 0 错误**。
- 注意：三个 worker 并行共用同一 csproj，`BaseIntermediateOutputPath=obj/<组>/` 会让默认通配 `**/*.cs` 把**其他组** `obj/<组>/Debug/*.cs`（生成的 AssemblyInfo.cs / MyPluginInfo.cs）也编进来，导致 `CS0579/CS0101/CS0229` 重复定义。必须额外加 `-p:DefaultItemExcludes='**/obj/**'`（只影响默认 glob，不影响 SDK/BepInEx 显式 `<Compile Include>` 生成的 AssemblyInfo/MyPluginInfo）。

## 六、待 Operator 决策清单

1. **QueueNewShopForPlacement 的 side 省略**：2.4.0 side 变 `Nullable<Side>` 且默认 null。本组推断 `NinjaLeft/NinjaRight/ShieldShopLeft/ShieldShopRight` 已把左右编码进 ShopType，省略 side 后由游戏内部 `GetSidedShopSide(type)` 推导。若实测商店摆错边，改传 `new Il2CppSystem.Nullable<Side>(Side.Left/Right)`（需先确认 `Il2CppSystem.Nullable<T>` 约束是否放行 `Side` 枚举——ilspycmd 显示 `where T : new()`，疑似 Cpp2IL 失真，编译期未验证显式构造）。
2. **CreateItem 本地闪烁省略**：2.4.0 `SpriteRendererFX.BlinkOverlay(Color)` 已移除（`BlinkRoutine`/`FadeOverlayRoutine` 为 protected 协程）。本组只保留 `Droppable.SendBlinkRequest(color)`（联网同步）。若需本地闪烁视觉，需另寻公开触发点（如 `FlashRoutine`/`Fade`），属纯外观，不影响功能。
3. **OverlapArea 深度边界**：5 参重载的 minDepth/maxDepth 传 `float.NegativeInfinity/PositiveInfinity`（等效无深度限制，与原 3 参一致）。属等价替换，建议实测草地生成是否正常避开 NotGrassable。
4. **ScaleRegistryHolder 键用 GetInstanceID**：托管 Dictionary 键为 GameObject instanceID，OnEnable 重新登记可自愈 ID 复用；若观察到缩放串位，可改键为 `mover.Pointer`（IntPtr）。
