using Duckov.Utilities;
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
    internal sealed partial class ZombieModeRuntimeModule
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
                    TryGiveZombieModeStarterAmmo(caliber, ZombieModeTuning.RandomGunRewardAmmoCount);
                }
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_RandomGunWithAmmo"));
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] RandomGunWithAmmo reward failed: " + e.Message);
                try
                {
                    if (gun != null)
                    {
                        gun.DestroyTree();
                    }
                }
                catch (System.Exception destroyEx)
                {
                    ModBehaviour.DevLog("[ZombieMode] RandomGunWithAmmo reward cleanup failed: " + destroyEx.Message);
                }

                GrantZombieModeFallbackPurificationReward("RandomGunWithAmmoRewardFail_Exception", bossNode ? 160 : 110);
                return true;
            }
        }

        private bool GrantZombieModeAmmoSupplyReward()
        {
            if (TryGiveZombieModeSupplyAmmo(ZombieModeTuning.AmmoSupplyRewardAmmoCount))
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
            return Mathf.Clamp(runState.PollutionTier + (bossNode ? 2 : 1), 1, ZombieModeTuning.StarterMaxQuality + (bossNode ? 1 : 0));
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
                owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Banner_RepairPackReceived"));
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

            runState.PollutionFromContracts += pollution;
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

            runState.PollutionFromContracts += pollution;
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
                TryGiveZombieModeAmmo(caliber, ZombieModeTuning.ContractGunRewardAmmoCount, minQuality, maxQuality);
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

            runState.PollutionFromContracts += 3;
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

            runState.PollutionFromContracts += 2;
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
            if (includeSpecifiedKeep && runState.InsuranceState.SpecifiedKeepItem == null && candidates.Count > 0)
            {
                runState.InsuranceState.SpecifiedKeepItem = candidates[0];
            }

            runState.InsuranceState.RandomKeepRatio = Mathf.Clamp(
                runState.InsuranceState.RandomKeepRatio + Mathf.Max(0f, randomKeepRatio),
                0f,
                0.80f);
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_InsuranceKeepOne"),
                Mathf.RoundToInt(runState.InsuranceState.RandomKeepRatio * 100f)));
        }

        private void ApplyZombieModeMapEventReward(ZombieModeRewardType rewardType)
        {
            if (rewardType == ZombieModeRewardType.MapEventHighValueAirdrop)
            {
                runState.PendingMapEvent = ZombieModePendingMapEventType.HighValueAirdrop;
                CreateZombieModeHighValueAirdrop(runState.RunId);
                NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop"));
                return;
            }

            runState.PendingMapEvent = ZombieModePendingMapEventType.EliteSquad;
            runState.PendingEliteSquadCount += 3;
            NotificationText.Push(L10n.T("BossRush_ZombieMode_Reward_MapEventEliteSquad"));
        }

        internal void SpawnPendingZombieModeEliteSquad(int runId)
        {
            if (!IsZombieModeRunValid(runId) || runState.PendingEliteSquadCount <= 0)
            {
                return;
            }

            int count = runState.PendingEliteSquadCount;
            runState.PendingEliteSquadCount = 0;
            runState.PendingMapEvent = ZombieModePendingMapEventType.None;
            SpawnZombieModeEliteSquadAsync(runId, count).Forget();
        }

        private async UniTask SpawnZombieModeEliteSquadAsync(int runId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsZombieModeRunValid(runId) || runState.CombatPhase != ZombieModeCombatPhase.Combat)
                {
                    return;
                }

                await TrySpawnZombieModeNormalZombieAsync(
                    runId,
                    GetZombieModeSpawnPosition(),
                    ZombieModeEnemyKind.Elite,
                    true,
                    () => runState.CombatPhase == ZombieModeCombatPhase.Combat);
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
            owner.ShowBigBanner(L10n.T("BossRush_ZombieMode_Reward_MapEventHighValueAirdrop"));
        }

        internal void SettleZombieModeFailureInsuranceShell(int runId)
        {
            if (runId <= 0 || runState.RunId != runId)
            {
                return;
            }

            float keepRatio = Mathf.Clamp(runState.InsuranceState.RandomKeepRatio, 0f, 0.80f);
            Item specified = runState.InsuranceState.SpecifiedKeepItem;
            runState.PurificationPoints = 0;
            if (keepRatio <= 0f && specified == null)
            {
                runState.InsuranceState.Reset();
                return;
            }

            List<Item> candidates = CollectZombieModeInsuranceCandidates();
            if (candidates.Count <= 0)
            {
                runState.InsuranceState.Reset();
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
            runState.InsuranceState.Reset();
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
                    ModBehaviour.DevLog("[ZombieMode] PlayerStorage.AddItem 失败: " + e.Message);
                }
            }

            return false;
        }

        internal string GetZombieModeAttributeKey(ZombieModeRewardType rewardType)
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

        /// <summary>
        /// 属性加成是否已到上限。0.1 累加十次是 0.99999994，留一点浮点余量；减伤按负数累加（上限 -0.40），
        /// 而 <see cref="GetZombieModeAttributeCap"/> 给的是正数，所以两边都比绝对值。
        /// </summary>
        internal static bool IsZombieModeAttributeBonusAtCap(float currentBonus, float cap)
        {
            return Mathf.Abs(currentBonus) >= Mathf.Abs(cap) - 0.0001f;
        }

        internal float GetZombieModeAttributeCap(ZombieModeRewardType rewardType)
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
        internal bool TryGiveZombieModeWaveClearHealingItem()
        {
            return TryGiveRandomItemByTags(ZombieModeRewardTagHealing, 1, ZombieModeTuning.StarterMaxQuality);
        }

        internal int TryGiveRandomItemByTagsTimes(string[] requiredTags, int minQuality, int maxQuality, int times)
        {
            int success = 0;
            for (int i = 0; i < times; i++)
            {
                if (TryGiveRandomItemByTags(requiredTags, minQuality, maxQuality))
                {
                    success++;
                }
            }
            return success;
        }

        /// <summary>
        /// 弹药补给按玩家手上正在用的枪配口径（中途拿到 RPG 就补火箭弹）；手上没枪、或这个口径一发弹药都发不出来时，
        /// 再按开局枪的口径发。两者都发不出来返回 false，由调用方落到随机弹药。
        /// </summary>
        private bool TryGiveZombieModeSupplyAmmo(int totalCount)
        {
            string held = TryReadZombieModeItemCaliber(TryGetZombieModePreferredWeaponForMerchantAmmo());
            if (!string.IsNullOrEmpty(held) && TryGiveZombieModeStarterAmmo(held, totalCount))
            {
                return true;
            }

            string starter = runState.StarterAmmoCaliber ?? string.Empty;
            return starter.Length > 0
                && !string.Equals(starter, held, System.StringComparison.Ordinal)
                && TryGiveZombieModeStarterAmmo(starter, totalCount);
        }

        internal string TryReadZombieModeItemCaliber(Item item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            try
            {
                if (item.Constants == null)
                {
                    return string.Empty;
                }
                string caliber = item.Constants.GetString("Caliber", null);
                return caliber ?? string.Empty;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 弹药 caliber 读取失败: " + e.Message);
            }
            return string.Empty;
        }

        internal bool TryGiveZombieModeStarterAmmo(string caliber, int totalCount)
        {
            return TryGiveZombieModeAmmo(caliber, totalCount, 1, 2);
        }

        /// <param name="allowQualityFallback">区间里没有该口径时是否改发离区间最近的那档；保底品质的购买传 false，
        /// 发不出就留着保底、按普通区间再买一次。</param>
        private bool TryGiveZombieModeAmmo(string caliber, int totalCount, int minQuality, int maxQuality, bool allowQualityFallback = true)
        {
            try
            {
                int chosenTypeId = PickZombieModeAmmoTypeForCaliber(caliber, minQuality, maxQuality, allowQualityFallback);
                if (chosenTypeId <= 0)
                {
                    return false;
                }

                Item ammoItem = ItemAssetsCollection.InstantiateSync(chosenTypeId);
                if (ammoItem == null)
                {
                    return false;
                }

                // 官方 StackCount 会夹到 MaxStackCount：普通子弹给一整组，火箭弹这类不堆叠 / 小堆叠的重弹药只剩一两发。
                // 不堆叠的物品设数量会报官方错误日志，只给可堆叠的设。
                if (ammoItem.Stackable)
                {
                    try { ammoItem.StackCount = totalCount; } catch (System.Exception e) { ModBehaviour.DevLog("[ZombieMode] ammoItem.StackCount 设置失败: " + e.Message); }
                }
                int granted = ammoItem.StackCount;
                ItemUtilities.SendToPlayer(ammoItem, true, true);

                int minimumRounds = Mathf.Min(totalCount, ZombieModeTuning.HeavyAmmoMinimumRounds);
                for (int guard = 0; granted < minimumRounds && guard < minimumRounds; guard++)
                {
                    Item extra = ItemAssetsCollection.InstantiateSync(chosenTypeId);
                    if (extra == null)
                    {
                        break;
                    }

                    if (extra.Stackable)
                    {
                        try { extra.StackCount = minimumRounds - granted; } catch (System.Exception) { }
                    }
                    granted += Mathf.Max(1, extra.StackCount);
                    ItemUtilities.SendToPlayer(extra, true, true);
                }
                return true;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[ZombieMode] 发放起始弹药失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 先按调用方的品质区间找该口径的弹药；区间里一发都没有时（火箭弹、能量弹这类只有高品质的口径），
        /// 放开品质取离区间最近的那档：区间在可用品质之下取最低档，在之上取不超过上限的最高档，距离相同取高的。
        /// 官方 Search 落空时只会往低品质降级，不会往上找，所以这里直接查全表。
        /// </summary>
        private int PickZombieModeAmmoTypeForCaliber(string caliber, int minQuality, int maxQuality, bool allowQualityFallback)
        {
            caliber = caliber ?? string.Empty;
            int picked = PickZombieModeStrictQualityCandidate(SearchZombieModeAmmo(caliber, minQuality, maxQuality, true), minQuality, maxQuality);
            if (picked > 0 || caliber.Length == 0 || !allowQualityFallback)
            {
                return picked;
            }

            int[] anyQuality = SearchZombieModeAmmo(caliber, 0, ZombieModeAmmoAnyQualityMax, false);
            if (anyQuality == null || anyQuality.Length <= 0)
            {
                ModBehaviour.DevLog("[ZombieMode] 口径 " + caliber + " 找不到任何弹药");
                return -1;
            }

            int nearest = -1;
            int nearestDistance = int.MaxValue;
            for (int i = 0; i < anyQuality.Length; i++)
            {
                int quality = GetZombieModeItemQuality(anyQuality[i]);
                if (quality < 0) continue;
                int distance = quality < minQuality ? minQuality - quality : (quality > maxQuality ? quality - maxQuality : 0);
                if (distance < nearestDistance || (distance == nearestDistance && quality > nearest))
                {
                    nearest = quality;
                    nearestDistance = distance;
                }
            }
            if (nearest < 0)
            {
                return -1;
            }

            ModBehaviour.DevLog("[ZombieMode] 口径 " + caliber + " 在品质 " + minQuality + "-" + maxQuality + " 无弹药，改发品质 " + nearest);
            return PickZombieModeStrictQualityCandidate(anyQuality, nearest, nearest);
        }

        private const int ZombieModeAmmoAnyQualityMax = 99;

        private int[] SearchZombieModeAmmo(string caliber, int minQuality, int maxQuality, bool allowOfficialDowngrade)
        {
            // 有的弹药只挂 Bullet 不挂 Ammo（或反过来），两组标签依次试。
            string[][] tagSets = { ZombieModeRewardTagAmmo, ZombieModeRewardTagBullet };
            for (int i = 0; i < tagSets.Length; i++)
            {
                Tag[] tags = ResolveZombieModeTags(tagSets[i]);
                if (tags == null || tags.Length <= 0)
                {
                    continue;
                }

                ItemFilter filter = new ItemFilter();
                filter.requireTags = tags;
                filter.minQuality = minQuality;
                filter.maxQuality = maxQuality;
                filter.caliber = caliber;
                int[] result = allowOfficialDowngrade
                    ? ItemAssetsCollection.Search(filter)
                    : ItemAssetsCollection.GetAllTypeIds(filter);
                if (result != null && result.Length > 0)
                {
                    return result;
                }
            }
            return null;
        }

        private static int GetZombieModeItemQuality(int typeId)
        {
            try
            {
                ItemMetaData metaData = ItemAssetsCollection.GetMetaData(typeId);
                return metaData.id > 0 ? metaData.quality : -1;
            }
            catch
            {
                return -1;
            }
        }

        internal bool TryGiveRandomItemByTags(string[] requiredTags, int minQuality, int maxQuality)
        {
            int typeId = FindRandomItemTypeByTags(requiredTags, minQuality, maxQuality);
            if (typeId <= 0)
            {
                return false;
            }

            Item item = ItemAssetsCollection.InstantiateSync(typeId);
            if (item == null)
            {
                return false;
            }

            ItemUtilities.SendToPlayer(item, false, false);
            return true;
        }
    }
}
