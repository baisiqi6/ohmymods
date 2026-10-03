# console-filter.awk — OhMyMods Mac ARM64 终端精简过滤器。
#
# 输入：游戏进程合并后的 stdout+stderr 逐行文本（tee 已把完整原文写入
#       launcher-console.log；本脚本只影响终端显示）。
# 输出：放行“需要可见”的行，仅抑制“确证的已知噪声”：
#       1. native loader 的 Mach-O 转储明细：MEMORY MAP 表头/地址区间行、
#          本轮确证的 LC 命令名（裸行）、CMD START/END、确证的 BIND_OPCODE
#          负载、以及 LC_SEGMENT/LC_SYMTAB 详情的“字段名 + 合法值”行；
#       2. 三个已审计 Debug 来源的“确证事件负载”（DobbyDetour/AssemblyPatcher/
#          Il2CppInterop 的具体消息形态）；
#       3. Info 级角色采样中确证的诊断事件（[HeroArcherVisuals]
#          [HeroArcherNative] / [HeroArcherPoseVisit] 的 t=/src=/actor= 负载、
#          [FarmCatMovement] native activity state unproven cat= 行）。
#
# 保护规则（先于所有抑制规则）：
#   - [Warning]/[Error]/[Fatal] 级别一律放行；
#   - 任意行含 error/fail/exception/warn/fatal/panic/denied/refused/abort/
#     timeout/not found 关键词一律放行（大小写不敏感）；
#   - 未知 LC 命令/未知尾部、MEMORY MAP 未闭合括号、字段值不匹配、未知同源
#     结构化内容与一切未匹配行默认放行。宁可多留已知噪声，不扩大吞行面。
#
# 兼容性：POSIX awk（macOS 自带 BWK awk）语义；不使用 gawk 扩展、不使用
# {m,n} 区间表达式。launcher 以 `awk -f console-filter.awk -` 调用。

function trim(v) {
    gsub(/^[ \t]+/, "", v)
    gsub(/[ \t]+$/, "", v)
    return v
}

# 错误关键词保护：命中即绝不抑制（宁可多显示，不可漏错误）。
function has_error_keyword(line,   t) {
    t = tolower(line)
    if (index(t, "error") != 0) return 1
    if (index(t, "fail") != 0) return 1
    if (index(t, "exception") != 0) return 1
    if (index(t, "warn") != 0) return 1
    if (index(t, "fatal") != 0) return 1
    if (index(t, "panic") != 0) return 1
    if (index(t, "denied") != 0) return 1
    if (index(t, "refused") != 0) return 1
    if (index(t, "abort") != 0) return 1
    if (index(t, "timeout") != 0) return 1
    if (index(t, "not found") != 0) return 1
    return 0
}

# MEMORY MAP 地址区间行：必须完整 9 字段且逐字段值形态合法，行尾无多余内容。
# 实测真实样本 68 行全部 9 字段；字段不足/额外尾部/未知值一律放行（保留）。
function is_memory_range(line) {
    if (substr(line, 1, 1) != " ") return 0
    if (NF != 9) return 0
    if ($1 !~ /^[0-9a-f]+-[0-9a-f]+$/) return 0
    if (length($1) < 17) return 0
    if ($2 !~ /^[r-][w-][x-]\([0-9a-f]+\)$/) return 0
    if ($3 !~ /^[r-][w-][x-]\([0-9a-f]+\)$/) return 0
    if ($4 != "copy" && $4 != "share") return 0
    if ($5 != "N") return 0
    if ($6 != "N" && $6 != "Y") return 0
    if ($7 !~ /^[0-9a-f]+$/) return 0
    if ($8 != "default") return 0
    if ($9 !~ /^[0-9]+$/) return 0
    return 1
}

function is_finite_num(v) { return v ~ /^-?[0-9]+(\.[0-9]+)?$/ }
function is_int_val(v)    { return v ~ /^-?[0-9]+$/ }

