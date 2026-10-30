"""Windows 2.4.3 / 11.1.0 源码闭包配方与导出构建的针对性测试。

可选私有证据环境变量：
  WINDOWS243_INPUTS_MANIFEST  实际已玩 B 的 inputs-manifest.json（逐项对照）
  WINDOWS243_RUNTIME_ROOT     固定 31 DLL runtime 闭包
  WINDOWS243_INTEROP_ROOT     固定 131 DLL interop 闭包
  WINDOWS243_DOTNET           8.0.425 dotnet 可执行文件（默认 dotnet）

缺少私有闭包/清单时只跳过依赖它们的用例；配方结构与负向路径仍会执行。
"""

import importlib.util
import json
import os
import re
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "windows243_release.py"
RECIPE_PATH = ROOT / "release" / "windows243-11.1.0" / "source-recipe.json"
PATCH_PATH = ROOT / "release" / "windows243-11.1.0" / "ModPanel.patch"

SPEC = importlib.util.spec_from_file_location("windows243_release", SCRIPT)
REL = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(REL)

RECIPE = json.loads(RECIPE_PATH.read_text(encoding="utf-8"))
INPUTS_MANIFEST = os.environ.get("WINDOWS243_INPUTS_MANIFEST")
RUNTIME_ROOT = os.environ.get("WINDOWS243_RUNTIME_ROOT")
INTEROP_ROOT = os.environ.get("WINDOWS243_INTEROP_ROOT")
DOTNET = os.environ.get("WINDOWS243_DOTNET", "dotnet")
ROOTS_OK = bool(RUNTIME_ROOT and INTEROP_ROOT
                and Path(RUNTIME_ROOT).is_dir() and Path(INTEROP_ROOT).is_dir())
HOST_PATH_RE = re.compile(r"(?:^|[\"'\s])(?:[A-Za-z]:[\\/]|/(?:Users|home|Volumes)/)")


def git_ready() -> bool:
    for commit in RECIPE["provenance"]["commits"].values():
        process = subprocess.run(
            ["git", "-C", str(ROOT), "cat-file", "-e", commit], capture_output=True)
        if process.returncode != 0:
            return False
    return True


def copy_recipe() -> dict:
    return json.loads(json.dumps(RECIPE))


def source_by_name(recipe: dict) -> dict:
    return {Path(entry["path"]).name: entry for entry in recipe["sources"]}


def reference_by_name(recipe: dict) -> dict:
    return {entry["logicalName"]: entry for entry in recipe["references"]}


def find_values(root: ET.Element, tag: str):
    return [node.text.strip() for node in root.iter(tag) if node.text and node.text.strip()]


