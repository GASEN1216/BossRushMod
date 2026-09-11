"""定向守卫不得因 Git 中文路径或失败的变更查询漏选。"""
from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

spec = spec_from_file_location("guard_runner", Path(__file__).resolve().parents[1] / "tools/run_guards.py")
runner = module_from_spec(spec)
spec.loader.exec_module(runner)


class ChangedPathsTests(unittest.TestCase):
    def test_git_nul_paths_preserve_unicode_spaces_and_newlines(self):
        tracked = "目录/中文名称.cs"
        untracked = " tests/new\nname.py "
        replies = [
            subprocess.CompletedProcess([], 0, (tracked + "\0").encode("utf-8"), b"\x96"),
            subprocess.CompletedProcess([], 0, (untracked + "\0").encode("utf-8"), b""),
        ]
        with patch.object(runner.subprocess, "run", side_effect=replies) as run:
            self.assertEqual(runner.changed_paths(), {tracked, untracked})
        for call in run.call_args_list:
            self.assertIn("-z", call.args[0])
            self.assertNotIn("text", call.kwargs)

    def test_incomplete_git_result_selects_all_guards(self):
        ok = subprocess.CompletedProcess([], 0, b"One.cs\0", b"")
        failed = subprocess.CompletedProcess([], 128, b"", b"invalid HEAD")
        with patch.object(runner.subprocess, "run", side_effect=[ok, failed]):
            self.assertEqual(runner.filter_by_changed_files(["OneGuard.py", "TwoGuard.py"]),
                             ["OneGuard.py", "TwoGuard.py"])

    def test_undecodable_or_failed_queries_select_all_guards(self):
        for result in [subprocess.CompletedProcess([], 0, b"\xff\0", b""), OSError("git unavailable")]:
            with self.subTest(result=result):
                kwargs = {"side_effect": result} if isinstance(result, Exception) else {"return_value": result}
                with patch.object(runner.subprocess, "run", **kwargs):
                    self.assertEqual(runner.filter_by_changed_files(["OneGuard.py"]), ["OneGuard.py"])


if __name__ == "__main__":
    unittest.main()
