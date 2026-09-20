"""Read-only audit of the actual 2.4 E-drive GameAssembly; writes only a local receipt.

Tokens were obtained from the matching Il2CppInterop Assembly-CSharp static initializers.
Expected RVAs deliberately fail on a different game build instead of claiming compatibility.
"""
from pathlib import Path
import hashlib
import json
import mmap
import struct

game = Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/GameAssembly.dll')
methods = [
    ('Archer.IsAvailableForJob', 100663812, 0x4b2880, 320),
    ('Archer.AssignJob', 100663813, 0x4af340, 544),
    ('Archer.SetGuardSlot', 100663820, 0x4b5d20, 144),
    ('Archer.EnterGuardSlot', 100663845, 0x4b0f40, 1184),
    ('Archer.ExitGuardSlot', 100663850, 0x4b13e0, 1008),
    ('GuardSlot.ExitArcher', 100670763, 0x57f660, 320),
    ('Kingdom.DistributeTowerArchers', 100671636, 0x59da10, 832),
    ('Kingdom.FetchArchersForJob', 100671637, 0x59de90, 1104),
]
with game.open('rb') as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as data:
    u16 = lambda offset: struct.unpack_from('<H', data, offset)[0]
    u32 = lambda offset: struct.unpack_from('<I', data, offset)[0]
    u64 = lambda offset: struct.unpack_from('<Q', data, offset)[0]
    pe = u32(0x3c)
    optional = pe + 24
    base = u64(optional + 24)
    sections = []
    for index in range(u16(pe + 6)):
        offset = optional + u16(pe + 20) + 40 * index
        _, va, size, raw = struct.unpack_from('<IIII', data, offset + 8)
        sections.append((va, size, raw))
    def raw_address(va):
        for start, size, raw in sections:
            if start <= va - base < start + size:
                return raw + va - base - start
        raise ValueError(hex(va))
    def virtual_address(offset):
        for va, size, raw in sections:
            if raw <= offset < raw + size:
                return base + va + offset - raw
        raise ValueError(offset)
    name = data.find(b'Assembly-CSharp.dll\0')
    assert name >= 0
    module = data.find(struct.pack('<Q', virtual_address(name)))
    assert module >= 0 and module % 8 == 0
    count = u64(module + 8)
    assert 1000 < count < 100000
    table = raw_address(u64(module + 16))
    pointers = [u64(table + index * 8) for index in range(count)]
    receipt = []
    for name, token, expected_rva, expected_size in methods:
        pointer = pointers[(token & 0xffffff) - 1]
        rva = pointer - base
        size = min(value for value in pointers if value > pointer) - pointer
        same_slots = pointers.count(pointer)
        assert (rva, size, same_slots) == (expected_rva, expected_size, 1), name
        offset = raw_address(pointer)
        receipt.append(dict(name=name, token=token, rva=hex(rva), adjacent_method_span=size,
                            assembly_csharp_slots=same_slots, first_32_bytes=data[offset:offset+32].hex()))
    result = dict(game=str(game), sha256=hashlib.sha256(data).hexdigest(), methods=receipt,
                  boundary='Static file audit only; not an installed detour or gameplay test.')
Path(__file__).with_name('native-audit.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print('PASS actual 2.4 native addresses, spans and same-address slot counts for 8 methods')
