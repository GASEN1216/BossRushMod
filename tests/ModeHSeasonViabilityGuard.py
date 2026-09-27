"""Guard: Mode H 六场可行性——计划器只抽「本池真组得出来」的骨架与人数。

## 为什么要有这份

2026-09-12 的 F3 实机跑（`BossRushValidation_20260912_072302_209`）里 Mode H 卡死在选秀页：
`MODE_H_FULL_SEASON` 连点 388 次「签约」、100 秒内 lifecycle 一步没动，日志上是同一行
`签约组合六场可行性检查失败: season_viability_match_6:plan_threat_out_of_corridor`，
`MODE_H_CACHE_HIT_CLEANUP` 跟着一起红（赛季没归档）。

根因是**数据层面结构性抽空**：走廊下界 `minFillPercent` 是按「基础威胁和」定的，
而单体威胁分只有 38..62，人数少的骨架怎么凑都够不着自己的下界。逐档复算（本守卫会重算一遍）：

* 第 6 场 `champion_beast` 只许 1–2 人：n=1 上界 62 < 下界 136；n=2 上界 120×1.2=144 < 下界 164。
  **两档都恒不可行**，等于第 6 场一半的候选抽签必然作废。
* 第 3 场 `relay_squad` 的 n=4、第 1 场 `single_beast` 的 n=1（除非池里有 62 分那位）同理。

再叠上「对手池 = 认证池 − 本季五席」对**所有签约组合都一样**这一点，一旦第 6 场判死，
玩家换哪个替补都没用，选秀页没有取消键也没有退出口——是一个真的死局，不是概率问题。

修法是在 `BuildCandidate` 里先抽擂台条件，再用与修复同一条枚举
（`TryFindLegalRosterSelection`：同走廊、同必选回响核心、同擂台条件、同合同原型矩阵）
筛出「本池真组得出来」的 (骨架, 人数)，只从这些里抽。**数据表与走廊数值一字不动。**

本守卫钉两层：

1. **结构层**：擂台条件先抽；候选抽签走 `CollectBuildableDraws`；筛选必须复用
   `TryFindLegalRosterSelection` 的同一套输入；筛不出来时仍回落原路径并报原来的拒绝原因。
2. **数据层**：直接从 `ThreatPlans.json` + `BossProfiles.json` 重算走廊算术，断言
   * 每一场至少有一档 (骨架, 人数) 在整份目录上可行——否则该场恒不可建，筛选也救不了；
   * 认证池 ≥ 10 人时，**任何**合法五席抽签留下的对手池都能把六场全部建出来
     （这正是 `CR-2026-09-12-003` 修复买到的性质：观测到的那次 |认证池| = 10）；
   * 门槛以下的规模根本不枚举——它们进不了门（见下一节）。

它证明的是算术与接线，不证明手感：六场打起来是不是好玩只能实机看。

### 小池缺口已拍板（`CR-2026-09-12-018`，2026-09-12）

认证池只有 8–9 人（12 个预设里有几个在玩家这台机器上不可用）时，对手池只剩 3–4 人，
第 6 场 / 第 4 场仍可能一档都建不出来。三条互斥的出路里选了**抬高 `MinProductionCandidateCount`（8 → 9）**：

* **不选「放宽走廊」**：那是改平衡数值，而且离线证不了打起来是什么样。
* **不选「让落选三席回到对手池」**：与已公布的设计直接冲突——落选三人各翻一张**去向牌**
  （回场签第 5 场回来打你 / 候签进转会窗口 / **撕票本季永久移除，谁也签不到**，见玩家 Wiki）。
  把他们无差别丢回对手池，等于让「撕票」那位照样出现在场上，是打自己脸。
* **停在 9 而不是 10**：10 会把「只认证过 9 个」的玩家整个挡在模式之外；而 9 人的 2.5% 是**有出路的**
  （判死提示就是「退出本赛季重新进入，候选名单会重抽」，重进即重抽）。8 人的 52.9% 没有出路。

于是本守卫的枚举从 9 起步：8 人那一档从此进不了门，9 人的 62 种坏组合仍冻成上界，数据改坏就转红。
"""
import itertools
import json
import re
import sys
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
from cs_source_util import clean_source

