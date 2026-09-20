#!/usr/bin/env python3
"""对生产校准表及导入校验执行回归；不把离线几何判断当成实机贴合。"""
import copy
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
import helmet_fit


class HelmetFitPolicyTests(unittest.TestCase):
    def setUp(self):
        self.table = helmet_fit.load_profiles()

    def test_every_profile_faces_forward_and_up(self):
        for p in self.table['profiles']:
            with self.subTest(helmet=p['baseName']):
                q = helmet_fit.quaternion(p['rotationEuler'])
                self.assertTrue(helmet_fit.near(helmet_fit.rotate(q, p['modelUp']), (0, 1, 0)))
                self.assertTrue(helmet_fit.near(helmet_fit.rotate(q, p['modelForward']), (0, 0, 1)))

    def test_meshy_batch_shares_front_axis(self):
        # 2026-09-20 实机修正：霜冠原先被记成 -Y 朝前（Y=0），实机上冠带整个朝后
        # （owner 截图 image-10）。霜冠与雷神之角是同一批 Meshy 模型，正面同为 +Y，
        # 两者都必须带 Y=180。这条断言取代了旧的「两顶头盔朝向必须不同」。
        profiles = {p['baseName']: p for p in self.table['profiles']}
        for name in ('ThunderHorn_Helmet', 'FrostCrown_Helmet'):
            self.assertEqual(profiles[name]['modelForward'], [0, 1, 0], name)
            self.assertEqual(profiles[name]['modelUp'], [0, 0, 1], name)
            self.assertEqual(profiles[name]['rotationEuler'], [-90, 180, 0], name)

    def test_confirmed_dragon_references_are_preserved(self):
        references = {p['baseName']: p for p in self.table['profiles'] if p['referenceOnly']}
        self.assertEqual(set(references), {'dargon_Helmet', 'dragonking_Helmet'})
        for name, scale in (('dargon_Helmet', [45, 50, 45]), ('dragonking_Helmet', [55, 70, 55])):
            self.assertEqual(references[name]['position'], [0, 0, 0])
            self.assertEqual(references[name]['rotationEuler'], [0, 0, 0])
            self.assertEqual(references[name]['scale'], scale)

    def test_thunder_crown_has_clearance_beyond_initial_pilot(self):
        p = next(p for p in self.table['profiles'] if p['baseName'] == 'ThunderHorn_Helmet')
        self.assertGreater(p['position'][1], 0.28, 'owner 实机反馈第一版头顶穿模')
        self.assertGreater(p['scale'][0], 55)
        self.assertGreater(p['scale'][1], 70)

    def invalid_table(self, mutate):
        table = copy.deepcopy(self.table)
        mutate(table['profiles'][0])
        with patch.object(Path, 'read_text', return_value=json.dumps(table)):
            with self.assertRaises(ValueError):
                helmet_fit.load_profiles()

    def test_rejects_lost_axis_correction(self):
        self.invalid_table(lambda p: p.update(rotationEuler=[0, 180, 0]))

    def test_rejects_nonfinite_and_nonpositive_transforms(self):
        self.invalid_table(lambda p: p.update(position=[0, float('nan'), 0]))
        self.invalid_table(lambda p: p.update(scale=[1, 0, 1]))
        self.invalid_table(lambda p: p.update(meshSize=[1, -1, 1]))

    def test_rejects_paths_outside_assets(self):
        self.invalid_table(lambda p: p.update(prefab='../other/ThunderHorn_Helmet_Model.prefab'))

    def test_import_staging_is_distinct_from_wear_pose(self):
        for p in self.table['profiles']:
            if p['bundle'] != 'skyisland_boss_gear':
                continue
            cx, cy, cz = p['meshCenter']
            sx, sy, sz = p['meshSize']
            bounds = {'widthHeightDepth': [sx, sy, sz],
                      'min': [cx-sx/2, -cz-sz/2, cy-sy/2],
                      'max': [cx+sx/2, -cz+sz/2, cy+sy/2]}
            with self.subTest(helmet=p['baseName']):
                helmet_fit.validate_staging_bounds(p['baseName'], 'Helmat', bounds, self.table)
                moved = copy.deepcopy(bounds)
                moved['min'][2] += p['position'][1]
                moved['max'][2] += p['position'][1]
                with self.assertRaises(ValueError):
                    helmet_fit.validate_staging_bounds(p['baseName'], 'Helmat', moved, self.table)
                changed = copy.deepcopy(bounds)
                changed['widthHeightDepth'][0] *= 1.2
                with self.assertRaises(ValueError):
                    helmet_fit.validate_staging_bounds(p['baseName'], 'Helmat', changed, self.table)

    def test_unknown_helmet_needs_calibration(self):
        with self.assertRaisesRegex(ValueError, '新头盔'):
            helmet_fit.validate_staging_bounds('New_Helmet', 'Helmat', {}, self.table)
        helmet_fit.validate_staging_bounds('Existing_Armor', 'Armor', {}, self.table)


if __name__ == '__main__':
    unittest.main(verbosity=2)
