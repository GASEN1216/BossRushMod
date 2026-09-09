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
    'E': ('EnemySpawn_E', 2, False, 'Scav', 'Scav'),
    'E_02': ('Search_E_02', 2, False, 'Scav', 'Scav'),
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
    # InstantiateSync 缺资源时返回同 TypeID 的空壳 FallbackItem，既不为 null 也不抛。
    assert 'item.TypeID != typeId' in crate, 'Crate fill must read TypeID back to reject FallbackItem shells'
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
    # Champion 保留自己的脸与名字，染色/放大会毁掉具名角色的辨识度。
    champion = tiers.split('internal static void Apply(', 1)[1].split('\n        }', 1)[0]
    assert 'decorate = tier != SkyIslandEnemyTier.Champion' in champion, 'Champions must skip appearance decoration'
    assert 'ApplyStoryChampion' in tiers, 'Named story foes need their own entry point'
    # HealthBar 有 `if (!characterPreset.showName) return;` 的门控，普通拾荒者 preset 上它是 false，
    # 只改 nameKey 血条上根本不显示。精英与 Boss 必须显式打开，Scav 保持匿名。
    assert 'if (tier != SkyIslandEnemyTier.Scav) character.characterPreset.showName = true;' in tiers, \
        'Renaming a tier without enabling showName leaves the health bar unchanged'
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
    assert 'PhaseThresholds = { 0.66f, 0.33f }' in boss, 'Storm boss phase thresholds changed'
    assert 'PulseDamage = 38f' in boss and 'PulseRadius = 9f' in boss, 'Storm pulse tuning changed'
    assert 'PulseTelegraph = 1f' in boss, 'Storm pulse must stay telegraphed'
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
    # 免费的归航菜如果能回满血，眠苔那副付费苔药就永远没人买。
    meal = services.split('internal string Meal(bool plantingDelivered)', 1)[1].split(chr(10) + '        }', 1)[0]
    assert 'player.Health.SetHealth(player.Health.MaxHealth)' not in meal, \
        'The free meal must top up only the gained cap, not fully heal'
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
    encounters_src = source('SkyIslandEncounters.cs')
    remaining = encounters_src.split('internal int RemainingClearable', 1)[1].split(chr(10) + '        }', 1)[0]
    # 已存档清场的组在 Tick 里被短路成 Cleared，cleared() 回调根本不会触发，必须排除。
    assert '!completed(encounter.Id)' in remaining, 'Saved-cleared encounters can never credit a contract again'
    assert '!encounter.Manual' in remaining, 'Manual encounters need story prerequisites; do not over-count them'


def check_session_ownership():
    session = source('SkyIslandSession.cs')
    for token in ('scavenging.Tick()', 'scavenging.Dispose()', 'services.Dispose()',
                  'bounty.ReportEncounterCleared()', 'bounty.ReportScavenged', 'bounty.ReportRegionVisited()'):
        assert token in session, 'Session must own the expansion wiring: ' + token
    # 清场回调在存档接受前会每秒重投同一个 id；委托记账必须按 id 幂等，
    # 否则一次延迟保存就把一单委托刷完。区域记账靠 RecordRegionVisited 的新位语义天然幂等。
    assert 'if (bountyCredited.Add(id)) bounty.ReportEncounterCleared();' in session, \
        'Encounter credit must be idempotent against the clear-callback retry loop'
    # 按结构判断而不是钉一行字面量：记账必须落在「这次才第一次记下这个区域」的分支里。
    assert 'story.RecordRegionVisited(nearest.name.Substring(4))' in session, \
        'Region visit must go through the story service'
    visit_branch = session.split('story.RecordRegionVisited(nearest.name.Substring(4))', 1)[1]
    visit_branch = visit_branch.split(chr(10) + '                }', 1)[0]
    assert 'bounty.ReportRegionVisited();' in visit_branch, \
        'Region credit must only fire on a newly recorded region'
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
    check_session_ownership()
    check_registration()
    print('PASS SkyIslandContentExpansionGuard')


if __name__ == '__main__':
    main()
