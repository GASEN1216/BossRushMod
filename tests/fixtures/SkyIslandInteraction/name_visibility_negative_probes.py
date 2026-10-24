"""名牌回归反向验证：仅破坏 Build 中的隔离副本，经真实入口确认转红并逐字还原。"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
HELPER = "Integration/Utils/NPCNameTagHelper.cs"


def main():
    manifest = ROOT / "Build/runtime-regressions/SkyIslandInteraction/source-hashes.json"
    if not manifest.is_file():
        raise SystemExit("先运行 python tools/run_runtime_regressions.py --filter SkyIslandInteraction")
    destination = Path(tempfile.mkdtemp(prefix="name-visibility-negative-", dir=ROOT / "Build"))
    paths = set(json.loads(manifest.read_text(encoding="utf-8")))
    paths.update(("tools/run_runtime_regressions.py", "tools/run_guards.py",
                  "tests/NPCNameTagHotPathTransformGuard.py", "tests/cs_source_util.py"))
    paths.update(path.relative_to(ROOT).as_posix() for path in HERE.iterdir() if path.is_file())
    for directory in (ROOT / "Integration/NPCs", ROOT / "SkyIsland"):
        paths.update(path.relative_to(ROOT).as_posix() for path in directory.rglob("*.cs"))
    for relative in paths:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / relative, target)

    def run(label, runtime=False):
        command = [sys.executable, "tools/run_runtime_regressions.py", "--filter", "SkyIslandInteraction", "--jobs", "1"] if runtime else [
            sys.executable, "tools/run_guards.py", "--filter", "NPCNameTagHotPathTransform", "--verbose"]
        completed = subprocess.run(command, cwd=destination, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   env=dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1"), timeout=90)
        output = completed.stdout.decode("utf-8", errors="replace")
        if runtime:
            fixture_log = destination / "Build/runtime-regressions/SkyIslandInteraction.log"
            output += "\n" + fixture_log.read_text(encoding="utf-8")
        (destination / (label + ".log")).write_text(output, encoding="utf-8")
        return completed.returncode, output

    for runtime in (False, True):
        code, output = run("baseline-" + str(runtime), runtime)
        if code:
            raise AssertionError("隔离基线未通过: " + output)

    probes = (
        ("overlay-created", "entry.Target = parent;", 'new GameObject("ForbiddenOverlay"); entry.Target = parent;',
         "友好 NPC 登记路径不得创建 UI", "friendly NPC registration and refresh create no independent name UI or components"),
        ("actor-created", "entry.Actor = entry.Target.GetComponent<DuckovDialogueActor>();",
         "entry.Actor = entry.Target.gameObject.AddComponent<DuckovDialogueActor>();",
         "姓名只能绑定现有 DuckovDialogueActor", None),
        ("actor-retry-unthrottled", "if (now < entry.NextActorLookupTime) return;", "",
         "无 actor 时必须按 1 秒间隔重试查找", "missing actor lookup waits one second before retrying"),
        ("initial-owner-name-overwritten", "entry.DisplayName = displayName ?? string.Empty;",
         "entry.DisplayName = displayName ?? string.Empty; entry.PendingNameUpdate = true;",
         "只有显式姓名变化才能提交注入", "registration preserves the dialogue owner's initial name"),
        ("initial-name-gate-removed", " || !entry.PendingNameUpdate", "",
         "只有显式姓名变化才能提交注入", "registration preserves the dialogue owner's initial name"),
        ("official-name-overwritten", ' || !nameKey.StartsWith("BossRush_", StringComparison.Ordinal)', "",
         "只能写 BossRush_ 姓名键", "official actor name keys cannot be overwritten by the helper"),
        ("pending-name-reinjected", "entry.PendingNameUpdate = false;", "",
         "只有显式姓名变化才能提交注入", "stable actor names do not repeatedly inject localization"),
        ("update-resurrects-registration",
         "if (!OriginalHealthBarEntriesByTransformId.TryGetValue(parent.GetInstanceID(), out entry) || entry == null)\n                return false;",
         'if (!OriginalHealthBarEntriesByTransformId.TryGetValue(parent.GetInstanceID(), out entry) || entry == null)\n                return RegisterOriginalHealthBarName(parent, displayName, 2.2f, "test");',
         "姓名更新只作用于已有登记", "name update cannot create an unregistered actor binding"),
        ("unregister-keeps-registration", "OriginalHealthBarEntriesByTransformId.Remove(parent.GetInstanceID());", "",
         "注销和重置必须释放 helper 登记", "name update cannot resurrect removed registration"),
        ("reset-deletes-official-actor", "OriginalHealthBarEntriesByTransformId.Clear();",
         "foreach (OriginalHealthBarEntry entry in OriginalHealthBarEntriesByTransformId.Values) { if (entry.Actor != null) UnityEngine.Object.Destroy(entry.Actor); } OriginalHealthBarEntriesByTransformId.Clear();",
         "注销和重置不得删除其他 owner 的官方 actor", "reset never destroys official actors owned by dialogue systems"),
    )
    evidence = []
    target = destination / HELPER
    for label, old, new, expected_guard, expected_runtime in probes:
        original = target.read_bytes()
        newline = "\r\n" if b"\r\n" in original else "\n"
        anchor = old.replace("\n", newline).encode("utf-8")
        if original.count(anchor) != 1:
            raise AssertionError("变异锚点必须唯一: " + label)
        before = hashlib.sha256(original).hexdigest()
        try:
            target.write_bytes(original.replace(anchor, new.replace("\n", newline).encode("utf-8"), 1))
            code, output = run(label + "-guard")
            if code == 0 or expected_guard not in output:
                raise AssertionError("守卫未在预期判据转红: " + label + "\n" + output)
            if expected_runtime:
                code, output = run(label + "-runtime", True)
                if code == 0 or "FAIL " + expected_runtime not in output:
                    raise AssertionError("执行回归未在预期行为转红: " + label + "\n" + output)
        finally:
            target.write_bytes(original)
        restored = hashlib.sha256(target.read_bytes()).hexdigest()
        if before != restored:
            raise AssertionError("恢复 SHA-256 不一致: " + label)
        evidence.append({"probe": label, "guard": expected_guard, "runtime": expected_runtime,
                         "sha256_before": before, "sha256_restored": restored, "passed": True})
        print("PASS negative probe " + label, flush=True)

    for runtime in (False, True):
        code, output = run("restored-" + str(runtime), runtime)
        if code:
            raise AssertionError("全部还原后未转绿: " + output)
    (destination / "negative-results.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print("Evidence: " + str(destination))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
