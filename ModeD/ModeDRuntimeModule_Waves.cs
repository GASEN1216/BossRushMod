using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        // P2-6: 复用 List 缓存，减少每波分配
        private static readonly List<SpawnInfo> reusableSpawnQueue = new List<SpawnInfo>();

        /// <summary>
        /// Mode D: 开始下一波
        /// </summary>
        /// <returns>是否成功开始下一波（false 表示仍有敌人存活等条件不满足）</returns>
        internal bool ModeDStartNextWave()
        {
            try
            {
                if (!modeDActive)
                {
                    ModBehaviour.DevLog("[ModeD] ModeDStartNextWave: Mode D 未激活");
                    return false;
                }

                // P1-2 修复：生成中禁止开波的硬护栏
                // 如果上一波仍有生成任务未结案，禁止开新波（防止跳波/状态不一致）
                if (modeDWaveIndex > 0 && modeDSpawnResolvedInCurrentWave < modeDExpectedEnemiesInCurrentWave)
                {
                    owner.ShowMessage(L10n.T(
                        "正在生成敌人，请稍候...",
                        "Still spawning enemies, please wait..."));
                    ModBehaviour.DevLog("[ModeD] [WARNING] ModeDStartNextWave: 上一波生成未完成，禁止开新波 (resolved=" +
                           modeDSpawnResolvedInCurrentWave + ", expected=" + modeDExpectedEnemiesInCurrentWave + ")");
                    return false;
                }

                // 取消可能存在的自动下一波协程（防止重复开波）
                if (modeDAutoNextWaveCoroutine != null)
                {
                    owner.StopCoroutine(modeDAutoNextWaveCoroutine);
                    modeDAutoNextWaveCoroutine = null;
                }

                if (modeDWaveIndex <= 0)
                {
                    owner.BeginAchievementSessionForArena("ModeD");
                }

                owner.SetBossRushRuntimeActiveForArena(true);

                // P0-2 修复：显式隐藏"冲下一波"选项（调用当前 ModBehaviour 的方法，而非路牌的）
                HideModeDNextWaveOption();

                // 检查当前波是否还有敌人存活
                CleanupDeadEnemies();
                if (modeDCurrentWaveEnemies.Count > 0)
                {
                    owner.ShowMessage(L10n.T(
                        "当前波次还有 " + modeDCurrentWaveEnemies.Count + " 个敌人存活！",
                        modeDCurrentWaveEnemies.Count + " enemies still alive in current wave!"));
                    return false;
                }

                modeDWaveIndex++;
                // 重置波次完成标志，允许下一波完成时触发
                modeDWaveCompletePending = false;
                // 重置生成计数
                modeDExpectedEnemiesInCurrentWave = 0;
                modeDSpawnResolvedInCurrentWave = 0;
                ModBehaviour.DevLog("[ModeD] 开始第 " + modeDWaveIndex + " 波");

                // 每次开新波前动态刷新每波敌人数
                if (owner.ModeDConfiguredEnemiesPerWave > 0)
                {
                    modeDEnemiesPerWave = Mathf.Clamp(owner.ModeDConfiguredEnemiesPerWave, 1, 10);
                }

                // 计算本波的 Boss 数量和小怪数量
                int totalEnemies = modeDEnemiesPerWave;
                int bossCount = GetModeDWaveBossCount(modeDWaveIndex, totalEnemies);
                int minionCount = totalEnemies - bossCount;

                ModBehaviour.DevLog("[ModeD] 波次 " + modeDWaveIndex + ": 总敌人=" + totalEnemies +
                       ", Boss=" + bossCount + ", 小怪=" + minionCount);

                // 显示波次横幅
                owner.ShowBigBanner(L10n.T(
                    "第 <color=yellow>" + modeDWaveIndex + "</color> 波开始！",
                    "Wave <color=yellow>" + modeDWaveIndex + "</color> started!"
                ));

                // 波次开始时切换路牌到加油状态（支持连点10次结束波次的兜底机制）
                if (owner.ArenaRewardSignInteract != null)
                {
                    owner.ArenaRewardSignInteract.SetCheerMode();
                }

                // 生成敌人
                SpawnModeDWaveEnemies(bossCount, minionCount);

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] ModeDStartNextWave 失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 根据波次计算 Boss 数量
        /// </summary>
        /// <remarks>
        /// 波次规则：
        /// <list type="bullet">
        /// <item>第 1-5 波：全小怪（0 个 Boss）</item>
        /// <item>第 6-10 波：1 个 Boss</item>
        /// <item>第 11-15 波：2 个 Boss</item>
        /// <item>第 16+ 波：全 Boss</item>
        /// </list>
        /// </remarks>
        /// <param name="waveIndex">当前波次索引</param>
        /// <param name="totalEnemies">本波总敌人数</param>
        /// <returns>Boss 数量</returns>

        /// <summary>
        /// 生成 Mode D 波次敌人
        /// </summary>
        /// <summary>
        /// 生成 Mode D 波次敌人（分帧生成，避免低端机帧尖刺）
        /// </summary>
        private System.Collections.IEnumerator SpawnModeDWaveEnemiesCoroutine(int bossCount, int minionCount)
        {
            Func<bool> isQueueCurrent = ModeDRuntimeModule.CaptureValidity(owner, true);
            if (!isQueueCurrent()) yield break;
            // 初始化部分（无 yield）
            modeDCurrentWaveEnemies.Clear();

            CharacterMainControl playerMain = CharacterMainControl.Main;
            if (playerMain == null)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] 未找到玩家");
                yield break;
            }

            // 获取刷怪点并洗牌，确保随机但不重复
            Vector3[] spawnPoints = owner.GetCurrentSceneSpawnPoints();
            // 兜底：如果 JSON 刷怪点为空，基于玩家位置生成圆环随机点
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Vector3 playerPos = playerMain.transform.position;
                spawnPoints = GenerateFallbackSpawnPointsAroundPlayer(playerPos);
                ModBehaviour.DevLog("[ModeD] 使用玩家位置生成的兜底刷怪点");
            }

            var shuffledPositions = ShuffleSpawnPoints(spawnPoints);

            bool bannerShown = false;
            Vector3 bannerPos = Vector3.zero;

            // 获取玩家位置，预分配所有安全刷怪位置（安全距离外、不重复）
            Vector3 modeDPlayerPos = playerMain.transform.position;
            int totalEnemies = bossCount + minionCount;
            var safePositions = SpawnPositionHelper.FindMultipleSafeSpawnPoints(totalEnemies, shuffledPositions, modeDPlayerPos, SpawnPositionHelper.DefaultSafeDistance);

            // 洗牌安全位置，保持随机空间分布（避免敌人按距离聚集）
            for (int i = safePositions.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                Vector3 temp = safePositions[i];
                safePositions[i] = safePositions[j];
                safePositions[j] = temp;
            }

            int posIndex = 0;

            // P2-6 优化：使用复用的 reusableSpawnQueue，避免每波分配新列表
            reusableSpawnQueue.Clear();

            // 生成 Boss（带重试机制，防止预设为空导致卡波次）
            for (int i = 0; i < bossCount; i++)
            {
                EnemyPresetInfo bossPreset = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    bossPreset = GetRandomBossPreset();
                    if (bossPreset != null) break;
                }

                if (bossPreset != null)
                {
                    Vector3 spawnPos = posIndex < safePositions.Count
                        ? safePositions[posIndex]
                        : SpawnPositionHelper.SnapToGround(shuffledPositions[posIndex % shuffledPositions.Count]);
                    posIndex++;

                    if (!bannerShown)
                    {
                        bannerPos = spawnPos;
                        bannerShown = true;
                    }

                    modeDExpectedEnemiesInCurrentWave++;
                    reusableSpawnQueue.Add(new SpawnInfo { preset = bossPreset, position = spawnPos, isBoss = true });
                }
                else
                {
                    ModBehaviour.DevLog("[ModeD] [WARNING] Boss预设获取失败，跳过该敌人（已重试5次）");
                }
            }

            // 生成小怪
            for (int i = 0; i < minionCount; i++)
            {
                EnemyPresetInfo minionPreset = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    minionPreset = GetRandomMinionPreset();
                    if (minionPreset != null) break;
                }

                if (minionPreset != null)
                {
                    Vector3 spawnPos = posIndex < safePositions.Count
                        ? safePositions[posIndex]
                        : SpawnPositionHelper.SnapToGround(shuffledPositions[posIndex % shuffledPositions.Count]);
                    posIndex++;

                    if (!bannerShown)
                    {
                        bannerPos = spawnPos;
                        bannerShown = true;
                    }

                    modeDExpectedEnemiesInCurrentWave++;
                    reusableSpawnQueue.Add(new SpawnInfo { preset = minionPreset, position = spawnPos, isBoss = false });
                }
                else
                {
                    ModBehaviour.DevLog("[ModeD] [WARNING] 小怪预设获取失败，跳过该敌人（已重试5次）");
                }
            }

            // 如果预期敌人数为0，直接完成本波
            if (modeDExpectedEnemiesInCurrentWave == 0)
            {
                ModBehaviour.DevLog("[ModeD] [WARNING] 本波无任何敌人可生成，直接完成波次");
                TryResolveModeDWaveComplete();
                yield break;
            }

            // 显示敌人方位横幅
            if (bannerShown)
            {
                try
                {
                    owner.ShowModeDEnemyBanner(L10n.T("敌人", "Enemies"), bannerPos, playerMain.transform.position,
                        0, 1, true, Mathf.Max(0, modeDWaveIndex - 1), 1);
                }
                catch {}
            }

            // 分帧逐个生成敌人（每生成一个等待一帧，避免低端机帧尖刺）
            // 注意：这部分不能放在 try-catch 中，因为 C# 不允许在包含 catch 的 try 块中使用 yield
            for (int i = 0; i < reusableSpawnQueue.Count; i++)
            {
                if (!isQueueCurrent()) yield break;
                var info = reusableSpawnQueue[i];
                SpawnModeDEnemy(info.preset, info.position, info.isBoss);

                // 每生成一个敌人后等待一帧（低端机友好）
                yield return null;
            }

            ModBehaviour.DevLog("[ModeD] 本波敌人生成完成（分帧生成，共" + reusableSpawnQueue.Count + "个）");
        }

        /// <summary>
        /// 生成 Mode D 波次敌人（入口方法，启动协程）
        /// </summary>
        private void SpawnModeDWaveEnemies(int bossCount, int minionCount)
        {
            owner.StartCoroutine(SpawnModeDWaveEnemiesCoroutine(bossCount, minionCount));
        }

        /// <summary>
        /// 刷怪信息结构（用于分帧生成）
        /// </summary>
        private struct SpawnInfo
        {
            public EnemyPresetInfo preset;
            public Vector3 position;
            public bool isBoss;
        }
        /// <summary>
        /// 获取随机 Boss 预设
        /// </summary>
        /// <remarks>
        /// 前期波次（第6-10波）会过滤掉强力 Boss（口口口口、四骑士、龙裔遗族、焚天龙皇），
        /// 避免白手起家模式下玩家在装备不足时遇到过强的敌人。
        /// </remarks>

        /// <summary>
        /// 获取随机小怪预设
        /// </summary>
        /// <remarks>
        /// 前10波会过滤掉幽灵（Cname_Ghost）
        /// </remarks>

        /// <summary>
        /// 洗牌刷怪点数组（Fisher-Yates 算法）
        /// P2-6 优化：使用复用 List 缓存，避免每波分配新列表
        /// </summary>

        /// <summary>
        /// 基于玩家位置生成兜底刷怪点（圆环分布）
        /// </summary>
        /// <param name="playerPos">玩家位置</param>
        /// <param name="pointCount">生成的点数（默认10个）</param>
        /// <param name="minRadius">最小半径</param>
        /// <param name="maxRadius">最大半径</param>

        /// <summary>
        /// 生成 Mode D 敌人（带最多 5 次重试）
        /// 使用 SpawnEnemyCore 通用方法，外层包装波次守卫逻辑
        /// </summary>
        private void SpawnModeDEnemy(EnemyPresetInfo preset, Vector3 position, bool isBoss)
        {
            Func<bool> isSpawnCurrent = ModeDRuntimeModule.CaptureValidity(owner, false);
            // 防止跨波迟到敌人污染下一波
            int waveToken = modeDWaveIndex;

            // 使用通用生成核心方法，通过回调注入 Mode D 差异化逻辑
            // onFailed 回调确保生成失败时也能递增 resolved 计数，防止波次卡住
            owner.SpawnModeDEnemyCore(
                preset,
                position,
                isBoss,
                isActiveCheck: isSpawnCurrent,
                onSpawned: (ctx) =>
                {
                    try
                    {
                        CharacterMainControl character = ctx.character;

                        // 再次检查波次一致性
                        if (!isSpawnCurrent())
                        {
                            UnityEngine.Object.Destroy(character.gameObject);
                            ModBehaviour.DevLog("[ModeD] 敌人配置完成但波次已变化，销毁敌人");
                            return;
                        }

                        character.gameObject.name = "ModeD_" + ctx.preset.displayName;

                        // 渐进式难度：按波次提升敌人属性
                        ApplyModeDWaveScaling(character, modeDWaveIndex);

                        // 官方小怪预设可能是中立/玩家友方。仅设置 AI 目标不能改变伤害与
                        // 敌对判定，因此在登记波次前补标准敌对性安全网。
                        try
                        {
                            if (!Team.IsEnemy(Teams.player, character.Team))
                            {
                                character.SetTeam(Teams.wolf);
                                ModBehaviour.DevLog("[ModeD] 敌对性安全网已将 " + ctx.preset.displayName
                                    + " 阵营修正为 wolf");
                            }
                        }
                        catch (Exception teamException)
                        {
                            ModBehaviour.DevLog("[ModeD] [WARNING] 敌对性安全网失败: " + teamException.Message);
                            try
                            {
                                character.SetTeam(Teams.wolf);
                            }
                            catch (Exception fallbackException)
                            {
                                ModBehaviour.DevLog("[ModeD] [ERROR] 强制设置 wolf 阵营仍失败: "
                                    + fallbackException.Message);
                                if (character.gameObject != null)
                                {
                                    UnityEngine.Object.Destroy(character.gameObject);
                                }
                                return;
                            }
                        }

                        bool confirmedHostile = false;
                        try { confirmedHostile = Team.IsEnemy(Teams.player, character.Team); }
                        catch (Exception verifyException)
                        {
                            ModBehaviour.DevLog("[ModeD] [WARNING] 敌对性回读失败: " + verifyException.Message);
                        }
                        if (!confirmedHostile)
                        {
                            ModBehaviour.DevLog("[ModeD] [ERROR] 敌对性修正后仍非玩家敌对，拒绝登记: "
                                + ctx.preset.displayName);
                            if (character.gameObject != null)
                            {
                                character.gameObject.SetActive(false);
                                UnityEngine.Object.Destroy(character.gameObject);
                            }
                            return;
                        }

                        // 强制设置 AI 仇恨到玩家
                        try
                        {
                            CharacterMainControl playerMain = CharacterMainControl.Main;
                            if (playerMain != null && playerMain.mainDamageReceiver != null)
                            {
                                AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
                                if (ai != null)
                                {
                                    ai.forceTracePlayerDistance = 500f;
                                    ai.searchedEnemy = playerMain.mainDamageReceiver;
                                    ai.SetTarget(playerMain.mainDamageReceiver.transform);
                                    ai.SetNoticedToTarget(playerMain.mainDamageReceiver);
                                    ai.noticed = true;
                                }
                            }
                        }
                        catch { }

                        // 再次检查波次一致性
                        if (!isSpawnCurrent())
                        {
                            UnityEngine.Object.Destroy(character.gameObject);
                            ModBehaviour.DevLog("[ModeD] 敌人配置完成但波次已变化，销毁敌人");
                            return;
                        }

                        // 加入当前波敌人列表
                        modeDCurrentWaveEnemies.Add(character);
                        owner.RegisterModeDEnemyRecoveryAnchor(character, ctx.position);

                        // 注册死亡事件
                        RegisterModeDEnemyDeath(character);

                        ModBehaviour.DevLog("[ModeD] 敌人生成成功: " + ctx.preset.displayName);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[ModeD] [ERROR] OnModeDEnemySpawned 失败: " + e.Message);
                    }
                    finally
                    {
                        // 波次一致性守卫：只有当前波的任务才计数
                        if (isSpawnCurrent()) ResolveModeDSpawnCount(waveToken);
                    }
                },
                onFailed: () =>
                {
                    // 生成失败也必须递增 resolved 计数，防止波次卡住
                    ModBehaviour.DevLog("[ModeD] 敌人生成最终失败，执行兜底结案");
                    if (isSpawnCurrent()) ResolveModeDSpawnCount(waveToken);
                },
                waveIndex: modeDWaveIndex
            );
        }

        /// <summary>
        /// Mode D 生成结案计数（成功和失败共用，防止波次卡住）
        /// </summary>

        /// <summary>
        /// 尝试解析波次是否完成
        /// 只有当满足以下所有条件时才触发波次完成：
        /// 1. 波次未标记为完成中（防止重复触发）
        /// 2. 当前没有存活敌人
        /// 3. 所有敌人生成都已结案（成功或最终失败，防止"迟到的怪"）
        /// </summary>

        /// <summary>
        /// 将敌人的伤害倍率统一设置为 1
        /// </summary>
        /// <remarks>
        /// 原版游戏中不同 Boss 有不同的 damageMultiplier（如 1.0、1.5、2.0 等），
        /// 在白手起家模式下给玩家发放武器后，高倍率 Boss 伤害会过高，
        /// 因此需要统一为 1 以保持游戏平衡。
        /// </remarks>
        /// <param name="character">需要调整的敌人角色</param>
        internal void NormalizeDamageMultiplier(CharacterMainControl character)
        {
            try
            {
                if (character == null) return;

                var item = character.CharacterItem;
                if (item == null) return;

                // 将枪械伤害倍率设置为1
                try
                {
                    Stat gunDmg = item.GetStat("GunDamageMultiplier");
                    if (gunDmg != null)
                    {
                        float oldValue = gunDmg.BaseValue;
                        gunDmg.BaseValue = 1f;
                        ModBehaviour.DevLog("[ModeD] 统一枪械伤害倍率: " + oldValue + " -> 1.0");
                    }
                }
                catch {}

                // 将近战伤害倍率设置为1
                try
                {
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier");
                    if (meleeDmg != null)
                    {
                        float oldValue = meleeDmg.BaseValue;
                        meleeDmg.BaseValue = 1f;
                        ModBehaviour.DevLog("[ModeD] 统一近战伤害倍率: " + oldValue + " -> 1.0");
                    }
                }
                catch {}
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] NormalizeDamageMultiplier 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 应用 Mode D 波次数值强化
        /// </summary>
        private void ApplyModeDWaveScaling(CharacterMainControl character, int waveIndex)
        {
            try
            {
                // 每波提升 3% 属性
                float scale = 1f + 0.03f * Mathf.Max(0, waveIndex - 1);

                // 通过 Item 的 Stat 系统修改生命值（MaxHealth 是只读属性，通过 Modifier 修改）
                try
                {
                    var characterItem = character.CharacterItem;
                    if (characterItem != null)
                    {
                        Stat maxHealthStat = characterItem.GetStat("MaxHealth");
                        if (maxHealthStat != null)
                        {
                            // 按整体生命乘波次比例；从 Value 算 Add 增量会再次吃末尾的难度倍率。
                            if (scale > 1f)
                            {
                                Modifier modifier = new Modifier(ItemStatsSystem.Stats.ModifierType.PercentageMultiply, scale - 1f, this);
                                maxHealthStat.AddModifier(modifier);
                            }

                            // 同步 CurrentHealth 到新的 MaxHealth
                            Health health = character.Health;
                            if (health != null)
                            {
                                health.CurrentHealth = health.MaxHealth;
                            }
                        }
                    }
                }
                catch {}

                ModBehaviour.DevLog("[ModeD] 应用波次强化: wave=" + waveIndex + ", scale=" + scale);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] ApplyModeDWaveScaling 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 注册敌人死亡事件
        /// </summary>
        private void RegisterModeDEnemyDeath(CharacterMainControl enemy)
        {
            try
            {
                Health health = enemy.GetComponent<Health>();
                if (health != null)
                {
                    health.OnDeadEvent.AddListener((dmgInfo) => OnModeDEnemyDeath(enemy));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] RegisterModeDEnemyDeath 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode D 敌人死亡处理
        /// </summary>
        private void OnModeDEnemyDeath(CharacterMainControl enemy)
        {
            // 日志单独隔离，避免 enemy 为 null 时影响后续关键操作
            try
            {
                ModBehaviour.DevLog("[ModeD] 敌人死亡: " + (enemy != null ? enemy.name : "<null>"));
            }
            catch {}

            // 获取死亡位置（独立 try，不影响后续操作）
            // 使用 bool 标记而不是 Vector3.zero，避免敌人死在世界原点时被误判
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

            // 标记 lootbox（独立 try，不影响后续操作）
            try
            {
                if (hasDeathPos)
                {
                    owner.StartCoroutine(BossRushLootboxUtility.DecorateLootboxesNearPosition(owner, deathPosition, false));
                }
            }
            catch {}

            // 关键操作：从列表移除 + 检查波次完成（独立 try，确保一定执行）
            try
            {
                owner.UnregisterEnemyRecoveryForArena(enemy);
                modeDCurrentWaveEnemies.Remove(enemy);
                TryResolveModeDWaveComplete();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] OnModeDEnemyDeath 关键操作失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理已死亡的敌人引用
        /// P2-6 优化：使用倒序循环代替 RemoveAll，避免 lambda 委托开销
        /// </summary>

        /// <summary>
        /// Mode D 波次完整性自检：定期检查是否有敌人存活，如果没有则自动推进波次
        /// </summary>
        internal void TickModeDIntegrity(float deltaTime)
        {
            if (!modeDActive)
            {
                return;
            }

            if (!owner.IsActive)
            {
                owner.ResetArenaIntegrityCheck();
                return;
            }

            if (owner.AdvanceArenaIntegrityCheck(deltaTime))
            {
                TryFixStuckWaveIfNoModeDEnemyAlive();
            }
        }

        /// <remarks>
        /// 解决问题：敌人死亡事件可能丢失（瞬杀、事件触发时机等），导致波次卡住无法推进。
        /// 此方法作为兜底机制，每隔一段时间检查一次敌人存活状态。
        /// </remarks>
        private void TryFixStuckWaveIfNoModeDEnemyAlive()
        {
            try
            {
                if (!modeDActive)
                {
                    return;
                }

                // 如果当前波次索引为0，说明还没开始第一波，不需要自检
                if (modeDWaveIndex <= 0)
                {
                    return;
                }

                // 清理已死亡的敌人引用
                CleanupDeadEnemies();

                // 使用统一的解析方法检查波次是否完成（遵守 resolved 达标约束）
                ModBehaviour.DevLog("[ModeD] 自检：调用 TryResolveModeDWaveComplete");
                TryResolveModeDWaveComplete();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] TryFixStuckWaveIfNoModeDEnemyAlive 失败: " + e.Message);
            }
        }

        /// <summary>
        /// Mode D 波次完成
        /// </summary>
        internal void OnModeDWaveComplete()
        {
            try
            {
                // 防止重复触发（敌人死亡事件和自检可能同时触发）
                if (modeDWaveCompletePending)
                {
                    return;
                }
                modeDWaveCompletePending = true;

                ModBehaviour.DevLog("[ModeD] 第 " + modeDWaveIndex + " 波完成！");

                // 通知战役契约（未启用时零成本早返）
                owner.NotifyCampaignModeDWaveComplete(modeDWaveIndex);

                // Mode D 完成10波视为"通关"，触发成就检查
                if (modeDWaveIndex >= 5)
                {
                    owner.CheckModeDFlawlessAchievementForRuntime();
                }

                if (modeDWaveIndex >= 10)
                {
                    owner.CheckModeDClearAchievementsForRuntime();
                }

                owner.ShowBigBanner(L10n.T(
                    "第 <color=yellow>" + modeDWaveIndex + "</color> 波完成！",
                    "Wave <color=yellow>" + modeDWaveIndex + "</color> completed!"
                ));

                // 波次结束时切换路牌回生小鸡状态
                if (owner.ArenaRewardSignInteract != null)
                {
                    owner.ArenaRewardSignInteract.SetEntryMode();
                }

                bool useInteract = false;
                try
                {
                    useInteract = owner.UseInteractBetweenWavesForArena;
                }
                catch {}

                if (useInteract)
                {
                    owner.ShowMessage(L10n.T("可以通过路牌开始下一波", "Use the signpost to start next wave"));

                    // 显示 Mode D 专用的"冲下一波"选项
                    ShowModeDNextWaveOption();
                }
                else
                {
                    float interval = 15f;
                    try
                    {
                        interval = owner.GetWaveIntervalSeconds();
                    }
                    catch {}

                    // 每5波额外休息时间
                    // modeDWaveIndex 此时尚未自增（自增在 ModeDStartNextWave 中），代表刚完成的波次编号
                    float milestoneBonus = owner.GetMilestoneRestBonusSeconds();
                    if (milestoneBonus > 0f && modeDWaveIndex > 0 && modeDWaveIndex % 5 == 0)
                    {
                        interval += milestoneBonus;
                        ModBehaviour.DevLog("[ModeD] 第 " + modeDWaveIndex + " 波完成，额外休息 " + milestoneBonus + " 秒");
                    }

                    if (interval <= 0f)
                    {
                        if (modeDActive)
                        {
                            ModeDStartNextWave();
                        }
                    }
                    else
                    {
                        int secondsInt = Mathf.RoundToInt(interval);
                        if (secondsInt < 1)
                        {
                            secondsInt = 1;
                        }

                        owner.ShowMessage(L10n.T(
                            "下一波将在 " + secondsInt + " 秒后自动开始",
                            "Next wave starts in " + secondsInt + " seconds"));

                        // 取消旧协程，启动新协程（防止重复开波）
                        ScheduleAutoNextWave(interval);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] OnModeDWaveComplete 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 调度自动下一波协程（取消旧协程，防止重复开波）
        /// </summary>
    }
}
