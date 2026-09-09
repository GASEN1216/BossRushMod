"""天空岛居民的稳定关系身份、显式生成 owner 与迟到清理边界。"""
from pathlib import Path
import json
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / 'Assets/Data/DuckNpcs.json').read_text(encoding='utf-8-sig'))
NPCS = {row['id']: row for row in DATA['npcs']}
SOURCE = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandResidents.cs').read_text(encoding='utf-8-sig'))
INTERACT = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandResidentInteractable.cs').read_text(encoding='utf-8-sig'))
SESSION = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8-sig'))
errors = []

for npc_id in ['sky_qinghe', 'sky_weibai', 'sky_fuzhou', 'sky_miantai', 'sky_zheling', 'sky_bellkeeper']:
    row = NPCS.get(npc_id)
    if row is None:
        errors.append('缺少居民蓝图 ' + npc_id)
        continue
    if row.get('scenes') != ['SkyIslandRaid']:
        errors.append(npc_id + ' 必须明确归属真实天空岛资源 Scene')
    if row.get('faceMode') != 'json' or not json.loads(row.get('faceJson', '{}')).get('savedSetting'):
        errors.append(npc_id + ' 缺少固化外观')
    if not row.get('invincible') or row.get('team') != 'player':
        errors.append(npc_id + ' 剧情实例必须友好且无敌，战斗实例单独生成')
    if npc_id in ('sky_qinghe', 'sky_weibai'):
        if not row.get('isPermanent') or not row.get('permanent', {}).get('marriedDialogues'):
            errors.append(npc_id + ' 必须接永久关系与既有婚姻')

for token in ['await DuckNpcSpawner.SpawnAsync', 'PermanentDuckNpcModule.AttachPermanentParts',
              'PermanentDuckNpcRegistry.RegisterInstance', 'AffinityManager.IsMarriedToPlayer',
              'PermanentDuckNpcRegistry.GetInstance(id) != null', 'if (!retained)',
              'PermanentDuckNpcRegistry.GetInstance(id) == npc', 'DuckNpcSpawner.Despawn(npc)',
              'seeker.graphMask = navigation.Mask', 'NPCInteractionGroupHelper.AddSubInteractable',
              'child.transform.SetParent(npc.transform, false)', 'Physics.IgnoreCollision',
              'disposed = true', 'npc.characterModel.SetFaceFromData(face)']:
    if token not in SOURCE:
        errors.append('居民 owner 缺少契约：' + token)

if 'callback(npcId, speaker)' not in INTERACT or 'valid()' not in INTERACT:
    errors.append('剧情交互必须检查会话 owner 后调用故事入口')
if 'new SkyIslandResidents' not in SESSION or '.Dispose()' not in SESSION:
    errors.append('天空岛会话未接居民创建/清理')
bridge = clean_source((ROOT / 'DebugAndTools/SkyIsland/SkyIslandSceneReferenceBridge.cs').read_text(encoding='utf-8-sig'))
def constant(source, name):
    match = re.search(r'const\s+string\s+' + name + r'\s*=\s*"([^"\r\n]+)"\s*;', source)
    return match.group(1) if match else None
scene_name = constant(bridge, 'SceneName')
scene_path = constant(bridge, 'ScenePath')
if scene_name != 'SkyIslandRaid' or scene_path != 'Assets/SkyIsland/SkyIslandRaid.unity':
    errors.append('居民 SceneName 必须与独立场景桥接的真实 ScenePath/SceneName 一致')
# 场景名的唯一事实源是桥接常量；通用 NPC 刷新的三处排除必须逐字用同一个名字，
# 但不能让 Integration 反向依赖 DebugAndTools，因此用守卫做跨文件比对。
for module in ('Integration/NPCs/DuckNpc/DuckNpcModule.cs', 'Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs'):
    text = clean_source((ROOT / module).read_text(encoding='utf-8-sig'))
    if f'string.Equals(sceneName, "{scene_name}", StringComparison.Ordinal)' not in text:
        errors.append('通用 NPC 刷新的排除场景名与桥接常量不一致：' + module)
if 'ResidentSceneName' in SOURCE:
    errors.append('居民 owner 不再自持场景名常量，避免出现第二个事实源')

if errors:
    for error in errors:
        print('SkyIslandResidentsGuard: FAIL ' + error)
    raise SystemExit(1)
print('SkyIslandResidentsGuard: PASS')
