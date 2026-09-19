#!/usr/bin/env python3
"""用真实装备表和真实 save_texture 函数核对装备导出尺寸；无需 Blender 或图像资产。"""
import ast
from pathlib import Path
import types
import unittest

ROOT = Path(__file__).resolve().parents[1]
IMPORTER = ROOT / 'tools/sky_island_boss_gear_import.py'
SHARED = ROOT / 'tools/sky_island_tripo_import.py'


def load_production_contract():
    table_tree = ast.parse(IMPORTER.read_text(encoding='utf-8'))
    tables = [node.value for node in table_tree.body if isinstance(node, ast.Assign)
              and any(isinstance(target, ast.Name) and target.id == 'PIECES' for target in node.targets)]
    assert len(tables) == 1
    pieces = ast.literal_eval(tables[0])
    shared_tree = ast.parse(SHARED.read_text(encoding='utf-8'))
    functions = [node for node in shared_tree.body if isinstance(node, ast.FunctionDef) and node.name == 'save_texture']
    assert len(functions) == 1
    namespace = {}
    exec(compile(ast.Module(body=functions, type_ignores=[]), str(SHARED), 'exec'), namespace)
    return pieces, namespace['save_texture']


class ImageDouble:
    def __init__(self, size):
        self.size = list(size)
        self.saved = False
        self.filepath_raw = ''
        self.file_format = ''

    def scale(self, width, height):
        self.size = [width, height]

    def save(self):
        self.saved = True


class ItemTextureBudgetTests(unittest.TestCase):
    def setUp(self):
        self.pieces, self.save_texture = load_production_contract()

    def export(self, size, limit):
        image = ImageDouble(size)
        node = types.SimpleNamespace(type='TEX_IMAGE', image=image)
        material = types.SimpleNamespace(use_nodes=True, node_tree=types.SimpleNamespace(nodes=[node]))
        obj = types.SimpleNamespace(material_slots=[types.SimpleNamespace(material=material)])
        output = self.save_texture(obj, Path('test_albedo.png'), limit)
        self.assertTrue(image.saved)
        self.assertEqual(image.file_format, 'PNG')
        self.assertEqual(output['file'], 'test_albedo.png')
        return output['size']

    def test_every_gear_uses_128_to_512_budget(self):
        self.assertTrue(self.pieces)
        for name, piece in self.pieces.items():
            with self.subTest(name=name):
                self.assertIn(piece[-1], (128, 256, 512), '随身装备不适用立绘1024例外')

    def test_production_export_obeys_each_gear_budget(self):
        for name, piece in self.pieces.items():
            with self.subTest(name=name):
                self.assertEqual(self.export((2048, 1024), piece[-1]), [512, 256])
                self.assertEqual(self.export((1024, 1024), piece[-1]), [512, 512])

    def test_small_source_is_not_upscaled(self):
        self.assertEqual(self.export((128, 64), 512), [128, 64])

    def test_missing_texture_remains_explicit(self):
        obj = types.SimpleNamespace(material_slots=[types.SimpleNamespace(material=None)])
        self.assertIsNone(self.save_texture(obj, Path('test.png'), 512))


if __name__ == '__main__':
    unittest.main(verbosity=2)
