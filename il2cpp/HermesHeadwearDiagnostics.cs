using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace KingdomEnhancedMod;

/// <summary>
/// Hermes 头饰被动诊断：只由既有 core/visual 事件点调用，输出有界的 <c>[HermesHeadwearDiag]</c> 行
/// （前缀后为缩写键，每字段单独 try，读失败记 unknown；异常不影响转化）。
///
/// 预算（每进程固定）：12 个采样对象 = 8 个头饰样本（choice&gt;=0）+ 4 个 miss 样本（choice&lt;0）；
/// 每样本最多 2 条 visual 快照（初次 apply 与 first-tick 共用，失败重试不额外开额度）+ 1 条 removed。
/// 额度检查先于任何原生读取与字符串构造；未采样对象、额度/快照用尽的重复调用零字符串构造。
/// 身份 = GameObject InstanceID + GameObject/组件原生指针，同 GOid 换指针算新世代（池复用）；
/// 原生池以完全相同的身份复用但旧样本已 Removed 时按新一只重新采样（新额度，旧额度不释放），
/// 匹配从最新样本往前，Visual/Removed 永远落在当前世代。诊断世代，不是业务身份。
/// 只读：只用 sharedMaterial（绝不碰会实例化的 material），不写对象、不扫场景、无新钩子/Tick。
///
/// 接线：core 抽选后 Decision(troll, state.Choice, state.HostEnabled)（未命中也调用，choice&lt;0 是
/// 配额未命中（quota-miss）/功能关闭的正常结果，不是故障）；ApplyVisual 成功/失败与视觉 Tick 处
/// Visual(...)；Drop/撤除处 Removed(troll, choice, reason)。观测不是根因修复：日志不证明屏幕实际可见性。
/// </summary>
internal static class HermesHeadwearDiagnostics
{
    private const string Prefix = "[HermesHeadwearDiag]";
    private const int MaxHeadwearSamples = 8;
    private const int MaxMissSamples = 4;
    private const int MaxVisualLines = 2;
    private const int MaxUnattributedRemoved = 4; // 身份完全读不到时的 Removed minimal 行上限

    /// <summary>一个「新转化世代」的采样记录；只放 int/IntPtr/bool，绝不持有 Unity 对象。</summary>
    private sealed class Sample
    {
        internal int Id;
        internal IntPtr Pointer;      // GameObject 原生指针
        internal IntPtr TrollPointer; // 组件原生指针（销毁后仍可从包装对象读回）
        internal int Choice;
        internal bool Headwear;
        internal bool HostEnabled;
        internal bool MaskIndexKnown;
        internal int MaskIndex;
        internal int VisualLines;
        internal bool Removed;
    }

    private static readonly List<Sample> Samples = new List<Sample>(MaxHeadwearSamples + MaxMissSamples);
    private static readonly StringBuilder Line = new StringBuilder(768);
    private static int _headwear, _miss, _visualOpen, _unattributedRemoved;

    /// <summary>新转化分配：同世代重复 Init 不重复记录；额度用尽直接返回（不读原生、不建串）。</summary>
    internal static void Decision(FriendlyTroll troll, int choice, bool hostEnabled)
    {
        try
        {
            bool headwear = choice >= 0;
            if (headwear ? _headwear >= MaxHeadwearSamples : _miss >= MaxMissSamples) return;
            if (!Identity(troll, out int id, out IntPtr pointer, out IntPtr trollPointer)) return;
            Sample existing = Find(id, pointer, trollPointer);
            if (existing != null && !existing.Removed) return; // 同世代重复 Init：沿用首条记录
            // 原生池可能以完全相同的 GOid/指针复用（旧样本已 Removed）：这是新一只，占新额度，旧额度不释放。

            var sample = new Sample { Id = id, Pointer = pointer, TrollPointer = trollPointer, Choice = choice, Headwear = headwear, HostEnabled = hostEnabled };
            try { sample.MaskIndex = troll._maskIndex; sample.MaskIndexKnown = true; } catch { }
            Samples.Add(sample);
            if (headwear) _headwear++; else _miss++;
            _visualOpen++;

            Line.Clear();
            Line.Append(Prefix).Append(" decision id=").Append(id).Append(" ptr=").Append(Hex(pointer))
                .Append(" choice=").Append(choice).Append(" headwear=").Append(headwear ? '1' : '0')
                .Append(" host=").Append(hostEnabled ? '1' : '0').Append(" nativeMaskIndex=");
            if (sample.MaskIndexKnown) Line.Append(sample.MaskIndex); else Line.Append("unknown");
            Line.Append(" hw=").Append(_headwear).Append('/').Append(MaxHeadwearSamples)
                .Append(" miss=").Append(_miss).Append('/').Append(MaxMissSamples);
            if (!headwear) Line.Append(" kind=").Append(hostEnabled ? "quota-miss" : "host-disabled");
            Emit();
        }
        catch { }
    }

