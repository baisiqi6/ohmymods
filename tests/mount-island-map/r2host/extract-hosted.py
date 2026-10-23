#!/usr/bin/env python3
"""从 il2cpp/ExtensionIslandMap.cs 逐字抽取字典生命周期方法体，生成 r2host 的 HostedMethods.cs。

从 repo 根运行：
    python3 tests/mount-island-map/r2host/extract-hosted.py

提取用花括号平衡取纯方法体（doc 注释/后续成员不进入范围；被抽取方法体的字符串字面量
无花括号，已核）。生成物运行时由 R2Program 对产品文件再抽取同签名做 sha256 比对
（hashlock）——产品方法变更后必须重新生成本文件，否则 host 拒绝执行（零执行证据）。
"""
import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
PROD = os.path.join(ROOT, "il2cpp", "ExtensionIslandMap.cs")
DEST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "HostedMethods.cs")

SIGS = [
    "private static int SlotIndexOf(",
    "private static bool SameMenu(",
    "private static int MenuId(",
    "private static bool CaptureLookupPriors(",
    "private static void AttachLookupLedger(",
    "private static bool LookupDictStillCurrent(",
    "private static bool LookupCommitGuard(",
    "private static bool CanPoisonAppendAttempt(",
    "private static bool RemoveTombstoneForPendingGeneration(",
    "private static void RemoveTombstone(",
    "private static void RestoreReplacedArraysIfStillOurs(",
    "private static bool WriteLookup(",
    "private static bool FixLookupOnDict(",
    "private static void DegradeLookupForRevoke(",
    "private static bool ReleaseLookup(",
]

src = io.open(PROD, encoding="utf-8").read()


def extract(sig):
    # 花括号平衡提取纯方法体（doc 注释/后续成员不进入范围；方法体字符串无花括号，已核）
    start = src.index(sig)
    i = src.index("{", start)
    brace = 0
    while True:
        c = src[i]
        if c == "{":
            brace += 1
        elif c == "}":
            brace -= 1
            if brace == 0:
                break
        i += 1
    return src[start:i + 1]


parts = [extract(sig) for sig in SIGS]

header = """// 自动生成：逐字拷贝 il2cpp/ExtensionIslandMap.cs 的字典生命周期方法体。
// 再生成命令（repo 根）：python3 tests/mount-island-map/r2host/extract-hosted.py
// 运行时由 R2Program 按 sha256 对产品文件再抽取比对（HashLocked 区，提取用同样的
// 花括号平衡算法）——不一致即全部 FAIL，不产生执行证据。不要手改本文件。
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using KingdomEnhancedMod;

namespace MountIslandMapR2Host
{
    internal static partial class HostedMapMethods
    {
        /// <summary>产品同名字段的 host 挂点（poison/pending 状态由逐字方法体维护）。</summary>
        internal static MapTimelineMenuGreece _poisonedMenu;
        /// <summary>产品同名字段的 host 挂点（pending generation 引用，LookupCommitGuard 用）。</summary>
        internal static Generation _generation;
        internal static readonly List<string> LogLines = new List<string>(8);

"""
footer = """

        private static void LogOnce(string key, string message)
        {
            LogLines.Add("once[" + key + "] " + message);
        }

        private static void Log(string message)
        {
            LogLines.Add("log " + message);
        }
    }
}
"""
io.open(DEST, "w", encoding="utf-8").write(header + "\n\n".join(parts) + footer)
print("generated", os.path.relpath(DEST, ROOT), "methods:", len(SIGS))
