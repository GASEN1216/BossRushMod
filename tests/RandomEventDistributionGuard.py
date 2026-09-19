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
assert '_playerModifiers != 2' in tempo, '必须确认两项真实属性挂载成功'
print('RandomEventDistributionGuard: PASS')
