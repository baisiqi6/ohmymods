using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomEnhancedMod;

// R6 KingdomPipeline 排序契约（source-only 只读验证器；单文件、无 hook、无 MonoBehaviour、无 driver、无周期扫描、
// 无写入、无重试、无静态缓存、不持 Native 引用）。
//
// 固定 API：internal static class PalaceKingdomSortContract / internal static bool TryRead(Camera camera,
// SpriteRenderer unit, out int firstPassMask, out string reason)。
// 调用点（仅 Root）：既有 exact private-12 / 已核 P1 camera / camera-unit 同 scene / owner 边界通过之后调用一次。
// 本文件不复核 game/land/scene/owner 身份、不重选相机、不选单位、不遍历 sky、不映射 Z、不触碰 plane/renderer/unit/
// sky/global；Root 保留其既有 clip/pivot/有效 plane 检查，GraphicsSettings.global 透明模式不作为该自定义 pipeline
// 的前提，本文件不读。成功后 Root 用 firstPassMask 做 PalaceEnvironmentSortPlanes 的 Z 映射，并自行按 registeredMoon
// 检查 P1 sky 成员：凡在 cameraMask 可见的成员层必须同属 firstPassMask、且不在 second pass；cameraMask 不可见的
// Moon1 不作为 P1 参照（上述 sky/Z/身份检查均不在本文件）。
//
// 证据（当前已闭合，均为只读）：
//  - receipts/r6-kingdom-pipeline-sort-readonly-r1.md：Native RendererListDesc 覆写 criteria=0x17(CommonTransparent)；
//    converter 用 descriptor 的 Camera 构造 SortingSettings，不另设 distance metric；RenderMainCam first pass =
//    cameraMask & ~settings+0x28；second pass 仅当 settings+0x93（renderReflections）为真时直接使用 +0x28。
//  - receipts/r6-pipeline-sort-cold-r1-result.json：实测 reflectionMask offset=0x28 value=0x20000000、
//    renderReflections offset=0x93 True、distanceMetric=1(Orthographic)、w2c 3×3=diag(1,1,-1)、row3=(0,0,0,1)、
//    cameraPosition 有限、identity 全匹配。当前值只作证据，不在本文件硬编码：未来 mask/开关取别的值也必须通过同一结构检查。
//
// 只读范围与判定：
//  - 一次冻结（每个 getter/属性只读一次，全部 local）：RenderPipelineManager.currentPipeline / s_CurrentPipeline /
//    s_CurrentPipelineAsset、KingdomRenderPipeline._Inst / _rendering、global::Rendering.Inst、
//    GraphicsSettings.currentRenderPipeline、settings.renderPipeline。三条指针等式必须同时成立；另做 exact-class
//    KingdomRenderPipeline 核对（子类可能覆写 Render，pinned Native body/hash 只对 exact class 成立）。任一缺失/不符
//    → false + 明确 reason，不按名字替换、不 fallback、不选别的实例、不写 global。
//  - reflectionMask / renderReflections 用 typed wrapper getter 各读一次，并各自做一次 runtime offset map
//    （NativeFieldInfoPtr_<exactName> → il2cpp_field_get_offset）；offset 必须分别为 0x28 / 0x93，与 pinned Native
//    consumer 一致，否则 pinned body 不适用 → false（只检查，不写入）。
//  - firstPassMask = camera.cullingMask & ~reflectionMask（纯 int）；unit 的 GO layer 必须 0..31，cameraMask 与
//    firstPassMask 都必须包含该 layer；renderReflections==true 且 reflectionMask 含 unit layer → 拒绝（否则该单位会被
//    reflection pass 再绘制一次）。
//  - camera 必须存活且 orthographic，forward 严格为世界 (0,0,1)；local new SortingSettings(camera) 的 distanceMetric
//    必须为 Orthographic（本 wrapper 数值 1，优先 typed enum），其 worldToCameraMatrix 3×3 必须严格 diag(1,1,-1)、
//    offdiag=0、row3=(0,0,0,1)、translation 有限，cameraPosition 各分量有限。ctor 的 criteria 不读、不比较：Native
//    消费者的 0x17 覆写已静态核，ctor 输出不是最终 Native 值。
//  - 异常只在 TryRead 调用边界捕获一次 → reason=unknown；不重试、不复用缓存。
//  - 日志 ≤3 行、prefix [PalaceKingdomSortContract]（经 PalaceCourtVisuals.Log 输出），只含只读数值与已知字符串。
//  - 未编译/未运行/未修复（Root 统一 copy/wire/build）；Native 视觉（实机夜图）验收另计。
internal static class PalaceKingdomSortContract
{
    private const string Tag = "[PalaceKingdomSortContract] ";
    private const int MaxLines = 3;

