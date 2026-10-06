"""Run with KEM_DOTNET=/path/to/dotnet python3 -m unittest discover -s tests/mac-embedded-assets -v."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('wiring', REPO / 'scripts/sync_mac_embedded_assets.py')
wiring = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wiring)
DOTNET = os.environ.get('KEM_DOTNET', shutil.which('dotnet'))


class WiringTests(unittest.TestCase):
    def setUp(self):
        temp_root = REPO / 'test-tmp'
        temp_root.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=temp_root)
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name).resolve()
        self.source = self.base / 'canonical source/il2cpp'
        (self.source / 'Assets').mkdir(parents=True)
        (self.source / 'Assets/One.png').write_bytes(b'first resource')
        (self.source / 'EmbeddedAssets.props').write_text('<Project><ItemGroup><EmbeddedResource Include="$(MSBuildThisFileDirectory)Assets/One.png" LogicalName="KEM.One.png" /></ItemGroup></Project>')
        self.wrapper = self.base / 'wrapper/Integration.csproj'
        self.wrapper.parent.mkdir()
        self.wrapper.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net6.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Version>10.9.38</Version></PropertyGroup><ItemGroup><Compile Include="../canonical source/il2cpp/*.cs" /><Compile Include="PluginInfo.cs" /><Reference Include="unchanged.dll" /><EmbeddedResource Include="legacy.png" LogicalName="KEM.Legacy.png" /></ItemGroup></Project>')

    def migrate(self):
        text, manifest = wiring.wire(self.wrapper)
        self.wrapper.write_text(text)
        return manifest

    def items(self, project=None, cwd=None):
        if not DOTNET:
            self.skipTest('Set KEM_DOTNET to enable real MSBuild item evaluation')
        result = subprocess.run([DOTNET, 'msbuild', str(project or self.wrapper), '-getItem:EmbeddedResource', '-nologo'], cwd=cwd or self.base, text=True, capture_output=True, check=True)
        return json.loads(result.stdout)['Items']['EmbeddedResource']

    def test_old_inline_wrapper_rejected_without_write(self):
        before = self.wrapper.read_bytes()
        with self.assertRaisesRegex(wiring.WiringError, 'no inline'):
            wiring.wire(self.wrapper, True)
        self.assertEqual(before, self.wrapper.read_bytes())

    def test_non_resource_xml_unchanged_and_idempotent(self):
        original = ET.fromstring(self.wrapper.read_text())
        self.migrate()
        after = ET.fromstring(self.wrapper.read_text())
        for tag in ('PropertyGroup', 'Compile', 'Reference'):
            self.assertEqual([ET.tostring(n) for n in original.iter(tag)], [ET.tostring(n) for n in after.iter(tag)])
        wired = self.wrapper.read_text()
        checked, manifest = wiring.wire(self.wrapper, True)
        self.assertEqual(wired, checked)
        self.assertEqual('KEM.One.png', manifest[0]['LogicalName'])

    def test_import_tracks_canonical_source_or_check_rejects(self):
        self.migrate()
        other = self.base / 'new canonical/il2cpp'
        shutil.copytree(self.source, other)
        self.wrapper.write_text(self.wrapper.read_text().replace('../canonical source/il2cpp/*.cs', '../new canonical/il2cpp/*.cs'))
        with self.assertRaisesRegex(wiring.WiringError, 'does not follow'):
            wiring.wire(self.wrapper, True)
        self.migrate()
        self.assertEqual(str(other / 'Assets/One.png'), wiring.wire(self.wrapper, True)[1][0]['FullPath'])

    def test_real_msbuild_relative_source_and_arbitrary_cwd(self):
        self.migrate()
        items = self.items(cwd=REPO)
        self.assertEqual(1, len(items))
        self.assertEqual('KEM.One.png', items[0]['LogicalName'])
        self.assertEqual(str(self.source / 'Assets/One.png'), items[0]['FullPath'])

    def test_future_item_appears_without_wrapper_rewrite(self):
        self.migrate()
        before = self.wrapper.read_bytes()
        (self.source / 'Assets/Two.png').write_bytes(b'future resource')
        props = self.source / 'EmbeddedAssets.props'
        props.write_text(props.read_text().replace('</ItemGroup>', '<EmbeddedResource Include="$(MSBuildThisFileDirectory)Assets/Two.png" LogicalName="KEM.Two.png" /></ItemGroup>'))
        self.assertEqual(['KEM.One.png', 'KEM.Two.png'], [n['LogicalName'] for n in self.items()])
        self.assertEqual(before, self.wrapper.read_bytes())
        self.assertEqual(2, len(wiring.wire(self.wrapper, True)[1]))

    def test_duplicate_logical_name_rejected(self):
        props = self.source / 'EmbeddedAssets.props'
        text = props.read_text()
        props.write_text(text.replace('</ItemGroup>', '<EmbeddedResource Include="$(MSBuildThisFileDirectory)Assets/One.png" LogicalName="KEM.One.png" /></ItemGroup>'))
        with self.assertRaisesRegex(wiring.WiringError, 'duplicate'):
            self.migrate()

    def test_missing_file_rejected(self):
        (self.source / 'Assets/One.png').unlink()
        with self.assertRaisesRegex(wiring.WiringError, 'Missing resource file'):
            self.migrate()

    def test_conditional_declarations_rejected(self):
        self.wrapper.write_text(self.wrapper.read_text().replace('<ItemGroup>', '<ItemGroup Condition="true">'))
        with self.assertRaisesRegex(wiring.WiringError, 'Conditional'):
            self.migrate()

    def test_conditional_props_rejected(self):
        props = self.source / 'EmbeddedAssets.props'
        props.write_text(props.read_text().replace('<Project>', '<Project Condition="true">'))
        with self.assertRaisesRegex(wiring.WiringError, 'Conditional'):
            self.migrate()

    def test_malformed_rejected(self):
        self.wrapper.write_text('<Project><')
        with self.assertRaises(ET.ParseError):
            self.migrate()

    def test_resource_remove_rejected(self):
        self.wrapper.write_text(self.wrapper.read_text().replace('Include="legacy.png" LogicalName="KEM.Legacy.png"', 'Remove="legacy.png"'))
        with self.assertRaisesRegex(wiring.WiringError, 'resource operation'):
            self.migrate()

    def test_cli_check_only_old_wrapper_rejected(self):
        result = subprocess.run(['python3', str(REPO / 'scripts/sync_mac_embedded_assets.py'), str(self.wrapper), '--check-only'], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertIn('no inline', result.stderr)


if __name__ == '__main__':
    unittest.main()
