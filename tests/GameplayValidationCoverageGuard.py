#!/usr/bin/env python3
"""防止新增玩家功能/自动断言不登记，或把人工/未执行项伪装成 PASS。"""
import importlib.util
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('gameplay_coverage', ROOT / 'tools/gameplay_coverage.py')
coverage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage)
errors = coverage.validate(coverage.load_manifest())
report = (ROOT / 'DebugAndTools/F3GameplayValidationCoverage.cs').read_text(encoding='utf-8-sig')
runner = (ROOT / 'DebugAndTools/F3GameplayValidationRunner.cs').read_text(encoding='utf-8-sig')
items = (ROOT / 'DebugAndTools/F3GameplayValidationItems.cs').read_text(encoding='utf-8-sig')
zombie = (ROOT / 'DebugAndTools/F3GameplayValidationZombie.cs').read_text(encoding='utf-8-sig')
for token in ['ModeHJsonParser.TryParse', 'NOT_RUN', 'MANUAL_PENDING', 'INCOMPLETE', 'AutomaticNotPassed',
              'WriteCoverageSnapshot();', 'previous == "FAIL"', 'GetPublishedTypeIds()', 'Enum.GetValues(typeof(RandomEventId))']:
    if token not in report:
        errors.append('覆盖账本缺少约束: ' + token)
if 'JsonUtility' in report:
    errors.append('覆盖清单必须显式解析数组，避免 JsonUtility 静默空表')
for token in ['RunSyncCase("COVERAGE_MANIFEST", InitializeCoverage)', '_coverage.Record(id, outcome)',
              'FinishCoverage()', '" | coverage=" + coverageState', 'yield return RunPublishedItemCases();']:
    if token not in runner:
        errors.append('覆盖功能没有接入 F3 主流程: ' + token)
for token in ['BossRushDynamicItemRegistry.GetPublishedTypeIds()', 'ItemAssetsCollection.InstantiateSync',
              'probe.TypeID != ids[i]', 'probe.DisplayName', 'probe.Icon', 'finally { DestroyProbeItem(probe); }', 'yield return null;']:
    if token not in items:
        errors.append('动态物品用例缺少真实工厂/身份/清理约束: ' + token)
for token in ['CollectZombieModePurificationPoint(runId, 3, null, null)',
              'zombieModeRunState.PurificationPoints != pointsBeforePickup + 3',
              'StartZombieModeExtractionFromUi(runId)', 'area.onCountDownSucceed', 'succeed.Invoke();',
              'first - before == points && second == first && !IsZombieModeActive',
              'IsRuntimeReady(BaseSceneNameForValidation())', 'finally { _host.ValidationSafeCleanup(); }']:
    if token not in zombie:
        errors.append('丧尸撤离用例缺少生产事件/结算/终态断言: ' + token)
if zombie.count('succeed.Invoke();') != 2:
    errors.append('丧尸撤离必须检查重复事件不能再次结算')
for error in errors:
    print('  - ' + error)
print('GameplayValidationCoverageGuard: ' + ('FAIL' if errors else 'PASS'))
raise SystemExit(bool(errors))
