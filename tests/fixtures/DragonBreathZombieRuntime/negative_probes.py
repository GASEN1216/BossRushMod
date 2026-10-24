"""Mutate sparse copies; never leave shared production files temporarily broken."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Build/champion-dragonbreath-20261024/dragon-negative-probes"
GUN = "Integration/DragonDescendant/DragonBreathWeaponConfig.cs"
INIT = "Integration/BossRushIntegrationRuntimeModule_Initialization.cs"
FIXTURE = "tests/fixtures/DragonBreathZombieRuntime"
FILES = [GUN, INIT, "Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs", "Integration/BossRushIntegration.cs", "Integration/DragonDescendant/DragonBreathConfig.cs", "Integration/Reforge/ReforgeDataPersistence.cs", "Integration/Reforge/ReforgeDataPersistenceCleanup.cs", "tests/cs_source_util.py", "tools/run_runtime_regressions.py", "鸭科夫源码/ItemStatsSystem/Item.cs", "鸭科夫源码/ItemStatsSystem/StatCollection.cs"]
FILES += ["鸭科夫源码/TeamSoda.Duckov.Core/" + name for name in ("ItemAgent_Gun.cs", "Projectile.cs", "DamageReceiver.cs", "Health.cs", "DamageInfo.cs", "ProjectileContext.cs", "ElementTypes.cs", "ElementFactor.cs", "DamageTypes.cs")]
FILES += [str(path.relative_to(ROOT)).replace("\\", "/") for path in (ROOT / FIXTURE).glob("*") if path.is_file()]
PROBES = [
    ("missing-neutral", GUN, 'item.Stats.Add(new Stat("DamageFactorToZombie", 1f, false));', ';', "fresh production configuration gives neutral zombie damage"),
    ("stale-added-hash", GUN, 'item.Stats.Add(new Stat("DamageFactorToZombie", 1f, false));\n            InvalidateStatsDictionary(item.Stats);', 'item.Stats.Add(new Stat("DamageFactorToZombie", 1f, false));', "fresh production configuration gives neutral zombie damage"),
    ("stale-profile-hash", GUN, 'InvalidateStatsDictionary(item.Stats);\n\n                // 验证关键 Stats', '// 验证关键 Stats', "full configuration refreshes hash cache even with an existing legal zombie factor"),
    ("old-instance-registration", INIT, 'DragonBreathWeaponConfig.NeedsRuntimeConfig,', 'item => CustomItemRuntimeStateHelper.NeedsGunRuntimeConfig(item, true),', "actual Item.Initialize postfix repairs old gun"),
    ("old-instance-reset", GUN, '// 旧实例只缺对丧尸系数时仅补这一项，保留当前基础属性、重铸和运行时修饰。\n            EnsureZombieDamageFactor(item);', 'ConfigureWeapon(item);', "scene subscription repairs already-held gun without resetting base damage"),
]


def main():
    before = {path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in FILES}
    results = []
    for name, path, needle, replacement, expected in PROBES:
        target = OUT / name
        for file in FILES:
            destination = target / file
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / file, destination)
        source_path = target / path
        source = source_path.read_bytes()
        old = needle.encode()
        # Repository files may retain CRLF; replacements preserve that file's convention.
        if b"\r\n" in source:
            old = old.replace(b"\n", b"\r\n")
            new = replacement.encode().replace(b"\n", b"\r\n")
        else:
            new = replacement.encode()
        assert source.count(old) == 1, (name, source.count(old))
        source_path.write_bytes(source.replace(old, new))
        result = subprocess.run([sys.executable, "tools/run_runtime_regressions.py", "--filter", "DragonBreathZombieRuntime", "--jobs", "1"], cwd=target, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        log = (target / "Build/runtime-regressions/DragonBreathZombieRuntime.log").read_text(encoding="utf-8")
        (target / "probe.log").write_bytes(result.stdout)
        assert result.returncode != 0 and expected in log, (name, log[-5000:])
        results.append({"probe": name, "expected_assertion": expected, "exit": result.returncode})
        print("RED:", name, "->", expected, flush=True)
    after = {path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in FILES}
    assert before == after, "Shared sources changed during negative probes"
    (OUT / "results.json").write_text(json.dumps({"probes": results, "shared_sources_unchanged": before}, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PASS: all probes red at expected assertions; shared sources unchanged")


if __name__ == "__main__":
    main()