    /// <summary>外观快照（own/head 可 null）：只对 Decision 已采样对象输出，每对象最多 2 条。</summary>
    internal static void Visual(FriendlyTroll troll, int choice, bool applied, SpriteRenderer own, Transform head, string phase)
    {
        try
        {
            if (_visualOpen <= 0) return; // 快照额度用尽：重复 Tick 在此退出，不碰原生
            if (!Identity(troll, out int id, out IntPtr pointer, out IntPtr trollPointer)) return;
            Sample sample = Find(id, pointer, trollPointer);
            if (sample == null || sample.Removed || sample.VisualLines >= MaxVisualLines) return;
            sample.VisualLines++;
            if (sample.VisualLines >= MaxVisualLines) _visualOpen--;

            Line.Clear();
            Line.Append(Prefix).Append(" visual id=").Append(id).Append(" ptr=").Append(Hex(pointer))
                .Append(" choice=").Append(choice).Append(" phase=").Append(phase ?? "?")
                .Append(" applied=").Append(applied ? '1' : '0')
                .Append(" snap=").Append(sample.VisualLines).Append('/').Append(MaxVisualLines);
            Line.Append(" own"); RendererField(own);
            Line.Append(" body"); BodyField(troll);
            Line.Append(" mask"); MaskField(troll, false);
            Line.Append(" tmpl"); MaskField(troll, true);
            Line.Append(" head"); HeadField(head);
            Emit();
        }
        catch { }
    }

    /// <summary>世代结束/外观撤除：已采样对象一条带 reason 的行，全取先前记录，不读销毁中的原生属性。</summary>
    internal static void Removed(FriendlyTroll troll, int choice, string reason)
    {
        try
        {
            if (Samples.Count == 0) return;
            Identity(troll, out int id, out IntPtr pointer, out IntPtr trollPointer);
            Sample sample = Find(id, pointer, trollPointer);
            if (sample != null)
            {
                if (sample.Removed) return;
                sample.Removed = true;
                int emitted = sample.VisualLines;
                if (sample.VisualLines < MaxVisualLines) { sample.VisualLines = MaxVisualLines; _visualOpen--; }

                Line.Clear();
                Line.Append(Prefix).Append(" removed id=").Append(sample.Id).Append(" ptr=").Append(Hex(sample.Pointer))
                    .Append(" choice=").Append(sample.Choice)
                    .Append(" headwear=").Append(sample.Headwear ? '1' : '0')
                    .Append(" host=").Append(sample.HostEnabled ? '1' : '0').Append(" nativeMaskIndex=");
                if (sample.MaskIndexKnown) Line.Append(sample.MaskIndex); else Line.Append("unknown");
                Line.Append(" visuals=").Append(emitted).Append('/').Append(MaxVisualLines)
                    .Append(" reason=").Append(reason ?? "?");
                Emit();
                return;
            }

            // 能读到任一身份说明不是我们的样本（静默）；完全读不到才输出有界 minimal 行。
            if (id != 0 || pointer != IntPtr.Zero || trollPointer != IntPtr.Zero) return;
            if (_unattributedRemoved >= MaxUnattributedRemoved) return;
            _unattributedRemoved++;
            Line.Clear();
            Line.Append(Prefix).Append(" removed id=unknown choice=").Append(choice)
                .Append(" reason=").Append(reason ?? "?").Append(" samples=").Append(Samples.Count);
            Emit();
        }
        catch { }
    }

    /// <summary>GOid + GameObject/组件指针；对象已销毁时退回包装对象自身持有的指针。</summary>
    private static bool Identity(FriendlyTroll troll, out int id, out IntPtr pointer, out IntPtr trollPointer)
    {
        id = 0;
        pointer = IntPtr.Zero;
        trollPointer = IntPtr.Zero;
        if (troll == null) return false;
        try { trollPointer = troll.Pointer; } catch { }
        try
        {
            GameObject owner = troll.gameObject;
            if (owner != null)
            {
                id = owner.GetInstanceID();
                pointer = owner.Pointer;
            }
        }
        catch { }
        return id != 0 || pointer != IntPtr.Zero || trollPointer != IntPtr.Zero;
    }

