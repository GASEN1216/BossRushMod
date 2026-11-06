"""Run the actual hazard owner and both health prefixes on official Hurt / CurrentHealth under installed Harmony."""
import hashlib
import importlib.util
import os
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DEATH = HERE.parent / 'DeathBroadcastIsolation'


def replace_once(text, old, new):
    assert text.count(old) == 1, old
    return text.replace(old, new, 1)


def main():
    if os.name != 'nt':
        raise SystemExit('Requires Windows .NET Framework and the installed game Harmony.')
    harmony = Path(os.environ.get('BOSSRUSH_HARMONY_DLL',
        r'D:\software\steam\steamapps\workshop\content\3167020\3588386576\0Harmony.dll'))
    if not harmony.is_file():
        raise SystemExit('Set BOSSRUSH_HARMONY_DLL to the installed 0Harmony.dll')
    base = ROOT / 'Build/runtime-regressions/TidegateHazardFatality'
    base.mkdir(parents=True, exist_ok=True)
    out = Path(tempfile.mkdtemp(prefix='run-', dir=base))
    spec = importlib.util.spec_from_file_location('death_broadcast_helpers', DEATH / 'run.py')
    shared = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(shared)
    member = shared.member
    official = ROOT / '鸭科夫源码/TeamSoda.Duckov.Core'
    health = (official / 'Health.cs').read_text(encoding='utf-8-sig')
    mainchar = (official / 'CharacterMainControl.cs').read_text(encoding='utf-8-sig')
    damage = (official / 'DamageInfo.cs').read_text(encoding='utf-8-sig')
    fields = re.sub(r'\[(SerializeField|HideInInspector|ItemTypeID)\]\s*', '',
                    damage[damage.index('public DamageTypes damageType;'):damage.rfind('}')])
    noinline = '[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n'
    setter = replace_once(member(health,'public float CurrentHealth'), '\n\t\tset\n', '\n'+noinline+'set\n')
    body = ('using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.Events;'
            'using Random = UnityEngine.Random;\npublic partial class Health {\n' +
            noinline + member(health, 'public bool Hurt(DamageInfo damageInfo)') + '\n' + setter + '\n' +
            member(health, 'public bool Invincible') + '\n' + member(health, 'public void SetInvincible(bool value)') + '\n}\n' +
            'public partial class CharacterMainControl {\n' + member(mainchar, 'private void OnDead(DamageInfo dmgInfo)') +
            '\n}\npublic struct DamageInfo {\n'+fields+member(damage,'public DamageInfo(CharacterMainControl fromCharacter = null)')+
            member(damage,'public void AddElementFactor(ElementTypes _type, float _factor)')+'\n'+
            noinline+member(damage,'public string GenerateDescription()')+'}\n')
    (out/'Official.cs').write_text(body, encoding='utf-8')
    # 同一个宿主替身，换成生产 setter 与可调整的装备属性；其余伤害流程仍逐字运行官方方法。
    stub = (DEATH / 'Stubs.cs').read_text(encoding='utf-8-sig')
    stub = replace_once(stub,'public float CurrentHealth { get; set; }',
                        'private float _currentHealth; public UnityEvent<Health> OnHealthChange = new UnityEvent<Health>();')
    stub = replace_once(stub,'public float ElementFactor(ElementTypes type) { return 1f; }',
                        'public float ResistanceFactor=1f; public float ElementFactor(ElementTypes type) { return ResistanceFactor; }')
    stub = replace_once(stub,'public static void LogWarning(object message)',
                        'public static void Log(object message) {} public static void LogWarning(object message)')
    stub = replace_once(stub, 'public float aiCombatFactor = 1f;', 'public string DisplayName=""; public float aiCombatFactor = 1f;')
    (out/'Host.cs').write_text(stub, encoding='utf-8')
    session = ROOT / 'Tidegate/TidegateSession.cs'
    update = member(session.read_text(encoding='utf-8-sig'),'private void Update()')
    gates = update[update.index('{')+1:update.index('if (player == null')]
    hit = re.search(r'try \{ if \(hazards != null && hazards\.Tick\(.+?\)\) return; \}\s+catch \(Exception e\) \{ TickFault\("hazards", e\); \}', update)
    assert hit, 'session hazard dispatch missing'
    (out/'SessionGate.cs').write_text('using System; using UnityEngine; namespace BossRush { partial class SessionGate { public void Step() {'
                                    +gates+hit.group()+'} } }', encoding='utf-8')
    paths = [ROOT/'Tidegate/TidegateHazards.cs', ROOT/'Tidegate/TidegateFrameProbe.cs', ROOT/'Patches/Combat/BossLethalHealthProtectionPatch.cs',
             official/'ElementFactor.cs',official/'ElementTypes.cs',official/'DamageTypes.cs',
             out/'Host.cs', out/'Official.cs', out/'SessionGate.cs', HERE/'Program.cs']
    shutil.copy2(harmony, out/'0Harmony.dll')
    tracked = [session, official/'Health.cs', official/'CharacterMainControl.cs', official/'DamageInfo.cs', harmony, DEATH/'Stubs.cs'] + paths
    (out/'sources.sha256').write_text('\n'.join(hashlib.sha256(x.read_bytes()).hexdigest()+' '+str(x) for x in tracked), encoding='utf-8')
    sdk = subprocess.check_output(['dotnet','--list-sdks'],text=True).strip().splitlines()[-1]
    compiler = Path(sdk[sdk.index('[')+1:sdk.index(']')])/sdk.split()[0]/'Roslyn/bincore/csc.dll'
    framework = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319'
    exe = out/'Hazard.exe'
    args = ['/nologo','/target:exe','/define:BOSSRUSH_DEV','/langversion:7.3','/nostdlib+', '/out:"'+str(exe)+'"', '/r:"'+str(harmony)+'"']
    args += ['/r:"'+str(framework/p)+'"' for p in ['mscorlib.dll','System.dll','System.Core.dll']]
    args += ['"'+str(p)+'"' for p in paths]
    response=out/'compile.rsp'
    response.write_text('\n'.join(args), encoding='utf-8-sig')
    code = subprocess.call(['dotnet',str(compiler),'/noconfig','@'+str(response)], cwd=ROOT)
    return code or subprocess.call([str(exe)], cwd=out)


if __name__ == '__main__':
    raise SystemExit(main())
