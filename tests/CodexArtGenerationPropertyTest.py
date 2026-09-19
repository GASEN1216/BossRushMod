#!/usr/bin/env python3
"""直接执行 tools/gen_codex_art.py：像素 alpha、筛选、强制重出与失败保留旧资产。

仅生成微型合成像素图；外部生图/抠图调用全部以 subprocess 替身隔离。
"""
import contextlib
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(ROOT / "tools"))
SPEC = importlib.util.spec_from_file_location("codex_art_under_test", ROOT / "tools/gen_codex_art.py")
art = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(art)


class CodexArtGenerationTests(unittest.TestCase):
    def setUp(self):
        work = ROOT / "Build/codex-art-tests"
        work.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(prefix="pixels-", dir=work)
        self.before_cwd = Path.cwd()
        os.chdir(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.addCleanup(os.chdir, self.before_cwd)
        self.addCleanup(mock.patch.stopall)
        mock.patch.object(art, "tool_paths", return_value=("mock-imagegen", "mock-chroma")).start()
        mock.patch.object(art.time, "sleep").start()
        self.output = io.StringIO()
        self.redirect = contextlib.redirect_stdout(self.output)
        self.redirect.__enter__()
        self.addCleanup(self.redirect.__exit__, None, None, None)
        self.spec = ("Assets/achievement/codex_first_entry.png", 8, "test prompt")
        self.raw = Path(art.RAW) / "codex_first_entry_raw.png"
        self.cut = Path(art.RAW) / "codex_first_entry_cut.png"
        self.final = Path(self.spec[0])

    def pixel_image(self, path, color=(73, 121, 211, 128), transparent=True):
        path = Path(path)
        path.parent.mkdir(parents=True, exist_ok=True)
        image = Image.new("RGBA", (3, 3), (0, 0, 0, 0) if transparent else (255, 0, 255, 255))
        image.putpixel((1, 1), color)
        image.save(path)

    def seed_existing(self):
        for index, path in enumerate((self.raw, self.cut, self.final), 1):
            self.pixel_image(path, (index * 20, 30, 40, 150))
        return {path: path.read_bytes() for path in (self.raw, self.cut, self.final)}

    def assert_preserved(self, originals):
        for path, expected in originals.items():
            self.assertEqual(path.read_bytes(), expected, str(path))
        self.assertFalse(any(path.is_dir() for path in Path(art.RAW).iterdir()), "应清理本轮暂存目录")

    def fake_generate(self, command, **kwargs):
        self.assertEqual(command[1], "mock-imagegen")
        self.pixel_image(command[command.index("--out") + 1])
        return subprocess.CompletedProcess(command, 0, "", "")

    def test_normalize_preserves_half_alpha_and_rgb(self):
        self.pixel_image("source.png")
        art.normalize("source.png", "result.png", 8)
        with Image.open("result.png") as image:
            self.assertEqual(image.size, (8, 8))
            self.assertEqual(image.getpixel((3, 3)), (73, 121, 211, 128), "半透明像素不能再次乘 alpha")
            self.assertEqual(image.getchannel("A").getbbox(), (3, 3, 4, 4))

    def test_white_building_keeps_alpha(self):
        self.pixel_image("source.png", (75, 80, 90, 160))
        art.normalize("source.png", "result.png", 8, white=True)
        with Image.open("result.png") as image:
            self.assertEqual(image.getpixel((3, 3)), (255, 255, 255, 160))

    def test_invisible_rgb_does_not_expand_alpha_crop(self):
        self.pixel_image("source.png")
        with Image.open("source.png") as image:
            image.putpixel((0, 0), (200, 0, 90, 0))
            image.save("source.png")
        art.normalize("source.png", "result.png", 8)
        with Image.open("result.png") as image:
            self.assertEqual(image.getchannel("A").getbbox(), (3, 3, 4, 4))

    def test_empty_alpha_is_rejected(self):
        Image.new("RGBA", (2, 2), (255, 0, 255, 0)).save("empty.png")
        with self.assertRaisesRegex(ValueError, "没有可见内容"):
            art.normalize("empty.png", "result.png", 8)
        self.assertFalse(Path("result.png").exists())

    def test_added_specs_and_existing_specs_remain(self):
        expected = {"petnest_first_hatch", "petnest_lineage_10", "petnest_lineage_30", "petnest_shiny",
                    "petnest_memorial", "codex_first_entry", "codex_10", "codex_20", "codex_all", "codex_fast_kill"}
        achievements = {Path(path).stem: size for path, size, _ in art.select_specs(["Assets/achievement/"])}
        self.assertEqual(set(achievements), expected)
        self.assertEqual(set(achievements.values()), {256})
        buildings = art.select_specs(["Assets/buildings/"])
        # 2026-09-19 第二轮补上后山战利品展示柜：它原本是不透明彩色渲染图，
        # 与报箱 / 婚礼堂的纯白手绘不是一路（实测第 4 条的同类问题）。
        self.assertEqual({Path(path).stem for path, _, _ in buildings},
                         {"petnest_relic_nest", "bossrush_campaign_board", "bossrush_backmountain_showcase"})
        self.assertTrue(all(size == 256 and "Pure white hand-drawn" in prompt for _, size, prompt in buildings))
        self.assertEqual({path for path, _, _ in buildings}, art.WHITE_ICONS)
        # 2 件物品图标 + 成就图标 + 建筑图标
        self.assertEqual(len(art.SPECS),
                         len(art.BOSSES) + len(art.AFFIX) + len(art.EVENTS)
                         + 2 + len(art.ACHIEVEMENTS) + len(art.BUILDINGS))

    def test_filter_unions_paths_and_is_case_insensitive(self):
        selected = art.select_specs(["ASSETS\\ACHIEVEMENT\\", "petnest_relic_nest"])
        self.assertEqual(len(selected), 11)
        self.assertTrue(all("Assets/achievement/" in spec[0] or "petnest_relic_nest" in spec[0] for spec in selected))

    def test_list_is_read_only_even_with_force(self):
        with mock.patch.object(art, "tool_paths", side_effect=AssertionError("预览不能解析外部工具")), \
             mock.patch.object(art, "generate_one", side_effect=AssertionError("预览不能生成")), \
             mock.patch.object(art.subprocess, "run", side_effect=AssertionError("预览不能启动进程")), \
             mock.patch.object(art.os, "makedirs", side_effect=AssertionError("预览不能创建目录")):
            self.assertEqual(art.main(["--list", "--force", "--filter", "Assets/achievement/"]), 0)
        self.assertEqual(list(Path.cwd().iterdir()), [])
        self.assertIn("codex_fast_kill.png", self.output.getvalue())
        self.assertNotIn("affix_forge_stone.png", self.output.getvalue())

    def test_force_only_generates_filtered_files(self):
        self.seed_existing()
        with mock.patch.object(art, "generate_one") as generate:
            self.assertEqual(art.main(["--force", "--filter", "codex_first_entry.png"]), 0)
        generate.assert_called_once_with(next(spec for spec in art.SPECS if spec[0] == self.spec[0]), force=True)

    def test_normal_run_skips_existing_target(self):
        self.seed_existing()
        with mock.patch.object(art, "generate_one") as generate:
            self.assertEqual(art.main(["--filter", "codex_first_entry.png"]), 0)
        generate.assert_not_called()

    def test_no_match_never_runs_generator(self):
        with mock.patch.object(art, "generate_one") as generate:
            self.assertEqual(art.main(["--force", "--filter", "unknown-icon-does-not-exist"]), 2)
        generate.assert_not_called()
        self.assertFalse(Path(art.RAW).exists())

    def test_force_failure_preserves_all_old_assets_even_with_partial_output(self):
        originals = self.seed_existing()
        def fail(command, **kwargs):
            self.pixel_image(command[command.index("--out") + 1], (1, 2, 3, 160))
            return subprocess.CompletedProcess(command, 1, "", "intentional generation failure")
        with mock.patch.object(art.subprocess, "run", side_effect=fail) as run:
            with self.assertRaisesRegex(RuntimeError, "生成失败"):
                art.generate_one(self.spec, force=True)
        self.assertEqual(run.call_count, 3)
        outputs = {call.args[0][call.args[0].index("--out") + 1] for call in run.call_args_list}
        self.assertEqual(len(outputs), 3, "各次尝试必须独立暂存，不能复用失败输出")
        self.assert_preserved(originals)

    def test_force_success_replaces_raw_cut_and_final_only_after_normalization(self):
        originals = self.seed_existing()
        real_normalize = art.normalize
        def verify_before_publish(*args, **kwargs):
            for path, data in originals.items():
                self.assertEqual(path.read_bytes(), data)
            return real_normalize(*args, **kwargs)
        with mock.patch.object(art.subprocess, "run", side_effect=self.fake_generate) as run, \
             mock.patch.object(art, "normalize", side_effect=verify_before_publish):
            art.generate_one(self.spec, force=True)
        self.assertEqual(run.call_count, 1, "force 应真实调用生成器，不复用旧 raw")
        for path, previous in originals.items():
            self.assertNotEqual(path.read_bytes(), previous)
        with Image.open(self.final) as image:
            self.assertEqual(image.getpixel((3, 3)), (73, 121, 211, 128))

    def test_nonforce_resumes_existing_raw_without_network(self):
        self.pixel_image(self.raw)
        with mock.patch.object(art.subprocess, "run", side_effect=AssertionError("不得生成已有 raw")):
            art.generate_one(self.spec)
        self.assertTrue(self.final.is_file())
        self.assertEqual(self.cut.read_bytes(), self.raw.read_bytes())

    def test_chroma_failure_does_not_reuse_old_cut(self):
        originals = self.seed_existing()
        def fail_cut(command, **kwargs):
            output = command[command.index("--out") + 1]
            if command[1] == "mock-imagegen":
                self.pixel_image(output, color=(75, 80, 90, 255), transparent=False)
                return subprocess.CompletedProcess(command, 0, "", "")
            self.pixel_image(output)
            return subprocess.CompletedProcess(command, 1, "", "intentional cut failure")
        with mock.patch.object(art.subprocess, "run", side_effect=fail_cut):
            with self.assertRaisesRegex(RuntimeError, "抠图失败"):
                art.generate_one(self.spec, force=True)
        self.assert_preserved(originals)

    def test_opaque_chroma_output_is_rejected(self):
        originals = self.seed_existing()
        def opaque(command, **kwargs):
            self.pixel_image(command[command.index("--out") + 1], color=(75, 80, 90, 255), transparent=False)
            return subprocess.CompletedProcess(command, 0, "", "")
        with mock.patch.object(art.subprocess, "run", side_effect=opaque):
            with self.assertRaisesRegex(ValueError, "没有透明背景"):
                art.generate_one(self.spec, force=True)
        self.assert_preserved(originals)

    def test_normalize_failure_preserves_all_assets(self):
        originals = self.seed_existing()
        with mock.patch.object(art.subprocess, "run", side_effect=self.fake_generate), \
             mock.patch.object(art, "normalize", side_effect=ValueError("intentional pixel failure")):
            with self.assertRaisesRegex(ValueError, "pixel failure"):
                art.generate_one(self.spec, force=True)
        self.assert_preserved(originals)

    def test_publish_failure_restores_already_replaced_assets(self):
        originals = self.seed_existing()
        real_replace = art.os.replace
        def replace(source, destination):
            if os.path.normpath(destination) == os.path.normpath(self.final):
                raise OSError("intentional publishing failure")
            return real_replace(source, destination)
        with mock.patch.object(art.subprocess, "run", side_effect=self.fake_generate), \
             mock.patch.object(art.os, "replace", side_effect=replace):
            with self.assertRaisesRegex(OSError, "publishing failure"):
                art.generate_one(self.spec, force=True)
        self.assert_preserved(originals)


    def load_dependent_tool(self, name):
        module_spec = importlib.util.spec_from_file_location(name, ROOT / ("tools/" + name + ".py"))
        module = importlib.util.module_from_spec(module_spec)
        with mock.patch.dict(sys.modules, {"gen_codex_art": art}):
            module_spec.loader.exec_module(module)
        return module

    def test_item_icon_wrapper_consumes_its_own_cli_flags(self):
        wrapper = self.load_dependent_tool("gen_sky_island_item_icons")
        with mock.patch.object(sys, "argv", ["gen_sky_island_item_icons.py", "--test"]), \
             mock.patch.dict(os.environ, {"OPENAI_API_KEY": "fixture-only", "OPENAI_BASE_URL": "fixture-only"}), \
             mock.patch.object(art, "main", return_value=0) as main, \
             mock.patch.object(art, "SPECS"), mock.patch.object(art, "RAW"):
            self.assertEqual(wrapper.main(), 0)
            main.assert_called_once_with([])
            self.assertEqual(art.SPECS, wrapper.SPECS[:2])

    def test_boss_gear_concepts_use_lazy_tool_path(self):
        wrapper = self.load_dependent_tool("gen_sky_island_boss_gear_art")
        with mock.patch.object(wrapper, "CONCEPTS", str(Path.cwd() / "concepts")), \
             mock.patch.object(art.subprocess, "run", side_effect=self.fake_generate) as run:
            self.assertEqual(wrapper.run_concepts(wrapper.PIECES[:1]), 0)
        self.assertEqual(run.call_count, 1)

    def test_building_pipeline_whitens_pixels(self):
        spec = next(spec for spec in art.SPECS if spec[0] == "Assets/buildings/petnest_relic_nest.png")
        with mock.patch.object(art.subprocess, "run", side_effect=self.fake_generate):
            art.generate_one(spec, force=True)
        with Image.open(spec[0]) as image:
            self.assertEqual(image.size, (256, 256))
            self.assertEqual(image.getpixel((127, 127)), (255, 255, 255, 128))


if __name__ == "__main__":
    unittest.main(verbosity=2)
