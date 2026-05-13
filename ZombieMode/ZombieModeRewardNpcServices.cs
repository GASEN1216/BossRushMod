// ============================================================================
// ZombieModeRewardNpcServices.cs - 丧尸模式临时 NPC 服务
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
        public void OpenZombieModeTemporaryNpcServiceUi(int runId, string serviceType)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            ZombieModeTemporaryNpc npc = FindZombieModeTemporaryNpc(serviceType);
            if (npc == null || npc.ServiceState == null)
            {
                return;
            }

            ZombieModeTemporaryNpcServiceView[] existingViews = UnityEngine.Object.FindObjectsOfType<ZombieModeTemporaryNpcServiceView>(true);
            for (int i = 0; i < existingViews.Length; i++)
            {
                if (existingViews[i] == null || existingViews[i].gameObject == null)
                {
                    continue;
                }

                Destroy(existingViews[i].gameObject);
            }

            GameObject root = new GameObject("ZombieMode_TemporaryNpcServiceUi_" + serviceType);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.RewardUi, root, root, null);
            ZombieModeTemporaryNpcServiceView view = root.AddComponent<ZombieModeTemporaryNpcServiceView>();
            view.Initialize(runId, this, serviceType);
        }

        private ZombieModeTemporaryNpc FindZombieModeTemporaryNpc(string serviceType)
        {
            for (int i = 0; i < zombieModeRunState.TemporaryNpcs.Count; i++)
            {
                ZombieModeTemporaryNpc npc = zombieModeRunState.TemporaryNpcs[i];
                if (npc != null &&
                    zombieModeRunState.TemporaryNpcs[i].ServiceState != null &&
                    string.Equals(npc.ServiceType, serviceType, System.StringComparison.Ordinal))
                {
                    return npc;
                }
            }

            return null;
        }

        public ZombieModeNpcCatalog.MerchantStockEntry[] GetZombieModeMerchantStock(int runId, string serviceType)
        {
            ZombieModeTemporaryNpc npc = IsZombieModeRunValid(runId) ? FindZombieModeTemporaryNpc(serviceType) : null;
            if (npc == null || npc.ServiceState == null)
            {
                return new ZombieModeNpcCatalog.MerchantStockEntry[0];
            }

            return npc.ServiceState.BossNodeStock || zombieModeRunState.CurrentWave > 0 && IsZombieModeBossWave(zombieModeRunState.CurrentWave)
                ? ZombieModeNpcCatalog.BossNodeStock
                : ZombieModeNpcCatalog.NormalWaveStock;
        }

        public ZombieModeNpcCatalog.NurseServiceEntry[] GetZombieModeNurseServices(int runId, string serviceType)
        {
            return IsZombieModeRunValid(runId) ? ZombieModeNpcCatalog.NurseServices : new ZombieModeNpcCatalog.NurseServiceEntry[0];
        }

        public int GetZombieModeNpcServiceRemaining(int runId, string serviceType, int index)
        {
            ZombieModeTemporaryNpc npc = IsZombieModeRunValid(runId) ? FindZombieModeTemporaryNpc(serviceType) : null;
            if (npc == null || npc.ServiceState == null || index < 0)
            {
                return 0;
            }

            if (string.Equals(serviceType, "Nurse", System.StringComparison.Ordinal))
            {
                return index < npc.ServiceState.NurseUsesRemaining.Count ? npc.ServiceState.NurseUsesRemaining[index] : 0;
            }

            return index < npc.ServiceState.MerchantStockRemaining.Count ? npc.ServiceState.MerchantStockRemaining[index] : 0;
        }

        public int GetZombieModeNpcServicePrice(int runId, int basePrice)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.FloorToInt(basePrice * GetZombieModeNpcServicePriceMultiplier()));
        }

        public bool TryPurchaseZombieModeMerchantStock(int runId, string serviceType, int stockIndex)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            ZombieModeTemporaryNpc npc = FindZombieModeTemporaryNpc(serviceType);
            ZombieModeNpcCatalog.MerchantStockEntry[] stock = GetZombieModeMerchantStock(runId, serviceType);
            if (npc == null || npc.ServiceState == null || stockIndex < 0 || stockIndex >= stock.Length || stockIndex >= npc.ServiceState.MerchantStockRemaining.Count)
            {
                return false;
            }

            if (npc.ServiceState.MerchantStockRemaining[stockIndex] <= 0)
            {
                return false;
            }

            ZombieModeNpcCatalog.MerchantStockEntry entry = stock[stockIndex];
            int cost = GetZombieModeNpcServicePrice(runId, entry.BasePrice);
            if (!SpendZombieModePurificationPoints(cost, "ZombieModeMerchantService"))
            {
                return false;
            }

            bool granted = false;
            if (entry.TypeId > 0)
            {
                granted = TryGiveZombieModeItemToPlayerOrDrop(entry.TypeId);
            }
            else if (!string.IsNullOrEmpty(entry.GrantTag))
            {
                if (IsZombieModeMerchantBulletStock(entry))
                {
                    granted = TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry);
                }

                if (!granted)
                {
                    granted = TryPurchaseZombieModeGuaranteedMerchantStockFromPool(entry);
                }
                if (!granted)
                {
                    granted = TryGiveRandomZombieModeMerchantItemFromModeEPool(entry);
                }
            }

            if (!granted)
            {
                RefundZombieModePurificationPoints(cost, "ZombieModeMerchantServiceFailed");
                return false;
            }

            npc.ServiceState.MerchantStockRemaining[stockIndex]--;
            NotificationText.Push(L10n.T(entry.DisplayKey));
            return true;
        }

        private bool IsZombieModeMerchantBulletStock(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            return entry != null &&
                   (string.Equals(entry.GrantTag, "Bullet", System.StringComparison.Ordinal) ||
                    string.Equals(entry.GrantTag, "Ammo", System.StringComparison.Ordinal));
        }

        private bool TryGiveZombieModeMerchantAmmoForEquippedWeapon(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (zombieModeRunState.GuaranteedMerchantPurchasePending)
            {
                int guaranteedMinQuality = Mathf.Max(entry.GrantMinQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
                int guaranteedMaxQuality = Mathf.Max(entry.GrantMaxQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
                if (TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry, guaranteedMinQuality, guaranteedMaxQuality))
                {
                    zombieModeRunState.GuaranteedMerchantPurchasePending = false;
                    zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 0;
                    return true;
                }
            }

            return TryGiveZombieModeMerchantAmmoForEquippedWeapon(entry, entry.GrantMinQuality, entry.GrantMaxQuality);
        }

        private bool TryGiveZombieModeMerchantAmmoForEquippedWeapon(ZombieModeNpcCatalog.MerchantStockEntry entry, int minQuality, int maxQuality)
        {
            Item weapon = TryGetZombieModePreferredWeaponForMerchantAmmo();
            string caliber = TryReadZombieModeItemCaliber(weapon);
            if (string.IsNullOrEmpty(caliber))
            {
                return false;
            }

            return TryGiveZombieModeAmmo(caliber, 100, minQuality, maxQuality);
        }

        private Item TryGetZombieModePreferredWeaponForMerchantAmmo()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return null;
            }

            Slot primarySlot = player.PrimWeaponSlot();
            Item primary = primarySlot != null ? primarySlot.Content : null;
            if (IsZombieModeAmmoTargetWeapon(primary))
            {
                return primary;
            }

            Slot secondarySlot = player.SecWeaponSlot();
            Item secondary = secondarySlot != null ? secondarySlot.Content : null;
            return IsZombieModeAmmoTargetWeapon(secondary) ? secondary : null;
        }

        private bool IsZombieModeAmmoTargetWeapon(Item item)
        {
            return item != null &&
                   item.GetComponent<ItemSetting_Gun>() != null &&
                   !string.IsNullOrEmpty(TryReadZombieModeItemCaliber(item));
        }

        private bool TryPurchaseZombieModeGuaranteedMerchantStockFromPool(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null ||
                string.IsNullOrEmpty(entry.GrantTag) ||
                !zombieModeRunState.GuaranteedMerchantPurchasePending)
            {
                return false;
            }

            int maxQuality = Mathf.Min(entry.GrantMaxQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
            maxQuality = Mathf.Max(maxQuality, zombieModeRunState.GuaranteedMerchantPurchaseMinQuality);
            int minQuality = Mathf.Max(entry.GrantMinQuality, 1);
            if (maxQuality < minQuality)
            {
                return false;
            }

            int[] poolIds = GetZombieModeMerchantModeECategoryPoolIds(entry);
            for (int quality = maxQuality; quality >= minQuality; quality--)
            {
                int typeId = PickZombieModeStrictQualityCandidate(poolIds, quality, quality);
                if (typeId <= 0)
                {
                    string[] grantTags = GetZombieModeMerchantGrantTagAliases(entry.GrantTag);
                    typeId = FindRandomItemTypeByTags(grantTags, quality, quality);
                }

                if (!TryGiveZombieModeItemToPlayerOrDrop(typeId))
                {
                    continue;
                }

                zombieModeRunState.GuaranteedMerchantPurchasePending = false;
                zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 0;
                return true;
            }

            return false;
        }

        private bool TryGiveRandomZombieModeMerchantItemFromModeEPool(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            int[] poolIds = GetZombieModeMerchantModeECategoryPoolIds(entry);
            int typeId = PickZombieModeStrictQualityCandidate(poolIds, entry.GrantMinQuality, entry.GrantMaxQuality);
            if (TryGiveZombieModeItemToPlayerOrDrop(typeId))
            {
                return true;
            }

            string[] grantTags = GetZombieModeMerchantGrantTagAliases(entry.GrantTag);
            return TryGiveRandomItemByTags(grantTags, entry.GrantMinQuality, entry.GrantMaxQuality);
        }

        private int[] GetZombieModeMerchantModeECategoryPoolIds(ZombieModeNpcCatalog.MerchantStockEntry entry)
        {
            if (entry == null)
            {
                return new int[0];
            }

            string suffix = GetZombieModeMerchantModeECategorySuffix(entry.GrantTag);
            if (string.IsNullOrEmpty(suffix))
            {
                return new int[0];
            }

            return GetModeEMerchantCategoryPoolIds(suffix);
        }

        public bool TryUseZombieModeNurseService(int runId, string serviceType, int serviceIndex)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            ZombieModeTemporaryNpc npc = FindZombieModeTemporaryNpc(serviceType);
            ZombieModeNpcCatalog.NurseServiceEntry[] services = ZombieModeNpcCatalog.NurseServices;
            if (npc == null || npc.ServiceState == null || serviceIndex < 0 || serviceIndex >= services.Length || serviceIndex >= npc.ServiceState.NurseUsesRemaining.Count)
            {
                return false;
            }

            if (npc.ServiceState.NurseUsesRemaining[serviceIndex] <= 0)
            {
                return false;
            }

            int cost = GetZombieModeNpcServicePrice(runId, services[serviceIndex].BasePrice);
            if (!SpendZombieModePurificationPoints(cost, "ZombieModeNurseService"))
            {
                return false;
            }

            if (!ApplyZombieModeNurseServiceEffect(serviceIndex))
            {
                RefundZombieModePurificationPoints(cost, "ZombieModeNurseServiceFailed");
                return false;
            }

            npc.ServiceState.NurseUsesRemaining[serviceIndex]--;
            NotificationText.Push(L10n.T(services[serviceIndex].ServiceKey));
            return true;
        }

        private bool ApplyZombieModeNurseServiceEffect(int serviceIndex)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return false;
            }

            float missing = Mathf.Max(0f, player.Health.MaxHealth - player.Health.CurrentHealth);
            if (serviceIndex == 0)
            {
                float heal = Mathf.Min(missing, player.Health.MaxHealth * 0.5f);
                player.Health.SetHealth(player.Health.CurrentHealth + Mathf.Max(1f, heal));
                return true;
            }

            if (serviceIndex == 1)
            {
                player.Health.SetHealth(player.Health.MaxHealth);
                return true;
            }

            if (serviceIndex == 2)
            {
                return ClearZombieModeNurseNegativeBuffs(player, true, false, false);
            }

            if (serviceIndex == 3)
            {
                return ClearZombieModeNurseNegativeBuffs(player, false, true, false);
            }

            if (serviceIndex == 4)
            {
                player.Health.SetHealth(player.Health.MaxHealth);
                return ClearZombieModeNurseNegativeBuffs(player, true, true, true);
            }

            return false;
        }

        private bool ClearZombieModeNurseNegativeBuffs(CharacterMainControl player, bool clearPoison, bool clearBleeding, bool clearExtendedNegative)
        {
            if (player == null)
            {
                return false;
            }

            bool changed = false;
            if (clearPoison)
            {
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Poison);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Nauseous);
            }

            if (clearBleeding)
            {
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Bleeding);
            }

            if (clearExtendedNegative)
            {
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Pain);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Burning);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Electric);
                changed |= TryRemoveZombieModeBuffsByTag(player, Buff.BuffExclusiveTags.Freeze);
            }

            return changed || clearPoison || clearBleeding || clearExtendedNegative;
        }

        private bool TryRemoveZombieModeBuffsByTag(CharacterMainControl player, Buff.BuffExclusiveTags tag)
        {
            if (player == null || tag == Buff.BuffExclusiveTags.NotExclusive)
            {
                return false;
            }

            try
            {
                player.RemoveBuffsByTag(tag, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private float GetZombieModeNpcServicePriceMultiplier()
        {
            return ZombieModeNpcCatalog.GetPollutionPriceMultiplier(zombieModeRunState.TotalPollution);
        }

        private bool TryGiveZombieModeItemToPlayerOrDrop(int typeId)
        {
            if (typeId <= 0)
            {
                return false;
            }

            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    return false;
                }

                return TryDeliverZombieModeItemToPlayerOrDrop(item, "TypeId_" + typeId);
            }
            catch
            {
                try
                {
                    if (item != null)
                    {
                        item.DestroyTree();
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] reward item DestroyTree 失败: " + e.Message);
                }
                return false;
            }
        }

        private bool TryDeliverZombieModeItemToPlayerOrDrop(Item item, string logContext)
        {
            if (item == null)
            {
                return false;
            }

            try
            {
                bool sent = false;
                try
                {
                    sent = ItemUtilities.SendToPlayerCharacterInventory(item, false);
                }
                catch (System.Exception e)
                {
                    DevLog("[ZombieMode] SendToPlayerCharacterInventory 失败: " + e.Message);
                }

                if (sent)
                {
                    return true;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 dropPosition = player != null
                    ? player.transform.position + Vector3.up * 0.3f
                    : (zombieModeRunState.ActiveSafeZoneActive ? zombieModeRunState.ActiveSafeZoneCenter + Vector3.up * 0.3f : Vector3.up * 0.3f);
                item.Drop(dropPosition, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                return true;
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] reward item deliver failed: " + logContext + ", " + e.Message);
                try
                {
                    item.DestroyTree();
                }
                catch (System.Exception destroyEx)
                {
                    DevLog("[ZombieMode] reward item DestroyTree 失败: " + destroyEx.Message);
                }

                return false;
            }
        }

        private void GrantZombieModeFallbackPurificationReward(string reason, int points)
        {
            points = Mathf.Max(1, points);
            zombieModeRunState.PurificationPoints += points;
            NotificationText.Push(string.Format(
                L10n.T("BossRush_ZombieMode_Notify_RewardFallbackPurification"),
                points));
            DevLog("[ZombieMode] Reward fallback purification: " + reason + ", points=" + points);
        }

        private int CalculateZombieModePurificationRewardPoints(bool bossNode)
        {
            int basePoints = bossNode ? ZombieModeTuning.InstantPurificationBossBase : ZombieModeTuning.InstantPurificationNormalBase;
            int pollutionBonus = (bossNode ? 25 : 10) * zombieModeRunState.TotalPollution;
            return Mathf.Max(1, basePoints + pollutionBonus);
        }

        private void ClearZombieModeRewardShell()
        {
            if (zombieModeRewardUiRoot != null)
            {
                Destroy(zombieModeRewardUiRoot);
                zombieModeRewardUiRoot = null;
            }
        }
    }
}
