"""Mutate sparse copies only; ensure the integration guard fails at the intended assertion."""
from contextlib import redirect_stdout
from pathlib import Path
import hashlib
import io
import json
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tests"))
import EclipseBloomIntegrationGuard as guard
import EquipmentFactoryConfiguratorRegistrationGuard as equipment_guard


def main():
    paths = list(guard.FIXED_PATHS)
    paths += [path.relative_to(ROOT).as_posix() for path in (ROOT / guard.PREFIX).glob("*.cs")]
    paths += [path.relative_to(ROOT).as_posix() for path in (ROOT / "Integration/SandstormChampion").glob("*.cs")]
    paths += [path for path, _, _ in equipment_guard.CONFIGS]
    paths += ["tests/CampaignArenaClearIsolationGuard.py", "tests/EquipmentFactoryConfiguratorRegistrationGuard.py",
              "tests/LootBlacklistDataRegistryGuard.py", "tests/ArchitectureStructureGuard.py", "tests/cs_source_util.py",
              "tests/SandstormChampionCombatGuard.py",
              "Integration/EquipmentFactory.cs", "Integration/EquipmentFactoryStaticCacheReset.cs",
              "Integration/BossRushIntegrationRuntimeModule_DeferredBootstrap.cs"]
    paths = sorted(set(paths))
    before = {path: hashlib.sha256((ROOT / path).read_bytes()).hexdigest() for path in paths}
    results = []
    out = ROOT / "Build/runtime-regressions/ResidualSunShield"
    out.mkdir(parents=True, exist_ok=True)
    probes = (
        ("module registration", "Integration/FieldAdventures/ExplorationRuntimeRegistration.cs",
         "host.Register(new EclipseBloomRuntimeModule());", "// removed module registration",
         "host.Register(new EclipseBloomRuntimeModule());"),
        ("item configuration", "Integration/NewWeapons/Common/NewWeaponItemConfigurators.cs",
         "ItemFactory.RegisterConfigurator(BossRushItemIds.ResidualSunShield, item => ResidualSunShieldWeaponConfig.TryConfigure(item));",
         "// removed item configuration", "缺少 ItemFactory.RegisterConfigurator(BossRushItemIds.ResidualSunShield"),
        ("actual final damage bridge", "ZombieMode/ZombieModeDamageRuntime.cs",
         "ResidualSunShieldRuntime.ModifyFinalDamage(health, ref info);", "// removed final damage bridge",
         "护盾必须在丧尸模式早返前"),
        ("async generation invalidation", guard.PREFIX + "EclipseBloomRuntimeModule.cs",
         "generation++;\n            active = false;\n            spawning = false;",
         "active = false;\n            spawning = false;", "private void Cleanup(): 缺少 generation++;"),
        ("death reward preparation", guard.PREFIX + "EclipseBloomBoss.cs",
         "if (!ResidualSunShieldWeaponConfig.TryConfigure(reward))",
         "if (reward == null)", "顺序缺少 ResidualSunShieldWeaponConfig.TryConfigure(reward)"),
        ("async preparation hidden from hits", guard.PREFIX + "EclipseBloomBoss.cs",
         "boss.gameObject.SetActive(false);", "// removed staging deactivation",
         "顺序缺少 boss.gameObject.SetActive(false);"),
        ("dead async result rejected", guard.PREFIX + "EclipseBloomBoss.cs",
         "if (boss == null || boss.Health == null || boss.Health.IsDead)",
         "if (boss == null || boss.Health == null)", "顺序缺少 if (boss == null || boss.Health == null || boss.Health.IsDead)"),
        ("petal official receiver wiring", guard.PREFIX + "EclipseBloomPetal.cs",
         "petal.health.dmgReceiver = receiver;", "petal.health.dmgReceiver = null;",
         "缺少 petal.health.dmgReceiver = receiver;"),
        ("petal collider orientation", guard.PREFIX + "EclipseBloomPetal.cs",
         "collider.direction = 2;", "collider.direction = 1;", "缺少 collider.direction = 2;"),
        ("core official health forwarding", guard.PREFIX + "EclipseBloomAbilityController.cs",
         "receiver.health = boss.Health;", "receiver.health = null;", "缺少 receiver.health = boss.Health;"),
        ("petal collider follows visual", guard.PREFIX + "EclipseBloomAbilityController.cs",
         "petals[i].transform.rotation = body.GetPetalRotation(i);", "// removed rotation synchronization",
         "缺少 petals[i].transform.rotation = body.GetPetalRotation(i);"),
        ("petal collider follows unfolding scale", guard.PREFIX + "EclipseBloomAbilityController.cs",
         "petals[i].transform.localScale = body.GetPetalScale(i);", "// removed unfolding scale synchronization",
         "缺少 petals[i].transform.localScale = body.GetPetalScale(i);"),
        ("restore official identity before death", guard.PREFIX + "EclipseBloomAbilityController.cs",
         "character.characterPreset = controller.originalPreset;", "// removed identity restoration",
         "缺少 character.characterPreset = controller.originalPreset;"),
        ("real official death prefix", "Patches/Combat/CharacterOnDeadPatch.cs",
         "if (EclipseBloomAbilityController.RestoreOfficialPresetBeforeDeath(__instance)) return;",
         "// removed death identity prefix", "官方死亡前缀必须先恢复花冠计数身份"),
        ("pending guard cancellation", guard.PREFIX + "ResidualSunShieldRuntime.cs",
         "guardFacing = Vector3.zero;\n            rules.CancelPending();",
         "guardFacing = Vector3.zero;", "private static void Deactivate(): 缺少 rules.CancelPending();"),
        ("scene fade cancellation", guard.PREFIX + "ResidualSunShieldRuntime.cs",
         "private static void OnSceneLoadingStarted(SceneLoadingContext context) { ResetStaticCaches(); }",
         "private static void OnSceneLoadingStarted(SceneLoadingContext context) { }",
         "private static void OnSceneLoadingStarted(: 缺少 ResetStaticCaches();"),
        ("idle coroutine exemption", "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs",
         "if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive)\n                {\n                    yield return owner.ArenaSharedWait05s;",
         "if (owner.IsCampaignFinalBossActive)\n                {\n                    yield return owner.ArenaSharedWait05s;",
         "缺少 if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive) { yield return"),
        ("direct clear exemption", "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs",
         "if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive) return;",
         "if (owner.IsCampaignFinalBossActive) return;", "缺少 if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive) return;"),
        ("native encounter consumes raid", guard.PREFIX + "EclipseBloomRuntimeModule.cs",
         "consumed = true;", "consumed = false;", "缺少 consumed = true;"),
        ("level owner identity", guard.PREFIX + "EclipseBloomEncounterPlacement.cs",
         "core != null ? core.gameObject.scene : level.gameObject.scene",
         "SceneManager.GetActiveScene()", "缺少 core != null ? core.gameObject.scene : level.gameObject.scene"),
        ("native pending map exclusion", guard.PREFIX + "EclipseBloomRuntimeModule.cs",
         "|| BossRushMapSelectionHelper.GetPendingEntryKind() != BossRushPendingEntryKind.None", "",
         "缺并行模式隔离 BossRushMapSelectionHelper.GetPendingEntryKind()"),
        ("visible ground required before damage", guard.PREFIX + "EclipseBloomAbilityController.cs",
         "if (body == null || !body.HasGroundWarningAt(player.transform.position)) return;", "",
         "缺少 if (body == null || !body.HasGroundWarningAt(player.transform.position)) return;"),
        ("ground sampling frame budget", guard.PREFIX + "EclipseBloomGroundProjection.cs",
         "internal const int SamplesPerFrame = 96;", "internal const int SamplesPerFrame = 960;",
         "地面预警采样预算必须保持每帧 96"),
        ("projected triangle clipping", guard.PREFIX + "EclipseBloomVisuals.cs",
         "if (valid[original[j]] && valid[original[j + 1]] && valid[original[j + 2]])", "if (true)",
         "缺少 if (valid[original[j]] && valid[original[j + 1]] && valid[original[j + 2]])"),
        ("dedicated pickup model configuration", guard.PREFIX + "ResidualSunShieldWeaponConfig.cs",
         "if (!ResidualSunShieldModel.Bind(item)) return false;", "",
         "缺少 if (!ResidualSunShieldModel.Bind(item)) return false;"),
        ("pickup uses actual mesh", guard.PREFIX + "ResidualSunShieldModel.cs",
         "item.useSpriteForPickup = false;", "item.useSpriteForPickup = true;", "缺少 item.useSpriteForPickup = false;"),
        ("shield authored bundle load", guard.PREFIX + "ResidualSunShieldModel.cs",
         "EquipmentFactory.LoadBundle(BundleName);", "", "缺少 EquipmentFactory.LoadBundle(BundleName);"),
        ("shield authored GroundPoint required", guard.PREFIX + "ResidualSunShieldModel.cs",
         '|| model.transform.Find("GroundPoint") == null', "",
         '缺少 if (model == null || model.transform.Find("GroundPoint")'),
        ("shield complete authored parts required", guard.PREFIX + "ResidualSunShieldModel.cs",
         "|| model.GetComponentsInChildren<MeshFilter>(true).Length != 6", "",
         '缺少 if (model == null || model.transform.Find("GroundPoint")'),
        ("shield borrowed resource ownership", guard.PREFIX + "ResidualSunShieldModel.cs",
         "template = model;", "UnityEngine.Object.Destroy(model.gameObject); template = model;",
         "盾片模型仅借用工厂资源"),
        ("native introduction identity", guard.PREFIX + "EclipseBloomRuntimeModule.cs",
         "current.boss != defeated", "false", "缺少 current.boss != defeated"),
        ("Boss pool catalogue registration", "WavesArena/WavesArenaRuntimeModule_EnemyPresets.cs",
         "EclipseBloomPoolAdapter.Register(owner);", "", "缺少 EclipseBloomPoolAdapter.Register(owner);"),
        ("legacy Boss pool spawn route", "WavesArena/WavesArenaRuntimeModule_LegacySpawn.cs",
         "if (EclipseBloomPoolAdapter.IsPreset(preset))", "if (false)",
         "缺少 if (EclipseBloomPoolAdapter.IsPreset(preset))"),
        ("shared Boss pool spawn route", "Utilities/EnemySpawnCore.cs",
         "else if (EclipseBloomPoolAdapter.IsPreset(currentPreset))\n                        {",
         "else if (false)\n                        {",
         "缺少 else if (EclipseBloomPoolAdapter.IsPreset(currentPreset))"),
        ("introduction lifecycle registration", "Integration/FieldAdventures/ExplorationRuntimeRegistration.cs",
         "host.Register(new EclipseBloomIntroduction());", "",
         "缺少 host.Register(new EclipseBloomIntroduction());"),
        ("official delivery transaction", guard.PREFIX + "EclipseBloomIntroduction.cs",
         "BeginDelivery = BeginDelivery", "BeginDelivery = null", "缺少 BeginDelivery = BeginDelivery"),
        ("death abandonment latch", guard.PREFIX + "EclipseBloomIntroduction.cs",
         "abandonPending = true;", "abandonPending = false;", "缺少 abandonPending = true;"),
        ("fog hidden warning cannot damage", guard.PREFIX + "EclipseBloomVisuals.cs",
         "|| _fills[sector].forceRenderingOff", "", "缺少 _fills[sector].forceRenderingOff"),
        ("dead committed instance reward owner", guard.PREFIX + "EclipseBloomAbilityController.cs",
         "!subscribed", "false", "缺少 !subscribed"),
        ("shield icon borrowed ownership", guard.PREFIX + "ResidualSunShieldIcon.cs",
         "private void OnDestroy() { sprite = null; }", "private void OnDestroy() { UnityEngine.Object.Destroy(sprite); sprite = null; }",
         "正式盾片图标只借用专属包资源"),
        ("concurrent source excludes runtime clone", guard.PREFIX + "EclipseBloomBoss.cs",
         "preset.name != \"BossRush_EclipseBloom_Runtime\" &&", "",
         "缺少 preset.name != \"BossRush_EclipseBloom_Runtime\""),
    )
    legacy_probes = (
        ("campaign direct clear stays protected", "tests/CampaignArenaClearIsolationGuard.py",
         "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs",
         "if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive) return;",
         "if (EclipseBloomRuntimeModule.IsEncounterActive) return;", "直接清场必须先检查终章与花冠占场"),
        ("bloom initial scan stays protected", "tests/CampaignArenaClearIsolationGuard.py",
         "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs",
         "if (!owner.IsCampaignFinalBossActive && !EclipseBloomRuntimeModule.IsEncounterActive) RefreshCharacterCache();",
         "if (!owner.IsCampaignFinalBossActive) RefreshCharacterCache();", "协程初始扫描必须避开终章与花冠占场"),
        ("campaign wait keeps loop budget", "tests/CampaignArenaClearIsolationGuard.py",
         "WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs",
         "if (owner.IsCampaignFinalBossActive || EclipseBloomRuntimeModule.IsEncounterActive)\n                {\n                    yield return owner.ArenaSharedWait05s;",
         "if (EclipseBloomRuntimeModule.IsEncounterActive)\n                {\n                    yield return owner.ArenaSharedWait05s;", "持续清场必须先等待并重试"),
        ("shield equipment bootstrap", "tests/EquipmentFactoryConfiguratorRegistrationGuard.py",
         "Integration/EquipmentConfiguratorBootstrap.cs", "ResidualSunShieldWeaponConfig.RegisterEquipmentConfigurator();",
         "// removed shield bootstrap", "registration order changed"),
        ("shield equipment self registration", "tests/EquipmentFactoryConfiguratorRegistrationGuard.py",
         guard.PREFIX + "ResidualSunShieldWeaponConfig.cs", 'EquipmentFactory.RegisterConfigurator("ResidualSunShieldWeaponConfig",',
         'EquipmentFactory.RegisterConfigurator("IncorrectResidualSunShieldWeaponConfig",', "does not register itself"),
        ("shield blacklist source identity", "tests/LootBlacklistDataRegistryGuard.py", "Config/ConfigItemIds.cs",
         "public const int ResidualSunShield = 500110;", "public const int ResidualSunShield = 500111;",
         "itemIds do not match hardcoded fallback ids"),
        ("minion death exemption remains", "tests/SandstormChampionCombatGuard.py", "Patches/Combat/CharacterOnDeadPatch.cs",
         "if (SandstormChampionMinionMarker.IsSummonedMinion(__instance))\n                return;",
         "// removed minion exemption", "必须先恢复花冠身份，再立即让小弟早返"),
    )
    with tempfile.TemporaryDirectory(prefix="integration-probes-", dir=out) as location:
        copied = Path(location)
        for path in paths:
            destination = copied / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / path, destination)
        with redirect_stdout(io.StringIO()):
            guard.validate(copied)
        for name, path, anchor, replacement, expected in probes:
            destination = copied / path
            original = destination.read_bytes()
            content = original.decode("utf-8-sig").replace("\r\n", "\n")
            assert content.count(anchor) == 1, name + ": anchor must match exactly once"
            destination.write_text(content.replace(anchor, replacement), encoding="utf-8")
            try:
                with redirect_stdout(io.StringIO()):
                    guard.validate(copied)
            except AssertionError as error:
                assert expected in str(error), name + ": unexpected assertion: " + str(error)
                print("PASS negative probe: " + name)
            else:
                raise AssertionError(name + ": mutation passed guard")
            finally:
                destination.write_bytes(original)
                assert hashlib.sha256(destination.read_bytes()).digest() == hashlib.sha256(original).digest()
            results.append({"name": name, "passed": True, "expected_assertion": expected, "restored": True})
        def run_legacy(script, expected_code=0, expected_text="PASS"):
            result = subprocess.run([sys.executable, "-X", "utf8", str(copied / script)], cwd=copied,
                                    text=True, capture_output=True, encoding="utf-8")
            output = result.stdout + result.stderr
            assert result.returncode == expected_code and expected_text in output, script + ": " + output

        for script in sorted({probe[1] for probe in legacy_probes}):
            run_legacy(script)
        for name, script, path, anchor, replacement, expected in legacy_probes:
            destination = copied / path
            original = destination.read_bytes()
            content = original.decode("utf-8-sig").replace("\r\n", "\n")
            assert content.count(anchor) == 1, name + ": anchor must match exactly once"
            destination.write_text(content.replace(anchor, replacement), encoding="utf-8")
            try:
                run_legacy(script, 1, expected)
                print("PASS negative probe: " + name)
            finally:
                destination.write_bytes(original)
                assert destination.read_bytes() == original
            results.append({"name": name, "passed": True, "expected_assertion": expected, "restored": True})
            run_legacy(script)
        # 真正移动小弟分支到首个追加掉落之后，不能只靠保留 token 让守卫通过。
        destination = copied / "Patches/Combat/CharacterOnDeadPatch.cs"
        original = destination.read_bytes()
        content = original.decode("utf-8-sig").replace("\r\n", "\n")
        minion = "if (SandstormChampionMinionMarker.IsSummonedMinion(__instance))\n                return;"
        handler = "FrostmourneBlueBossDropHandler.TryHandleBlueBossDeath(__instance);"
        assert content.count(minion) == content.count(handler) == 1
        destination.write_text(content.replace(minion, "").replace(handler, handler + "\n            " + minion), encoding="utf-8")
        try:
            run_legacy("tests/SandstormChampionCombatGuard.py", 1, "必须先恢复花冠身份，再立即让小弟早返")
            print("PASS negative probe: minion exemption moved behind extra drop")
        finally:
            destination.write_bytes(original)
            assert destination.read_bytes() == original
        results.append({"name": "minion exemption moved behind extra drop", "passed": True,
                        "expected_assertion": "必须先恢复花冠身份，再立即让小弟早返", "restored": True})
        run_legacy("tests/SandstormChampionCombatGuard.py")
        with redirect_stdout(io.StringIO()):
            guard.validate(copied)
    for path, signature in before.items():
        assert hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == signature, "production changed during probes: " + path
    (out / "integration-negative-results.json").write_text(json.dumps({"probes": results,
        "workspace_production_unchanged": True, "production_sha256": before}, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("EclipseBloomIntegrationGuard and 4 legacy guards: " + str(len(probes) + len(legacy_probes) + 1)
          + " isolated negative probes PASS; production SHA-256 unchanged")


if __name__ == "__main__":
    main()
