"""Freeze the Sky Island content expansion: scavenging points, enemy tiers, the Storm boss and resident services.

断言口径与仓库其余守卫一致：先剥 C# 注释再比对，避免「注释里出现同名 token」骗过守卫；
数值类断言钉具体数字，不只钉赋值语句在位。
"""
import json
import re
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parent.parent
SKY = ROOT / 'DebugAndTools/SkyIsland'

# 每个区域必须有产出，深处更值钱。改动内容表时必须同步这张表。
EXPECTED_REGION_COUNTS = {
    'A': 3, 'B': 4, 'C': 4, 'D': 4, 'E': 4, 'F': 4, 'G': 4, 'H': 4,
    'S1': 2, 'S2': 2, 'S3': 2, 'S4': 2,
}
EXPECTED_TIER_COUNTS = {'Supply': 11, 'Voyage': 18, 'Starworks': 10}
EXPECTED_TOTAL = 39

# 遭遇表的稳定身份：(marker, count, manual, tier, lead)
EXPECTED_ENCOUNTERS = {
    'C': ('EnemySpawn_C', 2, False, 'Scav', 'Scav'),
    'C_02': ('Search_C_02', 2, False, 'Scav', 'Scav'),
    'D': ('EnemySpawn_D', 2, False, 'Scav', 'Elite'),
    'D_02': ('Search_D_02', 2, False, 'Scav', 'Scav'),
    # 布局 v2：E 的两组自动敌群挪到 DE / GE 桥上的中继平台。
    'E': ('Relay_DE', 2, False, 'Scav', 'Scav'),
    'E_02': ('Relay_GE', 2, False, 'Scav', 'Scav'),
    'G': ('EnemySpawn_G', 2, False, 'Scav', 'Elite'),
    'G_02': ('Search_G_02', 2, False, 'Scav', 'Scav'),
    'S1': ('EnemySpawn_S1', 2, False, 'Scav', 'Scav'),
    'S2': ('EnemySpawn_S2', 2, False, 'Scav', 'Scav'),
    'S3': ('EnemySpawn_S3', 2, False, 'Scav', 'Scav'),
    'S4': ('EnemySpawn_S4', 3, False, 'Scav', 'Elite'),
    'F': ('Search_F_02', 2, False, 'Scav', 'Scav'),
    'Zheling': ('EnemySpawn_F', 1, True, 'Champion', 'Champion'),
    'BellKeeper': ('EnemySpawn_H', 3, True, 'Scav', 'Champion'),
    'Storm': ('POI_E', 3, True, 'Elite', 'Storm'),
}

NEW_SOURCES = (
    'SkyIslandLootTables.cs', 'SkyIslandLootPools.cs', 'SkyIslandRewardCrate.cs',
    'SkyIslandScavenging.cs', 'SkyIslandEnemyTiers.cs', 'SkyIslandStormBoss.cs',
    'SkyIslandBounty.cs', 'SkyIslandServices.cs', 'SkyIslandEnemyTier.cs',
)


# 真实作者布局里的标记集合：搜刮点与遭遇点的 marker 必须实际存在。
AUTHOR_MARKERS = {
    m['id'] for m in json.loads(
        (ROOT / 'ArtSource/SkyIsland/layout.json').read_text(encoding='utf-8'))['markers']
}


def source(name):
    return clean_source((SKY / name).read_text(encoding='utf-8'))


def check_loot_anchors():
    tables = source('SkyIslandLootTables.cs')
    anchors = re.findall(
        r'Anchor\("([A-Za-z0-9_]+)",\s*"([A-Za-z0-9_]+)",\s*([0-9.]+)f,\s*([0-9.]+)f,\s*'
        r'SkyIslandLootTier\.(\w+),\s*"([A-Za-z0-9]+)"\)', tables)
    assert len(anchors) == EXPECTED_TOTAL, 'Sky Island loot anchors changed: %d' % len(anchors)
    ids = [a[0] for a in anchors]
    assert len(set(ids)) == len(ids), 'Duplicate loot anchor id'
    regions = {}
    tiers = {}
    for anchor_id, marker, bearing, distance, tier, region in anchors:
        regions[region] = regions.get(region, 0) + 1
        tiers[tier] = tiers.get(tier, 0) + 1
        assert 4.0 <= float(distance) <= 12.0, 'Anchor %s offset out of sane range' % anchor_id
        assert 0.0 <= float(bearing) < 360.0, 'Anchor %s bearing out of range' % anchor_id
        # 搜刮点只能挂在**真实存在的**作者标记上：新增标记需要重建 54 MB 场景包，
        # 而写错名字在运行时只会静默跳过该点（Find 返回 null），编译与其它守卫都查不出来。
        assert marker in AUTHOR_MARKERS, 'Anchor %s references a marker absent from the author layout: %s' % (
            anchor_id, marker)
    assert regions == EXPECTED_REGION_COUNTS, 'Loot coverage per region changed: %r' % regions
    assert tiers == EXPECTED_TIER_COUNTS, 'Loot tier mix changed: %r' % tiers
    # 品质带必须单调递增且档次之间有区分度，否则「深处更值钱」这条设计承诺就是空的。
    # 只钉「赋值语句在位」是没断言：这里解析方法体，把每档的实际数字取出来比。
    bands = {}
    for accessor in ('MinQuality', 'MaxQuality'):
        marker = 'internal static int %s(SkyIslandLootTier tier)' % accessor
        assert marker in tables, 'Quality band accessor missing: ' + accessor
        body = tables.split(marker, 1)[1].split('\n        }', 1)[0]
        values = dict(re.findall(r'if \(tier == SkyIslandLootTier\.(\w+)\) return (\d+);', body))
        default = re.search(r'\n\s*return (\d+);', body)
        assert default, 'Quality band %s has no default branch' % accessor
        values['Supply'] = default.group(1)
        bands[accessor] = {k: int(v) for k, v in values.items()}
    assert bands['MinQuality'] == {'Supply': 1, 'Voyage': 2, 'Starworks': 4}, \
        'Loot minimum quality band changed: %r' % bands['MinQuality']
    # 游戏一共 8 档；最深一档必须够得到顶档，否则第 8 档物品在天空岛永远刷不出来。
    assert bands['MaxQuality'] == {'Supply': 3, 'Voyage': 5, 'Starworks': 8}, \
        'Loot maximum quality band changed: %r' % bands['MaxQuality']
    for tier_name in ('Supply', 'Voyage', 'Starworks'):
        assert bands['MinQuality'][tier_name] < bands['MaxQuality'][tier_name], \
            'Quality band inverted for ' + tier_name
    # 保底只给「赚来的」奖励，且必须落在该档常规带内。
    guarantee_body = tables.split('internal static int GuaranteeMinQuality(SkyIslandLootTier tier)', 1)[1] \
        .split('\n        }', 1)[0]
    guarantees = dict(re.findall(r'if \(tier == SkyIslandLootTier\.(\w+)\) return (\d+);', guarantee_body))
    assert guarantees == {'Starworks': '6', 'Voyage': '4'}, 'Guarantee bands changed: %r' % guarantees
    assert 'return 0;' in guarantee_body, 'Supply tier must have no guarantee'
    for tier_name, floor in guarantees.items():
        assert bands['MinQuality'][tier_name] <= int(floor) <= bands['MaxQuality'][tier_name], \
            'Guarantee floor for %s falls outside its own quality band' % tier_name
    # 件数同样必须随档次单调不减。历史教训：航务补给曾经是 2–4、星工遗存只有 2–3，
    # 中段区域比全图最深处出得还多，与品质带的递增方向相反，而件数一个数字都没被钉。
    counts = {}
    for accessor in ('MinCount', 'MaxCount'):
        marker = 'internal static int %s(SkyIslandLootTier tier)' % accessor
        assert marker in tables, 'Item count accessor missing: ' + accessor
        body = tables.split(marker, 1)[1].split('\n        }', 1)[0]
        values = dict(re.findall(r'if \(tier == SkyIslandLootTier\.(\w+)\) return (\d+);', body))
        ternary = re.search(r'return tier == SkyIslandLootTier\.(\w+) \? (\d+) : (\d+);', body)
        if ternary:
            assert ternary.group(2) != ternary.group(3), 'Degenerate ternary: both branches return the same count'
            values[ternary.group(1)] = ternary.group(2)
            values['Voyage'] = values['Starworks'] = ternary.group(3)
        else:
            default = re.search(r'\n\s*return (\d+);', body)
            assert default, 'Item count %s has no default branch' % accessor
            values['Supply'] = default.group(1)
        counts[accessor] = {k: int(v) for k, v in values.items()}
    assert counts['MinCount'] == {'Supply': 1, 'Voyage': 2, 'Starworks': 2}, \
        'Loot minimum counts changed: %r' % counts['MinCount']
    assert counts['MaxCount'] == {'Supply': 2, 'Voyage': 3, 'Starworks': 4}, \
        'Loot maximum counts changed: %r' % counts['MaxCount']
    for accessor, table in counts.items():
        ordered = [table['Supply'], table['Voyage'], table['Starworks']]
        assert ordered == sorted(ordered), \
            '%s must not invert against the quality bands: %r' % (accessor, ordered)
    for tier_name in ('Supply', 'Voyage', 'Starworks'):
        assert 1 <= counts['MinCount'][tier_name] <= counts['MaxCount'][tier_name] <= 4, \
            'Crate budget broken for ' + tier_name
    assert 'MarkerClearance = 4.5f' in tables, 'Marker clearance must stay >= interaction pick radius'
    assert 'ActivationRange = 72f' in tables, 'Scavenging activation gate changed'
    # Mono 与 .NET Core 的 string.GetHashCode 口径不同，抽样必须用自带稳定散列。
    assert 'StableHash' in tables and 'GetHashCode' not in tables, 'Loot sampling must not use string.GetHashCode'


