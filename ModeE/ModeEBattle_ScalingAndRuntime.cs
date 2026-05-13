using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 敌人死亡与动态缩放

        private sealed class ModeEEnemyScalingState
        {
            public int deathBaseline;
            public Modifier hp;
            public Modifier gunDmg;
            public Modifier meleeDmg;
        }

        private sealed class ModeEPlayerScalingState
        {
            public Modifier hp;
            public Modifier gunDmg;
            public Modifier meleeDmg;
        }

        private readonly Dictionary<CharacterMainControl, ModeEEnemyScalingState> modeEEnemyScalingStates
            = new Dictionary<CharacterMainControl, ModeEEnemyScalingState>();

        private int modeEPlayerLastHitKillCount = 0;
        private ModeEPlayerScalingState modeEPlayerScalingState = null;
        private Item modeEPlayerScalingItem = null;

        /// <summary>缓存死亡事件句柄，避免对象复用或重复注册导致 UnityEvent 持续膨胀。</summary>
        private readonly Dictionary<CharacterMainControl, UnityAction<DamageInfo>> modeEEnemyDeathHandlers
            = new Dictionary<CharacterMainControl, UnityAction<DamageInfo>>();

        /// <summary>缓存掉落拦截句柄，确保 Mode E 结束或对象复用时可以对称取消订阅。</summary>
        private readonly Dictionary<CharacterMainControl, Action<DamageInfo>> modeEEnemyLootHandlers
            = new Dictionary<CharacterMainControl, Action<DamageInfo>>();

        /// <summary>需要延迟批量缩放的阵营集合（死亡时记录，定时批量应用）</summary>
        private readonly HashSet<Teams> modeEPendingScalingFactions = new HashSet<Teams>();

        /// <summary>缩放批量应用计时器</summary>
        private float modeEScalingBatchTimer = 0f;

        /// <summary>缩放批量应用间隔（秒）- 每 5 秒统一应用一次，避免连锁死亡时的帧率尖刺</summary>
        private const float MODE_E_SCALING_BATCH_INTERVAL = 5f;

        /// <summary>
        /// 注册敌人死亡事件，触发按阵营的动态缩放
        /// </summary>
        private void RegisterModeEEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                if (enemy == null)
                {
                    return;
                }

                UnregisterModeEEnemyDeath(enemy);

                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    CharacterMainControl capturedEnemy = enemy;
                    UnityAction<DamageInfo> handler = null;
                    handler = (dmgInfo) =>
                    {
                        UnregisterModeEEnemyDeath(capturedEnemy);
                        OnModeEEnemyDeath(capturedEnemy, dmgInfo);
                    };
                    modeEEnemyDeathHandlers[enemy] = handler;
                    health.OnDeadEvent.AddListener(handler);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] RegisterModeEEnemyDeath 失败: " + e.Message);
            }
        }

        private void UnregisterModeEEnemyDeath(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            UnityAction<DamageInfo> handler;
            if (!modeEEnemyDeathHandlers.TryGetValue(enemy, out handler))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    Health health = enemy.GetComponent<Health>();
                    if (health != null)
                    {
                        health.OnDeadEvent.RemoveListener(handler);
                    }
                }
            }
            catch { }

            modeEEnemyDeathHandlers.Remove(enemy);
        }

        private void RegisterModeEEnemyLootHandler(CharacterMainControl enemy, Teams faction)
        {
            try
            {
                if (enemy == null)
                {
                    return;
                }

                UnregisterModeEEnemyLootHandler(enemy);

                CharacterMainControl capturedEnemy = enemy;
                Teams capturedFaction = faction;
                Action<DamageInfo> handler = (dmgInfo) =>
                {
                    if (!modeEActive || capturedFaction != modeEPlayerFaction || capturedEnemy == null)
                    {
                        return;
                    }

                    capturedEnemy.dropBoxOnDead = false;
                    DevLog("[ModeE] 同阵营Boss死亡，阻止掉落战利品箱子: " + capturedEnemy.gameObject.name);
                };

                modeEEnemyLootHandlers[enemy] = handler;
                enemy.BeforeCharacterSpawnLootOnDead += handler;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] RegisterModeEEnemyLootHandler 失败: " + e.Message);
            }
        }

        private void UnregisterModeEEnemyLootHandler(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            Action<DamageInfo> handler;
            if (!modeEEnemyLootHandlers.TryGetValue(enemy, out handler))
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    enemy.BeforeCharacterSpawnLootOnDead -= handler;
                }
            }
            catch { }

            modeEEnemyLootHandlers.Remove(enemy);
        }

        private Modifier AddModeEScalingModifier(CharacterMainControl character, string statName, float percent)
        {
            if (character == null || percent <= 0f) return null;
            Item characterItem = character.CharacterItem;
            if (characterItem == null) return null;

            Stat stat = characterItem.GetStat(statName);
            if (stat == null) return null;

            Modifier mod = new Modifier(ModifierType.Add, stat.BaseValue * percent, this);
            stat.AddModifier(mod);

            return mod;
        }

        private float GetModeEMaxHealthValue(CharacterMainControl character)
        {
            if (character == null)
            {
                return -1f;
            }

            Item characterItem = character.CharacterItem;
            if (characterItem == null)
            {
                return -1f;
            }

            Stat maxHealthStat = characterItem.GetStat("MaxHealth");
            return maxHealthStat != null ? maxHealthStat.Value : -1f;
        }

        private void SyncModeEHealthAfterMaxHealthRefresh(CharacterMainControl character, float oldMaxHealth)
        {
            if (character == null || character.Health == null)
            {
                return;
            }

            Item characterItem = character.CharacterItem;
            if (characterItem == null)
            {
                return;
            }

            Stat maxHealthStat = characterItem.GetStat("MaxHealth");
            if (maxHealthStat == null)
            {
                return;
            }

            float newMaxHealth = maxHealthStat.Value;
            float targetHealth = character.Health.CurrentHealth;
            if (oldMaxHealth > 0f)
            {
                float healthDelta = newMaxHealth - oldMaxHealth;
                if (healthDelta > 0f)
                {
                    targetHealth += healthDelta;
                }
            }

            character.Health.CurrentHealth = Mathf.Min(targetHealth, newMaxHealth);
        }

        private void RemoveModeEPlayerScalingModifiers()
        {
            if (modeEPlayerScalingState == null)
            {
                modeEPlayerScalingItem = null;
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Item characterItem = player != null ? player.CharacterItem : null;
            if (characterItem == null)
            {
                characterItem = modeEPlayerScalingItem;
            }

            if (characterItem == null)
            {
                // 角色对象可能临时缺失，保留句柄等待后续时机重试清理。
                return;
            }

            Stat hpStat = characterItem.GetStat("MaxHealth");
            if (hpStat != null && modeEPlayerScalingState.hp != null) hpStat.RemoveModifier(modeEPlayerScalingState.hp);

            Stat gunStat = characterItem.GetStat("GunDamageMultiplier");
            if (gunStat != null && modeEPlayerScalingState.gunDmg != null) gunStat.RemoveModifier(modeEPlayerScalingState.gunDmg);

            Stat meleeStat = characterItem.GetStat("MeleeDamageMultiplier");
            if (meleeStat != null && modeEPlayerScalingState.meleeDmg != null) meleeStat.RemoveModifier(modeEPlayerScalingState.meleeDmg);

            modeEPlayerScalingState = null;
            modeEPlayerScalingItem = null;
        }

        private void RefreshModeEPlayerScaling()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                RemoveModeEPlayerScalingModifiers();
                return;
            }

            float oldMaxHealth = GetModeEMaxHealthValue(player);
            RemoveModeEPlayerScalingModifiers();

            float hpPercent = modeEPlayerLastHitKillCount * 0.001f;
            float damagePercent = modeEPlayerLastHitKillCount * 0.001f;

            if (modeEPlayerScalingState == null)
            {
                modeEPlayerScalingState = new ModeEPlayerScalingState();
            }

            modeEPlayerScalingItem = player.CharacterItem;
            modeEPlayerScalingState.hp = AddModeEScalingModifier(player, "MaxHealth", hpPercent);
            modeEPlayerScalingState.gunDmg = AddModeEScalingModifier(player, "GunDamageMultiplier", damagePercent);
            modeEPlayerScalingState.meleeDmg = AddModeEScalingModifier(player, "MeleeDamageMultiplier", damagePercent);
            SyncModeEHealthAfterMaxHealthRefresh(player, oldMaxHealth);
        }

        private void ShowModeEPlayerGrowthBubble()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] ShowModeEPlayerGrowthBubble: 玩家或 transform 为 null");
                    return;
                }

                float totalBonusPercent = modeEPlayerLastHitKillCount * 0.1f;
                string bubbleText = "生命/伤害+0.1%，总加成" +
                    totalBonusPercent.ToString("F1", CultureInfo.InvariantCulture) + "%";

                DialogueBubblesManager.Show(bubbleText, player.transform, 2.5f, false, false, -1f, 3f);
                DevLog("[ModeE] 显示玩家成长气泡: " + bubbleText);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ShowModeEPlayerGrowthBubble 失败: " + e.Message);
            }
        }

        private void RemoveModeEScalingModifiers(CharacterMainControl enemy)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            ModeEEnemyScalingState scalingState = null;
            if (!modeEEnemyScalingStates.TryGetValue(enemy, out scalingState) || scalingState == null)
            {
                return;
            }

            try
            {
                if (!(enemy == null))
                {
                    Item characterItem = enemy.CharacterItem;
                    if (characterItem != null)
                    {
                        try
                        {
                            Stat oldHpStat = characterItem.GetStat("MaxHealth");
                            if (oldHpStat != null && scalingState.hp != null) oldHpStat.RemoveModifier(scalingState.hp);
                        }
                        catch { }

                        try
                        {
                            Stat oldGunStat = characterItem.GetStat("GunDamageMultiplier");
                            if (oldGunStat != null && scalingState.gunDmg != null) oldGunStat.RemoveModifier(scalingState.gunDmg);
                        }
                        catch { }

                        try
                        {
                            Stat oldMeleeStat = characterItem.GetStat("MeleeDamageMultiplier");
                            if (oldMeleeStat != null && scalingState.meleeDmg != null) oldMeleeStat.RemoveModifier(scalingState.meleeDmg);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            scalingState.hp = null;
            scalingState.gunDmg = null;
            scalingState.meleeDmg = null;
        }

        private void CleanupModeEEnemyRuntimeState(CharacterMainControl enemy, Teams? faction = null)
        {
            if (object.ReferenceEquals(enemy, null))
            {
                return;
            }

            UnregisterModeEEnemyDeath(enemy);
            UnregisterModeEEnemyLootHandler(enemy);
            UnregisterModeEEnemyFromSpawnerRoot(enemy);
            UnregisterEnemyRecovery(enemy);
            modeEPendingAggroTraceDistance.Remove(enemy);
            RemoveModeEScalingModifiers(enemy);
            modeEEnemyScalingStates.Remove(enemy);
            UntrackModeEAliveEnemy(enemy, faction);
        }

        /// <summary>
        /// Mode E 敌人死亡回调
        /// [性能优化] 死亡时只记录计数和标记脏阵营，不立即遍历应用缩放
        /// 缩放由 ModeEScalingBatchUpdate() 定时批量执行
        /// </summary>
        private void OnModeEEnemyDeath(CharacterMainControl enemy, DamageInfo damageInfo)
        {
            try
            {
                if (!modeEActive) return;

                // 获取死亡敌人的阵营
                Teams enemyFaction = enemy.Team;

                CharacterMainControl killer = null;
                try { killer = damageInfo.fromCharacter; } catch { }

                bool killedByPlayer = killer == CharacterMainControl.Main;
                bool killedEnemyBossForPlayer = killedByPlayer && enemyFaction != modeEPlayerFaction;
                if (killedEnemyBossForPlayer)
                {
                    modeEPlayerLastHitKillCount++;
                    RefreshModeEPlayerScaling();
                    ShowModeEPlayerGrowthBubble();
                }

                bool hasDeathPos = false;
                Vector3 deathPosition = Vector3.zero;
                try
                {
                    if (enemy != null && enemy.transform != null)
                    {
                        deathPosition = enemy.transform.position;
                        hasDeathPos = true;
                    }
                }
                catch {}

                // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
                try
                {
                    if (enemy.characterPreset != null)
                    {
                        UnityEngine.Object.Destroy(enemy.characterPreset);
                    }
                }
                catch { }

                // 从所有运行时注册表移除，避免事件/列表/虚拟 spawner root 持续膨胀
                CleanupModeEEnemyRuntimeState(enemy, enemyFaction);

                // 递增该阵营死亡计数
                if (modeEFactionDeathCount.ContainsKey(enemyFaction))
                {
                    modeEFactionDeathCount[enemyFaction]++;
                }
                else
                {
                    modeEFactionDeathCount[enemyFaction] = 1;
                }

                int deathCount = modeEFactionDeathCount[enemyFaction];
                DevLog("[ModeE] 阵营 " + enemyFaction + " 单位阵亡，累计死亡: " + deathCount);

                // 标记该阵营需要延迟缩放（不立即执行，等批量定时器触发）
                modeEPendingScalingFactions.Add(enemyFaction);

                // 累计击杀计数，每10次自动发放挑衅烟雾弹
                CheckRespawnItemAutoGrant();
                RegisterModeEFBossDeathForSweepToken();

                // 敌方阵营 Boss 保留原掉落内容，但要补挂 BossRush 箱子交互与追踪标记。
                if (enemyFaction != modeEPlayerFaction)
                {
                    try
                    {
                        if (hasDeathPos)
                        {
                            StartCoroutine(BossRushLootboxUtility.DecorateLootboxesNearPosition(this, deathPosition, true));
                        }
                    }
                    catch {}
                }

                FinalizeBossRushLootboxPathTracking(enemy);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] OnModeEEnemyDeath 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode E 缩放批量更新（由 Update 定时调用）
        /// 将累积的阵营死亡缩放一次性批量应用，避免每次死亡都遍历全列表
        /// </summary>
        public void ModeEScalingBatchUpdate()
        {
            if (!modeEActive) return;

            modeEScalingBatchTimer += Time.deltaTime;
            if (modeEScalingBatchTimer < MODE_E_SCALING_BATCH_INTERVAL) return;
            modeEScalingBatchTimer = 0f;

            if (modeEPendingScalingFactions.Count == 0) return;

            // 批量应用所有待处理阵营的缩放
            foreach (Teams faction in modeEPendingScalingFactions)
            {
                ApplyFactionDeathScaling(faction);
            }
            modeEPendingScalingFactions.Clear();
        }

        /// <summary>
        /// 对指定阵营的所有存活单位应用属性缩放（生命值 + 伤害）
        /// 个人层数 = 当前阵营死亡计数 - 该敌人出生时基线（deathBaseline）
        /// 每层提供 5% 生命/伤害；每次先移除旧 Modifier 再添加新的，避免累积叠加
        /// [P4性能优化] 使用阵营独立存活列表，只遍历同阵营单位，避免全量遍历
        /// </summary>
        private void ApplyFactionDeathScaling(Teams faction)
        {
            try
            {
                int factionDeathCount = GetModeEFactionDeathCount(faction);
                DevLog("[ModeE] 应用阵营缩放: " + faction + " 死亡计数=" + factionDeathCount);

                // [P4] 使用阵营独立列表，只遍历该阵营的存活单位
                List<CharacterMainControl> factionList = GetFactionAliveList(faction);
                if (factionList == null || factionList.Count == 0) return;

                for (int i = factionList.Count - 1; i >= 0; i--)
                {
                    CharacterMainControl enemy = factionList[i];
                    if (enemy == null || enemy.gameObject == null)
                    {
                        // 清理无效引用
                        factionList.RemoveAt(i);
                        CleanupModeEEnemyRuntimeState(enemy, faction);
                        continue;
                    }

                    try
                    {
                        ModeEEnemyScalingState scalingState = null;
                        if (!modeEEnemyScalingStates.TryGetValue(enemy, out scalingState) || scalingState == null)
                        {
                            scalingState = new ModeEEnemyScalingState();
                            scalingState.deathBaseline = GetModeEFactionDeathCount(faction);
                            modeEEnemyScalingStates[enemy] = scalingState;
                        }

                        int personalStacks = Mathf.Max(0, GetModeEFactionDeathCount(faction) - scalingState.deathBaseline);
                        float hpPercent = personalStacks * 0.05f;
                        float damagePercent = personalStacks * 0.05f;
                        float oldMaxHealth = GetModeEMaxHealthValue(enemy);

                        RemoveModeEScalingModifiers(enemy);

                        scalingState.hp = AddModeEScalingModifier(enemy, "MaxHealth", hpPercent);
                        scalingState.gunDmg = AddModeEScalingModifier(enemy, "GunDamageMultiplier", damagePercent);
                        scalingState.meleeDmg = AddModeEScalingModifier(enemy, "MeleeDamageMultiplier", damagePercent);
                        SyncModeEHealthAfterMaxHealthRefresh(enemy, oldMaxHealth);
                    }
                    catch { }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] ApplyFactionDeathScaling 失败: " + e.Message);
            }
        }

        private int GetModeEFactionDeathCount(Teams faction)
        {
            int deathCount = 0;
            modeEFactionDeathCount.TryGetValue(faction, out deathCount);
            return deathCount;
        }

        #endregion

        #region Mode E BossLiveMapMod 集成

        /// <summary>
        /// 获取或创建 Mode E 专用的虚拟 CharacterSpawnerRoot
        /// BossLiveMapMod 通过遍历 CharacterSpawnerRoot.CreatedCharacters 来发现敌人，
        /// Mode E 的敌人通过 SpawnEnemyCore 直接生成，不经过游戏原版 spawner 系统，
        /// 因此需要创建一个虚拟的 CharacterSpawnerRoot 来注册这些敌人
        /// </summary>
        private CharacterSpawnerRoot GetOrCreateModeESpawnerRoot()
        {
            if (modeEVirtualSpawnerRoot != null) return modeEVirtualSpawnerRoot;

            try
            {
                GameObject spawnerObj = new GameObject("ModeE_VirtualSpawnerRoot");
                UnityEngine.Object.DontDestroyOnLoad(spawnerObj);
                modeEVirtualSpawnerRoot = spawnerObj.AddComponent<CharacterSpawnerRoot>();
                // This virtual root is only a registry bridge; keep Update/Init from entering the vanilla spawn pipeline.
                modeEVirtualSpawnerRoot.enabled = false;
                DevLog("[ModeE] 创建虚拟 CharacterSpawnerRoot 用于 BossLiveMapMod 集成");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] 创建虚拟 CharacterSpawnerRoot 失败: " + e.Message);
            }

            return modeEVirtualSpawnerRoot;
        }

        private System.Collections.IList GetModeESpawnerRootCreatedCharactersList()
        {
            if (modeEVirtualSpawnerRoot == null)
            {
                return null;
            }

            try
            {
                if (!modeESpawnerRootCreatedCharactersAccessorCached)
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    Type rootType = typeof(CharacterSpawnerRoot);

                    modeESpawnerRootCreatedCharactersField =
                        rootType.GetField("CreatedCharacters", flags) ??
                        rootType.GetField("createdCharacters", flags);

                    if (modeESpawnerRootCreatedCharactersField == null)
                    {
                        modeESpawnerRootCreatedCharactersProperty =
                            rootType.GetProperty("CreatedCharacters", flags) ??
                            rootType.GetProperty("createdCharacters", flags);
                    }

                    modeESpawnerRootCreatedCharactersAccessorCached = true;

                    if (modeESpawnerRootCreatedCharactersField == null &&
                        modeESpawnerRootCreatedCharactersProperty == null &&
                        !modeESpawnerRootCreatedCharactersAccessorMissingLogged)
                    {
                        modeESpawnerRootCreatedCharactersAccessorMissingLogged = true;
                        DevLog("[ModeE] [WARNING] 未找到虚拟 SpawnerRoot 的 createdCharacters/CreatedCharacters 访问器");
                    }
                }

                if (modeESpawnerRootCreatedCharactersField != null)
                {
                    return modeESpawnerRootCreatedCharactersField.GetValue(modeEVirtualSpawnerRoot) as System.Collections.IList;
                }

                if (modeESpawnerRootCreatedCharactersProperty != null)
                {
                    return modeESpawnerRootCreatedCharactersProperty.GetValue(modeEVirtualSpawnerRoot, null) as System.Collections.IList;
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 获取虚拟 SpawnerRoot CreatedCharacters 失败: " + e.Message);
            }

            return null;
        }

        private void RemoveModeEEnemyFromSpawnerRootList(CharacterMainControl character)
        {
            try
            {
                System.Collections.IList list = GetModeESpawnerRootCreatedCharactersList();
                if (list == null)
                {
                    return;
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    object entry = list[i];
                    if (entry == null)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    CharacterMainControl existing = entry as CharacterMainControl;
                    if (existing != null && object.ReferenceEquals(existing, character))
                    {
                        list.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 从虚拟 CharacterSpawnerRoot 中移除敌人，防止 CreatedCharacters 列表无限膨胀
        /// 通过反射获取 CreatedCharacters 列表（无公开 Remove API）
        /// </summary>
        private void UnregisterModeEEnemyFromSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                modeESpawnerRootRegisteredEnemies.Remove(character);

                if (modeEVirtualSpawnerRoot == null) return;

                RemoveModeEEnemyFromSpawnerRootList(character);
            }
            catch { }
        }

        /// <summary>
        /// 将 Mode E 生成的敌人注册到虚拟 CharacterSpawnerRoot，
        /// 使 BossLiveMapMod 能通过标准流程检测到这些敌人
        /// </summary>
        private void RegisterModeEEnemyToSpawnerRoot(CharacterMainControl character)
        {
            try
            {
                if (character == null)
                {
                    return;
                }

                CharacterSpawnerRoot root = GetOrCreateModeESpawnerRoot();
                if (root != null)
                {
                    RemoveModeEEnemyFromSpawnerRootList(character);
                    modeESpawnerRootRegisteredEnemies.Add(character);
                    root.AddCreatedCharacter(character);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] RegisterModeEEnemyToSpawnerRoot 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理 Mode E 虚拟 CharacterSpawnerRoot
        /// </summary>
        private void CleanupModeEVirtualSpawnerRoot()
        {
            try
            {
                if (modeEVirtualSpawnerRoot != null)
                {
                    try
                    {
                        System.Collections.IList list = GetModeESpawnerRootCreatedCharactersList();
                        if (list != null)
                        {
                            list.Clear();
                        }
                    }
                    catch { }

                    if (modeEVirtualSpawnerRoot.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(modeEVirtualSpawnerRoot.gameObject);
                    }
                    modeEVirtualSpawnerRoot = null;
                    modeESpawnerRootRegisteredEnemies.Clear();
                    DevLog("[ModeE] 已清理虚拟 CharacterSpawnerRoot");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] CleanupModeEVirtualSpawnerRoot 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 基础血量提升

        /// <summary>
        /// Mode E 基础血量提升：非玩家阵营敌人血量 × 1.5
        /// 在敌人生成时一次性应用，不影响后续阵营死亡缩放机制
        /// 玩家所属阵营不应用（bear 阵营由 ApplyBearFactionStatBoost 独立处理）
        /// </summary>
        private void ApplyModeEBaseHealthBoost(CharacterMainControl character)
        {
            try
            {
                // 玩家所属阵营不应用血量提升（避免友方Boss被额外加强）
                if (character.Team == modeEPlayerFaction) return;

                ApplyStatBoostPercent(character, "MaxHealth", 0.5f, true);
                DevLog("[ModeE] 基础血量提升: " + character.gameObject.name + " HP × 1.5");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ApplyModeEBaseHealthBoost 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E BEAR阵营兜底

        /// <summary>
        /// 从全阵营小怪池随机抽取一个预设（不限阵营过滤）
        /// 用于 bear 阵营兜底（原版游戏无 bear 预设）
        /// </summary>
        private EnemyPresetInfo GetAllFactionMinionPreset()
        {
            if (modeDMinionPool == null || modeDMinionPool.Count == 0) return null;
            return modeDMinionPool[UnityEngine.Random.Range(0, modeDMinionPool.Count)];
        }

        /// <summary>
        /// BEAR阵营专属属性提升：血量和伤害提升150%（最终为原始值的 2.5 倍）
        /// 补偿小怪基础数值偏低，使其达到 Boss 级强度
        /// </summary>
        private void ApplyBearFactionStatBoost(CharacterMainControl character)
        {
            try
            {
                ApplyStatBoostPercent(character, "MaxHealth", 1.5f, true);
                ApplyStatBoostPercent(character, "GunDamageMultiplier", 1.5f, false);
                ApplyStatBoostPercent(character, "MeleeDamageMultiplier", 1.5f, false);
                DevLog("[ModeE] BEAR阵营属性提升: " + character.gameObject.name + " HP/Dmg × 2.5");
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ApplyBearFactionStatBoost 失败: " + e.Message);
            }
        }

        #endregion

        #region Mode E 属性提升工具方法

        /// <summary>
        /// 通用属性百分比提升：给角色的指定 Stat 增加 (BaseValue × percent) 的加法 Modifier
        /// <para>例如 percent=0.5 表示提升50%（最终 BaseValue × 1.5），percent=1.5 表示提升150%（最终 BaseValue × 2.5）</para>
        /// </summary>
        /// <param name="syncHealth">如果为 true 且 statName 为 MaxHealth，则同步当前血量到新上限</param>
        private void ApplyStatBoostPercent(CharacterMainControl character, string statName, float percent, bool syncHealth)
        {
            try
            {
                var characterItem = character.CharacterItem;
                if (characterItem == null) return;

                Stat stat = characterItem.GetStat(statName);
                if (stat == null) return;

                float boostAmount = stat.BaseValue * percent;
                Modifier mod = new Modifier(ModifierType.Add, boostAmount, this);
                stat.AddModifier(mod);

                if (syncHealth)
                {
                    Health health = character.Health;
                    if (health != null)
                    {
                        health.CurrentHealth = stat.Value;
                    }
                }
            }
            catch {}
        }

        #endregion
    }
}