    // pinned Native RenderMainCam 实际读取的两个 settings 偏移（receipts/r6-kingdom-pipeline-sort-readonly-r1.md）；
    // 只用于复核当前 wrapper 布局与 pinned body 一致，不代表这里断言其语义归属。
    private const long PinnedReflectionMaskOffset = 0x28L;
    private const long PinnedRenderReflectionsOffset = 0x93L;

    /// <summary>
    /// Root 在已核创建边界调用一次：成功返回 true 且 firstPassMask = camera.cullingMask &amp; ~reflectionMask；
    /// 失败返回 false 且 reason 为具体原因（异常路径为 unknown）。只看不写，无重试、无静态缓存。
    /// </summary>
    internal static bool TryRead(Camera camera, SpriteRenderer unit, out int firstPassMask, out string reason)
    {
        firstPassMask = 0;
        reason = "unknown: TryRead did not record a reason";
        int lines = 0;
        try
        {
            int computed;
            if (!TryReadCore(camera, unit, ref lines, out computed, out reason)) return false;
            firstPassMask = computed;
            return true;
        }
        catch (Exception e)
        {
            firstPassMask = 0;
            reason = "unknown: contract read fault (" + e.GetType().Name + "): " + e.Message;
            Log(ref lines, "reject: " + reason + "; read-only call boundary, no retry, no writes");
            return false;
        }
    }

