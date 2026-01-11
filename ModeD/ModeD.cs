// ============================================================================
// ModeD.cs - 白手起家模式核心逻辑
// ============================================================================
// 模块说明：
//   Mode D（白手起家）是 BossRush 的一个特殊玩法模式。
//   玩家需要"裸体"（不携带任何装备，仅允许携带船票）进入竞技场，
//   系统会随机发放开局装备，玩家通过击杀敌人获取更好的装备。
//   
// 主要功能：
//   - 检测玩家是否满足"裸体"入场条件
//   - 初始化物品池（武器、护甲、头盔、弹药、医疗品）
//   - 初始化敌人池（小怪池、Boss池）
//   - 管理 Mode D 的启动和结束
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// Mode D（白手起家）：无限波次赌狗向挑战模式
    /// <para>玩家裸体+船票入场，获得随机开局装备，通过击杀敌人获取更好装备</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode D 状态变量
        
        /// <summary>是否处于 Mode D 模式</summary>
        private bool modeDActive = false;
        
        /// <summary>Mode D 当前波次索引（从1开始）</summary>
        private int modeDWaveIndex = 0;
        
        /// <summary>Mode D 当前波次中存活的敌人列表</summary>
        private readonly List<CharacterMainControl> modeDCurrentWaveEnemies = new List<CharacterMainControl>();
        
        /// <summary>Mode D 小怪预设池</summary>
        private readonly List<EnemyPresetInfo> modeDMinionPool = new List<EnemyPresetInfo>();

        /// <summary>Mode D Boss预设池（复用现有enemyPresets）</summary>
        private List<EnemyPresetInfo> modeDBossPool = null;

        /// <summary>CharacterRandomPreset 缓存字典（按 nameKey 索引，避免重复 FindObjectsOfTypeAll）</summary>
        private static System.Collections.Generic.Dictionary<string, CharacterRandomPreset> cachedCharacterPresets = null;

        /// <summary>复用 List 缓存（避免 GetRandomBossPreset/GetRandomMinionPreset 每次 new List 造成 GC）</summary>
        private static readonly System.Collections.Generic.List<EnemyPresetInfo> presetFilterCache = new System.Collections.Generic.List<EnemyPresetInfo>();

        /// <summary>复用 List 缓存 #2（用于两阶段过滤，如血量筛选）</summary>
        private static readonly System.Collections.Generic.List<EnemyPresetInfo> presetFilterCache2 = new System.Collections.Generic.List<EnemyPresetInfo>();
        
        /// <summary>Mode D 物品池是否已初始化</summary>
        private bool modeDItemPoolsInitialized = false;
        
        /// <summary>Mode D 武器池（Gun Tag）</summary>
        private readonly List<int> modeDWeaponPool = new List<int>();
        
        /// <summary>Mode D 护甲池（Armor Tag）</summary>
        private readonly List<int> modeDArmortPool = new List<int>();
        
        /// <summary>Mode D 头盔池（Helmat Tag）</summary>
        private readonly List<int> modeDHelmetPool = new List<int>();
        
        /// <summary>Mode D 弹药池（Bullet Tag）</summary>
        private readonly List<int> modeDAmmoPool = new List<int>();
        
        /// <summary>Mode D 医疗品池</summary>
        private readonly List<int> modeDMedicalPool = new List<int>();

        /// <summary>Mode D 近战武器池（MeleeWeapon Tag，预建）</summary>
        private readonly List<int> modeDMeleePool = new List<int>();

        /// <summary>Mode D 图腾池（Totem Tag，预建）</summary>
        private readonly List<int> modeDTotemPool = new List<int>();

        /// <summary>Mode D 面具池（Mask Tag，预建）</summary>
        private readonly List<int> modeDMaskPool = new List<int>();

        /// <summary>Mode D 背包池（Backpack Tag，预建）</summary>
        private readonly List<int> modeDBackpackPool = new List<int>();

        /// <summary>Mode D 当前波次是否正在处理完成逻辑（防止重复触发）</summary>
        private bool modeDWaveCompletePending = false;

        /// <summary>Mode D 当前波次预期生成的敌人数（按实际调用 SpawnModeDEnemy 的次数累计）</summary>
        private int modeDExpectedEnemiesInCurrentWave = 0;

        /// <summary>Mode D 当前波次已"结案"的生成数量（成功或最终失败都算结案）</summary>
        private int modeDSpawnResolvedInCurrentWave = 0;

        /// <summary>自动下一波协程句柄（用于取消旧协程，防止重复开波）</summary>
        private Coroutine modeDAutoNextWaveCoroutine = null;

        #endregion
        
        #region Mode D 配置
        
        /// <summary>Mode D 每波敌人数（可配置，1-10，默认3）</summary>
        private int modeDEnemiesPerWave = 3;
        
        #endregion
        
        #region Mode D 公共属性

        /// <summary>是否处于 Mode D 模式</summary>
        public bool IsModeDActive { get { return modeDActive; } }

        /// <summary>Mode D 当前波次</summary>
        public int ModeDWaveIndex { get { return modeDWaveIndex; } }
        
        #endregion

        #region Mode D 核心方法

        /// <summary>
        /// 检测玩家是否满足"裸体"条件（完全为空，包括狗子背包）
        /// </summary>
        public bool IsPlayerNaked()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return false;

                Item characterItem = main.CharacterItem;
                if (characterItem == null) return false;

                // 检查装备槽是否全空
                string[] equipmentSlots = new string[] { "Armor", "Helmat", "FaceMask", "Backpack", "Headset" };
                foreach (string slotName in equipmentSlots)
                {
                    try
                    {
                        Slot slot = characterItem.Slots.GetSlot(slotName);
                        if (slot != null && slot.Content != null)
                        {
                            DevLog("[ModeD] 装备槽不为空: " + slotName);
                            return false;
                        }
                    }
                    catch {}
                }

                // 检查武器槽
                string[] weaponSlots = new string[] { "PrimaryWeapon", "SecondaryWeapon", "MeleeWeapon" };
                foreach (string slotName in weaponSlots)
                {
                    try
                    {
                        Slot slot = characterItem.Slots.GetSlot(slotName);
                        if (slot != null && slot.Content != null)
                        {
                            DevLog("[ModeD] 武器槽不为空: " + slotName);
                            return false;
                        }
                    }
                    catch {}
                }

                // 检查背包中是否存在非 BossRush 船票的物品
                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null && inventory.Content.Count > 0)
                {
                    // 允许的船票类型：优先使用动态注册的 bossRushTicketTypeId，否则退回到老的固定 ID 868
                    int allowedTicketTypeId = bossRushTicketTypeId > 0 ? bossRushTicketTypeId : 868;

                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        Item item = inventory.Content[i];
                        if (item == null)
                        {
                            continue;
                        }

                        int typeId = -1;
                        try
                        {
                            typeId = item.TypeID;
                        }
                        catch {}

                        // 仅允许 BossRush 船票存在于背包中
                        if (typeId == allowedTicketTypeId)
                        {
                            continue;
                        }

                        DevLog("[ModeD] 背包中存在非船票物品: " + item.DisplayName + " (TypeID=" + typeId + ")");
                        return false;
                    }
                }

                // 检查狗子背包（PetProxy.PetInventory）是否有物品，防止通过狗子保险箱偷渡装备
                try
                {
                    Inventory petInventory = PetProxy.PetInventory;
                    if (petInventory != null && petInventory.Content != null && petInventory.Content.Count > 0)
                    {
                        // 遍历狗子背包，检查是否有任何物品
                        for (int i = 0; i < petInventory.Content.Count; i++)
                        {
                            Item petItem = petInventory.Content[i];
                            if (petItem != null)
                            {
                                DevLog("[ModeD] 狗子背包中存在物品: " + petItem.DisplayName + " - 不满足裸体条件");
                                return false;
                            }
                        }
                    }
                }
                catch (Exception petEx)
                {
                    // 如果无法访问狗子背包（可能狗子不存在），忽略此检查
                    DevLog("[ModeD] 无法检查狗子背包: " + petEx.Message);
                }

                DevLog("[ModeD] 玩家满足裸体条件");
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] IsPlayerNaked 检测失败: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 启动 Mode D 模式
        /// </summary>
        public void StartModeD()
        {
            try
            {
                DevLog("[ModeD] 启动 Mode D 模式");

                modeDActive = true;
                modeDWaveIndex = 0;
                modeDWaveCompletePending = false;
                modeDCurrentWaveEnemies.Clear();

                // 读取配置
                if (config != null && config.modeDEnemiesPerWave > 0)
                {
                    modeDEnemiesPerWave = Mathf.Clamp(config.modeDEnemiesPerWave, 1, 10);
                }
                DevLog("[ModeD] 每波敌人数: " + modeDEnemiesPerWave);

                // 初始化物品池
                InitializeModeDItemPools();

                // 初始化敌人池
                InitializeModeDEnemyPools();

                // 前置构建全局掉落池（避免战斗中首次调用时卡顿）
                EnsureModeDGlobalItemPool();

                // 给玩家发放开局装备
                GivePlayerStarterKit();

                // 设置路牌为 Mode D 模式
                SetupSignForModeD();
                
                // 初始状态保持为生小鸡（EntryAndDifficulty），波次开始时再切换到加油状态

                ShowMessage(L10n.T("白手起家模式已激活！通过路牌开始挑战！", "Rags to Riches mode activated! Start the challenge via the signpost!"));
                ShowBigBanner(L10n.T("欢迎来到 <color=red>白手起家</color>！", "Welcome to <color=red>Rags to Riches</color>!"));
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] StartModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查并尝试启动 Mode D（在进入竞技场时调用）
        /// </summary>
        public bool TryStartModeD()
        {
            try
            {
                // 检查是否满足 Mode D 条件
                if (!IsPlayerNaked())
                {
                    DevLog("[ModeD] 玩家不满足裸体条件，不启动 Mode D");
                    return false;
                }

                DevLog("[ModeD] 检测到裸体入场，启动 Mode D");
                StartModeD();
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] TryStartModeD 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 结束 Mode D 模式
        /// </summary>
        public void EndModeD()
        {
            try
            {
                // 如果 Mode D 未激活，无需重复结束
                if (!modeDActive) return;

                // 先保存完成波次数，再清零
                int completedWaves = modeDWaveIndex;

                DevLog("[ModeD] 结束 Mode D 模式，完成波次: " + completedWaves);

                modeDActive = false;
                modeDWaveIndex = 0;
                modeDCurrentWaveEnemies.Clear();

                // 使用保存的波次数显示消息
                ShowMessage(L10n.T(
                    "白手起家挑战结束！共完成 " + completedWaves + " 波",
                    "Rags to Riches challenge ended! Completed " + completedWaves + " waves"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] EndModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 初始化 Mode D 物品池（按 Tag 筛选，包含游戏中所有该品类物品）
        /// </summary>
        private void InitializeModeDItemPools()
        {
            if (modeDItemPoolsInitialized) return;

            try
            {
                DevLog("[ModeD] 开始初始化物品池...");

                modeDWeaponPool.Clear();
                modeDArmortPool.Clear();
                modeDHelmetPool.Clear();
                modeDAmmoPool.Clear();
                modeDMedicalPool.Clear();
                modeDMeleePool.Clear();
                modeDTotemPool.Clear();
                modeDMaskPool.Clear();
                modeDBackpackPool.Clear();

                // 获取 Tag 系统
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    DevLog("[ModeD] [WARNING] 无法获取 TagsData");
                    return;
                }

                // 基础排除标签（demo锁定、不可掉落等）
                List<Duckov.Utilities.Tag> baseExclude = new List<Duckov.Utilities.Tag>();
                if (tagsData.LockInDemoTag != null) baseExclude.Add(tagsData.LockInDemoTag);
                if (tagsData.DestroyOnLootBox != null) baseExclude.Add(tagsData.DestroyOnLootBox);
                if (tagsData.DontDropOnDeadInSlot != null) baseExclude.Add(tagsData.DontDropOnDeadInSlot);
                if (tagsData.Character != null) baseExclude.Add(tagsData.Character);
                Duckov.Utilities.Tag[] excludeArray = baseExclude.ToArray();

                // 武器池（Gun Tag）
                if (tagsData.Gun != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Gun, excludeArray);
                    if (ids != null) modeDWeaponPool.AddRange(ids);
                }
                // 移除龙息（龙裔遗族Boss专属掉落，不应出现在白手起家随机池中）
                modeDWeaponPool.Remove(DragonDescendantConfig.DRAGON_BREATH_TYPE_ID);

                // 护甲池（Armor Tag）
                if (tagsData.Armor != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Armor, excludeArray);
                    if (ids != null) modeDArmortPool.AddRange(ids);
                }
                // 移除龙甲（龙裔遗族Boss专属掉落，不应出现在白手起家随机池中）
                modeDArmortPool.Remove(DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID);

                // 头盔池（Helmat Tag）
                if (tagsData.Helmat != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Helmat, excludeArray);
                    if (ids != null) modeDHelmetPool.AddRange(ids);
                }
                // 移除龙头（龙裔遗族Boss专属掉落，不应出现在白手起家随机池中）
                modeDHelmetPool.Remove(DragonDescendantConfig.DRAGON_HELM_TYPE_ID);

                // 弹药池（Bullet Tag）
                if (tagsData.Bullet != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Bullet, excludeArray);
                    if (ids != null) modeDAmmoPool.AddRange(ids);
                }

                // 医疗品池（通过名字查找 Medical/Consumable Tag）
                Duckov.Utilities.Tag medicalTag = FindTagByNameInInit("Medical");
                if (medicalTag == null) medicalTag = FindTagByNameInInit("Consumable");
                if (medicalTag == null) medicalTag = FindTagByNameInInit("Healing");
                if (medicalTag != null)
                {
                    int[] ids = SearchItemsByTag(medicalTag, excludeArray);
                    if (ids != null) modeDMedicalPool.AddRange(ids);
                }

                // P1-8: 预建近战武器池（MeleeWeapon Tag）
                Duckov.Utilities.Tag meleeTag = FindTagByNameInInit("MeleeWeapon");
                if (meleeTag != null)
                {
                    int[] ids = SearchItemsByTag(meleeTag, excludeArray);
                    if (ids != null) modeDMeleePool.AddRange(ids);
                }

                // P1-8: 预建图腾池（Totem Tag）
                Duckov.Utilities.Tag totemTag = FindTagByNameInInit("Totem");
                if (totemTag != null)
                {
                    int[] ids = SearchItemsByTag(totemTag, excludeArray);
                    if (ids != null) modeDTotemPool.AddRange(ids);
                }

                // P1-8: 预建面具池（Mask Tag）
                Duckov.Utilities.Tag maskTag = FindTagByNameInInit("Mask");
                if (maskTag == null) maskTag = FindTagByNameInInit("FaceMask");
                if (maskTag != null)
                {
                    int[] ids = SearchItemsByTag(maskTag, excludeArray);
                    if (ids != null) modeDMaskPool.AddRange(ids);
                }

                // P1-8: 预建背包池（使用 GameplayDataSettings.Tags.Backpack）
                if (tagsData.Backpack != null)
                {
                    int[] ids = SearchItemsByTag(tagsData.Backpack, excludeArray);
                    if (ids != null) modeDBackpackPool.AddRange(ids);
                }

                modeDItemPoolsInitialized = true;
                DevLog("[ModeD] 物品池初始化完成: " +
                       "武器=" + modeDWeaponPool.Count +
                       ", 护甲=" + modeDArmortPool.Count +
                       ", 头盔=" + modeDHelmetPool.Count +
                       ", 弹药=" + modeDAmmoPool.Count +
                       ", 医疗=" + modeDMedicalPool.Count +
                       ", 近战=" + modeDMeleePool.Count +
                       ", 图腾=" + modeDTotemPool.Count +
                       ", 面具=" + modeDMaskPool.Count +
                       ", 背包=" + modeDBackpackPool.Count);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] InitializeModeDItemPools 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 根据Tag搜索物品（包含所有品质）
        /// </summary>
        private int[] SearchItemsByTag(Duckov.Utilities.Tag tag, Duckov.Utilities.Tag[] excludeTags)
        {
            try
            {
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { tag };
                filter.excludeTags = excludeTags;
                filter.minQuality = 1;
                filter.maxQuality = 8; // 包含所有品质
                return ItemAssetsCollection.Search(filter);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 根据名称查找Tag（用于初始化）
        /// </summary>
        private Duckov.Utilities.Tag FindTagByNameInInit(string tagName)
        {
            try
            {
                foreach (var tag in GameplayDataSettings.Tags.AllTags)
                {
                    if (tag != null && tag.name == tagName)
                    {
                        return tag;
                    }
                }
            }
            catch {}
            return null;
        }

        /// <summary>
        /// 初始化 Mode D 敌人池（小怪池 = showName==false，Boss池复用现有）
        /// </summary>
        private void InitializeModeDEnemyPools()
        {
            try
            {
                DevLog("[ModeD] 开始初始化敌人池...");

                modeDMinionPool.Clear();

                // 扫描所有 CharacterRandomPreset
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets == null || allPresets.Length == 0)
                {
                    DevLog("[ModeD] [WARNING] 未找到任何 CharacterRandomPreset");
                    return;
                }

                // 构建缓存字典（一次性 O(N) 操作，后续 SpawnModeDEnemy 可 O(1) 查询）
                cachedCharacterPresets = new System.Collections.Generic.Dictionary<string, CharacterRandomPreset>();
                foreach (var preset in allPresets)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.nameKey)) continue;
                    if (!cachedCharacterPresets.ContainsKey(preset.nameKey))
                    {
                        cachedCharacterPresets[preset.nameKey] = preset;
                    }
                }
                DevLog("[ModeD] 缓存了 " + cachedCharacterPresets.Count + " 个 CharacterRandomPreset");

                foreach (var preset in allPresets)
                {
                    if (preset == null) continue;

                    string nameKey = preset.nameKey;
                    if (string.IsNullOrEmpty(nameKey)) continue;

                    int team = (int)preset.team;
                    // 只收集敌对阵营，排除玩家和中立阵营
                    if (team == (int)Teams.player || team == (int)Teams.middle) continue;

                    // 排除商人和宠物类型
                    bool shouldExclude = false;
                    
                    // 方法1：通过反射获取 characterIconType 字段
                    try
                    {
                        var iconField = typeof(CharacterRandomPreset).GetField("characterIconType",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (iconField != null)
                        {
                            object iconValue = iconField.GetValue(preset);
                            if (iconValue != null)
                            {
                                int iconType = (int)iconValue;
                                // CharacterIconTypes: merchant = 4, pet = 5
                                if (iconType == 4)
                                {
                                    shouldExclude = true;
                                    DevLog("[ModeD] 排除商人(iconType): " + nameKey);
                                }
                                else if (iconType == 5)
                                {
                                    shouldExclude = true;
                                    DevLog("[ModeD] 排除宠物(iconType): " + nameKey);
                                }
                            }
                        }
                    }
                    catch { }
                    
                    // 方法2：通过 GetCharacterIcon() 返回的图标来判断
                    if (!shouldExclude)
                    {
                        try
                        {
                            Sprite icon = preset.GetCharacterIcon();
                            if (icon != null)
                            {
                                Sprite merchantIcon = GameplayDataSettings.UIStyle.MerchantCharacterIcon;
                                Sprite petIcon = GameplayDataSettings.UIStyle.PetCharacterIcon;
                                if (merchantIcon != null && icon == merchantIcon)
                                {
                                    shouldExclude = true;
                                    DevLog("[ModeD] 排除商人(icon): " + nameKey);
                                }
                                else if (petIcon != null && icon == petIcon)
                                {
                                    shouldExclude = true;
                                    DevLog("[ModeD] 排除宠物(icon): " + nameKey);
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // 方法3：通过名字关键词排除（作为最后的保险）
                    if (!shouldExclude)
                    {
                        string lowerName = nameKey.ToLower();
                        if (lowerName.Contains("merchant") || lowerName.Contains("trader") || 
                            lowerName.Contains("shop") || lowerName.Contains("vendor") ||
                            nameKey.Contains("商人") || nameKey.Contains("商贩"))
                        {
                            shouldExclude = true;
                            DevLog("[ModeD] 排除商人(name): " + nameKey);
                        }
                        else if (lowerName.Contains("pet") || nameKey.Contains("宠物"))
                        {
                            shouldExclude = true;
                            DevLog("[ModeD] 排除宠物(name): " + nameKey);
                        }
                    }

                    if (shouldExclude) continue;

                    float health = (preset.health > 0f) ? preset.health : 100f;
                    float damage = preset.damageMultiplier;

                    // showName == false 的是小怪
                    if (!preset.showName)
                    {
                        // 排除已存在的
                        if (modeDMinionPool.Any(e => e.name == nameKey)) continue;

                        string displayName = GetLocalizedCharacterName(nameKey);

                        var minionInfo = new EnemyPresetInfo
                        {
                            name = nameKey,
                            displayName = displayName,
                            team = team,
                            baseHealth = health,
                            baseDamage = damage
                        };

                        modeDMinionPool.Add(minionInfo);
                        DevLog("[ModeD] 添加小怪: " + nameKey + " (health=" + health + ")");
                    }
                }

                // Boss池复用现有的 enemyPresets（已在 TryDiscoverAdditionalEnemies 中填充）
                modeDBossPool = enemyPresets;

                int bossCount = (modeDBossPool != null) ? modeDBossPool.Count : 0;
                DevLog("[ModeD] 敌人池初始化完成: 小怪=" + modeDMinionPool.Count + ", Boss=" + bossCount);
            }
            catch (Exception e)
            {
                DevLog("[ModeD] [ERROR] InitializeModeDEnemyPools 失败: " + e.Message);
            }
        }
        
        #endregion
    }
}

