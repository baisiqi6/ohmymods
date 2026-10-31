"""Run with: python3 -m unittest discover -s tests/windows243-package -v

The positive build uses a tiny 435-file fixture so it can run on macOS without
copying the real 200 MB runtime closure. The real closure is exercised by the
task-level smoke command recorded in package-worker/result.md.
"""
import argparse
import contextlib
import difflib
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock
import zipfile

REPO = Path(__file__).resolve().parents[2]
FIXTURES = Path(__file__).resolve().parent / "fixtures"

import importlib.util

_spec = importlib.util.spec_from_file_location("package_release", REPO / "scripts/package_release.py")
pkg = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(pkg)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _git(repo: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(repo), *args], capture_output=True, text=True, check=True)
    return result.stdout.strip()


class Fixture:
    def __init__(self, test: unittest.TestCase):
        self._tmp = tempfile.TemporaryDirectory()
        test.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        self.repo = self.root / "repo"
        self.runtime = self.root / "runtime"
        self.bootstrap = self.root / "bootstrap"
        self.output = self.root / "out"
        self.mod_dll = self.root / "KingdomEnhancedMod.dll"
        self.repo.mkdir()
        self.runtime.mkdir()
        self.bootstrap.mkdir()
        self._release_static()
        self._runtime_tree()
        self._write_lock()
        self._commit()

    def _release_static(self):
        release = self.repo / pkg.WIN243_RELEASE_REL
        release.mkdir(parents=True)
        source = REPO / pkg.WIN243_RELEASE_REL
        shutil.copy2(source / "BepInEx.cfg", release / "BepInEx.cfg")
        shutil.copy2(source / pkg.WIN243_NOTICE, release / pkg.WIN243_NOTICE)
        shutil.copytree(source / "launcher", release / "launcher")
        shutil.copytree(REPO / pkg.SUPPORT_TOOLS_REL, self.repo / pkg.SUPPORT_TOOLS_REL)
        (release / pkg.WIN243_DOCS[0]).write_text(
            "OhMyMods Windows 2.4.3 11.1.0\nFake fixture README.\n", encoding="utf-8")
        (release / pkg.WIN243_DOCS[1]).write_text(
            "Fake fixture update notes for 11.1.0.\n", encoding="utf-8")

    def _runtime_tree(self):
        self.runtime_entries = []
        self.bootstrap_entries = []

        def add_runtime(rel, category, data):
            path = self.runtime / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            self.runtime_entries.append({
                "category": category, "path": rel,
                "size": len(data), "sha256": digest(data),
            })

        for i in range(43):
            add_runtime(f"BepInEx/core/c{i:02}.dll", "loader", f"core-{i}".encode())
        for i in range(187):
            add_runtime(f"dotnet/d{i:03}.dll", "runtime", f"dotnet-{i}".encode())
        for i in range(72):
            add_runtime(f"BepInEx/unity-libs/u{i:02}.dll", "runtime", f"unity-{i}".encode())
        for i in range(133):
            add_runtime(f"BepInEx/interop/i{i:03}.dll", "generatedBindings", f"interop-{i}".encode())

        for name, data in (
            (".doorstop_version", b"4.3.0"),
            ("doorstop_config.ini", b"[General]\nenabled = true\n"),
            ("winhttp.dll", b"MZ bootstrap"),
        ):
            (self.bootstrap / name).write_bytes(data)
            self.bootstrap_entries.append({
                "path": name, "size": len(data), "sha256": digest(data),
            })

        self.mod_dll.write_bytes(b"MZ" + b"\x00" * 64 + "11.1.0".encode("utf-16le"))

    def _write_lock(self):
        cfg = (self.repo / pkg.WIN243_RELEASE_REL / "BepInEx.cfg").read_bytes()
        real_lock = json.loads(
            (REPO / pkg.WIN243_RELEASE_REL / "runtime-lock.json").read_text(encoding="utf-8"))
        self.lock = {
            "schema": 1,
            "platform": pkg.WIN243_PLATFORM,
            "version": pkg.WIN243_VERSION,
            "gameBuild": pkg.WIN243_GAME_BUILD,
            "releaseDirectory": pkg.WIN243_RELEASE_REL.as_posix(),
            "expectedCounts": dict(pkg.WIN243_EXPECTED_COUNTS),
            "totalRuntimeFiles": pkg.WIN243_TOTAL_RUNTIME,
            "game": real_lock["game"],
            "bootstrap": self.bootstrap_entries,
            "runtime": self.runtime_entries,
            "loaderConfig": {
                "path": "BepInEx/config/BepInEx.cfg",
                "size": len(cfg),
                "sha256": digest(cfg),
                "fixedKeys": dict(pkg.WIN243_FIXED_KEYS),
            },
            "mod": {
                "path": "BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll",
                "version": pkg.WIN243_VERSION,
                "versionProbe": ["ASCII", "UTF-16LE"],
                "metadataVerification": "root",
                "embeddedAssets": "inside DLL only; root verifies 38 resources",
            },
            "docs": list(pkg.WIN243_DOCS),
            "notice": pkg.WIN243_NOTICE,
            "launcher": {
                "directory": "launcher",
                "files": [pkg.WIN243_PS1, pkg.WIN243_CMD, pkg.WIN243_EASY_CMD],
                "catalog": pkg.WIN243_CATALOG,
            },
        }
        self.lock_path = self.repo / pkg.WIN243_RELEASE_REL / "runtime-lock.json"
        self.write_lock()

    def write_lock(self):
        self.lock_path.write_text(
            json.dumps(self.lock, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    def _commit(self):
        _git(self.repo, "init", "-q")
        _git(self.repo, "config", "user.email", "fixture@example.invalid")
        _git(self.repo, "config", "user.name", "fixture")
        _git(self.repo, "add", "-A")
        _git(self.repo, "commit", "-q", "-m", "fixture")
        self.commit = _git(self.repo, "rev-parse", "HEAD")

    def args(self, source_archive=None):
        return argparse.Namespace(
            platform=pkg.WIN243_PLATFORM,
            version=pkg.WIN243_VERSION,
            runtime_root=self.runtime,
            bootstrap_root=self.bootstrap,
            mod_dll=self.mod_dll,
            source_archive=source_archive,
            output_dir=self.output,
            commit=self.commit,
        )

    def build(self, source_archive=None):
        with contextlib.redirect_stdout(io.StringIO()):
            return pkg.win243_main(self.args(source_archive), repo=self.repo)

    def output_zip(self):
        return self.output / pkg.WIN243_OUT_NAME.format(pkg.WIN243_VERSION)


class Win243PackageTests(unittest.TestCase):
    def test_exact_package_catalog_manifest_and_readback(self):
        fixture = Fixture(self)
        result = fixture.build()
        out = Path(result["output"])
        self.assertEqual(out, fixture.output_zip())
        self.assertEqual(result["entries"], 451)
        self.assertEqual(result["runtimeFiles"], 435)
        self.assertEqual(result["generatedBindings"], 133)

        with zipfile.ZipFile(out) as zf:
            self.assertIsNone(zf.testzip())
            names = set(zf.namelist())
            self.assertEqual(len(names), 451)
            self.assertIn(pkg.WIN243_CATALOG, names)
            self.assertIn(pkg.WIN243_MANIFEST, names)
            self.assertIn("README.txt", names)
            self.assertIn("MOD_V11.1.0版本更新说明.txt", names)
            self.assertIn("NOTICE.txt", names)
            self.assertIn("BepInEx/config/BepInEx.cfg", names)
            self.assertIn("BepInEx/plugins/KingdomEnhancedMod/KingdomEnhancedMod.dll", names)
            self.assertNotIn("BepInEx/config/Other.cfg", names)

            catalog = json.loads(zf.read(pkg.WIN243_CATALOG).decode("utf-8"))
            self.assertEqual(catalog["version"], "11.1.0")
            self.assertEqual(catalog["platform"], "win243-x64")
            self.assertEqual(catalog["game"], fixture.lock["game"])
            self.assertEqual(len(catalog["payload"]), 449)
            self.assertEqual(catalog["counts"]["archiveEntries"], 451)
            self.assertEqual(catalog["counts"]["payloadFiles"], 449)
            self.assertEqual(catalog["counts"]["supportTools"], 3)
            self.assertEqual(catalog["counts"]["loader"], 43)
            self.assertEqual(catalog["counts"]["runtime"], 259)
            self.assertEqual(catalog["counts"]["generatedBindings"], 133)
            self.assertEqual(catalog["startupMutable"], ["BepInEx/config/BepInEx.cfg"])
            self.assertIn("UpdateInteropAssemblies=false", catalog["interopPolicy"])
            self.assertFalse(catalog["sourceArchive"])
            for item in catalog["payload"]:
                self.assertIn(item["path"], names)
                data = zf.read(item["path"])
                self.assertEqual(len(data), item["size"])
                self.assertEqual(digest(data), item["sha256"])
            self.assertNotIn("BepInEx/config/BepInEx.cfg",
                             {item["path"] for item in catalog["payload"]})
            self.assertIn(pkg.WIN243_MANIFEST,
                          {item["path"] for item in catalog["payload"]})
            for game in catalog["game"]:
                self.assertNotIn(game["path"], names)
            self.assertEqual(
                digest(zf.read("BepInEx/config/BepInEx.cfg")),
                fixture.lock["loaderConfig"]["sha256"])
            self.assertTrue(zf.read(pkg.WIN243_PS1).startswith(b"\xef\xbb\xbf"))
            manifest = zf.read(pkg.WIN243_MANIFEST).decode("utf-8")
            self.assertIn("ArchiveEntries: 451", manifest)
            self.assertIn("PayloadFiles: 449", manifest)
            self.assertIn("SourceArchive: none", manifest)
            self.assertIn("ModMetadataVerification: root", manifest)
            self.assertIn("EmbeddedAssets: inside DLL only", manifest)

        sidecar = Path(result["sidecar"])
        zip_sha = digest(out.read_bytes())
        self.assertEqual(result["zipSha256"], zip_sha)
        self.assertEqual(sidecar.read_text(encoding="ascii"),
                         f"{zip_sha}  {out.name}\n")

    def test_missing_required_doc_fails_without_output(self):
        fixture = Fixture(self)
        (fixture.repo / pkg.WIN243_RELEASE_REL / "README.txt").unlink()
        _git(fixture.repo, "add", "-A")
        _git(fixture.repo, "commit", "-q", "-m", "missing-doc fixture")
        fixture.commit = _git(fixture.repo, "rev-parse", "HEAD")
        with self.assertRaisesRegex(pkg.PackageError, "README.txt"):
            fixture.build()
        self.assertFalse(fixture.output_zip().exists())

    def test_wrong_runtime_hash_fails(self):
        fixture = Fixture(self)
        target = fixture.runtime / "BepInEx/core/c00.dll"
        data = bytearray(target.read_bytes())
        data[0] ^= 0xFF
        target.write_bytes(bytes(data))
        with self.assertRaisesRegex(pkg.PackageError, "sha256 mismatch"):
            fixture.build()
        self.assertFalse(fixture.output_zip().exists())

    def test_unreviewed_extra_file_fails(self):
        fixture = Fixture(self)
        (fixture.runtime / "BepInEx/core/extra.dll").write_bytes(b"unreviewed")
        with self.assertRaisesRegex(pkg.PackageError, "unreviewed files"):
            fixture.build()

    def test_symlink_runtime_file_fails(self):
        fixture = Fixture(self)
        target = fixture.runtime / "BepInEx/core/c00.dll"
        target.unlink()
        target.symlink_to(fixture.runtime / "BepInEx/core/c01.dll")
        with self.assertRaisesRegex(pkg.PackageError, "symlink"):
            fixture.build()

    def test_unsafe_lock_paths_and_duplicates_fail(self):
        fixture = Fixture(self)
        original = fixture.lock["runtime"][0]["path"]
        for bad in ("../escape.dll", "/tmp/escape.dll", "C:/escape.dll", r"dotnet\escape.dll"):
            with self.subTest(path=bad):
                fixture.lock["runtime"][0]["path"] = bad
                fixture.write_lock()
                with self.assertRaises(pkg.PackageError):
                    pkg.load_runtime_lock(fixture.repo)
        fixture.lock["runtime"][0]["path"] = original
        fixture.lock["runtime"][1]["path"] = original
        fixture.write_lock()
        with self.assertRaisesRegex(pkg.PackageError, "duplicate"):
            pkg.load_runtime_lock(fixture.repo)

    def test_loader_config_four_keys_reject_mutation(self):
        fixture = Fixture(self)
        path = fixture.repo / pkg.WIN243_RELEASE_REL / "BepInEx.cfg"
        good = path.read_text(encoding="utf-8")
        self.assertEqual(pkg.parse_fixed_interop_config(good), pkg.WIN243_FIXED_KEYS)
        mutations = {
            "value": good.replace("UpdateInteropAssemblies = false",
                                  "UpdateInteropAssemblies = true"),
            "duplicate": good + "\nUpdateInteropAssemblies = false\n",
            "missing": good.replace("GlobalMetadataPath = "
                                    "{GameDataPath}/il2cpp_data/Metadata/global-metadata.dat", ""),
            "extra-key": good.replace("[IL2CPP]\n", "[IL2CPP]\nUnknown = 1\n"),
            "extra-section": good + "\n[Logging]\nEnabled = true\n",
            "case": good.replace("[IL2CPP]", "[il2cpp]"),
        }
        for name, text in mutations.items():
            with self.subTest(name=name):
                with self.assertRaises(pkg.PackageError):
                    pkg.parse_fixed_interop_config(text)
        path.write_text(good.replace("UpdateInteropAssemblies = false",
                                     "UpdateInteropAssemblies = true"), encoding="utf-8")
        with self.assertRaises(pkg.PackageError):
            fixture.build()

    def test_commit_dirty_and_mod_version_fail(self):
        fixture = Fixture(self)
        bad_version = fixture.args()
        bad_version.version = "11.0.0"
        with self.assertRaisesRegex(pkg.PackageError, "must be 11.1.0"):
            pkg.win243_main(bad_version, repo=fixture.repo)

        fixture = Fixture(self)
        fixture.commit = "0" * 40
        with self.assertRaisesRegex(pkg.PackageError, "commit mismatch"):
            fixture.build()

        fixture = Fixture(self)
        (fixture.repo / pkg.WIN243_RELEASE_REL / "README.txt").write_text(
            "dirty\n", encoding="utf-8")
        with self.assertRaisesRegex(pkg.PackageError, "not clean"):
            fixture.build()

        fixture = Fixture(self)
        fixture.mod_dll.write_bytes(b"MZ" + b"\x00" * 64)
        with self.assertRaisesRegex(pkg.PackageError, "version string"):
            fixture.build()

    def test_source_archive_recorded_not_embedded_and_unsafe_rejected(self):
        fixture = Fixture(self)
        archive = fixture.root / "KingdomEnhancedMod_v11.1.0_Windows243_source.zip"
        with zipfile.ZipFile(archive, "w") as zf:
            zf.writestr("source/one.cs", "class One {}\n")
            zf.writestr("source/manifest.json", "{}\n")
        result = fixture.build(archive)
        self.assertEqual(result["sourceArchive"]["name"], archive.name)
        self.assertEqual(result["sourceArchive"]["sha256"], digest(archive.read_bytes()))
        with zipfile.ZipFile(result["output"]) as zf:
            self.assertNotIn(archive.name, zf.namelist())
            catalog = json.loads(zf.read(pkg.WIN243_CATALOG).decode("utf-8"))
            self.assertEqual(catalog["sourceArchive"]["name"], archive.name)
            self.assertFalse(catalog["sourceArchive"]["embedded"])
            manifest = zf.read(pkg.WIN243_MANIFEST).decode("utf-8")
            self.assertIn(f"SourceArchive: {archive.name}", manifest)

        bad = fixture.root / "KingdomEnhancedMod_v11.1.0_Windows243_bad_source.zip"
        with zipfile.ZipFile(bad, "w") as zf:
            zf.writestr("../escape.cs", "class Evil {}\n")
        with self.assertRaises(pkg.PackageError):
            fixture.build(bad)

        forbidden = fixture.root / "KingdomEnhancedMod_v11.1.0_Windows243_forbidden_source.zip"
        with zipfile.ZipFile(forbidden, "w") as zf:
            zf.writestr("GameAssembly.dll", b"MZ game")
        with self.assertRaisesRegex(pkg.PackageError, "forbidden"):
            fixture.build(forbidden)

    def test_forbidden_file_in_locked_runtime_fails(self):
        fixture = Fixture(self)
        old = fixture.runtime / "BepInEx/core/c00.dll"
        new = fixture.runtime / "BepInEx/core/GameAssembly.dll"
        old.rename(new)
        entry = next(item for item in fixture.lock["runtime"]
                     if item["path"] == "BepInEx/core/c00.dll")
        entry["path"] = "BepInEx/core/GameAssembly.dll"
        fixture.write_lock()
        _git(fixture.repo, "add", "-A")
        _git(fixture.repo, "commit", "-q", "-m", "forbidden fixture")
        fixture.commit = _git(fixture.repo, "rev-parse", "HEAD")
        with self.assertRaisesRegex(pkg.PackageError, "forbidden"):
            fixture.build()

    def test_directory_symlink_in_runtime_fails(self):
        fixture = Fixture(self)
        interop = fixture.runtime / "BepInEx/interop"
        target = fixture.runtime / "BepInEx/interop-real"
        interop.rename(target)
        interop.symlink_to(target)
        with self.assertRaisesRegex(pkg.PackageError, "symlink"):
            fixture.build()

    def test_launcher_diff_is_copy_only(self):
        fixture = Fixture(self)
        baseline_ps1 = (FIXTURES / pkg.WIN243_PS1).read_bytes()
        baseline_cmd = (FIXTURES / pkg.WIN243_CMD).read_bytes()
        new_ps1 = (fixture.repo / pkg.WIN243_RELEASE_REL / "launcher" / pkg.WIN243_PS1).read_bytes()
        new_cmd = (fixture.repo / pkg.WIN243_RELEASE_REL / "launcher" / pkg.WIN243_CMD).read_bytes()
        self.assertTrue(baseline_ps1.startswith(b"\xef\xbb\xbf"))
        self.assertTrue(new_ps1.startswith(b"\xef\xbb\xbf"))
        self.assertEqual(new_cmd, baseline_cmd)

        diff = list(difflib.unified_diff(
            baseline_ps1.decode("utf-8-sig").splitlines(),
            new_ps1.decode("utf-8-sig").splitlines(), lineterm=""))
        changed = {line for line in diff
                   if line.startswith(("+", "-")) and not line.startswith(("+++", "---"))}
        expected = {
            "-if (@(Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue).Count -ne 0) "
            "{ throw 'A KingdomTwoCrowns game is already running. Close it before starting this preview.' }",
            "+if (@(Get-Process -Name KingdomTwoCrowns -ErrorAction SilentlyContinue).Count -ne 0) "
            "{ throw 'A KingdomTwoCrowns game is already running. Close it before starting this release.' }",
            "-Write-Host ('OhMyMods '+$manifest.version+' / game 2.4.3 preview')",
            "+Write-Host ('OhMyMods '+$manifest.version+' / game 2.4.3r23537 x64')",
            "-Write-Host 'Manual gameplay validation pending. Current Windows profile saves will be used.'",
            "+Write-Host 'Windows 2.4.3 r23537 x64 release. Current game profile saves will be used.'",
        }
        self.assertEqual(changed, expected)
        easy = (fixture.repo / pkg.WIN243_RELEASE_REL / "launcher" / pkg.WIN243_EASY_CMD).read_bytes()
        self.assertTrue(easy.isascii())
        self.assertIn(b"ohmymods-win243-preview.cmd", easy)
        self.assertIn(b"call ", easy)

    def test_legacy_240_package_path_unchanged(self):
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root, True)
        repo = root / "repo"
        game = root / "game"
        (repo / "il2cpp/bin/Debug").mkdir(parents=True)
        (repo / "il2cpp").mkdir(exist_ok=True)
        (repo / "il2cpp/KingdomEnhancedMod.csproj").write_text(
            "<Project><PropertyGroup><Version>10.8.35</Version></PropertyGroup></Project>",
            encoding="utf-8")
        (repo / "il2cpp/bin/Debug/KingdomEnhancedMod.dll").write_bytes(b"MZ legacy")
        (repo / "release").mkdir()
        for name in ("更新日志.txt", "功能总览.txt", "用户指南.txt", "能力与路线图.txt",
                     "MOD_V10.8.35版本更新说明.txt"):
            (repo / "release" / name).write_text(f"{name}\n", encoding="utf-8")
        (repo / "release-notes-il2cpp.md").write_text("install\n", encoding="utf-8")
        (repo / "VERSIONING.md").write_text("version rules\n", encoding="utf-8")
        (game / "dotnet").mkdir(parents=True)
        (game / "dotnet/a.dll").write_bytes(b"dotnet")
        for dirname in ("BepInEx/core", "BepInEx/unity-libs", "BepInEx/config"):
            (game / dirname).mkdir(parents=True)
            (game / dirname / "a.dll").write_bytes(dirname.encode())
        (game / "BepInEx/config/BepInEx.cfg").write_bytes(b"[IL2CPP]\n")
        (game / "BepInEx/config/Other.cfg").write_bytes(b"personal")
        for name in (".doorstop_version", "doorstop_config.ini", "winhttp.dll"):
            (game / name).write_bytes(name.encode())
        _git(repo, "init", "-q")
        _git(repo, "config", "user.email", "fixture@example.invalid")
        _git(repo, "config", "user.name", "fixture")
        _git(repo, "add", "-A")
        _git(repo, "commit", "-q", "-m", "legacy fixture")

        with contextlib.redirect_stdout(io.StringIO()):
            with mock.patch.object(pkg, "REPO", repo), \
                    mock.patch.object(pkg, "GAME", game):
                self.assertEqual(pkg.main(["10.8.35"]), 0)
        out = repo / "release" / "KingdomEnhancedMod_v10.8.35_IL2CPP.zip"
        self.assertTrue(out.is_file())
        with zipfile.ZipFile(out) as zf:
            names = set(zf.namelist())
            self.assertIn("dotnet/a.dll", names)
            self.assertIn("BepInEx/core/a.dll", names)
            self.assertNotIn("BepInEx/config/Other.cfg", names)
            self.assertIn("BUILD-MANIFEST.txt", names)
            manifest = zf.read("BUILD-MANIFEST.txt").decode("utf-8")
            self.assertIn("ModVersion: 10.8.35", manifest)
            self.assertIn("2.4.0 IL2CPP", manifest)
        self.assertTrue(pkg.leak_check("BepInEx/interop/Assembly-CSharp.dll"))
        self.assertFalse(pkg.leak_check("winhttp.dll"))


if __name__ == "__main__":
    unittest.main()
