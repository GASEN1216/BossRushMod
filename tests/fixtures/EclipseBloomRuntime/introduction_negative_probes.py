"""Execute real introduction mutations in sparse copies; never modify shared production."""
from pathlib import Path
import hashlib
import importlib.util
import json
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/EclipseBloomIntroduction-negative"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("eclipse_intro_fixture", HERE / "run.py")
    fixture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture)
    hashes = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in fixture.INTRO_PRODUCTION}
    code, output = fixture.run_introduction_fixture(output_root=OUT / "baseline")
    (OUT / "baseline.log").write_text(output, encoding="utf-8")
    if code:
        print("FAIL introduction baseline")
        print(output[-5000:])
        return 1
    intro = "Integration/EclipseBloom/EclipseBloomIntroduction.cs"
    persistence = "Integration/EclipseBloom/EclipseBloomIntroductionPersistence.cs"
    probes = (
        ("death_keeps_accepted", intro,
         "if (Commit(new EclipseBloomIntroductionData())) abandonPending = false;",
         "abandonPending = false;",
         "同帧死亡必须立即撤销 accepted、遭遇和目标"),
        ("rejected_abandon_reopens_accepted", intro,
         "abandonPending = true;", "abandonPending = false;",
         "Store 拒写也须先取消内存投影并保持清理可运行"),
        ("completed_task_loses_history", intro,
         "TaskId = 1, Done = () => data != null && (data.Completed || data.Defeated)",
         "TaskId = 1, Done = () => data != null && data.Defeated",
         "历史投影中的目标必须保持已完成"),
        ("delivery_collect_writes_early", intro,
         "if (deliveryActive) return false;", "",
         "交付事务结束前不得采集或物理提交事实"),
        ("delivery_end_forgets_flush", intro,
         "if (persistence != null) persistence.RequestFlush();", "",
         "结束交付事务必须提交唯一 completed 事实并补齐物理保存"),
        ("slot_switch_keeps_abandon_owner", persistence,
         "if (slotChanged != null) slotChanged();", "",
         "切槽必须只读取新槽 completed，不沿用旧槽死亡重试"),
        ("contradictory_schema_is_writable", persistence,
         "|| (defeated && !accepted) || (completed && (accepted || defeated))",
         "|| (completed && (accepted || defeated))",
         "坏 schema 必须保持只读闭锁"),
        ("death_subscription_leaks", intro,
         "if (playerHealth != null) playerHealth.OnDeadEvent.RemoveListener(OnPlayerDied);\n            playerHealth = null;",
         "playerHealth = null;",
         "关闭必须退订玩家事件并撤销共享客户端"),
        ("destroy_flush_ignores_abandon_gate", persistence,
         "bool allowed = canFlush == null || canFlush();", "bool allowed = true;",
         "宿主销毁绕基地门时仍须拒绝冲出已失效的 accepted 批次"),
    )
    results = {}
    for name, target, original, replacement, expected in probes:
        copy_root = Path(tempfile.mkdtemp(prefix=name + "-", dir=OUT))
        for source in fixture.INTRO_PRODUCTION:
            path = copy_root / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((ROOT / source).read_bytes())
        path = copy_root / target
        value = path.read_text(encoding="utf-8-sig")
        if value.count(original) != 1:
            raise AssertionError(name + ": mutation anchor must occur exactly once")
        path.write_text(value.replace(original, replacement), encoding="utf-8")
        code, output = fixture.run_introduction_fixture(source_root=copy_root, output_root=copy_root / "build")
        (copy_root / "probe.log").write_text(output, encoding="utf-8")
        passed = code != 0 and "InvalidOperationException" in output and expected in output
        results[name] = {"passed": passed, "exit_code": code, "expected_assertion": expected, "copy_root": str(copy_root)}
        print(("PASS " if passed else "FAIL ") + name + ": " + expected, flush=True)
        if not passed:
            print(output[-5000:])
    unchanged = all(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == digest for name, digest in hashes.items())
    results["workspace_production_unchanged"] = unchanged
    results["production_sha256"] = hashes
    (OUT / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(("PASS" if unchanged else "FAIL") + ": workspace production SHA-256 unchanged")
    return 0 if unchanged and all(value["passed"] for value in results.values() if isinstance(value, dict) and "passed" in value) else 1


if __name__ == "__main__":
    raise SystemExit(main())
