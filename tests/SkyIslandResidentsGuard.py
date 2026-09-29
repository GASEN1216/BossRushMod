"""天空岛居民的稳定关系身份、显式生成 owner 与迟到清理边界。"""
from pathlib import Path
import json
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / 'Assets/Data/DuckNpcs.json').read_text(encoding='utf-8-sig'))
NPCS = {row['id']: row for row in DATA['npcs']}
SOURCE = clean_source((ROOT / 'SkyIsland/SkyIslandResidents.cs').read_text(encoding='utf-8-sig'))
INTERACT = clean_source((ROOT / 'SkyIsland/SkyIslandResidentInteractable.cs').read_text(encoding='utf-8-sig'))
# 会话每帧子系统 2026-09-29 提取到同一 partial 的 SkyIslandSessionTick.cs（发版审查 A-01），断言照旧针对整个类。
SESSION = clean_source((ROOT / 'SkyIsland/SkyIslandSession.cs').read_text(encoding='utf-8-sig') + '\n'
                       + (ROOT / 'SkyIsland/SkyIslandSessionTick.cs').read_text(encoding='utf-8-sig'))
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
    permanent = row.get('permanent', {})
    if not row.get('isPermanent') or not permanent.get('marriedDialogues'):
        errors.append(npc_id + ' 必须接永久关系与既有婚姻')
    if not permanent.get('positiveTags') and not permanent.get('positiveItemTypeIds'):
        errors.append(npc_id + ' 必须有玩家可使用的礼物偏好')
    if permanent.get('dailyChatAffinity', 0) != 30:
        errors.append(npc_id + ' 必须沿用每日聊天 +30 的共享规则')

for token in ['await DuckNpcSpawner.SpawnAsync', 'PermanentDuckNpcModule.AttachPermanentParts',
              'PermanentDuckNpcRegistry.RegisterInstance', 'AffinityManager.IsMarriedToPlayer',
              'PermanentDuckNpcRegistry.GetInstance(id) != null', 'if (!retained)',
              'PermanentDuckNpcRegistry.GetInstance(id) == npc', 'DuckNpcSpawner.Despawn(npc)',
              'seeker.graphMask = navigation.Mask', 'SkyIslandResidentInteractable.AttachPermanent(npc, id)',
              'child.transform.SetParent(npc.transform, false)', 'Physics.IgnoreCollision',
              'disposed = true', 'npc.characterModel.SetFaceFromData(face)']:
    if token not in SOURCE:
        errors.append('居民 owner 缺少契约：' + token)

if 'callback(npcId, speaker)' not in INTERACT or 'valid()' not in INTERACT:
    errors.append('剧情交互必须检查会话 owner 后调用故事入口')
for token in ['NPCInteractionGroupHelper.AddSubInteractable(owner.transform, "IslandStoryOption", group,',
              'if (npc == null || SkyIslandResidents.MarkerOf(id) == null) return;',
              'component.TalkPermanent, component.CanTalkPermanent', 'session.TalkToResident(id, target)',
              'story.DescribeNpc(id, true, false)', 'homeDialogue.Dispose()',
              'story.IsCurrentSlot', 'SceneLoader.IsSceneLoading']:
    if token not in INTERACT:
        errors.append('婚后剧情交互缺少绑定或生命周期门：' + token)
module = clean_source((ROOT / 'Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcModule.cs').read_text(encoding='utf-8-sig'))
if 'SkyIslandResidentInteractable.AttachPermanent(npc, blueprint.id);' not in module:
    errors.append('永久 NPC 装配（含婚后恢复）没有接回航路剧情')
if 'new SkyIslandResidents' not in SESSION or '.Dispose()' not in SESSION:
    errors.append('天空岛会话未接居民创建/清理')
encounters = clean_source((ROOT / 'SkyIsland/SkyIslandEncounters.cs').read_text(encoding='utf-8-sig'))
raid_started = re.search(r'internal bool WasStartedThisRaid\(string id\)\s*\{([^}]+)\}', encounters)
if raid_started is None or not re.fullmatch(
        r'\s*Encounter encounter = Find\(id\);\s*return encounter != null && encounter.Started;\s*',
        raid_started.group(1)):
    errors.append('折翎关系恢复必须区分本趟 Started 与旧档投影的 Cleared')
if 'residents.SetVisible("sky_zheling", encounters == null || !encounters.WasStartedThisRaid("Zheling"));' not in SESSION:
    errors.append('折翎只在本趟挑战后休整，不得按持久战败位永久隐藏')
bridge = clean_source((ROOT / 'SkyIsland/SkyIslandSceneReferenceBridge.cs').read_text(encoding='utf-8-sig'))
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
