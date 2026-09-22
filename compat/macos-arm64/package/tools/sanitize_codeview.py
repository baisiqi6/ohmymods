#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""sanitize_codeview.py — 结构化 PE CodeView (RSDS) 调试定位路径最小化脱敏工具。

用途（operator 专用打包步骤，不属于游戏运行时）
------------------------------------------------
4 个自有托管 DLL 的 PE debug 目录各含一条 type-2 (CODEVIEW/RSDS) 记录，其 NUL 结尾
的 PDB 定位路径携带本机绝对路径（/Users/...）。本工具仅将该路径字段原地替换为
basename + NUL + 等长零填充；GUID、age、节布局、所有头字段、IL/元数据/资源/MVID
一字节不动，输出与输入的字节 diff 严格收敛在定位字段区间内（工具内自校验）。

fail-closed 前置门（任一不满足即拒绝，不产出输出）
--------------------------------------------------
1. 输入 SHA256 必须命中内置 PINS（4 个自有 DLL）；CLI 没有任何跳过/放宽开关。
2. PE 结构完整解析（DOS/COFF/Optional/节表/debug 目录），畸形或越界即拒绝。
3. Optional header CheckSum 非零 → 拒绝（改后 checksum 会陈旧；拒绝优于重算，
   保住“diff 仅在定位区间”的回执主张，不引入第 5 个修改点）。
4. 存在证书目录（data directory 4 非空）→ 拒绝（签名覆盖 debug 目录，改即失效）。
5. 必须是 CLR 托管（data directory 14 非空）且无强名签名
   （StrongNameSignature.RVA/Size 均为 0 且未置 COMIMAGE_FLAGS_STRONGNAMESIGNED）。
6. debug 目录记录规则：type-2 (CODEVIEW/RSDS) 全部枚举、逐条处理并全部写入回
   执，数量或 basename 序列与 PINS 期望不符 → 拒绝（防输入漂移）；.NET 标准
   type-16 (reproducible) 与 type-19 (PDB checksum) 记录原样逐字节保留（仅做
   边界校验并记入回执）；其余任何非 type-2 类型一律拒绝。
7. 输入中家路径标记（/Users/ 或 \\Users\\）的所有出现必须完整落在某条 RSDS 路径
   字段内，否则拒绝；输出复扫必须零残留。

CLI
---
    python3 sanitize_codeview.py --input X.dll --output Y.dll --receipt Y.json

- --output/--receipt 必须是尚不存在的新路径（独占创建，禁止覆盖），且不得与
  --input 相同或互相相同。
- 回执仅记录偏移、长度、输入/输出 SHA256 与 basename；不含任何源绝对 PDB 路径。
- 纯函数确定性实现：同一输入恒产生同一输出字节与同一回执内容（无时间戳）。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from dataclasses import dataclass
from typing import Dict, List, Optional, Sequence, Tuple

TOOL_NAME = "sanitize_codeview"
RECEIPT_SCHEMA = 1

# 输入锁定：sha256 -> 该文件全部 type-2 RSDS 记录的期望 basename 序列（同时锁定
# 记录条数）。仅包含 4 个自有二进制的构建产物哈希，不含任何本机路径。
PINS: Dict[str, List[str]] = {
    "34272c8362e3d3be3102ba884d4ae51c8bcbee20bb1d40d517b79bdb263947d6":
        ["OhMyMods.Arm64Entry.pdb"],
    "3b851171ccd37e5e9da3d547bfa46c9f2027b360cf437ebf04a98abaa98570f5":
        ["OhMyMods.Arm64Injection.pdb"],
    "2111a1674439c37127e8c972bf1d2e800a45043f1f11a1df6f18f6b60169d69d":
        ["OhMyMods.MacCompatibility.pdb"],
    "162046f77e025d90b3d6c9b5ea25d92835cb0609c26563885898ae93dfc9fb8a":
        ["KingdomEnhancedMod.pdb"],
}

# 家路径标记（macOS/Unix 与 Windows 形态）；出现在定位字段之外即拒绝。
PERSONAL_MARKERS: Tuple[bytes, ...] = (b"/Users/", b"\\Users\\")

DEBUG_TYPE_CODEVIEW = 2
IMAGE_DEBUG_ENTRY_SIZE = 28
RSDS_HEADER_SIZE = 24  # 'RSDS'(4) + GUID(16) + Age(4)