PLANNER = 'ModeH/ModeHEncounterPlanner.cs'
MATCHFLOW = 'ModeH/ModeHRuntimeModule_MatchFlow.cs'
CONFIG = 'ModeH/ModeHConfig.cs'
THREAT_PLANS = 'Assets/Data/ModeH/ThreatPlans.json'
BOSS_PROFILES = 'Assets/Data/ModeH/BossProfiles.json'

# 认证池 9 人时当前数据下仍会出现「某一场一档都建不出来」的抽签组合数上界。
# 这是登记在案的已知缺口，不是可以随便抬高的预算：数字变大说明数据改坏了。
#
# 2026-09-12（`CR-2026-09-12-018`）：`MinProductionCandidateCount` 由 8 抬到 9，
# 8 人那一档（1332 种坏组合 / 52.9%）从此**进不了门**，不再枚举。9 人的 62 种（2.5%）保留为已知缺口——
# 它有出路：判死时的提示就是「退出本赛季重新进入，候选名单会重抽」，重进即重抽。
KNOWN_DEAD_SCENARIOS = {9: 62}

errors = []


def read(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append('读不到 ' + rel)
        return ''
    return clean_source(path.read_text(encoding='utf-8-sig'))


def read_json(rel):
    path = ROOT / rel
    if not path.exists():
        errors.append('读不到 ' + rel)
        return None
    return json.loads(path.read_text(encoding='utf-8-sig'))


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


planner = read(PLANNER)
matchflow = read(MATCHFLOW)
config = read(CONFIG)

# ---- 1. 结构层：抽签只从「组得出来」的组合里取 ----
def squeeze(text):
    return re.sub(r'\s+', ' ', text)


def check_structure(planner_src, matchflow_src):
    """结构断言集中在这里，好让下面的变异探针能整体重跑一遍。"""
    found = []

    def sub_body(src, signature, label):
        return body(src, signature, label)

    def need(haystack, needle, why):
        if needle not in haystack:
            found.append('%s（找不到 %r）' % (why, needle))

    candidate_src = sub_body(planner_src, 'private static ModeHMatchPlanDto BuildCandidate(', PLANNER)
    collect_src = sub_body(planner_src, 'private static void CollectBuildableDraws(', PLANNER)
    pick_src = squeeze(sub_body(matchflow_src, 'private void OnDraftPick(', MATCHFLOW))

    need(candidate_src, 'ModeHArenaConditionSpec condition = PickArenaCondition(stream);',
         '擂台条件必须先抽：可行性判定要用它算能力矩阵')
    order = [candidate_src.find('PickArenaCondition(stream)'),
             candidate_src.find('CollectBuildableDraws('),
             candidate_src.find('PickEntryScript(stream, skeleton)')]
    if -1 in order or order != sorted(order):
        found.append('BuildCandidate 的取值顺序必须是 擂台条件 → 可行组合 → 进场剧本（实际下标 %r）'
                     % (order,))
    need(candidate_src,
         'CollectBuildableDraws(corridor, enemyStableKeyPool, echoReturnStableKey, condition,',
         '可行组合筛选必须拿到本场走廊、对手池、回响核心与擂台条件')
    need(candidate_src, 'liveArchetypeIds, buildableSkeletons, buildableCounts);',
         '可行组合筛选必须带上存活合同原型（roster veto 与修复同源）')
    need(candidate_src, 'int draw = stream.NextInt(buildableSkeletons.Count);',
         '骨架与人数必须一次抽定，且只从可行列表里抽')
    need(candidate_src, 'unitCount = buildableCounts[draw];',
         '抽出的人数必须与抽出的骨架配对，不能再独立随机')
    # 兜底路径仍在：一个都筛不出来时不许改口径，照旧抽、照旧报原来的拒绝原因。
    need(candidate_src, 'skeleton = PickSkeleton(stream, corridor);',
         '筛不出可行组合时必须回落原抽签路径')
    need(candidate_src, 'unitCount = stream.NextIntInclusive(skeleton.MinUnits, skeleton.MaxUnits);',
         '回落路径的人数仍按骨架上下限抽')
    need(candidate_src, 'failureReasonId = "plan_skeleton_missing";',
         '回落路径仍要报原来的拒绝原因，不许把「本池确实组不出来」伪装成别的问题')

    need(collect_src,
         'if (!TryFindLegalRosterSelection(count, enemyStableKeyPool, requiredCoreKey,',
         '可行性判定必须复用组合修复的同一条枚举，不许另写一套走廊算术')
    need(collect_src, 'corridor, condition, liveArchetypeIds, out probe)) continue;',
         '可行性判定必须用同一走廊、同一擂台条件、同一合同原型')
    need(collect_src, 'if (string.IsNullOrEmpty(echoReturnStableKey)) continue;',
         '回场签骨架拿不到回响核心时不算「组得出来」')
    need(collect_src, 'for (int count = skeleton.MinUnits; count <= skeleton.MaxUnits; count++)',
         '人数必须逐档判定，不能只判最小人数')

    # 选人页两步锁定（2026-09-25）：第一次只锁首发，第二次由玩家明确选择接力；
    # 首发一旦锁定不得被刷新替换。两人组合仍必须过同一条签约 / 分流 / 六场可行性门，
    # 防止玩家手动挑出无法排满赛季的死局。
    need(pick_src, '_draftPrimaryProfileId = picked.profileId;',
         '第一次点击必须只锁定首发，不自动替玩家选接力')
    need(pick_src, 'ModeHDraftController.TrySignContracts( _season.profiles, _draftPrimaryProfileId, picked.profileId,',
         '第二次点击必须用玩家选定的首发与接力签约')
    need(pick_src, 'CanConstructFullSeason(contract, assignments, out failureReasonId)',
         '签约前仍要过六场可行性门')
    gate = pick_src.find('if (!viable)')
    accept = pick_src.find('_season.contract = contract;')
    if gate < 0 or accept < 0 or gate > accept:
        found.append('凑不出可行搭档时必须在写入合同之前返回（if (!viable) 要排在 _season.contract = contract; 之前）')
    return found


errors.extend(check_structure(planner, matchflow))

# 变异探针：放宽任意一条约束都必须被 check_structure 抓住。
for before, after in [
    ('ModeHArenaConditionSpec condition = PickArenaCondition(stream);', ''),
    ('int draw = stream.NextInt(buildableSkeletons.Count);', 'int draw = 0;'),
    ('unitCount = buildableCounts[draw];', 'unitCount = skeleton.MinUnits;'),
    ('CollectBuildableDraws(corridor, enemyStableKeyPool, echoReturnStableKey, condition,',
     'CollectBuildableDraws(corridor, null, echoReturnStableKey, condition,'),
    ('corridor, condition, liveArchetypeIds, out probe)) continue;',
     'corridor, condition, null, out probe)) continue;'),
    ('for (int count = skeleton.MinUnits; count <= skeleton.MaxUnits; count++)',
     'for (int count = skeleton.MinUnits; count <= skeleton.MinUnits; count++)'),
    ('!CanConstructFullSeason(contract, assignments, out failureReasonId)',
     'false'),
    ('if (!viable)', 'if (false)'),
    ('_season.profiles, _draftPrimaryProfileId, picked.profileId,', '_season.profiles, picked.profileId, _draftPrimaryProfileId,'),
]:
    in_planner = before in planner
    if not in_planner and before not in matchflow:
        errors.append('[probe] 缺少变异目标: ' + before)
        continue
    if in_planner:
        rejected = check_structure(planner.replace(before, after, 1), matchflow)
    else:
        rejected = check_structure(planner, matchflow.replace(before, after, 1))
    if not rejected:
        errors.append('[probe] 放宽约束的变异漏检: ' + before)

# ---- 2. 数据层：重算走廊算术 ----
plans = read_json(THREAT_PLANS)
profiles = read_json(BOSS_PROFILES)

coefficient_match = re.search(r'ThreatActionCountCoefficient\s*=\s*([0-9.]+)f', config)
tolerance_match = re.search(r'ThreatBudgetTolerance\s*=\s*([0-9.]+)f', config)
min_pool_match = re.search(r'MinProductionCandidateCount\s*=\s*(\d+)', config)
if not (coefficient_match and tolerance_match and min_pool_match):
    errors.append('ModeHConfig 里读不到威胁溢价 / 容差 / 最小认证池常量')

if plans and profiles and coefficient_match and tolerance_match and min_pool_match:
    coef_milli = int(float(coefficient_match.group(1)) * 1000)
    tolerance_percent = int(float(tolerance_match.group(1)) * 100)
    min_pool = int(min_pool_match.group(1))

    scores = {}
    archetypes = {}
    for entry in profiles['profileTemplates']:
        scores[entry['stableKey']] = int(entry['threatScore'])
        archetypes.setdefault(entry['archetypeId'], []).append(entry['stableKey'])
    corridors = {int(c['matchIndex']): c for c in plans['matchCorridor']}
    skeletons = {s['skeletonId']: s for s in plans['skeletons']}
    keys = sorted(scores)
    archetype_ids = sorted(archetypes)

    def bounds(budget, min_fill, count):
        premium = 1000 + coef_milli * (count - 1)
        scaled = budget * premium // 1000
        return scaled * min_fill // 100, scaled * (100 + tolerance_percent) // 100

    def effective(values):
        premium = 1000 + coef_milli * (len(values) - 1)
        return sum(values) * premium // 1000

    def draws(match_index):
        corridor = corridors[match_index]
        for skeleton_id in corridor['skeletonIds']:
            spec = skeletons[skeleton_id]
            for count in range(int(spec['minUnits']), int(spec['maxUnits']) + 1):
                yield skeleton_id, spec, count

    @lru_cache(maxsize=None)
    def match_buildable(pool_scores, match_index, echo_scores):
        corridor = corridors[match_index]
        for _, spec, count in draws(match_index):
            low, high = bounds(int(corridor['threatBudget']), int(corridor['minFillPercent']), count)
            if spec.get('requiresEchoReturn'):
                for echo in echo_scores:
                    if count - 1 > len(pool_scores):
                        continue
                    for combo in itertools.combinations(pool_scores, count - 1):
                        if low <= effective(combo + (echo,)) <= high:
                            return True
            else:
                if len(pool_scores) < count:
                    continue
                for combo in itertools.combinations(pool_scores, count):
                    if low <= effective(combo) <= high:
                        return True
        return False

    # 2a. 整份目录上每一场至少有一档可行；否则该场恒不可建，筛选也救不了。
    all_scores = tuple(sorted(scores.values()))
    for match_index in sorted(corridors):
        alive = []
        for skeleton_id, spec, count in draws(match_index):
            corridor = corridors[match_index]
            low, high = bounds(int(corridor['threatBudget']), int(corridor['minFillPercent']), count)
            if len(all_scores) < count:
                continue
            if any(low <= effective(combo) <= high
                   for combo in itertools.combinations(all_scores, count)):
                alive.append('%s/n=%d' % (skeleton_id, count))
        if not alive:
            errors.append('第 %d 场在整份目录上一档 (骨架, 人数) 都不可行，走廊或威胁分需要重定'
                          % match_index)

    # 2b. 认证池 ≥ 10 时任何合法五席都留得下可建的六场；8 / 9 人的已知缺口冻成上界。
    def count_dead(certified_size):
        dead = 0
        for certified in itertools.combinations(keys, certified_size):
            certified_set = set(certified)
            available = [[k for k in archetypes[a] if k in certified_set] for a in archetype_ids]
            if any(not bucket for bucket in available):
                continue
            for candidates in itertools.product(*available):
                pool = tuple(sorted(scores[k] for k in certified_set - set(candidates)))
                echoes = tuple(sorted(scores[k] for k in candidates))
                if any(not match_buildable(pool, m, echoes) for m in sorted(corridors)):
                    dead += 1
        return dead

    for certified_size in range(min_pool, len(keys) + 1):
        dead = count_dead(certified_size)
        allowed = KNOWN_DEAD_SCENARIOS.get(certified_size, 0)
        if dead > allowed:
            errors.append('认证池 %d 人时有 %d 种合法抽签留下建不出六场的对手池（登记上界 %d）'
                          % (certified_size, dead, allowed))
        elif dead < allowed:
            errors.append('认证池 %d 人的已知缺口已收窄到 %d（登记上界 %d）：'
                          '请把 KNOWN_DEAD_SCENARIOS 一并调小，不要留虚高预算'
                          % (certified_size, dead, allowed))

if errors:
    for e in errors:
        print('  - ' + e)
    print('ModeHSeasonViabilityGuard: FAIL')
    raise SystemExit(1)

print('ModeHSeasonViabilityGuard: PASS（擂台条件先抽、(骨架, 人数) 只从本池真组得出来的组合里抽、'
      '筛不出来时回落原路径并报原因；选人一次点击即签约，搭档逐个过六场可行性门；'
      '走廊算术重算：每场都有可行档，认证池 ≥ 10 人时任何合法五席都建得出六场，'
      '门槛已抬到 9、8 人那一档进不了门，9 人的已知缺口未扩大）')
