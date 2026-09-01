// ============================================================================
// RandomEventEffectsBridge_Loot.cs — 随机事件桥（E1 空投箱 / E7 金鸭雨现金）
// ============================================================================
// 模块职责：
//   收口两件需要 ModBehaviour private 基建的产物：
//     1. 空投奖励箱：GetLootBoxTemplateWithLoader / BossLootBoxLoaderReflection /
//        BuildGeneralBossLootCandidateIdSet / BuildGeneralLootExcludeTags /
//        MergeGeneralLootExcludeTags / IsItemBlacklisted 全部是 private。
//     2. 现金堆：官方 InstantiateAsync + Item.Drop，需要分帧避免尖刺。
//
// 硬约束：
//   - 逐条照 LootAndRewardsRandomBossLoot.cs 的 Boss 奖励箱写法，反射字段全部判空，
//     官方改字段名时只是「箱子内容退化」，绝不 NRE。
//   - 空投箱不进扫箱令口径（DecorateLootbox 的 registerSweepTracking 传 false）。
//   - 掉在地上的现金不回收：已经是玩家收益，属设计（无间炼狱现金磁铁按 TypeID 自动吸附）。
//   - 全部方法 no-throw；分帧产出的续作必须靠调用方传入的有效性闭包自查作废。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Scenes;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // ====================================================================
        // E1 空投补给：奖励箱
        // ====================================================================

        /// <summary>
        /// 创建一个空投奖励箱（就地实例化，不做下落动画）。失败返回 null。
        /// 内容按 [qualityMin, qualityMax] 均权、数量固定 itemCount，走官方 LootBoxLoader
        /// 的随机池；候选来源与 Boss 奖励箱同一个（BuildGeneralBossLootCandidateIdSet），
        /// 黑名单唯一入口仍是 IsItemBlacklisted。
        /// </summary>
        internal InteractableLootbox CreateRandomEventAirdropLootbox(
            Vector3 position,
            int itemCount,
            int qualityMin,
            int qualityMax)
        {
            try
            {
                InteractableLootbox prefab = GetLootBoxTemplateWithLoader();
                if (prefab == null)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 未找到 Lootbox 模板，空投取消");
                    return null;
                }

                InteractableLootbox lootbox = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
                if (lootbox == null)
                {
                    return null;
                }

                try { lootbox.needInspect = true; } catch (Exception) { }

                // 独立本地 Inventory：避免与其它 Lootbox 通过位置哈希共享同一份库存
                try
                {
                    InteractableLootboxInventoryHelper.EnsureLocalInventory(
                        lootbox,
                        RandomEventsTuning.AirdropLootboxInventoryCapacity);
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱本地库存创建失败: " + e.Message);
                }

                // registerSweepTracking 传 false：空投不进扫箱令口径。
                try
                {
                    BossRushLootboxUtility.DecorateLootbox(lootbox, this, false, true);
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱外观装饰失败: " + e.Message);
                }

                try
                {
                    MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex);
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱迁移场景失败: " + e.Message);
                }

                Duckov.Utilities.LootBoxLoader loader = null;
                try
                {
                    loader = lootbox.GetComponent<Duckov.Utilities.LootBoxLoader>();
                    if (loader == null)
                    {
                        loader = lootbox.gameObject.AddComponent<Duckov.Utilities.LootBoxLoader>();
                    }
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱 LootBoxLoader 获取失败: " + e.Message);
                }

                if (loader != null)
                {
                    ConfigureRandomEventAirdropLoader(loader, itemCount, qualityMin, qualityMax);
                }

                return lootbox;
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 创建空投箱失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 按空投参数改写 LootBoxLoader 的四个私有字段（数量 / 品质 / tag / 随机池）。
        /// 每个 FieldInfo 都可能为 null（官方改名），一律判空跳过，绝不 NRE。
        /// </summary>
        private void ConfigureRandomEventAirdropLoader(
            Duckov.Utilities.LootBoxLoader loader,
            int itemCount,
            int qualityMin,
            int qualityMax)
        {
            int count = Mathf.Max(1, itemCount);
            int qMin = Mathf.Clamp(qualityMin, 1, 8);
            int qMax = Mathf.Clamp(Mathf.Max(qualityMin, qualityMax), 1, 8);

            // ── 数量 ──────────────────────────────────────────
            try
            {
                FieldInfo randomCountField = BossLootBoxLoaderReflection.RandomCountField;
                if (randomCountField != null)
                {
                    randomCountField.SetValue(loader, new Vector2Int(count, count));
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱数量设置失败: " + e.Message);
            }

            // ── 品质均权 ──────────────────────────────────────
            try
            {
                FieldInfo qualitiesField = BossLootBoxLoaderReflection.QualitiesField;
                if (qualitiesField != null)
                {
                    Duckov.Utilities.RandomContainer<int> qualities =
                        qualitiesField.GetValue(loader) as Duckov.Utilities.RandomContainer<int>;
                    if (qualities != null && qualities.entries != null)
                    {
                        qualities.entries.Clear();
                        for (int q = qMin; q <= qMax; q++)
                        {
                            qualities.AddEntry(q, 1f);
                        }
                        qualities.RefreshPercent();
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱品质设置失败: " + e.Message);
            }

            // ── tag 白名单 / 黑名单 ───────────────────────────
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = null;
            try { tagsData = Duckov.Utilities.GameplayDataSettings.Tags; } catch (Exception) { }

            try
            {
                FieldInfo tagsField = BossLootBoxLoaderReflection.TagsField;
                if (tagsField != null && tagsData != null && tagsData.AllTags != null)
                {
                    Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag> tagsContainer =
                        tagsField.GetValue(loader) as Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag>;
                    if (tagsContainer != null && tagsContainer.entries != null)
                    {
                        tagsContainer.entries.Clear();
                        List<Duckov.Utilities.Tag> tagExclude = BuildGeneralLootExcludeTags(tagsData, true);
                        // AllTags 是 ReadOnlyCollection<Tag>（官方只读视图），不能隐式转 List。
                        // 这里只做顺序遍历，用 IList 接口接住即可，避免多余拷贝。
                        System.Collections.Generic.IList<Duckov.Utilities.Tag> allTags = tagsData.AllTags;
                        for (int i = 0; i < allTags.Count; i++)
                        {
                            Duckov.Utilities.Tag t = allTags[i];
                            if (t == null || (tagExclude != null && tagExclude.Contains(t)))
                            {
                                continue;
                            }
                            tagsContainer.AddEntry(t, 1f);
                        }
                        tagsContainer.RefreshPercent();
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱 tag 设置失败: " + e.Message);
            }

            try
            {
                FieldInfo excludeTagsField = BossLootBoxLoaderReflection.ExcludeTagsField;
                if (excludeTagsField != null)
                {
                    List<Duckov.Utilities.Tag> excludeList =
                        excludeTagsField.GetValue(loader) as List<Duckov.Utilities.Tag>;
                    if (excludeList == null)
                    {
                        excludeList = new List<Duckov.Utilities.Tag>();
                        excludeTagsField.SetValue(loader, excludeList);
                    }
                    MergeGeneralLootExcludeTags(excludeList, tagsData);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱排除 tag 设置失败: " + e.Message);
            }

            // ── 随机池 ────────────────────────────────────────
            try
            {
                FillRandomEventAirdropPool(loader);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱随机池设置失败: " + e.Message);
            }

            // ── fixedItems 必须初始化：LootBoxLoader.Setup() 会裸读它，null 会 NRE ──
            try
            {
                FieldInfo fixedItemsField = BossLootBoxLoaderReflection.FixedItemsField;
                if (fixedItemsField != null)
                {
                    List<int> fixedItems = fixedItemsField.GetValue(loader) as List<int>;
                    if (fixedItems == null)
                    {
                        fixedItems = new List<int>();
                        fixedItemsField.SetValue(loader, fixedItems);
                    }
                    fixedItems.Clear();
                }

                FieldInfo fixedChanceField = BossLootBoxLoaderReflection.FixedChanceField;
                if (fixedChanceField != null)
                {
                    fixedChanceField.SetValue(loader, 0f);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱 fixedItems 初始化失败: " + e.Message);
            }

            // ── 落地填充：EnsureLocalInventory 之后 Inventory 直接返回引用，
            //    不会再自动走 GetOrCreateInventory，必须手动 StartSetup 才有内容 ──
            try
            {
                loader.randomFromPool = true;
                loader.ignoreLevelConfig = true;
                loader.CalculateChances();
                loader.StartSetup();
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投箱填充失败: " + e.Message);
            }
        }

        /// <summary>把通用 Boss 掉落候选写进 loader 的随机池，权重均等。</summary>
        private void FillRandomEventAirdropPool(Duckov.Utilities.LootBoxLoader loader)
        {
            Type loaderEntryType = BossLootBoxLoaderReflection.LoaderEntryType;
            FieldInfo randomPoolField = BossLootBoxLoaderReflection.RandomPoolField;
            if (loaderEntryType == null || randomPoolField == null)
            {
                return;
            }

            object randomPoolObj = randomPoolField.GetValue(loader);
            if (randomPoolObj == null)
            {
                randomPoolObj = Activator.CreateInstance(randomPoolField.FieldType);
                randomPoolField.SetValue(loader, randomPoolObj);
            }
            if (randomPoolObj == null)
            {
                return;
            }

            FieldInfo entriesField = BossLootBoxLoaderReflection.RandomPoolEntriesField;
            if (entriesField == null)
            {
                return;
            }

            IList entriesList = entriesField.GetValue(randomPoolObj) as IList;
            if (entriesList == null)
            {
                object newEntries = Activator.CreateInstance(entriesField.FieldType);
                entriesField.SetValue(randomPoolObj, newEntries);
                entriesList = newEntries as IList;
            }

            Type entryType = BossLootBoxLoaderReflection.RandomPoolEntryType;
            FieldInfo lootEntryItemIdField = BossLootBoxLoaderReflection.LootEntryItemIdField;
            FieldInfo valueField = BossLootBoxLoaderReflection.RandomPoolEntryValueField;
            FieldInfo weightField = BossLootBoxLoaderReflection.RandomPoolEntryWeightField;
            if (entriesList == null || entryType == null ||
                lootEntryItemIdField == null || valueField == null || weightField == null)
            {
                return;
            }

            HashSet<int> candidates = BuildGeneralBossLootCandidateIdSet();
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            entriesList.Clear();
            foreach (int id in candidates)
            {
                try
                {
                    if (IsItemBlacklisted(id))
                    {
                        continue;
                    }

                    object entry = Activator.CreateInstance(entryType);
                    object entryValue = Activator.CreateInstance(loaderEntryType);
                    lootEntryItemIdField.SetValue(entryValue, id);
                    valueField.SetValue(entry, entryValue);
                    weightField.SetValue(entry, 1f);
                    entriesList.Add(entry);
                }
                catch (Exception)
                {
                    // 单个候选失败不影响整箱
                }
            }
        }

        /// <summary>
        /// 空投下落协程：从 groundPos + height 线性落到 groundPos，落地做零伤爆炸 + AI 声源 + 音效。
        /// crate 被销毁（切图 / 关开关 / 事件结束）时协程自行退出，绝不对空引用写坐标。
        /// </summary>
        internal IEnumerator RandomEventAirdropDropRoutine(
            GameObject crate,
            Vector3 groundPos,
            float height,
            float seconds,
            Action onLanded)
        {
            if (crate == null)
            {
                yield break;
            }

            Vector3 start = groundPos + Vector3.up * height;
            float duration = Mathf.Max(0.05f, seconds);
            float t = 0f;

            while (t < duration)
            {
                if (crate == null)
                {
                    yield break;
                }

                crate.transform.position = Vector3.Lerp(start, groundPos, t / duration);
                t += Time.deltaTime;
                yield return null;
            }

            if (crate == null)
            {
                yield break;
            }

            crate.transform.position = groundPos;

            try
            {
                CreateRandomEventHarmlessExplosion(groundPos, ExplosionFxTypes.normal, 0.5f);
                MakeRandomEventAiSound(
                    groundPos,
                    RandomEventsTuning.AirdropLandingSoundRadius,
                    SoundTypes.grenadeDropSound);
                PlayRandomEventModSound("lottery/special.mp3");
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投落地表现失败: " + e.Message);
            }

            if (onLanded != null)
            {
                try { onLanded(); }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 空投落地回调失败: " + e.Message);
                }
            }
        }

        // ====================================================================
        // E7 金鸭雨：现金堆
        // ====================================================================

        /// <summary>
        /// 抛撒现金堆。totalCash 均分到 pileCount 堆，逐堆分帧生成避免单帧尖刺。
        /// 现金 TypeID 451 的 StackCount 就是金额（EconomyManager.Cash == GetItemCount(451)）。
        /// </summary>
        internal void SpawnRandomEventCashPiles(
            Vector3 center,
            long totalCash,
            int pileCount,
            float radius,
            Action<int, int> onCompleted)
        {
            try
            {
                SpawnRandomEventCashPilesAsync(center, totalCash, pileCount, radius, onCompleted).Forget();
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 金鸭雨调度失败: " + e.Message);
                InvokeRandomEventCashCompletion(onCompleted, Mathf.Max(1, pileCount), 0);
            }
        }

        private async UniTaskVoid SpawnRandomEventCashPilesAsync(
            Vector3 center,
            long totalCash,
            int pileCount,
            float radius,
            Action<int, int> onCompleted)
        {
            int piles = Mathf.Max(1, pileCount);
            int spawned = 0;
            try
            {
                long per = Math.Max(1L, totalCash / piles);
                int sceneBuildIndex = SceneManager.GetActiveScene().buildIndex;

                for (int i = 0; i < piles; i++)
                {
                    // 切图后剩余堆一律作废，避免把钱撒到下一张图
                    if (SceneManager.GetActiveScene().buildIndex != sceneBuildIndex)
                    {
                        return;
                    }

                    Item cash = null;
                    try
                    {
                        cash = await ItemAssetsCollection.InstantiateAsync(RandomEventsTuning.CashItemTypeID);
                    }
                    catch (Exception e)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 现金实例化失败: " + e.Message);
                    }

                    if (cash == null)
                    {
                        continue;
                    }

                    if (SceneManager.GetActiveScene().buildIndex != sceneBuildIndex)
                    {
                        try { cash.DestroyTree(); } catch (Exception) { }
                        return;
                    }

                    try
                    {
                        int amount = (int)Math.Min((long)int.MaxValue, per);
                        if (cash.MaxStackCount > 0)
                        {
                            amount = Math.Min(amount, cash.MaxStackCount);
                        }
                        cash.StackCount = Math.Max(1, amount);

                        Vector3 flatDir = UnityEngine.Random.insideUnitSphere;
                        flatDir.y = 0f;
                        if (flatDir.sqrMagnitude < 0.0001f)
                        {
                            flatDir = Vector3.forward;
                        }
                        flatDir.Normalize();

                        Vector3 drop = SpawnPositionHelper.SnapToGround(
                            center + flatDir * UnityEngine.Random.Range(1f, Mathf.Max(1.5f, radius)))
                            + Vector3.up * 0.3f;

                        cash.Drop(drop, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                        spawned++;
                    }
                    catch (Exception e)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 现金落地失败: " + e.Message);
                        try { cash.DestroyTree(); } catch (Exception) { }
                    }

                    await UniTask.Yield();
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 金鸭雨生成失败: " + e.Message);
            }
            finally
            {
                InvokeRandomEventCashCompletion(onCompleted, piles, spawned);
            }
        }

        private static void InvokeRandomEventCashCompletion(Action<int, int> callback, int requested, int spawned)
        {
            if (callback == null) return;
            try { callback(requested, spawned); }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 金鸭雨完成回调失败: " + e.Message);
            }
        }
    }
}