# .NET SDK 标准的非 CodeView 调试记录：原样逐字节保留，不解析不改写。
# 16 = reproducible 构建标记（SizeOfData 通常为 0）；19 = PDB checksum
# （"SHA256\0" + RSDS GUID + 哈希，绑定同目录 RSDS 记录）。
PRESERVED_DEBUG_TYPES = frozenset({16, 19})

# 数据目录索引
DIR_SECURITY = 4     # 证书（Authenticode）
DIR_DEBUG = 6        # debug 目录
DIR_CLR = 14         # CLR runtime header

COMIMAGE_FLAGS_STRONGNAMESIGNED = 0x00000008


class SanitizeError(Exception):
    """结构化拒绝（fail-closed）；消息面向 operator，可含输入路径。"""


@dataclass(frozen=True)
class RsdsRecord:
    entry_index: int
    entry_offset: int          # debug 目录项文件偏移
    size_of_data: int          # IMAGE_DEBUG_DIRECTORY.SizeOfData
    address_of_raw_data: int   # RVA（仅记录，不参与改写）
    pointer_to_raw_data: int   # RSDS 数据区文件偏移
    path_offset: int           # = pointer_to_raw_data + 24
    field_length: int          # = size_of_data - 24（路径 + NUL + 填充）
    old_path: str
    guid_hex: str
    age: int

    @property
    def field_start(self) -> int:
        return self.path_offset

    @property
    def field_end(self) -> int:
        return self.path_offset + self.field_length

    @property
    def basename(self) -> str:
        parts = [p for p in self.old_path.replace("\\", "/").split("/") if p]
        return parts[-1] if parts else ""


@dataclass(frozen=True)
class PreservedRecord:
    entry_index: int
    debug_type: int
    size_of_data: int
    pointer_to_raw_data: int

    @property
    def field_start(self) -> int:
        return self.pointer_to_raw_data

    @property
    def field_end(self) -> int:
        return self.pointer_to_raw_data + self.size_of_data


@dataclass(frozen=True)
class PeInfo:
    checksum: int
    sections: Tuple[Tuple[int, int, int, int], ...]  # (vaddr, vsize, raw_ptr, raw_size)
    data_dirs: Tuple[Tuple[int, int], ...]           # (rva, size) per index


def _u16(data: bytes, off: int) -> int:
    return int.from_bytes(data[off:off + 2], "little")


def _u32(data: bytes, off: int) -> int:
    return int.from_bytes(data[off:off + 4], "little")


def _parse_pe(data: bytes) -> PeInfo:
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise SanitizeError("not a PE image (missing MZ signature)")
    e_lfanew = _u32(data, 0x3C)
    if e_lfanew < 0x40 or e_lfanew + 4 + 20 + 64 > len(data):
        raise SanitizeError("e_lfanew out of bounds")
    if data[e_lfanew:e_lfanew + 4] != b"PE\x00\x00":
        raise SanitizeError("missing PE signature")

    coff = e_lfanew + 4
    number_of_sections = _u16(data, coff + 2)
    size_of_optional = _u16(data, coff + 16)
    if not 1 <= number_of_sections <= 96:
        raise SanitizeError(f"unreasonable NumberOfSections {number_of_sections}")

    opt = coff + 20
    if size_of_optional < 64 or opt + size_of_optional > len(data):
        raise SanitizeError("optional header out of bounds")
    magic = _u16(data, opt)
    if magic == 0x20B:
        pe32plus = True
    elif magic == 0x10B:
        pe32plus = False
    else:
        raise SanitizeError(f"unknown optional header magic 0x{magic:X}")

    num_rvas_off = opt + (108 if pe32plus else 92)
    dd_off = opt + (112 if pe32plus else 96)
    min_opt_size = (num_rvas_off - opt) + 4 + 15 * 8
    if size_of_optional < min_opt_size:
        raise SanitizeError("optional header too small for data directories")
    if _u32(data, num_rvas_off) < 15:
        raise SanitizeError("NumberOfRvaAndSizes < 15 (debug/CLR directories missing)")

    checksum = _u32(data, opt + 64)

    sec_off = opt + size_of_optional
    if sec_off + number_of_sections * 40 > len(data):
        raise SanitizeError("section table out of bounds")
    sections: List[Tuple[int, int, int, int]] = []
    for i in range(number_of_sections):
        s = sec_off + i * 40
        vsize = _u32(data, s + 8)
        vaddr = _u32(data, s + 12)
        rsize = _u32(data, s + 16)
        rptr = _u32(data, s + 20)
        if rsize > 0 and (rptr == 0 or rptr + rsize > len(data)):
            raise SanitizeError(f"section #{i} raw data out of file bounds")
        sections.append((vaddr, vsize, rptr, rsize))

    data_dirs = tuple(
        (_u32(data, dd_off + i * 8), _u32(data, dd_off + i * 8 + 4))
        for i in range(15)
    )
    return PeInfo(checksum=checksum, sections=tuple(sections), data_dirs=data_dirs)