class RecipeContractTests(unittest.TestCase):
    def test_counts_and_pinned_commits(self):
        counts = RECIPE["provenance"]["counts"]
        self.assertEqual(counts["sources"], 287)
        self.assertEqual(counts["resources"], 38)
        self.assertEqual(counts["references"], 162)
        self.assertEqual(counts["gitContentSources"], 285)
        self.assertEqual(counts["patchedSources"], 1)
        self.assertEqual(counts["generatedSources"], 1)
        self.assertEqual(len(RECIPE["sources"]), 287)
        self.assertEqual(len(RECIPE["resources"]), 38)
        self.assertEqual(len(RECIPE["references"]), 162)
        self.assertEqual(
            RECIPE["provenance"]["commits"],
            {
                "base": "4543a79a556f532a1b9919121f2f6b69ad00d6a8",
                "pr282": "d7694e34e9be1e138aec4e024bf2051d10c61050",
                "gems": "dabbf4b583818a866a404cc42d5761e49ff282ac",
            })

    def test_paths_unique_and_safe(self):
        REL.validate_recipe(RECIPE)
        for collection, label in ((RECIPE["sources"], "source"),
                                  (RECIPE["resources"], "resource"),
                                  (RECIPE["references"], "reference")):
            paths = [entry.get("path") or entry.get("logicalName") for entry in collection]
            self.assertEqual(len(paths), len(set(paths)), label)

    def test_macros_and_build_properties(self):
        properties = RECIPE["properties"]
        defines = set(properties["defineConstants"])
        self.assertNotIn("VOYAGE_SAVE_DIAGNOSTICS", defines)
        self.assertNotIn("ANDROID", defines)
        for required in ("IL2CPP", "BIE", "BIE6", "DEBUG", "TRACE", "KTC_GAME_243"):
            self.assertIn(required, defines)
        self.assertTrue(properties["optimize"])
        self.assertEqual(properties["debugType"], "portable")
        self.assertTrue(properties["debugSymbols"])
        self.assertFalse(properties["pdbShipped"])
        self.assertTrue(properties["sanitizeCodeView"])
        self.assertTrue(properties["noDeploy"])
        self.assertFalse(properties["includeSourceRevisionInInformationalVersion"])
        self.assertEqual(properties["version"], "11.1.0")
        self.assertEqual(properties["assemblyVersion"], "11.1.0.0")
        self.assertEqual(properties["fileVersion"], "11.1.0.0")
        self.assertEqual(RECIPE["sdk"]["version"], "8.0.425")

    def test_reference_closure_shape(self):
        references = RECIPE["references"]
        self.assertEqual(sum(1 for x in references if x["root"] == "runtime"), 31)
        self.assertEqual(sum(1 for x in references if x["root"] == "interop"), 131)
        aliased = {x["logicalName"]: x["aliases"] for x in references if x.get("aliases")}
        self.assertEqual(aliased, {"MonoMod.Backports.dll": "backports"})

    def test_exactly_two_source_differences_from_installed_b(self):
        differences = {Path(x["path"]).name for x in RECIPE["sourceDifferences"]}
        self.assertEqual(differences, {"KingdomEnhancedPlugin.cs", "MyPluginInfo.cs"})
        sources = source_by_name(RECIPE)
        self.assertIn("ModPanel.cs", sources)
        self.assertEqual(len(sources["ModPanel.cs"]["edits"]), 3)
        self.assertNotIn("RoleTiming", "".join(sources))
        self.assertNotIn("RoleTiming", "".join(x["path"] for x in RECIPE["sources"]))

    def test_public_artifacts_have_no_host_paths(self):
        text = RECIPE_PATH.read_text(encoding="utf-8") + PATCH_PATH.read_text(encoding="utf-8")
        self.assertIsNone(HOST_PATH_RE.search(text))


