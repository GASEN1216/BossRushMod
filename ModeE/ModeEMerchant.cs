// ============================================================================
// ModeEMerchant.cs — Mode E 神秘商人 NPC
// ============================================================================
// 模块说明：
//   在 Mode E（划地为营）模式启动时生成神秘商人 NPC，提供全品类商店。
//   - 优先通过 nameKey 匹配商人预设（MerchantName_Myst），回退到 iconType==4
//   - 注入原版商人交互点，每个物品分类创建独立商店交互选项
//   - 除子弹外所有物品价格 ×10，商人生命值设为 999999
//   - Mode E 结束时安全清理商人及商店引用
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using TMPro;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // ====================================================================
        // Mode E 神秘商人字段
        // ====================================================================

        /// <summary>Mode E 神秘商人 NPC 实例引用</summary>
        private CharacterMainControl modeEMerchantNPC = null;

        /// <summary>Mode E 神秘商人各分类商店列表（清理用）</summary>
        private List<StockShop> modeEMerchantShops = new List<StockShop>();

        /// <summary>Mode E 医疗品商店需排除的原版物品 TypeID</summary>
        private static readonly HashSet<int> modeEMedicalShopExcludedIds = new HashSet<int>
        {
            88, 89, 136, 331, 1428, 1429
        };

        /// <summary>Mode E 神秘商人主交互引用（用于 Harmony patch 识别）</summary>
        private InteractableBase modeEMerchantMainInteract = null;

        /// <summary>获取 Mode E 神秘商人主交互引用（供 Harmony patch 使用）</summary>
        public InteractableBase ModeEMerchantMainInteract => modeEMerchantMainInteract;

        /// <summary>缓存的 Mode E 商人预设，避免重复扫描所有 CharacterRandomPreset</summary>
        private static CharacterRandomPreset cachedModeEMerchantPreset = null;

        /// <summary>缓存的 Mode E 商店分类商品 ID，Key 为分类后缀（如 Gun / Medical）</summary>
        private static readonly Dictionary<string, int[]> modeEMerchantCategoryItemCache = new Dictionary<string, int[]>();

        /// <summary>Mode E 其他分类商店的固定商品列表</summary>
        private static readonly int[] modeEMerchantOtherItemIds = new int[]
        {
            388,
            RespawnItemConfig.TAUNT_SMOKE_TYPE_ID,
            RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID,
            RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID,
            RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID
        };

        // ====================================================================
        // 生成神秘商人
        // ====================================================================

        /// <summary>
        /// 异步生成神秘商人 NPC，注入分类商店交互选项
        /// </summary>
        private async UniTaskVoid SpawnModeEMerchant(
            int modeFSessionToken = 0,
            int modeFRelatedScene = -1,
            int modeESessionToken = 0,
            int modeESessionRelatedScene = -1)
        {
            try
            {
                CharacterRandomPreset merchantPreset = GetModeEMerchantPreset();

                if (merchantPreset == null)
                {
                    DevLog("[ModeE] [ERROR] 未找到任何商人预设，跳过神秘商人生成");
                    return;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    DevLog("[ModeE] [ERROR] 玩家实例为空，跳过神秘商人生成");
                    return;
                }

                Vector3 spawnPos = player.transform.position + player.transform.forward * 2f;
                Vector3 dir = -player.transform.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                var character = await merchantPreset.CreateCharacterAsync(spawnPos, dir, relatedScene, null, false);
                if (character == null)
                {
                    DevLog("[ModeE] [ERROR] CreateCharacterAsync 返回空，神秘商人生成失败");
                    return;
                }

                if (!IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene) ||
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex != relatedScene)
                {
                    try
                    {
                        if (character.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(character.gameObject);
                        }
                    }
                    catch { }

                    DevLog("[ModeE] 商人生成完成时模式已结束或场景已切换，已放弃该实例");
                    return;
                }

                // 验证阵营有效性后再设置（modeEPlayerFaction 默认为 Teams.player）
                character.SetTeam(modeEPlayerFaction);
                modeEMerchantNPC = character;

                // 设置商人生命值为 999999
                SetModeEMerchantHealth(character);

                DevLog("[ModeE] 神秘商人 NPC 生成成功，阵营: " + character.Team);

                // 注入分类商店交互选项
                BuildModeEMerchantShop(character.gameObject);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] SpawnModeEMerchant 失败: " + e.Message);
            }
        }

        // ====================================================================
        // 设置商人生命值
        // ====================================================================

        /// <summary>设置商人生命值为 999999，防止被误杀</summary>
        private void SetModeEMerchantHealth(CharacterMainControl character)
        {
            try
            {
                // 通过 Stat 系统添加生命值修饰符
                Item characterItem = character.GetComponent<Item>();
                if (characterItem != null)
                {
                    Stat maxHealthStat = characterItem.GetStat("MaxHealth");
                    if (maxHealthStat != null)
                    {
                        // 添加大量生命值
                        float delta = 999999f - maxHealthStat.Value;
                        if (delta > 0)
                        {
                            Modifier mod = new Modifier(ModifierType.Add, delta, this);
                            maxHealthStat.AddModifier(mod);
                        }
                    }
                }

                // 同步当前血量到最大值
                if (character.Health != null)
                {
                    character.Health.SetHealth(character.Health.MaxHealth);
                    DevLog("[ModeE] 商人生命值已设置: " + character.Health.MaxHealth);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 设置商人生命值失败: " + e.Message);
            }
        }

        // ====================================================================
        // 构建分类商店并注入交互选项
        // ====================================================================

        /// <summary>
        /// 找到商人原有的 InteractableBase，保留原版交互但修改其显示名称为"召唤煤球"，
        /// 并注入每个物品分类的独立商店交互选项。
        /// 使用 Harmony patch 来拦截主交互的 OnTimeOut 实现召唤煤球功能。
        /// </summary>
        private void BuildModeEMerchantShop(GameObject npcGo)
        {
            try
            {
                // 找到商人原有的 InteractableBase（主交互点）
                InteractableBase mainInteract = npcGo.GetComponentInChildren<InteractableBase>(true);
                if (mainInteract == null)
                {
                    DevLog("[ModeE] [WARNING] 商人 NPC 上未找到 InteractableBase，无法注入商店");
                    return;
                }

                // 销毁原版商人自带的 StockShop，防止原始交易选项出现
                var origShop = npcGo.GetComponentInChildren<StockShop>(true);
                if (origShop != null)
                {
                    UnityEngine.Object.Destroy(origShop);
                    DevLog("[ModeE] 已移除商人原版 StockShop");
                }

                // 保存商人主交互引用，用于 Harmony patch 识别
                modeEMerchantMainInteract = mainInteract;

                // 修改主交互显示名称为"召唤煤球"（第一个选项）
                mainInteract.overrideInteractName = true;
                mainInteract._overrideInteractNameKey = "BossRush_ModeE_SummonPet";

                // 启用交互组（允许多个子选项）
                mainInteract.interactableGroup = true;

                // 获取 otherInterablesInGroup 列表
                var field = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null)
                {
                    DevLog("[ModeE] [ERROR] 未找到 otherInterablesInGroup 反射字段");
                    return;
                }

                var groupList = field.GetValue(mainInteract) as List<InteractableBase>;
                if (groupList == null)
                {
                    groupList = new List<InteractableBase>();
                    field.SetValue(mainInteract, groupList);
                }

                // 清理 null 元素
                for (int i = groupList.Count - 1; i >= 0; i--)
                {
                    if (groupList[i] == null)
                    {
                        groupList.RemoveAt(i);
                    }
                }

                bool hasRepairOption = false;
                for (int i = 0; i < groupList.Count; i++)
                {
                    if (groupList[i] is BossRushRepairInteractable)
                    {
                        hasRepairOption = true;
                        break;
                    }
                }

                if (!hasRepairOption)
                {
                    GameObject repairObj = new GameObject("ModeEOption_Repair");
                    repairObj.transform.SetParent(mainInteract.transform);
                    repairObj.transform.localPosition = Vector3.zero;
                    repairObj.transform.localRotation = Quaternion.identity;
                    repairObj.transform.localScale = Vector3.one;

                    BossRushRepairInteractable repairInteract = repairObj.AddComponent<BossRushRepairInteractable>();
                    groupList.Insert(0, repairInteract);
                }

                // 获取 Tag 系统
                var tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    DevLog("[ModeE] [WARNING] 无法获取 TagsData");
                    return;
                }

                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
                List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);

                int totalItems = 0;

                // 为每个分类创建独立商店和交互选项
                foreach (var cat in categories)
                {
                    var tags = cat.Item1;
                    var locKey = cat.Item2;
                    var suffix = cat.Item3;

                    // 多 Tag 合并搜索（取并集）
                    var allIds = ModeESearchItemsMultiTag(tags, emptyExclude);
                    if (allIds == null || allIds.Count == 0) continue;

                    if (suffix == "Medical")
                    {
                        int removedCount = allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                        if (allIds.Count == 0) continue;
                        if (removedCount > 0)
                            DevLog("[ModeE] 医疗品商店已排除 " + removedCount + " 个黑名单物品");
                    }

                    // 创建子 GameObject 挂载商店和交互
                    GameObject shopObj = new GameObject("ModeEShop_" + suffix);
                    shopObj.transform.SetParent(mainInteract.transform);
                    shopObj.transform.localPosition = Vector3.zero;
                    shopObj.transform.localRotation = Quaternion.identity;
                    shopObj.transform.localScale = Vector3.one;

                    // 创建 StockShop 组件
                    StockShop shop = shopObj.AddComponent<StockShop>();
                    modeEMerchantShops.Add(shop);

                    // 设置 merchantID（自定义值，不会从 StockShopDatabase 加载原版商品）
                    try
                    {
                        var fMerchant = ReflectionCache.StockShop_MerchantID;
                        if (fMerchant != null)
                            fMerchant.SetValue(shop, "ModeE_" + suffix);
                    }
                    catch { }

                    // 设置 accountAvaliable = true
                    try
                    {
                        var fAccount = ReflectionCache.StockShop_AccountAvaliable;
                        if (fAccount != null)
                            fAccount.SetValue(shop, true);
                    }
                    catch { }

                    // 填充商品（子弹原价，其余 ×10）
                    float shopPriceFactor = (suffix == "Bullet") ? 1.0f : 10.0f;
                    if (shop.entries == null)
                        shop.entries = new List<StockShop.Entry>();
                    else
                        shop.entries.Clear();

                    foreach (int id in allIds)
                    {
                        try
                        {
                            StockShopDatabase.ItemEntry raw = new StockShopDatabase.ItemEntry();
                            raw.typeID = id;
                            raw.maxStock = 9999;
                            raw.forceUnlock = true;
                            raw.priceFactor = shopPriceFactor;
                            raw.possibility = 1.0f;
                            raw.lockInDemo = false;

                            StockShop.Entry entry = new StockShop.Entry(raw);
                            entry.CurrentStock = entry.MaxStock;
                            entry.Show = true;
                            shop.entries.Add(entry);
                        }
                        catch { }
                    }

                    // itemInstances 预缓存在末尾由协程统一异步分帧执行

                    // 创建交互选项并注入到主交互组（使用本地化键）
                    var interact = shopObj.AddComponent<ModeEShopInteractable>();
                    interact.Setup(shop, locKey);
                    groupList.Add(interact);

                    totalItems += allIds.Count;
                    DevLog("[ModeE] 商店分类 " + locKey + ": " + allIds.Count + " 个物品");
                }

                // ============================================================
                // 创建"其他"商店（售卖特定物品 ID=388）
                // ============================================================
                {
                    GameObject otherShopObj = new GameObject("ModeEShop_Other");
                    otherShopObj.transform.SetParent(mainInteract.transform);
                    otherShopObj.transform.localPosition = Vector3.zero;
                    otherShopObj.transform.localRotation = Quaternion.identity;
                    otherShopObj.transform.localScale = Vector3.one;

                    StockShop otherShop = otherShopObj.AddComponent<StockShop>();
                    modeEMerchantShops.Add(otherShop);

                    // 设置 merchantID
                    try
                    {
                        var fMerchant = ReflectionCache.StockShop_MerchantID;
                        if (fMerchant != null)
                            fMerchant.SetValue(otherShop, "ModeE_Other");
                    }
                    catch { }

                    // 设置 accountAvaliable = true
                    try
                    {
                        var fAccount = ReflectionCache.StockShop_AccountAvaliable;
                        if (fAccount != null)
                            fAccount.SetValue(otherShop, true);
                    }
                    catch { }

                    // 填充商品（ID=388，原价）
                    if (otherShop.entries == null)
                        otherShop.entries = new List<StockShop.Entry>();
                    else
                        otherShop.entries.Clear();

                    // 388=原版物品；其余为 Mode E 专属消耗品
                    int[] otherItemIds = modeEMerchantOtherItemIds;
                    foreach (int id in otherItemIds)
                    {
                        try
                        {
                            StockShopDatabase.ItemEntry raw = new StockShopDatabase.ItemEntry();
                            raw.typeID = id;
                            raw.maxStock = 9999;
                            raw.forceUnlock = true;
                            raw.priceFactor = 1.0f;
                            raw.possibility = 1.0f;
                            raw.lockInDemo = false;

                            StockShop.Entry entry = new StockShop.Entry(raw);
                            entry.CurrentStock = entry.MaxStock;
                            entry.Show = true;
                            otherShop.entries.Add(entry);
                        }
                        catch { }
                    }

                    int modeFItemCount = 0;
                    if (modeFActive)
                    {
                        modeFItemCount = TryInjectModeFItemsIntoMerchantShop(otherShop);
                    }

                    // itemInstances 预缓存在末尾由协程统一异步分帧执行

                    // 创建交互选项
                    var otherInteract = otherShopObj.AddComponent<ModeEShopInteractable>();
                    otherInteract.Setup(otherShop, "BossRush_ModeE_Shop_Other");
                    groupList.Add(otherInteract);

                    totalItems += otherItemIds.Length + modeFItemCount;
                    DevLog("[ModeE] 商店分类 其他: " + otherItemIds.Length + " 个物品");
                }

                DevLog("[ModeE] 分类商店注入完成，共 " + (categories.Count + 1) + " 个分类，" + totalItems + " 个商品");

                // 异步分帧预缓存所有商店的 itemInstances，避免打开商店时同步实例化导致卡顿
                // 在缓存完成前打开商店仍可正常使用（原版按需加载兜底）
                StartCoroutine(CacheAllModeEShopItemInstancesAsync());
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] BuildModeEMerchantShop 失败: " + e.Message);
            }
        }

        // ====================================================================
        // 搜索指定分类的物品ID
        // ====================================================================

        /// <summary>
        /// 根据 Tag 搜索所有可用物品ID（品质1及以上）
        /// </summary>
        private CharacterRandomPreset GetModeEMerchantPreset()
        {
            try
            {
                if (cachedModeEMerchantPreset != null)
                {
                    return cachedModeEMerchantPreset;
                }

                CharacterRandomPreset merchantPreset = null;

                if (cachedCharacterPresets != null && cachedCharacterPresets.Count > 0)
                {
                    foreach (var kvp in cachedCharacterPresets)
                    {
                        string nameKey = kvp.Key;
                        if (string.IsNullOrEmpty(nameKey)) continue;
                        if (nameKey.Contains("Merchant") && nameKey.Contains("Myst"))
                        {
                            merchantPreset = kvp.Value;
                            break;
                        }
                    }

                    if (merchantPreset == null)
                    {
                        foreach (var kvp in cachedCharacterPresets)
                        {
                            string nameKey = kvp.Key;
                            if (string.IsNullOrEmpty(nameKey)) continue;
                            if (nameKey.Contains("Merchant"))
                            {
                                merchantPreset = kvp.Value;
                                break;
                            }
                        }
                    }
                }

                if (merchantPreset != null && cachedModeEMerchantPreset == null)
                {
                    cachedModeEMerchantPreset = merchantPreset;
                }

                if (merchantPreset == null)
                {
                    CharacterRandomPreset[] allPresets = ObjectCache.GetCharacterPresets();
                    CharacterRandomPreset fallbackMerchant = null;
                    for (int i = 0; i < allPresets.Length; i++)
                    {
                        CharacterRandomPreset preset = allPresets[i];
                        if (preset == null) continue;

                        try
                        {
                            string nameKey = preset.nameKey;
                            if (string.IsNullOrEmpty(nameKey)) continue;
                            if (nameKey.Contains("Merchant"))
                            {
                                if (nameKey.Contains("Myst"))
                                {
                                    merchantPreset = preset;
                                    break;
                                }

                                if (fallbackMerchant == null)
                                {
                                    fallbackMerchant = preset;
                                }
                            }
                        }
                        catch { }
                    }

                    if (merchantPreset == null)
                    {
                        merchantPreset = fallbackMerchant;
                    }

                    if (merchantPreset == null)
                    {
                        FieldInfo iconField = ReflectionCache.CharacterRandomPreset_CharacterIconType;
                        if (iconField != null)
                        {
                            for (int i = 0; i < allPresets.Length; i++)
                            {
                                CharacterRandomPreset preset = allPresets[i];
                                if (preset == null) continue;

                                try
                                {
                                    if ((int)iconField.GetValue(preset) == 4)
                                    {
                                        merchantPreset = preset;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                if (merchantPreset != null)
                {
                    cachedModeEMerchantPreset = merchantPreset;
                }

                return merchantPreset;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] GetModeEMerchantPreset 失败: " + e.Message);
                return null;
            }
        }

        private List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> GetModeEMerchantCategories(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            var categories = new List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>>();
            if (tagsData == null)
            {
                return categories;
            }

            if (tagsData.Gun != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Gun },
                    "BossRush_ModeE_Shop_Gun", "Gun"));

            Duckov.Utilities.Tag meleeTag = FindTagByNameInInit("MeleeWeapon");
            if (meleeTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { meleeTag },
                    "BossRush_ModeE_Shop_Melee", "Melee"));

            Duckov.Utilities.Tag accessoryTag = FindTagByNameInInit("Accessory");
            if (accessoryTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { accessoryTag },
                    "BossRush_ModeE_Shop_Accessory", "Accessory"));

            if (tagsData.Bullet != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Bullet },
                    "BossRush_ModeE_Shop_Bullet", "Bullet"));

            if (tagsData.Helmat != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Helmat },
                    "BossRush_ModeE_Shop_Helmat", "Helmat"));

            if (tagsData.Armor != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Armor },
                    "BossRush_ModeE_Shop_Armor", "Armor"));

            if (tagsData.Backpack != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Backpack },
                    "BossRush_ModeE_Shop_Backpack", "Backpack"));

            Duckov.Utilities.Tag totemTag = FindTagByNameInInit("Totem");
            if (totemTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { totemTag },
                    "BossRush_ModeE_Shop_Totem", "Totem"));

            Duckov.Utilities.Tag maskTag = FindTagByNameInInit("Mask");
            if (maskTag == null) maskTag = FindTagByNameInInit("FaceMask");
            Duckov.Utilities.Tag headsetTag = FindTagByNameInInit("Headset");
            var faceWearTags = new List<Duckov.Utilities.Tag>();
            if (maskTag != null) faceWearTags.Add(maskTag);
            if (headsetTag != null) faceWearTags.Add(headsetTag);
            if (faceWearTags.Count > 0)
                categories.Add(System.Tuple.Create(
                    faceWearTags,
                    "BossRush_ModeE_Shop_Mask", "Mask"));

            Duckov.Utilities.Tag medTag = FindTagByNameInInit("Medic");
            if (medTag == null) medTag = FindTagByNameInInit("Medical");
            if (medTag == null) medTag = FindTagByNameInInit("Consumable");
            if (medTag == null) medTag = FindTagByNameInInit("Healing");
            if (medTag != null)
            {
                var medTags = new List<Duckov.Utilities.Tag> { medTag };
                Duckov.Utilities.Tag injectorTag = FindTagByNameInInit("Injector");
                if (injectorTag != null)
                {
                    medTags.Add(injectorTag);
                }
                categories.Add(System.Tuple.Create(
                    medTags,
                    "BossRush_ModeE_Shop_Medical", "Medical"));
            }

            Duckov.Utilities.Tag foodTag = FindTagByNameInInit("Food");
            if (foodTag != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { foodTag },
                    "BossRush_ModeE_Shop_Food", "Food"));

            if (tagsData.Bait != null)
                categories.Add(System.Tuple.Create(
                    new List<Duckov.Utilities.Tag> { tagsData.Bait },
                    "BossRush_ModeE_Shop_Bait", "Bait"));

            return categories;
        }

        internal void PrewarmModeEMerchantCaches()
        {
            try
            {
                GetModeEMerchantPreset();

                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    return;
                }

                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
                List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);
                for (int i = 0; i < categories.Count; i++)
                {
                    System.Tuple<List<Duckov.Utilities.Tag>, string, string> category = categories[i];
                    List<int> allIds = ModeESearchItemsMultiTag(category.Item1, emptyExclude);
                    if (allIds == null || allIds.Count == 0)
                    {
                        continue;
                    }

                    if (category.Item3 == "Medical")
                    {
                        allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] PrewarmModeEMerchantCaches failed: " + e.Message);
            }
        }

        private System.Collections.IEnumerator WarmModeEMerchantCachesAsync()
        {
            GetModeEMerchantPreset();
            yield return null;

            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData == null)
            {
                yield break;
            }

            Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
            List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);
            for (int i = 0; i < categories.Count; i++)
            {
                System.Tuple<List<Duckov.Utilities.Tag>, string, string> category = categories[i];
                List<int> allIds = ModeESearchItemsMultiTag(category.Item1, emptyExclude);
                if (allIds != null && category.Item3 == "Medical")
                {
                    allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                }

                yield return null;
            }
        }

        private string BuildModeEMerchantCategoryCacheKey(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags)
        {
            string key = string.Empty;

            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    Duckov.Utilities.Tag tag = tags[i];
                    key += (tag != null ? tag.name : "<null>") + "|";
                }
            }

            key += "#";

            if (excludeTags != null)
            {
                for (int i = 0; i < excludeTags.Length; i++)
                {
                    Duckov.Utilities.Tag tag = excludeTags[i];
                    key += (tag != null ? tag.name : "<null>") + "|";
                }
            }

            return key;
        }

        private int[] ModeESearchItems(Duckov.Utilities.Tag tag, Duckov.Utilities.Tag[] excludeTags)
        {
            try
            {
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { tag };
                filter.excludeTags = excludeTags;
                // 品质限制：1及以上（排除品质0的物品）
                filter.minQuality = 1;
                filter.maxQuality = 99;
                int[] results = ItemAssetsCollection.Search(filter);
                DevLog("[ModeE] ModeESearchItems Tag=" + tag.name + " 找到 " + (results != null ? results.Length : 0) + " 个物品");
                return results;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ModeESearchItems 失败: " + e.Message);
                return null;
            }
        }

        // ====================================================================
        // 多 Tag 合并搜索物品ID（取并集，去重）
        // ====================================================================

        /// <summary>
        /// 根据多个 Tag 搜索物品ID，合并结果（并集去重）
        /// </summary>
        private List<int> ModeESearchItemsMultiTag(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags)
        {
            string cacheKey = BuildModeEMerchantCategoryCacheKey(tags, excludeTags);
            int[] cachedIds;
            if (modeEMerchantCategoryItemCache.TryGetValue(cacheKey, out cachedIds))
            {
                return cachedIds != null ? new List<int>(cachedIds) : new List<int>();
            }

            var idSet = new HashSet<int>();
            foreach (var tag in tags)
            {
                int[] ids = ModeESearchItems(tag, excludeTags);
                if (ids != null)
                {
                    foreach (int id in ids)
                    {
                        idSet.Add(id);
                    }
                }
            }

            List<int> result = new List<int>(idSet);
            modeEMerchantCategoryItemCache[cacheKey] = result.ToArray();
            return result;
        }

        internal int[] GetModeEMerchantCategoryPoolIds(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return new int[0];
            }

            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    return new int[0];
                }

                List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> categories = GetModeEMerchantCategories(tagsData);
                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
                for (int i = 0; i < categories.Count; i++)
                {
                    System.Tuple<List<Duckov.Utilities.Tag>, string, string> category = categories[i];
                    if (!string.Equals(category.Item3, suffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    List<int> allIds = ModeESearchItemsMultiTag(category.Item1, emptyExclude);
                    if (allIds == null)
                    {
                        return new int[0];
                    }

                    if (suffix == "Medical")
                    {
                        allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));
                    }

                    return allIds.ToArray();
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] GetModeEMerchantCategoryPoolIds 失败: " + e.Message);
            }

            return new int[0];
        }

        // ====================================================================
        // 预缓存商店物品实例
        // ====================================================================

        /// <summary>
        /// 异步分帧预缓存所有 Mode E 商店的 itemInstances。
        /// 每帧实例化一批物品（BATCH_SIZE 个），避免低端机商人生成时一次性加载数百物品导致卡顿。
        /// 在缓存完成前打开商店仍可正常使用（原版按需加载兜底），缓存完成后打开商店不再卡顿。
        /// </summary>
        private System.Collections.IEnumerator CacheAllModeEShopItemInstancesAsync()
        {
            const int BATCH_SIZE = 8;

            var fItems = ReflectionCache.StockShop_ItemInstances;
            if (fItems == null) yield break;

            // 快照商店列表，防止 CleanupModeEMerchant 清空列表导致迭代异常
            StockShop[] shops = modeEMerchantShops.ToArray();

            for (int s = 0; s < shops.Length; s++)
            {
                StockShop shop = shops[s];
                if (shop == null || shop.entries == null) continue;

                Dictionary<int, Item> dict;
                try
                {
                    dict = fItems.GetValue(shop) as Dictionary<int, Item>;
                    if (dict == null)
                    {
                        dict = new Dictionary<int, Item>();
                        fItems.SetValue(shop, dict);
                    }
                }
                catch { continue; }

                int entryCount = shop.entries.Count;
                int count = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    // 商店已被销毁则提前退出
                    if (shop == null) break;

                    var entry = shop.entries[i];
                    if (entry == null) continue;
                    int id = entry.ItemTypeID;
                    if (dict.ContainsKey(id)) continue;

                    try
                    {
                        Item item = ItemAssetsCollection.InstantiateSync(id);
                        if (item != null)
                            dict[id] = item;
                    }
                    catch { }

                    count++;
                    if (count % BATCH_SIZE == 0)
                        yield return null;
                }
            }

            DevLog("[ModeE] 所有商店物品实例异步预缓存完成");
        }

        // ====================================================================
        // 清理神秘商人
        // ====================================================================

        /// <summary>
        /// 清理 Mode E 神秘商人 NPC 及所有分类商店引用
        /// </summary>
        private void CleanupModeEMerchant()
        {
            try
            {
                // 清理煤球预设缓存
                ModeEPetSpawner.ClearCache();

                // 销毁所有分类商店的子 GameObject
                RunScopedRegistry.ForEachReverse(
                    modeEMerchantShops,
                    shop =>
                    {
                        if (shop != null && shop.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(shop.gameObject);
                        }
                    },
                    (e, shop) => DevLog("[ModeE] [WARNING] 清理商店子物体失败: " + e.Message));
                modeEMerchantShops.Clear();
                modeEMerchantMainInteract = null;

                // 销毁商人 NPC
                if (modeEMerchantNPC != null)
                {
                    try
                    {
                        if (modeEMerchantNPC.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(modeEMerchantNPC.gameObject);
                        }
                    }
                    catch { }
                    modeEMerchantNPC = null;
                }

                DevLog("[ModeE] 神秘商人已清理");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] CleanupModeEMerchant 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode E 商人静态缓存兜底清理 — 由 IBossRushRuntimeModule.OnDestroy 统一调用。
        /// 作为 CleanupModeEMerchant 的上位兜底，确保模组/场景销毁时所有静态缓存被完整释放。
        /// </summary>
        internal static void ResetModeEMerchantStaticCaches()
        {
            cachedModeEMerchantPreset = null;

            if (modeEMerchantCategoryItemCache != null)
            {
                modeEMerchantCategoryItemCache.Clear();
            }

            ModeEPetSpawner.ClearCache();
            ModeEMerchantSellAllUI.ResetStaticCaches();
        }
    }

    // ========================================================================
    // ModeEShopInteractable — 分类商店交互选项
    // ========================================================================

    /// <summary>
    /// Mode E 分类商店交互选项，注入到商人原有交互组中。
    /// 玩家选择后打开对应分类的 StockShop UI。
    /// </summary>

}
