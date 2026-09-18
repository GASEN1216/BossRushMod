"""本场规则必须接到实际入场、认证与清理；数值行为由 ModeHMarketAudit 执行。"""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def body(name, signature):
    source = clean_source((ROOT / 'ModeH' / name).read_text(encoding='utf-8-sig'))
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return re.sub(r'\s+', ' ', source[opening + 1:end - 1])


def main():
    checks = [
        ('ModeHRuntimeModule_CombatFlow.cs', 'private bool InitializeCombatRuntime(',
         'if (!_combatControl.ConfigureMatchRules(_season.currentMatchPlan, out failureReasonId)) return false;'),
        ('ModeHCombatControl.cs', 'public bool OnFighterEntered(',
         'if (!_matchRules.Enter(fighter, out failureReasonId)) return false;'),
        ('ModeHCombatControl.cs', 'public bool OnEnemyEntered(',
         'if (!_matchRules.Enter(enemy, out reason)) return false;'),
        ('ModeHCombatControl.cs', 'public bool Tick(',
         'if (!_matchRules.Tick(deltaTime, out ruleFailure)) throw new InvalidOperationException(ruleFailure);'),
        ('ModeHCombatControl.cs', 'public void RestoreAll()', '_matchRules.RestoreAll();'),
        ('ModeHRuntimeModule_CombatFlow.cs', 'private bool InitializeCombatRuntime(',
         'handle.PlanSlotIndex = _season.currentMatchPlan.enemyStableKeys.IndexOf(handle.StableKey);'),
        ('ModeHRuntimeModule_CombatProfiles.cs', 'private IEnumerator DriveReinforcementSpawn(',
         'handle.PlanSlotIndex = _season.currentMatchPlan.enemyStableKeys.IndexOf(handle.StableKey);'),
        ('ModeHRuntimeModule_CombatProfiles.cs', 'private IEnumerator DriveReinforcementSpawn(',
         'if (!control.OnEnemyEntered(enemy, out failureReasonId)) break;'),
        ('ModeHMatchRules.cs', 'internal bool Enter(ModeHParticipantRef',
         'reference.IsEnemy && ModeHEncounterPlanner.IsWoundedEnemy(_plan, reference.PlanSlotIndex)'),
        ('ModeHMatchRules.cs', 'internal void RestoreAll()',
         '_participants[i].Restore();'),
        ('ModeHMatchRules.cs', 'internal void Restore()',
         'Health.OnHealthChange.RemoveListener(OnHealthChanged);'),
    ]
    errors = []
    for name, signature, statement in checks:
        if statement not in body(name, signature):
            errors.append(name + ': ' + signature + ' 缺少 ' + statement)
    certification = clean_source((ROOT / 'ModeH/ModeHProductionCertification.cs').read_text(encoding='utf-8-sig'))
    for side in ('scavHandle', 'wolfHandle'):
        if 'ModeHMatchRules.HasRequiredHostSupport(' + side + '.Character)' not in certification:
            errors.append('生产认证缺少擂台宿主支持检查: ' + side)
    print('ModeHMatchRulesGuard: ' + ('FAIL\n' + '\n'.join(errors) if errors else 'PASS'))
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
