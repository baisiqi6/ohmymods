# ICall exact-signature probe run — 2026-10-24

- Operator self-reviewed batch（用户 2026-10-24 全权授权，不等待 Codex）；源改动 commit `60fa5ec`（两处既有 catch 的 `#if ANDROID` 分支追加 `MissingIl2CppInternalCallException.Signature/Message`，PC 日志逐字不变）。
- Case `controlled-candidate133-icall-signature-isolated-20261024-single1`；plan `cff3f574…023ab8`；gate `ff4ce1e1…4f99` 消费一次；window `07:50:00–08:10:00Z`，lease `09:45:07Z`。
- 候选 `aac037c0c06f81b5609be686f968196dd619903b94fc22bf6e52be11e140c54b`（2,276,352 B，Android net6 Release，408 输入构建前后 hash 保持、`BepInExPluginsPath=` 未部署）；cfg16b/609e/F35fae 未变。

## 运行事实

| 项 | 值 |
|---|---|
| controller / evidenceReportable | exit `0` / `true`；elapsed 239.2s |
| 候选与观察 | `mainHashState=candidate`、`controlValid=true`、READY、island/campaign/game=8、`Playing/2`、无同船 NRE、UNKNOWN 0 |
| 产品 InitPools | 同段 `native InitPools returned count=109` |
| Root/资源 | postflight `fullOriginal/sourceThree=true`、reap proven、root inputs 0/PNGs 0、baseline PNG 1；无 qemu 残留、端口空闲、ADB 85922 保持 |
| postrun F | READ 260337B/SHA `35faea07…`（READ not SAVE） |

## 精确运行期 Signature（本批目标）

`actual/observed.log:197`：`[HeavyShieldRuntime] attach held: MissingIl2CppInternalCallException sig=UnityEngine.Renderer::get_forceRenderingOff_Injected msg=ICall with signature UnityEngine.Renderer::get_forceRenderingOff_Injected was not resolved`

`actual/observed.log:215`：`[CoinCourierTeleportFx] effect group create failed: MissingIl2CppInternalCallException sig=UnityEngine.LineRenderer::set_useWorldSpace_Injected msg=ICall with signature UnityEngine.LineRenderer::set_useWorldSpace_Injected was not resolved`

结论：两处运行时 Signature 已实读，与静态报告的首个不可解析调用候选一致；运行期未出现第三个 `sig=` 行。此为事实采集，不是 producer 修复或 Native 兼容验收。

## 证据

| 证据 | SHA256 |
|---|---|
| result | `cb9a0f9d56916ff1e868c19021c6a5abf7c33273e2a83408dca95c10779157cf` |
| fresh/observed log | `8610603e557ceaaf077449afb29bb906d4e34114a497fb883b3c277892915d0b` |
| root postflight | `a4f1d2d8dc2704e69d15a85b2bfe69e0e68184cf564e161914c5e80b32bf4820` |
| canonical readback | `b0a30dd2f04a48b6f2163044801712947f86791fa3b65dd7fcc30d592259ec87` |
| spent gate | `ff4ce1e1af78ea88387e5a9b1c645ddb7643b1f83439a8e2a47781d7e8f74f99` |
| baseline PNG | `1c6ad18c076138b2df94a4e69aa61deb2b58cfcaa0db9840b92e4e0830e3406e` |
| postrun F | `35faea071dd59332d61e80e9136ffa2c190f8bbb76f00e4e21cca169f4580fb5` |

## 下一步（producer 修复，未开始）

- HeavyShield：`forceRenderingOff` 为首个缺失；同族仍有 `GetPropertyBlock/SetPropertyBlock`、`Time.unscaledTimeAsDouble` 等静态缺失，须在设计替换时一并核对，不能只删首调用。
- CoinCourierTeleportFx：`LineRenderer.set_useWorldSpace` 为首个缺失；现有 LineRenderer 可用面不足（positionCount/widthCurve/useWorldSpace 等），需要按表现契约选择 mesh/替代实现，保留时序/几何/颜色与 Android 边界，PC 不变。
- 修复后须新 build + 新单次 case 复验，并保持同一段 R2 判定；视觉/玩法/手机仍另验。
