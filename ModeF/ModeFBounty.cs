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
        private readonly Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg)> modeFBossModifiers
            = new Dictionary<CharacterMainControl, (Modifier hp, Modifier gunDmg)>();
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

                    // Boss 成长按战胜目标的印记数结算，每层印记提供 5% 生命/伤害成长。
                    float growthPercent = 0.05f * victimMarks;
                    ApplyModeFBossGrowth(killer, growthPercent);
                    BroadcastModeFBossGrowth(killer, victim, growthPercent);
                }

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

                CompareAndSwapEquipment(killer, victim);

                if (!ShouldUseModeFAbstractPlunderLootTracking())
                {
                    DevLog("[ModeF] Boss 预掠夺已执行即时换装，但当前关闭了随机掉落，跳过抽象战利品继承");
                    return true;
                }

                int physicalHighQualityCount = CountModeFPlunderableHighQualityItems(victim);
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

        private void TryReplaceModeFEquippedItem(CharacterMainControl owner, string slotKey, Item newItem, Item oldItem, Vector3 lootDropPosition)
        {
            try
            {
                if (owner == null || owner.CharacterItem == null || newItem == null) return;

                if (oldItem != null)
                {
                    if (!TrySwapModeFLootedItemWithRollback(owner, newItem, oldItem, slotKey, lootDropPosition))
                    {
                        DevLog("[ModeF] [WARNING] 替换装备失败，已执行回滚/掉落保护 slot=" + slotKey);
                    }
                    return;
                }

                if (!TryEquipModeFLootedItemOrDrop(owner, newItem, lootDropPosition))
                {
                    DevLog("[ModeF] [WARNING] 替换装备失败，已掉落战利品 slot=" + slotKey);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] TryReplaceModeFEquippedItem 失败: " + e.Message + " slot=" + slotKey);
            }
        }

        private bool TrySwapModeFLootedItemWithRollback(CharacterMainControl owner, Item newItem, Item oldItem, string slotKey, Vector3 lootDropPosition)
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
                    DropOrDestroyModeFLootedItem(owner, oldItem, lootDropPosition);
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

            DropOrDestroyModeFLootedItem(owner, newItem, lootDropPosition);
            if (oldDetached && oldItem != null && !restored)
            {
                DevLog("[ModeF] [WARNING] 旧装备回滚失败，已掉落 slot=" + slotKey);
                DropOrDestroyModeFLootedItem(owner, oldItem, lootDropPosition);
            }

            return false;
        }

        private bool TryEquipModeFLootedItemOrDrop(CharacterMainControl owner, Item item, Vector3 lootDropPosition)
        {
            bool plugged = TryEquipModeFLootedItem(owner, item);
            if (!plugged)
            {
                DropOrDestroyModeFLootedItem(owner, item, lootDropPosition);
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
                            if (probeItem != null)
                            {
                                probeItem.DestroyTree();
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
                        if (reward != null)
                        {
                            reward.DestroyTree();
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
