"""Integration carriers preserve legacy entry ownership and the DailyReport UI owner."""
from pathlib import Path
import re
from cs_source_util import clean_source
from integration_host_source import host_region

ROOT = Path(__file__).resolve().parents[1]
CORE = 'Integration/BossRushIntegration.cs'
HOST = 'Integration/IntegrationHostCompatibility.cs'
UI = 'Integration/DailyReport/DailyReportRuntimeModule_UI.cs'


def read(path):
    return clean_source((ROOT/path).read_text(encoding='utf-8-sig'))


def compact(source):
    return re.sub(r'\s+','',source)


def main():
    host, core, ui = read(HOST), read(CORE), read(UI)
    partials = {p.relative_to(ROOT).as_posix() for p in (ROOT/'Integration').rglob('*.cs')
                if re.search(r'\bpartial\s+class\s+ModBehaviour\b',clean_source(p.read_text(encoding='utf-8-sig')))}
    assert partials == {CORE,HOST,'Integration/BossRushIntegration_StartAndScene.cs','Integration/BossRushIntegration_TravelAndSetup.cs'}, 'Integration must keep only its two carriers and two pending scene-flow partials'
    for path in ('Integration/WikiBookItem.cs','Integration/Codex/CodexBookItem.cs',
                 'Integration/Bonus/SetBonusRuntimeHostBridge.cs',
                 'Integration/DragonKing/DragonKingRuntimeModuleHostBridge.cs',
                 'Integration/DragonDescendant/DragonDescendantRuntimeModuleHostBridge.cs',
                 'Integration/PhantomWitch/PhantomWitchRuntimeModuleHostBridge.cs'):
        assert 'partial class ModBehaviour' not in read(path), 'independent types must not retain a host block: '+path
    assert 'public class OriginalWeaponData' in host and 'public Projectile bulletPrefab;' in host, 'published nested OriginalWeaponData must remain on ModBehaviour'
    assert 'private DailyReportView dailyReportView;' in ui and 'DailyReportView dailyReportView' not in host, 'DailyReport view state must belong to its existing runtime module'
    assert 'internal sealed partial class DailyReportRuntimeModule' in ui, 'DailyReport UI must extend the registered owner'
    assert compact('public void OpenDailyReportUI() { dailyReportRuntime.OpenDailyReportUI(IsDailyReportConfiguredEnabled()); }') in compact(host), 'DailyReport public entry must pass the live configured gate once'
    assert compact('private void EnsureDailyReportView() { dailyReportRuntime.EnsureDailyReportView(); }') in compact(host), 'DailyReport private compatibility entry must remain a forward'
    assert 'internal void OpenDailyReportUI(bool configuredEnabled)' in ui and 'if (!configuredEnabled)' in ui, 'DailyReport UI must consume the configured gate before creating a view'
    assert ui.index('if (!configuredEnabled)') < ui.index('EnsureDailyReportView();'), 'DailyReport disabled gate must precede view creation'
    for snippet in ('if (dailyReportView != null) return;','DailyReportView.CreateRuntime(parent);','dailyReportView.RefreshAndOpen();'):
        assert snippet in ui, 'DailyReport lazy UI contract missing: '+snippet
    assert 'owner.' not in ui and '_owner.' not in ui, 'DailyReport UI must not reach back into host business members'
    body = clean_source(host_region(ROOT,'ContentBuildingBridges'))
    for builder, field in [('DailyReportMailboxBuilder','_dailyReportMailboxBuilder'),('CampaignBoardBuilder','_campaignBoardBuilder'),
                           ('ShowcaseBuildingBuilder','_showcaseBuildingBuilder'),('PetNestBuilder','_petNestBuilder')]:
        assert 'private '+builder+' '+field+';' in body, 'building references must retain their original uninitialized lifetime'
        assert field+' ?? ('+field+' = new '+builder+'(this))' in body, 'building owner creation must remain lazy'
    for region in ('AffinityRuntimeModuleHostBridge','DeathWraithRuntimeModuleHostBridge','GoblinNPCRuntimeModuleHostBridge',
                   'NurseNPCRuntimeModuleHostBridge','CourierNPC','NewWeaponBootstrap','MutatorRuntimeBridge',
                   'WeddingHostCompatibilityBridge','WishFountainHostCompatibilityBridge'):
        assert clean_source(host_region(ROOT,region)).strip(), 'missing production compatibility region: '+region
    print('IntegrationHostConsolidationGuard: PASS')


if __name__ == '__main__':
    try: main()
    except AssertionError as error:
        print('IntegrationHostConsolidationGuard: FAIL - '+str(error))
        raise SystemExit(1)
