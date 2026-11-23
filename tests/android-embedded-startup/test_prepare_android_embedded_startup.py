"""Offline packaging boundaries; synthetic ELF retains the real patch coordinates."""
import contextlib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SCRIPT = Path(__file__).resolve().parents[2] / 'scripts/prepare_android_embedded_startup.py'
NATIVE = 'lib/arm64-v8a/libil2cpp.so'
META = 'assets/bin/Data/Managed/Metadata/global-metadata.dat'
OFFSET = 0x262c6f0


def digest(data):
    return hashlib.sha256(data).hexdigest()


def elf():
    data = bytearray(OFFSET + 16)
    struct.pack_into('<16sHHIQQQIHHHHHH', data, 0,
                     b'\x7fELF\x02\x01\x01' + bytes(9), 3, 183, 1, 0,
                     64, 0, 0, 64, 56, 3, 0, 0, 0)
    struct.pack_into('<IIQQQQQQ', data, 64 + 2 * 56,
                     1, 5, 0x1f4d630, 0x1f51630, 0,
                     len(data) - 0x1f4d630, len(data) - 0x1f4d630, 0x1000)
    data[OFFSET:OFFSET + 4] = bytes.fromhex('c04b0236')
    return bytes(data)


class PackagingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.native = elf()

    def setUp(self):
        spec = importlib.util.spec_from_file_location('startup_packager', SCRIPT)
        self.mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.mod)
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.source = self.root / 'input.apk'
        self.output = self.root / 'output.apk'
        self.entries = {
            NATIVE: self.native, META: b'synthetic metadata',
            'assets/game.data': b'game\x00resources',
            'META-INF/MANIFEST.MF': b'old manifest',
            'META-INF/CERT.SF': b'old digest',
            'META-INF/CERT.RSA': b'old rsa',
            'META-INF/CERT.DSA': b'old dsa',
            'META-INF/cert.ec': b'old ec',
            'META-INF/services/example': b'keep service',
            'META-INF/notice.txt': b'keep notice',
        }
        self.make_input()

    def make_input(self):
        with zipfile.ZipFile(self.source, 'w', zipfile.ZIP_DEFLATED) as z:
            for name, data in self.entries.items():
                z.writestr(name, data)
        self.original = self.source.read_bytes()
        self.pins = {
            'INPUT_SHA256': digest(self.original),
            'NATIVE_SHA256': digest(self.entries[NATIVE]),
            'METADATA_SHA256': digest(self.entries[META]),
        }

    def run_helper(self, output=None):
        with patch.multiple(self.mod, **self.pins):
            with contextlib.redirect_stdout(io.StringIO()) as stdout:
                code = self.mod.main([str(self.source), str(output or self.output)])
        return code, stdout.getvalue()

    def assert_rejected(self):
        with patch('tempfile.NamedTemporaryFile', side_effect=AssertionError('write before validation')):
            with self.assertRaises(ValueError):
                self.run_helper()
        self.assertEqual(list(self.root.iterdir()), [self.source])
        self.assertEqual(self.source.read_bytes(), self.original)

    def test_only_authorized_native_bytes_change_and_signatures_are_removed(self):
        # Catches an incorrect offset/opcode, dropped resources, or stale signatures.
        code, stdout = self.run_helper()
        self.assertEqual(code, 0)
        receipt = json.loads(stdout)
        self.assertTrue(receipt['unsigned'])
        self.assertEqual(receipt['output_sha256'], digest(self.output.read_bytes()))
        with zipfile.ZipFile(self.output) as z:
            expected_names = {NATIVE, META, 'assets/game.data',
                              'META-INF/services/example', 'META-INF/notice.txt'}
            self.assertEqual(set(z.namelist()), expected_names)
            result = z.read(NATIVE)
            self.assertEqual(result[:OFFSET], self.native[:OFFSET])
            self.assertEqual(result[OFFSET:OFFSET+4], bytes.fromhex('01000014'))
            self.assertEqual(result[OFFSET+4:], self.native[OFFSET+4:])
            for name in expected_names - {NATIVE}:
                self.assertEqual(z.read(name), self.entries[name])
        self.assertEqual(self.source.read_bytes(), self.original)

    def test_apk_signing_block_not_carried_into_output(self):
        # A well-shaped synthetic v2 signing block sits outside ZIP entries.
        data = bytearray(self.original)
        eocd = data.rfind(b'PK\x05\x06')
        central = struct.unpack_from('<I', data, eocd + 16)[0]
        payload = b'private-old-signer-payload'
        block_size = len(payload) + 24
        block = (struct.pack('<Q', block_size) + payload
                 + struct.pack('<Q', block_size) + b'APK Sig Block 42')
        data[central:central] = block
        struct.pack_into('<I', data, eocd + len(block) + 16, central + len(block))
        self.source.write_bytes(data)
        self.original = bytes(data)
        self.pins['INPUT_SHA256'] = digest(data)
        self.run_helper()
        self.assertNotIn(payload, self.output.read_bytes())
        self.assertEqual(self.source.read_bytes(), self.original)
        with zipfile.ZipFile(self.output) as z:
            self.assertEqual(z.read('assets/game.data'), b'game\x00resources')

    def test_missing_required_entry_rejected_before_output_creation(self):
        del self.entries[META]
        with zipfile.ZipFile(self.source, 'w', zipfile.ZIP_DEFLATED) as z:
            for name, data in self.entries.items():
                z.writestr(name, data)
        self.original = self.source.read_bytes()
        self.pins['INPUT_SHA256'] = digest(self.original)
        self.assert_rejected()

    def test_unknown_input_hash_rejected_without_output(self):
        self.pins['INPUT_SHA256'] = '0' * 64
        self.assert_rejected()

    def test_wrong_native_hash_rejected_without_output(self):
        self.pins['NATIVE_SHA256'] = '0' * 64
        self.assert_rejected()

    def test_wrong_metadata_hash_rejected_without_output(self):
        self.pins['METADATA_SHA256'] = '0' * 64
        self.assert_rejected()

    def test_wrong_instruction_rejected_even_if_hashes_match(self):
        data = bytearray(self.native)
        data[OFFSET:OFFSET+4] = bytes.fromhex('01000014')
        self.entries[NATIVE] = data
        self.make_input()
        self.assert_rejected()

    def test_invalid_elf_or_mapping_rejected_even_if_hashes_match(self):
        # Covers class, endian, machine, header, executable mapping and file bounds.
        for offset, replacement in [(0, b'BAD!'), (4, b'\x01'), (5, b'\x02'),
                                    (18, b'\x3e\x00'), (54, b'\x00\x00'),
                                    (176, b'\x00\x00\x00\x00'),
                                    (180, b'\x04\x00\x00\x00'),
                                    (184, struct.pack('<Q', 0x1f4d631)),
                                    (192, struct.pack('<Q', 0x1f51631)),
                                    (208, struct.pack('<Q', 1))]:
            with self.subTest(offset=offset):
                data = bytearray(self.native)
                data[offset:offset+len(replacement)] = replacement
                self.entries[NATIVE] = data
                self.make_input()
                self.assert_rejected()

    def test_same_path_symlink_and_hardlink_rejected(self):
        for kind in ('same', 'symlink', 'hardlink'):
            with self.subTest(kind=kind):
                dest = self.source if kind == 'same' else self.output
                if kind == 'symlink':
                    dest.symlink_to(self.source)
                elif kind == 'hardlink':
                    import os
                    os.link(self.source, dest)
                try:
                    with self.assertRaises((ValueError, OSError)):
                        self.run_helper(dest)
                    self.assertEqual(self.source.read_bytes(), self.original)
                finally:
                    if kind != 'same':
                        dest.unlink()

    def test_existing_output_and_repeat_run_never_overwrite(self):
        self.run_helper()
        saved = self.output.read_bytes()
        with self.assertRaises((ValueError, OSError)):
            self.run_helper()
        self.assertEqual(self.output.read_bytes(), saved)
        self.assertEqual(set(self.root.iterdir()), {self.source, self.output})

    def test_failed_publish_cleans_temp_and_preserves_racing_output(self):
        # os.link is the atomic publication boundary; simulate a competing creator.
        def racing_link(src, dst):
            Path(dst).write_bytes(b'other writer')
            raise FileExistsError('output appeared')
        with patch('os.link', side_effect=racing_link):
            with self.assertRaises(FileExistsError):
                self.run_helper()
        self.assertEqual(self.output.read_bytes(), b'other writer')
        self.assertEqual(set(self.root.iterdir()), {self.source, self.output})
        self.assertEqual(self.source.read_bytes(), self.original)

    def test_failed_zip_write_cleans_temp(self):
        def fail_during_write(*args, **kwargs):
            pending = list(self.root.glob('.output.apk.*.tmp'))
            self.assertEqual(len(pending), 1)
            self.assertTrue(pending[0].is_file())
            raise OSError('disk failure')

        with patch('zipfile.ZipFile.writestr', side_effect=fail_during_write):
            with self.assertRaises(OSError):
                self.run_helper()
        self.assertEqual(list(self.root.iterdir()), [self.source])


if __name__ == '__main__':
    unittest.main()