@unittest.skipUnless(INPUTS_MANIFEST, "WINDOWS243_INPUTS_MANIFEST not set")
class InstalledInputProofTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads(Path(INPUTS_MANIFEST).read_text(encoding="utf-8"))
        cls.items = cls.manifest["orderedItems"]

    def test_compile_inputs_match_recipe_with_only_two_release_differences(self):
        actual = {Path(x["path"]).name: x for x in self.items["Compile"]}
        recipe = source_by_name(RECIPE)
        self.assertEqual(len(actual), 287)
        self.assertEqual(set(actual), set(recipe))
        expected_differences = {"KingdomEnhancedPlugin.cs", "MyPluginInfo.cs"}
        for name, recipe_entry in recipe.items():
            if name == "ModPanel.cs":
                self.assertEqual(recipe_entry["sha256"], actual[name]["sha256"])
            elif name in expected_differences:
                self.assertNotEqual(recipe_entry["sha256"], actual[name]["sha256"], name)
            else:
                self.assertEqual(recipe_entry["sha256"], actual[name]["sha256"], name)

    def test_resource_inputs_match_recipe(self):
        actual = {Path(x["path"]).name: x for x in self.items["EmbeddedResource"]}
        recipe = {Path(x["path"]).name: x for x in RECIPE["resources"]}
        self.assertEqual(len(actual), 38)
        self.assertEqual(set(actual), set(recipe))
        for name, recipe_entry in recipe.items():
            self.assertEqual(recipe_entry["sha256"], actual[name]["sha256"], name)
            self.assertEqual(recipe_entry["logicalName"], actual[name]["logicalName"], name)

    def test_reference_inputs_match_recipe(self):
        actual = {Path(x["path"]).name: x for x in self.items["Reference"]}
        recipe = reference_by_name(RECIPE)
        self.assertEqual(len(actual), 162)
        self.assertEqual(set(actual), set(recipe))
        for name, recipe_entry in recipe.items():
            self.assertEqual(recipe_entry["sha256"], actual[name]["sha256"], name)
            self.assertEqual(recipe_entry["include"], actual[name]["identity"], name)
            self.assertEqual(recipe_entry.get("aliases"), actual[name].get("aliases"), name)
            expected_root = "interop" if "/interop/" in actual[name]["path"] else "runtime"
            self.assertEqual(recipe_entry["root"], expected_root, name)

    def test_installed_project_reference_and_diagnostics(self):
        self.assertEqual(RECIPE["verifiedAgainst"]["installedProjectSha256"],
                         self.manifest["projectSha256"])
        defines = set(self.manifest["properties"]["DefineConstants"].split(";"))
        self.assertIn("VOYAGE_SAVE_DIAGNOSTICS", defines)
        self.assertNotIn("VOYAGE_SAVE_DIAGNOSTICS", RECIPE["properties"]["defineConstants"])
        self.assertEqual(self.manifest["properties"]["Optimize"], "true")
        deltas = {x["kind"] for x in RECIPE["verifiedAgainst"]["expectedSemanticDeltas"]}
        self.assertEqual(deltas, {"removed-debug-type", "compiler-generated-closure-rename", "release-metadata"})


