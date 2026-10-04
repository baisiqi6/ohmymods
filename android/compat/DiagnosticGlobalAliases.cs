// Diagnostic-only (compile) aliases for android/compat/CompileInventory.csproj — NOT part of the
// mod runtime surface and not compiled into the Android probe (which has no Rewired consumer).
//
// Rewired: the only qualified consumer in the shared sources is PatchLifecycle_MenuInput.cs
// (`Rewired.ReInput`); the main Android 36-source surface has zero ReInput call sites, so this
// alias deliberately stays out of android/GlobalAliases.cs (approved B0 condition A correction).
// Target namespace Il2CppRewired lives in the already-referenced Il2CppRewired_Core.dll interop.
global using Rewired = Il2CppRewired;
