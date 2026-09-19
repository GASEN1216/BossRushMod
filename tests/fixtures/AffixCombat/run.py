"""Run production combat dispatch/effects with controlled host substitutes."""
from pathlib import Path
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/affix-combat"


def member(source, signature):
    # The selected members have no braces in string literals. Keep the original bytes of the body.
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for i in range(opening, len(source)):
        depth += (source[i] == '{') - (source[i] == '}')
        if depth == 0:
            return source[start:i + 1]
    raise AssertionError(signature)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    rewards = (ROOT / "ZombieMode/ZombieModeRewardTriggerEffects.cs").read_text(encoding="utf-8-sig")
    skills = (ROOT / "ZombieMode/ZombieModePollution_RuntimeSkills.cs").read_text(encoding="utf-8-sig")
    mutators = (ROOT / "Integration/Mutators/MutatorManager.cs").read_text(encoding="utf-8-sig")
    definitions = (ROOT / "Integration/Mutators/MutatorDefinitions.cs").read_text(encoding="utf-8-sig")
    models = (ROOT / "ZombieMode/ZombieModeModels.cs").read_text(encoding="utf-8-sig")
    volatile = definitions[definitions.index('Id = "explode_on_death"'):]
    callback = member(volatile, "OnApply = ctx =>")
    callback = callback[callback.index('{'):]
    generated = 'using System; using System.Collections; using UnityEngine;\nnamespace BossRush { public sealed partial class ModBehaviour {\n'
    generated += member(rewards, "private void CreateZombieModeOptionExplosion(") + '\n'
    generated += member(rewards, "private void TriggerZombieModeDoomPulse(") + '\n'
    generated += member(skills, "public void DealZombieModeExplosionAreaDamage(") + '\n}\n'
    generated += 'internal static partial class MutatorManager {\n'
    generated += member(mutators, "private static void OnAnyCharacterDead(") + '\n'
    generated += member(mutators, "private static IEnumerator DispatchEnemyKilledNextFrame(") + '\n'
    generated += 'internal static void BindVolatile(MutatorContext ctx) ' + callback + '\n}\n'
    generated += member(models, "public sealed class ZombieModeRunOnlyRecord") + '\n}'
    forge = (ROOT / "Integration/AffixForge/AffixForgeSystem.cs").read_text(encoding="utf-8-sig")
    item_data = (ROOT / "Integration/AffixForge/AffixItemData.cs").read_text(encoding="utf-8-sig")
    generated += "\nnamespace BossRush { public static class AffixForgeSystem {\n"
    for signature in ("public static bool CanAffixForge(", "public static int GetMoneyCost(",
                      "public static int GetStoneCost(", "public static int GetUnlockedSlotCount(",
                      "public static int GetSlotCount("):
        generated += member(forge, signature) + "\n"
    generated += "} public static partial class AffixItemData {\n"
    generated += member(item_data, "public static AffixEquipMask GetEquipMask(") + "\n}}"
    generated = "using ItemStatsSystem;\n" + generated
    extracted = OUT / "ExplosionEntrypoints.cs"
    extracted.write_text(generated, encoding="utf-8")
    sources = [ROOT / "Integration/AffixForge" / name for name in (
        "AffixRuntimeService.cs", "AffixRuntimeService_Effects.cs", "AffixDefinitions.cs",
    )] + [ROOT / "ZombieMode/ZombieModeRuntimeModule.cs", ROOT / "Utilities/RunScopedRegistry.cs",
          HERE / "Program.cs", HERE / "Stubs.cs", extracted]
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
    project += '<TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion>'
    project += '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
    project += ''.join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project += '</ItemGroup></Project>'
    path = OUT / "AffixCombat.csproj"
    path.write_text(project, encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(path), "--configuration", "Release"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
