"""源码模式只接受明确列出的制品 PARTIAL；普通失败和来源不明的 exit=2 都失败。"""
from contextlib import redirect_stdout
from importlib.util import spec_from_file_location, module_from_spec
from io import StringIO
from pathlib import Path
from unittest.mock import patch
import unittest

spec = spec_from_file_location("guard_runner", Path(__file__).resolve().parents[1] / "tools/run_guards.py")
runner = module_from_spec(spec)
spec.loader.exec_module(runner)


class SourceOnlyGuardProfileTests(unittest.TestCase):
    def run_profile(self, name, code, source_only):
        argv = ["run_guards.py"] + (["--source-only"] if source_only else [])
        output = StringIO()
        with patch.object(runner.sys, "argv", argv), patch.object(runner, "collect_scripts", return_value=[name]), \
                patch.object(runner, "load_known_red", return_value={}), \
                patch.object(runner, "run_one", return_value=(name, code, "artifact omitted", 0.0)), redirect_stdout(output):
            result = runner.main()
        return result, output.getvalue()

    def test_explicit_partial_only_in_source_mode(self):
        result, output = self.run_profile("ModeGPresentationAssetGuard.py", 2, True)
        self.assertEqual(result, 0)
        self.assertIn("[PARTIAL]", output)
        self.assertIn("PASS=0", output)

    def test_release_rejects_partial(self):
        self.assertEqual(self.run_profile("ModeGPresentationAssetGuard.py", 2, False)[0], 1)

    def test_source_still_rejects_code_errors(self):
        self.assertEqual(self.run_profile("ModeGPresentationAssetGuard.py", 1, True)[0], 1)

    def test_no_arbitrary_partial(self):
        self.assertEqual(self.run_profile("AnyOtherGuard.py", 2, True)[0], 1)


if __name__ == "__main__":
    unittest.main()
