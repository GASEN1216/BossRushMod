"""Jeff 入门任务持久化、达标与交付的边界接线。实际存档转换由 ContentTransactions 执行。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
def read(path):
    return re.sub(r'\s+', ' ', clean_source((ROOT / path).read_text(encoding='utf-8-sig')))

def main():
    table = read('Campaign/CampaignGuideTable.cs')
    client = read('Campaign/CampaignOfficialQuestClient.cs')
    save = read('Campaign/CampaignPersistence.cs')
    progress = read('Campaign/CampaignProgressService.cs')
    facts = read('Campaign/CampaignGuideFacts.cs')
    loc = read('Localization/CampaignLocalization.cs')
    for name in ('ModeD','ModeE','ModeF','ModeG','ModeH','Zombie','PetNest','RandomEvents','SkyIslandGear','Garden','Trophy','AffixForge','Reforge','DailyReport'):
        assert f'internal const string {name} =' in table, f'missing guide {name}'
        assert f'case CampaignGuideTable.{name}:' in facts, f'guide has no observable objective {name}'
    for field in ('acceptedGuides','experiencedGuides','completedGuides'):
        assert f'public string[] {field};' in save, f'missing persisted {field}'
        assert f'if (decoded.{field} == null) decoded.{field} = new string[0];' in save, f'old saves miss {field} default'
        assert f'(string[])source.{field}.Clone()' in progress, f'chapter transaction drops {field}'
    assert 'CampaignSaveData copy = CampaignProgressService.CloneSaveData(current);' in save
    assert 'if (!Store(copy)) return false;' in save
    assert 'IsDelivered = () => CampaignGuideTable.IsCompleted(id),' in client, 'objective must not auto-deliver guide'
    assert 'Done = () => CampaignGuideTable.IsExperienced(id) || CampaignGuideTable.IsCompleted(id),' in client
    assert 'CanDeliver = () => CanWrite() && CampaignGuideTable.InBase() && CampaignGuideTable.IsAccepted(id) && CampaignGuideTable.IsExperienced(id) && !CampaignGuideTable.IsCompleted(id),' in client
    assert 'CanWrite() && CampaignGuideTable.InBase() && CampaignPersistence.TryAdvanceGuide(guideId, 3)' in client, 'stale delivery callback needs same gate'
    assert 'main.CharacterItem.GetAllChildren(true, true)' in facts, 'equipment objective must examine owned equipment'
    assert 'SkyIslandBossRules.GearSpec(item.TypeID) != null' in facts, 'visiting island does not prove gear'
    assert 'owner.ModeHRuntime.IsMatchInProgress' in facts, 'selecting fighters is not a started match'
    assert 'foreach (CampaignGuideTable.Definition guide in CampaignGuideTable.Definitions)' in loc
    print('CampaignGuideLifecycleGuard: PASS')

if __name__ == '__main__':
    main()
