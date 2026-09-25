"""鸭王杯预备全槽配装与可达随机场地的入口接线，算法另由执行回归验证。"""
from pathlib import Path
import re
import sys
from cs_source_util import clean_source
ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return re.sub(r'\s+', ' ', clean_source((ROOT / path).read_text(encoding='utf-8-sig')))

def main():
    errors = []
    def need(path, token, reason):
        if token not in read(path): errors.append(path + ': ' + reason)
    flow = 'ModeH/ModeHRuntimeModule_CombatFlow.cs'
    for token in ('GetPreparedProfileOutfit(starter)', 'GetPreparedProfileOutfit(relay)',
                  'GetPreparedEnemyOutfit(_season.currentMatchPlan, index)',
                  'input.PlayerPower = starterStats.Power', 'input.EnemyPower += enemyStats.Power'):
        need(flow, token, '首发、接力、敌方出战与报价必须消费同一装备预案: ' + token)
    need('ModeH/ModeHRuntimeModule_CombatProfiles.cs',
         'GetPreparedEnemyOutfit(_season.currentMatchPlan, index)', '增援不能漏装预案')
    need('ModeH/ModeHRuntimeModule_PreparedLoadouts.cs', 'ModeHLoadoutKitApplicator.TryApplyPreview(body, kits)',
         '预览必须让官方物品树计算装备Modifier')
    need('ModeH/ModeHLoadoutKitRegistry_Prepared.cs', 'foreach (Slot slot in template.Slots)', '预备配装枚举所有官方装备槽')
    need('ModeH/ModeHLoadoutKitRegistry_Prepared.cs', 'Array.Sort(ids);', '候选排序保证seed稳定')
    need('ModeH/ModeHLoadoutKitApplicator.cs', 'previewGun.SetTargetBulletType(previewAmmo);', '预览也设置冻结弹药类型')
    need('ModeH/ModeHLoadoutKitApplicator.cs', 'handle.Character.SwitchToFirstAvailableWeapon();', '换装后必须切出实际武器')
    nav = 'ModeH/ModeHMapSupportRegistry.cs'
    for token in ('astar.GetNearest(raw, NNConstraint.Walkable)', 'Physics.CheckCapsule(',
                  'path.CompleteState != PathCompleteState.Complete', 'length > Vector3.Distance(',
                  'AstarPath.StartPath(path);', 'path.BlockUntilCalculated();', 'path.Release(points);', 'copy.SpectatorPos = spectator;', 'copy.StagingPos = copy.ArenaCenter',
                  'reason = "map_variant_no_reachable_cluster"; return false;'):
        need(nav, token, '随机场地需地面、净空、完整短路径且看台同步: ' + token)
    need('ModeH/ModeHRuntimeModule_Recovery.cs', 'TryCreateRunVariant(_map, _runState.RunSeed,', '续赛必须按原seed重建场地')
    need('ModeH/ModeHRuntimeModule_SceneFlow.cs', 'AbortSetup(mapVariantReason ?? "map_no_safe_arena", true);',
         '找不到可达场地必须安全中止')
    if errors:
        print('ModeHPreparedMatchGuard: FAIL\n' + '\n'.join(errors)); return 1
    print('ModeHPreparedMatchGuard: PASS'); return 0

if __name__ == '__main__': sys.exit(main())
