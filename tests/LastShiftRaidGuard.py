#!/usr/bin/env python3
"""末班编组站接入的结构不变式（身份、入口、装配、互斥、部署；第三期起加剧情存档）。

钉的东西：
- 场景身份只经共用桥 `ModRaidSceneBridge` 登记，不另装官方 SceneLoader 补丁；进图走 `ModRaidLease`。
- 入口只在一趟没结束的官方出击里开（`RaidUtilities.CurrentRaid.ended`），上车前必弹确认框、写明本趟不结算，确认后才进会话。
- 入口模型只从 `lastshift_entry` 小包来；着色器不降级（世界与入口都在 `isSupported` 不过时抛错），白名单与判包脚本一致。
- 和潮闸镇、天空岛、试验场、完整 F3 验收互相拒绝。
- 室内屋顶认 `LastShiftIndoorRooms`；部署段与发布清单登记两个包；运行时模块已注册。
- 剧情（第三期）：存档键与图鉴前缀不改；本趟进度只在安全撤离时写进存档（阵亡丢本趟线索）；
  交票根先写存档再拿走票根；交票只发生在上车确认之后。
判断前去掉 C# 注释，免得注释里的同名字串骗过子串检查。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors = []


def code(rel):
    text = (ROOT / rel).read_text(encoding='utf-8-sig')
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.S)
    return '\n'.join(re.sub(r'//.*$', '', line) for line in text.splitlines())


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
need('LastShiftRuntimeModule.cs', module, 'SceneLoader.onStartedLoadingScene -= OnStartedLoading', '销毁路径退订')

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
need('LastShiftEntry.cs', entry, 'BossRushConfirmDialog.Show(', '上车前弹确认框')
need('LastShiftEntry.cs', entry, '本趟不结算', '确认框写明本趟不结算')
# 上车链：确认框 OnConfirm = Board → Board（交票根、可能再弹收尾页）→ Depart → LastShiftSession.Enter。
def body(text, signature):
    m = re.search(re.escape(signature) + r'.*?\n        \}', text, re.S)
    return m.group(0) if m else ''


completed = body(entry, 'protected override void OnInteractCompleted()')
board = body(entry, 'private void Board()')
depart = body(entry, 'private void Depart(')
if not re.search(r'OnConfirm\s*=\s*Board\s*,', completed):
    errors.append('LastShiftEntry.cs: 上车确认框的 OnConfirm 必须是 Board')
if len(re.findall(r'LastShiftSession\.Enter\(', entry)) != 1 or 'LastShiftSession.Enter(' not in depart:
    errors.append('LastShiftEntry.cs: LastShiftSession.Enter 只能出现一次，且只在 Depart 里')
if re.findall(r'\bDepart\(', entry.replace('private void Depart(', '')) and 'Depart(' not in board:
    errors.append('LastShiftEntry.cs: Depart 只能由 Board 走到')
if 'Depart(' in completed:
    errors.append('LastShiftEntry.cs: 交互完成时只弹确认框，不能直接 Depart')
if 'LastShiftStory.TryHandIn(' not in board or 'TryHandIn(' in completed:
    errors.append('LastShiftEntry.cs: 交票根只在确认上车之后（Board 里）')
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
