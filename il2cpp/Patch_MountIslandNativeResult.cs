using System;
#if ANDROID
using Il2CppCoatsink.Common;
#else
using Coatsink.Common;
#endif
using Il2CppInterop.Runtime;

namespace KingdomEnhancedMod
{
    /// <summary>
    /// 只读 Native 结果 reader（#260 b）。
    ///
    /// 边界：
    /// - 只由既有 <c>IslandSaveData.&lt;_TryLoad&gt;d__54.MoveNext</c> 的真实终末路径调用；本文件不注册
    ///   Harmony hook、不 hook factory / Reset / Dispose / 泛型 Return，不调用 load/storage/save/factory。
    /// - 不写任何 native 字段或游戏状态；唯一 native 读是元数据查询和一次
    ///   <c>il2cpp_field_get_value(instance, returnField, &amp;localInt)</c> 的整字段 4 字节复制。
    /// - 不使用生成属性 <c>@return.value</c>（其 <c>PointerToValueGeneric</c> 会二次装箱），不使用固定
    ///   offset/地址，不 mask/normalize，不把 Unknown 当 MissingData。
    /// - 元数据形状在整会话只解析一次：成功与失败都 sticky 缓存；任何尺寸/类型/字段布局异常都 fail-closed。
    /// - before/after stamp、owner/request/issued generation 的闭合由 Observer 负责；本 reader 只保证
    ///   对象 class 与元数据形状，不自行授予 fresh 权限。
    /// </summary>
    internal static unsafe class MountIslandNativeResult
    {
        // IL2CPP FIELD_ATTRIBUTE_STATIC；native type code（当前 interop 的 Il2CppTypeEnum）。
        private const int FieldAttributeStatic = 0x0010;
        private const int Il2CppTypePointer = 0x0F;
        private const int Il2CppTypeByRef = 0x10;
        private const int Il2CppTypeI4 = 0x08;

        // 有界字段枚举：Return<T> 的正常形状只有 value 一个非 static 字段。
        private const int MaxEnumeratedFields = 128;

        private static readonly object MetadataLock = new object();
        private static bool _metadataInitialized;
        private static bool _metadataOk;
        private static string _metadataReason = "uninitialized";
        private static IntPtr _routineClass;
        private static IntPtr _returnField;

        /// <summary>
        /// 读取一次终末 <c>Return&lt;SaveLoadResult&gt;</c> 的原始 4 字节（不解释、不 mask）。
        /// 失败返回 false，调用方必须保持原 <see cref="MountIslandCompletionClass.Unknown"/> 语义。
        /// </summary>
        internal static bool TryRead(IslandSaveData.__TryLoad_d__54 instance, out int flags, out string reason)
        {
            flags = 0;
            if (instance == null)
            {
                reason = "instance-null";
                return false;
            }

            IntPtr pointer;
            try
            {
                pointer = instance.Pointer;
            }
            catch (Exception e)
            {
                reason = "instance-pointer-fault:" + e.GetType().Name;
                return false;
            }
            if (pointer == IntPtr.Zero)
            {
                reason = "instance-pointer-zero";
                return false;
            }

            if (!EnsureMetadata(out reason)) return false;

            try
            {
                // 每次读取都重新核对象 class；缓存的有效 metadata 不能证明传入对象仍是同一 routine。
                if (IL2CPP.il2cpp_object_get_class(pointer) != _routineClass)
                {
                    reason = "routine-class-mismatch";
                    return false;
                }

                int local = 0;
                IL2CPP.il2cpp_field_get_value(pointer, _returnField, &local);
                GC.KeepAlive(instance);
                flags = local;
                reason = "ok";
                return true;
            }
            catch (Exception e)
            {
                flags = 0;
                reason = "field-read-fault:" + e.GetType().Name;
                return false;
            }
        }

