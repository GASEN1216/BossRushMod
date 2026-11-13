#!/usr/bin/env python3
"""末班编组站接入的结构不变式（身份、入口、装配、互斥、部署；第三期起加剧情存档）。

钉的东西：
- 场景身份只经共用桥 `ModRaidSceneBridge` 登记，不另装官方 SceneLoader 补丁；进图走 `ModRaidLease`。
- 入口只在一趟没结束的官方出击里开（`RaidUtilities.CurrentRaid.ended`），上车前必经官方对话选项，确认后才进会话（「本趟不结算」一行 owner 2026-11-11 要求去掉）。
- 入口模型只从 `lastshift_entry` 小包来；着色器不降级（世界与入口都在 `isSupported` 不过时抛错），白名单与判包脚本一致。
- 和潮闸镇、天空岛、试验场、完整 F3 验收互相拒绝。
- 室内屋顶认 `LastShiftIndoorRooms`；部署段与发布清单登记两个包；运行时模块已注册。
- 剧情（第三期）：存档键与图鉴前缀不改；本趟进度只在安全撤离时写进存档（阵亡丢本趟线索）；
  交票根先写存档再拿走票根；交票只发生在上车确认之后，或和余响说话时按了「把票根给她」之后。
- 余响在官方中立阵营 `middle`（仓库区的原版敌人不会朝她开火），装扮写死在蓝图里。
- 杰夫的引导是官方任务 590216、发在杰夫身上；和余响对话与剪票上车都记「见过余响」。
- 不在运行时扫官方物品表（owner 2026-11-11：官方物品编号查官方 Wiki 写死）。
判断前去掉 C# 注释，免得注释里的同名字串骗过子串检查。
"""
import json
import re
import sys
from pathlib import Path
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
errors = []


def code(rel):
    return clean_source((ROOT / rel).read_text(encoding='utf-8-sig'))


def need(rel, text, token, why):
    if token not in text:
        errors.append(f'{rel}: {why}（缺 `{token}`）')


folder = ROOT / 'LastShiftYard'
files = {p.name: code('LastShiftYard/' + p.name) for p in sorted(folder.glob('*.cs'))}
everything = '\n'.join(files.values())

if re.search(r'HarmonyPatch\s*\(\s*typeof\s*\(\s*SceneLoader', everything):
    errors.append('LastShiftYard: 官方 SceneLoader 转译补丁全仓只能装一份，本图只登记身份')
module = files.get('LastShiftRuntimeModule.cs', '')
need('LastShiftRuntimeModule.cs', module, 'ModRaidSceneBridge.Register(LastShiftRaidScene.Identity)', '启动时登记场景身份')
need('LastShiftRuntimeModule.cs', module, 'ModRaidSceneBridge.Unregister(LastShiftRaidScene.Identity)', '销毁时注销场景身份')
for event, handler in (
    ('SceneLoader.onStartedLoadingScene', 'OnStartedLoading'),
    ('SceneManager.sceneLoaded', 'OnSceneLoaded'),
    ('SceneManager.sceneUnloaded', 'OnSceneUnloaded'),
    ('LocalizationManager.OnSetLanguage', 'OnLanguageChanged'),
):
    need('LastShiftRuntimeModule.cs', module, f'{event} += {handler}', '入口监听官方场景生命周期')
    need('LastShiftRuntimeModule.cs', module, f'{event} -= {handler}', '销毁路径退订')

session = files.get('LastShiftSession.cs', '')
need('LastShiftSession.cs', session, 'new ModRaidLease(profile)', '进图走共用租约')
need('LastShiftSession.cs', session, 'lease.BeginLoad()', '进图走共用租约')
can_enter = re.search(r'static bool CanEnter\(.*?\n        \}', session, re.S)
if not can_enter or not re.search(r'RaidUtilities\.CurrentRaid\.ended\s*\)', can_enter.group(0)):
    errors.append('LastShiftSession.cs: CanEnter 必须拒绝已结束的出击（只在一趟没结束的官方出击里上车，继续同一趟、不新开）')
need('LastShiftSession.cs', session, 'ModRaidOfficialContract.Verify(profile, entryScene, out contractError)', '官方初始化后验合同')
for other in ('TidegateSession', 'SkyIslandSession', 'ArenaPrototypeSession'):
    need('LastShiftSession.cs', session, 'GetComponent<' + other + '>() != null', '与 ' + other + ' 互斥')

