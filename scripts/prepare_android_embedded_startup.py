#!/usr/bin/env python3
"""Prepare the frozen private embedded-resource APK for existing Patcher signing.

Usage: python3 scripts/prepare_android_embedded_startup.py INPUT OUTPUT
The output is unsigned. This is deliberately not a general APK patching tool.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import sys
import tempfile
import zipfile

PROFILES = {
    'installed-base-ef575': 'ef575e6c4fd14a473efd6aeacca61bd22ca4d401ca18721e61f918428d833a4e',
    'fulldlc-4b40': '4b40edd3e7eefb6df43e6777d566bda366179b5e245dd8f20e87810b0b56732d',
}
NATIVE_SHA256 = '774871434d39fe5145f05c9dc51101d973f0bf7dc63bba0f062d7c0848104157'
METADATA_SHA256 = 'ef1cc0cce412d3f3402cbfcb5a5e2ef1ca0d6ff47c4a90a705909f8301deae19'
NATIVE_ENTRY = 'lib/arm64-v8a/libil2cpp.so'
METADATA_ENTRY = 'assets/bin/Data/Managed/Metadata/global-metadata.dat'
PATCH_VA = 0x26306f0
PATCH_OFFSET = 0x262c6f0
ORIGINAL = bytes.fromhex('c04b0236')
REPLACEMENT = bytes.fromhex('01000014')
OLD_SIGNATURE = re.compile(r'META-INF/(?:MANIFEST\.MF|[^/]+\.(?:SF|RSA|DSA|EC))', re.I)


def resolve_profile(input_sha):
    for name, sha in PROFILES.items():
        if input_sha == sha:
            return name
    raise ValueError('unsupported input APK SHA256')


def stream_sha256(stream):
    result = hashlib.sha256()
    while chunk := stream.read(1024 * 1024):
        result.update(chunk)
    return result.hexdigest()


def validate_native(data):
    """Check ELF64 little-endian AArch64 and the unique executable target mapping."""
    if len(data) < 64 or data[:7] != b'\x7fELF\x02\x01\x01':
        raise ValueError('native is not ELF64 little-endian version 1')
    header = struct.unpack_from('<16sHHIQQQIHHHHHH', data)
    _, elf_type, machine, version, _, phoff, _, _, ehsize, phsize, phnum, *_ = header
    if (elf_type, machine, version, ehsize, phsize) != (3, 183, 1, 64, 56):
        raise ValueError('unexpected ELF header')
    if phnum < 3 or phoff < 64 or phoff + phsize * phnum > len(data):
        raise ValueError('invalid ELF program header table')
    mappings = []
    for index in range(phnum):
        kind, flags, offset, va, _, size, memsize, _ = struct.unpack_from(
            '<IIQQQQQQ', data, phoff + index * phsize)
        if kind != 1:
            continue
        if size > memsize or offset + size > len(data):
            raise ValueError('invalid ELF PT_LOAD bounds')
        if not (flags & 1) or size == 0:
            continue
        if va <= PATCH_VA <= va + size - 4:
            mappings.append((index, flags, offset, va, size))
    if len(mappings) != 1:
        raise ValueError('patch target not in exactly one executable file-backed PT_LOAD')
    index, flags, offset, va, size = mappings[0]
    if (index, flags, offset, va) != (2, 5, 0x1f4d630, 0x1f51630):
        raise ValueError('unexpected executable PT_LOAD mapping')
    if offset + PATCH_VA - va != PATCH_OFFSET:
        raise ValueError('patch file offset does not match ELF PT_LOAD')
    if not (offset <= PATCH_OFFSET and PATCH_OFFSET + 4 <= offset + size):
        raise ValueError('patch outside executable file-backed PT_LOAD')
    if data[PATCH_OFFSET:PATCH_OFFSET + 4] != ORIGINAL:
        raise ValueError('unexpected original instruction')
    return {
        'mapping_index': index,
        'segment_file_offset': offset,
        'segment_va': va,
        'segment_file_size': size,
        'segment_flags': flags,
    }


def prepare(source, output):
    source = Path(source)
    output = Path(output)
    if source.resolve() == output.resolve():
        raise ValueError('input and output overlap')
    if os.path.lexists(output):
        raise FileExistsError('output already exists')
    # Hash and ZIP reads use the same open source; no write is made before validation.
    with source.open('rb') as raw:
        input_sha = stream_sha256(raw)
        profile = resolve_profile(input_sha)
        raw.seek(0)
        with zipfile.ZipFile(raw) as original:
            try:
                native = original.read(NATIVE_ENTRY)
                with original.open(METADATA_ENTRY) as metadata:
                    metadata_sha = stream_sha256(metadata)
            except KeyError as exc:
                raise ValueError('required native or metadata entry missing') from exc
            if hashlib.sha256(native).hexdigest() != NATIVE_SHA256:
                raise ValueError('unexpected native SHA256')
            if metadata_sha != METADATA_SHA256:
                raise ValueError('unexpected metadata SHA256')
            native_mapping = validate_native(native)
            patched = bytearray(native)
            patched[PATCH_OFFSET:PATCH_OFFSET + 4] = REPLACEMENT
            removed = []
            temporary = None
            try:
                with tempfile.NamedTemporaryFile(dir=output.parent,
                                                 prefix='.' + output.name + '.',
                                                 suffix='.tmp', delete=False) as pending:
                    temporary = Path(pending.name)
                    # A fresh archive omits the APK signing block by construction.
                    with zipfile.ZipFile(pending, 'w', allowZip64=True) as rewritten:
                        rewritten.comment = original.comment
                        for entry in original.infolist():
                            if OLD_SIGNATURE.fullmatch(entry.filename):
                                removed.append(entry.filename)
                                continue
                            info = copy.copy(entry)
                            if entry.filename == NATIVE_ENTRY:
                                rewritten.writestr(info, patched)
                            else:
                                with original.open(entry) as src, rewritten.open(info, 'w') as dst:
                                    shutil.copyfileobj(src, dst, 1024 * 1024)
                    pending.flush()
                    os.fsync(pending.fileno())
                with temporary.open('rb') as result:
                    output_sha = stream_sha256(result)
                # Exclusive atomic publish: even a competing creator is never overwritten.
                os.link(temporary, output)
            finally:
                if temporary is not None:
                    temporary.unlink()
    return {
        'profile': profile,
        'input_sha256': input_sha, 'output_sha256': output_sha,
        'unsigned': True, 'native_entry': NATIVE_ENTRY,
        'native_mapping': native_mapping,
        'patch_offset': hex(PATCH_OFFSET), 'patch_va': hex(PATCH_VA),
        'original': ORIGINAL.hex(), 'replacement': REPLACEMENT.hex(),
        'removed_signatures': removed,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args(argv)
    receipt = prepare(args.input, args.output)
    print(json.dumps(receipt, sort_keys=True))
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, zipfile.BadZipFile) as exc:
        print(f'error: {exc}', file=sys.stderr)
        sys.exit(1)
