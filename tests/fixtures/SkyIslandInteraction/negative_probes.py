"""渡口商店反向验证：只修改 Build 下的隔离副本，每次按字节恢复并核对哈希。"""
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
ARTIFACTS = ROOT / "Build/runtime-regressions/SkyIslandInteraction"


def main():
    manifest = ARTIFACTS / "source-hashes.json"
    if not manifest.is_file():
        raise SystemExit("先运行 python tools/run_runtime_regressions.py --filter SkyIslandInteraction")
    destination = Path(tempfile.mkdtemp(prefix="dock-shop-negative-", dir=ROOT / "Build"))
    paths = set(json.loads(manifest.read_text(encoding="utf-8")))
    paths.add("tools/run_runtime_regressions.py")
    paths.update(p.relative_to(ROOT).as_posix() for p in HERE.iterdir() if p.is_file())
    for relative in paths:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / relative, target)

    evidence = []

    def run(label):
        completed = subprocess.run(
            [sys.executable, "tools/run_runtime_regressions.py", "--filter", "SkyIslandInteraction", "--jobs", "1"],
            cwd=destination, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            env=dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1"), timeout=90)
        output = completed.stdout.decode("utf-8", errors="replace")
        (destination / (label + ".log")).write_text(output, encoding="utf-8")
        detail = (destination / "Build/runtime-regressions/SkyIslandInteraction.log").read_text(encoding="utf-8")
        (destination / (label + "-fixture.log")).write_text(detail, encoding="utf-8")
        return completed.returncode, detail

    code, output = run("baseline")
    if code:
        raise AssertionError("隔离基线未通过: " + output)
    probes = (
        ("constructor-unwired", "SkyIsland/SkyIslandWorldStory.cs", "            AttachDockShop();", "",
         "production constructor adds a reachable dock shop"),
        ("dispose-unwired", "SkyIsland/SkyIslandWorldStory.cs", "            DisposeDockShop();", "",
         "production Dispose destroys its shop option"),
        ("wrong-npc-id", "SkyIsland/SkyIslandWorldStoryServices.cs", 'component => component.NpcId = "sky_fuzhou"',
         'component => component.NpcId = "wrong_npc"', "dock NPC identity and group membership are ready before first Awake"),
        ("stale-group-entry", "SkyIsland/SkyIslandWorldStoryServices.cs",
         "            if (dockShopGroup != null) dockShopGroup.Remove(owned);", "",
         "production Dispose destroys its shop option"),
        ("destroy-still-active", "SkyIsland/SkyIslandWorldStoryServices.cs", "            owned.gameObject.SetActive(false);", "",
         "production Dispose destroys its shop option"),
        ("unowned-shop-close", "SkyIsland/SkyIslandWorldStoryServices.cs",
         "NPCShopSystem.CloseShopIfOwnedBy(owned.transform.parent)", "NPCShopSystem.CloseShop()",
         "dock disposal leaves a different NPC shop open"),
    )
    for name, relative, old, new, expected in probes:
        target = destination / relative
        original = target.read_bytes()
        anchor = old.encode("utf-8")
        if original.count(anchor) != 1:
            raise AssertionError("变异锚点不唯一: " + name)
        before = hashlib.sha256(original).hexdigest()
        try:
            target.write_bytes(original.replace(anchor, new.encode("utf-8"), 1))
            code, output = run(name)
            if code == 0 or "FAIL " + expected not in output:
                raise AssertionError("变异没有在预期行为断言转红: " + name + "\n" + output)
        finally:
            target.write_bytes(original)
        restored = hashlib.sha256(target.read_bytes()).hexdigest()
        if before != restored:
            raise AssertionError("恢复 SHA-256 不匹配: " + name)
        evidence.append({"name": name, "source": relative, "expected_failure": expected,
                         "sha256_before": before, "sha256_restored": restored, "passed": True})
        print("PASS negative probe " + name, flush=True)
    code, output = run("restored")
    if code:
        raise AssertionError("全部恢复后未转绿: " + output)
    (destination / "negative-results.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    print("Evidence: " + str(destination))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
