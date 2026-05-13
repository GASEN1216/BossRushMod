using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private bool TrySwapModeFItemsBetweenCharacters(
            CharacterMainControl receiver,
            CharacterMainControl donor,
            Item donorItem,
            Item receiverItem,
            Vector3 lootDropPosition)
        {
            if (receiver == null || donor == null || donorItem == null)
            {
                return false;
            }

            if (receiver.CharacterItem == null || donor.CharacterItem == null)
            {
                return false;
            }

            try
            {
                donorItem.Detach();
            }
            catch
            {
                return false;
            }

            bool receiverItemDetached = false;
            if (receiverItem != null)
            {
                try
                {
                    receiverItem.Detach();
                    receiverItemDetached = true;
                }
                catch
                {
                    if (!TryRestoreModeFItemToCharacter(donor, donorItem))
                    {
                        DropOrDestroyModeFLootedItem(donor, donorItem, lootDropPosition);
                    }
                    return false;
                }
            }

            if (!TryEquipModeFLootedItem(receiver, donorItem))
            {
                if (!TryRestoreModeFItemToCharacter(donor, donorItem))
                {
                    DropOrDestroyModeFLootedItem(donor, donorItem, lootDropPosition);
                }
                if (receiverItemDetached && receiverItem != null)
                {
                    if (!TryRestoreModeFItemToCharacter(receiver, receiverItem))
                    {
                        DropOrDestroyModeFLootedItem(receiver, receiverItem, lootDropPosition);
                    }
                }

                return false;
            }

            if (receiverItem == null)
            {
                return true;
            }

            if (TryEquipModeFLootedItem(donor, receiverItem))
            {
                return true;
            }

            try { donorItem.Detach(); } catch { }
            bool donorRestored = TryRestoreModeFItemToCharacter(donor, donorItem);
            bool receiverRestored = TryRestoreModeFItemToCharacter(receiver, receiverItem);
            if (!donorRestored || !receiverRestored)
            {
                if (!donorRestored)
                {
                    DropOrDestroyModeFLootedItem(donor, donorItem, lootDropPosition);
                }

                if (!receiverRestored)
                {
                    DropOrDestroyModeFLootedItem(receiver, receiverItem, lootDropPosition);
                }

                DevLog("[ModeF] [WARNING] Boss 装备互换回滚不完整");
            }

            return false;
        }

        private bool TryRestoreModeFItemToCharacter(CharacterMainControl owner, Item item)
        {
            if (owner == null || owner.CharacterItem == null || item == null)
            {
                return false;
            }

            try
            {
                return owner.CharacterItem.TryPlug(item, true, null, 0);
            }
            catch
            {
                return false;
            }
        }

        private void RefillModeFBossGunAndAmmo(CharacterMainControl owner, Item gunItem, Vector3 lootDropPosition, bool ensureSpareAmmo = true)
        {
            try
            {
                if (owner == null || owner.CharacterItem == null || gunItem == null) return;

                ItemSetting_Gun gunSetting = gunItem.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null) return;

                int capacity = Mathf.Max(1, gunSetting.Capacity);

                try
                {
                    if (modeFGunBulletCountCacheField != null)
                    {
                        modeFGunBulletCountCacheField.SetValue(gunSetting, capacity);
                    }

                    int bulletCountHash = "BulletCount".GetHashCode();
                    if (modeFGunBulletCountHashField != null)
                    {
                        bulletCountHash = (int)modeFGunBulletCountHashField.GetValue(gunSetting);
                    }

                    gunItem.Variables.SetInt(bulletCountHash, capacity);
                }
                catch { }

                int bulletTypeId = gunSetting.TargetBulletID;
                if (!ensureSpareAmmo || bulletTypeId <= 0)
                {
                    return;
                }

                if (bulletTypeId > 0)
                {
                    Item bullet = ItemAssetsCollection.InstantiateSync(bulletTypeId);
                    if (bullet != null)
                    {
                        try
                        {
                            bullet.StackCount = Mathf.Max(1, bullet.MaxStackCount);
                        }
                        catch
                        {
                            bullet.StackCount = 1;
                        }

                        bool plugged = false;
                        try
                        {
                            plugged = owner.CharacterItem.TryPlug(bullet, true, null, 0);
                        }
                        catch { }

                        if (!plugged)
                        {
                            if (!TryReplaceModeFLowerQualityAmmo(owner, bullet, bulletTypeId))
                            {
                                DropOrDestroyModeFLootedItem(owner, bullet, lootDropPosition);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RefillModeFBossGunAndAmmo 失败: " + e.Message);
            }
        }

        private int TransferModeFBossCompatibleAmmo(CharacterMainControl receiver, CharacterMainControl donor, Item gunItem, Vector3 lootDropPosition)
        {
            if (receiver == null || donor == null || gunItem == null)
            {
                return 0;
            }

            Inventory donorInventory = null;
            try { donorInventory = donor.CharacterItem != null ? donor.CharacterItem.Inventory : null; } catch { }
            if (donorInventory == null)
            {
                return 0;
            }

            int bulletTypeId = 0;
            string targetCaliber = null;
            if (!TryGetModeFGunAmmoRequirements(gunItem, out bulletTypeId, out targetCaliber))
            {
                return 0;
            }

            modeFTransferableAmmoScratch.Clear();
            try
            {
                foreach (Item candidate in donorInventory)
                {
                    if (IsModeFCompatibleAmmoForGun(candidate, bulletTypeId, targetCaliber))
                    {
                        modeFTransferableAmmoScratch.Add(candidate);
                    }
                }
            }
            catch { }

            modeFTransferableAmmoScratch.Sort((a, b) =>
            {
                bool aExact = a != null && a.TypeID == bulletTypeId;
                bool bExact = b != null && b.TypeID == bulletTypeId;
                if (aExact != bExact)
                {
                    return bExact.CompareTo(aExact);
                }

                int aQuality = a != null ? a.Quality : 0;
                int bQuality = b != null ? b.Quality : 0;
                return bQuality.CompareTo(aQuality);
            });

            int transferred = 0;
            for (int i = 0; i < modeFTransferableAmmoScratch.Count; i++)
            {
                Item ammo = modeFTransferableAmmoScratch[i];
                if (ammo == null)
                {
                    continue;
                }

                bool detached = false;
                try
                {
                    ammo.Detach();
                    detached = true;
                }
                catch { }

                if (!detached)
                {
                    continue;
                }

                bool plugged = false;
                try
                {
                    plugged = receiver.CharacterItem != null && receiver.CharacterItem.TryPlug(ammo, true, null, 0);
                }
                catch { }

                if (!plugged)
                {
                    plugged = TryReplaceModeFLowerQualityAmmo(receiver, ammo, ammo.TypeID);
                }

                if (plugged)
                {
                    transferred++;
                }
                else
                {
                    DropOrDestroyModeFLootedItem(receiver, ammo, lootDropPosition);
                }
            }

            if (transferred > 0)
            {
                DevLog("[ModeF] Boss 换枪同步转移兼容弹药: " + transferred + " 组");
            }

            modeFTransferableAmmoScratch.Clear();
            return transferred;
        }

        private bool HasModeFCompatibleAmmo(CharacterMainControl owner, Item gunItem)
        {
            if (owner == null || gunItem == null)
            {
                return false;
            }

            Inventory inventory = null;
            try { inventory = owner.CharacterItem != null ? owner.CharacterItem.Inventory : null; } catch { }
            if (inventory == null)
            {
                return false;
            }

            int bulletTypeId = 0;
            string targetCaliber = null;
            if (!TryGetModeFGunAmmoRequirements(gunItem, out bulletTypeId, out targetCaliber))
            {
                return false;
            }

            try
            {
                foreach (Item candidate in inventory)
                {
                    if (IsModeFCompatibleAmmoForGun(candidate, bulletTypeId, targetCaliber))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool TryGetModeFGunAmmoRequirements(Item gunItem, out int bulletTypeId, out string targetCaliber)
        {
            bulletTypeId = 0;
            targetCaliber = null;

            if (gunItem == null)
            {
                return false;
            }

            ItemSetting_Gun gunSetting = gunItem.GetComponent<ItemSetting_Gun>();
            if (gunSetting == null)
            {
                return false;
            }

            bulletTypeId = gunSetting.TargetBulletID;
            if (bulletTypeId > 0)
            {
                string cachedCaliber;
                if (modeFBulletCaliberCache.TryGetValue(bulletTypeId, out cachedCaliber))
                {
                    targetCaliber = string.IsNullOrEmpty(cachedCaliber) ? null : cachedCaliber;
                }
                else
                {
                    try
                    {
                        Item bulletMeta = ItemAssetsCollection.InstantiateSync(bulletTypeId);
                        if (bulletMeta != null)
                        {
                            try { targetCaliber = bulletMeta.Constants != null ? bulletMeta.Constants.GetString("Caliber", null) : null; } catch { }
                            try { bulletMeta.DestroyTree(); } catch { }
                        }
                    }
                    catch { }

                    modeFBulletCaliberCache[bulletTypeId] = targetCaliber ?? string.Empty;
                }
            }

            return bulletTypeId > 0 || !string.IsNullOrEmpty(targetCaliber);
        }

        private bool IsModeFCompatibleAmmoForGun(Item ammoItem, int bulletTypeId, string targetCaliber)
        {
            if (ammoItem == null)
            {
                return false;
            }

            if (!DragonKingBossGunProfiles.IsBulletLike(ammoItem))
            {
                return false;
            }

            if (bulletTypeId > 0 && ammoItem.TypeID == bulletTypeId)
            {
                return true;
            }

            if (string.IsNullOrEmpty(targetCaliber))
            {
                return false;
            }

            string ammoCaliber = null;
            try { ammoCaliber = ammoItem.Constants != null ? ammoItem.Constants.GetString("Caliber", null) : null; } catch { }
            return string.Equals(ammoCaliber, targetCaliber, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryReplaceModeFLowerQualityAmmo(CharacterMainControl owner, Item newBullet, int preferredTypeId = 0)
        {
            if (owner == null || owner.CharacterItem == null || newBullet == null)
            {
                return false;
            }

            Inventory inventory = null;
            try { inventory = owner.CharacterItem.Inventory; } catch { }
            if (inventory == null)
            {
                return false;
            }

            string targetCaliber = null;
            try { targetCaliber = newBullet.Constants != null ? newBullet.Constants.GetString("Caliber", null) : null; } catch { }
            bool matchByType = preferredTypeId > 0;
            bool matchByCaliber = !string.IsNullOrEmpty(targetCaliber);
            if (!matchByType && !matchByCaliber)
            {
                return false;
            }

            Item replaceCandidate = null;
            try
            {
                foreach (Item candidate in inventory)
                {
                    if (candidate == null || candidate == newBullet)
                    {
                        continue;
                    }

                    if (!DragonKingBossGunProfiles.IsBulletLike(candidate))
                    {
                        continue;
                    }

                    bool exactTypeMatch = matchByType && candidate.TypeID == preferredTypeId;
                    string candidateCaliber = null;
                    if (!exactTypeMatch && matchByCaliber)
                    {
                        try { candidateCaliber = candidate.Constants != null ? candidate.Constants.GetString("Caliber", null) : null; } catch { }
                    }

                    bool sameAmmoGroup = exactTypeMatch ||
                        (matchByCaliber && string.Equals(candidateCaliber, targetCaliber, StringComparison.OrdinalIgnoreCase));
                    if (!sameAmmoGroup)
                    {
                        continue;
                    }

                    if (candidate.Quality >= newBullet.Quality)
                    {
                        continue;
                    }

                    if (replaceCandidate == null || candidate.Quality < replaceCandidate.Quality)
                    {
                        replaceCandidate = candidate;
                    }
                }
            }
            catch { }

            if (replaceCandidate == null)
            {
                return false;
            }

            bool detached = false;
            try
            {
                replaceCandidate.Detach();
                detached = true;
            }
            catch { }

            if (!detached)
            {
                return false;
            }

            bool plugged = false;
            try
            {
                plugged = owner.CharacterItem.TryPlug(newBullet, true, null, 0);
            }
            catch { }

            if (plugged)
            {
                try { replaceCandidate.DestroyTree(); } catch { }
                DevLog("[ModeF] 新枪补给弹药：已替换同口径低品质弹药");
                return true;
            }

            bool restored = false;
            try
            {
                restored = owner.CharacterItem.TryPlug(replaceCandidate, true, null, 0);
            }
            catch { }

            if (!restored)
            {
                DevLog("[ModeF] [WARNING] 同口径低品质弹药回滚失败");
                try { replaceCandidate.DestroyTree(); } catch { }
            }

            return false;
        }

        private Vector3 GetModeFBountyDropPosition(CharacterMainControl victim, CharacterMainControl fallbackOwner = null)
        {
            try
            {
                if (victim != null && victim.transform != null)
                {
                    return victim.transform.position + Vector3.up * 0.5f;
                }
            }
            catch { }

            try
            {
                if (fallbackOwner != null && fallbackOwner.transform != null)
                {
                    return fallbackOwner.transform.position + Vector3.up * 0.5f;
                }
            }
            catch { }

            return Vector3.up * 0.5f;
        }

        private void QueueModeFLeaderChangeContext(CharacterMainControl killer, CharacterMainControl victim)
        {
            string killerName = GetModeFActorDisplayName(killer, true);
            string victimName = GetModeFActorDisplayName(victim, false);

            modeFBountyLeaderContextZh = "<color=orange>" + killerName + "</color> 杀死了 <color=red>" + victimName
                + "</color>，并成为悬赏榜首！";
            modeFBountyLeaderContextEn = "<color=orange>" + killerName + "</color> killed <color=red>" + victimName
                + "</color> and became the Bounty Leader!";
        }

        private bool TryConsumeModeFLeaderChangeContext(out string zh, out string en)
        {
            zh = modeFBountyLeaderContextZh;
            en = modeFBountyLeaderContextEn;

            if (string.IsNullOrEmpty(zh) || string.IsNullOrEmpty(en))
            {
                ClearModeFLeaderChangeContext();
                return false;
            }

            modeFBountyLeaderContextZh = null;
            modeFBountyLeaderContextEn = null;
            return true;
        }

        private void ClearModeFLeaderChangeContext()
        {
            modeFBountyLeaderContextZh = null;
            modeFBountyLeaderContextEn = null;
        }

        private void CheckAndBroadcastLeaderChange(CharacterMainControl preferredLeader = null)
        {
            try
            {
                PruneModeFBountyMarksByCharacterId();

                CharacterMainControl oldLeader = modeFState.CurrentBountyLeader;
                int oldLeaderMarks = modeFState.CurrentBountyLeaderMarks;
                CharacterMainControl newLeader = null;
                int maxMarks = 0;

                for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
                {
                    CharacterMainControl candidate = modeFState.ActiveBosses[i];
                    if (candidate == null || candidate.Health == null || candidate.Health.IsDead)
                    {
                        continue;
                    }

                    int candidateMarks = 0;
                    modeFState.BountyMarksByCharacterId.TryGetValue(candidate.GetInstanceID(), out candidateMarks);
                    if (candidateMarks <= 0)
                    {
                        continue;
                    }

                    if (candidateMarks > maxMarks)
                    {
                        maxMarks = candidateMarks;
                        newLeader = candidate;
                    }
                }

                if (modeFState.PlayerBountyMarks > maxMarks)
                {
                    maxMarks = modeFState.PlayerBountyMarks;
                    newLeader = null;
                }

                if (preferredLeader == CharacterMainControl.Main && modeFState.PlayerBountyMarks == maxMarks && maxMarks > 0)
                {
                    newLeader = null;
                }
                else if (preferredLeader != null && preferredLeader != CharacterMainControl.Main)
                {
                    int preferredMarks = 0;
                    if (modeFState.BountyMarksByCharacterId.TryGetValue(preferredLeader.GetInstanceID(), out preferredMarks)
                        && preferredMarks == maxMarks && maxMarks > 0)
                    {
                        newLeader = preferredLeader;
                    }
                }
                else if (oldLeader != null && oldLeader.Health != null && !oldLeader.Health.IsDead)
                {
                    int oldMarks = 0;
                    if (modeFState.BountyMarksByCharacterId.TryGetValue(oldLeader.GetInstanceID(), out oldMarks)
                        && oldMarks == maxMarks && maxMarks > 0)
                    {
                        newLeader = oldLeader;
                    }
                }

                if (maxMarks <= 0)
                {
                    newLeader = null;
                }

                if (maxMarks != oldLeaderMarks || newLeader != oldLeader)
                {
                    modeFState.CurrentBountyLeader = newLeader;
                    modeFState.CurrentBountyLeaderMarks = maxMarks;

                    if (maxMarks > 0)
                    {
                        BroadcastModeFLeaderChange(newLeader, maxMarks);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] CheckAndBroadcastLeaderChange 失败: " + e.Message);
            }
        }

        private void PruneModeFBountyMarksByCharacterId()
        {
            if (modeFState.BountyMarksByCharacterId.Count <= 0)
            {
                return;
            }

            modeFActiveBossIdScratch.Clear();
            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                CharacterMainControl boss = modeFState.ActiveBosses[i];
                if (boss == null || boss.Health == null || boss.Health.IsDead)
                {
                    continue;
                }

                modeFActiveBossIdScratch.Add(boss.GetInstanceID());
            }

            modeFStaleBountyIdScratch.Clear();
            foreach (var kvp in modeFState.BountyMarksByCharacterId)
            {
                if (kvp.Value > 0 && modeFActiveBossIdScratch.Contains(kvp.Key))
                {
                    continue;
                }

                modeFStaleBountyIdScratch.Add(kvp.Key);
            }

            for (int i = 0; i < modeFStaleBountyIdScratch.Count; i++)
            {
                modeFState.BountyMarksByCharacterId.Remove(modeFStaleBountyIdScratch[i]);
            }

            if (modeFStaleBountyIdScratch.Count > 0)
            {
                MarkModeFHealthBarNamesDirty();
            }
        }

        /// <summary>
        /// 为悬赏 Boss 添加额外掉落。
        /// 当前实现直接复用共享高品质奖励池，候选规则与 GetRandomInfiniteHellHighQualityRewardTypeID 保持一致。
        /// </summary>
        private void AddBountyBossExtraLoot(CharacterMainControl victim, int marks)
        {
            try
            {
                if (marks <= 0) return;

                Vector3 dropPos = victim.transform.position + Vector3.up * 0.5f;

                for (int i = 0; i < marks; i++)
                {
                    try
                    {
                        int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                        if (rewardTypeId <= 0) continue;

                        Item reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                        if (reward != null)
                        {
                            reward.Drop(dropPos, true, UnityEngine.Random.insideUnitSphere.normalized, 30f);
                        }
                    }
                    catch { }
                }

                DevLog("[ModeF] 悬赏 Boss 额外掉落: " + marks + " 格高品质奖励");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] AddBountyBossExtraLoot 失败: " + e.Message);
            }
        }

    }
}
