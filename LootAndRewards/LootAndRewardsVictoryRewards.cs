// ============================================================================
// LootAndRewardsVictoryRewards.cs - 胜利奖励箱流程
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 所有敌人击败完成（LootAndRewards 分部实现）
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
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] OnAllEnemiesDefeated 场景校验失败，继续按通关流程收尾: " + e.Message);
            }

            SetBossRushRuntimeActive(false);
            // 注意：胜利后保持 bossRushArenaActive = true，确保大兴兴清理逻辑持续运行
            // 直到玩家离开 DEMO 场景，防止玩家走到左边触发原版大兴兴 Boss
            // bossRushArenaActive 会在玩家离开场景时（通过 OnSceneLoaded）自动重置

            // 取消敌人死亡监听
            Health.OnDead -= OnEnemyDiedWithDamageInfo;

            // 触发通关成就检查
            CheckClearAchievements();

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
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 切换 BossRush 路牌到凯旋状态失败: " + e.Message);
            }

            // 根据本次难度播放通关横幅，并启动延迟落地的奖励箱虚影演出
            try
            {
                int rewardHighCount = (bossesPerWave <= 1) ? 3 : 10;
                string diffName = (bossesPerWave <= 1) ? L10n.T("弹指可灭", "Easy Mode") : L10n.T("有点意思", "Hard Mode");

                string banner = L10n.T(
                    "<color=#FF0000>恭</color><color=#FF7F00>喜</color><color=#FFFF00>通</color><color=#00FF00>关</color> " +
                    "<color=#00FFFF>\u300c" + diffName + "\u300d</color>！ " +
                    "战利品宝箱正在显现\\o/",
                    "<color=#FF0000>C</color><color=#FF7F00>o</color><color=#FFFF00>n</color><color=#00FF00>g</color><color=#00FFFF>r</color><color=#0000FF>a</color><color=#8B00FF>t</color><color=#FF0000>s</color>! " +
                    "<color=#00FFFF>\u300c" + diffName + "\u300d</color> " +
                    "Your reward crate is materializing! \\o/"
                );

                ShowBigBanner(banner);
                StartVictoryRewardShadowCrate_LootAndRewards(rewardHighCount);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 生成 BossRush 通关奖励失败: " + e.Message);
            }

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
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 显示完成对话失败: " + e.Message);
            }
            finally
            {
                CompleteVictoryRewardShadowCrate_LootAndRewards();
            }

            try
            {
                // 生成返回出生点的交互点
                TryCreateReturnInteractable();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 生成返回交互点失败: " + e.Message);
            }
        }

        private CharacterMainControl TryGetMainCharacterForVictoryRewardShadowCrate_LootAndRewards()
        {
            CharacterMainControl main = null;

            try
            {
                main = CharacterMainControl.Main;
            }
            catch (Exception e)
            {
                LogLootWarningLimited("VictoryRewardShadow_main", "读取 CharacterMainControl.Main 失败", e);
            }

            if (main == null)
            {
                try
                {
                    main = playerCharacter as CharacterMainControl;
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("VictoryRewardShadow_playerCharacter", "从 playerCharacter 解析玩家失败", e);
                }
            }

            return main;
        }

        private InteractableLootbox GetVictoryRewardVisualLootBoxTemplate_LootAndRewards()
        {
            if (_cachedVictoryRewardVisualLootBoxTemplate != null)
            {
                try
                {
                    if (_cachedVictoryRewardVisualLootBoxTemplate.gameObject != null)
                    {
                        return _cachedVictoryRewardVisualLootBoxTemplate;
                    }
                }
                catch {}

                _cachedVictoryRewardVisualLootBoxTemplate = null;
            }

            try
            {
                InteractableLootbox[] all = Resources.FindObjectsOfTypeAll<InteractableLootbox>();
                if (all == null || all.Length <= 0)
                {
                    return GetDifficultyRewardLootBoxTemplate();
                }

                InteractableLootbox preferredBag = null;
                InteractableLootbox preferredNonDeliver = null;
                InteractableLootbox deliverFallback = null;

                for (int i = 0; i < all.Length; i++)
                {
                    InteractableLootbox box = all[i];
                    if (box == null || box.gameObject == null)
                    {
                        continue;
                    }

                    string name = box.name ?? string.Empty;
                    bool hasRenderer = false;

                    try
                    {
                        Renderer[] renderers = box.GetComponentsInChildren<Renderer>(true);
                        hasRenderer = renderers != null && renderers.Length > 0;
                    }
                    catch {}

                    if (!hasRenderer)
                    {
                        continue;
                    }

                    bool isDeliver = name.IndexOf("DeliverBox", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool looksLikeBag =
                        name.IndexOf("EnemyDie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Bag", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!isDeliver && looksLikeBag)
                    {
                        preferredBag = box;
                        break;
                    }

                    if (!isDeliver && preferredNonDeliver == null)
                    {
                        preferredNonDeliver = box;
                    }

                    if (isDeliver && deliverFallback == null)
                    {
                        deliverFallback = box;
                    }
                }

                if (preferredBag != null)
                {
                    _cachedVictoryRewardVisualLootBoxTemplate = preferredBag;
                    DevLog("[BossRush] 通关奖励视觉模板优先使用可见战利品箱: " + preferredBag.name);
                }
                else if (preferredNonDeliver != null)
                {
                    _cachedVictoryRewardVisualLootBoxTemplate = preferredNonDeliver;
                    DevLog("[BossRush] 通关奖励视觉模板使用非快递箱 Lootbox: " + preferredNonDeliver.name);
                }
                else if (deliverFallback != null)
                {
                    _cachedVictoryRewardVisualLootBoxTemplate = deliverFallback;
                    DevLog("[BossRush] [WARNING] 通关奖励视觉模板未找到更合适箱体，回退使用快递箱: " + deliverFallback.name);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 查找通关奖励视觉模板失败: " + e.Message);
            }

            if (_cachedVictoryRewardVisualLootBoxTemplate == null)
            {
                _cachedVictoryRewardVisualLootBoxTemplate = GetDifficultyRewardLootBoxTemplate();
            }

            return _cachedVictoryRewardVisualLootBoxTemplate;
        }

        private void StartVictoryRewardShadowCrate_LootAndRewards(int highQualityCount)
        {
            if (highQualityCount <= 0)
            {
                return;
            }

            if (_activeVictoryRewardShadowCrateController != null &&
                _activeVictoryRewardShadowCrateController.gameObject != null)
            {
                UnityEngine.Object.Destroy(_activeVictoryRewardShadowCrateController.gameObject);
            }
            _activeVictoryRewardShadowCrateController = null;

            CharacterMainControl main = TryGetMainCharacterForVictoryRewardShadowCrate_LootAndRewards();
            InteractableLootbox prefab = GetDifficultyRewardLootBoxTemplate();
            InteractableLootbox visualPrefab = GetVictoryRewardVisualLootBoxTemplate_LootAndRewards();

            if (main == null || prefab == null)
            {
                SpawnDifficultyRewardLootbox_LootAndRewards(highQualityCount);
                return;
            }

            try
            {
                GameObject controllerObject = new GameObject("BossRush_VictoryRewardShadowCrateController");
                MultiSceneCore.MoveToActiveWithScene(controllerObject, SceneManager.GetActiveScene().buildIndex);
                VictoryRewardShadowCrateController controller = controllerObject.AddComponent<VictoryRewardShadowCrateController>();

                if (!controller.Initialize(this, main, visualPrefab, highQualityCount))
                {
                    UnityEngine.Object.Destroy(controllerObject);
                    SpawnDifficultyRewardLootbox_LootAndRewards(highQualityCount);
                    return;
                }

                _activeVictoryRewardShadowCrateController = controller;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 启动通关奖励箱虚影失败，回退立即生成: " + e.Message);
                SpawnDifficultyRewardLootbox_LootAndRewards(highQualityCount);
            }
        }

        private void CompleteVictoryRewardShadowCrate_LootAndRewards()
        {
            try
            {
                if (_activeVictoryRewardShadowCrateController != null)
                {
                    _activeVictoryRewardShadowCrateController.CompleteAndLand();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 完成通关奖励箱虚影失败: " + e.Message);
            }
        }

        internal void NotifyVictoryRewardShadowCrateDisposed_LootAndRewards(VictoryRewardShadowCrateController controller)
        {
            if (controller == null || controller == _activeVictoryRewardShadowCrateController)
            {
                _activeVictoryRewardShadowCrateController = null;
            }
        }

        internal void SpawnDifficultyRewardLootboxAtWorldPosition_LootAndRewards(int highQualityCount, Vector3 worldPosition)
        {
            _difficultyRewardSpawnPositionOverrideActive = true;
            _difficultyRewardSpawnPositionOverride = worldPosition;

            try
            {
                SpawnDifficultyRewardLootbox_LootAndRewards(highQualityCount);
            }
            finally
            {
                _difficultyRewardSpawnPositionOverrideActive = false;
                _difficultyRewardSpawnPositionOverride = Vector3.zero;
            }
        }

        internal void SpawnDifficultyRewardLootboxFallback_LootAndRewards(int highQualityCount)
        {
            SpawnDifficultyRewardLootbox_LootAndRewards(highQualityCount);
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
                catch (Exception e)
                {
                    LogLootWarningLimited("SpawnDifficultyRewardLootbox_main", "读取 CharacterMainControl.Main 失败", e);
                }

                if (main == null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("SpawnDifficultyRewardLootbox_playerCharacter", "从 playerCharacter 解析玩家失败", e);
                    }
                }

                Vector3 pos = _difficultyRewardSpawnPositionOverride;
                if (!_difficultyRewardSpawnPositionOverrideActive)
                {
                    pos = demoChallengeStartPosition;
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
                }

                // 使用射线向下投射，优先只打地面层，避免命中玩家碰撞体；如果投射失败则保持轻微抬高
                try
                {
                    Vector3 rayStart = pos + Vector3.up * 1f;
                    UnityEngine.RaycastHit hit = default(UnityEngine.RaycastHit);
                    // 优先只打地面层，避免玩家或其他碰撞体干扰
                    bool hitGround = false;
                    try
                    {
                        var groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                        hitGround = UnityEngine.Physics.Raycast(rayStart, Vector3.down, out hit, 5f, groundMask, UnityEngine.QueryTriggerInteraction.Ignore);
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("SpawnDifficultyRewardLootbox_groundRaycast", "仅读取地面层的奖励箱落点射线失败，回退到全层检测", e);
                        hitGround = false;
                    }

                    if (!hitGround)
                    {
                        // 退回到旧逻辑：打所有非触发层，保证兼容性
                        if (UnityEngine.Physics.Raycast(rayStart, Vector3.down, out hit, 5f, ~0, UnityEngine.QueryTriggerInteraction.Ignore))
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
                catch (Exception e)
                {
                    LogLootWarningLimited("SpawnDifficultyRewardLootbox_adjustPos", "修正通关奖励箱落点失败，回退到抬高位置", e);
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
                catch (Exception e)
                {
                    LogLootWarningLimited("SpawnDifficultyRewardLootbox_debug1", "输出通关奖励箱调试信息失败", e);
                }

                InteractableLootbox lootbox = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                lootbox.needInspect = true;

                try
                {
                    VictoryRewardCrateHeroVisual.AttachToLootbox(lootbox, GetVictoryRewardVisualLootBoxTemplate_LootAndRewards());
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 为通关奖励箱附加英雄视觉外壳失败: " + e.Message);
                }

                try
                {
                    string boxName = lootbox != null && lootbox.gameObject != null ? lootbox.gameObject.name : "<null>";
                    DevLog("[BossRush] SpawnDifficultyRewardLootbox: 实例化 Lootbox 成功, instanceName=" + boxName +
                           ", type=" + (lootbox != null ? lootbox.GetType().FullName : "<null>") +
                           ", position=" + pos);
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("SpawnDifficultyRewardLootbox_debug2", "输出通关奖励箱实例调试信息失败", e);
                }

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
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] SpawnDifficultyRewardLootbox: 创建独立奖励箱库存失败: " + e.Message);
                }

                // 根据配置决定是否让通关奖励箱作为子弹掩体
                try
                {
                    ApplyLootBoxCoverSetting(lootbox, true);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] SpawnDifficultyRewardLootbox: 应用奖励箱掩体配置失败: " + e.Message);
                }

                try
                {
                    MultiSceneCore.MoveToActiveWithScene(lootbox.gameObject, SceneManager.GetActiveScene().buildIndex);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] SpawnDifficultyRewardLootbox: 将奖励箱移动到当前场景失败: " + e.Message);
                }

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

                        // 只保留高品质（5及以上）的品质权重，保证品质>=5
                        System.Reflection.FieldInfo qualitiesField = loaderType.GetField("qualities", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (qualitiesField != null)
                        {
                            Duckov.Utilities.RandomContainer<int> qualities = qualitiesField.GetValue(loader) as Duckov.Utilities.RandomContainer<int>;
                            if (qualities != null)
                            {
                                qualities.entries.Clear();
                                // 使用内部常量定义的高品质范围
                                int highQualityMin = LOOT_HIGH_QUALITY_MIN;
                                int highQualityMax = LOOT_HIGH_QUALITY_MAX;
                                for (int q = highQualityMin; q <= highQualityMax; q++)
                                {
                                    qualities.AddEntry(q, 1f);
                                }
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
                                        List<Duckov.Utilities.Tag> tagExclude = BuildGeneralLootExcludeTags(tagsData, true);
                                        for (int i = 0; i < allTags.Count; i++)
                                        {
                                            Duckov.Utilities.Tag t = allTags[i];
                                            if (t == null)
                                            {
                                                continue;
                                            }
                                            if (tagExclude.Contains(t))
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
                                MergeGeneralLootExcludeTags(excludeList, tagsData2);

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
                                                List<Duckov.Utilities.Tag> baseExclude = BuildGeneralLootExcludeTags(tagsData3);

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
                                                            if (IsItemBlacklisted(id))
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

                                            // 使用 GetMetaData 获取品质信息，避免 InstantiateSync 导致的卡顿
                                            // 只筛选 Quality>=5 的高品质物品（高品质物品通常价格也高，无需额外价格检查）
                                            List<int> highValueCandidates = new List<int>();

                                            try
                                            {
                                                foreach (int candidateId in idSet)
                                                {
                                                    try
                                                    {
                                                        // 使用元数据获取品质，避免实例化物品导致的性能问题
                                                        var meta = ItemAssetsCollection.GetMetaData(candidateId);
                                                        if (meta.id > 0 && meta.quality >= 5)
                                                        {
                                                            highValueCandidates.Add(candidateId);
                                                        }
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        LogLootWarningLimited("SpawnDifficultyRewardLootbox_candidateEval", "评估通关奖励候选物品元数据失败", e);
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
                                                DevLog("[BossRush] 通关奖励高品质候选物品数量=" + highValueCandidates.Count + " (Quality>=5)");
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
                        catch (Exception e)
                        {
                            LogLootWarningLimited("SpawnDifficultyRewardLootbox_buildPool", "构建通关奖励随机池失败", e);
                        }

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
                        StartCoroutine(CleanupDifficultyRewardLootboxInventory_LootAndRewards(lootbox, highQualityCount));
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
    }
}
