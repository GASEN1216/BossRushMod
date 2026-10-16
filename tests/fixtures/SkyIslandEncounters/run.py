"""Run the production encounter owner against controlled Unity/async substitutes."""
from pathlib import Path
import hashlib
import json
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build' / 'runtime-regressions' / 'SkyIslandEncounters'
OUT.mkdir(parents=True, exist_ok=True)

def member(source, signature):
    hits = list(re.finditer(r'^([ \t]*)' + re.escape(signature), source, re.M))
    assert len(hits) == 1, signature
    hit = hits[0]
    opening = source.index('{', hit.end())
    end = re.search(r'^' + re.escape(hit.group(1)) + r'\}', source[opening:], re.M)
    assert end, signature
    return source[hit.start():opening + end.end()]

# BeginChallenge 的联机分支也使用真实 partial，不能用恒真开战替身补编译。
coop_path = ROOT / 'SkyIsland/SkyIslandSessionCoop.cs'
coop = coop_path.read_text(encoding='utf-8-sig')
(OUT / 'ChallengeOwner.Extracted.cs').write_text(
    'using System; using UnityEngine;\nnamespace BossRush { internal sealed partial class SkyIslandEncounters {\n'
    + '\n'.join(member(coop, signature) for signature in (
        'internal bool TryGetChallengeOwner(', 'internal bool BeginRemoteChallenge('))
    + '\n} }\n', encoding='utf-8')
(OUT / 'sources.json').write_text(json.dumps({str(coop_path.relative_to(ROOT)):
    hashlib.sha256(coop_path.read_bytes()).hexdigest()}, indent=2), encoding='utf-8')
# 原样执行正式序章 SpawnBoss；只替换 Unity 边界，不复制数值或生成算法。
prelude = (ROOT / 'SkyIsland/SkyIslandPreludeFlow.cs').read_text(encoding='utf-8-sig')
signature = 'private async void SpawnBoss(Vector3 position, int expectedGeneration)'
member = signature + prelude.split(signature, 1)[1].split('private void OnBossDead(bool instrumentInBox)', 1)[0]
gate = 'private bool ShouldRunObjective()' + prelude.split('private bool ShouldRunObjective()', 1)[1].split('private void EnsureJeff()', 1)[0]
scene_constant = 'private const string GroundZeroScene' + prelude.split('private const string GroundZeroScene', 1)[1].split(';', 1)[0] + ';'
(OUT / 'PreludeSpawn.Extracted.cs').write_text(
    'using System;\nusing UnityEngine;\nusing UnityEngine.SceneManagement;\nusing Duckov.Utilities;\n'
    'namespace BossRush { internal sealed partial class SkyIslandPreludeFlow {\n'
    + scene_constant + '\n' + gate + member + '\n} }\n', encoding='utf-8')
build = subprocess.run([
    'dotnet', 'build', str(HERE / 'Regression.csproj'), '--configuration', 'Release',
    '--output', str(OUT / 'bin'),
    '-p:BaseIntermediateOutputPath=' + str(OUT / 'obj') + '/',
], cwd=ROOT)
if build.returncode:
    raise SystemExit(build.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT / 'bin' / 'Regression.dll')], cwd=ROOT).returncode)
