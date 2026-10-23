"""Exercise corruption that still has a self-consistent file hash index."""
from pathlib import Path
import hashlib
import json
import shutil
import tempfile
import unittest

from validate_pack import validate


PACK=Path(__file__).resolve().parents[2]/'il2cpp/Assets/Palace/Palace400R12'


class PackValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'pack'
        shutil.copytree(PACK,self.root)

    def rewrite_registry(self,change):
        self.rewrite_file('registry.json',change)

    def rewrite_file(self,name,change):
        file=self.root/name;document=json.loads(file.read_text())
        change(document);file.write_text(json.dumps(document))
        index=self.root/'SHA256.json';hashes=json.loads(index.read_text())
        hashes[name]=hashlib.sha256(file.read_bytes()).hexdigest()
        index.write_text(json.dumps(hashes))

    def test_complete_pack(self):
        result=validate(self.root)
        self.assertEqual(358,result['framesReadBack'])
        self.assertFalse(result['nativeAccepted'])

    def test_extra_unindexed_file_is_rejected(self):
        (self.root/'old-plugin.dll').write_bytes(b'stale')
        with self.assertRaisesRegex(ValueError,'exactly cover'):validate(self.root)

    def test_escape_path_is_rejected_even_with_pinned_registry(self):
        self.rewrite_registry(lambda d:d['assets'][0].update(path='../outside.png'))
        with self.assertRaisesRegex(ValueError,'Nonportable path'):validate(self.root)

    def test_repacked_frame_with_wrong_crop_is_rejected(self):
        def change(d):
            a=next(a for a in d['assets'] if a['id']=='court-dancer')
            a['frames'][0]['sourceRect'][0]+=1
            a['frames'][0]['masterRect']=a['frames'][0]['sourceRect'][:]
        self.rewrite_registry(change)
        with self.assertRaisesRegex(ValueError,'Frame RGBA readback'):validate(self.root)

    def test_repacked_frame_with_other_master_is_rejected(self):
        def change(d):
            a=next(a for a in d['assets'] if a['id']=='court-dancer')
            a['frames'][0]['masterId']='other-master'
        self.rewrite_registry(change)
        with self.assertRaisesRegex(ValueError,'Frame master mismatch'):validate(self.root)

    def test_art_cannot_self_declare_native_acceptance(self):
        self.rewrite_registry(lambda d:d.update(nativeAccepted=True))
        with self.assertRaisesRegex(ValueError,'Native acceptance'):validate(self.root)

    def test_repacked_layout_cannot_reuse_offline_approval(self):
        self.assertTrue(json.loads((self.root/'registry.json').read_text())['readyForIsolatedIntegration'])
        def change(d):
            d['placements'][0]['supportWorldX']+=.25
            d['placements'][0]['designSupportPixel'][0]+=8
        self.rewrite_file('layout.json',change)
        with self.assertRaisesRegex(ValueError,'approval layout mismatch'):validate(self.root)

    def test_repacked_placement_with_wrong_world_conversion_is_rejected(self):
        def change(d):d['placements'][0]['supportWorldX']+=.25
        self.rewrite_file('layout.json',change)
        with self.assertRaisesRegex(ValueError,'Design/world placement mismatch'):validate(self.root)

    def test_repacked_placement_with_support_outside_canvas_is_rejected(self):
        def change(d):d['placements'][0]['supportLocalPixel'][1]=-1
        self.rewrite_file('layout.json',change)
        with self.assertRaisesRegex(ValueError,'Support outside canvas'):validate(self.root)


if __name__=='__main__':unittest.main()