    /// <summary>从最新往前找（≤12）：同身份复活时 Visual/Removed 必须命中新世代的样本；
    /// GOid 与指针都读到且对不上 = 池复用新世代，不算旧样本。</summary>
    private static Sample Find(int id, IntPtr pointer, IntPtr trollPointer)
    {
        for (int i = Samples.Count - 1; i >= 0; i--)
        {
            Sample sample = Samples[i];
            if (Match(id, pointer, trollPointer, sample)) return sample;
        }
        return null;
    }

    private static bool Match(int id, IntPtr pointer, IntPtr trollPointer, Sample sample)
    {
        bool sameId = id != 0 && sample.Id != 0 && id == sample.Id;
        bool samePointer = Match(pointer, sample.Pointer) || Match(trollPointer, sample.TrollPointer);
        if (id != 0 && sample.Id != 0 && !sameId) return false;
        bool pointersComparable = (pointer != IntPtr.Zero || trollPointer != IntPtr.Zero)
            && (sample.Pointer != IntPtr.Zero || sample.TrollPointer != IntPtr.Zero);
        if (pointersComparable && !samePointer) return false;
        return sameId || samePointer;
    }

    private static bool Match(IntPtr left, IntPtr right) => left != IntPtr.Zero && left == right;

    // ------------------------------------------------------------------ 字段（单项 try）

    private static void RendererField(SpriteRenderer renderer)
    {
        if (renderer == null) { Line.Append("=null"); return; }
        Line.Append("[en="); Field(() => renderer.enabled ? "1" : "0");
        Line.Append(" act="); Field(() => Active(renderer.gameObject));
        Line.Append(" rgba="); Field(() => ColorText(renderer.color));
        Line.Append(" sprite="); Field(() => renderer.sprite != null ? renderer.sprite.name : "null");
        Line.Append(" mat="); Field(() => MaterialText(renderer.sharedMaterial));
        Line.Append(" layer="); Field(() => renderer.gameObject.layer.ToString(CultureInfo.InvariantCulture));
        Line.Append(" srt="); Field(() => Num(renderer.sortingLayerID) + "/" + Num(renderer.sortingOrder));
        Line.Append(" pos="); Field(() => VectorText(renderer.transform.position));
        Line.Append(" scale="); Field(() => VectorText(renderer.transform.lossyScale));
        Line.Append(" bnd="); Field(() => BoundsText(renderer.bounds));
        Line.Append(']');
    }

    private static void BodyField(FriendlyTroll troll)
    {
        try { RendererField(troll.GetComponent<SpriteRenderer>()); } catch { Line.Append("=unknown"); }
    }

    private static void MaskField(FriendlyTroll troll, bool prefab)
    {
        try { RendererField(prefab ? troll._maskPrefab : troll._mask); } catch { Line.Append("=unknown"); }
    }

    private static void HeadField(Transform head)
    {
        if (head == null) { Line.Append("=null"); return; }
        Line.Append("[act="); Field(() => Active(head.gameObject));
        Line.Append(" pos="); Field(() => VectorText(head.position));
        Line.Append(" scale="); Field(() => VectorText(head.lossyScale));
        Line.Append(']');
    }

    private static void Field(Func<string> read)
    {
        try { Line.Append(read()); } catch { Line.Append("unknown"); }
    }

    private static string Active(GameObject gameObject)
    {
        if (gameObject == null) return "null";
        return (gameObject.activeSelf ? "1" : "0") + "/" + (gameObject.activeInHierarchy ? "1" : "0");
    }

    private static string MaterialText(Material material)
    {
        if (material == null) return "null";
        Shader shader = material.shader;
        return shader != null ? material.name + "/" + shader.name : material.name;
    }

    private static string ColorText(Color color) =>
        Num(color.r, "F2") + "," + Num(color.g, "F2") + "," + Num(color.b, "F2") + "," + Num(color.a, "F2");

    private static string VectorText(Vector3 value) =>
        Num(value.x, "F3") + "," + Num(value.y, "F3") + "," + Num(value.z, "F3");

    private static string BoundsText(Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        return "c" + VectorText(center) + "/s" + VectorText(size);
    }

    private static string Hex(IntPtr pointer) => "0x" + pointer.ToInt64().ToString("x", CultureInfo.InvariantCulture);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(float value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static void Emit()
    {
        try { KingdomEnhancedPlugin.Instance?.LogSource.LogInfo(Line.ToString()); } catch { }
    }
}
