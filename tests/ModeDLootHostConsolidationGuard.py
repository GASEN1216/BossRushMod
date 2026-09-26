"""D/loot carriers keep existing owners, eager field initialization and independent types."""
from pathlib import Path
import re

from cs_source_util import clean_source
from integration_host_source import host_region

ROOT = Path(__file__).resolve().parents[1]
GROUPS = {
    'ModeD/ModeD.cs': ['ModeD', 'ModeDStaticCacheReset', 'ModeDEquipmentHostBridge',
                     'ModeDWaves', 'ModeDInteractables', 'ModeDGlobalLootStaticCacheReset'],
    'LootAndRewards/LootAndRewards.cs': ['LootAndRewards', 'LootAndRewardsInfiniteHell',
        'LootAndRewardsVictoryRewards', 'LootAndRewardsRandomBossLoot', 'LootAndRewardsSpecialLoot',
        'LootAndRewardsRuntimeHooks', 'ModeEFLootboxTracker'],
}


def read(path):
    return clean_source((ROOT / path).read_text(encoding='utf-8-sig'))


def compact(source):
    return re.sub(r'\s+', '', source)


def main():
    for carrier, regions in GROUPS.items():
        folder = ROOT / Path(carrier).parent
        partials = {p.relative_to(ROOT).as_posix() for p in folder.glob('*.cs')
                    if re.search(r'\bpartial\s+class\s+ModBehaviour\b', clean_source(p.read_text(encoding='utf-8-sig')))}
        assert partials == {carrier}, 'exactly one host carrier required in ' + folder.name
        raw = (ROOT / carrier).read_text(encoding='utf-8-sig')
        assert len(raw.splitlines()) <= 1200, 'host carrier must stay within 1200 lines: ' + carrier
        assert 'public partial class ModBehaviour : Duckov.Modding.ModBehaviour' in read(carrier), 'public host type/base must remain unchanged'
        positions = []
        for name in regions:
            assert clean_source(host_region(ROOT, name, carrier)).strip(), 'empty compatibility region: ' + name
            positions.append(raw.index('#region ' + name + '\n'))
        assert positions == sorted(positions), 'host regions must retain former compile-source order: ' + carrier

    mode_d = read('ModeD/ModeD.cs')
    assert 'using SharedModeEnemyEquipmentMaterializationPlan = BossRush.ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan;' in mode_d, 'equipment plan alias must keep the nested runtime type'
    assert compact('public bool ModeDStartNextWave() { return modeDRuntime.ModeDStartNextWave(); }') in compact(mode_d), 'wave entry must keep runtime forwarding'
    assert compact('private static void ResetModeDGlobalLootStaticCaches() { ModeDItemPool.ResetGlobalLootStaticCaches(); }') in compact(mode_d), 'global pool reset must keep runtime forwarding'
    interactable = read('ModeD/ModeDInteractables.cs')
    assert 'public class ModeDNextWaveInteractable : InteractableBase' in interactable, 'independent interactable must retain public type identity'
    assert 'partial class ModBehaviour' not in interactable, 'interactable file must retain only independent types'
    assert 'bool success = mod.ModeDStartNextWave();' in interactable, 'real interaction must still enter the public host bridge'

    loot = read('LootAndRewards/LootAndRewards.cs')
    declaration = 'private readonly AwenLootSweepRuntime awenLootSweepRuntime = new AwenLootSweepRuntime();'
    assert loot.count(declaration) == 1, 'Awen runtime must retain one private readonly eager initializer'
    tracker = read('LootAndRewards/ModeEFLootboxTracker.cs')
    assert 'partial class ModBehaviour' not in tracker, 'tracker types must remain independent of the host carrier'
    assert compact('public enum BossRushTrackedLootboxMode { None = 0, ModeE = 1, ModeF = 2 }') in compact(tracker), 'published tracked-mode enum identity and values must remain unchanged'
    assert compact('internal sealed class AwenLootSweepTarget { public InteractableLootbox Lootbox; public Vector3 VisitPosition; }') in compact(tracker), 'sweep target type/field identities must remain unchanged'
    sweep = clean_source(host_region(ROOT, 'ModeEFLootboxTracker', 'LootAndRewards/LootAndRewards.cs'))
    assert declaration in sweep, 'Awen initializer must stay with its complete host region'
    assert sweep.index(declaration) < sweep.index('private void BindAwenLootSweepRuntime()'), 'Awen eager declaration must precede its existing binding entry'
    assert 'public enum BossRushTrackedLootboxMode' not in loot and 'class AwenLootSweepTarget' not in loot, 'independent tracker types must not move into the carrier'
    print('ModeDLootHostConsolidationGuard: PASS')


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        print('ModeDLootHostConsolidationGuard: FAIL - ' + str(error))
        raise SystemExit(1)
