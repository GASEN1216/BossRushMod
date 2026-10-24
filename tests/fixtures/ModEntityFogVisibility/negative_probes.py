"""Break only a Build-isolated copy, run real entry points, and restore exact bytes."""
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
HELPER = "Utilities/ModEntityFogVisibility.cs"
PROCESSING = "Integration/EquipmentFactory_ItemProcessing.cs"
HALBERD = "Integration/DragonKing/Weapons/FenHuangHalberdWeaponConfig.cs"


def main():
    manifest = ROOT / "Build/runtime-regressions/ModEntityFogVisibility/source-hashes.json"
    if not manifest.is_file():
        raise SystemExit("先运行 python tools/run_runtime_regressions.py --filter ModEntityFogVisibility")
    destination = Path(tempfile.mkdtemp(prefix="fog-visibility-negative-", dir=ROOT / "Build"))
    paths = set(json.loads(manifest.read_text(encoding="utf-8")))
    paths.update(("tools/run_runtime_regressions.py", "tools/run_guards.py", "compile_official.bat",
                  "tests/ModEntityFogVisibilityGuard.py", "tests/cs_source_util.py",
                  "Integration/NPCs/Courier/CourierNpcRuntimeModule.cs", "Integration/NPCs/Goblin/GoblinNPC.cs",
                  "Integration/NPCs/Nurse/NurseNPC.cs", "Integration/NPCs/DuckNpc/DuckNpcFactory.cs",
                  "Integration/SandstormChampion/SandstormChampionBody.cs", "Integration/EquipmentFactory.cs",
                  "Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs"))
    paths.update(path.relative_to(ROOT).as_posix() for path in HERE.iterdir() if path.is_file())
    for relative in paths:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / relative, target)

    def run(label, runtime=False):
        command = [sys.executable, "tools/run_runtime_regressions.py", "--filter", "ModEntityFogVisibility", "--jobs", "1"] if runtime else [
            sys.executable, "tools/run_guards.py", "--filter", "ModEntityFogVisibility", "--verbose"]
        completed = subprocess.run(command, cwd=destination, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   env=dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1"), timeout=90)
        output = completed.stdout.decode("utf-8", errors="replace")
        if runtime:
            output += "\n" + (destination / "Build/runtime-regressions/ModEntityFogVisibility.log").read_text(encoding="utf-8")
        (destination / (label + ".log")).write_text(output, encoding="utf-8")
        return completed.returncode, output

    for runtime in (False, True):
        code, output = run("baseline-" + str(runtime), runtime)
        if code:
            raise AssertionError("隔离基线未通过: " + output)

    probes = (
        ("fog-event-removed", HELPER, "source.OnActiveChanged += OnFogActiveChanged;", "",
         "官方视野和死亡事件必须幂等订阅", "existing native hider is reused with one subscription"),
        ("observers-ignored", HELPER, "!source.isActiveAndEnabled || source.NumObservers > 0", "!source.isActiveAndEnabled || false",
         "首次显隐须读取官方启用观察者与玩家死亡状态", "native observers reveal immediately"),
        ("permanent-visible-at-birth", HELPER, "!source.isActiveAndEnabled || source.NumObservers > 0", "source.PermanentlyReveal || source.NumObservers > 0",
         "首次显隐须读取官方启用观察者与玩家死亡状态", "permanent reveal still waits for first native observation"),
        ("probe-enabled-before-config", HELPER,
         "binding.source.SamplePoints = new Transform[] { binding.ownedProbe.transform, left, right };",
         "binding.ownedProbe.SetActive(true); binding.source.SamplePoints = new Transform[] { binding.ownedProbe.transform, left, right };",
         "新 probe 必须 inactive 配齐采样再激活", "owned probe configures samples before first enable"),
        ("wrong-native-sampling-height", HELPER, "new Vector3(0f, 0.526f, 0f)", "new Vector3(0f, 0f, 0f)",
         "独立 NPC 采样必须沿用原版三点高度和间距", "owned probe configures samples before first enable"),
        ("raid-gate-removed", HELPER, "&& LevelManager.Instance.IsRaidMap", "",
         "只在 Raid 存活玩家视野外隐藏", "non-raid scenes preserve friendly visibility"),
        ("skill-enabled-overwritten", HELPER, "renderers[i].forceRenderingOff = true;", "renderers[i].enabled = false;",
         "只通过渲染强制关闭和光源遮罩隐藏", "unknown first-frame visibility hides renderers and lights in raids"),
        ("light-mask-not-restored", HELPER, "lights[i].cullingMask = lightRestore[i];", "lights[i].cullingMask = -1;",
         "恢复必须保留技能强制隐藏与原始光源遮罩", "reveal restores original force-off and exact light masks"),
        ("late-frame-tree-scan", HELPER, "private void LateUpdate()\n        {\n            if (!hidden) return;",
         "private void LateUpdate()\n        {\n            gameObject.GetComponentsInChildren<Renderer>(true); if (!hidden) return;",
         "热路径只在隐藏时遍历缓存不得扫描层级", "hidden late updates reassert cached visuals without scans"),
        ("death-no-restoration", HELPER, "mainCharacterDead = true;\n            RestoreRendering();", "mainCharacterDead = true;",
         "玩家死亡必须立即恢复渲染", "main character death restores rendering"),
        ("fog-event-not-retired", HELPER, "source.OnActiveChanged -= OnFogActiveChanged;", "source.OnActiveChanged += OnFogActiveChanged;",
         "禁用销毁必须退订还原并清理自建 probe", "disable restores snapshots and unsubscribes both events"),
        ("owned-probe-leaked", HELPER, "if (ownedProbe != null) Destroy(ownedProbe);", "",
         "禁用销毁必须退订还原并清理自建 probe", "destroying owner removes only its probe and restores visuals"),
        ("equipment-subvisual-not-initialized", PROCESSING, "visuals.SetRenderers();", "",
         "装备只在缺少官方子视觉时初始化且保留已有缓存", "equipment missing official subvisuals is initialized once"),
        ("equipment-existing-cache-replaced", PROCESSING, "modelGo == null || modelGo.GetComponent<CharacterSubVisuals>() != null", "modelGo == null",
         "装备只在缺少官方子视觉时初始化且保留已有缓存", "equipment preserves existing official subvisual caches"),
        ("halberd-fog-sync-removed", HALBERD, "holder.characterModel.SyncHiddenToMainCharacter();", "",
         "近战视觉准备末尾必须还原官方迷雾层", "melee visual initialization finishes by restoring official holder fog layer"),
        ("courier-unwired", "Integration/NPCs/Courier/CourierNpcRuntimeModule.cs", "ModEntityFogVisibility.Attach(courierNPCInstance);", "",
         "独立 NPC 修层后必须接官方视野", None),
        ("sandstorm-unwired", "Integration/SandstormChampion/SandstormChampionBody.cs", "ModEntityFogVisibility.Attach(go, boss.transform);", "",
         "沙暴额外外观建好后必须复用 Boss 视野", None),
        ("witch-clears-fog", "Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs",
         "SetStealthMode(PhantomWitchStealthMode.Visible);", "SetStealthMode(PhantomWitchStealthMode.Visible); bossCharacter.Show();",
         "女巫技能恢复不能清除官方迷雾 Hidden", None),
        ("equipment-explicit-binding-unwired", "Integration/EquipmentFactory.cs", "EnsureModelSubVisuals(modelAgent.gameObject);", "",
         "装备发现和显式绑定入口必须登记官方子视觉", None),
    )
    evidence = []
    for label, relative, old, new, expected_guard, expected_runtime in probes:
        target = destination / relative
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
        evidence.append({"probe": label, "path": relative, "guard": expected_guard, "runtime": expected_runtime,
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
