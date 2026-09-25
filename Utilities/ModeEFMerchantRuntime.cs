// E/F 共用商人实体、动态商店装配、预热和清理。
// 贝壳事务、交互类型和 F 专属商品通过模式策略绑定，状态只存一份。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed class ModeEFMerchantPolicy
    {
        internal Func<int, int, int, int, bool> IsSpawnSessionValid;
        internal Func<bool> ShellEconomyAvailable;
        internal Func<bool> VerifyShellPatchInstallation;
        internal Action<string, bool> SetShellEconomyUnavailable;
        internal Func<Teams> PlayerFaction;
        internal Action<CharacterMainControl> SetMerchantHealth;
        internal Func<bool> IsModeEActive;
        internal Func<bool> IsModeFActive;
        internal Func<string, bool> BeginShellMerchantGeneration;
        internal Func<StockShop, bool> RegisterShellMerchantShop;
        internal Func<StockShop, int> InjectModeFItems;
        internal Action<StockShop[]> WarmShellShops;
        internal Action<IEnumerator> StartCoroutine;
        internal Action ClearPetCache;
        internal Func<bool> HasShellSessionState;
        internal Action<string> InvalidateShellMerchantGeneration;
        internal Func<GameObject, StockShop, string, InteractableBase> CreateShopInteraction;
    }

    internal sealed class ModeEFMerchantRuntime
    {
        private readonly ModeEFMerchantPolicy policy;
        private readonly ModeEFMerchantCatalog merchantCatalog;
        private readonly int[] modeEMerchantOtherItemIds;

        internal ModeEFMerchantRuntime(ModeEFMerchantCatalog merchantCatalog, int[] otherItemIds, ModeEFMerchantPolicy policy)
        {
            this.merchantCatalog = merchantCatalog;
            this.modeEMerchantOtherItemIds = otherItemIds;
            this.policy = policy;
        }

        internal List<StockShop> Shops { get { return modeEMerchantShops; } }

        // ====================================================================
        // Mode E 神秘商人字段
        // ====================================================================

        /// <summary>Mode E 神秘商人 NPC 实例引用</summary>
        private CharacterMainControl modeEMerchantNPC = null;

        /// <summary>Mode E 神秘商人各分类商店列表（清理用）</summary>
        private List<StockShop> modeEMerchantShops = new List<StockShop>();

        /// <summary>仅用于让动态 StockShop.Awake 通过官方商人资料初始化。</summary>
        private const string ModeEMerchantAwakeBootstrapId = "Merchant_Normal";

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
        internal async UniTaskVoid SpawnModeEMerchant(
            int modeFSessionToken = 0,
            int modeFRelatedScene = -1,
            int modeESessionToken = 0,
            int modeESessionRelatedScene = -1)
        {
            CharacterMainControl spawnedCharacter = null;
            Func<bool> isRequestCurrent = () => policy.IsSpawnSessionValid(
                modeFSessionToken, modeFRelatedScene, modeESessionToken, modeESessionRelatedScene);
            try
            {
                if (modeESessionToken > 0 &&
                    (!policy.ShellEconomyAvailable() || !policy.VerifyShellPatchInstallation()))
                {
                    policy.SetShellEconomyUnavailable("merchant spawn preflight failed", true);
                    return;
                }

                CharacterRandomPreset merchantPreset = GetModeEMerchantPreset();

                if (merchantPreset == null)
                {
                    ModBehaviour.DevLog("[ModeE] [ERROR] 未找到任何商人预设，跳过神秘商人生成");
                    if (modeESessionToken > 0)
                    {
                        FailModeEShellMerchantBuild(null, "merchant preset unavailable");
                    }
                    return;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    ModBehaviour.DevLog("[ModeE] [ERROR] 玩家实例为空，跳过神秘商人生成");
                    if (modeESessionToken > 0)
                    {
                        FailModeEShellMerchantBuild(null, "merchant player unavailable");
                    }
                    return;
                }

                Vector3 spawnPos = player.transform.position + player.transform.forward * 2f;
                Vector3 dir = -player.transform.forward;
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                spawnedCharacter = await merchantPreset.CreateCharacterAsync(
                    spawnPos,
                    dir,
                    relatedScene,
                    null,
                    false);
                CharacterMainControl character = spawnedCharacter;
                if (!isRequestCurrent())
                {
                    if (character != null) UnityEngine.Object.Destroy(character.gameObject);
                    return;
                }
                if (character == null)
                {
                    ModBehaviour.DevLog("[ModeE] [ERROR] CreateCharacterAsync 返回空，神秘商人生成失败");
                    if (modeESessionToken > 0)
                    {
                        FailModeEShellMerchantBuild(null, "merchant character creation failed");
                    }
                    return;
                }

                if (!policy.IsSpawnSessionValid(
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

                    ModBehaviour.DevLog("[ModeE] 商人生成完成时模式已结束或场景已切换，已放弃该实例");
                    return;
                }

                if (modeESessionToken > 0 &&
                    (!policy.ShellEconomyAvailable() || !policy.VerifyShellPatchInstallation()))
                {
                    FailModeEShellMerchantBuild(
                        character.gameObject,
                        "merchant async continuation preflight failed");
                    return;
                }

                // 验证阵营有效性后再设置（policy.PlayerFaction() 默认为 Teams.player）
                character.SetTeam(policy.PlayerFaction());
                modeEMerchantNPC = character;

                // 设置商人生命值为 999999
                policy.SetMerchantHealth(character);

                ModBehaviour.DevLog("[ModeE] 神秘商人 NPC 生成成功，阵营: " + character.Team);

                // 注入分类商店交互选项
                BuildModeEMerchantShop(character.gameObject);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] SpawnModeEMerchant 失败: " + e.Message);
                if (!isRequestCurrent())
                {
                    if (spawnedCharacter != null) UnityEngine.Object.Destroy(spawnedCharacter.gameObject);
                    return;
                }
                if (modeESessionToken > 0)
                {
                    GameObject spawnedGo = null;
                    try
                    {
                        if (spawnedCharacter != null) spawnedGo = spawnedCharacter.gameObject;
                    }
                    catch { }
                    FailModeEShellMerchantBuild(spawnedGo, "merchant spawn failed");
                }
            }
        }

        // ====================================================================
        // 构建分类商店并注入交互选项
        // ====================================================================

        private void FailModeEShellMerchantBuild(GameObject npcGo, string reason)
        {
            policy.SetShellEconomyUnavailable(reason, true);
            modeEMerchantMainInteract = null;
            try
            {
                if (npcGo == null && modeEMerchantNPC != null)
                {
                    npcGo = modeEMerchantNPC.gameObject;
                }
                if (npcGo != null)
                {
                    npcGo.SetActive(false);
                    UnityEngine.Object.Destroy(npcGo);
                }
            }
            catch { }
            modeEMerchantNPC = null;
        }

        private bool TryConfigureModeEMerchantShopIdentity(
            StockShop shop,
            string merchantID,
            bool requireVerified)
        {
            try
            {
                var merchantField = BossRushEagerReflectionCache.StockShop_MerchantID;
                var accountField = BossRushEagerReflectionCache.StockShop_AccountAvaliable;
                if (merchantField == null || accountField == null)
                {
                    if (requireVerified)
                    {
                        ModBehaviour.DevLog("[ModeE/Shell] 商店 merchantID/accountAvaliable 反射字段缺失");
                    }
                    return !requireVerified;
                }

                merchantField.SetValue(shop, merchantID);
                accountField.SetValue(shop, true);
                if (!requireVerified) return true;

                object merchantReadBack = merchantField.GetValue(shop);
                object accountReadBack = accountField.GetValue(shop);
                bool valid = merchantReadBack is string &&
                             string.Equals((string)merchantReadBack, merchantID, StringComparison.Ordinal) &&
                             accountReadBack is bool && (bool)accountReadBack &&
                             string.Equals(shop.MerchantID, merchantID, StringComparison.Ordinal) &&
                             shop.AccountAvaliable;
                if (!valid)
                {
                    ModBehaviour.DevLog("[ModeE/Shell] 商店 merchantID/accountAvaliable 设置回读失败: " +
                        merchantID);
                }
                return valid;
            }
            catch (Exception e)
            {
                if (requireVerified)
                {
                    ModBehaviour.DevLog("[ModeE/Shell] 商店身份设置异常: " + merchantID + ", " + e.Message);
                }
                return !requireVerified;
            }
        }

        /// <summary>以官方 ID 引导动态 StockShop.Awake，再于 Start 前恢复稳定 ID。</summary>
        private StockShop CreateConfiguredModeEMerchantShop(GameObject shopObject,
            string stableMerchantId)
        {
            if (shopObject == null || string.IsNullOrEmpty(stableMerchantId)) return null;
            try
            {
                shopObject.SetActive(false);
                StockShop shop = shopObject.AddComponent<StockShop>();
                if (!TryConfigureModeEMerchantShopIdentity(
                        shop, ModeEMerchantAwakeBootstrapId, true))
                {
                    UnityEngine.Object.Destroy(shopObject);
                    return null;
                }
                shopObject.SetActive(true);
                if (!TryConfigureModeEMerchantShopIdentity(shop, stableMerchantId, true))
                {
                    shopObject.SetActive(false);
                    UnityEngine.Object.Destroy(shopObject);
                    return null;
                }
                return shop;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] 动态分类商店初始化失败: "
                    + stableMerchantId + ", " + e.Message);
                try {
                    shopObject.SetActive(false);
                    UnityEngine.Object.Destroy(shopObject);
                } catch (Exception cleanupException) {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 动态分类商店失败清理异常: " + cleanupException.Message);
                }
                return null;
            }
        }

        /// <summary>
        /// 找到商人原有的 InteractableBase，保留原版交互但修改其显示名称为"召唤煤球"，
        /// 并注入每个物品分类的独立商店交互选项。
        /// 使用 Harmony patch 来拦截主交互的 OnTimeOut 实现召唤煤球功能。
        /// </summary>
        private void BuildModeEMerchantShop(GameObject npcGo)
        {
            bool shellMode = policy.IsModeEActive() && !policy.IsModeFActive();
            try
            {
                if (shellMode && !policy.BeginShellMerchantGeneration("merchant create/rebuild"))
                {
                    FailModeEShellMerchantBuild(npcGo, "merchant generation preflight failed");
                    return;
                }

                // 找到商人原有的 InteractableBase（主交互点）
                InteractableBase mainInteract = npcGo.GetComponentInChildren<InteractableBase>(true);
                if (mainInteract == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 商人 NPC 上未找到 InteractableBase，无法注入商店");
                    if (shellMode)
                    {
                        FailModeEShellMerchantBuild(npcGo, "merchant interactable missing");
                    }
                    return;
                }

                // 销毁原版商人自带的 StockShop，防止原始交易选项出现
                var origShop = npcGo.GetComponentInChildren<StockShop>(true);
                if (origShop != null)
                {
                    UnityEngine.Object.Destroy(origShop);
                    ModBehaviour.DevLog("[ModeE] 已移除商人原版 StockShop");
                }

                // 保存商人主交互引用，用于 Harmony patch 识别
                modeEMerchantMainInteract = mainInteract;

                // 修改主交互显示名称为"召唤煤球"（第一个选项）
                mainInteract.overrideInteractName = true;
                mainInteract._overrideInteractNameKey = "BossRush_ModeE_SummonPet";

                // 启用交互组（允许多个子选项）
                mainInteract.interactableGroup = true;

                // 获取 otherInterablesInGroup 列表
                var field = BossRushEagerReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null)
                {
                    ModBehaviour.DevLog("[ModeE] [ERROR] 未找到 otherInterablesInGroup 反射字段");
                    if (shellMode)
                    {
                        FailModeEShellMerchantBuild(npcGo, "merchant interaction group contract missing");
                    }
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
                    ModBehaviour.DevLog("[ModeE] [WARNING] 无法获取 TagsData");
                    if (shellMode)
                    {
                        FailModeEShellMerchantBuild(npcGo, "merchant tags unavailable");
                    }
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
                        int removedCount = allIds.RemoveAll(id => merchantCatalog.IsExcludedMedicalItem(id));
                        if (allIds.Count == 0) continue;
                        if (removedCount > 0)
                            ModBehaviour.DevLog("[ModeE] 医疗品商店已排除 " + removedCount + " 个黑名单物品");
                    }

                    // 创建子 GameObject 挂载商店和交互
                    GameObject shopObj = new GameObject("ModeEShop_" + suffix);
                    shopObj.transform.SetParent(mainInteract.transform);
                    shopObj.transform.localPosition = Vector3.zero;
                    shopObj.transform.localRotation = Quaternion.identity;
                    shopObj.transform.localScale = Vector3.one;

                    // 创建 StockShop 组件：先以官方 ID 引导 Awake，再切回稳定分类 ID。
                    StockShop shop = CreateConfiguredModeEMerchantShop(
                        shopObj, "ModeE_" + suffix);
                    if (shop == null)
                    {
                        FailModeEShellMerchantBuild(npcGo, "shop identity/bootstrap failed");
                        return;
                    }
                    if (shellMode)
                    {
                        if (!policy.RegisterShellMerchantShop(shop))
                        {
                            UnityEngine.Object.Destroy(shopObj);
                            FailModeEShellMerchantBuild(npcGo, "shop registration failed");
                            return;
                        }
                    }
                    else
                    {
                        modeEMerchantShops.Add(shop);
                    }

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
                    var interact = policy.CreateShopInteraction(shopObj, shop, locKey);
                    groupList.Add(interact);

                    totalItems += allIds.Count;
                    ModBehaviour.DevLog("[ModeE] 商店分类 " + locKey + ": " + allIds.Count + " 个物品");
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

                    StockShop otherShop = CreateConfiguredModeEMerchantShop(
                        otherShopObj, "ModeE_Other");
                    if (otherShop == null)
                    {
                        FailModeEShellMerchantBuild(npcGo, "other shop identity/bootstrap failed");
                        return;
                    }
                    if (shellMode)
                    {
                        if (!policy.RegisterShellMerchantShop(otherShop))
                        {
                            UnityEngine.Object.Destroy(otherShopObj);
                            FailModeEShellMerchantBuild(npcGo, "other shop registration failed");
                            return;
                        }
                    }
                    else
                    {
                        modeEMerchantShops.Add(otherShop);
                    }

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
                    if (policy.IsModeFActive())
                    {
                        modeFItemCount = policy.InjectModeFItems(otherShop);
                    }

                    // itemInstances 预缓存在末尾由协程统一异步分帧执行

                    // 创建交互选项
                    var otherInteract = policy.CreateShopInteraction(otherShopObj, otherShop, "BossRush_ModeE_Shop_Other");
                    groupList.Add(otherInteract);

                    totalItems += otherItemIds.Length + modeFItemCount;
                    ModBehaviour.DevLog("[ModeE] 商店分类 其他: " + otherItemIds.Length + " 个物品");
                }

                ModBehaviour.DevLog("[ModeE] 分类商店注入完成，共 " + (categories.Count + 1) + " 个分类，" + totalItems + " 个商品");

                if (shellMode)
                {
                    policy.WarmShellShops(modeEMerchantShops.ToArray());
                }
                else
                {
                    // Mode F 保留旧预热；贝壳经济从不写入或拥有官方 itemInstances。
                    policy.StartCoroutine(CacheAllModeFShopItemInstancesAsync());
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] BuildModeEMerchantShop 失败: " + e.Message);
                if (shellMode)
                {
                    FailModeEShellMerchantBuild(npcGo, "merchant build failed");
                }
            }
        }

        // ====================================================================
        // 搜索指定分类的物品ID
        // ====================================================================

        /// <summary>
        /// 根据 Tag 搜索所有可用物品ID（品质1及以上）
        /// </summary>
        internal CharacterRandomPreset GetModeEMerchantPreset()
        { return merchantCatalog.GetModeEMerchantPreset(); }

        private List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> GetModeEMerchantCategories(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        { return merchantCatalog.GetModeEMerchantCategories(tagsData); }

        private List<int> ModeESearchItemsMultiTag(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags)
        { return merchantCatalog.ModeESearchItemsMultiTag(tags, excludeTags); }

        // ====================================================================
        // 预缓存商店物品实例
        // ====================================================================

        /// <summary>
        /// 异步分帧预缓存所有 Mode E 商店的 itemInstances。
        /// 每帧实例化一批物品（BATCH_SIZE 个），避免低端机商人生成时一次性加载数百物品导致卡顿。
        /// 在缓存完成前打开商店仍可正常使用（原版按需加载兜底），缓存完成后打开商店不再卡顿。
        /// </summary>
        private System.Collections.IEnumerator CacheAllModeFShopItemInstancesAsync()
        {
            const int BATCH_SIZE = 8;

            var fItems = BossRushEagerReflectionCache.StockShop_ItemInstances;
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

            ModBehaviour.DevLog("[ModeE] 所有商店物品实例异步预缓存完成");
        }

        // ====================================================================
        // 清理神秘商人
        // ====================================================================

        /// <summary>
        /// 清理 Mode E 神秘商人 NPC 及所有分类商店引用
        /// </summary>
        internal void CleanupModeEMerchant()
        {
            try
            {
                // 清理煤球预设缓存
                policy.ClearPetCache();

                if (policy.HasShellSessionState())
                {
                    policy.InvalidateShellMerchantGeneration("CleanupModeEMerchant");
                }

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
                    (e, shop) => ModBehaviour.DevLog("[ModeE] [WARNING] 清理商店子物体失败: " + e.Message));
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

                ModBehaviour.DevLog("[ModeE] 神秘商人已清理");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [WARNING] CleanupModeEMerchant 失败: " + e.Message);
            }
        }

    }
}
