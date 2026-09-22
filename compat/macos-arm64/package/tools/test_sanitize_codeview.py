#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""test_sanitize_codeview.py — sanitize_codeview 的结构化单元/CLI 测试。

全部用合成 PE 镜像驱动（自包含，不依赖真实 DLL）；真实 4 DLL 的集成验证由环境
变量 KEM_SANITIZE_ITG_ROOT 指向含 BepInEx/ 布局的安装根目录时启用（仍走公开
CLI，SHA pin 不放宽）。

合成镜像布局（build_image）：
  0x000 DOS(0x80) | PE sig+COFF(24) | Optional(232/216) | 节表(40) | 零填充
  0x400 .rdata: cor20(72) | debug entries(28*n) | RSDS 记录 | tail
  节 RVA 0x1000 ↔ 文件偏移 0x400；optional header 起始文件偏移 0x98。
"""

from __future__ import annotations

import hashlib
import json
import os
import struct
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import sanitize_codeview as sc  # noqa: E402

GUID = bytes(range(16))
AGE = 7

FILE_ALIGN = 0x400
SECTION_RVA = 0x1000
OPT_FILE_OFF = 0x98                    # e_lfanew(0x80)+sig(4)+COFF(20)
DBG_FILE_OFF = FILE_ALIGN + 72         # cor20 之后即 debug 目录

LONG_PATH = b"/Users/buildagent/workspace/demo-proj/obj/Release/net6.0/Demo.Module.pdb"
SECOND_PATH = b"/Users/buildagent/other-tree/obj/Debug/net6.0/Second.dll.pdb"


def _pack_entry(dtype, size, addr, ptr):
    return struct.pack("<IIHHIIII", 0, 0x67000000, 0, 0, dtype, size, addr, ptr)


def _data_dir_abs(index, pe32plus=True):
    return OPT_FILE_OFF + (112 if pe32plus else 96) + index * 8


def build_image(path: bytes, *, pe32plus=True, checksum=0, cert=(0, 0),
                strong=(0, 0), cor_flags=0, extra_entries=(), rsds_pad=b"",
                size_override=None, ptr_override=None, tail=b"",
                guid=GUID, age=AGE, path_without_nul=False):
    """合成一个最小可解析的托管 PE（单节 .rdata：cor20 + debug 目录 + RSDS）。

    extra_entries 为 (dtype, size, addr, ptr) 元组序列；主 RSDS 记录的
    size/ptr 可被 size_override/ptr_override 篡改以构造畸形输入。
    返回 (data, meta)；meta 含主 RSDS 记录关键偏移与 tail 偏移。
    """
    cor = bytearray(72)
    struct.pack_into("<I", cor, 0, 72)                      # cb
    struct.pack_into("<HH", cor, 4, 6, 0)                   # runtime version
    struct.pack_into("<II", cor, 8, 0x2000, 0x10)           # MetaData
    struct.pack_into("<I", cor, 16, cor_flags)              # Flags
    struct.pack_into("<II", cor, 32, strong[0], strong[1])  # StrongNameSignature

    rsds = (b"RSDS" + guid + struct.pack("<I", age) + path
            + (b"" if path_without_nul else b"\x00") + rsds_pad)

    raw = bytearray()
    raw += cor                                              # rva SECTION_RVA+0
    dbg_off_in_raw = len(raw)
    entries = bytearray(_pack_entry(sc.DEBUG_TYPE_CODEVIEW, 0, 0, 0))  # 占位
    for extra in extra_entries:
        entries += _pack_entry(*extra)
    rsds_off_in_raw = len(raw) + len(entries)
    tail_off_in_raw = rsds_off_in_raw + len(rsds)

    size_of_data = len(rsds) if size_override is None else size_override
    rsds_rva = SECTION_RVA + rsds_off_in_raw
    rsds_ptr = (FILE_ALIGN + rsds_off_in_raw) if ptr_override is None else ptr_override
    entries[0:28] = _pack_entry(sc.DEBUG_TYPE_CODEVIEW, size_of_data, rsds_rva, rsds_ptr)

    raw += entries
    raw += rsds
    raw += tail

    dd0 = 112 if pe32plus else 96                           # 数据目录起始（optional 内偏移）
    opt = bytearray(232 if pe32plus else 216)
    struct.pack_into("<H", opt, 0, 0x20B if pe32plus else 0x10B)
    struct.pack_into("<I", opt, 64, checksum)               # CheckSum
    struct.pack_into("<I", opt, 108 if pe32plus else 92, 16)  # NumberOfRvaAndSizes
    struct.pack_into("<II", opt, dd0 + 4 * 8, cert[0], cert[1])             # 4: security
    struct.pack_into("<II", opt, dd0 + 6 * 8, SECTION_RVA + dbg_off_in_raw,
                     len(entries))                                           # 6: debug
    struct.pack_into("<II", opt, dd0 + 14 * 8, SECTION_RVA, 72)             # 14: CLR

    coff = struct.pack("<HHIIIHH", 0xAA64 if pe32plus else 0x8664, 1, 0, 0, 0,
                       len(opt), 0x2022)
    sec = struct.pack("<8sIIIIIIHHI", b".rdata\x00\x00", len(raw), SECTION_RVA,
                      len(raw), FILE_ALIGN, 0, 0, 0, 0, 0x40000040)

    dos = bytearray(0x80)
    dos[0:2] = b"MZ"
    struct.pack_into("<I", dos, 0x3C, 0x80)

    head = bytes(dos) + b"PE\x00\x00" + coff + bytes(opt) + sec
    assert len(head) <= FILE_ALIGN
    data = head + bytearray(FILE_ALIGN - len(head)) + raw

    meta = {
        "data_offset": rsds_ptr,
        "path_offset": rsds_ptr + 24,
        "field_length": size_of_data - 24,
        "size_of_data": size_of_data,
        "tail_offset": FILE_ALIGN + tail_off_in_raw,
        "n_entries": 1 + len(extra_entries),
    }
    return bytes(data), meta


def build_two_records():
    """构造含两条合法 type-2 记录的镜像：第二条 RSDS 内嵌在 tail 区。"""
    second = b"RSDS" + GUID + struct.pack("<I", 9) + SECOND_PATH + b"\x00"
    # 先粗构建拿 tail 偏移，再回填第二条 entry 的 size/ptr（addr 保持 0 占位）
    data, meta = build_image(
        LONG_PATH, tail=second,
        extra_entries=[(2, 0, 0, 0)])
    fixed = bytearray(data)
    struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 16, len(second))
    struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 24, meta["tail_offset"])
    return bytes(fixed), meta


class TestStructuralSanitize(unittest.TestCase):
    def test_happy_path_replace_containment_and_receipt(self):
        pad = b"\xAA\xBB\xCC"  # 记录尾部既有非零填充：验证被零化且 diff 不越界
        data, meta = build_image(LONG_PATH, rsds_pad=pad)
        out, records = sc.sanitize_bytes(data)
        self.assertEqual(len(records), 1)
        rec = records[0]
        self.assertEqual(rec["path_offset"], meta["path_offset"])
        self.assertEqual(rec["field_length"], meta["field_length"])
        self.assertEqual(rec["new_path"], "Demo.Module.pdb")
        self.assertEqual(rec["guid_hex"], GUID.hex())
        self.assertEqual(rec["age"], AGE)
        self.assertEqual(rec["old_path_length"], len(LONG_PATH))

        start = meta["path_offset"]
        end = start + meta["field_length"]
        expected_field = b"Demo.Module.pdb" + b"\x00" * (meta["field_length"] - 15)
        self.assertEqual(out[start:end], expected_field)
        self.assertEqual(len(out), len(data))
        diffs = [i for i in range(len(data)) if data[i] != out[i]]
        self.assertTrue(diffs)
        self.assertTrue(all(start <= i < end for i in diffs),
                        "byte diff escaped the CodeView path field")
        self.assertNotIn(b"/Users/", out)

        parsed, _preserved = sc.parse_debug_records(out)
        self.assertEqual([r.old_path for r in parsed], ["Demo.Module.pdb"])
        self.assertEqual((parsed[0].guid_hex, parsed[0].age), (GUID.hex(), AGE))

    def test_pe32_variant_supported(self):
        data, _ = build_image(LONG_PATH, pe32plus=False)
        out, _records = sc.sanitize_bytes(data)
        self.assertNotIn(b"/Users/", out)

    def test_windows_style_path_basename(self):
        data, _ = build_image(b"C:\\Build\\work\\Demo.pdb")
        out, records = sc.sanitize_bytes(data)
        self.assertEqual(records[0]["new_path"], "Demo.pdb")
        self.assertNotIn(b"C:\\Build", out)

    def test_determinism(self):
        data, _ = build_image(LONG_PATH)
        out1, _ = sc.sanitize_bytes(data)
        out2, _ = sc.sanitize_bytes(data)
        self.assertEqual(out1, out2)

    def test_multiple_rsds_records_all_processed(self):
        # 未钉住模式：多条 type-2 记录逐条枚举、逐条处理并全部入回执
        data, _meta = build_two_records()
        out, records = sc.sanitize_bytes(data)
        self.assertEqual([r["new_path"] for r in records],
                         ["Demo.Module.pdb", "Second.dll.pdb"])
        self.assertNotIn(b"/Users/", out)
        self.assertNotEqual(records[0]["path_offset"], records[1]["path_offset"])

    def test_preserved_type16_19_untouched(self):
        # .NET 标准 type-19 (PDB checksum) 与 type-16 (reproducible) 原样逐字节保留
        checksum_rec = (b"SHA256\x00" + GUID + b"\x9c" * 16)  # 39 字节，形同真实记录
        data, meta = build_image(
            LONG_PATH, tail=checksum_rec,
            extra_entries=[(19, 0, 0, 0), (16, 0, 0, 0)])
        fixed = bytearray(data)
        # 回填 type-19 entry 的 size/ptr 指向 tail 区；type-16 保持 size=0/ptr=0
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 16, len(checksum_rec))
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 24, meta["tail_offset"])
        fixed = bytes(fixed)
        out, records = sc.sanitize_bytes(fixed)
        self.assertEqual(len(records), 1)  # 仅 type-2 入脱敏回执
        # type-19 数据区逐字节不变；type-16 记录本身不变（size 0 无数据区）
        p_start, p_end = meta["tail_offset"], meta["tail_offset"] + len(checksum_rec)
        self.assertEqual(out[p_start:p_end], checksum_rec)
        # 目录项字节（两条保留 entry 共 56 字节）不变
        self.assertEqual(out[DBG_FILE_OFF + 28:DBG_FILE_OFF + 28 + 56],
                         fixed[DBG_FILE_OFF + 28:DBG_FILE_OFF + 28 + 56])
        # diff 仍只落在 type-2 路径字段
        start, end = meta["path_offset"], meta["path_offset"] + meta["field_length"]
        diffs = [i for i in range(len(fixed)) if fixed[i] != out[i]]
        self.assertTrue(diffs)
        self.assertTrue(all(start <= i < end for i in diffs))
        self.assertNotIn(b"/Users/", out)
        # 解析侧可观测到保留记录
        _recs, preserved = sc.parse_debug_records(out)
        self.assertEqual([(p.debug_type, p.size_of_data) for p in preserved],
                         [(19, len(checksum_rec)), (16, 0)])

    def test_preserved_type19_out_of_bounds_rejected(self):
        checksum_rec = b"SHA256\x00" + GUID + b"\x9c" * 16
        data, _meta = build_image(
            LONG_PATH, extra_entries=[(19, 0, 0, 0)])
        fixed = bytearray(data)
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 16, len(checksum_rec))
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 24, 0x10000000)  # 越界 ptr
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(bytes(fixed))
        self.assertIn("preserved record data out of file bounds", str(ctx.exception))

    def test_count_mismatch_vs_expected_rejected(self):
        data, _meta = build_two_records()
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data, expected_basenames=["Demo.Module.pdb"])
        self.assertIn("record count 2 != expected 1", str(ctx.exception))

    def test_wrong_basename_pin_rejected(self):
        data, _ = build_image(LONG_PATH)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data, expected_basenames=["Other.pdb"])
        self.assertIn("!= pinned", str(ctx.exception))

    def test_nonzero_checksum_rejected(self):
        data, _ = build_image(LONG_PATH, checksum=0xDEADBEEF)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("CheckSum is non-zero", str(ctx.exception))

    def test_certificate_rejected(self):
        data, _ = build_image(LONG_PATH, cert=(0x400, 0x500))
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("certificate", str(ctx.exception))

    def test_strong_name_directory_rejected(self):
        data, _ = build_image(LONG_PATH, strong=(0x1200, 8))
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("strong-name", str(ctx.exception))

    def test_strong_name_flag_rejected(self):
        data, _ = build_image(LONG_PATH, cor_flags=0x8)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("strong-name", str(ctx.exception))

    def test_not_managed_rejected(self):
        data, _ = build_image(LONG_PATH)
        fixed = bytearray(data)
        struct.pack_into("<II", fixed, _data_dir_abs(14), 0, 0)  # 抹掉 CLR 目录
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(bytes(fixed))
        self.assertIn("not a CLR managed image", str(ctx.exception))

    def test_unknown_debug_type_rejected(self):
        data, _ = build_image(LONG_PATH, extra_entries=[(1, 16, 0, 0x200)])
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("not CODEVIEW(2)", str(ctx.exception))

    def test_pointer_out_of_bounds_rejected(self):
        data, _ = build_image(LONG_PATH, ptr_override=0x10000000)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("out of file bounds", str(ctx.exception))

    def test_size_out_of_bounds_rejected(self):
        data, _ = build_image(LONG_PATH, size_override=0x100000)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("out of file bounds", str(ctx.exception))

    def test_missing_nul_terminator_rejected(self):
        data, _ = build_image(LONG_PATH, path_without_nul=True)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("missing NUL terminator", str(ctx.exception))

    def test_truncated_field_rejected(self):
        # 字段被裁到 basename+NUL 都放不下：无 NUL → 触发 terminator 门，
        # 兜住“路径字段过小”的畸形形态（fit 分支为纯防御，正常不可达）
        data, _ = build_image(b"/a/b.pdb", size_override=24 + 3)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("missing NUL terminator", str(ctx.exception))

    def test_marker_outside_path_fields_rejected(self):
        data, _ = build_image(LONG_PATH, tail=b"junk /Users/leak tail")
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(data)
        self.assertIn("outside CodeView path fields", str(ctx.exception))

    def test_marker_straddling_field_end_rejected(self):
        # 字段在标记中间截断："/Users/" 只有前缀落在字段内
        data, _ = build_image(LONG_PATH, rsds_pad=b"/Users/")
        fixed = bytearray(data)
        size = sc._u32(fixed, DBG_FILE_OFF + 16)
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 16, size - 2)
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(bytes(fixed))
        self.assertIn("outside CodeView path fields", str(ctx.exception))

    def test_overlapping_records_rejected(self):
        data, _meta = build_image(
            LONG_PATH, extra_entries=[(2, 0, 0, 0)])
        fixed = bytearray(data)
        # 第二条 entry 与第一条同数据区 → 重叠
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 16,
                         sc._u32(fixed, DBG_FILE_OFF + 16))
        struct.pack_into("<I", fixed, DBG_FILE_OFF + 28 + 24,
                         sc._u32(fixed, DBG_FILE_OFF + 24))
        with self.assertRaises(sc.SanitizeError) as ctx:
            sc.sanitize_bytes(bytes(fixed))
        self.assertIn("overlapping", str(ctx.exception))

    def test_bad_magic_rejected(self):
        with self.assertRaises(sc.SanitizeError):
            sc.parse_debug_records(b"MZnotape" + bytearray(0x200))

    def test_pins_table_shape(self):
        self.assertEqual(len(sc.PINS), 4)
        for sha, names in sc.PINS.items():
            self.assertEqual(len(sha), 64)
            self.assertEqual(len(names), 1)
            self.assertTrue(names[0].endswith(".pdb"))
        # 工具源码不含真实个人绝对路径组件（marker 定义本身只是通用前缀）
        with open(os.path.abspath(sc.__file__), "rb") as f:
            src = f.read()
        for personal in (b"Documents/Codex", b"ni/work"):
            self.assertNotIn(personal, src)


class TestCli(unittest.TestCase):
    def _run(self, *argv):
        tool = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "sanitize_codeview.py")
        return subprocess.run([sys.executable, tool, *argv],
                              capture_output=True, text=True)

    def test_unknown_hash_rejected_and_no_output_written(self):
        with tempfile.TemporaryDirectory() as tmp:
            data, _ = build_image(LONG_PATH)
            inp = os.path.join(tmp, "Demo.Module.dll")
            outp = os.path.join(tmp, "out.dll")
            rec = os.path.join(tmp, "out.json")
            with open(inp, "wb") as f:
                f.write(data)
            proc = self._run("--input", inp, "--output", outp, "--receipt", rec)
            self.assertEqual(proc.returncode, 1)
            self.assertIn("unknown input sha256", proc.stderr)
            self.assertFalse(os.path.exists(outp))
            self.assertFalse(os.path.exists(rec))

    def test_output_overwrite_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            data, _ = build_image(LONG_PATH)
            inp = os.path.join(tmp, "in.dll")
            outp = os.path.join(tmp, "out.dll")
            rec = os.path.join(tmp, "out.json")
            with open(inp, "wb") as f:
                f.write(data)
            with open(outp, "wb") as f:
                f.write(b"existing")
            proc = self._run("--input", inp, "--output", outp, "--receipt", rec)
            self.assertEqual(proc.returncode, 1)
            self.assertIn("already exists", proc.stderr)
            with open(outp, "rb") as f:
                self.assertEqual(f.read(), b"existing")  # 原文件未被触碰

    def test_receipt_overwrite_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            data, _ = build_image(LONG_PATH)
            inp = os.path.join(tmp, "in.dll")
            outp = os.path.join(tmp, "out.dll")
            rec = os.path.join(tmp, "out.json")
            with open(inp, "wb") as f:
                f.write(data)
            with open(rec, "wb") as f:
                f.write(b"{}")
            proc = self._run("--input", inp, "--output", outp, "--receipt", rec)
            self.assertEqual(proc.returncode, 1)
            self.assertIn("already exists", proc.stderr)
            self.assertFalse(os.path.exists(outp))

    def test_output_equal_input_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            data, _ = build_image(LONG_PATH)
            inp = os.path.join(tmp, "in.dll")
            with open(inp, "wb") as f:
                f.write(data)
            proc = self._run("--input", inp, "--output", inp,
                             "--receipt", os.path.join(tmp, "r.json"))
            self.assertEqual(proc.returncode, 1)
            self.assertIn("must differ from input", proc.stderr)

    def test_missing_input_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            proc = self._run("--input", os.path.join(tmp, "nope.dll"),
                             "--output", os.path.join(tmp, "o.dll"),
                             "--receipt", os.path.join(tmp, "o.json"))
            self.assertEqual(proc.returncode, 1)
            self.assertIn("not an existing regular file", proc.stderr)

    def test_no_bypass_flag_exists(self):
        proc = self._run("--help")
        self.assertEqual(proc.returncode, 0)
        for banned in ("--skip", "--force", "--allow", "--no-pin"):
            self.assertNotIn(banned, proc.stdout)


@unittest.skipUnless(os.environ.get("KEM_SANITIZE_ITG_ROOT"),
                     "set KEM_SANITIZE_ITG_ROOT to the staged install root to run")
class TestRealDllsIntegration(unittest.TestCase):
    RELS = [
        "BepInEx/core/OhMyMods.Arm64Entry.dll",
        "BepInEx/core/OhMyMods.Arm64Injection.dll",
        "BepInEx/core/OhMyMods.MacCompatibility.dll",
        "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll",
    ]

    # 真实输入读取证据（2026-09-22 对 cold-test/attempt-01 clean install 只读探查固化）：
    # 每文件 debug 目录 3 条：type-2（RSDS，含 /Users 路径，仅其 path 字段被改写）、
    # type-19（PDB checksum，39 字节，原样保留）、type-16（reproducible，size 0，原样保留）。
    EXPECTED = {
        "OhMyMods.Arm64Entry.dll":      {"path_offset": 6644,     "field": 112, "sz": 136, "p19": 0x1A64},
        "OhMyMods.Arm64Injection.dll":  {"path_offset": 7576,     "field": 120, "sz": 144, "p19": 0x1E10},
        "OhMyMods.MacCompatibility.dll": {"path_offset": 10188,   "field": 113, "sz": 137, "p19": 0x283D},
        "KingdomEnhancedMod.dll":       {"path_offset": 1153324,  "field": 127, "sz": 151, "p19": 0x1199AB},
    }

    def test_all_four_dlls_cli(self):
        root = os.environ["KEM_SANITIZE_ITG_ROOT"]
        tool = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "sanitize_codeview.py")
        with tempfile.TemporaryDirectory() as tmp:
            for rel in self.RELS:
                inp = os.path.join(root, rel)
                outp = os.path.join(tmp, os.path.basename(rel))
                rec = outp + ".receipt.json"
                proc = subprocess.run(
                    [sys.executable, tool, "--input", inp, "--output", outp,
                     "--receipt", rec], capture_output=True, text=True)
                self.assertEqual(proc.returncode, 0, proc.stderr)
                with open(inp, "rb") as f:
                    before = f.read()
                with open(outp, "rb") as f:
                    after = f.read()
                with open(rec, encoding="utf-8") as f:
                    receipt = json.load(f)
                self.assertEqual(receipt["input"]["sha256"],
                                 hashlib.sha256(before).hexdigest())
                self.assertEqual(receipt["output"]["sha256"],
                                 hashlib.sha256(after).hexdigest())
                self.assertNotIn(b"/Users/", after)
                # 证据断言：type-2 记录偏移/长度、保留记录 19/16 形态与探查一致
                exp = self.EXPECTED[os.path.basename(rel)]
                rec2 = receipt["records"][0]
                self.assertEqual(len(receipt["records"]), 1)
                self.assertEqual(rec2["path_offset"], exp["path_offset"])
                self.assertEqual(rec2["field_length"], exp["field"])
                self.assertEqual(rec2["size_of_data"], exp["sz"])
                preserved = receipt["preserved_debug_entries"]
                self.assertEqual([(p["type"], p["entry_index"]) for p in preserved],
                                 [(19, 1), (16, 2)])
                self.assertEqual(preserved[0]["size_of_data"], 39)
                self.assertEqual(preserved[0]["data_offset"], exp["p19"])
                self.assertEqual(preserved[1]["size_of_data"], 0)
                # 解析侧独立复核（不依赖回执）
                recs, pres = sc.parse_debug_records(before)
                self.assertEqual(len(recs), 1)
                self.assertEqual(recs[0].path_offset, exp["path_offset"])
                self.assertEqual([(p.debug_type, p.size_of_data) for p in pres],
                                 [(19, 39), (16, 0)])
                print(f"[evidence] {os.path.basename(rel)}: type2 off={rec2['path_offset']} "
                      f"field={rec2['field_length']} sz={rec2['size_of_data']}; "
                      f"preserved type19(off={preserved[0]['data_offset']},size=39) "
                      f"type16(size=0); diffs in {receipt['modified_ranges']}")
                ranges = [tuple(r) for r in receipt["modified_ranges"]]
                diffs = [i for i in range(len(before)) if before[i] != after[i]]
                self.assertTrue(diffs)
                self.assertTrue(all(any(s <= i < e for s, e in ranges) for i in diffs))
                self.assertNotIn("/Users/", open(rec, encoding="utf-8").read())


if __name__ == "__main__":
    unittest.main()
