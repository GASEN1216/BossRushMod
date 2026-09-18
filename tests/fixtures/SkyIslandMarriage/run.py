"""逐字运行任务给予者接管、离婚回收与婚后剧情交互；不读取玩家存档。"""
from pathlib import Path
import hashlib
import json
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Build/runtime-regressions/SkyIslandMarriage'

def member(source, signature):
    hits = list(re.finditer(r'^([ \t]*)' + re.escape(signature), source, re.M))
    assert len(hits) == 1, signature
    hit = hits[0]
    opening = source.index('{', hit.end())
    end = re.search(r'^' + re.escape(hit.group(1)) + r'\}', source[opening:], re.M)
    assert end, signature
    return source[hit.start():opening + end.end()]

paths = ['DebugAndTools/SkyIsland/SkyIslandOfficialQuestGivers.cs',
         'Integration/Wedding/WeddingModBehaviourBridge.cs',
         'Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs',
         'Integration/Wedding/NPCMarriageSystem.cs',
         'DebugAndTools/SkyIsland/SkyIslandResidentDialogue.cs',
         'Integration/Dialogue/DialogueActorFactory.cs']
givers, bridge, permanent, marriage, dialogue, factory = [(ROOT / path).read_text(encoding='utf-8-sig') for path in paths]
fields = []
for name in ('FallbackAttemptLimit', 'attached', 'fallbackDone', 'fallbackAttempts'):
    found = re.findall(r'^        private [^\n]*\b' + name + r'\b[^\n]*;', givers, re.M)
    assert len(found) == 1, name
    fields.extend(found)
methods = [member(givers, signature) for signature in
           ('internal static void AttachResident(', 'internal static void EnsureDeviceFallback(', 'private static bool HasAttachedGiver(',
            'internal static void ClearSessionState()')]
source = 'using System; using System.Collections.Generic; using System.Threading.Tasks; using UnityEngine; using BossRush.Utils; using NodeCanvas.DialogueTrees;\nnamespace BossRush {\n'
source += 'internal static partial class SkyIslandOfficialQuestGivers {\n' + '\n'.join(fields + methods) + '\n}\n'
source += 'public partial class ModBehaviour {\n' + member(bridge, 'public void HandleDivorceNpcRelocation(') + '\n' + member(bridge, 'internal GameObject GetSpouseInstance(') + '\n}\n'
source += 'internal static class PermanentDuckNpcModule {\n' + member(permanent, 'internal static void ReleaseDivorcedNpc(') + '\n}\n'
marriage_fields = []
for name in ('DIVORCE_RELOCATE_DELAY_SECONDS', 'operationGeneration'):
    found = re.findall(r'^        private [^\n]*\b' + name + r'\b[^\n]*;', marriage, re.M)
    assert len(found) == 1, name
    marriage_fields.extend(found)
marriage_methods = [member(marriage, signature) for signature in (
    'private sealed class MarriageOperation', 'private static MarriageOperation BeginOperation(',
    'public static void HandleRingGiftAccepted(', 'public static void HandleDivorceRequested(',
    'private static async UniTaskVoid RunMarriageSequenceAsync(',
    'private static async UniTaskVoid RunDivorceFinalizeAsync(',
    'private static void RelocateOrDespawnMarriedNpc(', 'private static void ShowMarriageFeedback(',
    'private static IDialogueActor ResolveDialogueActor(', 'private static string[][] BuildMarriageCutsceneDialogues(')]
# 仅异步返回类型适配；入口、捕获时点、每个 await 及所有收尾/反馈逻辑逐字运行。
source += 'public static partial class NPCMarriageSystem {\n' + '\n'.join(marriage_fields + marriage_methods).replace('async UniTaskVoid', 'async Task') + '\n}\n'
source += 'internal static partial class ActorReuseRegression {\n' + member(dialogue, 'private static IDialogueActor EnsureActor(') + '\n}\n}\n'
OUT.mkdir(parents=True, exist_ok=True)
generated = OUT / 'Production.cs'
generated.write_text(source, encoding='utf-8')
(OUT / 'sources.json').write_text(json.dumps({p: hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in paths}, indent=2), encoding='utf-8')
result = subprocess.run(['dotnet', 'build', str(HERE/'Regression.csproj'), '-c', 'Release', '--output', str(OUT/'bin'),
    '-p:BaseIntermediateOutputPath='+str(OUT/'obj')+'/', '-p:MarriageGeneratedSource='+str(generated)], cwd=ROOT)
if result.returncode:
    raise SystemExit(result.returncode)
raise SystemExit(subprocess.run(['dotnet', str(OUT/'bin/Regression.dll')], cwd=ROOT).returncode)