# [HeroArcherVisuals] [HeroArcherNative]：仅过滤完整正常帧——键顺序固定、数值有限、
# native=1/hero=1/reason=None；未知值、缺字段、合法前缀后异常尾部一律保留。
function hero_native_ok(rest,   f, n, nk, i, j, kv, k, v, keys_exp) {
    rest = trim(rest)
    n = split(rest, f, " ")
    if (n != 20 && n != 21) return 0
    if (f[1] != "[HeroArcherVisuals]" || f[2] != "[HeroArcherNative]") return 0
    nk = split("t frame src actor life hash nt len ct pose nat win native forceoff hero reason y face", keys_exp, " ")
    for (i = 1; i <= nk; i++) {
        kv = f[i + 2]
        j = index(kv, "=")
        if (j == 0) return 0
        k = substr(kv, 1, j - 1)
        v = substr(kv, j + 1)
        if (k != keys_exp[i]) return 0
        if (k == "t" || k == "nt" || k == "len" || k == "ct" || k == "win" || k == "y") {
            if (!is_finite_num(v)) return 0
        } else if (k == "src") {
            if (v != "panel-late" && v != "apply") return 0
        } else if (k == "reason") {
            if (v != "None") return 0
        } else if (k == "native" || k == "hero") {
            if (v != "1") return 0
        } else if (k == "forceoff") {
            if (v != "0" && v != "1") return 0
        } else if (k == "face") {
            if (v !~ /^-?[0-9]+\/-?[0-9]+f0$/) return 0
        } else {
            if (!is_int_val(v)) return 0
        }
    }
    if (n == 21) {
        kv = f[21]
        if (substr(kv, 1, 5) != "shot=") return 0
        if (!is_int_val(substr(kv, 6))) return 0
    }
    return 1
}

# [HeroArcherVisuals] [HeroArcherPoseVisit]：同上，仅过滤完整正常帧。
function hero_pose_ok(rest,   f, n, nk, i, j, kv, k, v, action_v, next_v, keys_exp) {
    rest = trim(rest)
    n = split(rest, f, " ")
    if (n != 11 && n != 12) return 0
    if (f[1] != "[HeroArcherVisuals]" || f[2] != "[HeroArcherPoseVisit]") return 0
    nk = split("t frame actor action dur ctMax frames win next", keys_exp, " ")
    action_v = ""
    next_v = ""
    for (i = 1; i <= nk; i++) {
        kv = f[i + 2]
        j = index(kv, "=")
        if (j == 0) return 0
        k = substr(kv, 1, j - 1)
        v = substr(kv, j + 1)
        if (k != keys_exp[i]) return 0
        if (k == "t" || k == "dur" || k == "win") {
            if (!is_finite_num(v)) return 0
        } else if (k == "ctMax") {
            if (v !~ /^-?[0-9]+(\.[0-9]+)?\/-?[0-9]+(\.[0-9]+)?$/) return 0
        } else if (k == "frames") {
            if (v !~ /^[0-9]+-[0-9]+$/) return 0
        } else if (k == "action") {
            if (v != "Prepare" && v != "Shoot") return 0
            action_v = v
        } else if (k == "next") {
            if (v != "Stand" && v != "Shoot") return 0
            next_v = v
        } else {
            if (!is_int_val(v)) return 0
        }
    }
    if (n == 12) {
        kv = f[12]
        if (substr(kv, 1, 6) != "shots=") return 0
        if (substr(kv, 7) !~ /^[0-9]+@-?[0-9]+(\.[0-9]+)?$/) return 0
    }
    # 只确证真实出现的动作配对：Prepare→Shoot / Prepare→Stand / Shoot→Stand；
    # 其余组合一律放行（保留）
    if (!((action_v == "Prepare" && next_v == "Shoot") ||
          (action_v == "Prepare" && next_v == "Stand") ||
          (action_v == "Shoot" && next_v == "Stand"))) return 0
    return 1
}

# 结构化日志头解析出的 level/src/rest 是否属于确证噪声。
function is_structured_noise(level, src, rest) {
    if (level == "Debug") {
        if (src == "DobbyDetour") {
            if (rest ~ /^ Preparing detour from 0x[0-9a-fA-F]+ to 0x[0-9a-fA-F]+$/) return 1
            if (rest ~ /^ Prepared detour; Trampoline: 0x[0-9a-fA-F]+$/) return 1
            if (rest ~ /^ Original: [0-9a-fA-F]+, Trampoline: [0-9a-fA-F]+, diff: [0-9a-fA-F]+$/) return 1
            return 0
        }
        if (src == "AssemblyPatcher") {
            if (rest ~ /^ Assembly loaded: [A-Za-z0-9._+-]+$/) return 1
            return 0
        }
        if (src == "Il2CppInterop") {
            if (rest == " Picked mono_class_instance_size as a Class::Init substitute") return 1
            if (rest == " Original class was inflated, falling back to reflection") return 1
            if (rest ~ /^ (GenericMethod::GetMethod|MetadataCache::GetTypeInfoFromTypeDefinitionIndex|Class::GetDefaultFieldValue|Class::FromIl2CppType|Class::FromName) found: 0x[0-9a-fA-F]+$/) return 1
            if (rest ~ /^ Class::Init: 0x[0-9a-fA-F]+$/) return 1
            return 0
        }
        return 0
    }
    if (level == "Info" && src == "KingdomEnhancedMod") {
        # 仅确证诊断事件的完整负载形态；未知同源内容一律放行
        if (index(rest, "[HeroArcherNative]") != 0 && hero_native_ok(rest)) return 1
        if (index(rest, "[HeroArcherPoseVisit]") != 0 && hero_pose_ok(rest)) return 1
        if (rest ~ /^ \[FarmCatMovement\] native activity state unproven cat=-?[0-9]+ pc=-?[0-9]+ x=-?[0-9]+(\.[0-9]+)? bodyVx=-?[0-9]+(\.[0-9]+)?$/) return 1
        return 0
    }
    return 0
}