@unittest.skipUnless(ROOTS_OK and git_ready(), "runtime/interop closure or pinned Git commits missing")
class ExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(prefix="windows243-export-")
        cls.out = Path(cls.tmp.name) / "export"
        cls.zip = Path(cls.tmp.name) / "source.zip"
        cls.result = REL.export_source(
            RECIPE_PATH, cls.out, ROOT, Path(RUNTIME_ROOT), Path(INTEROP_ROOT), zip_path=cls.zip)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_export_counts(self):
        self.assertEqual(self.result["sources"], 287)
        self.assertEqual(self.result["resources"], 38)
        self.assertEqual(self.result["references"], 162)
        self.assertEqual(len(list((self.out / "source").glob("*.cs"))), 287)
        self.assertEqual(len(list((self.out / "assets").glob("*.png"))), 38)

    def test_project_items_and_properties(self):
        root = ET.parse(self.out / "Release.csproj").getroot()
        self.assertEqual(len(root.findall(".//Compile")), 287)
        references = root.findall(".//Reference")
        self.assertEqual(len(references), 162)
        for reference in references:
            self.assertEqual(reference.findtext("Private"), "false")
            hint = reference.findtext("HintPath")
            if reference.get("Include") == "MonoMod.Backports":
                self.assertTrue(hint.startswith("$(RuntimeRoot)/"), hint)
            else:
                self.assertTrue(hint.startswith(("$(RuntimeRoot)/", "$(InteropRoot)/")), hint)
        self.assertEqual(sum(1 for x in references if x.findtext("HintPath").startswith("$(RuntimeRoot)/")), 31)
        self.assertEqual(sum(1 for x in references if x.findtext("HintPath").startswith("$(InteropRoot)/")), 131)
        self.assertEqual(root.findtext(".//Optimize"), "true")
        self.assertEqual(root.findtext(".//DebugType"), "portable")
        self.assertEqual(root.findtext(".//DebugSymbols"), "true")
        self.assertEqual(root.findtext(".//NoDeploy"), "true")
        self.assertIsNotNone(root.find(".//BepInExPluginsPath"))
        self.assertEqual(root.findtext(".//BepInExPluginsPath"), "")
        self.assertEqual(root.findtext(".//IncludeSourceRevisionInInformationalVersion"), "false")
        self.assertNotIn("VOYAGE_SAVE_DIAGNOSTICS", (self.out / "props" / "ProjectSettings.shared.props").read_text())
        assets = ET.parse(self.out / "props" / "EmbeddedAssets.props").getroot()
        self.assertEqual(len(assets.findall(".//EmbeddedResource")), 38)
        for resource in assets.findall(".//EmbeddedResource"):
            self.assertTrue(resource.get("Include").startswith("$(MSBuildThisFileDirectory)../assets/"))
            self.assertTrue(resource.get("LogicalName"))
        for path in (self.out / "Release.csproj", self.out / "props" / "ProjectSettings.shared.props",
                     self.out / "props" / "EmbeddedAssets.props", self.out / "global.json"):
            self.assertIsNone(HOST_PATH_RE.search(path.read_text(encoding="utf-8")), path)

    def test_source_and_asset_bytes(self):
        recipe = source_by_name(RECIPE)
        self.assertEqual(
            REL.sha256_file(self.out / "source" / "ModPanel.cs"),
            recipe["ModPanel.cs"]["sha256"])
        plugin = (self.out / "source" / "KingdomEnhancedPlugin.cs").read_text(encoding="utf-8")
        self.assertIn("build=11.1.0-win-x64", plugin)
        self.assertNotIn("10.8.35-native-scale-farmcats-20261001", plugin)
        info = (self.out / "source" / "MyPluginInfo.cs").read_text(encoding="utf-8")
        self.assertIn('PLUGIN_VERSION="11.1.0"', info)
        for entry in RECIPE["resources"]:
            self.assertEqual(REL.sha256_file(self.out / entry["path"]), entry["sha256"])

    def test_building_has_no_missing_patch_link(self):
        building = (self.out / "BUILDING.md").read_text(encoding="utf-8")
        self.assertNotIn("ModPanel.patch", building)
        self.assertIn("No separate patch file is required", building)
        self.assertIn("source-recipe.json", building)

    def test_verify_only_reads_git_sources_and_resources(self):
        result = REL.verify_only(
            RECIPE_PATH, ROOT, Path(RUNTIME_ROOT), Path(INTEROP_ROOT))
        self.assertEqual(result["sources"], 287)
        self.assertEqual(result["resources"], 38)
        self.assertEqual(result["references"], 162)
        self.assertGreater(result["verifiedSourceBytes"], 0)
        self.assertGreater(result["verifiedResourceBytes"], 0)

    def test_zip_is_self_contained_and_verifiable(self):
        with zipfile.ZipFile(self.zip) as archive:
            names = set(archive.namelist())
        self.assertIn("source-recipe.json", names)
        self.assertIn("BUILDING.md", names)
        self.assertIn("verify_closure.py", names)
        self.assertIn("sanitize_debug.py", names)
        self.assertIn("global.json", names)
        self.assertFalse(any(name.startswith((".git/", "bin/", "obj/")) for name in names))
        process = subprocess.run(
            [sys.executable, str(self.out / "verify_closure.py"),
             "--recipe", str(self.out / "source-recipe.json"),
             "--runtime-root", RUNTIME_ROOT, "--interop-root", INTEROP_ROOT],
            capture_output=True, text=True)
        self.assertEqual(process.returncode, 0, process.stderr)
        self.assertIn("total=162", process.stdout)


