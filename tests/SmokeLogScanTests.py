"""Tests for the manual smoke-test log scanner."""

from pathlib import Path
import sys
import tempfile
import unittest


sys.path.insert(0, str(Path(__file__).resolve().parent))

import SmokeLogScan


class SmokeLogScanTests(unittest.TestCase):
    def test_ignores_non_bossrush_exception_blocks(self):
        text = """ArgumentNullException: The Playable is null
  at KINEMATION.MagicBlend.Runtime.MagicBlendState.OnStateEnter()

NullReferenceException: Object reference not set to an instance of an object
  at BattlefieldTypeKillNotice.ModBehaviour.OnDead()
"""

        result = SmokeLogScan.scan_log_text(text)

        self.assertEqual(2, result.total_error_blocks)
        self.assertEqual(0, len(result.bossrush_error_blocks))

    def test_flags_exception_block_with_bossrush_stack_frame(self):
        text = """NullReferenceException: Object reference not set to an instance of an object
  at BossRush.ModBehaviour.TickModeDIntegrity(System.Single deltaTime)
  at BossRush.ModeDRuntimeModule.OnUpdate(System.Single deltaTime, System.Single unscaledDeltaTime)
"""

        result = SmokeLogScan.scan_log_text(text)

        self.assertEqual(1, result.total_error_blocks)
        self.assertEqual(1, len(result.bossrush_error_blocks))
        self.assertIn("TickModeDIntegrity", result.bossrush_error_blocks[0])

    def test_flags_unity_error_line_with_bossrush_text(self):
        text = """[Error] [BossRush] Mode D cleanup failed
  at BossRush.ModBehaviour.EndModeD()
"""

        result = SmokeLogScan.scan_log_text(text)

        self.assertEqual(1, result.total_error_blocks)
        self.assertEqual(1, len(result.bossrush_error_blocks))

    def test_finds_latest_log_by_modified_time(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            old_log = root / "2026-05-09_10-00-00.log"
            new_log = root / "2026-05-09_12-00-00.log"
            old_log.write_text("old", encoding="utf-8")
            new_log.write_text("new", encoding="utf-8")
            old_time = 1000
            new_time = 2000
            old_log.touch()
            new_log.touch()
            import os
            os.utime(str(old_log), (old_time, old_time))
            os.utime(str(new_log), (new_time, new_time))

            self.assertEqual(new_log, SmokeLogScan.find_latest_log(root))


if __name__ == "__main__":
    unittest.main()
