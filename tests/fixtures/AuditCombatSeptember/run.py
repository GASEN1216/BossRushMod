"""Extract current production members and execute deterministic combat regressions."""
from pathlib import Path
import hashlib
import html
import json
import os
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / "Build/runtime-regressions/AuditCombatSeptember"
HASHES = {}


def source(path):
    data = (ROOT / path).read_bytes()
    HASHES[path] = hashlib.sha256(data).hexdigest()
    return data.decode("utf-8-sig")


def member(path, marker):
    text = source(path)
    start = text.index(marker)
    end = text.index("{", start) + 1
    # All selected members are checked for balanced braces; bodies remain unchanged.
    depth = 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[start:end]


def methods(path, markers):
    return "\n".join(member(path, marker) for marker in markers)


def main():
    base = "Common/Equipment/EquipmentAbilityAction.cs"
    flight = "Integration/FlightTotem/CA_Flight.cs"
    child = "Integration/DragonKing/DragonKingAbilityController_ChildProtection.cs"
    pool = "Integration/DragonKing/Weapons/DragonKingBossGunRuntime_ProjectilesAndPatches.cs"
    vfx = "Integration/PhantomWitch/PhantomWitchVfxRedesign.cs"
    owner = "Integration/PhantomWitch/PhantomWitchAbilityController_CleanupAndTelemetry.cs"
    curse = "Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs"
    realm = source("Integration/PhantomWitch/PhantomWitchScytheAction.cs")
    damage_start = realm.index("DamageInfo damageInfo = new DamageInfo(caster);")
    damage_end = realm.index("damageInfo.AddElementFactor(ElementTypes.ghost, 1f);", damage_start) + len("damageInfo.AddElementFactor(ElementTypes.ghost, 1f);")
    asset = source("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
    modifier = re.search(r"SetFieldSafe\(modifier, modifierTypeField, (ItemStatsSystem.Stats.ModifierType.\w+)\);", asset).group(1)
    slow = re.search(r"CurseSlowPerLayer\s*=\s*([-\d.]+f)", source("Integration/PhantomWitch/PhantomWitchConfig.cs")).group(1)
    replacements = {
        "Fire": {"FIRE_METHOD": member("Integration/Bonus/DragonSetBonus.cs", "private void OnDragonSetHurt(")},
        "Flight": {
            "BASE_METHOD": member(base, "protected override void OnUpdateAction("),
            "BASE_POLICY": re.search(r"protected virtual bool ContinueUpdatingWhenStaminaDepleted[^;]+;", source(base)).group(0),
            "FLIGHT_POLICY": re.search(r"protected override bool ContinueUpdatingWhenStaminaDepleted[^;]+;", source(flight)).group(0),
            "FLIGHT_METHODS": methods(flight, ["protected override void OnAbilityUpdate(", "protected override bool ShouldAutoConsumeStamina(", "protected override void OnStaminaDepleted(", "private void UpdateFlight(", "private void UpdateFlightPlatform("])
        },
        "Child": {"CHILD_METHODS": methods(child, ["private IEnumerator SpawnDescendantForProtection(", "private async void SpawnDescendantAsync(", "private void ReleaseChild("])},
        "Pool": {"POOL_METHODS": methods(pool, ["internal static void RequestFireExplosionEffectWarmup(", "private static async UniTaskVoid WarmFireExplosionEffectPoolAsync(", "internal static void ClearFireExplosionEffectPool("]).replace("async UniTaskVoid", "async Task")},
        "Mark": {},
        "Wraith": {
            "WRAITH_METHODS": methods("Integration/DeathWraith/DeathWraithSpawnFlow.cs", [
                "private async void TrySpawnStoredDeathWraithForRaid_DeathWraith(",
                "private async UniTask<CharacterMainControl> CreateWraithCharacterFromPlayerSnapshot_DeathWraith("
            ]).replace("async UniTask<CharacterMainControl>", "async Task<CharacterMainControl>"),
            "WRAITH_CLEANUP": methods("Integration/DeathWraith/DeathWraithLifecycleAndPersistence.cs", [
                "private void ClearDeathWraithState_DeathWraith(", "private void RegisterActiveWraith_DeathWraith("
            ]) + member("Integration/DeathWraith/DeathWraithSystem.cs", "private void OnSetFile_DeathWraith(")
        },
        "Vfx": {
            "VFX_METHODS": methods(vfx, ["private static GameObject GetOrBuildVfx(", "private static bool TryAcquireCleanPooledRoot(", "private static bool IsReusablePooledRoot(", "private static void CleanupRootForPooling(", "internal sealed class PhantomWitchVfxRecycler", "private static GameObject CreateRoot("]),
            "OWNER_METHODS": methods(owner, ["private void PruneDestroyedEffects(", "private void TrackEffect(", "private void CleanupAllEffects(", "internal void UntrackPooledEffect("]),
            "ATTRIBUTION_METHOD": member("Integration/AffixForge/AffixRuntimeService.cs", "private static bool IsPlayerHitOnEnemy("),
            "REALM_DAMAGE": realm[damage_start:damage_end]
        },
        "Curse": {
            "CURSE_METHODS": methods(curse, ["private static void OnGlobalHurt(", "private static CharacterMainControl TryGetTargetCharacter(", "private static CharacterBuffManager TryGetBuffManager("]),
            "CURSE_MODIFIER": modifier,
            "CURSE_SLOW": slow
        }
    }
    candidates = []
    if os.environ.get("GAME_PATH"):
        candidates.append(Path(os.environ["GAME_PATH"]) / "Duckov_Data/Managed")
    candidates.extend(p / "Managed" for p in ROOT.parents)
    managed = next((p for p in candidates if (p / "ItemStatsSystem.dll").exists()), None)
    if managed is None:
        raise RuntimeError("Official ItemStatsSystem.dll is required for the curse Stat regression")
    OUT.mkdir(parents=True, exist_ok=True)
    results = {}
    for name, values in replacements.items():
        target = OUT / name
        target.mkdir(exist_ok=True)
        text = (HERE / (name + ".cs.txt")).read_text(encoding="utf-8")
        for key, value in values.items():
            assert text.count("/* " + key + " */") == 1, key
            text = text.replace("/* " + key + " */", value)
        (target / "Program.cs").write_text(text, encoding="utf-8")
        includes = ['<Compile Include="Program.cs" />']
        if name == "Mark":
            path = "Integration/DragonKing/Weapons/DragonFlameMarkTracker.cs"
            source(path)
            includes.append('<Compile Include="' + html.escape(str(ROOT / path)) + '" />')
        if name == "Curse":
            path = "Integration/PhantomWitch/PhantomWitchPerformancePolicy.cs"
            source(path)
            includes.append('<Compile Include="' + html.escape(str(ROOT / path)) + '" />')
            for dll in ["ItemStatsSystem", "UnityEngine.CoreModule"]:
                includes.append('<Reference Include="' + dll + '"><HintPath>' + html.escape(str(managed / (dll + ".dll"))) + '</HintPath></Reference>')
        project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' + ''.join(includes) + '</ItemGroup></Project>'
        (target / "Probe.csproj").write_text(project, encoding="utf-8")
        build = subprocess.run(["dotnet", "build", str(target / "Probe.csproj"), "-c", "Release", "--nologo", "-o", str(target / "bin"), "-p:BaseIntermediateOutputPath=" + str(target / "obj") + "/"], cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
        (target / "build.log").write_text(build.stdout + build.stderr, encoding="utf-8")
        if build.returncode:
            print(build.stdout + build.stderr)
            return build.returncode
        run = subprocess.run(["dotnet", str(target / "bin/Probe.dll")], cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
        (target / "result.log").write_text(run.stdout + run.stderr, encoding="utf-8")
        print(name + ": " + ("PASS" if run.returncode == 0 else "FAIL"))
        results[name] = {"passed": run.returncode == 0, "output": run.stdout + run.stderr}
        if run.returncode:
            print(run.stdout + run.stderr)
            return run.returncode
    (OUT / "source-hashes.json").write_text(json.dumps(HASHES, indent=2), encoding="utf-8")
    (OUT / "results.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
