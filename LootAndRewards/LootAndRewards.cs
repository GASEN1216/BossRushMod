// ============================================================================
// LootAndRewards.cs - 掉落与奖励系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的掉落和奖励系统，包括：
//   - Boss 掉落物生成和随机化
//   - 通关奖励箱生成
//   - 无间炼狱模式的现金池和特殊奖励
//   - 掉落物品黑名单管理
//   
// 主要功能：
//   - OnInfiniteHellWaveCompleted: 无间炼狱单波完成处理
//   - OnAllEnemiesDefeated: 所有敌人击败后的通关处理
//   - SpawnDifficultyRewardLootbox: 生成通关奖励箱
//   - GetRandomInfiniteHellHighQualityRewardTypeID: 获取高品质奖励物品
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    /// <summary>
    /// 掉落与奖励系统模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private static InteractableLootbox _cachedLootBoxTemplateWithLoader = null;
        private static InteractableLootbox _cachedDifficultyRewardLootBoxTemplate = null;
        private static readonly HashSet<int> ManualLootBlacklist = new HashSet<int>
        {
            153, 284, 292, 293, 294, 295, 297, 299, 300,
            313, 314, 315, 316, 317, 366, 373, 375, 376,
            385, 386, 427, 672, 745, 747, 750, 751, 753, 757,
            760, 761, 762, 763, 766, 774, 775, 778, 809,
            811, 814, 816, 817, 823, 824, 825, 835, 1046,
            1047, 1048, 1049, 1050, 1051, 1052, 1053, 1054,
            1062, 1064, 1065, 1066, 1067, 1068, 1069, 1073,
            1092, 1158, 1164, 1214, 1225, 1249, 1273,
            // 龙套装（龙裔遗族Boss专属掉落，不应出现在随机掉落池中）
            500003, // 赤龙首（龙头）
            500004  // 焰鳞甲（龙甲）
        };

        private List<EnemyPresetInfo> enemyPresets = new List<EnemyPresetInfo>();
        private float minBossBaseHealth = 100f;
        private float maxBossBaseHealth = 100f;

        private readonly Dictionary<CharacterMainControl, float> bossSpawnTimes = new Dictionary<CharacterMainControl, float>();
        private readonly Dictionary<CharacterMainControl, int> bossOriginalLootCounts = new Dictionary<CharacterMainControl, int>();
        private readonly HashSet<CharacterMainControl> countedDeadBosses = new HashSet<CharacterMainControl>();

        private bool infiniteHellMode = false;
        private int infiniteHellWaveIndex = 0;
        private long infiniteHellCashPool = 0L;
        private bool infiniteHell100WaveRewardGiven = false;
        private long infiniteHellWaveCashThisWave = 0L;
        private List<int> infiniteHellHighQualityItemPool = null;
        private bool infiniteHellHighQualityItemPoolInitialized = false;

        // ============================================================================
        // 物品价值缓存系统 - 避免Boss死亡时同步实例化大量物品导致卡顿
        // ============================================================================
        private static Dictionary<int, ItemValueCacheEntry> _itemValueCache = null;
        private static bool _itemValueCacheInitialized = false;
        private static bool _itemValueCacheInitializing = false;

        /// <summary>
        /// 物品价值缓存条目
        /// </summary>
        private struct ItemValueCacheEntry
        {
            public int value;
            public int quality;
        }

        /// <summary>
        /// 初始化物品价值缓存（异步，在后台分帧处理避免卡顿）
        /// </summary>
        private void InitializeItemValueCacheAsync()
        {
            if (_itemValueCacheInitialized || _itemValueCacheInitializing)
            {
                return;
            }
            _itemValueCacheInitializing = true;
            StartCoroutine(InitializeItemValueCacheCoroutine());
        }

        /// <summary>
        /// 物品价值缓存初始化协程 - 分帧处理避免卡顿
        /// </summary>
        private IEnumerator InitializeItemValueCacheCoroutine()
        {
            if (_itemValueCache == null)
            {
                _itemValueCache = new Dictionary<int, ItemValueCacheEntry>();
            }
            else
            {
                _itemValueCache.Clear();
            }

            DevLog("[BossRush] 开始初始化物品价值缓存...");

            // 收集所有候选物品ID
            HashSet<int> idSet = new HashSet<int>();
            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                if (tagsData != null && tagsData.AllTags != null)
                {
                    List<Duckov.Utilities.Tag> baseExclude = new List<Duckov.Utilities.Tag>();
                    if (tagsData.DestroyOnLootBox != null) baseExclude.Add(tagsData.DestroyOnLootBox);
                    if (tagsData.DontDropOnDeadInSlot != null) baseExclude.Add(tagsData.DontDropOnDeadInSlot);
                    if (tagsData.LockInDemoTag != null) baseExclude.Add(tagsData.LockInDemoTag);

                    foreach (Duckov.Utilities.Tag tag in tagsData.AllTags)
                    {
                        if (tag == null || baseExclude.Contains(tag)) continue;

                        ItemFilter filter = default(ItemFilter);
                        filter.requireTags = new Duckov.Utilities.Tag[] { tag };
                        filter.excludeTags = baseExclude.ToArray();
                        filter.minQuality = 1;
                        filter.maxQuality = 8;

                        int[] ids = ItemAssetsCollection.Search(filter);
                        if (ids != null)
                        {
                            foreach (int id in ids)
                            {
                                if (id > 0 && !ManualLootBlacklist.Contains(id))
                                {
                                    idSet.Add(id);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 收集候选物品ID失败: " + e.Message);
            }

            DevLog("[BossRush] 物品价值缓存：共需处理 " + idSet.Count + " 个物品");

            // 分帧处理：每帧处理一定数量的物品
            const int itemsPerFrame = 20;
            int processedCount = 0;
            List<int> idList = new List<int>(idSet);

            for (int i = 0; i < idList.Count; i++)
            {
                int itemId = idList[i];
                try
                {
                    Item temp = ItemAssetsCollection.InstantiateSync(itemId);
                    if (temp != null)
                    {
                        ItemValueCacheEntry entry = new ItemValueCacheEntry();
                        try { entry.value = temp.Value; } catch { entry.value = 0; }
                        try { entry.quality = temp.Quality; } catch { entry.quality = -1; }
                        _itemValueCache[itemId] = entry;
                        UnityEngine.Object.Destroy(temp.gameObject);
                    }
                }
                catch { }

                processedCount++;

                // 每处理一定数量的物品，等待下一帧
                if (processedCount >= itemsPerFrame)
                {
                    processedCount = 0;
                    yield return null;
                }
            }

            _itemValueCacheInitialized = true;
            _itemValueCacheInitializing = false;
            DevLog("[BossRush] 物品价值缓存初始化完成，共缓存 " + _itemValueCache.Count + " 个物品");
        }

        /// <summary>
        /// 从缓存获取物品价值信息
        /// </summary>
        private bool TryGetCachedItemValue(int itemId, out int value, out int quality)
        {
            ItemValueCacheEntry entry;
            if (_itemValueCache != null && _itemValueCache.TryGetValue(itemId, out entry))
            {
                value = entry.value;
                quality = entry.quality;
                return true;
            }
            value = 0;
            quality = -1;
            return false;
        }

        /// <summary>
        /// 无间炼狱单波完成：掉落现金、更新显示并准备下一波（LootAndRewards 备份实现）
        /// </summary>
        private async void OnInfiniteHellWaveCompleted_LootAndRewards()
        {
            try
            {
                // 增加波次
                infiniteHellWaveIndex++;

                long cashThisWave = infiniteHellWaveCashThisWave;
                infiniteHellWaveCashThisWave = 0L;

                // 在路牌位置掉落 3 叠现金
                try
                {
                    if (cashThisWave > 0)
                    {
                        long perStack = cashThisWave / 3L;
                        if (perStack < 1L) perStack = cashThisWave; // 波次奖励太低时全部塞一叠

                        // 从配置系统获取当前地图的默认位置作为兜底
                        Vector3 basePos = GetCurrentSceneDefaultPosition();
                        try
                        {
                            if (_bossRushSignGameObject != null)
                            {
                                basePos = _bossRushSignGameObject.transform.position + Vector3.up * 0.2f;
                            }
                        }
                        catch {}

                        int stackCount = (cashThisWave >= perStack * 3L) ? 3 : 1;
                        for (int i = 0; i < stackCount; i++)
                        {
                            long stackValue = (stackCount == 1) ? cashThisWave : perStack;
                            if (stackValue <= 0) break;

                            Item cashItem = null;
                            try
                            {
                                cashItem = ItemAssetsCollection.InstantiateSync(EconomyManager.CashItemID);
                            }
                            catch {}

                            if (cashItem == null)
                            {
                                break;
                            }

                            try
                            {
                                cashItem.StackCount = (int)Mathf.Clamp(stackValue, 1, int.MaxValue);
                            }
                            catch {}

                            Vector3 offset = (stackCount == 1) ? Vector3.zero : (new Vector3(i - 1, 0f, 0f) * 0.3f);
                            Vector3 dropPos = basePos + offset;

                            try
                            {
                                cashItem.Drop(dropPos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                            }
                            catch {}
                        }
                    }
                }
                catch {}

                // 只在无间炼狱下显示现金池文案
                try
                {
                    if (bossRushSignInteract != null && infiniteHellCashPool > 0)
                    {
                        bossRushSignInteract.UpdateInfiniteHellCashDisplay(infiniteHellCashPool);
                    }
                }
                catch {}

                // 玩家头顶气泡提示现金池累积
                try
                {
                    CharacterMainControl player = null;
                    try { player = CharacterMainControl.Main; } catch {}
                    if (player == null && playerCharacter != null)
                    {
                        try { player = playerCharacter as CharacterMainControl; } catch {}
                    }

                    if (player != null)
                    {
                        string bubble = "现金池已累计：<color=red>" + infiniteHellCashPool.ToString() + "</color>";
                        // 使用文档示例中的最简调用形式，避免可选参数带来的兼容性问题
                        await DialogueBubblesManager.Show(bubble, player.transform);
                    }
                }
                catch {}

                // 每 5 波奖励一次 1253 号物品（在路牌位置掉落）
                try
                {
                    if (infiniteHellWaveIndex > 0 && infiniteHellWaveIndex % 5 == 0)
                    {
                        int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                        if (rewardTypeId <= 0)
                        {
                            rewardTypeId = 1253;
                        }

                        Item reward5 = null;
                        try
                        {
                            reward5 = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                        }
                        catch {}

                        if (reward5 != null)
                        {
                            try
                            {
                                // 从配置系统获取当前地图的默认位置作为兜底
                                Vector3 basePos = GetCurrentSceneDefaultPosition();
                                try
                                {
                                    if (_bossRushSignGameObject != null)
                                    {
                                        basePos = _bossRushSignGameObject.transform.position + Vector3.up * 0.3f;
                                    }
                                }
                                catch {}

                                try
                                {
                                    reward5.Drop(basePos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                                }
                                catch {}
                            }
                            catch {}
                        }
                    }
                }
                catch {}

                // 100 波一次性奖励（在路牌位置掉落）
                try
                {
                    if (!infiniteHell100WaveRewardGiven && infiniteHellWaveIndex >= 100)
                    {
                        infiniteHell100WaveRewardGiven = true;

                        Item special = null;
                        try
                        {
                            special = ItemAssetsCollection.InstantiateSync(1254);
                        }
                        catch {}

                        if (special != null)
                        {
                            try
                            {
                                // 从配置系统获取当前地图的默认位置作为兜底
                                Vector3 basePos = GetCurrentSceneDefaultPosition();
                                try
                                {
                                    if (_bossRushSignGameObject != null)
                                    {
                                        basePos = _bossRushSignGameObject.transform.position + Vector3.up * 0.3f;
                                    }
                                }
                                catch {}

                                try
                                {
                                    special.Drop(basePos, true, UnityEngine.Random.insideUnitSphere.normalized, 45f);
                                }
                                catch {}
                            }
                            catch {}
                        }
                    }
                }
                catch {}

                // 准备下一波（无终点：不调用 OnAllEnemiesDefeated）
                if (config != null && config.useInteractBetweenWaves)
                {
                    // 无间炼狱模式下，主路牌交互始终用于显示现金池，
                    // 下一波由单独的 BossRushNextWaveInteractable 处理，这里不切换路牌主文案，
                    // 只确保路牌 group 中存在一个“下一波”子选项（不添加清空箱子选项）。
                    try
                    {
                        if (bossRushSignInteract != null)
                        {
                            bossRushSignInteract.AddNextWaveOnly();
                        }
                    }
                    catch {}
                }
                else
                {
                    StartNextWaveCountdown();
                }
            }
            catch {}
        }

        private int GetRandomInfiniteHellHighQualityRewardTypeID()
        {
            if (infiniteHellHighQualityItemPoolInitialized && infiniteHellHighQualityItemPool != null && infiniteHellHighQualityItemPool.Count > 0)
            {
                int cachedIndex = UnityEngine.Random.Range(0, infiniteHellHighQualityItemPool.Count);
                return infiniteHellHighQualityItemPool[cachedIndex];
            }

            infiniteHellHighQualityItemPoolInitialized = true;

            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData == null || tagsData.AllTags == null || tagsData.AllTags.Count == 0)
            {
                return -1;
            }

            List<Duckov.Utilities.Tag> baseExclude = new List<Duckov.Utilities.Tag>();
            if (tagsData.DestroyOnLootBox != null) baseExclude.Add(tagsData.DestroyOnLootBox);
            if (tagsData.DontDropOnDeadInSlot != null) baseExclude.Add(tagsData.DontDropOnDeadInSlot);
            if (tagsData.LockInDemoTag != null) baseExclude.Add(tagsData.LockInDemoTag);

            List<Duckov.Utilities.Tag> includeTags = new List<Duckov.Utilities.Tag>();
            for (int i = 0; i < tagsData.AllTags.Count; i++)
            {
                Duckov.Utilities.Tag tag = tagsData.AllTags[i];
                if (tag == null)
                {
                    continue;
                }
                if (baseExclude.Contains(tag))
                {
                    continue;
                }
                includeTags.Add(tag);
            }

            HashSet<int> idSet = new HashSet<int>();

            for (int i = 0; i < includeTags.Count; i++)
            {
                Duckov.Utilities.Tag requireTag = includeTags[i];
                if (requireTag == null)
                {
                    continue;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[]
                {
                    requireTag
                };
                filter.excludeTags = baseExclude.ToArray();
                filter.minQuality = 1;
                filter.maxQuality = 8;

                int[] ids = ItemAssetsCollection.Search(filter);
                if (ids == null)
                {
                    continue;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    int id = ids[j];
                    if (id > 0)
                    {
                        if (ManualLootBlacklist.Contains(id))
                        {
                            continue;
                        }
                        idSet.Add(id);
                    }
                }
            }

            if (idSet.Count == 0)
            {
                return -1;
            }

            const int priceThreshold = 10000;
            List<int> preferred = new List<int>();
            List<int> fallbackHighQuality = new List<int>();

            foreach (int candidateId in idSet)
            {
                int v = 0;
                int quality = -1;

                try
                {
                    Item temp = ItemAssetsCollection.InstantiateSync(candidateId);
                    if (temp != null)
                    {
                        try
                        {
                            v = temp.Value;
                        }
                        catch
                        {
                            v = 0;
                        }

                        try
                        {
                            quality = temp.Quality;
                        }
                        catch
                        {
                            quality = -1;
                        }

                        UnityEngine.Object.Destroy(temp.gameObject);
                    }
                }
                catch
                {
                    v = 0;
                }

                if (quality >= 5)
                {
                    if (v >= priceThreshold)
                    {
                        preferred.Add(candidateId);
                    }
                    else
                    {
                        fallbackHighQuality.Add(candidateId);
                    }
                }
            }

            List<int> pool = null;

            if (preferred.Count > 0)
            {
                pool = preferred;
            }
            else if (fallbackHighQuality.Count > 0)
            {
                pool = fallbackHighQuality;
            }

            if (pool == null || pool.Count == 0)
            {
                return -1;
            }

            infiniteHellHighQualityItemPool = pool;

            int index = UnityEngine.Random.Range(0, infiniteHellHighQualityItemPool.Count);
            return infiniteHellHighQualityItemPool[index];
        }

        /// <summary>
        /// 所有敌人击败完成（LootAndRewards 备份实现）
        /// </summary>
        private async void OnAllEnemiesDefeated_LootAndRewards()
        {
            // 防止在非竞技场场景（例如中途撤离回家后）误触发通关流程
            try
            {
                string sceneName = SceneManager.GetActiveScene().name;
                // 使用配置系统判断是否在有效的 BossRush 竞技场场景
                if (!IsCurrentSceneValidBossRushArena())
                {
                    DevLog("[BossRush] OnAllEnemiesDefeated 被调用但当前不在竞技场场景(" + sceneName + ")，已重置状态不播报通关");
                    SetBossRushRuntimeActive(false);
                    bossRushArenaActive = false;
                    Health.OnDead -= OnEnemyDiedWithDamageInfo;
                    return;
                }
            }
            catch {}

            SetBossRushRuntimeActive(false);
            bossRushArenaActive = false;

            // 取消敌人死亡监听
            Health.OnDead -= OnEnemyDiedWithDamageInfo;
            
            // 通知快递员 BossRush 通关
            NotifyCourierBossRushCompleted();

            ShowMessage(L10n.T("所有敌人已击败！你赢了！", "All enemies defeated! You win!"));
            DevLog("[BossRush] BossRush挑战完成！");

            // 将路牌切换到凯旋状态，仅展示最终彩色标题
            try
            {
                if (bossRushSignInteract != null)
                {
                    bossRushSignInteract.SetVictoryMode();
                }
            }
            catch {}

            // 根据本次难度在“下一波”交互点附近生成通关奖励箱，并播放彩虹横幅
            try
            {
                int rewardHighCount = (bossesPerWave <= 1) ? 3 : 10;
                string diffName = (bossesPerWave <= 1) ? L10n.T("弹指可灭", "Easy Mode") : L10n.T("有点意思", "Hard Mode");

                string banner = L10n.T(
                    "<color=#FF0000>恭</color><color=#FF7F00>喜</color><color=#FFFF00>通</color><color=#00FF00>关</color> " +
                    "<color=#00FFFF>\u300c" + diffName + "\u300d</color>！ " +
                    "请到场地中心领取奖励\\o/",
                    "<color=#FF0000>C</color><color=#FF7F00>o</color><color=#FFFF00>n</color><color=#00FF00>g</color><color=#00FFFF>r</color><color=#0000FF>a</color><color=#8B00FF>t</color><color=#FF0000>s</color>! " +
                    "<color=#00FFFF>\u300c" + diffName + "\u300d</color> " +
                    "Claim your rewards at the center! \\o/"
                );

                ShowBigBanner(banner);
                SpawnDifficultyRewardLootbox_LootAndRewards(rewardHighCount);
            }
            catch {}

            // 等待2秒后显示气泡对话
            await UniTask.Delay(2000);

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    // 显示气泡对话（带炫彩文字），支持中英文
                    string bubbleText = L10n.T(
                        "<color=#FF0000>你</color><color=#FF7F00>简</color><color=#FFFF00>直</color><color=#00FF00>是</color><color=#0000FF>鸭</color><color=#4B0082>鸭</color><color=#9400D3>星</color><color=#FF0000>球</color><color=#FF7F00>里</color><color=#FFFF00>最</color><color=#00FF00>强</color><color=#0000FF>的</color><color=#4B0082>鸭</color><color=#9400D3>！</color><color=#FF0000>！</color><color=#FF7F00>！</color>",
                        "<color=#FF0000>You</color> <color=#FF7F00>are</color> <color=#FFFF00>the</color> <color=#00FF00>strongest</color> <color=#0000FF>duck</color><color=#4B0082>!</color><color=#9400D3>!</color><color=#FF0000>!</color>"
                    );
                    await DialogueBubblesManager.Show(
                        bubbleText,
                        player.transform,
                        1f,
                        false,
                        true,
                        -1,
                        5f
                    );
                }

                // 生成返回出生点的交互点
                TryCreateReturnInteractable();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 显示完成对话失败: " + e.Message);
            }
        }

        private void SpawnDifficultyRewardLootbox_LootAndRewards(int highQualityCount)
        {
            try
            {
                if (highQualityCount <= 0)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch
                {
                }

                if (main == null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch
                    {
                    }
                }

                Vector3 pos = demoChallengeStartPosition;
                if (pos == Vector3.zero)
                {
                    if (main != null)
                    {
                        pos = main.transform.position;
                    }
                    else
                    {
                        // 从配置系统获取当前地图的默认位置
                        pos = GetCurrentSceneDefaultPosition();
                    }
                }

                // 使用射线向下投射，优先只打地面层，避免命中玩家碰撞体；如果投射失败则保持轻微抬高
                try
                {
                    Vector3 rayStart = pos + Vector3.up * 10f;
                    UnityEngine.RaycastHit hit = default(UnityEngine.RaycastHit);
                    // 优先只打地面层，避免玩家或其他碰撞体干扰
                    bool hitGround = false;
                    try
                    {
                        var groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                        hitGround = UnityEngine.Physics.Raycast(rayStart, Vector3.down, out hit, 50f, groundMask, UnityEngine.QueryTriggerInteraction.Ignore);
                    }
                    catch
                    {
                        hitGround = false;
                    }

                    if (!hitGround)
                    {
                        // 退回到旧逻辑：打所有非触发层，保证兼容性
                        if (UnityEngine.Physics.Raycast(rayStart, Vector3.down, out hit, 50f, ~0, UnityEngine.QueryTriggerInteraction.Ignore))
                        {
                            hitGround = true;
                        }
                    }

                    if (hitGround)
                    {
                        pos = hit.point + Vector3.up * 0.05f;
                    }
                    else
                    {
                        pos += Vector3.up * 0.1f;
                    }
                }
                catch
                {
                    pos += Vector3.up * 0.1f;
                }

                InteractableLootbox prefab = GetDifficultyRewardLootBoxTemplate();
                if (prefab == null)
                {
                    DevLog("[BossRush] SpawnDifficultyRewardLootbox: 未找到 Lootbox 模板，无法生成通关奖励箱");
                    return;
                }

                try
                {
                    DevLog("[BossRush] SpawnDifficultyRewardLootbox: highQualityCount=" + highQualityCount +
                           ", demoStartPos=" + demoChallengeStartPosition +
                           ", playerPos=" + (main != null ? main.transform.position.ToString() : "<null>") +
                           ", finalPos=" + pos +
                           ", prefabName=" + prefab.name);
                }
                catch {}

                InteractableLootbox lootbox = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                lootbox.needInspect = true;

                try
                {
                    string boxName = lootbox != null && lootbox.gameObject != null ? lootbox.gameObject.name : "<null>";
                    DevLog("[BossRush] SpawnDifficultyRewardLootbox: 实例化 Lootbox 成功, instanceName=" + boxName +
                           ", type=" + (lootbox != null ? lootbox.GetType().FullName : "<null>") +
                           ", position=" + pos);
                }
                catch {}

                // 为通关奖励箱创建独立本地 Inventory，避免与其他 Lootbox 通过位置哈希共享同一个库存
                try
                {
                    System.Type lootboxType = typeof(InteractableLootbox);
                    System.Reflection.MethodInfo createLocalInventoryMethod =
                        lootboxType.GetMethod("CreateLocalInventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (createLocalInventoryMethod != null)
                    {
                        createLocalInventoryMethod.Invoke(lootbox, null);
                    }
                }
                catch {}

                // 根据配置决定是否让通关奖励箱作为子弹掩体
                try
                {
                    ApplyLootBoxCoverSetting(lootbox, true);
                }
                catch {}

                // 为奖励箱添加伪搬运交互（与 Boss 奖励箱一致）
                try
                {
                }
                catch {}

                try
                {
                    MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex);
                }
                catch {}

                Duckov.Utilities.LootBoxLoader loader = lootbox.GetComponent<Duckov.Utilities.LootBoxLoader>();
                if (loader != null)
                {
                    try
                    {
                        System.Type loaderType = typeof(Duckov.Utilities.LootBoxLoader);

                        // 强制通关奖励箱固定高品质物品数量
                        int minCount = Math.Max(1, highQualityCount);
                        int maxCount = minCount;

                        System.Reflection.FieldInfo randomCountField = loaderType.GetField("randomCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (randomCountField != null)
                        {
                            Vector2Int rc = new Vector2Int(minCount, maxCount);
                            randomCountField.SetValue(loader, rc);
                        }

                        // 只保留高品质（5 和 6）的品质权重，保证品质>=5
                        System.Reflection.FieldInfo qualitiesField = loaderType.GetField("qualities", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (qualitiesField != null)
                        {
                            Duckov.Utilities.RandomContainer<int> qualities = qualitiesField.GetValue(loader) as Duckov.Utilities.RandomContainer<int>;
                            if (qualities != null)
                            {
                                qualities.entries.Clear();
                                qualities.AddEntry(5, 1f);
                                qualities.AddEntry(6, 1f);
                                qualities.RefreshPercent();
                            }
                        }

                        try
                        {
                            System.Reflection.FieldInfo tagsField = loaderType.GetField("tags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            System.Reflection.FieldInfo excludeTagsField = loaderType.GetField("excludeTags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            System.Reflection.FieldInfo randomPoolField = loaderType.GetField("randomPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            System.Type loaderEntryType = loaderType.GetNestedType("Entry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

                            if (tagsField != null && excludeTagsField != null && randomPoolField != null && loaderEntryType != null)
                            {
                                Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag> tagsContainer = tagsField.GetValue(loader) as Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag>;
                                if (tagsContainer != null)
                                {
                                    tagsContainer.entries.Clear();

                                    Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                                    if (tagsData != null && tagsData.AllTags != null)
                                    {
                                        var allTags = tagsData.AllTags;
                                        for (int i = 0; i < allTags.Count; i++)
                                        {
                                            Duckov.Utilities.Tag t = allTags[i];
                                            if (t == null)
                                            {
                                                continue;
                                            }
                                            if (t == tagsData.Character || t == tagsData.DestroyOnLootBox || t == tagsData.DontDropOnDeadInSlot || t == tagsData.LockInDemoTag)
                                            {
                                                continue;
                                            }
                                            tagsContainer.AddEntry(t, 1f);
                                        }
                                    }

                                    tagsContainer.RefreshPercent();
                                }

                                List<Duckov.Utilities.Tag> excludeList = excludeTagsField.GetValue(loader) as List<Duckov.Utilities.Tag>;
                                if (excludeList == null)
                                {
                                    excludeList = new List<Duckov.Utilities.Tag>();
                                    excludeTagsField.SetValue(loader, excludeList);
                                }

                                Duckov.Utilities.GameplayDataSettings.TagsData tagsData2 = Duckov.Utilities.GameplayDataSettings.Tags;
                                if (tagsData2 != null)
                                {
                                    if (tagsData2.DestroyOnLootBox != null && !excludeList.Contains(tagsData2.DestroyOnLootBox)) excludeList.Add(tagsData2.DestroyOnLootBox);
                                    if (tagsData2.DontDropOnDeadInSlot != null && !excludeList.Contains(tagsData2.DontDropOnDeadInSlot)) excludeList.Add(tagsData2.DontDropOnDeadInSlot);
                                    if (tagsData2.LockInDemoTag != null && !excludeList.Contains(tagsData2.LockInDemoTag)) excludeList.Add(tagsData2.LockInDemoTag);
                                }

                                object randomPoolObj = randomPoolField.GetValue(loader);
                                if (randomPoolObj != null)
                                {
                                    System.Type randomPoolType = randomPoolObj.GetType();
                                    System.Reflection.FieldInfo entriesField = randomPoolType.GetField("entries", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    object entriesObj = (entriesField != null) ? entriesField.GetValue(randomPoolObj) : null;
                                    System.Collections.IList entriesList = entriesObj as System.Collections.IList;
                                    System.Type randomContainerEntryType = randomPoolType.GetNestedType("Entry", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                                    if (randomContainerEntryType != null && randomContainerEntryType.ContainsGenericParameters && randomPoolType.IsGenericType)
                                    {
                                        System.Type[] genericArgs = randomPoolType.GetGenericArguments();
                                        if (genericArgs != null && genericArgs.Length > 0)
                                        {
                                            randomContainerEntryType = randomContainerEntryType.MakeGenericType(genericArgs);
                                        }
                                    }

                                    if (entriesList != null && randomContainerEntryType != null)
                                    {
                                        entriesList.Clear();

                                        System.Reflection.FieldInfo lootEntryItemIdField = loaderEntryType.GetField("itemTypeID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                        System.Reflection.FieldInfo rcValueField = randomContainerEntryType.GetField("value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                        System.Reflection.FieldInfo rcWeightField = randomContainerEntryType.GetField("weight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                                        if (lootEntryItemIdField != null && rcValueField != null && rcWeightField != null)
                                        {
                                            HashSet<int> idSet = new HashSet<int>();

                                            Duckov.Utilities.GameplayDataSettings.TagsData tagsData3 = Duckov.Utilities.GameplayDataSettings.Tags;
                                            if (tagsData3 != null)
                                            {
                                                List<Duckov.Utilities.Tag> baseExclude = new List<Duckov.Utilities.Tag>();
                                                if (tagsData3.DestroyOnLootBox != null) baseExclude.Add(tagsData3.DestroyOnLootBox);
                                                if (tagsData3.DontDropOnDeadInSlot != null) baseExclude.Add(tagsData3.DontDropOnDeadInSlot);
                                                if (tagsData3.LockInDemoTag != null) baseExclude.Add(tagsData3.LockInDemoTag);

                                                List<Duckov.Utilities.Tag> includeTags = new List<Duckov.Utilities.Tag>();
                                                if (tagsData3.AllTags != null)
                                                {
                                                    foreach (Duckov.Utilities.Tag tag in tagsData3.AllTags)
                                                    {
                                                        if (tag == null) continue;
                                                        if (baseExclude.Contains(tag)) continue;
                                                        includeTags.Add(tag);
                                                    }
                                                }

                                                for (int i = 0; i < includeTags.Count; i++)
                                                {
                                                    Duckov.Utilities.Tag requireTag = includeTags[i];
                                                    if (requireTag == null)
                                                    {
                                                        continue;
                                                    }

                                                    ItemFilter filter = default(ItemFilter);
                                                    filter.requireTags = new Duckov.Utilities.Tag[] { requireTag };
                                                    filter.excludeTags = baseExclude.ToArray();
                                                    // 与 Boss 奖励候选池保持一致：品质范围 1~6，后续用实际品质/价格做最终清理
                                                    filter.minQuality = 1;
                                                    filter.maxQuality = 8;

                                                    int[] ids = ItemAssetsCollection.Search(filter);
                                                    if (ids == null)
                                                    {
                                                        continue;
                                                    }

                                                    for (int j = 0; j < ids.Length; j++)
                                                    {
                                                        int id = ids[j];
                                                        if (id > 0)
                                                        {
                                                            if (ManualLootBlacklist.Contains(id))
                                                            {
                                                                continue;
                                                            }
                                                            idSet.Add(id);
                                                        }
                                                    }
                                                }
                                            }

                                            // 先统计通关奖励候选总数量
                                            DevLog("[BossRush] 通关奖励候选物品数量=" + idSet.Count);

                                            // 基于真实物品数据评估品质和价格，收集 Quality>=5 且 Value>=2000 的高价值候选
                                            const int difficultyHighPriceThreshold = 2000;
                                            List<int> highValueCandidates = new List<int>();

                                            try
                                            {
                                                foreach (int candidateId in idSet)
                                                {
                                                    int v = 0;
                                                    int quality = -1;
                                                    string name = "<unknown>";

                                                    try
                                                    {
                                                        Item temp = ItemAssetsCollection.InstantiateSync(candidateId);
                                                        if (temp != null)
                                                        {
                                                            try
                                                            {
                                                                v = temp.Value;
                                                            }
                                                            catch
                                                            {
                                                                v = 0;
                                                            }

                                                            try
                                                            {
                                                                quality = temp.Quality;
                                                            }
                                                            catch
                                                            {
                                                                quality = -1;
                                                            }

                                                            UnityEngine.Object.Destroy(temp.gameObject);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        v = 0;
                                                    }

                                                    try
                                                    {
                                                        var meta = ItemAssetsCollection.GetMetaData(candidateId);
                                                        if (meta.id > 0)
                                                        {
                                                            name = meta.DisplayName;
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        name = "<meta-failed>";
                                                    }

                                                    DevLog("[BossRush] 通关奖励候选物品评估: typeID=" + candidateId + ", 名称=" + name + ", Quality=" + quality + ", Value=" + v);

                                                    if (quality >= 5 && v >= difficultyHighPriceThreshold)
                                                    {
                                                        highValueCandidates.Add(candidateId);
                                                    }
                                                }
                                            }
                                            catch (Exception evalEx)
                                            {
                                                DevLog("[BossRush] 评估通关奖励候选物品失败: " + evalEx.Message);
                                            }

                                            IEnumerable<int> poolSource = idSet;
                                            if (highValueCandidates.Count > 0)
                                            {
                                                poolSource = highValueCandidates;
                                                DevLog("[BossRush] 通关奖励高品质高价候选物品数量=" + highValueCandidates.Count + " (Quality>=5, Value>=" + difficultyHighPriceThreshold + ")");
                                            }

                                            // 使用最终确定的候选集合构建 randomPool；如果没有高价候选，则退回使用完整 idSet
                                            foreach (int id2 in poolSource)
                                            {
                                                object lootEntry = Activator.CreateInstance(loaderEntryType);
                                                lootEntryItemIdField.SetValue(lootEntry, id2);

                                                object rcEntry = Activator.CreateInstance(randomContainerEntryType);
                                                rcValueField.SetValue(rcEntry, lootEntry);
                                                rcWeightField.SetValue(rcEntry, 1f);

                                                entriesList.Add(rcEntry);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch {}

                        System.Reflection.FieldInfo fixedItemsField = loaderType.GetField("fixedItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        System.Reflection.FieldInfo fixedChanceField = loaderType.GetField("fixedItemSpawnChance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (fixedItemsField != null)
                        {
                            System.Collections.Generic.List<int> fixedItems = fixedItemsField.GetValue(loader) as System.Collections.Generic.List<int>;
                            if (fixedItems == null)
                            {
                                fixedItems = new System.Collections.Generic.List<int>();
                                fixedItemsField.SetValue(loader, fixedItems);
                            }
                            fixedItems.Clear();
                        }
                        if (fixedChanceField != null)
                        {
                            fixedChanceField.SetValue(loader, 0f);
                        }

                        loader.GenrateCashChance = 0f;
                        loader.maxRandomCash = 0;

                        loader.randomFromPool = true;
                        loader.ignoreLevelConfig = true;
                        loader.CalculateChances();
                        loader.StartSetup();
                    }
                    catch (Exception cfgEx)
                    {
                        DevLog("[BossRush] 配置通关奖励 LootBoxLoader 失败: " + cfgEx.Message);
                    }
                }
                else
                {
                    DevLog("[BossRush] 通关奖励盒子上没有 LootBoxLoader 组件，将使用 Prefab 默认内容");
                }

                Inventory inventory = lootbox.Inventory;
                if (inventory != null)
                {
                    inventory.NeedInspection = lootbox.needInspect;
                }

                DevLog("[BossRush] 已为难度奖励生成专用奖励盒子，高品质物品数量=" + highQualityCount);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] SpawnDifficultyRewardLootbox 错误: " + e.Message);
            }
        }

        /// <summary>
        /// 玩家死亡保护（BossRush期间）- 参考keep_items_on_death实现（LootAndRewards 备份实现）
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush_LootAndRewards(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // 检查是否是BossRush期间的玩家死亡
                if (!IsActive) return;

                var character = deadHealth.GetComponent<CharacterMainControl>();
                if (character == null) return;

                // 检查是否是玩家
                bool isPlayer = false;
                try
                {
                    isPlayer = CharacterMainControlExtensions.IsMainCharacter(character);
                }
                catch
                {
                    isPlayer = (character == CharacterMainControl.Main);
                }

                if (!isPlayer) return;

                DevLog("[BossRush] 检测到玩家死亡，阻止物品掉落");

                // 结束BossRush
                SetBossRushRuntimeActive(false);
                bossRushArenaActive = false;
                currentBoss = null;
                try
                {
                    if (ammoShop != null)
                    {
                        try
                        {
                            if (ammoShop.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(ammoShop.gameObject);
                            }
                        }
                        catch {}
                        ammoShop = null;
                    }
                }
                catch {}

                // 取消敌人死亡监听
                Health.OnDead -= OnEnemyDiedWithDamageInfo;

                // 如果是 Mode D 模式，结束 Mode D
                if (modeDActive)
                {
                    EndModeD();
                }

                ShowMessage(L10n.T("BossRush挑战失败！", "BossRush challenge failed!"));
            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnPlayerDeathInBossRush错误: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 在角色真正生成掉落物之前拦截玩家掉落逻辑（LootAndRewards 备份实现）
        /// （事件来源：CharacterMainControl.BeforeCharacterSpawnLootOnDead）
        /// </summary>
        private void OnMainCharacterBeforeSpawnLoot_LootAndRewards(DamageInfo dmgInfo)
        {
            try
            {
                // 仅在BossRush激活期间生效
                if (!IsActive)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null)
                {
                    return;
                }

                // 确保这是玩家角色
                bool isPlayer = false;
                try
                {
                    isPlayer = CharacterMainControlExtensions.IsMainCharacter(main);
                }
                catch
                {
                    isPlayer = true; // Main一般就是玩家
                }

                if (!isPlayer)
                {
                    return;
                }

                // 关键点：在真正SpawnLoot前把掉落开关关掉
                main.dropBoxOnDead = false;
                DevLog("[BossRush] OnMainCharacterBeforeSpawnLoot: 已关闭玩家掉落");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnMainCharacterBeforeSpawnLoot 错误: " + e.Message);
            }
        }

        /// <summary>
        /// 在Boss真正生成掉落物之前拦截并随机化掉落（LootAndRewards 备份实现）
        /// （事件来源：CharacterMainControl.BeforeCharacterSpawnLootOnDead）
        /// </summary>
        private void OnBossBeforeSpawnLoot_LootAndRewards(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            try
            {
                // 仅在BossRush激活时生效
                if (!IsActive || bossMain == null)
                {
                    return;
                }

                // 只处理由 BossRush 生成且被追踪的 Boss
                if (!bossSpawnTimes.ContainsKey(bossMain))
                {
                    return;
                }

                // 双保险：基于掉落事件再做一次死亡判定（HandleBossDeath 内部会去重）
                HandleBossDeath(bossMain, dmgInfo);

                // 注意：龙裔遗族特殊掉落已移至 RandomizeBossLoot_LootAndRewards 方法中
                // 在掉落箱创建并填充物品后，直接添加到掉落箱的Inventory中

                // 无间炼狱：完全禁止 lootbox 掉落，改为现金池逻辑
                if (infiniteHellMode)
                {
                    try
                    {
                        bossMain.dropBoxOnDead = false;
                    }
                    catch {}

                    bossSpawnTimes.Remove(bossMain);
                    bossOriginalLootCounts.Remove(bossMain);
                    return;
                }

                if (config == null || !config.enableRandomBossLoot)
                {
                    return; // 未启用随机掉落，保持原版掉落
                }

                // 计算击杀耗时（仅用于日志）
                float spawnTime = bossSpawnTimes[bossMain];
                float killDuration = Time.time - spawnTime;

                // 计算 Boss 最大生命（用于格子数量和高品质数量）
                float maxHealth = 100f;
                try
                {
                    if (bossMain.Health != null)
                    {
                        maxHealth = bossMain.Health.MaxHealth;
                    }
                }
                catch {}

                // 基础掉落格子数量：按 Boss 池的基础血量范围，将当前 Boss 血量线性映射到 [5,15]
                int baseCount = 10;
                float refMin = minBossBaseHealth;
                float refMax = maxBossBaseHealth;
                if (refMax > refMin && refMin > 0f)
                {
                    float t = Mathf.InverseLerp(refMin, refMax, maxHealth);
                    float mapped = Mathf.Lerp(7f, 15f, t);
                    baseCount = Mathf.RoundToInt(mapped);
                }
                else
                {
                    // 如果未能正确初始化 Boss 池血量范围，则退化为按自身血量近似映射
                    float mapped = 7f + (maxHealth / 100f);
                    baseCount = Mathf.RoundToInt(mapped);
                }
                baseCount = Mathf.Clamp(baseCount, 7, 15);

                // 高品质最高数量：MaxHealth/100，限制在[1,10]
                int maxHighByHealth = Mathf.Clamp(Mathf.FloorToInt(maxHealth / 100f), 1, 10);
                int highCount = maxHighByHealth;

                // 高品质数量不能超过总格子数
                if (highCount > baseCount)
                {
                    highCount = baseCount;
                }

                // 每100血量增加0.5%的高品质概率（相较旧版本略微削弱高品质加成）
                float highChanceBonusByHealth = (maxHealth / 100f) * 0.005f;

                float referenceWindow = 60f * (1f + maxHealth / 500f);
                if (referenceWindow > 0f)
                {
                    float timeRatio = 1f - (killDuration / referenceWindow);
                    timeRatio = Mathf.Clamp01(timeRatio);
                    float timeBonus = timeRatio * 0.05f;
                    highChanceBonusByHealth += timeBonus;
                }

                DevLog("[BossRush] Boss击杀耗时: " + killDuration.ToString("F1")
                    + "秒, MaxHP=" + maxHealth
                    + ", 基础掉落数量=" + baseCount
                    + ", 高品质最高数量=" + highCount);

                // 随机生成物品并填充到Boss掉落源（CharacterItem.Inventory），由原版逻辑创建LootBox
                RandomizeBossLoot_LootAndRewards(bossMain, baseCount, highCount, killDuration, highChanceBonusByHealth);

                // 清理已处理的Boss记录
                bossSpawnTimes.Remove(bossMain);
                bossOriginalLootCounts.Remove(bossMain);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnBossBeforeSpawnLoot 错误: " + e.Message);
            }
        }

        private void RandomizeBossLoot_LootAndRewards(CharacterMainControl bossMain, int totalCount, int highQualityCount, float killDuration, float highChanceBonusByHealth)
        {
            try
            {
                if (bossMain == null)
                {
                    DevLog("[BossRush] BossMain 无效，无法生成奖励盒子");
                    return;
                }

                if (totalCount < 1)
                {
                    totalCount = 1;
                }
                if (highQualityCount < 0)
                {
                    highQualityCount = 0;
                }
                if (highQualityCount > totalCount)
                {
                    highQualityCount = totalCount;
                }
                float highChance = 0f;
                if (totalCount > 0)
                {
                    highChance = (float)highQualityCount / (float)totalCount;
                }
                if (highChanceBonusByHealth > 0f)
                {
                    highChance += highChanceBonusByHealth;
                }
                highChance = Mathf.Clamp01(highChance);

                bool useBossDeadBoxPrefab = false;
                InteractableLootbox prefab = null;

                // 当“掉落箱作为掩体（挡子弹）”选项关闭时，优先尝试使用敌人死亡掉落用的 Lootbox 预制体
                try
                {
                    if (config != null && !config.lootBoxBlocksBullets && bossMain != null && bossMain.deadLootBoxPrefab != null)
                    {
                        prefab = bossMain.deadLootBoxPrefab;
                        useBossDeadBoxPrefab = true;
                        DevLog("[BossRush] 使用 Boss 死亡掉落的 Lootbox 模板作为奖励箱");
                    }
                }
                catch
                {
                }

                if (prefab == null)
                {
                    prefab = GetLootBoxTemplateWithLoader();
                }

                if (prefab == null)
                {
                    DevLog("[BossRush] 未找到可用的 Lootbox 模板，回退到原版 Boss 掉落逻辑");
                    return;
                }

                try
                {
                    bossMain.dropBoxOnDead = false;
                }
                catch
                {
                }

                Vector3 position = bossMain.transform.position + Vector3.up * 0.1f;
                Quaternion rotation = bossMain.transform.rotation;

                InteractableLootbox lootbox = UnityEngine.Object.Instantiate(prefab, position, rotation);
                lootbox.needInspect = true;

                // 为 Boss 奖励箱创建独立本地 Inventory，避免与其他 Lootbox 通过位置哈希共享同一个库存
                // 这是解决"箱子没有奖励且格子为64"问题的关键
                try
                {
                    System.Type lootboxType = typeof(InteractableLootbox);
                    System.Reflection.MethodInfo createLocalInventoryMethod =
                        lootboxType.GetMethod("CreateLocalInventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (createLocalInventoryMethod != null)
                    {
                        createLocalInventoryMethod.Invoke(lootbox, null);
                        DevLog("[BossRush] Boss 奖励箱已创建独立本地 Inventory");
                    }
                }
                catch (Exception localInvEx)
                {
                    DevLog("[BossRush] 创建 Boss 奖励箱本地 Inventory 失败: " + localInvEx.Message);
                }

                try
                {
                    if (lootbox != null && lootbox.gameObject != null)
                    {
                        bool isPetRelated = false;

                        try
                        {
                            if (lootbox.GetComponentInParent<PetAI>() != null)
                            {
                                isPetRelated = true;
                            }
                        }
                        catch {}

                        try
                        {
                            if (lootbox.GetComponentInParent<PetProxy>() != null)
                            {
                                isPetRelated = true;
                            }
                        }
                        catch {}

                        if (!isPetRelated)
                        {
                            BossRushLootboxMarker marker = lootbox.gameObject.GetComponent<BossRushLootboxMarker>();
                            if (marker == null)
                            {
                                lootbox.gameObject.AddComponent<BossRushLootboxMarker>();
                            }

                            BossRushDeleteLootboxInteractable deleteInteract = null;
                            try
                            {
                                deleteInteract = lootbox.gameObject.GetComponent<BossRushDeleteLootboxInteractable>();
                            }
                            catch {}

                            if (deleteInteract == null)
                            {
                                try
                                {
                                    deleteInteract = lootbox.gameObject.AddComponent<BossRushDeleteLootboxInteractable>();
                                }
                                catch {}
                            }

                            try
                            {
                                lootbox.interactableGroup = true;

                                System.Type baseType = typeof(InteractableBase);
                                System.Reflection.FieldInfo othersField = baseType.GetField("otherInterablesInGroup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (othersField != null)
                                {
                                    System.Collections.Generic.List<InteractableBase> hostList = othersField.GetValue(lootbox) as System.Collections.Generic.List<InteractableBase>;
                                    if (hostList == null)
                                    {
                                        hostList = new System.Collections.Generic.List<InteractableBase>();
                                        othersField.SetValue(lootbox, hostList);
                                    }
                                    if (deleteInteract != null && !hostList.Contains(deleteInteract))
                                    {
                                        hostList.Add(deleteInteract);
                                    }
                                }
                            }
                            catch {}
                        }
                    }
                }
                catch {}

                // 仅在使用 BossRush 专用奖励箱模板时，按配置处理“挡子弹”和伪搬运交互
                if (!useBossDeadBoxPrefab)
                {
                    // 根据配置决定是否让掉落箱作为子弹掩体
                    try
                    {
                        ApplyLootBoxCoverSetting(lootbox);
                    }
                    catch
                    {
                    }

                    // 为 Boss 奖励箱添加伪搬运交互（BossRushCarryInteractable），并与 Lootbox 组成同一个交互组
                    try
                    {
                        BossRushCarryInteractable carryInteract = lootbox.gameObject.GetComponent<BossRushCarryInteractable>();
                        if (carryInteract == null)
                        {
                            carryInteract = lootbox.gameObject.AddComponent<BossRushCarryInteractable>();
                        }

                        // 仅让 Lootbox 作为组的 master，搬起交互只是成员，避免交互组状态混乱
                        try
                        {
                            lootbox.interactableGroup = true;

                            System.Type baseType = typeof(InteractableBase);
                            System.Reflection.FieldInfo othersField = baseType.GetField("otherInterablesInGroup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (othersField != null)
                            {
                                System.Collections.Generic.List<InteractableBase> hostList = othersField.GetValue(lootbox) as System.Collections.Generic.List<InteractableBase>;
                                if (hostList == null)
                                {
                                    hostList = new System.Collections.Generic.List<InteractableBase>();
                                    othersField.SetValue(lootbox, hostList);
                                }
                                if (!hostList.Contains(carryInteract))
                                {
                                    hostList.Add(carryInteract);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex);
                }
                catch
                {
                }

                Duckov.Utilities.LootBoxLoader loader = lootbox.GetComponent<Duckov.Utilities.LootBoxLoader>();
                if (loader == null)
                {
                    try
                    {
                        loader = lootbox.gameObject.AddComponent<Duckov.Utilities.LootBoxLoader>();
                    }
                    catch
                    {
                    }
                }

                if (loader != null)
                {
                    try
                    {
                        System.Type loaderType = typeof(Duckov.Utilities.LootBoxLoader);

                        // 根据 totalCount 设定随机数量范围（不再向下浮动，保证不少于目标数量）
                        int minCount = Math.Max(1, totalCount);
                        int maxCount = Math.Max(minCount, totalCount + 1);

                        System.Reflection.FieldInfo randomCountField = loaderType.GetField("randomCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (randomCountField != null)
                        {
                            Vector2Int rc = new Vector2Int(minCount, maxCount);
                            randomCountField.SetValue(loader, rc);
                        }

                        // 调整品质权重：1 代表普通品质，5 代表高品质
                        System.Reflection.FieldInfo qualitiesField = loaderType.GetField("qualities", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (qualitiesField != null)
                        {
                            Duckov.Utilities.RandomContainer<int> qualities = qualitiesField.GetValue(loader) as Duckov.Utilities.RandomContainer<int>;
                            if (qualities != null)
                            {
                                qualities.entries.Clear();

                                float clampedHigh = Mathf.Clamp01(highChance);
                                float lowWeight = 1f - clampedHigh;
                                float highWeight = clampedHigh;

                                if (lowWeight <= 0f)
                                {
                                    lowWeight = 0.01f;
                                }
                                if (highWeight <= 0f)
                                {
                                    highWeight = 0.01f;
                                }

                                qualities.AddEntry(1, lowWeight);
                                qualities.AddEntry(5, highWeight);

                                qualities.RefreshPercent();
                            }
                        }

                        System.Reflection.FieldInfo tagsField = loaderType.GetField("tags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        System.Reflection.FieldInfo excludeTagsField = loaderType.GetField("excludeTags", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (tagsField != null)
                        {
                            Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag> tagsContainer = tagsField.GetValue(loader) as Duckov.Utilities.RandomContainer<Duckov.Utilities.Tag>;
                            if (tagsContainer != null)
                            {
                                tagsContainer.entries.Clear();

                                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
                                if (tagsData != null && tagsData.AllTags != null)
                                {
                                    var allTags = tagsData.AllTags;
                                    for (int i = 0; i < allTags.Count; i++)
                                    {
                                        Duckov.Utilities.Tag t = allTags[i];
                                        if (t == null)
                                        {
                                            continue;
                                        }
                                        if (t == tagsData.Character)
                                        {
                                            continue;
                                        }
                                        if (t == tagsData.DestroyOnLootBox)
                                        {
                                            continue;
                                        }
                                        if (t == tagsData.DontDropOnDeadInSlot)
                                        {
                                            continue;
                                        }
                                        if (t == tagsData.LockInDemoTag)
                                        {
                                            continue;
                                        }
                                        tagsContainer.AddEntry(t, 1f);
                                    }
                                }

                                tagsContainer.RefreshPercent();
                            }
                        }

                        if (excludeTagsField != null)
                        {
                            List<Duckov.Utilities.Tag> excludeList = excludeTagsField.GetValue(loader) as List<Duckov.Utilities.Tag>;
                            if (excludeList == null)
                            {
                                excludeList = new List<Duckov.Utilities.Tag>();
                                excludeTagsField.SetValue(loader, excludeList);
                            }

                            Duckov.Utilities.GameplayDataSettings.TagsData tagsData2 = Duckov.Utilities.GameplayDataSettings.Tags;
                            if (tagsData2 != null)
                            {
                                if (tagsData2.DestroyOnLootBox != null && !excludeList.Contains(tagsData2.DestroyOnLootBox))
                                {
                                    excludeList.Add(tagsData2.DestroyOnLootBox);
                                }
                                if (tagsData2.DontDropOnDeadInSlot != null && !excludeList.Contains(tagsData2.DontDropOnDeadInSlot))
                                {
                                    excludeList.Add(tagsData2.DontDropOnDeadInSlot);
                                }
                                if (tagsData2.LockInDemoTag != null && !excludeList.Contains(tagsData2.LockInDemoTag))
                                {
                                    excludeList.Add(tagsData2.LockInDemoTag);
                                }
                            }
                        }

                        try
                        {
                            System.Type loaderEntryType = loaderType.GetNestedType("Entry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                            System.Reflection.FieldInfo randomPoolField = loaderType.GetField("randomPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            object randomPoolObj = (randomPoolField != null) ? randomPoolField.GetValue(loader) : null;

                            // 新增：在新增 LootBoxLoader 组件时，randomPool 可能为 null，这里显式创建一个实例
                            if (randomPoolObj == null && randomPoolField != null)
                            {
                                try
                                {
                                    randomPoolObj = Activator.CreateInstance(randomPoolField.FieldType);
                                    randomPoolField.SetValue(loader, randomPoolObj);
                                }
                                catch
                                {
                                }
                            }

                            if (loaderEntryType != null && randomPoolObj != null)
                            {
                                System.Type randomPoolType = randomPoolObj.GetType();
                                System.Reflection.FieldInfo entriesField = randomPoolType.GetField("entries", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                object entriesObj = (entriesField != null) ? entriesField.GetValue(randomPoolObj) : null;
                                System.Collections.IList entriesList = entriesObj as System.Collections.IList;
                                System.Type randomContainerEntryType = randomPoolType.GetNestedType("Entry", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                                if (randomContainerEntryType != null && randomContainerEntryType.ContainsGenericParameters && randomPoolType.IsGenericType)
                                {
                                    System.Type[] genericArgs = randomPoolType.GetGenericArguments();
                                    if (genericArgs != null && genericArgs.Length > 0)
                                    {
                                        randomContainerEntryType = randomContainerEntryType.MakeGenericType(genericArgs);
                                    }
                                }

                                // 新增：如果 entries 列表本身为 null，则创建一个新的列表实例
                                if (entriesList == null && entriesField != null)
                                {
                                    try
                                    {
                                        object newEntries = Activator.CreateInstance(entriesField.FieldType);
                                        entriesField.SetValue(randomPoolObj, newEntries);
                                        entriesList = newEntries as System.Collections.IList;
                                    }
                                    catch
                                    {
                                    }
                                }

                                if (entriesList != null && randomContainerEntryType != null)
                                {
                                    entriesList.Clear();

                                    System.Reflection.FieldInfo lootEntryItemIdField = loaderEntryType.GetField("itemTypeID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    System.Reflection.FieldInfo rcValueField = randomContainerEntryType.GetField("value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                    System.Reflection.FieldInfo rcWeightField = randomContainerEntryType.GetField("weight", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                                    if (lootEntryItemIdField != null && rcValueField != null && rcWeightField != null)
                                    {
                                        HashSet<int> idSet = new HashSet<int>();

                                        Duckov.Utilities.GameplayDataSettings.TagsData tagsData3 = Duckov.Utilities.GameplayDataSettings.Tags;
                                        if (tagsData3 != null)
                                        {
                                            List<Duckov.Utilities.Tag> baseExclude = new List<Duckov.Utilities.Tag>();
                                            if (tagsData3.DestroyOnLootBox != null)
                                            {
                                                baseExclude.Add(tagsData3.DestroyOnLootBox);
                                            }
                                            if (tagsData3.DontDropOnDeadInSlot != null)
                                            {
                                                baseExclude.Add(tagsData3.DontDropOnDeadInSlot);
                                            }
                                            if (tagsData3.LockInDemoTag != null)
                                            {
                                                baseExclude.Add(tagsData3.LockInDemoTag);
                                            }

                                            List<Duckov.Utilities.Tag> includeTags = new List<Duckov.Utilities.Tag>();
                                            if (tagsData3.AllTags != null)
                                            {
                                                foreach (Duckov.Utilities.Tag tag in tagsData3.AllTags)
                                                {
                                                    if (tag == null)
                                                    {
                                                        continue;
                                                    }
                                                    if (baseExclude.Contains(tag))
                                                    {
                                                        continue;
                                                    }
                                                    includeTags.Add(tag);
                                                }
                                            }

                                            for (int i = 0; i < includeTags.Count; i++)
                                            {
                                                Duckov.Utilities.Tag requireTag = includeTags[i];
                                                if (requireTag == null)
                                                {
                                                    continue;
                                                }

                                                ItemFilter filter = default(ItemFilter);
                                                filter.requireTags = new Duckov.Utilities.Tag[]
                                                {
                                                    requireTag
                                                };
                                                filter.excludeTags = baseExclude.ToArray();
                                                filter.minQuality = 1;
                                                filter.maxQuality = 8;

                                                int[] ids = ItemAssetsCollection.Search(filter);
                                                if (ids == null)
                                                {
                                                    continue;
                                                }

                                                for (int j = 0; j < ids.Length; j++)
                                                {
                                                    int id = ids[j];
                                                    if (id > 0)
                                                    {
                                                        if (ManualLootBlacklist.Contains(id))
                                                        {
                                                            continue;
                                                        }
                                                        idSet.Add(id);
                                                    }
                                                }
                                            }
                                        }

                                        DevLog("[BossRush] Boss 奖励候选物品数量=" + idSet.Count);

                                        if (idSet.Count > 0)
                                        {
                                            foreach (int id2 in idSet)
                                            {
                                                object lootEntry = Activator.CreateInstance(loaderEntryType);
                                                lootEntryItemIdField.SetValue(lootEntry, id2);

                                                object rcEntry = Activator.CreateInstance(randomContainerEntryType);
                                                rcValueField.SetValue(rcEntry, lootEntry);
                                                
                                                // 皇冠（1254）权重降为0.1，使其掉落概率与原版接近
                                                float itemWeight = (id2 == 1254) ? 0.1f : 1f;
                                                rcWeightField.SetValue(rcEntry, itemWeight);

                                                entriesList.Add(rcEntry);
                                            }

                                            DevLog("[BossRush] Boss 奖励 randomPool 条目数=" + entriesList.Count);
                                        }

                                        // 使用 LootBoxLoader.fixedItems 做一次生成前的高价值保底：
                                        // 1) 不按价格过滤 randomPool 候选，只是额外收集 Value>2000 的候选列表；
                                        // 2) 如果存在高价值候选，则随机选 1 个写入 fixedItems，并将 fixedItemSpawnChance 设为 1；
                                        System.Reflection.FieldInfo fixedItemsField = loaderType.GetField("fixedItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                        System.Reflection.FieldInfo fixedChanceField = loaderType.GetField("fixedItemSpawnChance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                                        List<int> fixedItems = null;
                                        if (fixedItemsField != null)
                                        {
                                            fixedItems = fixedItemsField.GetValue(loader) as List<int>;
                                            if (fixedItems == null)
                                            {
                                                fixedItems = new List<int>();
                                                fixedItemsField.SetValue(loader, fixedItems);
                                            }
                                            fixedItems.Clear();
                                        }

                                        const int bossHighValueThreshold = 5000;
                                        List<int> highValueCandidates = new List<int>();
                                        List<int> qualityRangeCandidates = new List<int>();
                                        int bestCandidateId = -1;
                                        int bestCandidateValue = -1;
                                        Dictionary<int, int> candidateValues = new Dictionary<int, int>();

                                        // 使用缓存评估物品价值，避免同步实例化导致卡顿
                                        try
                                        {
                                            foreach (int candidateId in idSet)
                                            {
                                                int v = 0;
                                                int quality = -1;

                                                // 优先从缓存获取物品价值信息
                                                if (TryGetCachedItemValue(candidateId, out v, out quality))
                                                {
                                                    // 缓存命中，直接使用缓存数据
                                                }
                                                else
                                                {
                                                    // 缓存未命中，跳过该物品的价值评估（不再同步实例化）
                                                    // 仍然将其加入候选池，只是不参与高价值保底筛选
                                                    v = 0;
                                                    quality = -1;
                                                }

                                                // 根据品质筛选保底候选
                                                if (quality >= 4 && quality <= 6)
                                                {
                                                    qualityRangeCandidates.Add(candidateId);
                                                }

                                                if (v >= bossHighValueThreshold)
                                                {
                                                    highValueCandidates.Add(candidateId);
                                                }

                                                candidateValues[candidateId] = v;

                                                if (v > bestCandidateValue)
                                                {
                                                    bestCandidateValue = v;
                                                    bestCandidateId = candidateId;
                                                }
                                            }
                                        }
                                        catch (Exception priceEx)
                                        {
                                            DevLog("[BossRush] 评估 Boss 候选物品价格失败: " + priceEx.Message);
                                        }

                                        if (fixedItems != null)
                                        {
                                            int guaranteedId = -1;

                                            if (qualityRangeCandidates.Count > 0)
                                            {
                                                int pickIndex = UnityEngine.Random.Range(0, qualityRangeCandidates.Count);
                                                guaranteedId = qualityRangeCandidates[pickIndex];
                                            }

                                            if (guaranteedId > 0)
                                            {
                                                fixedItems.Add(guaranteedId);

                                                if (fixedChanceField != null)
                                                {
                                                    fixedChanceField.SetValue(loader, 1f);
                                                }

                                                try
                                                {
                                                    var meta = ItemAssetsCollection.GetMetaData(guaranteedId);
                                                    int finalValue;
                                                    if (!candidateValues.TryGetValue(guaranteedId, out finalValue))
                                                    {
                                                        finalValue = -1;
                                                    }
                                                    string name = meta.DisplayName;
                                                    DevLog("[BossRush] Boss 掉落保底物品: typeID=" + guaranteedId + ", 名称=" + name + ", 价格=" + finalValue + (highValueCandidates.Count > 0 ? " (>= " + bossHighValueThreshold + ")" : " (相对最高价)"));
                                                }
                                                catch (Exception metaEx)
                                                {
                                                    DevLog("[BossRush] Boss 掉落保底物品(typeID=" + guaranteedId + ")，获取元数据失败: " + metaEx.Message);
                                                }
                                            }
                                            else
                                            {
                                                if (fixedChanceField != null)
                                                {
                                                    fixedChanceField.SetValue(loader, 0f);
                                                }

                                                DevLog("[BossRush] Boss 候选池中未能确定任何可用的保底物品，本次不做高价值保底。");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception poolEx)
                        {
                            DevLog("[BossRush] 构建 Boss 奖励 randomPool 失败: " + poolEx.Message);
                        }

                        loader.randomFromPool = true;
                        loader.ignoreLevelConfig = true;
                        loader.CalculateChances();

                        // 关键：手动调用 Setup() 填充物品
                        // 因为 CreateLocalInventory 后 Inventory 属性直接返回 inventoryReference，
                        // 不会再走 GetOrCreateInventory 的逻辑，所以 LootBoxLoader.Setup() 不会被自动触发
                        try
                        {
                            loader.StartSetup();
                            DevLog("[BossRush] 已手动触发 LootBoxLoader.StartSetup() 填充物品");
                        }
                        catch (Exception setupEx)
                        {
                            DevLog("[BossRush] 调用 LootBoxLoader.StartSetup() 失败: " + setupEx.Message);
                        }
                    }
                    catch (Exception cfgEx)
                    {
                        DevLog("[BossRush] 配置 Boss 奖励 LootBoxLoader 失败: " + cfgEx.Message);
                    }
                }
                else
                {
                    DevLog("[BossRush] Boss 奖励盒子上没有 LootBoxLoader 组件，将使用 Prefab 默认内容");
                }

                // 访问一次 Inventory，确保需要搜索动画，并设置初始容量
                Inventory inventory = lootbox.Inventory;
                if (inventory != null)
                {
                    inventory.NeedInspection = lootbox.needInspect;
                    // 先设置一个较大的初始容量，等 LootBoxLoader 填充完成后再调整
                    inventory.SetCapacity(512);
                }

                DevLog("[BossRush] 已为 Boss 生成专用奖励盒子，总目标物品数量=" + totalCount + ", 期望高品质比例=" + highChance.ToString("P0") + "（击杀耗时: " + killDuration.ToString("F1") + "秒）");

                // 龙裔遗族特殊掉落：在掉落箱创建后添加龙套装
                // 使用协程等待 LootBoxLoader 填充完成后再添加
                try
                {
                    this.StartCoroutine(AddDragonSetToLootboxCoroutine(lootbox, bossMain));
                }
                catch (Exception dragonEx)
                {
                    DevLog("[BossRush] 安排龙套装掉落协程失败: " + dragonEx.Message);
                }

                // 调试：记录实际掉落物的品质与价格，方便验证过滤逻辑
                try
                {
                    this.StartCoroutine(LogBossLootInventory_LootAndRewards(lootbox));
                }
                catch (Exception logEx)
                {
                    DevLog("[BossRush] 安排 Boss 掉落实际物品日志协程失败: " + logEx.Message);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] RandomizeBossLoot 错误: " + e.Message);
            }
        }

        /// <summary>
        /// 调试：记录 Boss 掉落实际物品列表（LootAndRewards 备份实现）
        /// </summary>
        private IEnumerator LogBossLootInventory_LootAndRewards(InteractableLootbox lootbox)
        {
            if (lootbox == null)
            {
                yield break;
            }

            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                yield break;
            }

            // 等待 LootBoxLoader 完成填充
            int tries = 0;
            const int maxTries = 50;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            List<Item> content = inv.Content;
            if (content == null)
            {
                yield break;
            }

            try
            {
                DevLog("[BossRush] Boss 掉落实际物品列表开始, 总数=" + content.Count);
            }
            catch {}

            for (int i = 0; i < content.Count; i++)
            {
                Item item = content[i];
                if (item == null)
                {
                    continue;
                }

                int q = -1;
                int v = -1;
                string name = "<unknown>";
                string displayQ = "<unknown>";

                try
                {
                    q = item.Quality;
                }
                catch {}

                try
                {
                    v = item.Value;
                }
                catch {}

                try
                {
                    name = item.DisplayName;
                }
                catch {}

                try
                {
                    displayQ = item.DisplayQuality.ToString();
                }
                catch {}

                DevLog("[BossRush] 实际掉落物: typeID=" + item.TypeID + ", 名称=" + name + ", Quality=" + q + ", DisplayQuality=" + displayQ + ", Value=" + v);
            }

            try
            {
                DevLog("[BossRush] Boss 掉落实际物品列表结束");
            }
            catch {}

            // LootBoxLoader 填充完成后，根据实际物品数量调整 Inventory 容量
            // 这是解决"格子为64"问题的关键
            try
            {
                int lastPos = inv.GetLastItemPosition();
                int newCapacity = Mathf.Max(8, lastPos + 1);
                inv.SetCapacity(newCapacity);
                DevLog("[BossRush] Boss 奖励箱容量已调整为: " + newCapacity);
            }
            catch (Exception capEx)
            {
                DevLog("[BossRush] 调整 Boss 奖励箱容量失败: " + capEx.Message);
            }
        }

        /// <summary>
        /// 清理通关奖励 Lootbox 中的低品质/低价值物品，仅保留高品质奖励（LootAndRewards 备份实现）
        /// </summary>
        private IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(InteractableLootbox lootbox, int highQualityCount)
        {
            if (lootbox == null)
            {
                yield break;
            }

            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                yield break;
            }

            // 等待一小段时间，给 LootBoxLoader.Setup 机会完成
            const int maxTries = 30;
            int tries = 0;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            try
            {
                List<Item> content = inv.Content;
                if (content == null || content.Count == 0)
                {
                    yield break;
                }

                // 调试：清理前先输出一次通关奖励箱实际物品列表
                try
                {
                    DevLog("[BossRush] 通关奖励清理前物品列表开始, 总数=" + content.Count);
                    for (int i = 0; i < content.Count; i++)
                    {
                        Item item = content[i];
                        if (item == null) continue;

                        int q = -1;
                        int v = -1;
                        string name = "<unknown>";
                        string displayQ = "<unknown>";

                        try { q = item.Quality; } catch {}
                        try { v = item.Value; } catch {}
                        try { name = item.DisplayName; } catch {}
                        try { displayQ = item.DisplayQuality.ToString(); } catch {}

                        DevLog("[BossRush] 通关奖励实际物品(清理前): typeID=" + item.TypeID + ", 名称=" + name + ", Quality=" + q + ", DisplayQuality=" + displayQ + ", Value=" + v);
                    }
                    DevLog("[BossRush] 通关奖励清理前物品列表结束");
                }
                catch {}

                int beforeCount = content.Count;

                // 按品质和价格分三档：高品质高价、高品质低价、其它
                List<Item> preferred = new List<Item>();
                List<Item> fallbackHighQuality = new List<Item>();
                List<Item> fallbackOthers = new List<Item>();

                const int priceThreshold = 2000;

                for (int i = 0; i < content.Count; i++)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    int value = 0;
                    try
                    {
                        value = item.Value;
                    }
                    catch
                    {
                        value = 0;
                    }

                    if (item.Quality >= 5)
                    {
                        if (value >= priceThreshold)
                        {
                            preferred.Add(item);
                        }
                        else
                        {
                            fallbackHighQuality.Add(item);
                        }
                    }
                    else
                    {
                        fallbackOthers.Add(item);
                    }
                }

                int target = highQualityCount;
                if (target < 1)
                {
                    target = 1;
                }
                if (target > beforeCount)
                {
                    target = beforeCount;
                }

                List<Item> keep = new List<Item>(target);

                bool hasPreferred = preferred.Count > 0;

                // 1) 如果存在高品质高价物品，则尽量只保留这一档（等价于每一格都是一次保底抽取）
                for (int i = 0; i < preferred.Count && keep.Count < target; i++)
                {
                    keep.Add(preferred[i]);
                }

                // 2) 如果本次没有任何满足价格阈值的高品质物品，则退而求其次，用高品质但低价的物品补齐
                if (!hasPreferred)
                {
                    for (int i = 0; i < fallbackHighQuality.Count && keep.Count < target; i++)
                    {
                        keep.Add(fallbackHighQuality[i]);
                    }
                }

                // 3) 仍然完全不使用低品质物品 (fallbackOthers)，保证 Quality>=5

                int removed = 0;

                // 反向遍历，删除未入选的物品
                for (int i = content.Count - 1; i >= 0; i--)
                {
                    Item item = content[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (!keep.Contains(item))
                    {
                        removed++;
                        ItemTreeExtensions.DestroyTree(item);
                    }
                }

                // 调试：清理后再次输出通关奖励箱实际物品列表
                try
                {
                    List<Item> afterContent = inv.Content;
                    if (afterContent != null)
                    {
                        DevLog("[BossRush] 通关奖励清理后物品列表开始, 总数=" + afterContent.Count);
                        for (int i = 0; i < afterContent.Count; i++)
                        {
                            Item item = afterContent[i];
                            if (item == null) continue;

                            int q2 = -1;
                            int v2 = -1;
                            string name2 = "<unknown>";
                            string displayQ2 = "<unknown>";

                            try { q2 = item.Quality; } catch {}
                            try { v2 = item.Value; } catch {}
                            try { name2 = item.DisplayName; } catch {}
                            try { displayQ2 = item.DisplayQuality.ToString(); } catch {}

                            DevLog("[BossRush] 通关奖励实际物品(清理后): typeID=" + item.TypeID + ", 名称=" + name2 + ", Quality=" + q2 + ", DisplayQuality=" + displayQ2 + ", Value=" + v2);
                        }
                        DevLog("[BossRush] 通关奖励清理后物品列表结束");
                    }
                }
                catch {}

                if (removed > 0)
                {
                    DevLog("[BossRush] 调整通关奖励箱内容: 原总数=" + beforeCount + ", 目标高品质数量=" + target + ", 实际保留数量=" + keep.Count + ", 移除数量=" + removed);
                }
            }
            catch (Exception cleanEx)
            {
                DevLog("[BossRush] 清理通关奖励箱低品质物品失败: " + cleanEx.Message);
            }
        }

        /// <summary>
        /// 判断Boss是否是龙裔遗族（支持多Boss模式）
        /// 通过GameObject名称或预设nameKey判断，不依赖单一实例引用
        /// </summary>
        private bool IsDragonDescendantBoss(CharacterMainControl boss)
        {
            if (boss == null) return false;
            
            try
            {
                // 方法1：检查GameObject名称（SpawnDragonDescendant中设置为"BossRush_DragonDescendant"）
                if (boss.gameObject != null && boss.gameObject.name.Contains("DragonDescendant"))
                {
                    return true;
                }
                
                // 方法2：检查预设nameKey
                if (boss.characterPreset != null && 
                    boss.characterPreset.nameKey == DragonDescendantConfig.BOSS_NAME_KEY)
                {
                    return true;
                }
            }
            catch { }
            
            return false;
        }

        /// <summary>
        /// 龙裔遗族特殊掉落：在掉落箱填充完成后添加龙套装（协程版本）
        /// 从Boss身上获取装备（保留耐久度等属性），而不是新创建
        /// </summary>
        private IEnumerator AddDragonSetToLootboxCoroutine(InteractableLootbox lootbox, CharacterMainControl bossMain)
        {
            // 检查是否是龙裔遗族Boss（通过名称判断，支持多Boss模式）
            if (!IsDragonDescendantBoss(bossMain))
            {
                yield break;
            }

            // 检查是否启用了随机掉落配置
            if (config == null || !config.enableRandomBossLoot)
            {
                DevLog("[DragonDescendant] 随机掉落未启用，跳过龙套装掉落");
                yield break;
            }

            if (lootbox == null)
            {
                DevLog("[DragonDescendant] 掉落箱为空，无法添加龙套装");
                yield break;
            }

            // 等待掉落箱Inventory加载完成
            Inventory inv = lootbox.Inventory;
            if (inv == null)
            {
                DevLog("[DragonDescendant] 掉落箱Inventory为空");
                yield break;
            }

            int tries = 0;
            const int maxTries = 30;
            while (tries < maxTries && inv.Loading)
            {
                tries++;
                yield return new WaitForSeconds(0.1f);
            }

            // 随机选择掉落龙头或龙甲
            bool dropHelm = UnityEngine.Random.Range(0, 2) == 0;
            int selectedTypeId = dropHelm ? DragonDescendantConfig.DRAGON_HELM_TYPE_ID : DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID;
            string itemName = dropHelm ? "龙头" : "龙甲";

            DevLog("[DragonDescendant] 随机选择龙套装掉落: " + itemName + " (TypeID=" + selectedTypeId + ")");

            try
            {
                // 通过TypeID查找物品模板并创建新实例（满耐久）
                Item templateItem = FindItemByTypeId(selectedTypeId);
                if (templateItem == null)
                {
                    DevLog("[DragonDescendant] 未找到龙套装模板: TypeID=" + selectedTypeId);
                    yield break;
                }

                // 创建新的物品实例（满耐久）
                Item newItem = templateItem.CreateInstance();
                if (newItem == null)
                {
                    DevLog("[DragonDescendant] 创建龙套装实例失败");
                    yield break;
                }

                // 确保耐久度为满（CreateInstance应该已经是满耐久，但保险起见再设置一次）
                float maxDurability = newItem.MaxDurability;
                if (maxDurability > 0)
                {
                    newItem.Durability = maxDurability;
                }

                // 添加到掉落箱
                inv.AddItem(newItem);
                DevLog("[DragonDescendant] 已将 " + itemName + " 添加到掉落箱，耐久度: " + GetItemDurability(newItem));
            }
            catch (Exception addEx)
            {
                DevLog("[DragonDescendant] 添加龙套装到掉落箱失败: " + addEx.Message);
            }
        }

        /// <summary>
        /// 获取物品的耐久度（用于日志）
        /// </summary>
        private string GetItemDurability(Item item)
        {
            try
            {
                if (item == null) return "N/A";
                var durabilityStat = item.GetStat("Durability");
                var maxDurabilityStat = item.GetStat("MaxDurability");
                if (durabilityStat != null && maxDurabilityStat != null)
                {
                    return durabilityStat.Value.ToString("F1") + "/" + maxDurabilityStat.Value.ToString("F1");
                }
                else if (durabilityStat != null)
                {
                    return durabilityStat.Value.ToString("F1");
                }
            }
            catch { }
            return "N/A";
        }

        /// <summary>
        /// [已废弃] 在掉落物品生成之前，检查是否是龙裔遗族Boss并添加龙套装到库存
        /// 注意：此方法已不再使用，因为BossRush创建独立的掉落箱，不使用Boss库存
        /// 请使用 AddDragonSetToLootboxCoroutine 代替
        /// </summary>
        [System.Obsolete("使用 AddDragonSetToLootboxCoroutine 代替")]
        private void TryAddDragonSetToLootBeforeSpawn(CharacterMainControl bossMain)
        {
            // 此方法已废弃，保留仅为兼容性
            // 龙套装掉落逻辑已移至 AddDragonSetToLootboxCoroutine
            DevLog("[DragonDescendant] TryAddDragonSetToLootBeforeSpawn 已废弃，请使用 AddDragonSetToLootboxCoroutine");
        }
    }
}
