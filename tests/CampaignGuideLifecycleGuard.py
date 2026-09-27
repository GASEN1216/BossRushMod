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
    assert 'CanWrite() && CampaignGuideTable.InBase() && CampaignProgressService.TryDeliverGuide(guideId, rewardCash)' in client, 'stale delivery callback needs same gate'
    guide_deliver = progress.split('internal static bool TryDeliverGuide(', 1)[1].split('#endregion', 1)[0]
    assert '!CampaignPersistence.IsGuideExperienced(guideId)' in guide_deliver, 'guide delivery must require the experienced fact'
    assert 'CampaignSaveCoordinator.TryPrepareCashReward()' in guide_deliver and 'EconomyManager.Pay(new Cost((long)rewardCash), true, true)' in guide_deliver, 'guide cash must follow the chapter snapshot/refund pattern'
    assert guide_deliver.index('EconomyManager.Add(rewardCash)') < guide_deliver.index('CampaignPersistence.TryAdvanceGuide(guideId, 3)'), 'pay before writing the delivery fact so a failed write can refund'
    assert '_cashPaidPendingGuideId = guideId;' in guide_deliver, 'paid-but-unwritten retries must not pay twice'
    rewards = read('Campaign/CampaignRewardTable.cs')
    for name in ('ModeD','ModeE','ModeF','ModeG','ModeH','Zombie','PetNest','RandomEvents','SkyIslandGear','Garden','Trophy','AffixForge','Reforge','DailyReport'):
        assert f'case CampaignGuideTable.{name}:' in rewards, f'guide {name} has no reward entry'
    assert 'RewardItems = grant.Items,' in client and 'Submissions = grant.Submissions,' in client and 'RewardMoney = cash,' in client
    assert 'RewardItems = CampaignRewardTable.ChapterItems(def.Order),' in client and 'Submissions = CampaignRewardTable.ChapterSubmissions(def.Order),' in client
    assert 'main.CharacterItem.GetAllChildren(true, true)' in facts, 'equipment objective must examine owned equipment'
    assert 'SkyIslandBossRules.GearSpec(item.TypeID) != null' in facts, 'visiting island does not prove gear'
    assert 'owner.ModeHRuntime.HasCompletedMatch' in facts, 'guide requires a completed match, not merely starting combat'
    match = read('ModeH/ModeHRuntimeModule_PreparedLoadouts.cs')
    assert 'report.reportStatus == (int)ModeHMatchReportStatus.SettledPendingArchive' in match
    assert 'report.reportStatus == (int)ModeHMatchReportStatus.Archived' in match
    assert 'foreach (CampaignGuideTable.Definition guide in CampaignGuideTable.Definitions)' in loc
    print('CampaignGuideLifecycleGuard: PASS')

if __name__ == '__main__':
    main()