entry = files.get('LastShiftEntry.cs', '')
yuxiang = files.get('LastShiftYuXiang.cs', '')
# 上车链：交互 → 余响官方对话选择 → 剪票动画 → 官方入图。时序与副作用由 LastShiftEntryRuntime 执行覆盖。
def body(text, signature):
    m = re.search(re.escape(signature) + r'.*?\n        \}', text, re.S)
    return m.group(0) if m else ''


completed = body(entry, 'protected override void OnInteractCompleted()')
board = body(yuxiang, 'private async UniTask Board(')
converse = body(yuxiang, 'private async UniTask Converse(')
depart = body(entry, 'internal void Depart(')
after_punch = body(entry, 'private System.Collections.IEnumerator EnterAfterPunch(')
need('LastShiftEntry.cs', completed, 'yuXiang.Talk(this, true);', '剪票交互接入余响的官方对话')
need('LastShiftYuXiang.cs', board, 'request.Choose(LastShiftText.YuXiangBoardingChoices', '登车须经过官方对话的上车选择')
need('LastShiftYuXiang.cs', converse, 'request.Choose(LastShiftText.YuXiangHandInChoices', '单独交票须经过官方对话的交付选择')
for source in ('LastShiftEntry.cs', 'LastShiftYuXiang.cs', 'LastShiftStory.cs'):
    if 'BossRushConfirmDialog' in files.get(source, ''):
        errors.append(source + ': 余响剧情、交票和登车使用官方对话组件')
for token in ('DialogueActorFactory.Create(', 'DialogueManager.ShowDialogueSequenceBilingual(', 'DialogueManager.ShowMultipleChoiceBilingual('):
    need('LastShiftYuXiang.cs', yuxiang, token, '余响使用官方角色、字幕与选项')
if len(re.findall(r'LastShiftSession\.Enter\(', entry)) != 1 or 'LastShiftSession.Enter(' not in after_punch \
        or 'CanBoard()' not in after_punch or 'StartCoroutine(EnterAfterPunch(player))' not in depart:
    errors.append('LastShiftEntry.cs: LastShiftSession.Enter 只能在剪票动画后、重新过 CanBoard 时调用')
if len(re.findall(r'\.Depart\(', yuxiang)) != 1 or 'request.Entry.Depart(' not in board or 'Depart(' in completed:
    errors.append('LastShiftEntry.cs: Depart 只能由余响的登车对话走到')
for flow, choice in ((board, 'YuXiangBoardingChoices'), (converse, 'YuXiangHandInChoices')):
    at = flow.find(choice)
    if at == -1 or flow.find('LastShiftStory.TryHandIn(', at) == -1:
        errors.append('LastShiftYuXiang.cs: 只有玩家在官方选项确认后才交票根')
need('LastShiftEntry.cs', entry, 'bundle.LoadAsset<GameObject>(LastShiftEntry.PrefabPath)', '入口模型只从小包来')
for rel in ('LastShiftEntry.cs', 'LastShiftWorld.cs'):
    if not re.search(r'if\s*\(\s*!shader\.isSupported\s*\)\s*\r?\n?\s*throw', files.get(rel, '')):
        errors.append(rel + ': 着色器不被支持时必须抛错，不降级')
if re.search(r'Shader\.Find\(', everything):
    errors.append('LastShiftYard: 不许运行时 Shader.Find 找替代着色器')

world = files.get('LastShiftWorld.cs', '')
shaders = re.search(r'WorldShaders\s*=\s*\{([^}]*)\}', world)
runtime = set(re.findall(r'"([^"]+)"', shaders.group(1))) if shaders else set()
verify = (ROOT / 'tools/verify_lastshift_bundle_shaders.py').read_text(encoding='utf-8')
required = re.search(r'REQUIRED\s*=\s*\{(.*?)\}', verify, re.S)
offline = set(re.findall(r'"(BossRush/[^"]+)"\s*:', required.group(1))) if required else set()
if not runtime or runtime != offline:
    errors.append(f'着色器白名单不一致：运行时 {sorted(runtime)}，判包脚本 {sorted(offline)}')

npcs = json.loads((ROOT / 'Assets/Data/DuckNpcs.json').read_text(encoding='utf-8-sig'))
yu = [n for n in npcs.get('npcs', []) if n.get('id') == 'lastshift_yuxiang']
if len(yu) != 1 or yu[0].get('team') != 'middle':
    errors.append('Assets/Data/DuckNpcs.json: 余响必须在中立阵营 middle（站在出击图里，别的阵营会朝她开火）')

