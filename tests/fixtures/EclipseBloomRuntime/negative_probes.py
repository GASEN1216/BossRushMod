"""Break runtime contracts only in independent copies, then require behavioral failures."""
from pathlib import Path
import hashlib
import importlib.util
import json
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/EclipseBloomRuntime-negative"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("eclipse_runtime_fixture", HERE / "run.py")
    fixture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture)
    original_hashes = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest() for name in fixture.PRODUCTION}
    code, output = fixture.run_fixture(output_root=OUT / "baseline")
    (OUT / "baseline.log").write_text(output, encoding="utf-8")
    if code:
        print("FAIL: baseline is red before runtime negative probes")
        print(output[-6000:])
        return 1

    controller = "Integration/EclipseBloom/EclipseBloomAbilityController.cs"
    petal = "Integration/EclipseBloom/EclipseBloomPetal.cs"
    boss = "Integration/EclipseBloom/EclipseBloomBoss.cs"
    module = "Integration/EclipseBloom/EclipseBloomRuntimeModule.cs"
    shield_geometry = "Integration/EclipseBloom/ResidualSunShieldModelGeometry.cs"
    flower_geometry = "Integration/EclipseBloom/EclipseBloomVisuals_Geometry.cs"
    ground = "Integration/EclipseBloom/EclipseBloomGroundProjection.cs"
    probes = (
        ("shield_shell_loses_back", shield_geometry,
         "b.Triangle(new Vector3(0f, -0.036f, 0f), bottomC, bottomA);", "",
         "production shield model is closed with paired opposite edge winding at its seams: ResidualSunShield_ClosedObsidianPlate"),
        ("shield_faces_reversed", shield_geometry,
         "triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);",
         "triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);",
         "production shield model front faces enclose positive physical volume in every component"),
        ("flower_petal_loses_cap", flower_geometry,
         "triangles.Add(0); triangles.Add(1); triangles.Add(2);", "",
         "production flower model is closed with paired opposite edge winding at its seams: EclipseBloom_ClosedMetalPetal"),
        ("detached_petals_leak", controller,
         "if (petals[i] != null) Destroy(petals[i].gameObject);", "if (petals[i] != null) { }",
         "cancel destroys all detached petals and body"),
        ("petal_does_not_cancel_sector", petal,
         "owner.BreakPetal(index);", "owner.BreakPetal(-1);",
         "official Hurt to Dead destroys the struck petal and permanently clears its bit"),
        ("reward_transfer_repeats", controller,
         "reward = null; return;", "return;",
         "normal official event order emits exactly one exclusive reward"),
        ("owner_death_revokes_killed_boss_reward", controller,
         "if (stopped || !subscribed || reward == null", "if (isCurrent == null || !isCurrent() || stopped || !subscribed || reward == null",
         "committed boss actual death keeps exactly one exclusive reward when player death invalidates owner in the same frame"),
        ("live_callback_can_emit_reward", controller,
         "|| !boss.Health.IsDead", "",
         "a live committed boss cannot emit its exclusive reward through a premature preloot callback"),
        ("uncommitted_reward_leak", boss,
         "if (reward != null) UnityEngine.Object.Destroy(reward.gameObject);", "if (reward != null) { }",
         "failed spawn destroys every uncommitted exclusive reward"),
        ("mode_e_entry_overlap", module,
         "|| owner.CampaignModeEActiveForRuntime", "",
         "every existing active mode excludes natural flower spawning"),
        ("native_success_does_not_consume_raid", module,
         "consumed = true;", "consumed = false;",
         "temporary same owner additive loading preserves an already committed encounter"),
        ("additive_callback_cancels_encounter", module,
         "RefreshLevelOwner();\n            if (active", "Cleanup();\n            RefreshLevelOwner();\n            if (active",
         "duplicate main and target loaded callbacks preserve the same in-flight raid encounter"),
        ("late_generation_reenters_successor", module,
         "token == generation &&", "",
         "late completion of an old generation cannot replace or clean the committed successor"),
        ("spawn_into_wrong_active_scene", boss,
         "targetScene.buildIndex, null, false);", "SceneManager.GetActiveScene().buildIndex, null, false);",
         "production character factory receives the target subscene build index instead of the active main scene"),
        ("ground_sampling_exceeds_frame_budget", ground,
         "cursor + SamplesPerFrame", "cursor + SamplesPerFrame * 2",
         "first frame respects the ninety six sample budget and leaves unvisited ground unavailable"),
        ("ground_bridges_two_floors", ground,
         "if (max - min > 0.65f) return false;", "",
         "a height discontinuity cannot bridge two floors with a false warning plane"),
        ("missing_ground_warning_still_damages", controller,
         "if (body == null || !body.HasGroundWarningAt(player.transform.position)) return;", "",
         "height cover and friendly team gates are applied before official damage"),
        ("pause_keeps_attacking", controller,
         "Time.deltaTime <= 0f || BossRushUI.IsGamePaused()", "Time.deltaTime <= 0f",
         "menu pause stops both attack advancement and repeated damage"),
        ("petal_hit_volume_ignores_visual_scale", controller,
         "petals[i].transform.localScale = body.GetPetalScale(i);", "petals[i].transform.localScale = Vector3.one;",
         "initial petal hit volume uses the matching visible leaf scale"),
        ("uncommitted_boss_exposed", boss,
         "boss.gameObject.SetActive(false);", "boss.gameObject.SetActive(true);",
         "uncommitted boss stays inactive during the preparation yield before its reward and death subscription exist"),
        ("dead_boss_committed", boss,
         "if (boss == null || boss.Health == null || boss.Health.IsDead)", "if (false)",
         "death during preparation never commits a dead boss and releases the encounter for retry"),
        ("concurrent_factory_clones_temporary_source", boss,
         "preset.name != \"BossRush_EclipseBloom_Runtime\" &&", "",
         "only an in flight flower temporary clone cannot be used as a source preset for another encounter"),
    )
    results = {}
    for name, target, original, replacement, expected in probes:
        copy_root = Path(tempfile.mkdtemp(prefix=name + "-", dir=OUT))
        for source in fixture.PRODUCTION:
            path = copy_root / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((ROOT / source).read_bytes())
        copied_source = copy_root / target
        text = copied_source.read_text(encoding="utf-8-sig")
        if text.count(original) != 1:
            raise AssertionError(name + ": mutation anchor must occur exactly once: " + original)
        copied_source.write_text(text.replace(original, replacement), encoding="utf-8")
        code, output = fixture.run_fixture(source_root=copy_root, output_root=copy_root / "build")
        (copy_root / "probe.log").write_text(output, encoding="utf-8")
        passed = code != 0 and "FAIL " in output and expected in output
        results[name] = {"passed": passed, "exit_code": code, "expected_assertion": expected,
                         "copy_root": str(copy_root)}
        print(("PASS " if passed else "FAIL ") + name + ": " + expected, flush=True)
        if not passed:
            print(output[-6000:])

    unchanged = all(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == value for name, value in original_hashes.items())
    results["workspace_production_unchanged"] = unchanged
    results["production_sha256"] = original_hashes
    (OUT / "results.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    print(("PASS" if unchanged else "FAIL") + ": workspace production SHA-256 unchanged")
    return 0 if unchanged and all(value["passed"] for key, value in results.items()
                                  if isinstance(value, dict) and "passed" in value) else 1


if __name__ == "__main__":
    raise SystemExit(main())