def _rva_to_offset(pe: PeInfo, rva: int) -> Optional[int]:
    for vaddr, vsize, rptr, rsize in pe.sections:
        span = max(vsize, rsize)
        if vaddr <= rva < vaddr + span:
            delta = rva - vaddr
            if rptr == 0 or rsize == 0 or delta >= rsize:
                return None
            return rptr + delta
    return None


def parse_debug_records(data: bytes) -> Tuple[List[RsdsRecord], List[PreservedRecord]]:
    """解析并执行全部结构安全门；返回 (全部 type-2 记录, 原样保留的非 CodeView 记录)。"""
    pe = _parse_pe(data)

    if pe.checksum != 0:
        raise SanitizeError(
            f"optional header CheckSum is non-zero (0x{pe.checksum:08X}); "
            "refusing to edit (checksum would go stale)")

    cert_rva, cert_size = pe.data_dirs[DIR_SECURITY]
    if cert_rva != 0 or cert_size != 0:
        raise SanitizeError(
            f"certificate/security directory present (rva=0x{cert_rva:X}, "
            f"size={cert_size}); signature would not survive the edit")

    clr_rva, clr_size = pe.data_dirs[DIR_CLR]
    if clr_rva == 0 or clr_size == 0:
        raise SanitizeError("not a CLR managed image (data directory 14 empty)")

    cor_off = _rva_to_offset(pe, clr_rva)
    if cor_off is None or cor_off + 72 > len(data):
        raise SanitizeError("CLR header unmapped or out of bounds")
    sn_rva = _u32(data, cor_off + 32)
    sn_size = _u32(data, cor_off + 36)
    cor_flags = _u32(data, cor_off + 16)
    if sn_rva != 0 or sn_size != 0 or (cor_flags & COMIMAGE_FLAGS_STRONGNAMESIGNED):
        raise SanitizeError(
            "strong-name signed image (StrongNameSignature present or "
            "COMIMAGE_FLAGS_STRONGNAMESIGNED set); refusing to edit")

    dbg_rva, dbg_size = pe.data_dirs[DIR_DEBUG]
    if dbg_rva == 0 or dbg_size == 0:
        raise SanitizeError("debug directory missing")
    if dbg_size % IMAGE_DEBUG_ENTRY_SIZE != 0:
        raise SanitizeError(f"malformed debug directory size {dbg_size}")
    dbg_off = _rva_to_offset(pe, dbg_rva)
    if dbg_off is None or dbg_off + dbg_size > len(data):
        raise SanitizeError("debug directory unmapped or out of bounds")

    records: List[RsdsRecord] = []
    preserved: List[PreservedRecord] = []
    for i in range(dbg_size // IMAGE_DEBUG_ENTRY_SIZE):
        e = dbg_off + i * IMAGE_DEBUG_ENTRY_SIZE
        dtype = _u32(data, e + 12)
        size_of_data = _u32(data, e + 16)
        addr_raw = _u32(data, e + 20)
        ptr_raw = _u32(data, e + 24)
        tag = f"debug entry #{i}"
        if dtype != DEBUG_TYPE_CODEVIEW:
            if dtype not in PRESERVED_DEBUG_TYPES:
                raise SanitizeError(
                    f"{tag}: record type {dtype} is not CODEVIEW(2) nor a known "
                    "preserved .NET type (16/19); refusing")
            if size_of_data > 0 and (ptr_raw == 0 or ptr_raw + size_of_data > len(data)):
                raise SanitizeError(f"{tag}: preserved record data out of file bounds")
            preserved.append(PreservedRecord(
                entry_index=i, debug_type=dtype,
                size_of_data=size_of_data, pointer_to_raw_data=ptr_raw))
            continue
        if ptr_raw == 0:
            raise SanitizeError(f"{tag}: null PointerToRawData")
        if size_of_data < RSDS_HEADER_SIZE + 2:
            raise SanitizeError(f"{tag}: SizeOfData {size_of_data} too small for RSDS+path")
        if ptr_raw + size_of_data > len(data):
            raise SanitizeError(f"{tag}: data range out of file bounds")
        if data[ptr_raw:ptr_raw + 4] != b"RSDS":
            raise SanitizeError(f"{tag}: missing RSDS signature")
        guid_hex = data[ptr_raw + 4:ptr_raw + 20].hex()
        age = _u32(data, ptr_raw + 20)
        path_off = ptr_raw + RSDS_HEADER_SIZE
        field_length = size_of_data - RSDS_HEADER_SIZE
        field = data[path_off:path_off + field_length]
        nul = field.find(b"\x00")
        if nul < 1:
            raise SanitizeError(f"{tag}: path empty or missing NUL terminator")
        path_bytes = field[:nul]
        if any(b < 0x20 for b in path_bytes):
            raise SanitizeError(f"{tag}: control bytes inside path")
        try:
            old_path = path_bytes.decode("utf-8")
        except UnicodeDecodeError:
            raise SanitizeError(f"{tag}: path is not valid UTF-8")
        if not old_path.strip():
            raise SanitizeError(f"{tag}: blank path")
        records.append(RsdsRecord(
            entry_index=i, entry_offset=e, size_of_data=size_of_data,
            address_of_raw_data=addr_raw, pointer_to_raw_data=ptr_raw,
            path_offset=path_off, field_length=field_length,
            old_path=old_path, guid_hex=guid_hex, age=age))

    if not records:
        raise SanitizeError("debug directory contains no CODEVIEW records")

    spans = sorted(
        [(r.pointer_to_raw_data, r.pointer_to_raw_data + r.size_of_data) for r in records]
        + [(p.pointer_to_raw_data, p.pointer_to_raw_data + p.size_of_data)
           for p in preserved if p.size_of_data > 0])
    for (s1, e1), (s2, _e2) in zip(spans, spans[1:]):
        if s2 < e1:
            raise SanitizeError("overlapping debug data records")
    return records, preserved


def _iter_marker_hits(data: bytes):
    for marker in PERSONAL_MARKERS:
        start = 0
        while True:
            idx = data.find(marker, start)
            if idx < 0:
                break
            yield idx, idx + len(marker), marker
            start = idx + 1


def _verify_markers_confined(data: bytes, records: Sequence[RsdsRecord]) -> None:
    for s, e, marker in _iter_marker_hits(data):
        if not any(r.field_start <= s and e <= r.field_end for r in records):
            raise SanitizeError(
                f"personal path marker {marker!r} at bytes [{s},{e}) lies outside "
                "CodeView path fields; refusing (would not be sanitized)")


def sanitize_bytes(
    data: bytes,
    expected_basenames: Optional[Sequence[str]] = None,
) -> Tuple[bytes, List[dict]]:
    """核心脱敏：仅重写各 RSDS 路径字段为 basename+NUL+等长零填充。

    expected_basenames 非 None 时同时执行“type-2 条数与 basename 序列必须与清单
    完全一致”的门（CLI 对 PINS 输入强制传入）。
    """
    records, _preserved = parse_debug_records(data)
    _verify_markers_confined(data, records)

    actual_basenames = [r.basename for r in records]
    if expected_basenames is not None:
        if len(actual_basenames) != len(expected_basenames):
            raise SanitizeError(
                f"CODEVIEW record count {len(actual_basenames)} != expected "
                f"{len(expected_basenames)} from pin manifest")
        for i, (actual, expected) in enumerate(zip(actual_basenames, expected_basenames)):
            if actual != expected:
                raise SanitizeError(
                    f"CODEVIEW record #{i} basename {actual!r} != pinned {expected!r}")

    out = bytearray(data)
    for r in records:
        name = r.basename.encode("utf-8")
        if len(name) + 1 > r.field_length:
            raise SanitizeError(
                f"basename ({len(name)} bytes) does not fit path field "
                f"({r.field_length} bytes) at offset {r.path_offset}")
        out[r.field_start:r.field_end] = name + b"\x00" * (r.field_length - len(name))
    out = bytes(out)

    for _s, _e, marker in _iter_marker_hits(out):
        raise SanitizeError(
            f"internal error: personal path marker {marker!r} survived sanitization")

    out_records, _out_preserved = parse_debug_records(out)
    if len(out_records) != len(records):
        raise SanitizeError("internal error: record count changed after edit")
    for before, after in zip(records, out_records):
        if (before.path_offset, before.size_of_data, before.guid_hex, before.age) != \
           (after.path_offset, after.size_of_data, after.guid_hex, after.age):
            raise SanitizeError("internal error: RSDS metadata changed after edit")
        if after.old_path != before.basename:
            raise SanitizeError("internal error: sanitized path is not the basename")

    if len(out) != len(data):
        raise SanitizeError("internal error: file size changed")
    for i in range(len(data)):
        if data[i] != out[i] and not any(r.field_start <= i < r.field_end for r in records):
            raise SanitizeError(
                f"internal error: byte diff at {i} escaped CodeView path fields")

    receipt_records = [
        {
            "type": DEBUG_TYPE_CODEVIEW,
            "entry_index": r.entry_index,
            "data_offset": r.pointer_to_raw_data,
            "size_of_data": r.size_of_data,
            "path_offset": r.path_offset,
            "field_length": r.field_length,
            "old_path_length": len(r.old_path.encode("utf-8")),
            "new_path": r.basename,
            "guid_hex": r.guid_hex,
            "age": r.age,
        }
        for r in records
    ]
    return out, receipt_records


def _refuse_existing(path: str, what: str) -> None:
    if os.path.lexists(path):
        raise SanitizeError(f"{what} already exists, overwrite forbidden: {path}")


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        prog=TOOL_NAME,
        description="Structure-aware PE CodeView (RSDS) PDB-path sanitizer for the "
                    "4 pinned own managed DLLs. Rewrites only the null-terminated "
                    "path field inside each type-2 debug record to its basename plus "
                    "zero padding of identical field length.")
    parser.add_argument("--input", required=True, help="input DLL (must match a pinned sha256)")
    parser.add_argument("--output", required=True, help="output DLL (must not exist; never overwrites)")
    parser.add_argument("--receipt", required=True, help="receipt JSON path (must not exist)")
    args = parser.parse_args(argv)

    try:
        in_real = os.path.realpath(args.input)
        out_real = os.path.realpath(args.output)
        rec_real = os.path.realpath(args.receipt)

        if not os.path.isfile(args.input):
            raise SanitizeError(f"input is not an existing regular file: {args.input}")
        if out_real == in_real:
            raise SanitizeError("output path must differ from input path")
        if rec_real in (in_real, out_real):
            raise SanitizeError("receipt path must differ from input/output paths")
        _refuse_existing(args.output, "output")
        _refuse_existing(args.receipt, "receipt")

        with open(args.input, "rb") as f:
            data = f.read()
        input_sha = hashlib.sha256(data).hexdigest()
        expected = PINS.get(input_sha)
        if expected is None:
            raise SanitizeError(
                f"unknown input sha256 {input_sha}: not one of the {len(PINS)} "
                "pinned own DLLs; refusing (there is no bypass flag by design)")

        _records, preserved = parse_debug_records(data)
        out, receipt_records = sanitize_bytes(data, expected)
        out_sha = hashlib.sha256(out).hexdigest()

        with open(args.output, "xb") as f:
            f.write(out)
        receipt = {
            "schema": RECEIPT_SCHEMA,
            "tool": TOOL_NAME,
            "input": {"file": os.path.basename(args.input), "size": len(data),
                      "sha256": input_sha},
            "output": {"file": os.path.basename(args.output), "size": len(out),
                       "sha256": out_sha},
            "records": receipt_records,
            "preserved_debug_entries": [
                {"type": p.debug_type, "entry_index": p.entry_index,
                 "data_offset": p.pointer_to_raw_data,
                 "size_of_data": p.size_of_data}
                for p in preserved],
            "modified_ranges": [[r["path_offset"],
                                 r["path_offset"] + r["field_length"]]
                                for r in receipt_records],
            "notes": "byte diff confined to CodeView RSDS path fields; RSDS GUID/age, "
                     "headers, IL, resources and MVID untouched",
        }
        with open(args.receipt, "x", encoding="utf-8") as f:
            json.dump(receipt, f, indent=2, sort_keys=True, ensure_ascii=False)
            f.write("\n")

        ranges = " ".join(f"[{r['path_offset']},{r['path_offset'] + r['field_length']})"
                          for r in receipt_records)
        print(f"sanitized {os.path.basename(args.input)}: {input_sha[:12]} -> "
              f"{out_sha[:12]}, path fields {ranges}")
        return 0
    except SanitizeError as exc:
        print(f"{TOOL_NAME}: reject: {exc}", file=sys.stderr)
        return 1
    except OSError as exc:
        print(f"{TOOL_NAME}: error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