if re.search(r'ItemAssetsCollection\.Instance|\.entries\b|GetAllTypeIds\(|ItemAssetsCollection\.Search\(', everything):
    errors.append('LastShiftYard: 不在运行时扫官方物品表，官方物品编号查官方 Wiki 写死')
if len(yu) == 1 and sorted(yu[0].get('equipmentTypeIds') or []) != [791, 1136]:
    errors.append('Assets/Data/DuckNpcs.json: 余响的站务员装扮（鸭舌帽 1136、皮夹克 791）写在蓝图里')
jeff = files.get('LastShiftJeffIntro.cs', '')
need('LastShiftJeffIntro.cs', jeff, 'internal const int QuestId = 590216;', '杰夫引导的官方任务号（进官方存档快照）')
need('LastShiftJeffIntro.cs', jeff, 'GiverId = (int)QuestGiverID.Jeff', '引导挂在杰夫身上')
need('LastShiftYuXiang.cs', converse, 'LastShiftStory.MarkMet();', '和余响对话算见过她')
need('LastShiftYuXiang.cs', board, 'LastShiftStory.MarkMet();', '剪票上车算见过她')

story = files.get('LastShiftStory.cs', '')
need('LastShiftStory.cs', story, 'StorageKey = "BossRush_LastShiftYard_Story_v1"', '剧情存档键不改（进存档）')
need('LastShiftStory.cs', story, 'NoteKeyPrefix = "BossRushLastShift_"', '图鉴前缀不改（进官方笔记解锁表）')
hand_in = body(story, 'internal static bool TryHandIn(')
mutate_at, consume_at = hand_in.find('Mutate('), hand_in.find('TidegateInventory.Consume(')
if mutate_at < 0 or consume_at < 0 or mutate_at > consume_at:
    errors.append('LastShiftStory.cs: 交票根要先写剧情存档、写成了再拿走票根')
commits = re.findall(r'LastShiftStory\.CommitRun\(', everything)
commit_story = body(session, 'private void CommitStory()')
if len(commits) != 1 or 'LastShiftStory.CommitRun(' not in commit_story:
    errors.append('LastShiftSession.cs: 本趟进度只能在 CommitStory 里写进存档（全图一处）')
if not re.search(r'Safe\("story_commit",\s*CommitStory\);\s*\n\s*Close\(true,\s*"extract_"', session):
    errors.append('LastShiftSession.cs: CommitStory 只在撤离读条完成、返航之前调用')
if len(re.findall(r'\bCommitStory\b', session)) != 2:
    errors.append('LastShiftSession.cs: CommitStory 只能有撤离这一处调用（阵亡丢本趟线索）')
module = files.get('LastShiftRuntimeModule.cs', '')
need('LastShiftRuntimeModule.cs', module, 'persistence.Dispose()', '销毁路径收尾剧情存档')
need('LastShiftRuntimeModule.cs', module, 'LastShiftNoteBridge.ResetStaticCaches()', '销毁路径退订图鉴的存档事件')

checks = {
    'Tidegate/TidegateSession.cs': 'GetComponent<LastShiftSession>() != null',
    'Tidegate/TidegateRuntimeModule.cs': 'GetComponent<LastShiftSession>() == null',
    'SkyIsland/SkyIslandSession.cs': 'GetComponent<LastShiftSession>() != null',
    'DebugAndTools/ArenaPrototype/ArenaPrototypeSession.cs': 'GetComponent<LastShiftSession>() != null',
    'DebugAndTools/F3GameplayValidationRunner.cs': 'GetComponent<LastShiftSession>() != null',
    'Common/World/RaidIndoorRoofs.cs': 'g.name == "LastShiftIndoorRooms"',
    'ModBehaviourRuntimeModules.cs': 'runtimeModuleHost.Register(new LastShiftRuntimeModule())',
}
for rel, token in checks.items():
    need(rel, code(rel), token, '互斥 / 室内 / 注册接线')

bat = (ROOT / 'compile_official.bat').read_text(encoding='utf-8')
for bundle in ('lastshift_raid', 'lastshift_entry'):
    if f'copy /Y "Assets\\arenas\\{bundle}"' not in bat:
        errors.append(f'compile_official.bat: 部署段缺 {bundle}')
    if f'"Assets/arenas/{bundle}"' not in (ROOT / 'tools/resource_release_manifest.json').read_text(encoding='utf-8'):
        errors.append(f'tools/resource_release_manifest.json: 发布清单缺 {bundle}')

for error in errors:
    print('  - ' + error)
print('LastShiftRaidGuard: ' + ('FAIL' if errors else 'PASS'))
sys.exit(1 if errors else 0)
