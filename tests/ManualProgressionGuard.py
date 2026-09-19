"""人工快速推进只能从 Dev 的专用测试槽入口调用真实业务服务。"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
source = (ROOT / 'DebugAndTools/CampaignPetNestDebugControls.cs').read_text(encoding='utf-8-sig')
assert source.strip().startswith('#if BOSSRUSH_DEV') and source.strip().endswith('#endif'), '整个手动演练必须隔离到 Dev'
button = source[source.index('private static void Button('):source.index('private static TextMeshProUGUI Label(')]
assert button.index('CanRunManualProgression(host, out reason)') < button.index('report(action(), false)'), '动作执行前必须过槽位门'
runner = (ROOT / 'DebugAndTools/F3GameplayValidationRunner.cs').read_text(encoding='utf-8-sig')
gate = runner[runner.index('internal static bool CanRunManualProgression('):]
gate = gate[:gate.index('#endif')]
for check in ('!ModBehaviour.DevModeEnabled', '!IsDedicatedCurrentSlot()', '_instance._running', '!IsBaseScene()', 'SavesSystem.IsSaving', 'ValidationHasActiveMode'):
    assert check in gate, '快速推进门缺少 ' + check
for api in ('TryAcceptContract', 'EnsureArmedFor', 'ReportStandardClear', 'ReportPlayerKill', 'ReportFinalBossKill', 'TryDeliver',
            'TryHatchEgg', 'TryCondenseAndHatch', 'TrySetDeployedPet', 'TryDepart', 'SettleDueExpeditions', 'TryGrantPendingRewards'):
    assert re.search(r'\b' + api + r'\(', source), '缺少真实业务调用 ' + api
assert 'TryUnlock(' not in source and 'SavesSystem.Save' not in source, '不允许绕过生产事务伪造达成'
assert 'if (i < order && !CampaignProgressService.TryDeliver' in source, '所选章必须留给公告板交付'

# 2026-09-19 第 20 条：单个委托、单条目标要能分开验，否则只能一键推完整章，
# 漏上报的那一条目标永远暴露不出来。整章与单条必须共用同一套上报调用。
for entry in ('AcceptContract(', 'CompleteNextObjective(', 'DeliverContract(', 'AuditAllObjectives'):
    assert entry in source, '缺少逐委托调试入口 ' + entry
satisfy = source[source.index('private static void SatisfyObjective('):source.index('private static string StateLabel(')]
for api in ('ReportStandardClear', 'ReportWaveReached', 'ReportPlayerKill', 'ReportExtract', 'ReportFinalBossKill'):
    assert api in satisfy, 'SatisfyObjective 缺少上报 ' + api
complete_chapter = source[source.index('private static string CompleteChapter('):]
complete_chapter = complete_chapter[:complete_chapter.index('private static string GrantEgg(')]
assert 'SatisfyObjective(' in complete_chapter and 'ReportStandardClear' not in complete_chapter,     '整章推进必须复用 SatisfyObjective，不许再抄一份 switch'
# 自检按钮只做静态核对：真驱动追踪器会经 NotifyObjectivesSatisfied 改章节状态。
audit = source[source.index('private static string AuditAllObjectives('):]
audit = audit[:audit.index('private static string CompleteChapter(')]
for forbidden in ('CampaignObjectiveTracker.Report', 'CampaignObjectiveTracker.EnsureArmedFor',
                  'CampaignObjectiveTracker.ResetSession', 'SatisfyObjective('):
    assert forbidden not in audit, '自检不得驱动追踪器（有存档副作用）：' + forbidden

print('ManualProgressionGuard: PASS')