    private static bool TryReadCore(Camera camera, SpriteRenderer unit, ref int lines, out int firstPassMask, out string reason)
    {
        firstPassMask = 0;

        if (camera == null || !(bool)camera)
        {
            reason = "camera missing/null/destroyed";
            return Reject(ref lines, reason);
        }
        if (unit == null || !(bool)unit)
        {
            reason = "unit missing/null/destroyed";
            return Reject(ref lines, reason);
        }
        GameObject unitGo = unit.gameObject;
        if (unitGo == null)
        {
            reason = "unit gameObject missing";
            return Reject(ref lines, reason);
        }

        // 相机形态：sorting metric 沿 camera forward，映射 Z 前 forward 必须严格为世界 +Z（分量精确相等）。
        if (!camera.orthographic)
        {
            reason = "camera is not orthographic";
            return Reject(ref lines, reason);
        }
        Vector3 forward = camera.transform.forward;
        if (forward.x != 0f || forward.y != 0f || forward.z != 1f)
        {
            reason = "camera forward is not exactly (0,0,1): " + Vec3(forward);
            return Reject(ref lines, reason);
        }

        // 一次冻结：以下每个 getter/属性只读一次，后续判断全部使用这些 local。
        KingdomRenderPipeline activePipeline = KingdomRenderPipeline._Inst;
        global::Rendering settings = KingdomRenderPipeline._rendering;
        global::Rendering settingsInst = global::Rendering.Inst;
        RenderPipeline currentPipeline = RenderPipelineManager.currentPipeline;
        RenderPipeline sCurrentPipeline = RenderPipelineManager.s_CurrentPipeline;
        RenderPipelineAsset sCurrentAsset = RenderPipelineManager.s_CurrentPipelineAsset;
        RenderPipelineAsset graphicsAsset = GraphicsSettings.currentRenderPipeline;

        bool pipelineMissing = activePipeline == null || activePipeline.Pointer == IntPtr.Zero
            || currentPipeline == null || currentPipeline.Pointer == IntPtr.Zero
            || sCurrentPipeline == null || sCurrentPipeline.Pointer == IntPtr.Zero;
        bool pipelineMatch = !pipelineMissing
            && currentPipeline.Pointer == activePipeline.Pointer
            && sCurrentPipeline.Pointer == activePipeline.Pointer;
        if (!pipelineMatch)
        {
            reason = "pipeline identity not established (missing=" + pipelineMissing + " currentPipeline="
                + Ptr(currentPipeline) + " s_CurrentPipeline=" + Ptr(sCurrentPipeline) + " _Inst=" + Ptr(activePipeline) + ")";
            return Reject(ref lines, reason);
        }

        // exact-class 核对：pinned Native body/hash 只对 exact KingdomRenderPipeline 成立。
        IntPtr expectedClass = Il2CppClassPointerStore<KingdomRenderPipeline>.NativeClassPtr;
        if (expectedClass == IntPtr.Zero)
        {
            reason = "KingdomRenderPipeline class pointer store not initialized";
            return Reject(ref lines, reason);
        }
        IntPtr actualClass = IL2CPP.il2cpp_object_get_class(currentPipeline.Pointer);
        if (actualClass != expectedClass)
        {
            reason = "active pipeline class is not exact KingdomRenderPipeline (class=" + PtrHex(actualClass)
                + " expected=" + PtrHex(expectedClass) + "); subclass may override Render";
            return Reject(ref lines, reason);
        }

        bool settingsMissing = settings == null || settings.Pointer == IntPtr.Zero
            || settingsInst == null || settingsInst.Pointer == IntPtr.Zero;
        bool settingsMatch = !settingsMissing && settings.Pointer == settingsInst.Pointer;
        if (!settingsMatch)
        {
            reason = "settings identity not established (_rendering=" + Ptr(settings) + " Rendering.Inst="
                + Ptr(settingsInst) + " missing=" + settingsMissing + ")";
            return Reject(ref lines, reason);
        }

        var settingsAsset = settings.renderPipeline;
        bool assetMissing = settingsAsset == null || !(bool)settingsAsset
            || graphicsAsset == null || !(bool)graphicsAsset
            || sCurrentAsset == null || !(bool)sCurrentAsset;
        bool assetMatch = !assetMissing
            && settingsAsset.Pointer == graphicsAsset.Pointer
            && settingsAsset.Pointer == sCurrentAsset.Pointer;
        if (!assetMatch)
        {
            reason = "pipeline asset identity not established (settings.renderPipeline=" + Ptr(settingsAsset)
                + " GraphicsSettings.currentRenderPipeline=" + Ptr(graphicsAsset) + " s_CurrentPipelineAsset="
                + Ptr(sCurrentAsset) + " missing=" + assetMissing + ")";
            return Reject(ref lines, reason);
        }

        // 版本 pin：pinned Native RenderMainCam 读取 settings+0x28 / +0x93；offset 不符即拒（只检查，不写入）。
        long reflectionMaskOffset = FieldOffset("reflectionMask");
        long renderReflectionsOffset = FieldOffset("renderReflections");
        if (reflectionMaskOffset != PinnedReflectionMaskOffset || renderReflectionsOffset != PinnedRenderReflectionsOffset)
        {
            reason = "settings layout differs from pinned Native consumer (reflectionMask offset="
                + OffText(reflectionMaskOffset) + " expected 0x28; renderReflections offset="
                + OffText(renderReflectionsOffset) + " expected 0x93)";
            return Reject(ref lines, reason);
        }

        Log(ref lines, "association ok: currentPipeline=" + Ptr(currentPipeline) + " s_CurrentPipeline="
            + Ptr(sCurrentPipeline) + " _Inst=" + Ptr(activePipeline) + " exactClass=True settings=" + Ptr(settings)
            + " Rendering.Inst=" + Ptr(settingsInst) + " asset=" + Ptr(settingsAsset)
            + " GraphicsSettings.currentRenderPipeline=" + Ptr(graphicsAsset) + " s_CurrentPipelineAsset="
            + Ptr(sCurrentAsset) + " offsets=0x28/0x93");

        // 关联与布局通过后：只读实际值（typed wrapper getter，各一次）。
        LayerMask reflectionMask = settings.reflectionMask;
        bool renderReflections = settings.renderReflections;
        int cameraMask = camera.cullingMask;
        int reflectionMaskValue = reflectionMask.value;
        int firstPass = cameraMask & ~reflectionMaskValue;

        int unitLayer = unitGo.layer;
        if (unitLayer < 0 || unitLayer > 31)
        {
            reason = "unit GameObject.layer out of range: " + unitLayer;
            return Reject(ref lines, reason);
        }
        int unitBit = 1 << unitLayer;
        if ((cameraMask & unitBit) == 0)
        {
            reason = "camera cullingMask " + Hex(cameraMask) + " does not include unit layer " + unitLayer;
            return Reject(ref lines, reason);
        }
        if ((firstPass & unitBit) == 0)
        {
            reason = "first pass mask " + Hex(firstPass) + " excludes unit layer " + unitLayer
                + " (cameraMask=" + Hex(cameraMask) + " reflectionMask=" + Hex(reflectionMaskValue) + ")";
            return Reject(ref lines, reason);
        }
        if (renderReflections && (reflectionMaskValue & unitBit) != 0)
        {
            reason = "renderReflections=true and reflectionMask " + Hex(reflectionMaskValue) + " includes unit layer "
                + unitLayer + "; unit would also be drawn by the reflection pass";
            return Reject(ref lines, reason);
        }

        // Actual sorting contract：local ctor 输出（不写 camera/global）；criteria 不读，Native 0x17 覆写已静态核。
        var sorting = new UnityEngine.Rendering.SortingSettings(camera);
        if (sorting.distanceMetric != UnityEngine.Rendering.DistanceMetric.Orthographic)
        {
            reason = "SortingSettings(camera).distanceMetric=" + (int)sorting.distanceMetric
                + " is not Orthographic (wrapper value 1); sorting axis unproven";
            return Reject(ref lines, reason);
        }

        Matrix4x4 w2c = sorting.worldToCameraMatrix;
        if (w2c.m00 != 1f || w2c.m11 != 1f || w2c.m22 != -1f
            || w2c.m01 != 0f || w2c.m02 != 0f || w2c.m10 != 0f || w2c.m12 != 0f
            || w2c.m20 != 0f || w2c.m21 != 0f
            || w2c.m30 != 0f || w2c.m31 != 0f || w2c.m32 != 0f || w2c.m33 != 1f)
        {
            reason = "SortingSettings(camera).worldToCameraMatrix is not exactly diag(1,1,-1)+translation: rows "
                + Row(w2c.m00, w2c.m01, w2c.m02, w2c.m03) + " " + Row(w2c.m10, w2c.m11, w2c.m12, w2c.m13) + " "
                + Row(w2c.m20, w2c.m21, w2c.m22, w2c.m23) + " " + Row(w2c.m30, w2c.m31, w2c.m32, w2c.m33);
            return Reject(ref lines, reason);
        }
        if (!Finite(w2c.m03) || !Finite(w2c.m13) || !Finite(w2c.m23))
        {
            reason = "worldToCameraMatrix translation not finite (m03=" + F(w2c.m03) + " m13=" + F(w2c.m13)
                + " m23=" + F(w2c.m23) + ")";
            return Reject(ref lines, reason);
        }
        Vector3 cameraPosition = sorting.cameraPosition;
        if (!Finite(cameraPosition.x) || !Finite(cameraPosition.y) || !Finite(cameraPosition.z))
        {
            reason = "SortingSettings(camera).cameraPosition not finite: " + Vec3(cameraPosition);
            return Reject(ref lines, reason);
        }

        firstPassMask = firstPass;
        reason = "ok";
        Log(ref lines, "ok: cameraMask=" + Hex(cameraMask) + " reflectionMask=" + Hex(reflectionMaskValue)
            + " renderReflections=" + renderReflections + " firstPassMask=" + Hex(firstPass) + " unitLayer=" + unitLayer
            + " metric=Orthographic(1) w2c=diag(1,1,-1) row3=(0,0,0,1) translation="
            + Vec3(new Vector3(w2c.m03, w2c.m13, w2c.m23)) + " cameraPos=" + Vec3(cameraPosition)
            + "; single read, no retry, no writes, no refs retained");
        return true;
    }

