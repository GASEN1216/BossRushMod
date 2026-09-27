using System;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        internal void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // Mode D 有独立的敌人死亡处理（RegisterModeDEnemyDeath），不走普通模式逻辑
                // 避免 Mode D 打死敌人时误触发普通模式的通关判定
                if (owner.IsModeDActive)
                {
                    return;
                }

                if (!owner.IsActive || deadHealth == null)
                {
                    return;
                }

                CharacterMainControl deadCharacter = null;
                try
                {
                    deadCharacter = deadHealth.TryGetCharacter();
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[BossRush] [WARNING] OnEnemyDied 读取死亡角色失败: " + e.Message);
                }

                // 多Boss模式：检查是否是当前波的其中一名Boss
                if (BossesPerWave > 1 && CurrentWaveBosses != null && CurrentWaveBosses.Count > 0)
                {
                    MonoBehaviour matchedBoss = null;
                    for (int i = 0; i < CurrentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = CurrentWaveBosses[i];
                        if (boss == null) continue;

                        bool isDeadBoss = false;

                        try
                        {
                            CharacterMainControl bossCharacter = boss as CharacterMainControl;
                            if (bossCharacter != null && deadCharacter != null)
                            {
                                isDeadBoss = (bossCharacter == deadCharacter);
                            }
                        }
                        catch (Exception e)
                        {
                            ModBehaviour.DevLog("[BossRush] [WARNING] OnEnemyDied 比对多Boss角色失败: " + e.Message);
                        }

                        if (!isDeadBoss)
                        {
                            try
                            {
                                Health bossHealth = boss.GetComponent<Health>();
                                if (bossHealth == deadHealth || boss.gameObject == deadHealth.gameObject)
                                {
                                    isDeadBoss = true;
                                }
                            }
                            catch (Exception e)
                            {
                                ModBehaviour.DevLog("[BossRush] [WARNING] OnEnemyDied 比对多Boss Health失败: " + e.Message);
                            }
                        }

                        if (isDeadBoss)
                        {
                            matchedBoss = boss;
                            break;
                        }
                    }

                    if (matchedBoss != null)
                    {
                        ModBehaviour.DevLog("[BossRush] 当前波有一名Boss被击败");

                        // 处理Boss掉落随机化
                        CharacterMainControl bossMainControl = matchedBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
                else
                {
                    // 单Boss模式：保持原有逻辑
                    bool isCurrentBossDead = false;

                    if (CurrentBoss != null)
                    {
                        try
                        {
                            CharacterMainControl CurrentBossCharacter = CurrentBoss as CharacterMainControl;
                            if (CurrentBossCharacter != null && deadCharacter != null)
                            {
                                isCurrentBossDead = (CurrentBossCharacter == deadCharacter);
                            }
                        }
                        catch (Exception e)
                        {
                            ModBehaviour.DevLog("[BossRush] [WARNING] OnEnemyDied 比对当前Boss角色失败: " + e.Message);
                        }

                        if (!isCurrentBossDead)
                        {
                            try
                            {
                                isCurrentBossDead = (deadHealth.gameObject == ((MonoBehaviour)CurrentBoss).gameObject);
                            }
                            catch (Exception e)
                            {
                                ModBehaviour.DevLog("[BossRush] [WARNING] OnEnemyDied 比对当前Boss对象失败: " + e.Message);
                            }
                        }
                    }

                    if (CurrentBoss != null && isCurrentBossDead)
                    {
                        ModBehaviour.DevLog("[BossRush] 当前敌人已击败");
                        CharacterMainControl bossMainControl = CurrentBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] OnEnemyDied 错误: " + e.Message);
            }
        }

        internal void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)
        {
            try
            {
                if (!owner.IsActive || bossMain == null)
                {
                    return;
                }

                if (CountedDeadBosses.Contains(bossMain))
                {
                    return;
                }

                CountedDeadBosses.Add(bossMain);
                owner.UnregisterEnemyRecoveryForArena(bossMain);

                // 识别 Boss 类型并触发成就（同一角色实例只计一次，避免专用死亡回调和通用死亡流重复计数）
                owner.CheckBossKillAchievementsOnceForArena(bossMain);

                // ── 本波成员校验：这条线以下全是波次记账，非本波 Boss 一律不得越过 ──
                //
                // 本方法有三个调用点，前两个在 OnEnemyDiedWithDamageInfo 内、成员身份已由那里的
                // 比对证明；第三个是 OnBossBeforeSpawnLoot_LootAndRewards 的掉落漏斗，它只验了
                // 「在不在 bossSpawnTimes 里」，任何走共享刷怪核心的 Boss 都满足。于是随机事件的
                // 乱入 Boss（RndEvt_Intruder_*）死亡会被当成本波 Boss 死亡：波次提前推进，
                // ProceedAfterWaveFinished 还会把 CurrentBoss 置 null 把真 Boss 丢出状态机，
                // 玩家再打死它时又推一次波；最后一波则提前触发 OnAllEnemiesDefeated。
                //
                // 校验放在成就与去重之后：杀掉一只真 Boss 该算的成就照算，只是不参与波次账。
                if (!IsCurrentWaveBossMember(bossMain))
                {
                    string nonMemberName = "<unknown>";
                    try { nonMemberName = bossMain.gameObject.name; }
                    catch (Exception e)
                    {
                        // 读名字只为日志，读不到不影响拦截结论
                        ModBehaviour.DevLog("[BossRush] [WARNING] HandleBossDeath 读取非本波 Boss 名称失败: " + e.Message);
                    }
                    // 不打 [WARNING]：对乱入 Boss / Mode D / 孩儿护我龙裔来说，被拦下才是正常稳态，
                    // 打成警告会让正常流程长期刷屏。
                    ModBehaviour.DevLog("[BossRush] 非本波 Boss 死亡，跳过波次记账: " + nonMemberName
                        + "（bossesPerWave=" + BossesPerWave + ", modeDActive=" + owner.IsModeDActive + "）");
                    return;
                }

                // 无间炼狱：先累加现金池
                if (InfiniteHellMode)
                {
                    try
                    {
                        float maxHp = 0f;
                        if (bossMain.Health != null)
                        {
                            maxHp = bossMain.Health.MaxHealth;
                        }
                        if (maxHp < 0f) maxHp = 0f;
                        long reward = (long)Mathf.Round(maxHp * 10f);
                        if (reward < 0L) reward = 0L;
                        InfiniteHellCashPool += reward;
                        InfiniteHellWaveCashThisWave += reward;
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[BossRush] [WARNING] HandleBossDeath 计算无间炼狱现金池失败: " + e.Message);
                    }
                }

                if (BossesPerWave > 1 && CurrentWaveBosses != null && CurrentWaveBosses.Count > 0)
                {
                    for (int i = 0; i < CurrentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = CurrentWaveBosses[i];
                        if (boss == null)
                        {
                            continue;
                        }

                        CharacterMainControl bossCharacter = null;
                        try
                        {
                            bossCharacter = boss as CharacterMainControl;
                        }
                        catch (Exception e)
                        {
                            ModBehaviour.DevLog("[BossRush] [WARNING] HandleBossDeath 读取多Boss角色失败: " + e.Message);
                        }

                        if (bossCharacter == bossMain)
                        {
                            CurrentWaveBosses.RemoveAt(i);
                            break;
                        }
                    }
                }

                DefeatedEnemies++;

                if (BossesPerWave > 1)
                {
                    BossesInCurrentWaveRemaining = Mathf.Max(0, BossesInCurrentWaveRemaining - 1);

                    if (BossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                        return;
                    }
                }
                else
                {
                    // 单Boss模式：击杀后直接推进到下一波
                    ProceedAfterWaveFinished();
                    return;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] HandleBossDeath 错误: " + e.Message);
            }
        }

        /// <summary>
        /// 这只 Boss 是否属于本波，即它的死亡是否有资格推进波次。
        ///
        /// 比对口径逐字沿用 OnEnemyDiedWithDamageInfo 的既有语义：先比 CharacterMainControl 引用，
        /// 再回落 Health 与 gameObject 引用（多阶段 Boss 换过组件引用时仍能认出来）。
        ///
        /// 单 Boss 档**只认 CurrentBoss**：CurrentWaveBosses 会在 SpawnNextEnemy 的单 Boss 分支里
        /// 被清空，那时去查列表恒为空，会把真 Boss 也判成非成员。
        ///
        /// no-throw：任何比对异常都按「不是成员」处理（fail-closed），宁可漏推一次波
        /// 也不能让旁路 Boss 推波——漏推还有 TryFixStuckWaveIfNoBossAlive 自愈，误推没有回头路。
        /// </summary>
        private bool IsCurrentWaveBossMember(CharacterMainControl bossMain)
        {
            if (bossMain == null)
            {
                return false;
            }

            // Mode D 有独立的敌人死亡处理（RegisterModeDEnemyDeath / modeDCurrentWaveEnemies），
            // 口径与 OnEnemyDiedWithDamageInfo 开头那条 owner.IsModeDActive 早返一致。
            //
            // 为什么判在这里而不是 HandleBossDeath 开头：Mode D 会 BeginAchievementSession("ModeD")，
            // 而 Mode D 的 Boss 击杀成就**只**经 HandleBossDeath 里的 CheckBossKillAchievementsOnce
            // 计数（那是它全仓库仅有的两个调用点之一）。放在方法开头早返会把 Mode D 的
            // Boss 击杀成就整条掐掉。放在成员判定里则只挡波次记账，成就照常。
            //
            // 这条同时挡住 Mode D 里刷出的龙王/龙裔——它们会无条件写 CurrentBoss，
            // 光靠下面的容器比对挡不住。
            if (owner.IsModeDActive)
            {
                return false;
            }

            // 多 Boss 档先查本波列表；查不到再回落 CurrentBoss。
            // 回落不是冗余：CurrentBoss 在 SpawnEnemyAtPositionAsync 里是**无条件**赋值的
            // （CurrentWaveBosses.Add 才受 BossesPerWave > 1 门控），多 Boss 档它就是本波最后
            // 生成的那只。万一登记进列表那步失败，靠它仍能认出真 Boss，避免把真 Boss 判成
            // 非成员导致卡波。
            if (BossesPerWave > 1 && CurrentWaveBosses != null)
            {
                for (int i = 0; i < CurrentWaveBosses.Count; i++)
                {
                    if (IsSameWaveBossInstance(CurrentWaveBosses[i], bossMain))
                    {
                        return true;
                    }
                }
            }

            return IsSameWaveBossInstance(CurrentBoss, bossMain);
        }

        /// <summary>
        /// 单项比对：引用 -> Health -> gameObject，逐层 try/catch。
        /// 与 OnEnemyDiedWithDamageInfo 的多 Boss 分支同一套判据。
        /// </summary>
        private bool IsSameWaveBossInstance(MonoBehaviour tracked, CharacterMainControl bossMain)
        {
            if (tracked == null || bossMain == null)
            {
                return false;
            }

            try
            {
                CharacterMainControl trackedCharacter = tracked as CharacterMainControl;
                if (trackedCharacter != null && trackedCharacter == bossMain)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 本波成员比对角色失败: " + e.Message);
            }

            try
            {
                Health trackedHealth = tracked.GetComponent<Health>();
                if (trackedHealth != null && trackedHealth == bossMain.Health)
                {
                    return true;
                }
                if (tracked.gameObject == bossMain.gameObject)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 本波成员比对 Health/对象失败: " + e.Message);
            }

            return false;
        }

        /// <summary>
        /// 当当前波所有Boss被击杀或因生成失败/异常被跳过时，推进到下一波或结束挑战
        /// </summary>
        internal void ProceedAfterWaveFinished()
        {
            try
            {
                // 通知快递员 Boss 战结束
                owner.NotifyCourierBossFightEnd();

                // 通知快递员当前没有Boss（召唤间隔期间）
                owner.NotifyCourierNoBoss(true);

                CurrentEnemyIndex++;
                CurrentBoss = null;

                if (InfiniteHellMode)
                {
                    // 无间炼狱：统一走专用逻辑
                    OnInfiniteHellWaveCompleted_LootAndRewards();
                    return;
                }

                // 使用过滤后的 Boss 列表判断是否还有下一波
                var filteredPresets = owner.GetFilteredEnemyPresets();
                int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;

                if (CurrentEnemyIndex >= presetCount)
                {
                    owner.OnAllEnemiesDefeatedForArena();
                    return;
                }

                if (CurrentEnemyIndex < presetCount)
                {
                    if (owner.ArenaUsesInteractBetweenWaves)
                    {
                        try
                        {
                            if (owner.ArenaRewardSignInteract != null)
                            {
                                owner.ArenaRewardSignInteract.SetNextWaveMode();
                            }
                        }
                        catch (Exception e)
                        {
                            ModBehaviour.DevLog("[BossRush] [WARNING] ProceedAfterWaveFinished 设置下一波交互失败: " + e.Message);
                        }
                    }
                    else
                    {
                        StartNextWaveCountdown();
                    }
                }
                else
                {
                    owner.OnAllEnemiesDefeatedForArena();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] ProceedAfterWaveFinished 错误: " + e.Message);
            }
        }

        /// <summary>
        /// Boss 在生成阶段失败时的统一处理：修正当前波计数并在必要时推进波次
        /// </summary>
        internal void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            try
            {
                // 记录日志方便排查
                try
                {
                    string name = (preset != null ? preset.displayName : "<null>");
                    ModBehaviour.DevLog("[BossRush] OnBossSpawnFailed: Boss 生成失败, preset=" + name);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[BossRush] OnBossSpawnFailed 日志记录失败: " + e.Message);
                }

                // 递增已击败敌人数，保持总数一致
                DefeatedEnemies++;

                if (BossesPerWave > 1)
                {
                    // 多Boss模式：减少当前波剩余Boss数量
                    BossesInCurrentWaveRemaining = Mathf.Max(0, BossesInCurrentWaveRemaining - 1);

                    if (BossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                    }
                }
                else
                {
                    // 单Boss模式：视为跳过该敌人，直接进入下一波
                    ProceedAfterWaveFinished();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [ERROR] OnBossSpawnFailed 错误: " + e.Message);
            }
        }

    }
}