def check_pools():
    pools = source('SkyIslandLootPools.cs')
    # 官方 Search 在结果为空时会自行降级 quality 反复重搜，会把星工遗存悄悄降成杂物。
    assert 'ItemAssetsCollection.GetAllTypeIds(filter)' in pools, 'Pool query must use GetAllTypeIds'
    assert 'ItemAssetsCollection.Search(' not in pools, 'Never use the down-grading official Search for tiered loot'
    assert 'LootBlacklistRegistry.Contains(ids[i])' in pools, 'Custom content must be excluded via the loot blacklist'
    assert 'result.Sort()' in pools, 'Pool order must be stable across machines for the seeded stream'
    assert 'internal static int[] GetGuaranteeBand(SkyIslandLootTier tier)' in pools, 'Guarantee band query missing'
    assert 'int key = minQuality * 100 + maxQuality' in pools, 'Pools must cache per quality band, not per tier'
    assert 'ResetStaticCaches' in pools, 'Pool cache needs a lifecycle reset'
    # 只缓存完整跑完的查询：标签表还没就绪、或查询中途抛异常时得到的空池若也进缓存，缓存要到模块销毁才清，
    # 本进程之后每一趟出击的箱子都是空的（2026-09-10 全方位审核）。
    query = pools.split('int key = minQuality * 100 + maxQuality', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'if (complete) cache[key] = cached;' in query, 'Only a fully completed pool query may be cached'
    assert re.search(r'(?<!if \(complete\) )cache\[key\] = cached;', query) is None, \
        'An unconditional cache write would pin a failed (empty) pool for the whole process'
    assert query.index('result.AddRange(unique);') < query.index('complete = true;'), \
        'complete must be set only after the pool was actually built'
    # 排除口径必须走共享策略：只给 excludeTags 会漏掉 DestroyOnLootBox / DontDropOnDeadInSlot /
    # LockInDemoTag 这三类「设计上不该进箱子」的物品（既有 Boss 奖池一直在排它们）。
    assert 'LootExcludeTagPolicy.BuildExcludeTags(' in pools, \
        'Pool exclusions must come from the shared policy, not a local hand-written list'
    # 池形状必须与既有 Boss 奖池一致：逐 tag requireTags 再取并集，
    # 否则无 tag 物品（占位件、内部道具、无标签任务件）会整批落进池子。
    assert 'filter.requireTags = new[] { tag }' in pools, \
        'Pool must be built per official tag so untagged items stay out'
    assert 'foreach (Tag tag in tags.AllTags)' in pools, 'Pool must iterate the official tag set'

    policy = clean_source((ROOT / 'Config/LootExcludeTagPolicy.cs').read_text(encoding='utf-8'))
    for token in ('tagsData.DestroyOnLootBox', 'tagsData.DontDropOnDeadInSlot',
                  'tagsData.LockInDemoTag', 'TryFindQuestTag(tagsData)'):
        assert token in policy, 'Shared loot exclusion dropped a tag: ' + token
    # 既有 Boss 奖池必须复用同一份列表，否则两条路又会各写各的。
    loot = clean_source((ROOT / 'LootAndRewards/LootAndRewards.cs').read_text(encoding='utf-8'))
    assert 'return LootExcludeTagPolicy.BuildExcludeTags(tagsData, includeCharacterTag);' in loot, \
        'The boss loot pool must delegate its exclusions to the shared policy'


def check_crate():
    crate = source('SkyIslandRewardCrate.cs')
    # 官方 LootBoxLoader.Awake 会按位置哈希随机 SetActive 并写 inLevelData；必须在 Awake 之前摘掉。
    assert 'staging.SetActive(false)' in crate, 'Crate must be instantiated under an inactive parent'
    assert 'LootBoxLoader loader = box.GetComponent<LootBoxLoader>()' in crate, 'Crate must strip the official loader'
    # Destroy 帧末才移除组件，而 SetParent 到活动父节点会在本帧触发 Awake，
    # LootBoxLoader.Awake 照样会按位置哈希把箱子随机 SetActive(false)。必须 DestroyImmediate。
    assert 'UnityEngine.Object.DestroyImmediate(loader)' in crate, \
        'Loader must be removed immediately; deferred Destroy still lets Awake run on activation'
    build = crate.split('internal static InteractableLootbox Build(', 1)[1].split('\n        }', 1)[0]
    assert build.index('DestroyImmediate(loader)') < build.index('SetParent(parent, true)'), \
        'Loader must be stripped while the clone is still inactive'
    assert build.index('box.transform.position = position') < build.index('SetParent(parent, true)'), \
        'World position must be final before activation; official code keys off transform.position'
    assert 'InteractableLootboxInventoryHelper.EnsureLocalInventory' in crate, 'Crates need independent local inventories'
    # InstantiateSync 缺资源时返回空壳 FallbackItem，既不为 null 也不抛；而官方 InstantiateFallbackItem
    # 把**同一个 TypeID** 写回去，回读 TypeID 分辨不出它（2026-09-10 全方位审核核对反编译源）。
    # 必须在实例化之前问 GetPrefab；TypeID 回读只留作第二道防线。
    fill = crate.split('internal static int Fill(', 1)[1].split('\n        }', 1)[0]
    assert 'ItemAssetsCollection.GetPrefab(typeId) == null' in fill, \
        'Crate fill must check the prefab before InstantiateSync; a FallbackItem shell carries the same TypeID'
    assert fill.index('ItemAssetsCollection.GetPrefab(typeId)') < fill.index('ItemAssetsCollection.InstantiateSync(typeId)'), \
        'The prefab check must run before instantiation'
    assert 'item.TypeID != typeId' in fill, 'Keep the null/TypeID read-back as a second line of defence'
    # 一件都没装进去的箱子必须收回并报失败：委托谢礼「先送达再消费」全靠 Create 的返回值，
    # 把空箱也算建成，玩家交了单拿到空箱，委托却已经被消耗。
    create = crate.split('internal static bool Create(', 1)[1].split('\n        }', 1)[0]
    empty = create.split('if (added == 0)', 1)
    assert len(empty) == 2, 'A crate that received no item must be withdrawn, not reported as built'
    empty_branch = empty[1].split('}', 1)[0]
    assert 'Destroy(box.gameObject)' in empty_branch and 'return false;' in empty_branch, \
        'An empty crate must be destroyed and reported as a failure so bounty rewards are not consumed'
    assert create.index('if (added == 0)') < create.index('return true;'), \
        'The empty-crate check must run before the crate is reported as built'
    assert 'item.DestroyTree()' in crate, 'Failed item must not leak as a free-floating object'
    # 官方 CreateLocalInventory() 只 AddComponent<Inventory>()，NeedInspection 字段默认 false、
    # 容量是默认 64。不补就得到「开箱即全明牌」的箱子，与本 Mod 其余三条建箱路径和原版都不一致。
    assert 'box.needInspect = true' in crate, 'Crate must keep the official inspect flow'
    assert 'box.Inventory.NeedInspection = true' in crate, \
        'A local inventory never inherits needInspect; it must be set explicitly'
    assert 'box.Inventory.SetCapacity(InventoryCapacity)' in crate, \
        'fallbackCapacity only applies on the fallback path; capacity must be set explicitly'


def check_scavenging():
    scav = source('SkyIslandScavenging.cs')
    assert 'InteractableLootbox.OnStartLoot += OnStartLoot' in scav, 'Loot progress needs the official open event'
    assert 'InteractableLootbox.OnStartLoot -= OnStartLoot' in scav, 'Event subscription must be released'
    assert 'SkyIslandRewardCrate.Build' in scav and 'SkyIslandRewardCrate.Fill' in scav, 'Reuse the shared crate builder'
    # 4.12 门控：进入范围才建箱，一次一个；不在进图时预生成整图战利品。
    assert 'if (best != null) Build(best);' in scav, 'Only one crate may be built per tick'
    assert 'SkyIslandLootTables.ActivationRange' in scav, 'Crate construction must be range gated'
    assert 'TooCloseToMarker' in scav and 'TooCloseToPlacedPoint' in scav, 'Placement must avoid interaction competition'
    assert 'Physics.CheckCapsule' in scav and 'Physics.Raycast' in scav, 'Placement needs ground and wall checks'
    body = scav.split('public void Dispose()', 1)[1]
    assert 'UnityEngine.Object.Destroy(point.Box.gameObject)' in body, 'Session exit must destroy crates it owns'
    # 39 块世界空间 TMP 牌子不能全程常驻，必须按距离带滞回门控。
    assert 'private void UpdateLabels(Vector3 origin)' in scav, 'Crate labels must be distance gated'
    assert 'LabelShowRange = 45f' in scav and 'LabelHideRange = 55f' in scav, 'Label hysteresis band changed'
    assert 'point.Label.SetActive(visible)' in scav, 'Label gating must actually toggle the object'
    assert 'if (point.Label != null) point.Label.SetActive(false);' in scav,         'A freshly built label must start hidden and be opened by the gate'
    # 地上捡到的箱子不得开保底：十个星工遗存箱各保底一件高品质，一趟就发烂了。
    fill_call = scav.split('SkyIslandRewardCrate.Fill(', 1)[1].split(');', 1)[0]
    assert 'true' not in fill_call, 'Found loot must not request the guaranteed top band'


def check_enemy_tiers():
    tiers = source('SkyIslandEnemyTiers.cs')
    # 档次枚举单独放在无依赖文件里，隔离回归才能只链接它。
    enum_source = source('SkyIslandEnemyTier.cs')
    for name in ('Scav = 0', 'Elite = 1', 'Champion = 2', 'Storm = 3'):
        assert name in enum_source, 'Enemy tier ordinal changed: ' + name
    assert 'using ' not in enum_source, 'Tier enum file must stay dependency free'
    assert 'MaterialPropertyBlock' in tiers, 'Tint must go through a property block'
    assert 'renderer.GetPropertyBlock' in tiers and 'renderer.SetPropertyBlock' in tiers, \
        'Tint must be applied through the renderer property block'
    assert 'HasProperty(' in tiers, 'Tint must probe the material for a usable color property'
    assert re.search(r'renderer\.sharedMaterial\s*=', tiers) is None, 'Never assign sharedMaterial; it pollutes every same-model enemy'
    # 只缩放模型：角色 transform 不动，碰撞体与导航半径保持官方口径。
    assert 'character.characterModel.transform.localScale' in tiers, 'Tier scaling must target the model only'
    assert re.search(r'character\.transform\.localScale\s*=', tiers) is None, 'Never scale the character transform'
    assert 'Mathf.Min(DamageMultiplier(tier), 3f)' in tiers, 'Damage multiplier must stay capped at 3'
    assert 'character.Health.SetHealth(character.Health.MaxHealth)' in tiers, 'Health must be synced after raising the cap'
    # 三张倍率表必须**随档次严格递增**。历史教训：Champion（折翎、钟守）曾经是 2.2/1.3/1.25，
    # 比守航标的 Elite（2.6/1.35/1.3）还软——单挑的主线对手比路上的杂兵小队更弱，
    # 而当时守卫只钉了「伤害封顶 3」这条赋值语句在位，一个数字都没钉，于是倒挂无人察觉。
    # 这里解析方法体取出实际数字，口径同上面的品质带解析。
    multipliers = {}
    for accessor in ('HealthMultiplier', 'DamageMultiplier', 'ReactionSpeedup'):
        marker = 'internal static float %s(SkyIslandEnemyTier tier)' % accessor
        assert marker in tiers, 'Tier multiplier accessor missing: ' + accessor
        body = tiers.split(marker, 1)[1].split('\n        }', 1)[0]
        values = dict(re.findall(r'if \(tier == SkyIslandEnemyTier\.(\w+)\) return ([\d.]+)f;', body))
        default = re.search(r'\n\s*return ([\d.]+)f;', body)
        assert default, 'Tier multiplier %s has no default branch' % accessor
        values['Scav'] = default.group(1)
        multipliers[accessor] = {k: float(v) for k, v in values.items()}
    assert multipliers['HealthMultiplier'] == {'Scav': 1.0, 'Elite': 2.6, 'Champion': 4.5, 'Storm': 13.0}, \
        'Enemy health multipliers changed: %r' % multipliers['HealthMultiplier']
    assert multipliers['DamageMultiplier'] == {'Scav': 1.0, 'Elite': 1.35, 'Champion': 1.55, 'Storm': 1.8}, \
        'Enemy damage multipliers changed: %r' % multipliers['DamageMultiplier']
    assert multipliers['ReactionSpeedup'] == {'Scav': 1.0, 'Elite': 1.3, 'Champion': 1.45, 'Storm': 1.7}, \
        'Enemy reaction speedups changed: %r' % multipliers['ReactionSpeedup']
    for accessor, table in multipliers.items():
        ordered = [table['Scav'], table['Elite'], table['Champion'], table['Storm']]
        assert ordered == sorted(ordered) and len(set(ordered)) == len(ordered), \
            '%s must increase strictly with tier: %r' % (accessor, ordered)
    # Champion 保留自己的脸与名字，染色/放大会毁掉具名角色的辨识度。
    champion = tiers.split('internal static void Apply(', 1)[1].split('\n        }', 1)[0]
    assert 'decorate = tier != SkyIslandEnemyTier.Champion' in champion, 'Champions must skip appearance decoration'
    assert 'ApplyStoryChampion' in tiers, 'Named story foes need their own entry point'
    # HealthBar 有 `if (!characterPreset.showName) return;` 的门控，普通拾荒者 preset 上它是 false，
    # 只改 nameKey 血条上根本不显示。精英与 Boss 必须显式打开，Scav 保持匿名。
    assert 'if (tier != SkyIslandEnemyTier.Scav) character.characterPreset.showName = true;' in tiers, \
        'Renaming a tier without enabling showName leaves the health bar unchanged'
    # Scav 必须**不**改名：它的 showName 恒为 false，自定义名玩家根本看不到，
    # 但改 nameKey 会把击杀记到 Count/Kills/BossRush_SkyIsland_Enemy_Scav 下，
    # 官方拾荒者击杀数与 RequireEnemyKilled 解锁都不再推进。自动组按出击刷新之后这批击杀会反复产生。
    assert 'if (decorate && tier != SkyIslandEnemyTier.Scav)' in champion, \
        'Plain scavengers must keep the official name key so official kill counters still advance'
    # 数值层是乘法（血量 *=、反应时间 /=），重复施加会复利：18 倍血跑两遍就是 324 倍。
    # 这两条必须按**方法体**判断：只在整份源码里找 token，删掉一处另一处还在，子串仍会命中。
    assert 'MarkApplied(character)' in champion, 'Tier application must be idempotent'
    champ_body = tiers.split('internal static void ApplyStoryChampion(', 1)[1] \
        .split(chr(10) + '        }', 1)[0]
    assert 'MarkApplied(character)' in champ_body, 'Named champion application must be idempotent too'
    # AI 调参必须用自己的标记：AICharacterController 常常就挂在角色本体上，
    # 共用一个标记会让先跑的 ApplyAi 把后跑的 Apply 一起挡掉。
    apply_ai = tiers.split('internal static void ApplyAi(', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'GetComponent<SkyIslandEnemyAiMark>()' in apply_ai, \
        'AI tuning must be guarded against re-application'
    assert 'AddComponent<SkyIslandEnemyAiMark>()' in apply_ai, 'AI guard must actually set its mark'
    assert 'SkyIslandEnemyTierMark' not in apply_ai, \
        'AI tuning must not reuse the tier mark; it would suppress Apply on the same object'


def check_storm_boss():
    boss = source('SkyIslandStormBoss.cs')
    assert 'PhaseThresholds = { 0.80f, 0.60f, 0.40f, 0.20f }' in boss, 'Storm boss phase thresholds changed'
    assert 'PulseDamage = 38f' in boss and 'PulseRadius = 7f' in boss, 'Storm pulse tuning changed'
    assert 'PulseTelegraph = 1.4f' in boss, 'Storm pulse must stay telegraphed'
    # 相位数是这场 Boss 战的**唯一**节奏来源：只有两档就是「打很久的血包 + 两次特殊时刻」。
    thresholds = re.search(r'PhaseThresholds = \{([^}]*)\}', boss)
    assert thresholds, 'Phase threshold table must stay parseable'
    phases = [float(v.strip().rstrip('f')) for v in thresholds.group(1).split(',') if v.strip()]
    assert len(phases) >= 4, 'The storm needs at least four telegraphed moments, not a long health bar'
    assert phases == sorted(phases, reverse=True), 'Phase thresholds must be listed high to low'
    # 官方 ExplosionManager.CreateExplosion 没有距离衰减，圈内一律吃满伤：
    # 能不能逃掉完全由 半径 ÷ 预警秒数 决定。旧值 9m/1s 需要 9 m/s，贴脸的近战流数学上逃不掉。
    radius = float(re.search(r'PulseRadius = ([\d.]+)f', boss).group(1))
    telegraph = float(re.search(r'PulseTelegraph = ([\d.]+)f', boss).group(1))
    assert radius / telegraph <= 5.5, \
        'First pulse must be escapable at a normal run speed: %.2f m/s required' % (radius / telegraph)
    # 俯视视角下一盏点光源读不出 AoE 边界，而三波 38 伤害站在中心必死：预警必须是贴地圆环。
    assert 'CreateWarningRing' in boss and 'LineRenderer' in boss, 'Storm pulse needs a ground-projected ring'
    assert 'internal static float RadiusForWave(int wave)' in boss, \
        'Wave radius must live in one place so the ring and the damage cannot drift apart'
    assert 'SetRing(ring, RadiusForWave(0)' in boss, 'The telegraph ring must use the real wave radius'
    assert 'SetRing(ring, RadiusForWave(wave + 1), 1f)' in boss, \
        'Later waves must be telegraphed during the inter-wave gap'
    # 自建伤害三件套：不显式传就会打到玩家自己 / 被当成直接击杀起链。
    detonate = boss.split('private void Detonate(int wave)', 1)[1].split('\n        }', 1)[0]
    assert 'damage.isFromBuffOrEffect = true' in detonate, 'Storm pulse must use the buff/effect channel'
    assert 'damage.fromWeaponItemID = 0' in detonate, 'Storm pulse must not claim a weapon'
    assert 'ExplosionFxTypes.normal, 0f, false)' in detonate, 'Storm pulse must pass canHurtSelf:false'
    assert 'RadiusForWave(wave)' in detonate, 'Damage radius must come from the shared accessor'
    assert 'health.OnDeadEvent.AddListener(OnDead)' in boss and 'health.OnDeadEvent.RemoveListener(OnDead)' in boss, \
        'Boss death subscription must be paired'
    assert 'SkyIslandRewardCrate.Create' in boss, 'Trophy must reuse the shared crate builder'
    # Boss 战利品与委托谢礼是赚来的，必须开保底。
    assert 'TrophyItemCount, true)' in boss, 'Boss trophy must request the guaranteed top band'
    services = source('SkyIslandServices.cs')
    assert 'BountyRewardItemCount, true)' in services, 'Bounty reward must request the guaranteed top band'
    # 剧情事实挂在本体死亡上：否则杀了 Boss 留着随从离岛，下次还能再刷一次战利品。
    session = source('SkyIslandSession.cs')
    defeated = session.split('private void OnStormDefeated(Vector3 position)', 1)[1].split('\n        }', 1)[0]
    assert 'SkyIslandStoryAction.StormSlain' in defeated, \
        'Storm flag must be written when the boss itself dies, not only when the whole group is cleared'
    assert defeated.index('StormSlain') < defeated.index('DropTrophy'), \
        'Record the permanent fact before handing out the trophy'
    # 奖励箱不得与噬风自己的尸体箱同点（官方 OnDead 把尸体箱放在倒下位置 +0.1 m）：官方 CA_Interact
    # 按到交互体轴心的距离严格小于取唯一目标，两个箱子几乎重合时总有一个整局选不中（2026-09-10 全方位审核）。
    assert 'SkyIslandRewardCrate.TryFindCratePosition(' in defeated, \
        'The trophy must step away from the boss corpse lootbox'
    assert defeated.index('TryFindCratePosition(') < defeated.index('DropTrophy('), \
        'Placement must be resolved before the trophy is dropped'
    assert 'SkyIslandStormBoss.DropTrophy(root.transform, drop, raidSeed)' in defeated, \
        'The trophy must use the resolved position, not the raw death position'
    # CS1631：catch 子句体内不能 yield return。
    for block in re.findall(r'catch\s*\([^)]*\)\s*\{[^}]*\}', boss):
        assert 'yield return' not in block, 'yield return inside catch is CS1631'


def check_content_table():
    content = source('SkyIslandContent.cs')
    fallback = re.findall(
        r'Encounter\("([A-Za-z0-9_]+)",\s*"([A-Za-z0-9_]+)",\s*(\d+)'
        r'(?:,\s*(true|false))?(?:,\s*SkyIslandEnemyTier\.(\w+))?(?:,\s*SkyIslandEnemyTier\.(\w+))?\)',
        content)
    assert len(fallback) == len(EXPECTED_ENCOUNTERS), 'Fallback encounter count changed: %d' % len(fallback)
    parsed = {}
    for enc_id, marker, count, manual, tier, lead in fallback:
        tier = tier or 'Scav'
        parsed[enc_id] = (marker, int(count), manual == 'true', tier, lead or tier)
    assert parsed == EXPECTED_ENCOUNTERS, 'Fallback encounter table drifted:\n%r' % parsed

    # JSON 与内置表必须逐字段一致：运行时的严格校验会因不一致整表回退，玩家看不到任何报错。
    data = json.loads((ROOT / 'Assets/Data/SkyIsland/World.json').read_text(encoding='utf-8'))
    assert data['version'] == 1, 'World.json version changed without parser review'
    json_table = {}
    for entry in data['encounters']:
        assert set(entry) == {'id', 'marker', 'count', 'manual', 'tier', 'lead'}, \
            'World.json encounter fields must match the strict parser exactly'
        json_table[entry['id']] = (entry['marker'], entry['count'], entry['manual'], entry['tier'], entry['lead'])
    assert json_table == EXPECTED_ENCOUNTERS, 'World.json encounter table drifted:\n%r' % json_table
    assert 'TryParseTier' in content, 'Tier must be parsed from a stable string, not an enum ordinal'
    assert 'known.Tier != parsedTier || known.Lead != parsedLead' in content, 'Parser must verify tier bindings'


def check_story_contract():
    rules = source('SkyIslandStoryRules.cs')
    codec = source('SkyIslandStoryCodec.cs')
    session = source('SkyIslandSession.cs')
    assert 'StormSlain = 32768' in rules, 'Storm flag bit changed; save compatibility depends on it'
    assert 'KnownFlags = 65535' in rules, 'KnownFlags must cover the new bit or decoding rejects every save'
    assert 'SkyIslandStoryAction.StormSlain' in session, 'Storm clear must be recorded through the story funnel'
    # 噬风只在双航标点亮后到场，所以「已击败但航标没亮」必然是坏数据。
    assert 'data.Has(SkyIslandStoryFlag.StormSlain) && !data.BothBeacons' in codec, 'Codec must reject impossible storm state'
    assert '!source.StormResolved' in rules, 'Beating the storm must count as a bell keeper persuasion route'
    assert 'id == "Storm" && (!story.Current.BothBeacons || story.Current.StormResolved)' in session, \
        'Storm challenge must be gated on both beacons and be one-shot'
    assert 'SkyIslandStormBoss.DropTrophy' in session, 'Storm defeat must drop its trophy'


def check_services_and_bounty():
    services = source('SkyIslandServices.cs')
    bounty = source('SkyIslandBounty.cs')
    # 独立出击关卡不保证有 StockShopView，服务一律自带 UI。
    assert 'StockShopView' not in services and 'NPCShopSystem' not in services, \
        'Island services must not depend on the scene-local official shop UI'
    assert 'EconomyManager.IsEnough' in services and 'EconomyManager.Pay' in services, 'Paid services must pre-check then pay'
    assert services.index('EconomyManager.IsEnough') < services.index('EconomyManager.Pay'), \
        'Affordability must be checked before charging'
    # 维修必须走官方口径：按**物品价值**计价（旧的「缺口耐久 × 3」价值无关，
    # 盈亏平衡点在 Value = 6 × MaxDurability，高价值武器会被卖得比官方维修台便宜一到两个数量级），
    # 并且每修一次都要累加永久磨损，否则渡口就是「又便宜又无损」的修理站。
    assert 'RepairPricePerPoint' not in services, 'Flat per-point repair pricing is value-blind; use the official formula'
    assert 'itemValue * (repairAmount / maxDurability) * 0.5f' in services, \
        'Repair price must follow the official value-based formula'
    assert 'item.GetRepairLossRatio()' in services, 'Repair must read the official repair-loss ratio'
    assert 'entry.Item.DurabilityLoss += entry.LostPercentage;' in services, \
        'Repairing must accrue permanent durability loss exactly like the official bench'
    # 入列门与官方维修台 ItemRepairView.CanRepair 一致：Repairable（UseDurability 且带 Repairable 标签）、
    # 剩余上限不低于 1。只看 UseDurability 会把药品、食物这类「用耐久记剩余次数」的物品按维修价补满次数，
    # 官方维修台对它们显示「无法维修」（2026-09-10 全方位审核）。
    assert 'item.Repairable && item.MaxDurabilityWithLoss >= 1f' in services, \
        'Repair eligibility must match the official bench (Repairable tag and at least 1 durability cap left)'
    assert 'item.UseDurability && item.MaxDurability > 0f' not in services, \
        'UseDurability alone admits consumables that count uses through durability'
    # 账户门控读 LevelConfig.accountAvailable，**不是** SaveCharacter：
    # 后者是「是否把主角写回存档」，raid 图里同样为 true（天空岛的官方合同还硬性要求 true），
    # 拿它当账户门控恒真、等于没门控。accountAvailable 才是官方表达「账户在本图可用」的字段。
    assert 'EconomyManager.IsEnough(new Cost((long)price), true, true)' not in services, \
        'Paid services must read the account gate, not hard-code it'
    assert 'LevelConfig.Instance.AccountAvailable' in services, \
        'Account availability must follow LevelConfig.accountAvailable'
    assert 'LevelConfig.SaveCharacter' not in services, \
        'SaveCharacter is not an account gate; it is true on raid maps'
    assert services.count('bool account = AccountAvailable;') >= 2, 'Every paid island service needs the same gate'
    # 苔药必须按缺失血量比例计价，不能是定额。定额 120 + 全额回满 + 120 秒冷却，加上
    # accountAvailable 让银行存款在出击图里也能花，等于岛上根本没有血量压力。
    # 用比例而不是按血量点数，报价与玩家 MaxHealth 的实际量级无关。
    assert 'HealPrice = ' not in services, 'Flat heal pricing removes all attrition pressure from the raid'
    assert 'HealPriceFull = 480' in services, 'Full-restore heal price changed'
    assert 'HealPriceMinimum = 60' in services, 'Heal service floor changed'
    assert 'HealCooldown = 300f' in services, 'Heal cooldown changed'
    heal_price = services.split('internal static int HealPriceFor(float currentHealth, float maxHealth)', 1)[1] \
        .split(chr(10) + '        }', 1)[0]
    assert '(maxHealth - currentHealth) / maxHealth' in heal_price, \
        'Heal price must scale with the missing health fraction, not with absolute points'
    assert 'HealPriceFull * missing' in heal_price, 'Heal price must be derived from the full-restore price'
    heal = services.split('internal string Heal()', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'HealPriceFor(player.Health.CurrentHealth, player.Health.MaxHealth)' in heal, \
        'The heal service must quote through the shared pricing helper'
    # 免费的归航菜如果能回满血，眠苔那副付费苔药就永远没人买。
    meal = services.split('internal string Meal(bool plantingDelivered)', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'player.Health.SetHealth(player.Health.MaxHealth)' not in meal, \
        'The free meal must top up only the gained cap, not fully heal'
    # 晴禾是永久 NPC：与玩家结婚后由婚姻系统接管、不再上岛（SkyIslandResidents 跳过生成，
    # PermanentDuckNpcModule 对 SkyIslandRaid 恒返回 false）。归航菜若只挂在她身上就会永久失联。
    # 苇白的委托早有留言板兜底，这条纪律必须对称。
    world_story = source('SkyIslandWorldStory.cs')
    read_point = world_story.split('internal void ReadPoint(string key, Action recorded)', 1)[1] \
        .split('internal void Talk(', 1)[0]
    assert 'case "Search_C":' in read_point and 'Meal)' in read_point, \
        'The homecoming meal needs a world-device fallback for when Qinghe has married and left the island'
    assert 'RuntimeStatModifierTracker.TryAdd' in services, 'Buffs must be tracked'
    disposal = services.split('public void Dispose()', 1)[1]
    assert 'RuntimeStatModifierTracker.RemoveAll(records' in disposal, 'Raid buffs must be removed on session exit'
    assert 'WalkNodeBudget' in services and 'WalkDepthBudget' in services, 'Item tree walk must be bounded'
    assert '"MoveSpeed"' not in services and 'ZombieModeStatNames.MoveSpeed' not in services, \
        'MoveSpeed is an Animator parameter, not a character stat'
    # 委托按出击计，不进存档；写盘只能走剧情服务那一条。
    assert 'SavesSystem' not in bounty, 'Bounty state is per-raid and must not touch saves'
    assert 'baseline = Counter(kind)' in bounty, 'Progress must be measured from an accept-time baseline'
    assert 'BaseThreatTarget = 3' in bounty and 'BaseSalvageTarget = 4' in bounty and 'BaseSurveyTarget = 4' in bounty, \
        'Bounty base targets changed'
    assert 'StarworksFromRound = 3' in bounty, 'Bounty reward escalation changed'
    # 目标只线性增长而保底顶档恒定，没有上限就是可刷的。
    assert 'MaxRounds = 3' in bounty, 'A raid must cap how many contracts can be completed'
    assert 'completedRounds < MaxRounds' in bounty, 'The round cap must actually gate accepting'
    # 退单是「接了才发现做不完」的唯一出口：清场与巡岛的计数源都是持久存档事实，
    # 老档上可能一件都不剩，没有退单就等于把本局委托槽永久卡死。
    abandon = bounty.split('internal bool TryAbandon(out string message)', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'active = SkyIslandBountyKind.None;' in abandon, 'Abandoning must free the contract slot'
    assert 'baseline = 0;' in abandon, 'Abandoning must reset the baseline so re-accepting rebaselines'
    assert 'completedRounds++' not in abandon, 'Abandoning must not count as a completed round'
    # 原设计要求奖励送达与状态推进分开：送不出去时委托必须原样保留。
    claim = bounty.split('internal bool TryClaim(', 1)[1].split('\n        }', 1)[0]
    assert 'if (deliver != null && !deliver(reward))' in claim, 'Claim must deliver before consuming'
    assert claim.index('deliver(reward)') < claim.index('completedRounds++'), \
        'Delivery must be attempted before the round is consumed'
    story = source('SkyIslandWorldStory.cs')
    assert 'contract.TryClaim(reward => session.DropBountyReward(position, reward, round)' in story, \
        'The claim UI must pass the delivery callback, not consume then drop'
    # 谢礼箱不能落在交单锚点上：锚点是苇白本人或风铃集留言板，直接生成会把箱子塞进 NPC/告示牌里
    # 抢同一次交互选择；而且三轮委托的锚点完全相同，箱子会一摞叠在一处，只有最上面那个按得到。
    assert 'BountyRewardBearingStep = 120f' in services, 'Bounty crates must fan out per round'
    assert 'BountyRewardDistance = 3f' in services, 'Bounty crates must stand clear of the hand-in anchor'
    drop = services.split('internal bool DropBountyReward(Vector3 position, SkyIslandLootTier tier, int round)', 1)[1] \
        .split(chr(10) + '        }', 1)[0]
    assert 'BountyRewardBearing + round * BountyRewardBearingStep' in drop, \
        'Each contract round must use its own bearing so crates never stack'
    assert 'SkyIslandRewardCrate.TryFindCratePosition' in drop, \
        'Bounty crates must reuse the shared standable-placement probe'
    # 只派做得完的单：Threats/Survey 的计数源是持久存档事实，老档上可能一件都不剩。
    assert 'session.AvailableBountyProgress(kind) < target' in story, \
        'Contracts must be gated on how much progress this raid can still produce'
    assert 'contract.TryAbandon(out message)' in story, 'The player must always be able to drop a contract'
    assert 'contract.CanAcceptMore' in story, 'The dispatch UI must respect the per-raid round cap'

    # 可完成量必须真的读三个不同寿命的信号源，否则门控是摆设。
    session_src = source('SkyIslandSession.cs')
    available = session_src.split('internal int AvailableBountyProgress(SkyIslandBountyKind kind)', 1)[1] \
        .split(chr(10) + '        }' + chr(10), 1)[0]
    assert 'scavenging.AvailablePoints' in available, 'Salvage availability must come from the scavenging owner'
    assert 'encounters.RemainingClearable' in available, 'Threat availability must exclude already-saved clears'
    assert 'story.HasVisitedRegion(' in available, 'Survey availability must exclude already-visited regions'
    # 数区域不数 POI 节点：场景包里还有装饰节点 POI_B_Mural，按节点数会把它算成永远去不了的第 13 个区域，
    # 剩 3 个真区域时可完成量算成 4，恰好派得出一张做不完的「巡视群岛区域 ×4」（2026-09-10 全方位审核）。
    survey = available.split('SkyIslandBountyKind.Survey', 1)[1]
    assert 'regionIds' in survey and 'landmarks' not in survey, \
        'Survey availability must count ground regions, not POI nodes (POI_B_Mural is not a region)'
    encounters_src = source('SkyIslandEncounters.cs')
    remaining = encounters_src.split('internal int RemainingClearable', 1)[1].split(chr(10) + '        }', 1)[0]
    # 自动组按出击刷新（短路只留给手动组），所以**不能**再用存档事实过滤可完成量：
    # 沿用旧的 !completed(id) 会让第二次进岛起可完成量恒为 0，「清理航路威胁」永远派不出来。
    assert 'completed(' not in remaining, \
        'Auto encounters respawn each raid; filtering by saved clears zeroes contract availability on revisits'
    assert '!encounter.Manual' in remaining, 'Manual encounters need story prerequisites; do not over-count them'


def check_encounter_refresh():
    """自动组按出击刷新、手动组一次性——这张图能不能算出击图，全押在这一条上。

    历史：所有遭遇（含 13 组自动组）都靠存档 `clearedEncounters` 永久抑制生成，而 39 个
    搜刮点每趟重刷。于是第二次进岛起全岛零敌人、约百件产出（含 ~25 件品质 4–8）无限重复，
    这张 raid 图退化成无风险刷宝台。编译和当时的守卫都查不出来。
    """
    encounters = source('SkyIslandEncounters.cs')
    tick = encounters.split('internal void Tick()', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'if (!encounter.Started && encounter.Manual && completed(encounter.Id))' in tick, \
        'Saved clears may only suppress manual (named story) encounters; auto groups must respawn each raid'
    # 反例：没有 encounter.Manual 这一项就等于全组永久静音。
    assert 'if (!encounter.Started && completed(encounter.Id)) {' not in tick, \
        'The unconditional short-circuit is what emptied the island on every revisit'
    # 具名剧情对手仍必须是一次性的：折翎/钟守/噬风打完就不该再出现。
    session = source('SkyIslandSession.cs')
    assert 'id == "Storm" && (!story.Current.BothBeacons || story.Current.StormResolved)' in session, \
        'The storm must stay one-shot'
    begin = encounters.split('internal bool BeginChallenge(string id)', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'completed(id)' in begin, 'Manual challenges must still be blocked by the saved fact'
    # 剧情装置的前置读的仍是同一份持久事实，重刷的敌群不会把已完成的装置重新锁上。
    rules = source('SkyIslandStoryRules.cs')
    for region in ('D', 'G'):
        assert 'source.EncounterCleared("' + region + '")' in rules, \
            'Beacon prerequisites must keep reading the persistent clear fact: ' + region
    # 战斗探测按半径，不是全图。
    within = encounters.split('internal bool HasLivingEnemiesWithin(Vector3 point, float radius)', 1)[1] \
        .split(chr(10) + '        }', 1)[0]
    assert 'actor.Life.transform.position' in within, 'Proximity must use the live actor position'
    assert 'sqrMagnitude <= squared' in within, 'Proximity must compare squared distances, not call Distance per actor'


def check_session_ownership():
    session = source('SkyIslandSession.cs')
    for token in ('scavenging.Tick()', 'scavenging.Dispose()', 'services.Dispose()',
                  'bounty.ReportEncounterCleared()', 'bounty.ReportScavenged', 'bounty.ReportRegionVisited()'):
        assert token in session, 'Session must own the expansion wiring: ' + token
    # 清场回调在存档接受前会每秒重投同一个 id；委托记账必须按 id 幂等，
    # 否则一次延迟保存就把一单委托刷完。区域记账靠 RecordRegionVisited 的新位语义天然幂等。
    assert 'if (bountyCredited.Add(id)) bounty.ReportEncounterCleared();' in session, \
        'Encounter credit must be idempotent against the clear-callback retry loop'
    # 到访记账与区域名共用「脚下那块地」这一个事实源（2026-09-10 全方位审核）。
    # 旧口径「离最近地标 60 米」按作者布局的导航网格复算：主岛上只罩住 30–53% 的可走面积，
    # CS1 / FS3 两座桥的大半段与 C / F 两岛边缘却能提前点亮 S1 / S3 的迷雾并推进「巡视群岛区域」。
    # 几何本身由 tests/SkyIslandRegionResolutionPropertyTest.py 复算并反向验证；这里钉接线。
    assert (ROOT / 'tests/SkyIslandRegionResolutionPropertyTest.py').exists(), \
        'The ground-region geometry property test is missing'
    # 按结构判断而不是钉一行字面量：记账必须落在「这次才第一次记下这个区域」的分支里。
    assert 'story.RecordRegionVisited(standingRegion)' in session, \
        'Region visit must go through the story service with the ground region the player stands on'
    visit_branch = session.split('story.RecordRegionVisited(standingRegion)', 1)[1]
    visit_branch = visit_branch.split(chr(10) + '                }', 1)[0]
    assert 'bounty.ReportRegionVisited();' in visit_branch, \
        'Region credit must only fire on a newly recorded region'
    assert session.count('RecordRegionVisited(') == 1, 'There must be exactly one region-visit credit point'
    update = session.split('private void Update()', 1)[1].split('private bool HudSuppressed()', 1)[0]
    assert 'Nearest(landmarks' not in update, 'Region resolution must not fall back to the nearest landmark'
    index = session.split('private void IndexGroundRegions()', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'SkyIslandStoryService.GroundRegionOf(collider.name)' in index, \
        'Ground regions must be derived from the generator collider naming (COL_Ground_<region>)'
    assert 'groundRegions[collider] = id;' in index and 'regionIds.Add(id)' in index, \
        'The region index must record both the collider map and the region list'
    build = session.split('PrepareMarkers();', 1)[1].split('root.SetActive(true);', 1)[0]
    assert 'IndexGroundRegions();' in build, 'Ground regions must be indexed while the world is assembled'
    story_service = source('SkyIslandStoryService.cs')
    region_of = story_service.split('internal static string GroundRegionOf(string colliderName)', 1)[1] \
        .split(chr(10) + '        }', 1)[0]
    assert 'const string prefix = "COL_Ground_";' in region_of and 'RegionBit(id) == 0 ? null : id' in region_of, \
        'Only colliders named after a real region (A-H, S1-S4) may count; bridges must resolve to null'
    # 折翎的战斗实例用的就是他自己的脸和名字：战败后把剧情体放回原地，玩家会看到
    # 刚打死的人站在自己的尸体和掉落箱旁边，头顶还挂着「聊聊航路」。
    #
    # 判据必须是「本局是否打响过」这一个事实源。用 `!IsBusy && !ZhelingDefeated` 不行：
    # 两个条件延迟不同——最后一名倒下的那一帧 IsBusy 就转 false，而持久 flag 要等
    # encounters.Tick（0.25 s 节流）提交并被存档接受；存档有写屏障时 flag 永远落不下来，
    # 那就是持久可见（CR-2026-09-09-013）。
    assert 'residents.SetVisible("sky_zheling", !ZhelingDefeated && !HasStoryChallengeStarted("Zheling"));' in session, \
        'A defeated Zheling must not walk back as a talkable resident'
    assert 'IsStoryChallengeActive("Zheling")' not in session.split('residents.SetVisible("sky_zheling"', 1)[1].split(';', 1)[0], \
        'Zheling visibility must not depend on the frame-latency IsBusy query'
    enc_src = source('SkyIslandEncounters.cs')
    assert 'internal bool HasStarted(string id)' in enc_src, \
        'The one-shot "was this group ever fought" fact must live in the encounter owner'
    assert 'encounter.Started || encounter.Cleared' in enc_src, \
        'HasStarted must also cover the save-driven one-shot close, not just this run'
    # 每帧路径不得用闭包 Find：IsBusy/HasStarted 每帧各问两次，闭包捕获等于每帧产生垃圾。
    assert 'encounters.Find(e =>' not in enc_src and 'encounters.Exists(e =>' not in enc_src, \
        'Per-frame encounter lookups must not allocate a closure (AGENTS 4.12)'
    # 面板会把 timeScale 压到 0，而这个暂停是可靠的（ModBehaviour.LateUpdate 排在官方
    # TimeScaleManager.Update 之后）。没有战斗门，搜索点/居民/纪念物就都是战斗中的暂停键，
    # 而且面板里还挂着苔药与整备，等于可以定格战斗再花钱回满血。
    assert 'internal bool CanOpenStoryPanel(out string reason)' in session, \
        'Story panels need a combat gate; the modal pause is reliable and would otherwise be a free pause button'
    gate = session.split('internal bool CanOpenStoryPanel(out string reason)', 1)[1] \
        .split(chr(10) + '        }', 1)[0]
    assert 'encounters.HasLivingEnemiesWithin(' in gate, 'The combat gate must ask the encounter owner'
    assert 'StoryPanelQuietRadius' in gate, 'The quiet radius must be a named constant, not a literal'
    # 按半径而不是全图：把一组敌人丢在岛的另一头不该让全岛剧情交互永久失效。
    assert 'HasLivingEnemies)' not in gate, 'A whole-island check would deadlock the story on abandoned enemies'
    story_src = source('SkyIslandWorldStory.cs')
    assert 'private bool BlockedByCombat()' in story_src, 'The gate must have one shared entry point'
    for entry in ('internal void ReadPoint(string key, Action recorded)', 'internal void Talk(string id, Transform speaker)'):
        body = story_src.split(entry, 1)[1].split(chr(10) + '        }', 1)[0]
        assert 'if (BlockedByCombat()) return;' in body, 'Story entry point missing the combat gate: ' + entry
    tick = story_src.split('internal void Tick()', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'presentation.Dispose();' in tick, \
        'An open panel must close itself when combat starts (async spawns finish at timeScale 0)'
    module = source('SkyIslandRuntimeModule.cs')
    for reset in ('SkyIslandLootPools.ResetStaticCaches()', 'SkyIslandEnemyTiers.ResetStaticCaches()',
                  'SkyIslandStormBoss.ResetStaticCaches()'):
        assert reset in module, 'Static caches must be released by the module OnDestroy owner: ' + reset


def check_registration():
    # 新增 .cs 不进编译清单会静默不参与编译（AGENTS 4.1）。
    bat = (ROOT / 'compile_official.bat').read_text(encoding='utf-8', errors='ignore')
    for name in NEW_SOURCES:
        assert (SKY / name).exists(), 'Missing expansion source: ' + name
        assert 'DebugAndTools\\SkyIsland\\' + name in bat, 'Source not registered in compile list: ' + name
    # 共享排除口径也是新增 .cs，同样必须登记，否则静默不参与编译。
    assert (ROOT / 'Config/LootExcludeTagPolicy.cs').exists(), 'Missing shared loot exclusion policy'
    assert 'Config\\LootExcludeTagPolicy.cs' in bat, \
        'Shared loot exclusion policy not registered in compile list'


def main():
    check_loot_anchors()
    check_pools()
    check_crate()
    check_scavenging()
    check_enemy_tiers()
    check_storm_boss()
    check_content_table()
    check_story_contract()
    check_services_and_bounty()
    check_encounter_refresh()
    check_session_ownership()
    check_registration()
    print('PASS SkyIslandContentExpansionGuard')


if __name__ == '__main__':
    main()
