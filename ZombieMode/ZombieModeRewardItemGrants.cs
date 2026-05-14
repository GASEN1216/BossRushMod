// ============================================================================
// ZombieModeRewardItemGrants.cs - 丧尸模式物品与契约奖励
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool GrantZombieModeRandomMeleeReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            if (TryGiveRandomItemByTags(ZombieModeRewardTagMeleeWeapon, 1, maxQuality))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomMeleeWeapon"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("RandomMeleeRewardFail", bossNode ? 140 : 90);
            return true;
        }

        private bool GrantZombieModeRandomGunWithAmmoReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            int gunTypeId = FindRandomItemTypeByTags(ZombieModeRewardTagGun, 1, maxQuality);
            if (gunTypeId <= 0)
            {
                GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_NoType", bossNode ? 160 : 110);
                return true;
            }

            Item gun = null;
            try
            {
                gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
                if (gun == null)
                {
                    GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Instantiate", bossNode ? 160 : 110);
                    return true;
                }

                string caliber = TryReadZombieModeItemCaliber(gun);
                if (!TryDeliverZombieModeItemToPlayerOrDrop(gun, "RandomGunWithAmmoReward"))
                {
                    GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Deliver", bossNode ? 160 : 110);
                    return true;
                }

                gun = null;
                if (!string.IsNullOrEmpty(caliber))
                {
                    TryGiveZombieModeStarterAmmo(caliber, 60);
                }
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomGunWithAmmo"));
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] RandomGunWithAmmo reward failed: " + e.Message);
                try
                {
                    if (gun != null)
                    {
                        gun.DestroyTree();
                    }
                }
                catch (System.Exception destroyEx)
                {
                    DevLog("[ZombieMode] RandomGunWithAmmo reward cleanup failed: " + destroyEx.Message);
                }

                GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Exception", bossNode ? 160 : 110);
                return true;
            }
        }

        private bool GrantZombieModeAmmoSupplyReward()
        {
            string caliber = !string.IsNullOrEmpty(zombieModeRunState.StarterAmmoCaliber)
                ? zombieModeRunState.StarterAmmoCaliber
                : string.Empty;
            if (!string.IsNullOrEmpty(caliber) && TryGiveZombieModeStarterAmmo(caliber, 120))
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_AmmoSupply"));
                return true;
            }

            bool granted = TryGiveRandomItemByTags(ZombieModeRewardTagAmmo, 1, 4);
            granted |= TryGiveRandomItemByTags(ZombieModeRewardTagBullet, 1, 4);
            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_AmmoSupply"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("AmmoSupplyRewardFail", 70);
            return true;
        }

        private bool GrantZombieModeMedicalSupplyReward()
        {
            int count = UnityEngine.Random.Range(3, 6);
            int granted = TryGiveRandomItemByTagsTimes(ZombieModeRewardTagsMedicMedicalHealing, 1, 4, count);
            if (granted > 0)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MedicalSupply"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("MedicalSupplyRewardFail", 70);
            return true;
        }

        private bool GrantZombieModeArmorOrHelmetReward(bool bossNode)
        {
            int maxQuality = GetZombieModeRewardMaxQuality(bossNode);
            string[] tags = UnityEngine.Random.value < 0.5f ? ZombieModeRewardTagArmor : ZombieModeRewardTagHelmet;
            bool granted = TryGiveRandomItemByTags(tags, 1, maxQuality);
            if (!granted)
            {
                granted = TryGiveRandomItemByTags(ZombieModeRewardTagArmor, 1, maxQuality) ||
                          TryGiveRandomItemByTags(ZombieModeRewardTagHelmet, 1, maxQuality);
            }

            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_ArmorOrHelmet"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("ArmorOrHelmetRewardFail", bossNode ? 160 : 100);
            return true;
        }

        private int GetZombieModeRewardMaxQuality(bool bossNode)
        {
            return Mathf.Clamp(zombieModeRunState.PollutionTier + (bossNode ? 2 : 1), 1, ZombieModeTuning.StarterMaxQuality + (bossNode ? 1 : 0));
        }

        private bool GrantZombieModeFortificationPack(bool bossNode)
        {
            int granted = 0;
            granted += GrantZombieModeItemRepeated(FoldableCoverPackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackFoldableCoverBoss : ZombieModeNpcCatalog.RepairPackFoldableCoverNormal);
            granted += GrantZombieModeItemRepeated(ReinforcedRoadblockPackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackReinforcedRoadblockBoss : ZombieModeNpcCatalog.RepairPackReinforcedRoadblockNormal);
            granted += GrantZombieModeItemRepeated(BarbedWirePackConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackBarbedWireBoss : ZombieModeNpcCatalog.RepairPackBarbedWireNormal);
            granted += GrantZombieModeItemRepeated(EmergencyRepairSprayConfig.TYPE_ID, bossNode ? ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayBoss : ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayNormal);

            if (granted > 0)
            {
                ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_RepairPackReceived"));
                return true;
            }

            GrantZombieModeFallbackPurificationReward("FortificationPackRewardFail", bossNode ? 160 : 100);
            return true;
        }

        private int GrantZombieModeItemRepeated(int typeId, int count)
        {
            int granted = 0;
            for (int i = 0; i < Mathf.Max(0, count); i++)
            {
                if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
                {
                    granted++;
                }
            }

            return granted;
        }

        private bool ApplyZombieModeContractPollutionDeal(bool bossNode)
        {
            int pollution = bossNode ? 2 : 1;
            int pointsCost = bossNode ? 150 : 80;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractPollutionDeal"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractPollutionCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractGearDeal(bool bossNode)
        {
            int pollution = bossNode ? 3 : 2;
            int pointsCost = bossNode ? 120 : 60;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractGearDeal"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += pollution;
            GrantZombieModeContractGearDealRewardOnly();
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractGearCost"),
                pollution,
                pointsCost));
            return true;
        }

        private bool GrantZombieModeContractGearDealRewardOnly()
        {
            bool granted = GrantZombieModeContractGearDealGunWithAmmo();
            granted |= GrantZombieModeContractGearDealArmorOrHelmet();
            if (!granted)
            {
                GrantZombieModeFallbackPurificationReward("ContractGearDeal", 180);
            }

            return granted;
        }

        private bool GrantZombieModeContractGearDealGunWithAmmo()
        {
            int minQuality = ZombieModeContractGearDealMinQuality;
            int maxQuality = GetZombieModeContractGearDealMaxQuality();
            int gunTypeId = PickZombieModeStrictQualityCandidate(
                GetZombieModeRewardCandidateIds(ZombieModeRewardTagGun, minQuality, maxQuality),
                minQuality,
                maxQuality);
            if (gunTypeId <= 0)
            {
                return false;
            }

            Item gun = ItemAssetsCollection.InstantiateSync(gunTypeId);
            if (gun == null)
            {
                return false;
            }

            string caliber = TryReadZombieModeItemCaliber(gun);
            ItemUtilities.SendToPlayer(gun, false, false);
            if (!string.IsNullOrEmpty(caliber))
            {
                TryGiveZombieModeAmmo(caliber, 60, minQuality, maxQuality);
            }

            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomGunWithAmmo"));
            return true;
        }

        private bool GrantZombieModeContractGearDealArmorOrHelmet()
        {
            int minQuality = ZombieModeContractGearDealMinQuality;
            int maxQuality = GetZombieModeContractGearDealMaxQuality();
            string[] tags = UnityEngine.Random.value < 0.5f ? ZombieModeRewardTagArmor : ZombieModeRewardTagHelmet;
            bool granted = TryGiveZombieModeContractGearDealItemByTags(tags, minQuality, maxQuality);
            if (!granted)
            {
                granted = TryGiveZombieModeContractGearDealItemByTags(ZombieModeRewardTagArmor, minQuality, maxQuality) ||
                          TryGiveZombieModeContractGearDealItemByTags(ZombieModeRewardTagHelmet, minQuality, maxQuality);
            }

            if (granted)
            {
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_ArmorOrHelmet"));
            }

            return granted;
        }

        private bool TryGiveZombieModeContractGearDealItemByTags(string[] tags, int minQuality, int maxQuality)
        {
            int typeId = PickZombieModeStrictQualityCandidate(
                GetZombieModeRewardCandidateIds(tags, minQuality, maxQuality),
                minQuality,
                maxQuality);
            return TryGiveZombieModeItemToPlayerOrDrop(typeId);
        }

        private int GetZombieModeContractGearDealMaxQuality()
        {
            return Mathf.Max(ZombieModeContractGearDealMinQuality, ZombieModeTuning.StarterMaxQuality + 1);
        }

        private bool ApplyZombieModeContractHugePurification()
        {
            int pointsCost = 200;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractHugePurification"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += 3;
            ApplyZombieModeInsuranceReward(0.30f, true);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractHugePurificationCost"),
                3,
                pointsCost));
            return true;
        }

        private bool ApplyZombieModeContractInsurance()
        {
            int pointsCost = 80;
            if (!SpendZombieModePurificationPoints(pointsCost, "ContractInsurance"))
            {
                return false;
            }

            zombieModeRunState.PollutionFromContracts += 2;
            ApplyZombieModeInsuranceReward(0.20f, true);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_ContractInsuranceCost"),
                2,
                pointsCost));
            return true;
        }

        private void ApplyZombieModeInsuranceReward(float randomKeepRatio, bool includeSpecifiedKeep)
        {
            List<Item> candidates = CollectZombieModeInsuranceCandidates();
            if (includeSpecifiedKeep && zombieModeRunState.InsuranceState.SpecifiedKeepItem == null && candidates.Count > 0)
            {
                zombieModeRunState.InsuranceState.SpecifiedKeepItem = candidates[0];
            }

            zombieModeRunState.InsuranceState.RandomKeepRatio = Mathf.Clamp(
                zombieModeRunState.InsuranceState.RandomKeepRatio + Mathf.Max(0f, randomKeepRatio),
                0f,
                0.80f);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_InsuranceKeepOne"),
                Mathf.RoundToInt(zombieModeRunState.InsuranceState.RandomKeepRatio * 100f)));
        }

        private void ApplyZombieModeMapEventReward(ZombieModeRewardType rewardType)
        {
            if (rewardType == ZombieModeRewardType.MapEventHighValueAirdrop)
            {
                zombieModeRunState.PendingMapEvent = ZombieModePendingMapEventType.HighValueAirdrop;
                CreateZombieModeHighValueAirdrop(zombieModeRunState.RunId);
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop"));
                return;
            }

            zombieModeRunState.PendingMapEvent = ZombieModePendingMapEventType.EliteSquad;
            zombieModeRunState.PendingEliteSquadCount += 3;
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MapEventEliteSquad"));
        }

        private void SpawnPendingZombieModeEliteSquad(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.PendingEliteSquadCount <= 0)
            {
                return;
            }

            int count = zombieModeRunState.PendingEliteSquadCount;
            zombieModeRunState.PendingEliteSquadCount = 0;
            zombieModeRunState.PendingMapEvent = ZombieModePendingMapEventType.None;
            SpawnZombieModeEliteSquadAsync(runId, count).Forget();
        }

        private async UniTask SpawnZombieModeEliteSquadAsync(int runId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    GetZombieModeSpawnPosition(),
                    ZombieModeEnemyKind.Elite,
                    true,
                    () => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat);
                await UniTask.Yield();
            }
        }

        private void CreateZombieModeHighValueAirdrop(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            bool granted = false;
            granted |= TryGiveRandomItemByTags(ZombieModeRewardTagGun, 4, ZombieModeTuning.StarterMaxQuality + 1);
            granted |= TryGiveRandomItemByTags(ZombieModeRewardTagMeleeWeapon, 4, ZombieModeTuning.StarterMaxQuality + 1);
            granted |= TryGiveRandomItemByTags(ZombieModeRewardTagArmor, 4, ZombieModeTuning.StarterMaxQuality + 1);
            if (!granted)
            {
                GrantZombieModeFallbackPurificationReward("HighValueSupply", 180);
            }
            ShowBigBanner(L10n.T("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop"));
        }

        private void SettleZombieModeFailureInsuranceShell(int runId)
        {
            if (runId <= 0 || zombieModeRunState.RunId != runId)
            {
                return;
            }

            float keepRatio = Mathf.Clamp(zombieModeRunState.InsuranceState.RandomKeepRatio, 0f, 0.80f);
            Item specified = zombieModeRunState.InsuranceState.SpecifiedKeepItem;
            zombieModeRunState.PurificationPoints = 0;
            if (keepRatio <= 0f && specified == null)
            {
                zombieModeRunState.InsuranceState.Reset();
                return;
            }

            List<Item> candidates = CollectZombieModeInsuranceCandidates();
            if (candidates.Count <= 0)
            {
                zombieModeRunState.InsuranceState.Reset();
                return;
            }

            int saved = 0;
            if (specified != null && candidates.Contains(specified))
            {
                if (TryMoveZombieModeInsuredItemToStorage(specified))
                {
                    candidates.Remove(specified);
                    saved++;
                }
            }

            int randomKeepCount = Mathf.FloorToInt(candidates.Count * keepRatio);
            for (int i = 0; i < randomKeepCount && candidates.Count > 0; i++)
            {
                int index = UnityEngine.Random.Range(0, candidates.Count);
                Item item = candidates[index];
                candidates.RemoveAt(index);
                if (TryMoveZombieModeInsuredItemToStorage(item))
                {
                    saved++;
                }
            }

            if (saved > 0)
            {
                NotificationText.Push(string.Format(L10n.T("BossRush_ZombieMode_Settle_InsuranceSaved"), saved));
            }
            zombieModeRunState.InsuranceState.Reset();
        }

        private List<Item> CollectZombieModeInsuranceCandidates()
        {
            return CollectZombieModeTopLevelPlayerItems();
        }

        private bool TryMoveZombieModeInsuredItemToStorage(Item item)
        {
            if (item == null || item.IsBeingDestroyed)
            {
                return false;
            }

            try
            {
                item.Detach();
                ReforgeDataPersistence.SyncCurrentReforgeState(item);
                PlayerStorage.Push(item, true);
                return true;
            }
            catch
            {
                try
                {
                    if (PlayerStorage.Inventory != null)
                    {
                        return PlayerStorage.Inventory.AddItem(item);
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] PlayerStorage.AddItem 失败: " + e.Message);
                }
            }

            return false;
        }

        private string GetZombieModeAttributeKey(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.AttributeMaxHealth:
                    return ZombieModeAttributeMaxHealthKey;
                case ZombieModeRewardType.AttributeMoveSpeed:
                    return ZombieModeAttributeMoveSpeedKey;
                case ZombieModeRewardType.AttributeMeleeDamage:
                    return ZombieModeAttributeMeleeDamageKey;
                case ZombieModeRewardType.AttributeRangedDamage:
                    return ZombieModeAttributeRangedDamageKey;
                case ZombieModeRewardType.AttributeReloadSpeed:
                    return ZombieModeAttributeReloadSpeedKey;
                case ZombieModeRewardType.AttributeDamageReduction:
                    return ZombieModeAttributeDamageReductionKey;
                default:
                    return string.Empty;
            }
        }

        private float GetZombieModeAttributeCap(ZombieModeRewardType rewardType)
        {
            switch (rewardType)
            {
                case ZombieModeRewardType.AttributeMaxHealth:
                    return 1.00f;
                case ZombieModeRewardType.AttributeMoveSpeed:
                    return 0.30f;
                case ZombieModeRewardType.AttributeMeleeDamage:
                    return 1.20f;
                case ZombieModeRewardType.AttributeRangedDamage:
                    return 1.00f;
                case ZombieModeRewardType.AttributeReloadSpeed:
                    return 0.80f;
                case ZombieModeRewardType.AttributeDamageReduction:
                    return 0.40f;
                default:
                    return 1f;
            }
        }
    }
}