    /// <summary>
    /// 每个字段一次 runtime offset map：只经 generator 已解析的 NativeFieldInfoPtr_&lt;exactName&gt; 取 fieldInfo，
    /// 再查询 offset；缺失返回 -1（据此拒绝，不替换别名、不读裸 instance+offset）。
    /// </summary>
    private static long FieldOffset(string exactName)
    {
        FieldInfo field = typeof(global::Rendering).GetField("NativeFieldInfoPtr_" + exactName,
            BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null) return -1L;
        object raw = field.GetValue(null);
        IntPtr fieldInfo = raw is IntPtr ptr ? ptr : IntPtr.Zero;
        if (fieldInfo == IntPtr.Zero) return -1L;
        return (long)IL2CPP.il2cpp_field_get_offset(fieldInfo);
    }

    private static bool Reject(ref int lines, string reason)
    {
        Log(ref lines, "reject: " + reason + "; read-only, no fallback, no writes");
        return false;
    }

    /// <summary>有界日志：每次调用最多 MaxLines 行（association + ok/reject），超出即丢弃后续行。</summary>
    private static void Log(ref int lines, string message)
    {
        if (lines >= MaxLines) return;
        lines++;
        PalaceCourtVisuals.Log(Tag + message);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static string F(float value) => value.ToString("R");

    private static string Vec3(Vector3 v) => "(" + v.x.ToString("R") + "," + v.y.ToString("R") + "," + v.z.ToString("R") + ")";

    private static string Row(float a, float b, float c, float d)
        => "[" + a.ToString("R") + "," + b.ToString("R") + "," + c.ToString("R") + "," + d.ToString("R") + "]";

    private static string Hex(int value) => "0x" + value.ToString("x8");

    private static string Ptr(Il2CppObjectBase value) => value == null ? "none" : value.Pointer.ToInt64().ToString();

    private static string PtrHex(IntPtr value) => "0x" + value.ToInt64().ToString("x");

    private static string OffText(long offset) => offset < 0 ? "unknown(missing NativeFieldInfoPtr)" : "0x" + offset.ToString("x");
}
