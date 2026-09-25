"""果实变身必须保留玩家生命/物品身份，并且可完整收尾。物理与外观仍需实机。"""
from pathlib import Path
import re
from cs_source_util import clean_source
ROOT = Path(__file__).resolve().parents[1]
def read(path):
    return re.sub(r'\s+', ' ', clean_source((ROOT / path).read_text(encoding='utf-8-sig')))

def main():
    morph = read('Integration/BackMountain/BackMountainBossMorphService.cs')
    module = read('Integration/BackMountain/BackMountainRuntimeModule.cs')
    harvest = read('Integration/BackMountain/GardenHarvestNoticePatch.cs')
    assert not re.search(r'\bSetHealth\s*\(|\bMaxHealth\b|\.CharacterItem\s*=|\.SetCharacterItem\s*\(', morph), 'morph must not replace health or player equipment tree'
    for fruit, helm, armor in (('DragonFruit', 'DragonDescendantConfig.DRAGON_HELM_TYPE_ID', 'DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID'),
                               ('EmberChili', 'DragonKingConfig.DRAGON_KING_HELM_TYPE_ID', 'DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID')):
        case = re.search(r'case BossRushItemIds\.' + fruit + r':(.*?)return true;', morph).group(1)
        assert 'profile.Helm = ' + helm + ';' in case and 'profile.Armor = ' + armor + ';' in case, 'missing matching boss armor: ' + fruit
    witch = re.search(r'case BossRushItemIds\.PhantomMushroom:(.*?)return true;', morph).group(1)
    assert 'profile.NameKey = PhantomWitchConfig.BasePresetNameKey;' in witch
    assert 'profile.Weapon = PhantomWitchConfig.ReservedScytheTypeId;' in witch
    assert 'profile.Scale = PhantomWitchConfig.BossModelScale;' in witch
    for token in ('character.SetCharacterModel(_model); RestoreCollision();', '_character.SetCharacterModel(null);',
                  '_remaining -= Time.deltaTime;', 'if (BossRushUI.IsGamePaused()) return;',
                  'RuntimeStatModifierTracker.RemoveAll(_modifiers, "BossFruit");',
                  '_character.OnShootEvent -= OnAttack;', '_character.OnAttackEvent -= OnAttack;',
                  '_character.OnHoldAgentChanged -= OnHoldChanged;',
                  'CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnEquipmentChanged;',
                  'private void OnDestroy() { Restore(); }',
                  'AttachCostume(weapon, _model.RightHandSocket, true);',
                  'PhantomWitchScytheWeaponConfig.PrepareRuntimeHoldAgentVisual(visual.gameObject);',
                  'scripts[i].enabled = false;', 'colliders[i].enabled = false;',
                  'renderer.forceRenderingOff = true;', '_hiddenEquipment[i].forceRenderingOff = false;',
                  'saved.Key.handAnimationType = saved.Value;'):
        assert token in morph, 'missing morph lifecycle: ' + token
    for prefix in ('body', 'damage'):
        for field in ('radius', 'height', 'center', 'enabled'):
            assert '_' + prefix + 'Collider.' + field + ' = _' + prefix + field.title() + ';' in morph, 'must restore collider ' + prefix + '.' + field
    assert not re.search(r'\.ChangeHoldItem\s*\(|\.DestroyTree\s*\(|\.SetHolder\s*\(', morph), 'morph must not replace or bind actual weapons'
    assert 'if (!_active) return;' in morph and '_character.Health.IsDead' in morph and 'SceneLoader.IsSceneLoading' in morph
    assert 'info.isFromBuffOrEffect = true;' in morph and 'info.fromWeaponItemID = 0;' in morph
    assert '!Team.IsEnemy(_character.Team, target.Team)' in morph and 'Physics.Linecast(origin, point, _wallMask' in morph
    usage = read('Integration/BackMountain/RaidMealUsageBehavior.cs')
    assert 'BackMountainBossMorphService.TryBegin(item.TypeID, owner)' in usage and 'RaidMealService.RegisterMeal(' not in usage
    assert 'BackMountainBossMorphService.CanUse;' in usage and 'IsBaseLevel' not in usage
    assert module.count('BackMountainBossMorphService.Clear();') >= 2, 'shutdown and unload must release morph'
    assert '[HarmonyPrefix] internal static bool EnsureHarvestProduct' in harvest
    assert 'BackMountainItems.EnsureRuntimeRegistration(productId)' in harvest
    assert 'route.opcode = OpCodes.Ldarg_0;' in harvest and 'nameof(PreferPlayerInventory)' in harvest
    print('BackMountainMorphLifecycleGuard: PASS')

if __name__ == '__main__': main()
