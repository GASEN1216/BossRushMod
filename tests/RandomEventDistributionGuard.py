"""奖励事件在首抽与排除任一上次事件后都低于 10%，新增战场事件必须入池。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
tuning = clean_source((ROOT / 'RandomEvents/RandomEventsTuning.cs').read_text(encoding='utf-8-sig'))
weights = {name: float(value) for name, value in re.findall(r'const float Weight(\w+) = ([\d.]+)f;', tuning)}
assert len(weights) == 11 and all(value > 0 for value in weights.values()), '全部事件必须有正权重'
for excluded in [None, *weights]:
    total = sum(value for name, value in weights.items() if name != excluded)
    for reward in ('AirdropSupply', 'GoldenDuckRain'):
        if excluded != reward:
            assert weights[reward] / total < 0.1, f'{reward} 在排除 {excluded} 后奖励概率过高'
catalog = clean_source((ROOT / 'RandomEvents/RandomEventCatalog.cs').read_text(encoding='utf-8-sig'))
tempo = clean_source((ROOT / 'RandomEvents/RandomEventTempo.cs').read_text(encoding='utf-8-sig'))
for event in ('WildChase', 'MeleeCarnival', 'HeavySteps'):
    assert catalog.count(f'new RandomEventTempo(RandomEventId.{event})') == 1, event + ' 必须入池一次'
assert 'ctx.Scope.RegisterCleanup(ClearModifiers);' in tempo, '触发失败也必须回收临时属性'
assert 'RuntimeStatModifierTracker.RemoveAll(_records, "RandomEventTempo");' in tempo, '事件结束必须移除属性'
def body(source, signature):
    assert source.count(signature) == 1, '方法缺失或重复: ' + signature
    start = source.index('{', source.index(signature)); end, depth = start + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return re.sub(r'\s+', '', source[start + 1:end - 1])

trigger = body(tempo, 'internal override bool OnTrigger(')
refresh = body(tempo, 'private bool RefreshPlayer(')
tick = body(tempo, 'internal override void OnTick(')
assert 'if(!RefreshPlayer())returnfalse;' in trigger, '触发必须确认两项真实玩家属性挂载成功'
assert 'if(_playerModifiers==2)returntrue;' in refresh, '必须确认两项真实属性挂载成功'
assert 'RuntimeStatModifierTracker.RemoveAll(_playerRecords,"RandomEventTempopartialplayer");returnfalse;' in refresh, '缺失任一属性时必须撤销半成品'
assert 'RefreshPlayer();' in tick, '重生或物品换绑必须重新接入本机临时属性'
assert 'RuntimeStatModifierTracker.RemoveAll(_playerRecords, "RandomEventTempo player");' in tempo, '个人属性必须成对清理'
bridge = clean_source((ROOT / 'RandomEvents/RandomEventEffectsBridge.cs').read_text(encoding='utf-8-sig'))
anchor = body(catalog, 'internal static CharacterMainControl ResolveAlivePlayer()')
assert 'foreach(BossRushCoopPlayermemberinBossRushCoop.Players)' in anchor and 'UnityEngine.Random.Range(0,++candidates)' in anchor, 'All living participants must be eligible without origin anchor bias'
safe = body(bridge, 'internal Vector3 GetRandomEventSafePointAwayFromPlayer(')
assert 'RandomEventEffectHelpers.ResolveAlivePlayer()' in safe and 'IsRandomEventPointSafeForParty(point,minDistance)' in safe, 'Intrusion anchors and safe presets must consider the actual party'
party = body(bridge, 'private static bool IsRandomEventPointSafeForParty(')
assert 'foreach(BossRushCoopPlayermemberinBossRushCoop.Players)' in party and 'delta.sqrMagnitude<minDistance*minDistance' in party, 'Every alive guest has the original intrusion minimum distance'
print('RandomEventDistributionGuard: PASS')
