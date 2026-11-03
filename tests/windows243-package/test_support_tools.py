"""Support collector packaging contract; no real saves, runtime or network."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from test_package_release import Fixture, pkg, digest


class SupportFixture(Fixture):
    def _release_static(self):
        super()._release_static()
        root = self.repo / "release/support-tools/windows"
        root.mkdir(parents=True, exist_ok=True)
        files = {
            "OhMyMods-CollectDiagnostics.ps1": b"\xef\xbb\xbfparam()\r\nWrite-Host 'fixture'\r\n",
            "一键收集排查资料.cmd": b'@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0OhMyMods-CollectDiagnostics.ps1"\r\n',
            "排查资料工具说明.txt": "合成工具说明，非玩家数据。\n".encode(),
        }
        for name, data in files.items():
            (root / name).write_bytes(data)
        (root / "manifest.json").write_text(json.dumps({
            "schema": 1, "version": "1.0.0", "files": [
                {"path": name, "size": len(data), "sha256": digest(data)}
                for name, data in sorted(files.items())
            ],
        }, ensure_ascii=False), encoding="utf-8")
        self.support = root


class SupportToolPackagingTests(unittest.TestCase):
    def test_main_and_addon_contain_identical_pinned_tools(self):
        f = SupportFixture(self)
        main = f.build()
        addon = pkg.build_support_tools_zip(f.repo, f.root / "addon")
        with zipfile.ZipFile(main["output"]) as a, zipfile.ZipFile(addon["output"]) as b:
            expected = pkg.load_support_tools(f.repo)
            self.assertEqual(set(b.namelist()), set(expected))
            for name, data in expected.items():
                self.assertEqual(a.read(name), data)
                self.assertEqual(b.read(name), data)
            catalog = json.loads(a.read(pkg.WIN243_CATALOG))
            self.assertEqual(set(catalog["categories"]["supportTools"]), set(expected))
            rows = {r["path"]: r for r in catalog["payload"]}
            for name, data in expected.items():
                self.assertEqual(rows[name]["sha256"], digest(data))
            self.assertEqual(catalog["counts"]["archiveEntries"], 451)
            self.assertEqual(catalog["counts"]["payloadFiles"], 449)
            self.assertIn("SupportToolFiles: 3", a.read(pkg.WIN243_MANIFEST).decode())
        self.assertEqual(Path(addon["sidecar"]).read_text().split()[0],
                         digest(Path(addon["output"]).read_bytes()))

    def test_source_hash_mismatch_rejected_before_any_zip(self):
        f = SupportFixture(self)
        (f.support / "OhMyMods-CollectDiagnostics.ps1").write_bytes(b"modified")
        with self.assertRaisesRegex(pkg.PackageError, "support.*(hash|size)"):
            pkg.build_support_tools_zip(f.repo, f.root / "addon")
        self.assertFalse((f.root / "addon").exists())

    def test_missing_extra_symlink_and_duplicate_sources_rejected(self):
        for mode in ("missing", "extra", "symlink", "duplicate", "escape", "bom"):
            with self.subTest(mode=mode):
                f = SupportFixture(self)
                path = f.support / "OhMyMods-CollectDiagnostics.ps1"
                lock = f.support / "manifest.json"
                raw = json.loads(lock.read_text())
                if mode == "missing":
                    path.unlink()
                elif mode == "extra":
                    (f.support / "personal.txt").write_text("do not package")
                elif mode == "symlink":
                    path.unlink()
                    path.symlink_to(f.mod_dll)
                elif mode == "duplicate":
                    raw["files"].append(dict(raw["files"][0]))
                    lock.write_text(json.dumps(raw))
                elif mode == "escape":
                    raw["files"][0]["path"] = "../personal.txt"
                    lock.write_text(json.dumps(raw))
                else:
                    content = path.read_bytes()[3:]
                    path.write_bytes(content)
                    for row in raw["files"]:
                        if row["path"] == path.name:
                            row.update(size=len(content), sha256=digest(content))
                    lock.write_text(json.dumps(raw))
                with self.assertRaises(pkg.PackageError):
                    pkg.load_support_tools(f.repo)

    def test_addon_repeat_never_overwrites_and_is_reproducible(self):
        f = SupportFixture(self)
        first = pkg.build_support_tools_zip(f.repo, f.root / "a")
        second = pkg.build_support_tools_zip(f.repo, f.root / "b")
        old = Path(first["output"]).read_bytes()
        self.assertEqual(old, Path(second["output"]).read_bytes())
        with self.assertRaisesRegex(pkg.PackageError, "already exists"):
            pkg.build_support_tools_zip(f.repo, f.root / "a")
        self.assertEqual(old, Path(first["output"]).read_bytes())


if __name__ == "__main__":
    unittest.main()
