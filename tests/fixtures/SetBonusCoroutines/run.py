"""Link complete production spell coroutines and exact production death handlers."""
from pathlib import Path
import hashlib
import os
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build/set-bonus-coroutines'


def member(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    masked = re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"',
                    lambda m: ' ' * len(m.group()), source, flags=re.S)
    depth = 0
    for index in range(opening, len(source)):
        depth += (masked[index] == '{') - (masked[index] == '}')
        if depth == 0:
            return source[start:index + 1]
    raise ValueError(signature)


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    # Keep the lethal OnDead -> trailing OnHurt contract executable in the
    # linked fixture and fail fast if a future edit moves the dead guard below
    # any effectful operation in the four production consumers.
    for path, method_name in [
        (ROOT / 'Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs', 'private static void OnHurt('),
        (ROOT / 'Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs', 'private static void OnHurt('),
        (ROOT / 'Integration/Bonus/FrostSetBonus.cs', 'private void OnFrostSetHurt('),
        (ROOT / 'Integration/Bonus/ThunderSetBonus.cs', 'private void OnThunderSetHurt('),
    ]:
        source = path.read_text(encoding='utf-8-sig')
        body = member(source, method_name)
        guard = body.find('IsDead')
        if guard < 0:
            raise SystemExit('missing lethal IsDead guard: ' + str(path))
        first_effect = min((p for token in ('StartCoroutine', 'SetHealth', 'currentCharges++', 'TryApplyFrostFreeze')
                             for p in [body.find(token)] if p >= 0), default=len(body))
        if guard > first_effect:
            raise SystemExit('lethal guard occurs after effect: ' + str(path))
    (OUT / 'production-death-hurt-sources.sha256').write_text('\n'.join(
        hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + str(path)
        for path in [
            ROOT / 'Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs',
            ROOT / 'Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs',
            ROOT / 'Integration/Bonus/FrostSetBonus.cs',
            ROOT / 'Integration/Bonus/ThunderSetBonus.cs',
        ]), encoding='utf-8')
    methods = []
    for name in ('Frost', 'Thunder'):
        source = (ROOT / f'Integration/Bonus/{name}SetBonus.cs').read_text(encoding='utf-8-sig')
        methods.append(member(source, f'private void On{name}SetAnyDead('))
    visuals = (ROOT / 'Integration/Bonus/SetBonusVisuals.cs').read_text(encoding='utf-8-sig')
    methods.append(member(visuals, 'private void BumpSetBonusGeneration('))
    generated = 'using System; using System.Collections; using UnityEngine;\nnamespace BossRush { public partial class ModBehaviour {\n' + '\n'.join(methods) + '\n}}'
    (OUT / 'DeathHandlers.cs').write_text(generated, encoding='utf-8')
    weapon_methods = []
    for name, fields in (
        ('EnergyShield', 'private static float lastTriggerTime;'),
        ('ThunderRing', 'private static int currentCharges; private static float lastChargeTime; private static float lastChargeCooldownTime; private static int cachedEquipCheckFrame; private static CharacterMainControl cachedEquipCheckPlayer; private static bool cachedEquipCheckResult;'),
    ):
        source = (ROOT / f'Integration/NewWeapons/{name}/{name}Runtime.cs').read_text(encoding='utf-8-sig')
        body = member(source, 'private static void OnAnyDead(')
        reset = member(source, 'public static void ResetStaticCaches(')
        weapon_methods.append(
            f'namespace BossRush {{ internal static class {name}DeathProbe {{ {fields} '
            + reset + ' ' + body
            + ' internal static void SetState(){'
            + ('lastTriggerTime = 42f;' if name == 'EnergyShield' else 'currentCharges = 3; lastChargeTime = 42f; lastChargeCooldownTime = 42f;')
            + '} internal static bool IsReset(){'
            + ('return lastTriggerTime == 0f;' if name == 'EnergyShield' else 'return currentCharges == 0 && lastChargeTime == 0f && lastChargeCooldownTime == 0f;')
            + '} internal static void InvokeDead(Health target){ OnAnyDead(target, new DamageInfo()); } }}'
        )
    (OUT / 'WeaponDeathHandlers.cs').write_text('using System; using UnityEngine;\n' + '\n'.join(weapon_methods), encoding='utf-8')
    (OUT / 'source.sha256').write_text(hashlib.sha256(generated.encode('utf-8')).hexdigest(), encoding='utf-8')
    raise SystemExit(subprocess.call(['dotnet', 'run', '--project', str(HERE / 'SetBonusCoroutines.csproj'),
                                     '--configuration', 'Release', '--verbosity', 'quiet'], cwd=ROOT,
                                    env=dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US')))
