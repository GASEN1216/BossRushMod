using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 悬赏系统

        /// <summary>Mode F Boss 成长 Modifier 缓存</summary>
        private readonly Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg)> modeFBossModifiers
            = new Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg)>();
        private bool modeFBountyLeaderDirty = false;
        private CharacterMainControl modeFBountyLeaderPreferred = null;
        private readonly HashSet<int> modeFActiveBossIdScratch = new HashSet<int>();
        private readonly List<int> modeFStaleBountyIdScratch = new List<int>();

        private void MarkModeFBountyLeaderDirty(CharacterMainControl preferredLeader = null)
        {
            modeFBountyLeaderDirty = true;
            if (preferredLeader != null)
            {
                modeFBountyLeaderPreferred = preferredLeader;
            }
        }

        private void RefreshModeFBountyLeaderIfDirty()
        {
            if (!modeFBountyLeaderDirty)
            {
                return;
            }

            CharacterMainControl preferredLeader = modeFBountyLeaderPreferred;
            modeFBountyLeaderDirty = false;
            modeFBountyLeaderPreferred = null;
            CheckAndBroadcastLeaderChange(preferredLeader);
        }

        /// <summary>
        /// 生成悬赏名单（第二阶段开始时调用）
        /// 抽取 40-50% 存活 Boss，向上取整，最少 5 个
        /// </summary>
        private void GenerateBountyList()
        {
            try
            {
                List<CharacterMainControl> alive = modeFState.ActiveBosses;
                if (alive == null || alive.Count == 0)
                {
                    DevLog("[ModeF] GenerateBountyList: 无存活 Boss");
                    return;
                }

                // 清理无效引用
                for (int i = alive.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl boss = alive[i];
                    if (boss == null || boss.gameObject == null || boss.Health == null || boss.Health.IsDead)
                    {
                        alive.RemoveAt(i);
                        CleanupModeFBossRuntimeState(boss);
                    }
                }

                int total = alive.Count;
                float ratio = UnityEngine.Random.Range(0.4f, 0.5f);
                int bountyCount = Mathf.CeilToInt(total * ratio);
                bountyCount = Mathf.Max(bountyCount, 5);
                bountyCount = Mathf.Min(bountyCount, total);

                // 随机抽取
                List<CharacterMainControl> shuffled = new List<CharacterMainControl>(alive);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    CharacterMainControl temp = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = temp;
                }

                modeFState.BountyMarksByCharacterId.Clear();
                modeFState.InitialBountyBossIds.Clear();

                for (int i = 0; i < bountyCount; i++)
                {
                    CharacterMainControl boss = shuffled[i];
                    int charId = boss.GetInstanceID();
                    modeFState.BountyMarksByCharacterId[charId] = 1;
                    modeFState.InitialBountyBossIds.Add(charId);
                }

                ApplyModeFPhasePressure();
                MarkModeFBountyLeaderDirty();
                RefreshModeFBountyLeaderIfDirty();
                DevLog("[ModeF] 悬赏名单已生成: " + bountyCount + "/" + total + " 个 Boss 被标记");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] GenerateBountyList 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Boss 被 Boss 击杀时的处理
        /// </summary>
        private void OnModeFBossKilledByBoss(CharacterMainControl killer, CharacterMainControl victim)
        {
            try
            {
                int victimId = victim.GetInstanceID();
                int killerId = killer.GetInstanceID();

                // 继承全部印记
                int victimMarks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(victimId, out victimMarks);

                if (victimMarks > 0)
                {
                    int killerMarks = 0;
                    modeFState.BountyMarksByCharacterId.TryGetValue(killerId, out killerMarks);
                    modeFState.BountyMarksByCharacterId[killerId] = killerMarks + victimMarks;
                    modeFState.BountyMarksByCharacterId.Remove(victimId);

                    DevLog("[ModeF] Boss 继承印记: killer=" + killer.gameObject.name + " +" + victimMarks + " (总计=" + (killerMarks + victimMarks) + ")");
                
                    // Boss 成长 +5% 生命/伤害
                    ApplyModeFBossGrowth(killer, 0.05f);

                    // 装备掠夺
                    CompareAndSwapEquipment(killer, victim);
                }

                ApplyModeFPhasePressure();

                MarkModeFBountyLeaderDirty(killer);
                RefreshModeFBountyLeaderIfDirty();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFBossKilledByBoss 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Boss 被玩家击杀时的处理
        /// </summary>
        private void OnModeFBossKilledByPlayer(CharacterMainControl victim)
        {
            try
            {
                int victimId = victim.GetInstanceID();
                int victimMarks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(victimId, out victimMarks);

                bool isBounty = victimMarks > 0;

                // 玩家击杀带印记 Boss 只加 1 印记
                if (isBounty)
                {
                    modeFState.PlayerBountyMarks += 1;
                    MarkModeFPlayerNameTagDirty();
                    DevLog("[ModeF] 玩家获得 +1 悬赏印记 (总计=" + modeFState.PlayerBountyMarks + ")");
                }

                modeFState.BountyMarksByCharacterId.Remove(victimId);
                modeFState.PlayerKillCount++;

                // 回血和最大生命成长
                ApplyModeFKillReward(isBounty);

                // 工事补给发放
                GrantModeFKillRewards(modeFState.PlayerKillCount);

                // 击杀奖励气泡
                string killMsg = isBounty
                    ? L10n.T("悬赏Boss击杀！+1印记", "Bounty Boss killed! +1 mark")
                    : L10n.T("Boss击杀！", "Boss killed!");
                ShowModeFRewardBubble(killMsg);

                ApplyModeFPhasePressure();

                MarkModeFBountyLeaderDirty(CharacterMainControl.Main);
                RefreshModeFBountyLeaderIfDirty();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFBossKilledByPlayer 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 对 Boss 应用成长（生命+伤害）
        /// </summary>
        private void ApplyModeFBossGrowth(CharacterMainControl boss, float growthPercent)
        {
            try
            {
                var characterItem = boss.CharacterItem;
                if (characterItem == null) return;

                // 移除旧 Modifier
                if (modeFBossModifiers.TryGetValue(boss, out var oldMods))
                {
                    try
                    {
                        Stat oldHpStat = characterItem.GetStat("MaxHealth");
                        if (oldHpStat != null && oldMods.hp != null) oldHpStat.RemoveModifier(oldMods.hp);
                    }
                    catch { }
                    try
                    {
                        Stat oldGunStat = characterItem.GetStat("GunDamageMultiplier");
                        if (oldGunStat != null && oldMods.gunDmg != null) oldGunStat.RemoveModifier(oldMods.gunDmg);
                    }
                    catch { }
                }

                // 计算累积成长倍率
                float existingGrowth = 0f;
                if (oldMods.hp != null)
                {
                    Stat hpStat = characterItem.GetStat("MaxHealth");
                    if (hpStat != null && hpStat.BaseValue > 0)
                    {
                        existingGrowth = oldMods.hp.Value / hpStat.BaseValue;
                    }
                }
                float totalGrowth = existingGrowth + growthPercent;

                Modifier newHpMod = null;
                Modifier newGunMod = null;

                // 生命值成长
                Stat maxHealthStat = characterItem.GetStat("MaxHealth");
                if (maxHealthStat != null)
                {
                    float hpDelta = maxHealthStat.BaseValue * totalGrowth;
                    if (hpDelta > 0)
                    {
                        float oldMaxHp = maxHealthStat.Value;
                        newHpMod = new Modifier(ModifierType.Add, hpDelta, this);
                        maxHealthStat.AddModifier(newHpMod);

                        // 当前血量同步提升
                        Health health = boss.Health;
                        if (health != null)
                        {
                            float actualIncrease = maxHealthStat.Value - oldMaxHp;
                            health.CurrentHealth = Mathf.Min(health.CurrentHealth + actualIncrease, maxHealthStat.Value);
                        }
                    }
                }

                // 伤害成长
                Stat gunDmgStat = characterItem.GetStat("GunDamageMultiplier");
                if (gunDmgStat != null)
                {
                    float gunDelta = gunDmgStat.BaseValue * totalGrowth;
                    if (gunDelta > 0)
                    {
                        newGunMod = new Modifier(ModifierType.Add, gunDelta, this);
                        gunDmgStat.AddModifier(newGunMod);
                    }
                }

                modeFBossModifiers[boss] = (newHpMod, newGunMod);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] ApplyModeFBossGrowth 失败: " + e.Message);
            }
        }

        private void RemoveModeFBossGrowthModifiers(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            (Modifier hp, Modifier gunDmg) oldMods;
            if (!modeFBossModifiers.TryGetValue(boss, out oldMods))
            {
                return;
            }

            try
            {
                if (!(boss == null))
                {
                    Item characterItem = boss.CharacterItem;
                    if (characterItem != null)
                    {
                        try
                        {
                            Stat oldHpStat = characterItem.GetStat("MaxHealth");
                            if (oldHpStat != null && oldMods.hp != null)
                            {
                                oldHpStat.RemoveModifier(oldMods.hp);
                            }
                        }
                        catch { }

                        try
                        {
                            Stat oldGunStat = characterItem.GetStat("GunDamageMultiplier");
                            if (oldGunStat != null && oldMods.gunDmg != null)
                            {
                                oldGunStat.RemoveModifier(oldMods.gunDmg);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            modeFBossModifiers.Remove(boss);
        }

        /// <summary>
        /// 比较并交换装备（头盔/护甲/枪）
        /// </summary>
        private void CompareAndSwapEquipment(CharacterMainControl killer, CharacterMainControl victim)
        {
            try
            {
                if (killer == null || victim == null) return;
                if (killer.CharacterItem == null || victim.CharacterItem == null) return;

                // 比较头盔
                TrySwapSlot(killer, victim, "Helmat");
                // 比较护甲
                TrySwapSlot(killer, victim, "Armor");
                // 比较枪
                TrySwapGun(killer, victim);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] CompareAndSwapEquipment 失败: " + e.Message);
            }
        }

        private void TrySwapSlot(CharacterMainControl killer, CharacterMainControl victim, string slotTag)
        {
            try
            {
                if (killer == null || victim == null) return;
                if (killer.CharacterItem == null || victim.CharacterItem == null) return;


                Item killerItem = GetModeFEquippedItemBySlot(killer, slotTag);
                Item victimItem = GetModeFEquippedItemBySlot(victim, slotTag);

                // 遍历 killer 的装备槽找到对应 tag 的物品

                if (victimItem == null) return;
                int victimQuality = victimItem.Quality;
                int killerQuality = killerItem != null ? killerItem.Quality : 0;

                if (victimQuality > killerQuality)
                {
                    // 替换：先卸下 killer 的，再装上 victim 的
                    TryReplaceModeFEquippedItem(killer, slotTag, victimItem, killerItem);
                    DevLog("[ModeF] Boss 换装 [" + slotTag + "]: Q" + killerQuality + " -> Q" + victimQuality);
                }
            }
            catch { }
        }

        private void TrySwapGun(CharacterMainControl killer, CharacterMainControl victim)
        {
            try
            {
                if (killer == null || victim == null) return;

                Item killerGun = killer.GetGun() != null ? killer.GetGun().Item : null;
                Item victimGun = victim.GetGun() != null ? victim.GetGun().Item : null;

                if (victimGun == null) return;
                int victimQuality = victimGun.Quality;
                int killerQuality = killerGun != null ? killerGun.Quality : 0;

                if (victimQuality > killerQuality)
                {
                    if (!TrySwapModeFLootedItemWithRollback(killer, victimGun, killerGun, "Gun"))
                    {
                        DevLog("[ModeF] [WARNING] Boss 换枪失败，已执行回滚/掉落保护");
                        return;
                    }

                    // 补满弹匣
                    RefillModeFBossGunAndAmmo(killer, victimGun);

                    DevLog("[ModeF] Boss 换枪: Q" + killerQuality + " -> Q" + victimQuality);
                }
            }
            catch { }
        }

        /// <summary>
        /// 检查并广播榜首变化
        /// </summary>
        private Item GetModeFEquippedItemBySlot(CharacterMainControl character, string slotKey)
        {
            try
            {
                if (character == null || character.CharacterItem == null || character.CharacterItem.Slots == null)
                {
                    return null;
                }

                try
                {
                    var indexedSlot = character.CharacterItem.Slots[slotKey];
                    if (indexedSlot != null)
                    {
                        return indexedSlot.Content;
                    }
                }
                catch { }

                try
                {
                    var namedSlot = character.CharacterItem.Slots.GetSlot(slotKey);
                    if (namedSlot != null)
                    {
                        return namedSlot.Content;
                    }
                }
                catch { }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void TryReplaceModeFEquippedItem(CharacterMainControl owner, string slotKey, Item newItem, Item oldItem)
        {
            try
            {
                if (owner == null || owner.CharacterItem == null || newItem == null) return;

                if (oldItem != null)
                {
                    if (!TrySwapModeFLootedItemWithRollback(owner, newItem, oldItem, slotKey))
                    {
                        DevLog("[ModeF] [WARNING] 替换装备失败，已执行回滚/掉落保护 slot=" + slotKey);
                    }
                    return;
                }

                if (!TryEquipModeFLootedItemOrDrop(owner, newItem))
                {
                    DevLog("[ModeF] [WARNING] 替换装备失败，已掉落战利品 slot=" + slotKey);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] TryReplaceModeFEquippedItem 失败: " + e.Message + " slot=" + slotKey);
            }
        }

        private bool TrySwapModeFLootedItemWithRollback(CharacterMainControl owner, Item newItem, Item oldItem, string slotKey)
        {
            if (owner == null || owner.CharacterItem == null || newItem == null)
            {
                return false;
            }

            bool oldDetached = false;
            if (oldItem != null)
            {
                try
                {
                    oldItem.Detach();
                    oldDetached = true;
                }
                catch { }
            }

            if (TryEquipModeFLootedItem(owner, newItem))
            {
                if (oldDetached && oldItem != null)
                {
                    try { oldItem.DestroyTree(); } catch { }
                }

                return true;
            }

            bool restored = false;
            if (oldDetached && oldItem != null)
            {
                try
                {
                    restored = owner.CharacterItem.TryPlug(oldItem, true, null, 0);
                }
                catch { }
            }

            DropOrDestroyModeFLootedItem(owner, newItem);
            if (oldDetached && oldItem != null && !restored)
            {
                DevLog("[ModeF] [WARNING] 旧装备回滚失败，已掉落 slot=" + slotKey);
                DropOrDestroyModeFLootedItem(owner, oldItem);
            }

            return false;
        }

        private bool TryEquipModeFLootedItemOrDrop(CharacterMainControl owner, Item item)
        {
            bool plugged = TryEquipModeFLootedItem(owner, item);
            if (!plugged)
            {
                DropOrDestroyModeFLootedItem(owner, item);
            }

            return plugged;
        }

        private bool TryEquipModeFLootedItem(CharacterMainControl owner, Item item)
        {
            if (owner == null || owner.CharacterItem == null || item == null)
            {
                return false;
            }

            try { item.Detach(); } catch { }

            try
            {
                return owner.CharacterItem.TryPlug(item, true, null, 0);
            }
            catch
            {
                return false;
            }
        }

        private void DropOrDestroyModeFLootedItem(CharacterMainControl owner, Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                if (owner != null && owner.transform != null)
                {
                    item.Drop(owner.transform.position + Vector3.up * 0.3f, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                }
                else if (item.gameObject != null)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                }
            }
            catch { }
        }

        private void RefillModeFBossGunAndAmmo(CharacterMainControl owner, Item gunItem)
        {
            try
            {
                if (owner == null || owner.CharacterItem == null || gunItem == null) return;

                ItemSetting_Gun gunSetting = gunItem.GetComponent<ItemSetting_Gun>();
                if (gunSetting == null) return;

                int capacity = Mathf.Max(1, gunSetting.Capacity);

                try
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    FieldInfo bulletCacheField = typeof(ItemSetting_Gun).GetField("_bulletCountCache", flags);
                    FieldInfo bulletHashField = typeof(ItemSetting_Gun).GetField("bulletCountHash", flags);
                    if (bulletCacheField != null)
                    {
                        bulletCacheField.SetValue(gunSetting, capacity);
                    }

                    int bulletCountHash = "BulletCount".GetHashCode();
                    if (bulletHashField != null)
                    {
                        bulletCountHash = (int)bulletHashField.GetValue(gunSetting);
                    }

                    gunItem.Variables.SetInt(bulletCountHash, capacity);
                }
                catch { }

                int bulletTypeId = gunSetting.TargetBulletID;
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

                        if (!plugged && owner.transform != null)
                        {
                            bullet.Drop(owner.transform.position + Vector3.up * 0.3f, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RefillModeFBossGunAndAmmo 失败: " + e.Message);
            }
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

                // 玩家印记也参与比较
                if (modeFState.PlayerBountyMarks > maxMarks)
                {
                    maxMarks = modeFState.PlayerBountyMarks;
                    newLeader = null; // null 表示玩家是榜首
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
        }

        /// <summary>
        /// 为悬赏 Boss 添加额外掉落
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
                        int rewardTypeId = GetModeFHighQualityRewardTypeID();
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

        /// <summary>M3: 已验证的高品质 TypeID 缓存，避免每次实例化物品仅为检查品质</summary>
        private readonly List<int> modeFVerifiedHighQualityTypeIds = new List<int>();
        private readonly HashSet<int> modeFVerifiedHighQualityTypeIdSet = new HashSet<int>();
        private bool modeFHighQualityPoolBuilt = false;

        /// <summary>
        /// 获取 Mode F 高品质奖励物品 TypeID（品质>=6）
        /// </summary>
        private int GetModeFHighQualityRewardTypeID()
        {
            // 如果缓存已构建，直接从缓存随机取
            if (modeFHighQualityPoolBuilt && modeFVerifiedHighQualityTypeIds.Count > 0)
            {
                return modeFVerifiedHighQualityTypeIds[UnityEngine.Random.Range(0, modeFVerifiedHighQualityTypeIds.Count)];
            }

            // 首次调用：构建缓存
            if (!modeFHighQualityPoolBuilt)
            {
                modeFHighQualityPoolBuilt = true;
                for (int probe = 0; probe < 40; probe++)
                {
                    int candidateId = GetRandomInfiniteHellHighQualityRewardTypeID();
                    if (candidateId <= 0) continue;

                    Item probeItem = null;
                    try
                    {
                        probeItem = ItemAssetsCollection.InstantiateSync(candidateId);
                        if (probeItem != null && probeItem.Quality >= 6 && modeFVerifiedHighQualityTypeIdSet.Add(candidateId))
                        {
                            modeFVerifiedHighQualityTypeIds.Add(candidateId);
                        }
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            if (probeItem != null && probeItem.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(probeItem.gameObject);
                            }
                        }
                        catch { }
                    }
                }

                if (modeFVerifiedHighQualityTypeIds.Count > 0)
                {
                    return modeFVerifiedHighQualityTypeIds[UnityEngine.Random.Range(0, modeFVerifiedHighQualityTypeIds.Count)];
                }
            }

            // 缓存为空时回退到原始逻辑
            for (int attempt = 0; attempt < 20; attempt++)
            {
                int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                if (rewardTypeId <= 0) continue;

                Item reward = null;
                try
                {
                    reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                    if (reward != null && reward.Quality >= 6)
                    {
                        if (modeFVerifiedHighQualityTypeIdSet.Add(rewardTypeId))
                        {
                            modeFVerifiedHighQualityTypeIds.Add(rewardTypeId);
                        }
                        return rewardTypeId;
                    }
                }
                catch { }
                finally
                {
                    try
                    {
                        if (reward != null && reward.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(reward.gameObject);
                        }
                    }
                    catch { }
                }
            }

            return -1;
        }

        #endregion
    }
}
