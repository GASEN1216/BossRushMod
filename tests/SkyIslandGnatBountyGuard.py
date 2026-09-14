"""Guard: 苇白的「夜里驱蚋」委托——打下来的云蚋有人记账，且只派做得完的单。

## 为什么要有这份

内容批次四把云蚋、风晶灭蚊灯、药烟蒲扇、云苔纱笠和蛙卵都做了，唯独「把蚋打下来」这条路
没有任何回报：云蚋死了不掉东西，也没有任何目标在数它。于是驱风香（云苔纤维 2 + 青穗草 2，
约 300 价值，燃着时整群都不来、还挡一切风、耐力恢复 +15%）把风晶灭蚊灯（晴岚风晶 1 + 残铜片 3，
约 2790 价值、还要先点亮两盏风晶灯、而且把刷新权重 ×1.5 主动招蚋）全面压住；风灯「招蚋、
照着的不躲」那半句也没有兑现对象。两件做出来的东西没有人会用，属于「为了堆内容而加」。

修复把云蚋接进已有的航务委托：打下来的蚋算第四类单子。本守卫钉住这条线不断：

1. 委托侧：`SkyIslandBountyKind.Gnats` 有自己的基数、计数器、中英文名，并挂进派单列表；
2. 计数侧：只有被打下来（`killed`）才上报，且 `Dispose` 不经过 `Remove`
   ——否则离岛清场会被当成一串击杀；
3. 门控侧：可完成量走「天亮之前保守还能打下几只」，不是夜里 / 没有蚊群一律 0，
   于是白天根本不会挂出这一单；读钟仍只经 `SkyIslandLighting` 一处；
4. 派单侧：苇白面板那条「只派做得完的单」的硬规则按 `AllKinds` 遍历，新方向自动受同一条约束；
5. 编译清单登记了新拆出来的 partial。

它证明的是接线与算术，不证明手感：一单 12 只在实机里是不是「两三分钟的活」只能实机看
（见 `docs/制作教程/天空岛/天空岛_待人工验证清单.md`）。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

BOUNTY = 'DebugAndTools/SkyIsland/SkyIslandBounty.cs'
GNATS = 'DebugAndTools/SkyIsland/SkyIslandGnats.cs'
SESSION = 'DebugAndTools/SkyIsland/SkyIslandSession.cs'
PARTIAL = 'DebugAndTools/SkyIsland/SkyIslandSessionGnatBounty.cs'
RULES = 'DebugAndTools/SkyIsland/SkyIslandMosquitoRules.cs'
NIGHT = 'DebugAndTools/SkyIsland/SkyIslandNight.cs'
LIGHTING = 'DebugAndTools/SkyIsland/SkyIslandLighting.cs'
STORY = 'DebugAndTools/SkyIsland/SkyIslandWorldStory.cs'
ENCOUNTERS = 'DebugAndTools/SkyIsland/SkyIslandEncounters.cs'
MANIFEST = 'compile_official.bat'

errors = []


def read(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append('读不到 ' + rel)
        return ''
    return clean_source(path.read_text(encoding='utf-8-sig'))


def body(src, signature, label):
    start = src.find(signature)
    if start < 0:
        errors.append('%s 找不到 %s' % (label, signature))
        return ''
    opening = src.find('{', start)
    if opening < 0:
        errors.append('%s 的 %s 没有块体' % (label, signature))
        return ''
    depth = 0
    for i in range(opening, len(src)):
        if src[i] == '{':
            depth += 1
        elif src[i] == '}':
            depth -= 1
            if depth == 0:
                return src[start:i + 1]
    errors.append('%s 的 %s 花括号不配对' % (label, signature))
    return ''


def require(haystack, needle, why):
    if needle not in haystack:
        errors.append('%s（找不到 %r）' % (why, needle))


def forbid(haystack, needle, why):
    if needle in haystack:
        errors.append('%s（不该出现 %r）' % (why, needle))


bounty = read(BOUNTY)
gnats = read(GNATS)
session = read(SESSION)
partial = read(PARTIAL)
rules = read(RULES)
night = read(NIGHT)
lighting = read(LIGHTING)
story = read(STORY)
manifest = (ROOT / MANIFEST).read_text(encoding='utf-8', errors='ignore')

# ---- 1. 委托侧 ----
require(bounty, 'Gnats = 4', '驱蚋要有自己的稳定枚举值（不复用既有方向）')
base = re.search(r'internal const int BaseGnatTarget = (\d+);', bounty)
if not base:
    errors.append('读不到 BaseGnatTarget')
else:
    target = int(base.group(1))
    if target < 6 or target > 24:
        errors.append('驱蚋基数 %d 不在 6–24 的合理区间：太少没内容、太多变成刷' % target)
require(bounty, 'internal void ReportGnatCulled() { culledTotal++; }', '驱蚋要有自己的计数器')
require(body(bounty, 'internal int Counter(SkyIslandBountyKind kind)', BOUNTY),
        'if (kind == SkyIslandBountyKind.Gnats) return culledTotal;', 'Counter 要认驱蚋')
require(body(bounty, 'internal int TargetFor(SkyIslandBountyKind kind)', BOUNTY),
        'if (kind == SkyIslandBountyKind.Gnats) return BaseGnatTarget + completedRounds;',
        'TargetFor 要认驱蚋，并和另外三类一样每交一单 +1')
kinds = body(bounty, 'internal static SkyIslandBountyKind[] AllKinds', BOUNTY)
require(kinds, 'SkyIslandBountyKind.Gnats', '驱蚋要挂进派单列表，否则永远派不出来')
names = {}
for func in ('NameCn', 'NameEn'):
    block = body(bounty, 'internal static string %s(SkyIslandBountyKind kind)' % func, BOUNTY)
    match = re.search(r'if \(kind == SkyIslandBountyKind\.Gnats\) return "([^"]+)";', block)
    if not match:
        errors.append('%s 没有驱蚋的名字（英文玩家会看到中文）' % func)
    else:
        names[func] = match.group(1)
if len(names) == 2 and names['NameCn'] == names['NameEn']:
    errors.append('驱蚋的中英文名一样，八成是漏了一处')
if 'NameEn' in names and re.search(r'[一-鿿]', names['NameEn']):
    errors.append('驱蚋的英文名里有中文：' + names['NameEn'])

# ---- 2. 计数侧：只有被打下来才算，退出局不算 ----
remove = body(gnats, 'private void Remove(int index, bool killed, float now, Quaternion view)', GNATS)
require(remove, 'if (!killed) return;', '散开（跑远、大风、进烟）不算击杀')
require(remove, 'if (session != null && session.Bounty != null) session.Bounty.ReportGnatCulled();',
        '击杀要上报给委托，且要对空会话与空委托宽容')
if remove.count('ReportGnatCulled') != 1:
    errors.append('Remove 里上报了不止一次，会把一只算成多只')
dispose = body(gnats, 'public void Dispose()', GNATS)
forbid(dispose, 'Remove(', 'Dispose 不许走 Remove：离岛清场会被当成一串击杀')
require(dispose, 'gnats[i] = null;', 'Dispose 直接清数组')
# 上报点只有 Remove 一处：别处再加一处就会重复计数。
if gnats.count('ReportGnatCulled') != 1:
    errors.append('SkyIslandGnats 里的击杀上报点不止 Remove 一处：%d 处' % gnats.count('ReportGnatCulled'))

# ---- 3. 门控侧 ----
route = body(session, 'internal int AvailableBountyProgress(SkyIslandBountyKind kind)', SESSION)
require(route, 'return kind == SkyIslandBountyKind.Gnats ? AvailableGnatCull() : 0;',
        '会话要把驱蚋的可完成量转发给拆出去的 partial')
cull = body(partial, 'private int AvailableGnatCull()', PARTIAL)
require(cull, 'if (swarm == null || !swarm.Usable) return 0;', '没有蚊群或精灵表缺失时不派这一单')
require(cull, 'if (!SkyIslandNight.IsNight(hours)) return 0;', '白天不派这一单')
require(cull, 'SkyIslandLighting.ClockHours()', '读钟仍只经唯一入口')
require(cull, 'SkyIslandMosquitoRules.CullableBeforeDawn(', '可完成量要走保守下界算式')
require(cull, 'SkyIslandNight.RealSecondsUntilDawn(hours, SkyIslandLighting.ClockScale())',
        '距天亮的现实秒要按官方时钟倍速折算')
forbid(partial, 'GameClock.', '本文件不许直接读官方时钟（只经 SkyIslandLighting）')

dawn = body(night, 'internal static double RealSecondsUntilDawn(double hours, double clockScale)', NIGHT)
require(dawn, 'if (!IsNight(hours)) return 0.0;', '不是夜里就没有剩余夜长')
require(dawn, 'clockScale <= 0.0) return 0.0;', '倍速非正时不算（避免除零与负数）')
cullable = body(rules, 'internal static int CullableBeforeDawn(double realSecondsUntilDawn, int alive)', RULES)
require(cullable, 'SpawnCheckSeconds + SpawnCooldownMax', '一群按最坏节奏折算（保守）')
require(cullable, '* GroupMin', '一群只按最少只数折算（保守）')
require(cullable, 'alive > MaxAlive ? MaxAlive', '场上只数要按同时存活上限夹住')
scale = body(lighting, 'internal static double ClockScale()', LIGHTING)
require(scale, 'SkyIslandNight.DefaultClockScale', '读不到官方时钟时回落默认倍速')

# ---- 4. 派单侧：只派做得完的单这条硬规则按 AllKinds 遍历 ----
offer = body(story, 'private void BountyChoices(', STORY)
require(offer, 'SkyIslandBountyKind[] kinds = SkyIslandBounty.AllKinds;', '派单要遍历 AllKinds，新方向才会自动挂出')
require(offer, 'if (session.AvailableBountyProgress(kind) < target) continue;',
        '只派做得完的单：新方向也必须受这条硬规则约束')
require(offer, 'contract.TryAbandon(out message)', '永远保留退单出口（天亮之后这一单做不下去）')

# ---- 6. 「清理航路威胁」的口径与注释诚实性（CR-2026-09-12-012）----
# 背景：`AvailableBountyProgress` 的主注释一度声称「Threats 的清场是持久存档事实，已清过的组这局
# 根本不会再触发回调（Tick 直接短路成 Cleared）」。这句对 Threats 数的那批**自动**组是错的：
# `Tick` 的短路带着 `encounter.Manual`，只关折翎 / 钟守 / 噬风。自动组按出击刷新，
# 照存档过滤会让第二趟起可完成量恒为 0、「清理航路威胁」永远派不出来——而这正是
# `RemainingClearable` 自己的注释在警告的那条已知回归。注释把人往回归上推，所以一并钉住。
encounters = read(ENCOUNTERS)
tick = body(encounters, 'internal void Tick()', ENCOUNTERS)
require(tick, 'encounter.Manual && completed(encounter.Id)',
        '存档已清事实只许一次性关掉手动组；去掉 Manual 会让自动组第二趟起不再刷新')
remaining = body(encounters, 'internal int RemainingClearable', ENCOUNTERS)
forbid(remaining, 'completed(',
       '可完成量不得按存档已清过滤：那样第二趟起恒为 0，「清理航路威胁」永远派不出来')
require(remaining, '!encounter.Manual && !encounter.Cleared', '只数本趟还没清的自动组')
# 注释要在原始源码里查（clean_source 会把注释剥掉）。
session_raw = (ROOT / SESSION).read_text(encoding='utf-8-sig')
progress_doc = session_raw[:session_raw.find('internal int AvailableBountyProgress(')]
progress_doc = progress_doc[progress_doc.rfind('/// <summary>'):]
if '已清过的组这局根本不会再触发回调' in progress_doc:
    errors.append('AvailableBountyProgress 的注释仍在声称「已清过的组不会再触发回调」——'
                  '那只对手动组成立，对 Threats 数的自动组是反的')
require(progress_doc, 'encounter.Manual',
        'AvailableBountyProgress 的注释要写明存档已清事实只关手动组')

# ---- 5. 编译清单 ----
if 'SkyIslandSessionGnatBounty.cs' not in manifest:
    errors.append('新拆出来的 partial 没登记进 compile_official.bat（源码在但不会被编译）')

if errors:
    for e in errors:
        print('  - ' + e)
    print('SkyIslandGnatBountyGuard: FAIL')
    raise SystemExit(1)

print('SkyIslandGnatBountyGuard: PASS（驱蚋委托的基数/计数器/中英文名与派单列表；'
      '击杀只从 Remove(killed) 上报一次、Dispose 不计数；白天与无蚊群不派单、'
      '可完成量按保守下界与官方时钟倍速折算；派单仍走「只派做得完的单」与退单出口；'
      '「清理航路威胁」只数本趟未清的自动组且注释与之一致；编译清单已登记）')
