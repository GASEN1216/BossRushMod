// ============================================================================
// ModeEMerchant.cs — Mode E 神秘商人 NPC
// ============================================================================
// 模块说明：
//   在 Mode E（划地为营）模式启动时生成神秘商人 NPC，提供全品类商店。
//   - 优先通过 nameKey 匹配商人预设（MerchantName_Myst），回退到 iconType==4
//   - 注入原版商人交互点，每个物品分类创建独立商店交互选项
//   - 所有物品价格 ×10，商人生命值设为 999999
//   - Mode E 结束时安全清理商人及商店引用
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

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

        /// <summary>Mode E 神秘商人主交互引用（用于 Harmony patch 识别）</summary>
        private InteractableBase modeEMerchantMainInteract = null;

        /// <summary>获取 Mode E 神秘商人主交互引用（供 Harmony patch 使用）</summary>
        public InteractableBase ModeEMerchantMainInteract => modeEMerchantMainInteract;

        // ====================================================================
        // 生成神秘商人
        // ====================================================================

        /// <summary>
        /// 异步生成神秘商人 NPC，注入分类商店交互选项
        /// </summary>
        private async UniTaskVoid SpawnModeEMerchant()
        {
            try
            {
                // 查找原版商人预设 —— 优先使用缓存字典，避免重复调用 FindObjectsOfTypeAll
                CharacterRandomPreset merchantPreset = null;

                // 优先从缓存字典查找（O(1) 操作）
                if (cachedCharacterPresets != null && cachedCharacterPresets.Count > 0)
                {
                    // 尝试精确匹配神秘商人
                    foreach (var kvp in cachedCharacterPresets)
                    {
                        string nameKey = kvp.Key;
                        if (string.IsNullOrEmpty(nameKey)) continue;
                        if (nameKey.Contains("Merchant") && nameKey.Contains("Myst"))
                        {
                            merchantPreset = kvp.Value;
                            DevLog("[ModeE] 从缓存找到神秘商人预设: " + nameKey);
                            break;
                        }
                    }
                    // 回退到任意商人
                    if (merchantPreset == null)
                    {
                        foreach (var kvp in cachedCharacterPresets)
                        {
                            if (kvp.Key.Contains("Merchant"))
                            {
                                merchantPreset = kvp.Value;
                                DevLog("[ModeE] 从缓存找到商人预设 (回退): " + kvp.Key);
                                break;
                            }
                        }
                    }
                }

                // 缓存未命中时回退到 FindObjectsOfTypeAll（仅首次或缓存为空时）
                if (merchantPreset == null)
                {
                    var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                    CharacterRandomPreset fallbackMerchant = null;
                    foreach (var preset in allPresets)
                    {
                        try
                        {
                            string nameKey = preset.nameKey;
                            if (string.IsNullOrEmpty(nameKey)) continue;
                            if (nameKey.Contains("Merchant"))
                            {
                                if (nameKey.Contains("Myst"))
                                {
                                    merchantPreset = preset;
                                    DevLog("[ModeE] 找到神秘商人预设 (nameKey): " + nameKey);
                                    break;
                                }
                                if (fallbackMerchant == null)
                                    fallbackMerchant = preset;
                            }
                        }
                        catch { }
                    }

                    if (merchantPreset == null && fallbackMerchant != null)
                        merchantPreset = fallbackMerchant;

                    // 回退到 characterIconType == 4
                    if (merchantPreset == null)
                    {
                        var iconField = ReflectionCache.CharacterRandomPreset_CharacterIconType;
                        if (iconField != null)
                        {
                            foreach (var preset in allPresets)
                            {
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
                    UnityEngine.Object.DestroyImmediate(origShop);
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

                // 获取 Tag 系统
                var tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData == null)
                {
                    DevLog("[ModeE] [WARNING] 无法获取 TagsData");
                    return;
                }

                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];

                // 定义分类列表：(标签列表, 本地化键, merchantID后缀)
                // 使用本地化键代替原始中文字符串
                var categories = new List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>>();

                // 枪械
                if (tagsData.Gun != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { tagsData.Gun },
                        "BossRush_ModeE_Shop_Gun", "Gun"));
                // 近战武器
                var meleeTag = FindTagByNameInInit("MeleeWeapon");
                if (meleeTag != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { meleeTag },
                        "BossRush_ModeE_Shop_Melee", "Melee"));

                // 配件模组 —— 仅使用 Accessory 标签
                var accessoryTag = FindTagByNameInInit("Accessory");
                if (accessoryTag != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { accessoryTag },
                        "BossRush_ModeE_Shop_Accessory", "Accessory"));

                // 子弹
                if (tagsData.Bullet != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { tagsData.Bullet },
                        "BossRush_ModeE_Shop_Bullet", "Bullet"));
                // 头盔
                if (tagsData.Helmat != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { tagsData.Helmat },
                        "BossRush_ModeE_Shop_Helmat", "Helmat"));
                // 护甲
                if (tagsData.Armor != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { tagsData.Armor },
                        "BossRush_ModeE_Shop_Armor", "Armor"));
                // 背包
                if (tagsData.Backpack != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { tagsData.Backpack },
                        "BossRush_ModeE_Shop_Backpack", "Backpack"));
                // 图腾
                var totemTag = FindTagByNameInInit("Totem");
                if (totemTag != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { totemTag },
                        "BossRush_ModeE_Shop_Totem", "Totem"));
                // 面具
                var maskTag = FindTagByNameInInit("Mask");
                if (maskTag == null) maskTag = FindTagByNameInInit("FaceMask");
                if (maskTag != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { maskTag },
                        "BossRush_ModeE_Shop_Mask", "Mask"));
                // 医疗品
                var medTag = FindTagByNameInInit("Medical");
                if (medTag == null) medTag = FindTagByNameInInit("Consumable");
                if (medTag == null) medTag = FindTagByNameInInit("Healing");
                if (medTag != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { medTag },
                        "BossRush_ModeE_Shop_Medical", "Medical"));
                // 诱饵
                if (tagsData.Bait != null)
                    categories.Add(System.Tuple.Create(
                        new List<Duckov.Utilities.Tag> { tagsData.Bait },
                        "BossRush_ModeE_Shop_Bait", "Bait"));

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

                    // 填充商品（价格 ×10）
                    // 注意：不预缓存 itemInstances，由 StockShop 按需加载，节省内存
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
                            raw.priceFactor = 10.0f; // 价格 ×10
                            raw.possibility = 1.0f;
                            raw.lockInDemo = false;

                            StockShop.Entry entry = new StockShop.Entry(raw);
                            entry.CurrentStock = entry.MaxStock;
                            shop.entries.Add(entry);
                        }
                        catch { }
                    }

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
                    // 注意：不预缓存 itemInstances，由 StockShop 按需加载
                    if (otherShop.entries == null)
                        otherShop.entries = new List<StockShop.Entry>();
                    else
                        otherShop.entries.Clear();

                    int[] otherItemIds = new int[] { 388 };
                    foreach (int id in otherItemIds)
                    {
                        try
                        {
                            StockShopDatabase.ItemEntry raw = new StockShopDatabase.ItemEntry();
                            raw.typeID = id;
                            raw.maxStock = 9999;
                            raw.forceUnlock = true;
                            raw.priceFactor = 1.0f; // 原价
                            raw.possibility = 1.0f;
                            raw.lockInDemo = false;

                            StockShop.Entry entry = new StockShop.Entry(raw);
                            entry.CurrentStock = entry.MaxStock;
                            otherShop.entries.Add(entry);
                        }
                        catch { }
                    }

                    // 创建交互选项
                    var otherInteract = otherShopObj.AddComponent<ModeEShopInteractable>();
                    otherInteract.Setup(otherShop, "BossRush_ModeE_Shop_Other");
                    groupList.Add(otherInteract);

                    totalItems += otherItemIds.Length;
                    DevLog("[ModeE] 商店分类 其他: " + otherItemIds.Length + " 个物品");
                }

                DevLog("[ModeE] 分类商店注入完成，共 " + (categories.Count + 1) + " 个分类，" + totalItems + " 个商品");
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
            var idSet = new HashSet<int>();
            foreach (var tag in tags)
            {
                int[] ids = ModeESearchItems(tag, excludeTags);
                if (ids != null)
                {
                    foreach (int id in ids)
                        idSet.Add(id);
                }
            }
            return new List<int>(idSet);
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
                for (int i = modeEMerchantShops.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        StockShop shop = modeEMerchantShops[i];
                        if (shop != null && shop.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(shop.gameObject);
                        }
                    }
                    catch { }
                }
                modeEMerchantShops.Clear();

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
    }

    // ========================================================================
    // ModeEShopInteractable — 分类商店交互选项
    // ========================================================================

    /// <summary>
    /// Mode E 分类商店交互选项，注入到商人原有交互组中。
    /// 玩家选择后打开对应分类的 StockShop UI。
    /// </summary>
    public class ModeEShopInteractable : InteractableBase
    {
        /// <summary>关联的 StockShop 实例</summary>
        private StockShop _shop;

        /// <summary>显示名称（如"枪械"、"护甲"等）</summary>
        private string _displayName;

        /// <summary>
        /// 初始化交互选项
        /// </summary>
        public void Setup(StockShop shop, string displayName)
        {
            _shop = shop;
            _displayName = displayName;
            this.overrideInteractName = true;
            this._overrideInteractNameKey = displayName;
        }

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
            try { base.Awake(); } catch { }
            try
            {
                // 禁用碰撞体（作为子交互选项不需要独立碰撞检测）
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                    this.interactCollider.enabled = false;
            }
            catch { }
            try { this.MarkerActive = false; } catch { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch { }
            try
            {
                // Start 后重新设置名称（防止被 base.Start 覆盖）
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayName))
                    this._overrideInteractNameKey = _displayName;
            }
            catch { }
        }

        protected override bool IsInteractable()
        {
            return _shop != null;
        }

        /// <summary>
        /// 玩家选择此交互选项时，打开对应分类的商店 UI
        /// </summary>
        protected override void OnTimeOut()
        {
            try
            {
                if (_shop == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] ModeEShopInteractable: _shop 为 null");
                    return;
                }
                _shop.ShowUI();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ModeEShopInteractable.OnTimeOut 失败: " + e.Message);
            }
        }
    }

    // ========================================================================
    // ModeEPetSpawner — 召唤煤球辅助类
    // ========================================================================

    /// <summary>
    /// Mode E 召唤煤球辅助类。
    /// 提供静态方法用于生成煤球宠物NPC。
    /// </summary>
    public static class ModeEPetSpawner
    {
        /// <summary>缓存的煤球预设（避免重复查找）</summary>
        private static CharacterRandomPreset cachedCoalballPreset = null;

        /// <summary>
        /// 异步生成煤球宠物（供 Harmony patch 调用）
        /// </summary>
        public static void SpawnPet()
        {
            SpawnPetAsync().Forget();
        }

        /// <summary>
        /// 清理缓存（Mode E 结束时调用）
        /// </summary>
        public static void ClearCache()
        {
            cachedCoalballPreset = null;
        }

        /// <summary>
        /// 重置宠物NPC的雇佣交互点状态，防止位置哈希导致的状态复用
        /// 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
        /// 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
        /// </summary>
        private static void ResetPetHireInteractable(GameObject petGo)
        {
            try
            {
                if (petGo == null) return;

                // 查找宠物NPC上的所有 InteractableBase 组件
                var interactables = petGo.GetComponentsInChildren<InteractableBase>(true);
                if (interactables == null || interactables.Length == 0)
                {
                    ModBehaviour.DevLog("[ModeE] 煤球NPC上未找到 InteractableBase");
                    return;
                }

                foreach (var interact in interactables)
                {
                    if (interact == null) continue;

                    // 通过反射重置 requireItem 和 requireItemUsed 状态
                    try
                    {
                        // 获取 requireItem 字段
                        var requireItemField = typeof(InteractableBase).GetField("requireItem", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemUsed 字段
                        var requireItemUsedField = typeof(InteractableBase).GetField("requireItemUsed", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemId 字段（用于判断是否是雇佣交互）
                        var requireItemIdField = typeof(InteractableBase).GetField("requireItemId", 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (requireItemField != null && requireItemIdField != null)
                        {
                            int itemId = (int)requireItemIdField.GetValue(interact);
                            // 只重置需要 ID=388 物品的交互点（雇佣交互）
                            if (itemId == 388)
                            {
                                requireItemField.SetValue(interact, true);
                                if (requireItemUsedField != null)
                                {
                                    requireItemUsedField.SetValue(interact, false);
                                }
                                ModBehaviour.DevLog("[ModeE] 已重置煤球雇佣交互点状态 (requireItemId=388)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModBehaviour.DevLog("[ModeE] [WARNING] 重置交互点状态失败: " + ex.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ResetPetHireInteractable 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 异步生成煤球NPC
        /// </summary>
        private static async UniTaskVoid SpawnPetAsync()
        {
            try
            {
                // 获取玩家位置
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 召唤煤球：玩家为空");
                    return;
                }

                // 查找煤球预设（优先使用缓存）
                CharacterRandomPreset coalballPreset = cachedCoalballPreset;

                if (coalballPreset == null)
                {
                    // 优先从 ModBehaviour 的缓存字典查找
                    var inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        // 通过反射获取 cachedCharacterPresets（如果可访问）
                        // 回退到 FindObjectsOfTypeAll
                        try
                        {
                            var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                            foreach (var preset in allPresets)
                            {
                                if (preset == null) continue;
                                try
                                {
                                    string nameKey = preset.nameKey;
                                    if (!string.IsNullOrEmpty(nameKey) && nameKey.Contains("SnowPMC"))
                                    {
                                        coalballPreset = preset;
                                        cachedCoalballPreset = preset; // 缓存以供后续使用
                                        ModBehaviour.DevLog("[ModeE] 找到煤球预设: " + nameKey);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                if (coalballPreset == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 未找到煤球预设 (Character_SnowPMC)，无法召唤");
                    NotificationText.Push(L10n.T("未找到煤球预设", "Coalball preset not found"));
                    return;
                }

                // 在玩家前方生成煤球
                Vector3 spawnPos = player.transform.position + player.transform.forward * 1.5f;
                Vector3 dir = -player.transform.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                var coalballCharacter = await coalballPreset.CreateCharacterAsync(spawnPos, dir, relatedScene, null, false);
                if (coalballCharacter == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 煤球生成失败");
                    return;
                }

                // 设置煤球为玩家阵营
                var inst2 = ModBehaviour.Instance;
                if (inst2 != null)
                {
                    coalballCharacter.SetTeam(inst2.ModeEPlayerFaction);
                }
                else
                {
                    coalballCharacter.SetTeam(Teams.player);
                }

                // [修复] 重置煤球NPC的雇佣交互点状态，防止位置哈希导致的状态复用
                // 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
                // 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
                ResetPetHireInteractable(coalballCharacter.gameObject);

                ModBehaviour.DevLog("[ModeE] 煤球召唤成功");
                NotificationText.Push(L10n.T("煤球已召唤！", "Coalball summoned!"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] SpawnPetAsync 失败: " + e.Message);
            }
        }
    }
}
