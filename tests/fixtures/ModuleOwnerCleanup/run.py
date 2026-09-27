"""Execute real owner cleanup, event registration/unregistration and Arena actions."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = Path(os.environ.get("BOSSRUSH_FIXTURE_OUT", str(ROOT / "Build/runtime-regressions/ModuleOwnerCleanup")))


def block(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    opening = source.index("{", start)
    depth, i = 1, opening + 1
    while depth:
        c = source[i]
        if source.startswith("//", i):
            i = source.find("\n", i)
        elif source.startswith("/*", i):
            i = source.index("*/", i + 2) + 2
        elif c in "\"'":
            quote = c
            verbatim = i > 0 and source[i - 1] == "@"
            i += 1
            while True:
                if source[i] == quote:
                    if verbatim and source[i + 1:i + 2] == quote:
                        i += 2
                        continue
                    i += 1
                    break
                if source[i] == "\\" and not verbatim:
                    i += 2
                else:
                    i += 1
        else:
            depth += (c == "{") - (c == "}")
            i += 1
    return source[start:i]


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    evidence = {}
    def read(path):
        p = ROOT / path
        evidence[path] = hashlib.sha256(p.read_bytes()).hexdigest()
        return p.read_text(encoding="utf-8-sig")
    generated = []
    set_parts = []
    for file, fields, methods in (
        ("DragonSetBonus.cs", ("dragonSetActive", "dragonKingSetActive", "dragonSetEventRegistered", "dragonLevelEventRegistered", "dragonHurtEventRegistered", "cachedSlotChangedEventField", "slotChangedEventFieldCached", "cachedHeadTransform"),
         ("internal void RegisterDragonSetEvents()", "internal void UnregisterDragonSetEvents()", "private static FieldInfo GetCachedSlotChangedEventField()", "private static void ResetSetBonusReflectionCaches()", "private void RegisterDragonHurtEvent()", "private void UnregisterDragonHurtEvent()", "private void DeactivateDragonSetBonus()")),
        ("SetBonusManager.cs", ("setBonusEventRegistered", "setBonusLevelEventRegistered"),
         ("internal void RegisterSetBonusEvents()", "internal void UnregisterSetBonusEvents()")),
        ("FrostSetBonus.cs", ("frostSetActive", "frostSetHurtRegistered", "frostSetIceResistModifier", "frostSetIceResistStat", "frostSetEyeLights"),
         ("private void RegisterFrostSetHurtEvent()", "private void UnregisterFrostSetHurtEvent()", "private void DeactivateFrostSetBonus()")),
        ("ThunderSetBonus.cs", ("thunderSetActive", "thunderSetHurtRegistered", "thunderSetElecResistModifier", "thunderSetElecResistStat", "thunderSetEyeLights"),
         ("private void RegisterThunderSetHurtEvent()", "private void UnregisterThunderSetHurtEvent()", "private void DeactivateThunderSetBonus()")),
        ("DragonSetBonus_Dash.cs", ("isDragonDashing", "dragonDashCharacter", "isInChainDashWindow", "afterimages"),
         ("private void CancelDragonDash()", "private void ClearAfterimages()")),
    ):
        source = read("Integration/Bonus/" + file)
        for field in fields:
            matches = re.findall(r"^\s*private (?:static |readonly )*[^\n;]+\b" + re.escape(field) + r"\b[^\n;]*;", source, re.M)
            assert len(matches) == 1, (file, field, matches)
            set_parts.append(matches[0])
        set_parts += [block(source, signature) for signature in methods]
    generated.append("internal partial class SetBonusRuntimeModule : BossRushRuntimeModuleBase {\n" + "\n".join(set_parts) + "\n}")
    for npc in ("Goblin", "Nurse", "Courier"):
        path = "Integration/NPCs/" + npc + "/" + ("CourierNpcRuntimeModule.cs" if npc == "Courier" else npc + "NPC.cs")
        source = read(path)
        method = ("public" if npc == "Courier" else "internal") + " void Destroy" + npc + "NPC()"
        generated.append("internal sealed class " + npc + "NpcRuntimeModule : BossRushRuntimeModuleBase {\n" +
                         "private ModBehaviour owner; private bool destroyed; private GameObject " + npc.lower() + "NPCInstance; private " + npc + "NPCController " + npc.lower() + "Controller;\n" +
                         "\n".join(block(source, x) for x in ("public override void OnAwake(ModBehaviour owner)", "public override void OnDestroy()", method)) + "\n}")
    generated.append(block(read("Integration/Frostmourne/FrostmourneBootstrap.cs"), "internal sealed class FrostmourneRuntimeModule"))
    generated.append("internal sealed partial class ModeDRuntimeModule {" + block(read("ModeD/ModeDRuntimeModule_Waves.cs"), "internal void TickModeDIntegrity(float deltaTime)") + "}")
    host = read("Integration/IntegrationHostCompatibility.cs")
    bridge_methods = [block(host, signature) for signature in (
        "internal void RegisterArenaWaveBossFromContent", "internal void ClearArenaCurrentBossFromContent", "internal void RemoveArenaWaveBossFromContent", "internal bool HasArenaEnemyPresetCatalog",
        "internal EnemyPresetInfo FindArenaEnemyPreset", "internal void AddArenaEnemyPreset", "internal void RecordArenaBossLoot", "internal void RemoveArenaBossLootRecord", "internal int ArenaBossLootRecordCount", "internal void CopyArenaTrackedBossCharactersTo")]
    dhost = read("ModeD/ModeD.cs")
    bridge_methods += [block(dhost, signature) for signature in ("internal void ResetArenaIntegrityCheck()", "internal bool AdvanceArenaIntegrityCheck(float deltaTime)")]
    generated.append("public partial class ModBehaviour {" + "\n".join(bridge_methods) + "}")
    output = OUT / "Production.cs"
    output.write_text("using System; using System.Collections; using System.Collections.Generic; using System.Reflection; using UnityEngine; using UnityEngine.SceneManagement; using ItemStatsSystem.Stats; using ItemStatsSystem.Items; namespace BossRush {\n" + "\n".join(generated) + "\n}", encoding="utf-8")
    linked = [ROOT / p for p in ("Integration/Bonus/SetBonusRuntimeHostBridge.cs", "Integration/FlightTotem/FlightTotemBootstrap.cs", "Integration/ReverseScale/ReverseScaleBootstrap.cs", "Common/Equipment/AbilitySystemHelper.cs", "WavesArena/WavesArenaRuntimeModule_BossAccess.cs", "Utilities/ModBossPresetLookup.cs")]
    for p in linked:
        evidence[p.relative_to(ROOT).as_posix()] = hashlib.sha256(p.read_bytes()).hexdigest()
    sources = linked + [output, HERE / "Program.cs"]
    includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in sources)
    project = OUT / "Regression.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
    (OUT / "production-sha256.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
    return subprocess.call(["dotnet", "run", "--project", str(project), "--configuration", "Release"], cwd=ROOT)

if __name__ == "__main__":
    raise SystemExit(main())
