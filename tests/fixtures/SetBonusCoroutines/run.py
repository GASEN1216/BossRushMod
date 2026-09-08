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
    methods = []
    for name in ('Frost', 'Thunder'):
        source = (ROOT / f'Integration/Bonus/{name}SetBonus.cs').read_text(encoding='utf-8-sig')
        methods.append(member(source, f'private void On{name}SetAnyDead('))
    visuals = (ROOT / 'Integration/Bonus/SetBonusVisuals.cs').read_text(encoding='utf-8-sig')
    methods.append(member(visuals, 'private void BumpSetBonusGeneration('))
    generated = 'namespace BossRush { public partial class ModBehaviour {\n' + '\n'.join(methods) + '\n}}'
    (OUT / 'DeathHandlers.cs').write_text(generated, encoding='utf-8')
    (OUT / 'source.sha256').write_text(hashlib.sha256(generated.encode('utf-8')).hexdigest(), encoding='utf-8')
    raise SystemExit(subprocess.call(['dotnet', 'run', '--project', str(HERE / 'SetBonusCoroutines.csproj'),
                                     '--configuration', 'Release', '--verbosity', 'quiet'], cwd=ROOT,
                                    env=dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US')))
