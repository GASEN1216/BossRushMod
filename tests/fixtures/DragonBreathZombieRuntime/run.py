"""Run production Dragon Breath repair and verbatim official gun/projectile/health bodies."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Build/runtime-regressions/DragonBreathZombieRuntime"
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    hashes = {}

    def read(path):
        data = (ROOT / path).read_bytes()
        hashes[path] = hashlib.sha256(data).hexdigest()
        return data.decode("utf-8-sig")

    def member(path, signature):
        source = clean_source(read(path))
        assert source.count(signature) == 1, signature
        start = source.index(signature)
        opening = source.index("{", start)
        depth = 0
        for index in range(opening, len(source)):
            depth += (source[index] == "{") - (source[index] == "}")
            if depth == 0:
                tail = index + 1
                if source[tail:tail + 1] == ";":
                    tail += 1
                return source[start:tail]
        raise AssertionError(signature)

    gun = "Integration/DragonDescendant/DragonBreathWeaponConfig.cs"
    helper = "Integration/Reforge/ReforgeDataPersistence.cs"
    cleanup = "Integration/Reforge/ReforgeDataPersistenceCleanup.cs"
    hooks = "Integration/BossRushIntegrationRuntimeModule_RuntimeHooks.cs"
    init = "Integration/BossRushIntegrationRuntimeModule_Initialization.cs"
    official = "鸭科夫源码/TeamSoda.Duckov.Core/"
    code = ["using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Reflection; using Duckov.Buffs; using ItemStatsSystem; using ItemStatsSystem.Stats; using UnityEngine; using Object = UnityEngine.Object; using Random = UnityEngine.Random; namespace BossRush { static partial class DragonBreathWeaponConfig {"]
    for signature in ("private static readonly HashSet<string> DISPLAY_STATS", "private static readonly Dictionary<string, float> WEAPON_STATS", "public static void ConfigureWeapon(", "internal static bool NeedsRuntimeConfig(", "internal static void RestoreRuntimeConfig(", "private static void EnsureZombieDamageFactor(", "private static void InvalidateStatsDictionary(", "private static void SetWeaponStats(", "private static void SetStatBaseValue(", "private static void SetStatDisplay(", "private static void CacheStatFields("):
        code.append(member(gun, signature))
    code += ["} static partial class CustomItemRuntimeStateHelper {", member(helper, "private static readonly string[] CriticalGunStatKeys"), member(helper, "private sealed class RuntimeConfigEntry"), "private static readonly Dictionary<int, RuntimeConfigEntry> runtimeConfigEntries = new Dictionary<int, RuntimeConfigEntry>();"]
    for signature in ("public static void RegisterRuntimeConfiguredItem(", "public static void RegisterGunRuntimeConfiguredItem(", "public static bool IsRuntimeConfiguredType(", "public static bool EnsureCustomItemConfigured(", "public static bool RestoreRuntimeState(", "public static bool NeedsGunRuntimeConfig("):
        code.append(member(helper, signature))
    code += ["} static partial class ReforgeDataPersistence {", member(helper, "private static readonly HashSet<int> _restoredItems")]
    for signature in ("public static void MarkAsRestored(", "public static void UnmarkRestored(", "public static bool HasReforgeData(", "public static bool TryRestoreReforgeData(", "private static bool TryRestoreStat("):
        code.append(member(helper, signature))
    for signature in ("private static Item GetItemPrefab(", "private static float GetPrefabStatValue("):
        code.append(member(cleanup, signature))
    code += ["} partial class IntegrationRuntimeModule {", member(init, "internal void RegisterCustomWeaponRuntimeConfigs(")]
    for signature in ("private static int RestoreRuntimeStateForHoldAgent(", "private void OnPlayerHoldAgentChanged(", "internal void SubscribeDragonBreathEffectEvent(", "internal void UnsubscribeDragonBreathEffectEvent("):
        code.append(member(hooks, signature))
    code += ["} partial class ModBehaviour {", member("Integration/BossRushIntegration.cs", "private void RegisterCustomWeaponRuntimeConfigs("), "}", member(helper, "public static class ItemInitializeRestorePatch"), "}"]
    code += ["namespace ItemStatsSystem { partial class Item {", member("鸭科夫源码/ItemStatsSystem/Item.cs", "public float GetStatValue(int hash)"), "}}"]
    code += ["public partial class ItemAgent_Gun : DuckovItemAgent {", member(official + "ItemAgent_Gun.cs", "public float DamageFactorToZombie"), member(official + "ItemAgent_Gun.cs", "private void ShootOneBullet("), "}"]
    code += ["public partial class Projectile : MonoBehaviour {", member(official + "Projectile.cs", "private void UpdateMoveAndCheck("), member(official + "Projectile.cs", "private void AddElementsToDamageInfo("), "}"]
    code += ["public partial class DamageReceiver : MonoBehaviour {", member(official + "DamageReceiver.cs", "public bool Hurt(DamageInfo damageInfo)"), "}"]
    code += ["public partial class Health : MonoBehaviour {", member(official + "Health.cs", "public bool Hurt(DamageInfo damageInfo)"), "}"]
    damage = read(official + "DamageInfo.cs")
    fields = damage[damage.index("public DamageTypes damageType;"):damage.rfind("}")]
    fields = re.sub(r"\[(SerializeField|HideInInspector|ItemTypeID)\]\s*", "", fields)
    code += ["public struct DamageInfo {", fields, member(official + "DamageInfo.cs", "public DamageInfo(CharacterMainControl fromCharacter = null)"), member(official + "DamageInfo.cs", "public void AddElementFactor("), "}"]
    generated = OUT / "Production.cs"
    generated.write_text("\n".join(code), encoding="utf-8")
    hashes["Integration/DragonDescendant/DragonBreathConfig.cs"] = hashlib.sha256((ROOT / "Integration/DragonDescendant/DragonBreathConfig.cs").read_bytes()).hexdigest()
    files = [generated, HERE / "Stubs.cs", HERE / "Program.cs", ROOT / "Integration/DragonDescendant/DragonBreathConfig.cs", ROOT / "鸭科夫源码/ItemStatsSystem/StatCollection.cs", *[ROOT / (official + name) for name in ("ProjectileContext.cs", "ElementTypes.cs", "ElementFactor.cs", "DamageTypes.cs")]]
    from xml.sax.saxutils import escape
    project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>'
    project += "".join('<Compile Include="' + escape(str(path), {'"': '&quot;'}) + '" />' for path in files)
    project += "</ItemGroup></Project>"
    path = OUT / "Regression.csproj"
    path.write_text(project, encoding="utf-8")
    (OUT / "source-hashes.json").write_text(json.dumps(hashes, ensure_ascii=False, indent=2), encoding="utf-8")
    status, output = run_project_fixture(path, OUT, ROOT)
    print(output)
    return status


if __name__ == "__main__":
    raise SystemExit(main())