@unittest.skipUnless(ROOTS_OK and git_ready(), "runtime/interop closure or pinned Git commits missing")
class NegativeExportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="windows243-negative-")
        self.base = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def write_recipe(self, recipe: dict) -> Path:
        path = self.base / "recipe.json"
        path.write_text(json.dumps(recipe, ensure_ascii=False), encoding="utf-8")
        return path

    def export(self, recipe_path: Path, output: Path = None, runtime: Path = None, interop: Path = None):
        output = output or (self.base / "export")
        return REL.export_source(
            recipe_path, output, ROOT,
            runtime or Path(RUNTIME_ROOT), interop or Path(INTEROP_ROOT))

    def test_rejects_dirty_existing_export(self):
        output = self.base / "export"
        output.mkdir()
        (output / "leftover.txt").write_text("x", encoding="utf-8")
        with self.assertRaisesRegex(REL.ReleaseError, "already exists"):
            self.export(RECIPE_PATH, output)

    def test_rejects_missing_git_source(self):
        recipe = copy_recipe()
        recipe["sources"][0]["origin"]["path"] = "il2cpp/__missing__.cs"
        with self.assertRaisesRegex(REL.ReleaseError, "missing Git blob"):
            self.export(self.write_recipe(recipe))

    def test_rejects_wrong_source_hash(self):
        recipe = copy_recipe()
        recipe["sources"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(REL.ReleaseError, "sha256 mismatch"):
            self.export(self.write_recipe(recipe))

    def test_rejects_wrong_origin_hash(self):
        recipe = copy_recipe()
        recipe["sources"][0]["origin"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(REL.ReleaseError, "origin sha256 mismatch"):
            self.export(self.write_recipe(recipe))

    def test_rejects_path_traversal(self):
        for bad in ("source/../escape.cs", "/tmp/escape.cs", "source\\escape.cs"):
            recipe = copy_recipe()
            recipe["sources"][0]["path"] = bad
            with self.assertRaisesRegex(REL.ReleaseError, "unsafe"):
                REL.validate_recipe(recipe)
        for bad in ("assets/../escape.png", "/tmp/escape.png", "assets\\escape.png"):
            recipe = copy_recipe()
            recipe["resources"][0]["path"] = bad
            with self.assertRaisesRegex(REL.ReleaseError, "unsafe"):
                REL.validate_recipe(recipe)

    def test_rejects_duplicate_source_resource_and_reference(self):
        recipe = copy_recipe()
        recipe["sources"].append(json.loads(json.dumps(recipe["sources"][0])))
        recipe["provenance"]["counts"]["sources"] += 1
        with self.assertRaisesRegex(REL.ReleaseError, "duplicate source"):
            REL.validate_recipe(recipe)
        recipe = copy_recipe()
        recipe["resources"].append(json.loads(json.dumps(recipe["resources"][0])))
        recipe["provenance"]["counts"]["resources"] += 1
        with self.assertRaisesRegex(REL.ReleaseError, "duplicate resource"):
            REL.validate_recipe(recipe)
        recipe = copy_recipe()
        recipe["references"].append(json.loads(json.dumps(recipe["references"][0])))
        recipe["provenance"]["counts"]["references"] += 1
        with self.assertRaisesRegex(REL.ReleaseError, "duplicate reference"):
            REL.validate_recipe(recipe)

    def test_rejects_missing_resource(self):
        recipe = copy_recipe()
        recipe["resources"][0]["origin"]["path"] = "il2cpp/Assets/__missing__.png"
        with self.assertRaisesRegex(REL.ReleaseError, "missing Git blob"):
            self.export(self.write_recipe(recipe))

    def test_rejects_wrong_resource_hash(self):
        recipe = copy_recipe()
        recipe["resources"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(REL.ReleaseError, "resource export sha256 mismatch"):
            self.export(self.write_recipe(recipe))

    def test_verify_rejects_wrong_git_source(self):
        recipe = copy_recipe()
        recipe["sources"][0]["origin"]["path"] = "il2cpp/__missing__.cs"
        with self.assertRaisesRegex(REL.ReleaseError, "missing Git blob"):
            REL.verify_only(self.write_recipe(recipe), ROOT, Path(RUNTIME_ROOT), Path(INTEROP_ROOT))

    def test_verify_rejects_wrong_source_hash(self):
        recipe = copy_recipe()
        recipe["sources"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(REL.ReleaseError, "sha256 mismatch"):
            REL.verify_only(self.write_recipe(recipe), ROOT, Path(RUNTIME_ROOT), Path(INTEROP_ROOT))

    def test_verify_rejects_wrong_resource_hash(self):
        recipe = copy_recipe()
        recipe["resources"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(REL.ReleaseError, "resource export sha256 mismatch"):
            REL.verify_only(self.write_recipe(recipe), ROOT, Path(RUNTIME_ROOT), Path(INTEROP_ROOT))

    def test_rejects_forbidden_macro(self):
        recipe = copy_recipe()
        recipe["properties"]["defineConstants"].append("VOYAGE_SAVE_DIAGNOSTICS")
        with self.assertRaisesRegex(REL.ReleaseError, "forbidden macro"):
            REL.validate_recipe(recipe)

    def test_rejects_unknown_commit(self):
        recipe = copy_recipe()
        recipe["sources"][0]["origin"]["commit"] = "f" * 40
        with self.assertRaisesRegex(REL.ReleaseError, "not declared"):
            REL.validate_recipe(recipe)

    def test_rejects_missing_reference(self):
        empty = self.base / "empty-runtime"
        empty.mkdir()
        with self.assertRaisesRegex(REL.ReleaseError, "missing reference"):
            self.export(RECIPE_PATH, runtime=empty)

    def test_rejects_wrong_reference_hash(self):
        recipe = copy_recipe()
        recipe["references"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(REL.ReleaseError, "sha256 mismatch"):
            self.export(self.write_recipe(recipe))


class BuildContractTests(unittest.TestCase):
    def test_pdb_path_is_export_local_and_unique(self):
        first = REL.export_pdb_path(Path("/tmp/export-a"), "KingdomEnhancedMod")
        second = REL.export_pdb_path(Path("/tmp/export-b"), "KingdomEnhancedMod")
        self.assertNotEqual(first, second)
        self.assertEqual(first, Path("/tmp/export-a/obj/pdb/KingdomEnhancedMod.pdb"))
        self.assertEqual(first.parts[-3:], ("obj", "pdb", "KingdomEnhancedMod.pdb"))

    @unittest.skipUnless(ROOTS_OK, "runtime/interop closure or dotnet missing")
    def test_sdk_check_uses_export_global_json(self):
        with tempfile.TemporaryDirectory(prefix="windows243-sdk-") as tmp:
            tmp = Path(tmp)
            export = tmp / "export"
            export.mkdir()
            (export / "global.json").write_text(
                json.dumps({"sdk": {"version": "8.0.425", "rollForward": "disable"}}),
                encoding="utf-8")
            outside = tmp / "outside"
            outside.mkdir()
            (outside / "global.json").write_text(
                json.dumps({"sdk": {"version": "99.0.0", "rollForward": "disable"}}),
                encoding="utf-8")
            previous = Path.cwd()
            try:
                os.chdir(outside)
                self.assertEqual(REL.dotnet_sdk_version(DOTNET, export), "8.0.425")
            finally:
                os.chdir(previous)

    @unittest.skipUnless(ROOTS_OK, "runtime/interop closure or dotnet missing")
    def test_rejects_rogue_output_without_glob_fallback(self):
        with tempfile.TemporaryDirectory(prefix="windows243-output-") as tmp:
            output = Path(tmp)
            (output / "Release.csproj").write_text("<Project />", encoding="utf-8")
            rogue = output / "bin" / "Other" / "KingdomEnhancedMod.dll"
            rogue.parent.mkdir(parents=True)
            rogue.write_bytes(b"not the declared output")

            def fake_run(command, **kwargs):
                stdout = "8.0.425\n" if "--version" in command else "0 Warning(s)\n0 Error(s)\n"
                return subprocess.CompletedProcess(command, 0, stdout, "")

            with mock.patch.object(REL.subprocess, "run", side_effect=fake_run):
                with self.assertRaisesRegex(REL.ReleaseError, "missing exact build output"):
                    REL.build_export(
                        output, DOTNET, Path("/tmp/runtime"), Path("/tmp/interop"),
                        "KingdomEnhancedMod")


@unittest.skipUnless(ROOTS_OK and git_ready(), "runtime/interop closure or pinned Git commits missing")
class BuildTests(unittest.TestCase):
    def test_direct_build_and_zip_rebuild_are_zero_warning_zero_error(self):
        with tempfile.TemporaryDirectory(prefix="windows243-build-") as tmp:
            tmp = Path(tmp)
            export = tmp / "export"
            archive = tmp / "source.zip"
            direct = REL.export_source(
                RECIPE_PATH, export, ROOT, Path(RUNTIME_ROOT), Path(INTEROP_ROOT),
                zip_path=archive, build=True, dotnet=DOTNET)
            self.assertEqual(direct["build"]["warnings"], 0)
            self.assertEqual(direct["build"]["errors"], 0)
            self.assertEqual(direct["build"]["debugType"], "portable")
            self.assertGreaterEqual(direct["build"]["codeViewEntriesRemoved"], 1)
            dll = Path(direct["build"]["dll"])
            self.assertEqual(dll, export / "bin" / "Debug" / "net6.0" / "KingdomEnhancedMod.dll")
            self.assertEqual(Path(direct["build"]["pdbPath"]), export / "obj" / "pdb" / "KingdomEnhancedMod.pdb")
            self.assertTrue(Path(direct["build"]["pdbPath"]).is_relative_to(export))
            self.assertTrue(dll.is_file())
            self.assertFalse(dll.with_suffix(".pdb").exists())
            self.assertFalse(list(export.rglob("*.pdb")))
            data = dll.read_bytes()
            self.assertNotIn(b"MusketeerVoyageSaveDiagnostics", data)
            self.assertNotIn(b"RSDS", data)
            self.assertNotIn(b".pdb", data)
            self.assertIn("build=11.1.0-win-x64".encode("utf-16le"), data)

            extracted = tmp / "extracted"
            with zipfile.ZipFile(archive) as handle:
                handle.extractall(extracted)
            check = subprocess.run(
                [sys.executable, str(extracted / "verify_closure.py"),
                 "--recipe", str(extracted / "source-recipe.json"),
                 "--runtime-root", RUNTIME_ROOT, "--interop-root", INTEROP_ROOT],
                capture_output=True, text=True)
            self.assertEqual(check.returncode, 0, check.stderr)

            raw = subprocess.run(
                [DOTNET, "build", "Release.csproj", "-c", "Debug", "-m:1", "-v:minimal",
                 "-nologo", "--disable-build-servers",
                 f"-p:RuntimeRoot={RUNTIME_ROOT}", f"-p:InteropRoot={INTEROP_ROOT}",
                 "-p:NoDeploy=true", "-p:BepInExPluginsPath=",
                 "-p:UseSharedCompilation=false", "-p:NuGetAudit=false"],
                cwd=extracted, capture_output=True, text=True)
            raw_output = (raw.stdout or "") + (raw.stderr or "")
            self.assertEqual(raw.returncode, 0, raw_output)
            self.assertIn("0 Warning(s)", raw_output)
            self.assertIn("0 Error(s)", raw_output)
            rebuilt_dll = extracted / "bin" / "Debug" / "net6.0" / "KingdomEnhancedMod.dll"
            self.assertTrue(rebuilt_dll.is_file())
            self.assertTrue(rebuilt_dll.with_suffix(".pdb").exists())

            sanitize = subprocess.run(
                [sys.executable, str(extracted / "sanitize_debug.py"), str(rebuilt_dll)],
                capture_output=True, text=True)
            self.assertEqual(sanitize.returncode, 0, sanitize.stderr)
            self.assertIn("debug entries removed:", sanitize.stdout)
            for pdb in extracted.rglob("*.pdb"):
                pdb.unlink()
            self.assertFalse(list(extracted.rglob("*.pdb")))
            rebuilt_data = rebuilt_dll.read_bytes()
            self.assertNotIn(b"MusketeerVoyageSaveDiagnostics", rebuilt_data)
            self.assertNotIn(b"RSDS", rebuilt_data)
            self.assertNotIn(b".pdb", rebuilt_data)
            self.assertIn("build=11.1.0-win-x64".encode("utf-16le"), rebuilt_data)


if __name__ == "__main__":
    unittest.main(verbosity=2)
