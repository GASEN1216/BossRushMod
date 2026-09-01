// ============================================================================
// RandomEventEffectsBridge_Spawn.cs — 随机事件桥（E3 乱入 Boss / E8 鸭群巡游 / E4 神秘商人）
// ============================================================================
// 模块职责：
//   三类生成物统一收口。全部走既有生成基建；动态官方角色的 MagicBlend 首帧竞态
//   由 Patches/Compatibility 共享层处理，本桥不拥有专属 Harmony patch。
//
// 波次隔离（本文件最危险的部分，改动前先读 tests/RandomEventsWaveIsolationGuard.py）：
//   乱入 Boss **绝不写入任何波次容器、绝不参与推波计数**。它只做三件事：
//     1. 走 SpawnEnemyCoreInternalAsync 的 Legacy 路径（options 传 null），
//        自动吃当局变异词条与掉落追踪，表现与在场敌人一致；
//     2. onCommit 里执行 AGENTS 4.5 敌对性安全网 + 恢复锚点注册 + 统一命名前缀；
//     3. 由事件自己的清理逻辑 Destroy，绝不指望通关清场帮忙
//        （清场按 characterPreset.team 判定，被 SetTeam 修正过的 middle 预设不会被清掉）。
//
// 硬约束：
//   - 全部方法 no-throw。
//   - 异步续作必须先跑 isStillValid()，不通过就地销毁半成品，绝不跨局/跨图泄漏。
//   - 商人使用稳定常量 merchantID（RandomEventsTuning.MerchantIdConstant）：
//     StockShop 是 ISaveDataProvider，merchantID 拼时间戳会让官方存档键无界膨胀。
//   - 巡游鸭复用官方 SpawnEgg.spawnCharacter，并写入 eggSpawnPreset 以获得清怪豁免；
//     它们不是敌人，不走刷怪核心（不配装、不加倍率、不做掉落追踪）。
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // ====================================================================
        // E3 Boss 乱入
        // ====================================================================

        /// <summary>
        /// 生成一只乱入 Boss。preset 由事件层从 GetFilteredEnemyPresets() 里挑好后传入
        /// （事件需要 displayName 播报，避免桥内再挑一次导致名字与实体对不上）。
        /// onSpawned 只在 commit 成功后回调，供事件登记自己的清理列表。
        /// </summary>
        internal void SpawnRandomEventIntruderBoss(
            EnemyPresetInfo preset,
            Vector3 position,
            Func<bool> isStillValid,
            Action<CharacterMainControl> onSpawned,
            Action onFailed)
        {
            try
            {
                if (preset == null)
                {
                    if (onFailed != null) onFailed();
                    return;
                }
                SpawnRandomEventIntruderBossAsync(preset, position, isStillValid, onSpawned, onFailed).Forget();
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 乱入 Boss 调度失败: " + e.Message);
                if (onFailed != null)
                {
                    try { onFailed(); } catch (Exception) { }
                }
            }
        }

        private async UniTaskVoid SpawnRandomEventIntruderBossAsync(
            EnemyPresetInfo preset,
            Vector3 position,
            Func<bool> isStillValid,
            Action<CharacterMainControl> onSpawned,
            Action onFailed)
        {
            try
            {
                // 标准 BossRush 不经过 Mode D/E/Zombie 的预设缓存初始化路径。
                // 事件目录拿到的是稳定 nameKey，SpawnCore 随后仍要从该缓存解析官方 preset；
                // 未在这里准备会出现“目录有条目但实体查找失败”的实机空转。
                EnsureCharacterPresetsCacheReady();

                // options 一律不传（= null = 完整 Legacy 行为）：自动吃当局变异词条与
                // Boss 掉落追踪，与在场敌人口径一致。
                EnemySpawnCoreResult result = await SpawnEnemyCoreInternalAsync(
                    preset,
                    position,
                    true,
                    isStillValid,
                    1,
                    skipDragonDescendant: false,
                    skipDragonKing: false,
                    deferActivationUntilNextFrame: true,
                    onCommit: (spawnContext) => ConfigureRandomEventIntruderBoss(spawnContext, position));

                if (result == null || !result.success || result.context == null || result.context.character == null)
                {
                    string reason = result != null ? result.failureReason : "结果为空";
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 乱入 Boss 生成失败: " + reason);
                    if (onFailed != null)
                    {
                        try { onFailed(); } catch (Exception) { }
                    }
                    return;
                }

                CharacterMainControl boss = result.context.character;

                // 生成完成时事件可能已经结束（到时 / 切图 / 关开关），就地销毁半成品。
                if (isStillValid != null && !isStillValid())
                {
                    DespawnRandomEventIntruderBoss(boss);
                    return;
                }

                if (onSpawned != null)
                {
                    try { onSpawned(boss); }
                    catch (Exception e)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 乱入 Boss 登记失败: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 乱入 Boss 生成异常: " + e.Message);
                if (onFailed != null)
                {
                    try { onFailed(); } catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// 乱入 Boss 的 commit 回调：敌对性安全网（AGENTS 4.5）+ 命名前缀 + 恢复锚点。
        /// 这里**只做这三件事**，绝不写入任何波次状态。
        /// </summary>
        private bool ConfigureRandomEventIntruderBoss(EnemySpawnContext spawnContext, Vector3 position)
        {
            if (spawnContext == null || spawnContext.character == null)
            {
                return false;
            }

            CharacterMainControl character = spawnContext.character;

            // ── AGENTS 4.5 敌对性安全网：官方 preset 可能是中立队伍，
            //    不修正就会出现「不攻击、不可击杀」的假 Boss。 ──
            try
            {
                if (PetNestCompanionAgent.IsCompanionCharacter(character))
                {
                    DevLog(RandomEventsTuning.LogPrefix + "敌对性安全网豁免遗种巢随从");
                }
                else if (!Team.IsEnemy(Teams.player, character.Team))
                {
                    DevLog(RandomEventsTuning.LogPrefix + "检测到非敌对乱入 Boss (team=" + character.Team
                        + ")，强制设为 Teams.wolf");
                    character.SetTeam(Teams.wolf);
                }
            }
            catch (Exception teamEx)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 强制乱入 Boss 阵营失败: " + teamEx.Message);
            }

            try
            {
                string label = spawnContext.preset != null ? spawnContext.preset.displayName : "Boss";
                // 命名前缀固定 RndEvt_Intruder_：调试与实机排查靠它一眼区分乱入实体
                character.gameObject.name = "RndEvt_Intruder_" + label;
            }
            catch (Exception) { }

            try
            {
                RegisterEnemyRecoveryAnchor(character, position);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 乱入 Boss 恢复锚点注册失败: " + e.Message);
            }

            return true;
        }

        /// <summary>
        /// 销毁乱入 Boss（含恢复监控退订）。幂等，null 安全。
        /// ⚠️ 必须自己销毁：通关清场按 characterPreset.team 判定，
        /// 被运行时 SetTeam 修正过但 preset.team 仍是中立的 Boss 不会被它清掉。
        /// </summary>
        internal void DespawnRandomEventIntruderBoss(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return;
            }

            try
            {
                UnregisterEnemyRecovery(boss);
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 乱入 Boss 恢复监控退订失败: " + e.Message);
            }

            try
            {
                if (boss.gameObject != null)
                {
                    UnityEngine.Object.Destroy(boss.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 销毁乱入 Boss 失败: " + e.Message);
            }
        }

        // ====================================================================
        // E8 鸭群巡游
        // ====================================================================

        /// <summary>
        /// 生成一队下蛋鸭。复用官方 SpawnEgg.spawnCharacter，并写入 eggSpawnPreset
        /// 使它们获得与玩家下蛋同款的清怪豁免。找不到 SpawnEgg 时静默跳过。
        /// </summary>
        internal void SpawnRandomEventParadeDucks(
            Vector3 startPos,
            Vector3 forward,
            int count,
            Func<bool> isStillValid,
            Action<CharacterMainControl> onSpawned,
            Action<int, int> onCompleted)
        {
            try
            {
                SpawnRandomEventParadeDucksAsync(
                    startPos, forward, count, isStillValid, onSpawned, onCompleted).Forget();
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 鸭群巡游调度失败: " + e.Message);
                InvokeRandomEventSpawnCompletion(onCompleted, Mathf.Max(0, count), 0, "鸭群巡游");
            }
        }

        private async UniTaskVoid SpawnRandomEventParadeDucksAsync(
            Vector3 startPos,
            Vector3 forward,
            int count,
            Func<bool> isStillValid,
            Action<CharacterMainControl> onSpawned,
            Action<int, int> onCompleted)
        {
            int requested = Mathf.Max(0, count);
            int spawned = 0;
            try
            {
                SpawnEgg behavior = null;
                try { behavior = cachedSpawnEggBehavior; } catch (Exception) { }

                if (behavior == null)
                {
                    try
                    {
                        SpawnEgg[] all = Resources.FindObjectsOfTypeAll<SpawnEgg>();
                        if (all != null && all.Length > 0)
                        {
                            behavior = all[0];
                            cachedSpawnEggBehavior = behavior;
                        }
                    }
                    catch (Exception) { }
                }

                if (behavior == null || behavior.spawnCharacter == null)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "未找到官方 SpawnEgg 预设，鸭群巡游静默跳过");
                    return;
                }

                // 清怪逻辑靠 characterPreset == eggSpawnPreset 做豁免，必须写。
                // 不还原：它本来就是全局缓存，音频 hooks 的 reset 路径统一清空。
                try { eggSpawnPreset = behavior.spawnCharacter; } catch (Exception) { }

                int sceneBuildIndex = SceneManager.GetActiveScene().buildIndex;
                Vector3 dir = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;

                for (int i = 0; i < count; i++)
                {
                    if (isStillValid != null && !isStillValid())
                    {
                        return;
                    }

                    Vector3 p = SpawnPositionHelper.SnapToGround(
                        startPos - dir * (i * RandomEventsTuning.DuckParadeSpacing));

                    CharacterMainControl duck = null;
                    try
                    {
                        duck = await behavior.spawnCharacter.CreateCharacterAsync(p, dir, sceneBuildIndex, null, false);
                    }
                    catch (Exception e)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 生成巡游鸭失败: " + e.Message);
                    }

                    if (duck == null)
                    {
                        continue;
                    }

                    if ((isStillValid != null && !isStillValid()) ||
                        SceneManager.GetActiveScene().buildIndex != sceneBuildIndex)
                    {
                        try
                        {
                            if (duck.gameObject != null) UnityEngine.Object.Destroy(duck.gameObject);
                        }
                        catch (Exception) { }
                        return;
                    }

                    try
                    {
                        duck.gameObject.name = "RndEvt_ParadeDuck";
                        duck.gameObject.SetActive(true);
                    }
                    catch (Exception) { }

                    if (onSpawned != null)
                    {
                        try { onSpawned(duck); }
                        catch (Exception e)
                        {
                            DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 巡游鸭登记失败: " + e.Message);
                        }
                    }

                    spawned++;

                    await UniTask.Yield();
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 鸭群巡游生成异常: " + e.Message);
            }
            finally
            {
                InvokeRandomEventSpawnCompletion(onCompleted, requested, spawned, "鸭群巡游");
            }
        }

        private static void InvokeRandomEventSpawnCompletion(
            Action<int, int> callback,
            int requested,
            int spawned,
            string label)
        {
            if (callback == null) return;
            try { callback(requested, spawned); }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] " + label + "完成回调失败: " + e.Message);
            }
        }

        // ====================================================================
        // E4 神秘商人路过
        // ====================================================================

        /// <summary>商人预设是否可解析。事件的 CanTrigger 用它做前置判定。</summary>
        internal bool HasRandomEventMerchantPreset()
        {
            try
            {
                return GetModeEMerchantPreset() != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 生成限时神秘商人（角色 + 单一 StockShop + 交互选项）。
        /// 角色创建是异步的，因此结果经 onSpawned 回调回吐（不接壳经济、不接 Harmony、不做分类）。
        /// </summary>
        internal void SpawnRandomEventMerchant(
            Vector3 position,
            Func<bool> isStillValid,
            Action<CharacterMainControl, StockShop> onSpawned,
            Action onFailed)
        {
            try
            {
                SpawnRandomEventMerchantAsync(position, isStillValid, onSpawned, onFailed).Forget();
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 神秘商人调度失败: " + e.Message);
                if (onFailed != null)
                {
                    try { onFailed(); } catch (Exception) { }
                }
            }
        }

        private async UniTaskVoid SpawnRandomEventMerchantAsync(
            Vector3 position,
            Func<bool> isStillValid,
            Action<CharacterMainControl, StockShop> onSpawned,
            Action onFailed)
        {
            CharacterMainControl character = null;
            try
            {
                CharacterRandomPreset preset = GetModeEMerchantPreset();
                if (preset == null)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 未找到商人预设，神秘商人取消");
                    if (onFailed != null) onFailed();
                    return;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                Vector3 facing = player != null ? -player.transform.forward : Vector3.forward;
                int sceneBuildIndex = SceneManager.GetActiveScene().buildIndex;

                character = await preset.CreateCharacterAsync(position, facing, sceneBuildIndex, null, false);
                if (character == null)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人角色创建失败");
                    if (onFailed != null) onFailed();
                    return;
                }

                if ((isStillValid != null && !isStillValid()) ||
                    SceneManager.GetActiveScene().buildIndex != sceneBuildIndex)
                {
                    DespawnRandomEventMerchant(character, null);
                    return;
                }

                try { character.SetTeam(Teams.player); } catch (Exception) { }
                try { SetModeEMerchantHealth(character); } catch (Exception) { }
                try { character.gameObject.name = "RndEvt_Merchant"; } catch (Exception) { }

                StockShop shop = BuildRandomEventMerchantShop(character.gameObject);
                if (shop == null)
                {
                    DespawnRandomEventMerchant(character, null);
                    if (onFailed != null) onFailed();
                    return;
                }

                try
                {
                    MultiSceneCore.MoveToActiveWithScene(character.gameObject, sceneBuildIndex);
                }
                catch (Exception) { }

                if (onSpawned != null)
                {
                    try { onSpawned(character, shop); }
                    catch (Exception e)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人登记失败: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 神秘商人生成异常: " + e.Message);
                DespawnRandomEventMerchant(character, null);
                if (onFailed != null)
                {
                    try { onFailed(); } catch (Exception) { }
                }
            }
        }

        /// <summary>
        /// 在商人 NPC 上挂一个 StockShop + 交互选项。
        /// 商品 = 弹药全量 + 医疗全量 + 1 件随机高品质，统一 1.5 倍溢价。
        /// tag 来源复用 GetModeEMerchantCategories，禁止凭记忆写 tag 字符串。
        /// </summary>
        private StockShop BuildRandomEventMerchantShop(GameObject npcGo)
        {
            try
            {
                if (npcGo == null)
                {
                    return null;
                }

                InteractableBase mainInteract = npcGo.GetComponentInChildren<InteractableBase>(true);
                if (mainInteract == null)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人身上没有 InteractableBase，放弃注入商店");
                    return null;
                }

                // 销毁原版 StockShop，避免原始交易选项混进来
                try
                {
                    StockShop origShop = npcGo.GetComponentInChildren<StockShop>(true);
                    if (origShop != null)
                    {
                        UnityEngine.Object.Destroy(origShop);
                    }
                }
                catch (Exception) { }

                GameObject shopObj = new GameObject("BossRushEventShop");
                // StockShop.Awake 会立刻按 merchantID 加载并订阅存档；先保持 inactive，
                // 等身份、刷新周期与条目全部写完后再激活，不能让 Awake 看见默认字段。
                shopObj.SetActive(false);
                shopObj.transform.SetParent(mainInteract.transform, false);
                shopObj.transform.localPosition = Vector3.zero;
                shopObj.transform.localRotation = Quaternion.identity;
                shopObj.transform.localScale = Vector3.one;

                StockShop shop = shopObj.AddComponent<StockShop>();

                // StockShop.Awake 会先查官方 MerchantDatabase。先给它一个存在的引导 ID，
                // 激活完成后在同一帧 Start 之前换回稳定 Mod ID，避免无意义的官方错误日志。
                try
                {
                    System.Reflection.FieldInfo merchantField = BossRushEagerReflectionCache.StockShop_MerchantID;
                    System.Reflection.FieldInfo accountField = BossRushEagerReflectionCache.StockShop_AccountAvaliable;
                    if (merchantField == null) throw new InvalidOperationException("StockShop.merchantID field unavailable");
                    merchantField.SetValue(shop, RandomEventsTuning.MerchantAwakeBootstrapId);
                    if (accountField != null) accountField.SetValue(shop, true);
                    if (BossRushEagerReflectionCache.StockShop_RefreshAfterTimeSpan != null)
                    {
                        BossRushEagerReflectionCache.StockShop_RefreshAfterTimeSpan.SetValue(
                            shop, TimeSpan.FromDays(1d).Ticks);
                    }
                    if (BossRushEagerReflectionCache.StockShop_RefreshStockOnStart != null)
                    {
                        BossRushEagerReflectionCache.StockShop_RefreshStockOnStart.SetValue(shop, false);
                    }
                    if (BossRushEagerReflectionCache.StockShop_LastTimeRefreshedStock != null)
                    {
                        BossRushEagerReflectionCache.StockShop_LastTimeRefreshedStock.SetValue(
                            shop, DateTime.UtcNow.ToBinary());
                    }
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人商店身份设置失败: " + e.Message);
                    UnityEngine.Object.Destroy(shopObj);
                    return null;
                }

                RandomEventMerchantShopInteractable interact =
                    shopObj.AddComponent<RandomEventMerchantShopInteractable>();
                interact.Setup(shop, RandomEventsTuning.LocalizationPrefix + "MerchantShop");

                try
                {
                    mainInteract.interactableGroup = true;
                    System.Reflection.FieldInfo groupField =
                        BossRushEagerReflectionCache.InteractableBase_OtherInterablesInGroup;
                    if (groupField != null)
                    {
                        List<InteractableBase> groupList = groupField.GetValue(mainInteract) as List<InteractableBase>;
                        if (groupList == null)
                        {
                            groupList = new List<InteractableBase>();
                            groupField.SetValue(mainInteract, groupList);
                        }
                        for (int i = groupList.Count - 1; i >= 0; i--)
                        {
                            if (groupList[i] == null) groupList.RemoveAt(i);
                        }
                        groupList.Add(interact);
                    }
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人交互组注入失败: " + e.Message);
                }

                shopObj.SetActive(true);
                // 激活已同步执行 StockShop.Awake；Unity 的 Start 要到本帧稍后才执行。
                // 在这个窗口恢复稳定 ID，并用事件库存覆盖引导商人的官方条目。
                try
                {
                    System.Reflection.FieldInfo merchantField = BossRushEagerReflectionCache.StockShop_MerchantID;
                    if (merchantField == null)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人稳定身份字段不可用，放弃生成");
                        UnityEngine.Object.Destroy(shopObj);
                        return null;
                    }
                    merchantField.SetValue(shop, RandomEventsTuning.MerchantIdConstant);

                    shop.entries = new List<StockShop.Entry>();
                    int filled = FillRandomEventMerchantEntries(shop);
                    if (filled == 0)
                    {
                        DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人商店无可售商品，放弃生成");
                        UnityEngine.Object.Destroy(shopObj);
                        return null;
                    }

                    if (shop.entries != null)
                    {
                        for (int i = 0; i < shop.entries.Count; i++)
                        {
                            StockShop.Entry entry = shop.entries[i];
                            if (entry == null) continue;
                            entry.CurrentStock = entry.MaxStock;
                            entry.Show = true;
                        }
                    }
                    if (BossRushEagerReflectionCache.StockShop_LastTimeRefreshedStock != null)
                    {
                        BossRushEagerReflectionCache.StockShop_LastTimeRefreshedStock.SetValue(
                            shop, DateTime.UtcNow.ToBinary());
                    }
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人初始库存复位失败: " + e.Message);
                    UnityEngine.Object.Destroy(shopObj);
                    return null;
                }
                return shop;
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[ERROR] 构建商人商店失败: " + e.Message);
                return null;
            }
        }

        /// <summary>填充商品，返回实际写入的条目数。</summary>
        private int FillRandomEventMerchantEntries(StockShop shop)
        {
            int added = 0;
            try
            {
                Duckov.Utilities.GameplayDataSettings.TagsData tagsData = null;
                try { tagsData = Duckov.Utilities.GameplayDataSettings.Tags; } catch (Exception) { }
                if (tagsData == null)
                {
                    return 0;
                }

                Duckov.Utilities.Tag[] emptyExclude = new Duckov.Utilities.Tag[0];
                List<Tuple<List<Duckov.Utilities.Tag>, string, string>> categories =
                    GetModeEMerchantCategories(tagsData);
                if (categories == null)
                {
                    return 0;
                }

                HashSet<int> written = new HashSet<int>();

                for (int i = 0; i < categories.Count; i++)
                {
                    Tuple<List<Duckov.Utilities.Tag>, string, string> cat = categories[i];
                    if (cat == null)
                    {
                        continue;
                    }
                    string suffix = cat.Item3;
                    if (suffix != "Bullet" && suffix != "Medical")
                    {
                        continue;
                    }

                    List<int> ids = ModeESearchItemsMultiTag(cat.Item1, emptyExclude);
                    if (ids == null)
                    {
                        continue;
                    }
                    for (int k = 0; k < ids.Count; k++)
                    {
                        if (AddRandomEventMerchantEntry(shop, ids[k], 99, written))
                        {
                            added++;
                        }
                    }
                }

                // 1 件随机高品质彩头
                try
                {
                    HashSet<int> candidates = BuildGeneralBossLootCandidateIdSet();
                    if (candidates != null && candidates.Count > 0)
                    {
                        List<int> highQuality = new List<int>();
                        foreach (int id in candidates)
                        {
                            if (written.Contains(id) || IsItemBlacklisted(id))
                            {
                                continue;
                            }
                            if (ItemAssetsCollection.GetMetaData(id).quality >= RandomEventsTuning.MerchantHighQualityMin)
                            {
                                highQuality.Add(id);
                            }
                        }

                        for (int n = 0; n < RandomEventsTuning.MerchantRandomHighQualityCount && highQuality.Count > 0; n++)
                        {
                            int pick = UnityEngine.Random.Range(0, highQuality.Count);
                            if (AddRandomEventMerchantEntry(shop, highQuality[pick], 1, written))
                            {
                                added++;
                            }
                            highQuality.RemoveAt(pick);
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人高品质彩头挑选失败: " + e.Message);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 商人商品填充失败: " + e.Message);
            }

            return added;
        }

        private bool AddRandomEventMerchantEntry(
            StockShop shop, int typeId, int maxStock, HashSet<int> written)
        {
            try
            {
                if (shop == null || shop.entries == null || written.Contains(typeId) || IsItemBlacklisted(typeId))
                {
                    return false;
                }

                StockShopDatabase.ItemEntry raw = new StockShopDatabase.ItemEntry();
                raw.typeID = typeId;
                raw.maxStock = maxStock > 0 ? maxStock : 1;
                raw.forceUnlock = true;
                raw.priceFactor = RandomEventsTuning.MerchantPriceFactor;
                raw.possibility = 1f;
                raw.lockInDemo = false;

                StockShop.Entry entry = new StockShop.Entry(raw);
                entry.CurrentStock = entry.MaxStock;
                entry.Show = true;
                shop.entries.Add(entry);
                written.Add(typeId);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 收摊：先关正开着的商店 UI（顺序不能反，否则玩家会盯着一个已销毁的目标），
        /// 再销毁 StockShop（其 OnDestroy 会自行退订官方存档收集回调），最后销毁角色。
        /// 幂等，null 安全。
        /// </summary>
        internal void DespawnRandomEventMerchant(CharacterMainControl merchant, StockShop shop)
        {
            try
            {
                if (shop != null && StockShopView.Instance != null && StockShopView.Instance.Target == shop)
                {
                    try { StockShopView.Instance.Close(); }
                    catch (Exception)
                    {
                        try { StockShopView.Instance.gameObject.SetActive(false); } catch (Exception) { }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 关闭商人商店 UI 失败: " + e.Message);
            }

            try
            {
                if (shop != null && shop.gameObject != null)
                {
                    UnityEngine.Object.Destroy(shop.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 销毁商人商店失败: " + e.Message);
            }

            try
            {
                if (merchant != null && merchant.gameObject != null)
                {
                    UnityEngine.Object.Destroy(merchant.gameObject);
                }
            }
            catch (Exception e)
            {
                DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 销毁商人失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 随机事件商人的商店交互选项。照 ModeEShopInteractable 精简：
    /// 不接壳经济、不接 Harmony、不做分类，只负责「打开这一个 StockShop」。
    /// </summary>
    internal sealed class RandomEventMerchantShopInteractable : InteractableBase
    {
        private StockShop _shop;
        private string _displayNameKey;

        internal void Setup(StockShop shop, string displayNameKey)
        {
            _shop = shop;
            _displayNameKey = displayNameKey;
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = displayNameKey;
            }
            catch (Exception) { }
        }

        protected override void Awake()
        {
            try
            {
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayNameKey))
                {
                    this._overrideInteractNameKey = _displayNameKey;
                }
            }
            catch (Exception) { }

            try
            {
                NPCInteractionGroupHelper.GetOrCreateGroupList(this, "[RandomEventMerchantShop]");
            }
            catch (Exception) { }

            try { base.Awake(); } catch (Exception) { }

            try
            {
                // 作为子交互选项不需要独立碰撞检测
                this.interactCollider = GetComponent<Collider>();
                if (this.interactCollider != null)
                {
                    this.interactCollider.enabled = false;
                }
            }
            catch (Exception) { }

            try { this.MarkerActive = false; } catch (Exception) { }
        }

        protected override void Start()
        {
            try { base.Start(); } catch (Exception) { }
            try
            {
                // base.Start 可能覆盖名称，这里补一次
                this.overrideInteractName = true;
                if (!string.IsNullOrEmpty(_displayNameKey))
                {
                    this._overrideInteractNameKey = _displayNameKey;
                }
            }
            catch (Exception) { }
        }

        protected override bool IsInteractable()
        {
            return _shop != null;
        }

        protected override void OnTimeOut()
        {
            try
            {
                if (_shop == null)
                {
                    return;
                }
                _shop.ShowUI();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "[WARNING] 打开商人商店失败: " + e.Message);
            }
        }
    }
}
