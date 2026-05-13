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
        #region Mode F 悬赏系统

        /// <summary>Mode F Boss 成长 Modifier 缓存</summary>
        private readonly Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg, Modifier meleeDmg)> modeFBossModifiers
            = new Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg, Modifier meleeDmg)>();
        private bool modeFBountyLeaderDirty = false;
        private CharacterMainControl modeFBountyLeaderPreferred = null;
        private string modeFBountyLeaderContextZh = null;
        private string modeFBountyLeaderContextEn = null;
        private readonly HashSet<int> modeFActiveBossIdScratch = new HashSet<int>();
        private readonly List<int> modeFStaleBountyIdScratch = new List<int>();
        private readonly Dictionary<int, int> modeFBossCarriedHighQualityLootCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> modeFBossPendingHighQualityLootPenaltyCounts = new Dictionary<int, int>();
        private readonly HashSet<int> modeFHandledPreLootPlunderVictimIds = new HashSet<int>();
        private readonly HashSet<int> modeFPlunderSeenItemIdScratch = new HashSet<int>();
        private readonly List<Item> modeFTransferableAmmoScratch = new List<Item>();
        private readonly Dictionary<int, string> modeFBulletCaliberCache = new Dictionary<int, string>();
        private static readonly BindingFlags ModeFPrivateInstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo modeFGunBulletCountCacheField =
            typeof(ItemSetting_Gun).GetField("_bulletCountCache", ModeFPrivateInstanceFlags);
        private static readonly FieldInfo modeFGunBulletCountHashField =
            typeof(ItemSetting_Gun).GetField("bulletCountHash", ModeFPrivateInstanceFlags);

        private bool ShouldUseModeFAbstractPlunderLootTracking()
        {
            return config != null && config.enableRandomBossLoot;
        }

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
            ClearModeFLeaderChangeContext();
        }

        /// <summary>
        /// 生成悬赏名单（第二阶段开始时调用）
        /// 当前规则：所有存活 Boss 初始都获得 1 层印记
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

                modeFState.BountyMarksByCharacterId.Clear();

                for (int i = 0; i < total; i++)
                {
                    CharacterMainControl boss = alive[i];
                    int charId = boss.GetInstanceID();
                    modeFState.BountyMarksByCharacterId[charId] = 1;
                    EnsureModeFBossNameTag(boss);
                }

                MarkModeFHealthBarNamesDirty();
                ApplyModeFPhasePressure();
                MarkModeFBountyLeaderDirty();
                RefreshModeFBountyLeaderIfDirty();
                DevLog("[ModeF] [BOUNTY] allAliveMarked=" + total);
                DevLog("[ModeF] 悬赏名单已生成: " + total + "/" + total + " 个 Boss 被标记");
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
                    MarkModeFHealthBarNamesDirty();

                    DevLog("[ModeF] Boss 继承印记: killer=" + killer.gameObject.name + " +" + victimMarks + " (总计=" + (killerMarks + victimMarks) + ")");

                    // Boss 成长按战胜目标的印记数结算，每层印记提供 5% 生命/伤害成长。
                    float growthPercent = 0.05f * victimMarks;
                    ApplyModeFBossGrowth(killer, growthPercent);
                    BroadcastModeFBossGrowth(killer, victim, growthPercent);
                }

                EnsureModeFBossNameTag(killer);
                ApplyModeFPhasePressure();

                QueueModeFLeaderChangeContext(killer, victim);
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
                    MarkModeFHealthBarNamesDirty();
                    MarkModeFPlayerNameTagDirty();
                    DevLog("[ModeF] 玩家获得 +1 悬赏印记 (总计=" + modeFState.PlayerBountyMarks + ")");
                }

                modeFState.BountyMarksByCharacterId.Remove(victimId);
                modeFState.PlayerKillCount++;

                // 回血和最大生命成长
                var killReward = ApplyModeFKillReward(isBounty, victimMarks);

                // 工事补给发放
                GrantModeFKillRewards(modeFState.PlayerKillCount);

                // 击杀奖励气泡
                string killMsg = BuildModeFKillRewardBubbleText(
                    isBounty,
                    killReward.healAmount,
                    killReward.maxHealthGain);
                ShowModeFRewardBubble(killMsg);

                ApplyModeFPhasePressure();

                QueueModeFLeaderChangeContext(CharacterMainControl.Main, victim);
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
                bool hadOldMods = modeFBossModifiers.TryGetValue(boss, out var oldMods);
                if (hadOldMods)
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
                    try
                    {
                        Stat oldMeleeStat = characterItem.GetStat("MeleeDamageMultiplier");
                        if (oldMeleeStat != null && oldMods.meleeDmg != null) oldMeleeStat.RemoveModifier(oldMods.meleeDmg);
                    }
                    catch { }
                }

                // 计算累积成长倍率
                float existingGrowth = 0f;
                if (hadOldMods && oldMods.hp != null)
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
                Modifier newMeleeMod = null;

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

                // 近战伤害成长
                Stat meleeDmgStat = characterItem.GetStat("MeleeDamageMultiplier");
                if (meleeDmgStat != null)
                {
                    float meleeDelta = meleeDmgStat.BaseValue * totalGrowth;
                    if (meleeDelta > 0)
                    {
                        newMeleeMod = new Modifier(ModifierType.Add, meleeDelta, this);
                        meleeDmgStat.AddModifier(newMeleeMod);
                    }
                }

                modeFBossModifiers[boss] = (newHpMod, newGunMod, newMeleeMod);
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

            (Modifier hp, Modifier gunDmg, Modifier meleeDmg) oldMods;
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
                        Stat oldHpStat = characterItem.GetStat("MaxHealth");
                        if (oldHpStat != null && oldMods.hp != null)
                        {
                            oldHpStat.RemoveModifier(oldMods.hp);
                        }

                        Stat oldGunStat = characterItem.GetStat("GunDamageMultiplier");
                        if (oldGunStat != null && oldMods.gunDmg != null)
                        {
                            oldGunStat.RemoveModifier(oldMods.gunDmg);
                        }

                        Stat oldMeleeStat = characterItem.GetStat("MeleeDamageMultiplier");
                        if (oldMeleeStat != null && oldMods.meleeDmg != null)
                        {
                            oldMeleeStat.RemoveModifier(oldMods.meleeDmg);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RemoveModeFBossGrowthModifiers failed: " + e.Message);
            }

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

                Vector3 lootDropPosition = GetModeFBountyDropPosition(victim, killer);

                // 比较头盔
                TrySwapSlot(killer, victim, "Helmat", lootDropPosition);
                // 比较护甲
                TrySwapSlot(killer, victim, "Armor", lootDropPosition);
                // 比较枪
                TrySwapGun(killer, victim, lootDropPosition);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] CompareAndSwapEquipment 失败: " + e.Message);
            }
        }

        private bool TryHandleModeFBossPreLootPlunder(CharacterMainControl killer, CharacterMainControl victim)
        {
            try
            {
                if (!modeFActive || killer == null || victim == null || object.ReferenceEquals(killer, victim))
                {
                    return false;
                }

                if (!IsTrackedModeFBoss(killer))
                {
                    return false;
                }

                int victimMarks = 0;
                try { modeFState.BountyMarksByCharacterId.TryGetValue(victim.GetInstanceID(), out victimMarks); } catch { }
                if (victimMarks <= 0)
                {
                    return false;
                }

                int victimId = victim.GetInstanceID();
                if (!modeFHandledPreLootPlunderVictimIds.Add(victimId))
                {
                    return false;
                }

                if (!ShouldUseModeFAbstractPlunderLootTracking())
                {
                    CompareAndSwapEquipment(killer, victim);
                    DevLog("[ModeF] Boss 预掠夺已执行即时换装，但当前关闭了随机掉落，跳过抽象战利品继承");
                    return true;
                }

                int physicalHighQualityCount = CountModeFPlunderableHighQualityItems(victim);
                CompareAndSwapEquipment(killer, victim);
                int carriedHighQualityCount = ConsumeModeFBossCarriedHighQualityLootCount(victim);
                int totalTransferredCount = physicalHighQualityCount + carriedHighQualityCount;

                if (physicalHighQualityCount > 0)
                {
                    AddModeFBossPendingHighQualityLootPenaltyCount(victim, physicalHighQualityCount);
                }

                if (totalTransferredCount > 0)
                {
                    AddModeFBossCarriedHighQualityLootCount(killer, totalTransferredCount);
                }

                DevLog("[ModeF] Boss 高品质战利品转移: killer="
                    + killer.gameObject.name
                    + ", victim=" + victim.gameObject.name
                    + ", physicalQ6+=" + physicalHighQualityCount
                    + ", carried+=" + carriedHighQualityCount
                    + ", transferred=" + totalTransferredCount);

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] TryHandleModeFBossPreLootPlunder 失败: " + e.Message);
                return false;
            }
        }

        private int CountModeFPlunderableHighQualityItems(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.CharacterItem == null)
                {
                    return 0;
                }

                int count = 0;
                modeFPlunderSeenItemIdScratch.Clear();

                try
                {
                    if (character.CharacterItem.Slots != null)
                    {
                        foreach (Slot slot in character.CharacterItem.Slots)
                        {
                            Item slotItem = slot != null ? slot.Content : null;
                            if (slotItem == null)
                            {
                                continue;
                            }

                            int itemId = slotItem.GetInstanceID();
                            if (!modeFPlunderSeenItemIdScratch.Add(itemId))
                            {
                                continue;
                            }

                            if (slotItem.Quality >= 6)
                            {
                                count++;
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    Inventory inventory = character.CharacterItem.Inventory;
                    if (inventory != null)
                    {
                        foreach (Item inventoryItem in inventory)
                        {
                            if (inventoryItem == null)
                            {
                                continue;
                            }

                            int itemId = inventoryItem.GetInstanceID();
                            if (!modeFPlunderSeenItemIdScratch.Add(itemId))
                            {
                                continue;
                            }

                            if (inventoryItem.Quality >= 6)
                            {
                                count++;
                            }
                        }
                    }
                }
                catch { }

                modeFPlunderSeenItemIdScratch.Clear();
                return count;
            }
            catch
            {
                modeFPlunderSeenItemIdScratch.Clear();
                return 0;
            }
        }

        private void AddModeFBossCarriedHighQualityLootCount(CharacterMainControl boss, int count)
        {
            if (boss == null || count == 0)
            {
                return;
            }

            int bossId = boss.GetInstanceID();
            int existing = 0;
            modeFBossCarriedHighQualityLootCounts.TryGetValue(bossId, out existing);
            int next = existing + count;
            if (next > 0)
            {
                modeFBossCarriedHighQualityLootCounts[bossId] = next;
            }
            else
            {
                modeFBossCarriedHighQualityLootCounts.Remove(bossId);
            }
        }

        private int ConsumeModeFBossCarriedHighQualityLootCount(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return 0;
            }

            int bossId = boss.GetInstanceID();
            int count = 0;
            if (!modeFBossCarriedHighQualityLootCounts.TryGetValue(bossId, out count))
            {
                return 0;
            }

            modeFBossCarriedHighQualityLootCounts.Remove(bossId);
            return Mathf.Max(0, count);
        }

        private void AddModeFBossPendingHighQualityLootPenaltyCount(CharacterMainControl boss, int count)
        {
            if (boss == null || count <= 0)
            {
                return;
            }

            int bossId = boss.GetInstanceID();
            int existing = 0;
            modeFBossPendingHighQualityLootPenaltyCounts.TryGetValue(bossId, out existing);
            modeFBossPendingHighQualityLootPenaltyCounts[bossId] = existing + count;
        }

        private int ConsumeModeFBossPendingHighQualityLootPenaltyCount(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return 0;
            }

            int bossId = boss.GetInstanceID();
            int count = 0;
            if (!modeFBossPendingHighQualityLootPenaltyCounts.TryGetValue(bossId, out count))
            {
                return 0;
            }

            modeFBossPendingHighQualityLootPenaltyCounts.Remove(bossId);
            return Mathf.Max(0, count);
        }

        private void ClearModeFBossPlunderLootState(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return;
            }

            int bossId = boss.GetInstanceID();
            modeFBossCarriedHighQualityLootCounts.Remove(bossId);
            modeFBossPendingHighQualityLootPenaltyCounts.Remove(bossId);
            modeFHandledPreLootPlunderVictimIds.Remove(bossId);
        }

        private void ClearAllModeFBossPlunderLootState()
        {
            modeFBossCarriedHighQualityLootCounts.Clear();
            modeFBossPendingHighQualityLootPenaltyCounts.Clear();
            modeFHandledPreLootPlunderVictimIds.Clear();
            modeFPlunderSeenItemIdScratch.Clear();
            modeFTransferableAmmoScratch.Clear();
            modeFBulletCaliberCache.Clear();
        }

        private void TrySwapSlot(CharacterMainControl killer, CharacterMainControl victim, string slotTag, Vector3 lootDropPosition)
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
                    if (TrySwapModeFItemsBetweenCharacters(killer, victim, victimItem, killerItem, lootDropPosition))
                    {
                        DevLog("[ModeF] Boss 换装 [" + slotTag + "]: Q" + killerQuality + " -> Q" + victimQuality);
                    }
                    else
                    {
                        DevLog("[ModeF] [WARNING] Boss 换装失败 [" + slotTag + "]，已保持原装备");
                    }
                }
            }
            catch { }
        }

        private void TrySwapGun(CharacterMainControl killer, CharacterMainControl victim, Vector3 lootDropPosition)
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
                    if (!TrySwapModeFItemsBetweenCharacters(killer, victim, victimGun, killerGun, lootDropPosition))
                    {
                        DevLog("[ModeF] [WARNING] Boss 换枪失败，已保持原装备");
                        return;
                    }

                    TransferModeFBossCompatibleAmmo(killer, victim, victimGun, lootDropPosition);
                    // 补满弹匣
                    RefillModeFBossGunAndAmmo(killer, victimGun, lootDropPosition, !HasModeFCompatibleAmmo(killer, victimGun));

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

        private void DropOrDestroyModeFLootedItem(CharacterMainControl owner, Item item, Vector3? preferredDropPosition = null)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                Vector3 dropPosition = Vector3.zero;
                bool hasDropPosition = false;

                if (preferredDropPosition.HasValue)
                {
                    dropPosition = preferredDropPosition.Value;
                    hasDropPosition = true;
                }
                else if (owner != null && owner.transform != null)
                {
                    dropPosition = owner.transform.position + Vector3.up * 0.3f;
                    hasDropPosition = true;
                }

                if (hasDropPosition)
                {
                    item.Drop(dropPosition, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                }
                else if (item.gameObject != null)
                {
                    item.DestroyTree();
                }
            }
            catch { }
        }

        #endregion
    }
}
