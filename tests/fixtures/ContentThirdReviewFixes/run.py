"""Run production-linked content regressions; requires .NET 8 SDK, no game process."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys

HERE = Path(__file__).resolve().parent


def extract_presentation():
    root = HERE.parents[2]
    out = root / "Build/content-third-review-fixes/DailyReport"
    out.mkdir(parents=True, exist_ok=True)
    paths = {
        "DailyReportView": ("Integration/DailyReport/DailyReportUI.cs", (
            "private void FitPaper()", "private static string BuildBountyBlock(")),
        "DailyReportMailboxBuilder": ("Integration/DailyReport/DailyReportMailboxRuntime.cs", (
            "private bool InjectDailyReportBuildingData()", "private static void SetDailyReportBuildingInfoField(",
            "private void SetDailyReportBuildingCost(")),
    }
    code = "using System; using System.Collections; using System.Collections.Generic; using System.Reflection; using UnityEngine; using TMPro; namespace BossRush {\n"
    hashes = {}
    for cls, (path, signatures) in paths.items():
        raw = (root / path).read_bytes()
        hashes[path] = hashlib.sha256(raw).hexdigest()
        source = raw.decode("utf-8-sig")
        code += "partial class " + cls + " {\n"
        if cls == "DailyReportView":
            # 2026-09-20：面板尺寸改为引用版面表常量，不再是字面量
            for constant in ("PanelWidth", "PanelHeight"):
                declaration = re.search(
                    r"private const float " + constant + r"\s*=\s*DailyReportLayoutTable\." + constant + r";",
                    source)
                assert declaration, constant
                code += declaration.group(0) + "\n"
        for signature in signatures:
            start = source.index(signature)
            opening = source.index("{", start)
            depth, end = 1, opening + 1
            while depth:
                depth += (source[end] == "{") - (source[end] == "}")
                end += 1
            code += source[start:end] + "\n"
        code += "}\n"
    code += "}\n"
    (out / "ExtractedPresentation.cs").write_text(code, encoding="utf-8")
    (out / "presentation-source-hashes.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")


def main():
    dotnet = shutil.which("dotnet")
    if not dotnet:
        print("ERROR: .NET 8 SDK is required; these regressions were not run.")
        return 1
    extract_presentation()
    root = HERE.parents[2]
    sys.path.insert(0, str(root / 'tools'))
    sys.path.insert(0, str(root / 'tests'))
    from cs_source_util import clean_source
    dragon_out = root / 'Build/content-third-review-fixes/DragonSpawn'
    dragon_out.mkdir(parents=True, exist_ok=True)
    dragon_code = 'using System; using System.Collections; using System.Threading.Tasks; using UnityEngine; using Cysharp.Threading.Tasks; namespace BossRush {\n'
    dragon_hashes = {}
    for path, cls, signatures in (
        ('Integration/DragonDescendant/DragonDescendantBoss.cs', 'DragonDescendantRuntimeModule',
         ('public async UniTask<CharacterMainControl> SpawnDragonDescendant(',)),
        ('Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs', 'DragonDescendantRuntimeModule',
         ('internal void CleanupCancelledDragonDescendant(',)),
        ('Integration/DragonDescendant/DragonDescendantAbilities.cs', 'DragonDescendantAbilityController',
         ('public void Initialize(', 'internal bool HasCombatAuthority',
          'private bool UsesArenaOpponent()', 'private void RefreshPlayerReference()')),
        ('Integration/DragonDescendant/DragonDescendantAbilities_ProjectilesAndGrenades.cs', 'DragonDescendantAbilityController',
         ('private void OnBossShoot(', 'private IEnumerator GrenadeTimerCoroutine()')),
        ('Integration/DragonDescendant/DragonDescendantAbilities_ResurrectionAndPhase.cs', 'DragonDescendantAbilityController',
         ('internal bool ShouldClampLethalHealthDuringHurt()',)),
    ):
        raw = (root / path).read_bytes()
        dragon_hashes[path] = hashlib.sha256(raw).hexdigest()
        source = clean_source(raw.decode('utf-8-sig'))
        dragon_code += 'partial class ' + cls + ' {\n'
        for signature in signatures:
            assert source.count(signature) == 1, signature
            start = source.index(signature)
            end = source.index('{', start) + 1
            depth = 1
            while depth:
                depth += (source[end] == '{') - (source[end] == '}')
                end += 1
            # Only the awaitable carrier changes; the complete production method body is retained.
            dragon_code += source[start:end].replace('async UniTask<CharacterMainControl>', 'async Task<CharacterMainControl>') + '\n'
        dragon_code += '}\n'
    lease_path = 'Integration/DragonKing/DragonKingCoopContactRuntimeModule.cs'
    lease_raw = (root / lease_path).read_bytes()
    dragon_hashes[lease_path] = hashlib.sha256(lease_raw).hexdigest()
    lease_source = clean_source(lease_raw.decode('utf-8-sig'))
    start = lease_source.index('internal sealed class CoopBossCreationLease')
    end = lease_source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (lease_source[end] == '{') - (lease_source[end] == '}')
        end += 1
    dragon_code += lease_source[start:end] + '\n'
    (dragon_out / 'ExtractedDragonSpawn.cs').write_text(dragon_code + '}\n', encoding='utf-8')
    (dragon_out / 'production-source-hashes.json').write_text(json.dumps(dragon_hashes, indent=2), encoding='utf-8')
    capture_path = 'DebugAndTools/F3GameplayValidationAutotestCapture.cs'
    capture_raw = (root / capture_path).read_bytes()
    # Explicitly expose this Dev-only method to the fixture, without enabling unrelated Dev files.
    capture_source = clean_source(capture_raw.decode('utf-8-sig').replace('#if BOSSRUSH_DEV', ''))
    signature = 'private IEnumerator CaptureAutotestShot('
    assert capture_source.count(signature) == 1, signature
    start = capture_source.index(signature)
    end = capture_source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (capture_source[end] == '{') - (capture_source[end] == '}')
        end += 1
    capture_out = root / 'Build/content-third-review-fixes/CaptureTiming'
    capture_out.mkdir(parents=True, exist_ok=True)
    (capture_out / 'ExtractedCapture.cs').write_text('using System; using System.Collections; using System.Collections.Generic; using System.IO; using UnityEngine; namespace BossRush { partial class F3GameplayValidationRunner {\n' + capture_source[start:end] + '\n}}', encoding='utf-8')
    (capture_out / 'production-source-hashes.json').write_text(json.dumps({capture_path: hashlib.sha256(capture_raw).hexdigest()}, indent=2), encoding='utf-8')
    witch_out = root / 'Build/content-third-review-fixes/Codex'
    witch_out.mkdir(parents=True, exist_ok=True)
    witch_sources = (
        ('Integration/PhantomWitch/PhantomWitchBoss.cs', 'PhantomWitchRuntimeModule',
         'private void OnPhantomWitchDeath('),
        ('Integration/DragonKing/DragonKingBoss.cs', 'DragonKingRuntimeModule',
         'private void OnDragonKingDeath('),
        ('Integration/DragonDescendant/DragonDescendantBoss_RuntimeAndCleanup.cs', 'DragonDescendantRuntimeModule',
         'private void OnDragonDescendantDeath('),
        ('Integration/DragonDescendant/DragonDescendantBoss.cs', 'DragonDescendantRuntimeModule',
         'internal CharacterRandomPreset FindFallbackPreset()'),
        ('Utilities/BossCleanupHelpers.cs', 'BossCleanupHelpers',
         'public static void DestroyRuntimePreset('),
    )
    generated = 'using System; namespace BossRush {\n'
    hashes = {}
    for path, cls, signature in witch_sources:
        raw = (root / path).read_bytes()
        hashes[path] = hashlib.sha256(raw).hexdigest()
        source = clean_source(raw.decode('utf-8-sig'))
        if cls == 'PhantomWitchRuntimeModule':
            preset_name = re.search(r'customPreset\.name\s*=\s*([^;]+);', source)
            assert preset_name, 'production witch runtime preset name'
            generated += 'partial class PhantomWitchRuntimeModule { internal static readonly string RuntimePresetName = ' + preset_name.group(1) + '; }\n'
        assert source.count(signature) == 1, signature
        start = source.index(signature)
        end = source.index('{', start) + 1
        depth = 1
        while depth:
            depth += (source[end] == '{') - (source[end] == '}')
            end += 1
        generated += 'partial class ' + cls + ' {\n' + source[start:end] + '\n}\n'
    for path, cls in (
        ('Integration/DragonKing/DragonKingBoss.cs', 'DragonKingRuntimeModule'),
        ('Integration/DragonDescendant/DragonDescendantBoss.cs', 'DragonDescendantRuntimeModule'),
    ):
        raw = (root / path).read_bytes()
        hashes[path] = hashlib.sha256(raw).hexdigest()
        source = clean_source(raw.decode('utf-8-sig'))
        preset_name = re.search(r'customPreset\.name\s*=\s*([^;]+);', source)
        assert preset_name, path + ' runtime preset name'
        generated += 'partial class ' + cls + ' { internal static readonly string RuntimePresetName = ' + preset_name.group(1) + '; }\n'
    generated += '}\n'
    (witch_out / 'ExtractedWitchDeath.cs').write_text(generated, encoding='utf-8')
    (witch_out / 'witch-death-source-hashes.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
    from run_runtime_regressions import run_project_fixture
    failures = []
    for name in ("DailyReport", "Codex", "DragonSpawn", "CaptureTiming"):
        code, output = run_project_fixture(HERE / name / (name + ".csproj"),
            root / 'Build/content-third-review-fixes' / name / 'runs', HERE)
        print(output)
        if code:
            failures.append(name)
    if failures:
        print("FAIL: " + ", ".join(failures))
        return 1
    print("ContentThirdReviewFixes: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