        /// <summary>整会话一次：shape 成功或失败都 sticky；失败原因原样返回给 Observer 日志。</summary>
        private static bool EnsureMetadata(out string reason)
        {
            lock (MetadataLock)
            {
                if (_metadataInitialized)
                {
                    reason = _metadataReason;
                    return _metadataOk;
                }

                _metadataInitialized = true;
                try
                {
                    MountIslandNativeResultShape shape = ReadShape(out IntPtr routineClass, out IntPtr returnField);
                    if (!MountIslandNativeResultShapePolicy.IsValid(shape))
                    {
                        _metadataOk = false;
                        _metadataReason = "shape-invalid: " + MountIslandNativeResultShapePolicy.Describe(shape);
                        reason = _metadataReason;
                        return false;
                    }

                    _routineClass = routineClass;
                    _returnField = returnField;
                    _metadataOk = true;
                    _metadataReason = "ok";
                    reason = _metadataReason;
                    return true;
                }
                catch (Exception e)
                {
                    _metadataOk = false;
                    _metadataReason = "metadata-fault:" + e.GetType().Name + ":" + e.Message;
                    reason = _metadataReason;
                    return false;
                }
            }
        }

        /// <summary>
        /// live metadata → 纯 shape。每个可能为 0 的 handle 在进入会解引用的 native API 前检查；
        /// 形状比较全部使用精确指针相等，不比较名字字符串来证明类型。
        /// </summary>
        private static MountIslandNativeResultShape ReadShape(out IntPtr routineClass, out IntPtr returnField)
        {
            routineClass = Il2CppClassPointerStore<IslandSaveData.__TryLoad_d__54>.NativeClassPtr;
            if (routineClass == IntPtr.Zero) throw new InvalidOperationException("routine-class-zero");

            IntPtr expectedReturnClass = Il2CppClassPointerStore<Routine.Return<SaveLoadResult>>.NativeClassPtr;
            if (expectedReturnClass == IntPtr.Zero) throw new InvalidOperationException("return-class-zero");

            IntPtr enumClass = Il2CppClassPointerStore<SaveLoadResult>.NativeClassPtr;
            if (enumClass == IntPtr.Zero) throw new InvalidOperationException("enum-class-zero");

            var shape = new MountIslandNativeResultShape
            {
                RoutineClassPresent = true,
                RoutineClassReferenceType = !IL2CPP.il2cpp_class_is_valuetype(routineClass),
                EnumClassPresent = true,
            };

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(routineClass, "return");
            if (field == IntPtr.Zero) throw new InvalidOperationException("return-field-missing");
            returnField = field;
            shape.ReturnFieldPresent = true;
            shape.ReturnFieldParentExact = IL2CPP.il2cpp_field_get_parent(field) == routineClass;
            shape.ReturnFieldNonStatic = (IL2CPP.il2cpp_field_get_flags(field) & FieldAttributeStatic) == 0;

            IntPtr fieldType = IL2CPP.il2cpp_field_get_type(field);
            if (fieldType == IntPtr.Zero) throw new InvalidOperationException("return-field-type-zero");
            int fieldKind = IL2CPP.il2cpp_type_get_type(fieldType);
            shape.ReturnFieldTypeNotByRefOrPointer =
                fieldKind != Il2CppTypePointer && fieldKind != Il2CppTypeByRef;
            // kind 不能证明非 byref：独立 flag 必须为 false。
            shape.ReturnFieldByRefFlagFree = !IL2CPP.il2cpp_type_is_byref(fieldType);

            IntPtr fieldClass = IL2CPP.il2cpp_class_from_type(fieldType);
            if (fieldClass == IntPtr.Zero) throw new InvalidOperationException("return-field-class-zero");
            shape.ReturnClassExact = fieldClass == expectedReturnClass;
            shape.ReturnClassValueType = IL2CPP.il2cpp_class_is_valuetype(expectedReturnClass);

            uint returnAlignment = 0;
            shape.ReturnValueSize = IL2CPP.il2cpp_class_value_size(expectedReturnClass, ref returnAlignment);
            shape.ReturnAlignment = returnAlignment;

            shape.EnumIsEnum = IL2CPP.il2cpp_class_is_enum(enumClass);
            shape.EnumValueType = IL2CPP.il2cpp_class_is_valuetype(enumClass);
            shape.EnumBaseTypeIsInt32 = false;
            shape.EnumBaseTypeKindNotByRefOrPointer = false;
            shape.EnumBaseTypeByRefFlagFree = false;
            if (shape.EnumIsEnum)
            {
                // 只对确认是 enum 的 class 调 enum_basetype；非 enum 直接 fail-closed。
                IntPtr enumBaseType = IL2CPP.il2cpp_class_enum_basetype(enumClass);
                if (enumBaseType != IntPtr.Zero)
                {
                    int enumBaseKind = IL2CPP.il2cpp_type_get_type(enumBaseType);
                    shape.EnumBaseTypeKindNotByRefOrPointer =
                        enumBaseKind != Il2CppTypePointer && enumBaseKind != Il2CppTypeByRef;
                    shape.EnumBaseTypeByRefFlagFree = !IL2CPP.il2cpp_type_is_byref(enumBaseType);
                    shape.EnumBaseTypeIsInt32 = enumBaseKind == Il2CppTypeI4;
                }
            }

            uint enumAlignment = 0;
            shape.EnumValueSize = IL2CPP.il2cpp_class_value_size(enumClass, ref enumAlignment);
            shape.EnumAlignment = enumAlignment;

            // Return<T> 必须恰好只有一个非 static 实例字段 value，且其类型精确为 SaveLoadResult。
            IntPtr iterator = IntPtr.Zero;
            IntPtr valueField = IntPtr.Zero;
            string valueFieldName = null;
            int nonStaticFields = 0;
            int enumerated = 0;
            while (true)
            {
                if (++enumerated > MaxEnumeratedFields)
                    throw new InvalidOperationException("field-enumeration-overflow");

                IntPtr current = IL2CPP.il2cpp_class_get_fields(expectedReturnClass, ref iterator);
                if (current == IntPtr.Zero) break;
                if ((IL2CPP.il2cpp_field_get_flags(current) & FieldAttributeStatic) != 0) continue;

                nonStaticFields++;
                string name = IL2CPP.il2cpp_field_get_name_(current);
                if (string.Equals(name, "value", StringComparison.Ordinal))
                {
                    valueField = current;
                    valueFieldName = name;
                }
            }

            shape.NonStaticFieldCount = nonStaticFields;
            shape.ValueFieldPresent = valueField != IntPtr.Zero;
            shape.ValueFieldNamedValue = valueField != IntPtr.Zero
                && string.Equals(valueFieldName, "value", StringComparison.Ordinal);
            if (valueField != IntPtr.Zero)
            {
                IntPtr valueType = IL2CPP.il2cpp_field_get_type(valueField);
                if (valueType == IntPtr.Zero) throw new InvalidOperationException("value-field-type-zero");
                int valueKind = IL2CPP.il2cpp_type_get_type(valueType);
                shape.ValueFieldTypeNotByRefOrPointer =
                    valueKind != Il2CppTypePointer && valueKind != Il2CppTypeByRef;
                shape.ValueFieldByRefFlagFree = !IL2CPP.il2cpp_type_is_byref(valueType);
                IntPtr valueClass = IL2CPP.il2cpp_class_from_type(valueType);
                shape.ValueFieldClassExactEnum = valueClass != IntPtr.Zero && valueClass == enumClass;
            }

            return shape;
        }
    }

    /// <summary>
    /// 把终末 read 适配为纯 <see cref="IMountIslandTerminalFlagsReader"/>；只在 Observer 的真实
    /// terminal 分支创建，不缓存游戏对象、不扩权。
    /// </summary>
    internal sealed class MountIslandNativeResultReader : IMountIslandTerminalFlagsReader
    {
        private readonly IslandSaveData.__TryLoad_d__54 _instance;

        internal MountIslandNativeResultReader(IslandSaveData.__TryLoad_d__54 instance)
        {
            _instance = instance;
        }

        bool IMountIslandTerminalFlagsReader.TryRead(out int flags, out string reason)
            => MountIslandNativeResult.TryRead(_instance, out flags, out reason);
    }
}