# 是否属于可抑制的确证噪声（仅按当前行判定，不跨行累积状态）。
function is_recognized_noise(   line, rb, header, colon, level, src, rest) {
    line = $0

    # ---- 结构化 BepInEx 日志头：[Level  : Source] message ----
    if (substr(line, 1, 1) == "[") {
        rb = index(line, "]")
        if (rb > 2) {
            header = substr(line, 2, rb - 2)
            colon = index(header, ":")
            if (colon > 0) {
                level = trim(substr(header, 1, colon - 1))
                src = trim(substr(header, colon + 1))
                rest = substr(line, rb + 1)
                return is_structured_noise(level, src, rest)
            }
        }
        return 0
    }

    # ---- native loader 逐行白名单 ----
    if (line ~ /^mh=[0-9a-fA-F]+ slide=[0-9a-fA-F]+$/) return 1
    # 只认审计确认的绝对 dylib 路径完整形态；带闭括号的未知载荷/相对文本一律放行
    if (line ~ /^MEMORY MAP\(\/[^()]*\.dylib\)$/) return 1
    if (line == " start address    end address      protection    max_protection inherit     shared reserved offset   behavior         user_wired_count") return 1
    if (line ~ /^ +offset +size$/) return 1
    if (line ~ /^ +(rebase|bind|weak_bind|lazy_bind|export_bind) +[0-9a-f]+ +[0-9a-f]+$/) return 1
    if (is_memory_range(line)) return 1
    # 本轮确证的 LC 命令：只认裸行；未知命令/任意尾部一律放行
    if (line ~ /^LC_(LOAD_DYLIB|SEGMENT_64|UUID|SYMTAB|SOURCE_VERSION|ID_DYLIB|FUNCTION_STARTS|DYSYMTAB|DYLD_INFO_ONLY|DATA_IN_CODE|CODE_SIGNATURE|BUILD_VERSION)$/) return 1
    if (line == "CMD START" || line == "CMD END") return 1
    # 确证的 BIND_OPCODE：完整负载形态
    if (line ~ /^0x[0-9a-f]+: BIND_OPCODE_DONE$/) return 1
    if (line ~ /^0x[0-9a-f]+: BIND_OPCODE_DO_BIND$/) return 1
    if (line ~ /^0x[0-9a-f]+: BIND_OPCODE_SET_DYLIB_ORDINAL_IMM: ordinal = [0-9]+$/) return 1
    if (line ~ /^0x[0-9a-f]+: BIND_OPCODE_SET_DYLIB_ORDINAL_ULEB: ordinal = [0-9]+$/) return 1
    if (line ~ /^0x[0-9a-f]+: BIND_OPCODE_SET_SEGMENT_AND_OFFSET_ULEB: seg_index = [0-9]+, seg_offset = 0x[0-9a-f]+$/) return 1
    if (line ~ /^0x[0-9a-f]+: BIND_OPCODE_SET_SYMBOL_TRAILING_FLAGS_IMM: sym_name = [A-Za-z0-9_.$]+$/) return 1
    # LC_SEGMENT/LC_SYMTAB 详情：“字段名 + 合法值”精确形态（值不符即放行）
    if (line ~ /^  segname   [A-Za-z0-9_.]+$/) return 1
    if (line ~ /^  vmaddr +[0-9a-f]+  vmsize +[0-9a-f]+$/) return 1
    if (line ~ /^  fileoff +[0-9a-f]+  filesize +[0-9a-f]+$/) return 1
    if (line ~ /^  maxprot +[0-9]+  initprot +[0-9]+$/) return 1
    if (line ~ /^  nsects +[0-9]+  flags +[0-9]+$/) return 1
    if (line ~ /^  section_64 \([0-9]+\)$/) return 1
    if (line ~ /^      sectname  [A-Za-z0-9_.]+$/) return 1
    if (line ~ /^      segname   [A-Za-z0-9_.]+$/) return 1
    if (line ~ /^      (addr|size|offset|align|reloff) +0x[0-9a-f]+$/) return 1
    if (line ~ /^      flags +0x[0-9a-f]+$/) return 1
    if (line ~ /^      nreloc +[0-9]+$/) return 1
    if (line ~ /^      reserved[123] +[0-9]+$/) return 1
    return 0
}

{
    if (is_recognized_noise() && !has_error_keyword($0)) next
    print
}
