# Actual E 2.4 rendering API scout

Read-only Cecil inspection of the installed E-copy BepInEx/interop/UnityEngine.CoreModule.dll and Assembly-CSharp.dll. No game or runtime methods executed. The 2.1 decompiled BaseSpriteFX/SpriteRendererFX is behavior reference, not proof of identical 2.4 native internals.

## Confirmed sorting failure and narrow fix

Renderer.set_sortingLayerName(string) is an unstripped managed wrapper calling MemoryExtensions.AsSpan -> ReadOnlySpan<char>.GetPinnableReference -> ManagedSpanWrapper -> injected setter. This is the exact dependency named by the observed MissingMethodException.

Renderer.get_sortingLayerID(), set_sortingLayerID(int), get_sortingOrder(), set_sortingOrder(int) are normal il2cpp_runtime_invoke wrappers. Use lr.sortingLayerID = source.sortingLayerID and integer sortingOrder. Do not use sortingLayerName or sorting layer name conversion. No runtime upgrade is required by this source-level workaround.

## Afterimage API paths

Normal native invoke wrappers verified:
- SpriteRenderer.sprite get/set
- SpriteRenderer.color get/set
- SpriteRenderer.flipX get/set
- Renderer.sharedMaterial get/set
- Renderer.sortingLayerID and sortingOrder get/set
- MaterialPropertyBlock parameterless constructor and Clear()
- Material.HasProperty(int), Material.GetColor(int)
- Shader.PropertyToID(string): normal native invoke plus ManagedStringToIl2Cpp, not the broken span shim

Available generated ICall paths, not direct native invoke wrappers:
- SpriteRenderer.flipY get/set -> *_Injected delegate, registered by ResolveICall in the type initializer
- MaterialPropertyBlock.SetColor(int, Color) / GetColor(int) -> SetColorImpl/GetColorImpl -> BindingsMarshaller.ConvertToNative (native invoke) -> *_Injected ICall delegate
- Renderer.GetPropertyBlock/SetPropertyBlock(MaterialPropertyBlock) -> Internal_*PropertyBlock -> *_Injected ICall delegate
- MaterialPropertyBlock.HasColor(int)/HasProperty(int) -> HasVectorImpl/HasPropertyImpl -> *_Injected ICall

These examined paths do not invoke GetPinnableReference and did not contain an UnstripException body. Actual ICall resolution and visible output still require controlled runtime validation. Include all chosen helpers in the final transitive API audit rather than equating method presence with compatibility.

## Native white-overlay property

Actual Assembly-CSharp exposes BaseSpriteFX.SPOverlay as a static int getter backed by il2cpp_field_static_get_value. No _OverlayID member was found in BaseSpriteFX/SpriteRendererFX. The reference BaseSpriteFX.Awake assigns SPOverlay = Shader.PropertyToID("_Overlay"). Treat a local variable named OverlayID as an integer ID for the shader property _Overlay, not evidence that the property name is _OverlayID.

Prefer the already initialized native SPOverlay, or compute Shader.PropertyToID("_Overlay") once. Material.HasProperty(int) can validate the selected source material. Do not mutate source/shared Material; pass white overlay via each new renderer's own MaterialPropertyBlock. Whether a particular material uses the property to produce the intended white silhouette and multiplies vertex alpha as expected remains a visual acceptance item.

## Approved smallest ownership design

Use four self-owned SpriteRenderers: three frozen-pose ghosts plus a fourth overlay that follows the live source and is enabled only during the current motion's invulnerable burst. The original renderer's material, color and MPB remain untouched.

- Create only the small fixed number of helper objects, not copies of the Knight gameObject, components, Animator, collider, or sync state.
- Reuse the source sharedMaterial by reference and use self-owned MPBs for _Overlay white. Do not call source.material (can instantiate), and never change sharedMaterial fields.
- Copy the actual sprite frame, flipX/flipY, world position, world rotation, and signed world scale. Root localScale.x sign is facing; do not lose it or double-apply it with flipX.
- Freeze each ghost's pose at capture; a root that follows the owner would drag historic ghosts unless their world pose is retained/reapplied. The fourth overlay follows the owner each frame.
- Ghost alpha near/middle/far starts .45/.25/.10 and fades over about .2 scaled seconds. Alpha is separate from the full-white overlay tint; do not multiply alpha twice unintentionally.
- Use integer sortingLayerID/sortingOrder, with a deliberate stable relation to the source. Same order/layer alone can give ambiguous overlapping transparency order.
- Motion ownership must be revalidated for Begin/Tick/End. Burst expiration, distance completion, external takeover, config/style/authority loss, OnDisable and pooled reuse must hide the fourth overlay immediately. Only self-owned ghost trails may continue their bounded fade if the agreed lifecycle permits.
- A helper exception must not alter movement, hit logic or invulnerability cleanup. No per-frame GameObject/material allocation, scene/resource searches, new native Dispose/OnDestroy hooks, or class injection is needed if updated by the existing motion driver.

## Why native GlowOverlay is not a precise burst lifetime primitive

The reference BaseSpriteFX.GlowOverlay stops/replaces its single _overlayRoutine. That same slot is used by indefinite FireAttacks glow and other flashes. A .6-second scheduled glow does not stop when a motion terminates earlier. ClearOverlayRoutines clears fade and recolor routines too; StopIndefiniteGlowOverlay only toggles the indefinite flag and does not cancel ordinary GlowOverlay. Therefore do not schedule native glow and later broadly clear SpriteFX to match invulnerability.

Source-MPB override was considered but not selected: MPB has no per-property Remove. Restoring a missing Overlay with a material color leaves an override that can mask later native glow; restoring the whole saved block can erase external concurrent MPB writes. The self-owned fourth renderer avoids both conflicts.
