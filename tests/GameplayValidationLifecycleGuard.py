#!/usr/bin/env python3
"""第五轮实机回归：成功消费入场意图、真实整备、场景/迟到 Boss 订阅回收。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors = []


def read(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')


scene = read('ModeH/ModeHRuntimeModule_SceneFlow.cs')
create = scene[scene.index('private void CreateDraftingSeason('):]
if not (0 <= create.find('RequestSeasonWrite(') < create.find('ModeHProfilePersistence.LoadCurrent()')
        < create.find('ModeHEntry.CancelPendingEntry();')):
    errors.append('Mode H 首份赛季写入并回读成功后必须消费入场意图；此前须保留退款凭据')
reset = scene[scene.index('internal void ForceResetStateForValidation()'):scene.index('internal void ForceResetStateForValidation()') + 1700]
if 'ModeHEntry.CancelPendingEntry();' not in reset:
    errors.append('F3 强制清理必须回收未完成的入场意图')
modes = read('DebugAndTools/F3GameplayValidationModes.cs')
for token in ['yield return RunModeHStarterKits(map)', 'archived && intentCleared', 'entry_intent_not_consumed']:
    if token not in modes:
        errors.append('F3 缺少成功入场/真实整备验证: ' + token)
kits = read('DebugAndTools/F3GameplayValidationModeHKits.cs')
for token in ['GetStarterKitIds()', 'ModeHPresetRegistry.GetAuditedPreset',
              'ModeHLoadoutKitApplicator.TryApply', 'ModeHLoadoutKitApplicator.Recycle(application)',
              'if (request.Abandoned) ModeHSpawnBridge.Recycle(handle)',
              'gun.BulletCount == loaded && total == kit.Spec.AmmoCount',
              'item.StackCount > item.MaxStackCount', 'item.InInventory != equipped.Inventory',
              'item.InInventory != character.Inventory', 'equipped.TypeID != kit.ResolvedTypeId']:
    if token not in kits:
        errors.append('H 整备验收缺少实际角色/库存/迟到回收约束: ' + token)
hooks = read('Integration/IntegrationRuntimeHooks.cs')
if not (0 <= hooks.find('AffixForgeStoneDropService.ClearAllTracking();') < hooks.find('OnSceneLoaded_Integration(scene, mode)')):
    errors.append('Integration 场景回调必须先回收旧词缀熔石掉落订阅')
campaign = read('Campaign/CampaignFinalBoss.cs')
for variable in ['boss', 'campaignFinalBossInstance']:
    clear = campaign.find('ClearBossRandomLootTracking(' + variable + ');')
    destroy = campaign.find('UnityEngine.Object.Destroy(' + variable + '.gameObject)', clear)
    if clear < 0 or destroy < clear or destroy - clear > 200:
        errors.append('终章主动销毁/迟到生成必须先回收掉落订阅: ' + variable)
for error in errors:
    print('  - ' + error)
print('GameplayValidationLifecycleGuard: ' + ('FAIL' if errors else 'PASS'))
raise SystemExit(bool(errors))
