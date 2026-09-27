using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class MutatorBossRegenRuntime
    {
        private readonly List<MonoBehaviour> _singleBossRegenList = new List<MonoBehaviour>(1);
        private Func<bool> getIsActive;
        private bool IsActive { get { return getIsActive(); } }
        private Func<int> getbossesPerWave;
        private int bossesPerWave { get { return getbossesPerWave(); } }
        private Func<MonoBehaviour> getcurrentBoss;
        private MonoBehaviour currentBoss { get { return getcurrentBoss(); } }
        private Func<List<MonoBehaviour>> getcurrentWaveBosses;
        private List<MonoBehaviour> currentWaveBosses { get { return getcurrentWaveBosses(); } }
        private Func<bool> getmodeDActive;
        private bool modeDActive { get { return getmodeDActive(); } }
        private Func<List<CharacterMainControl>> getmodeDCurrentWaveEnemies;
        private List<CharacterMainControl> modeDCurrentWaveEnemies { get { return getmodeDCurrentWaveEnemies(); } }
        private Func<bool> getmodeEActive;
        private bool modeEActive { get { return getmodeEActive(); } }
        private Func<List<CharacterMainControl>> getModeEAliveEnemies;
        private List<CharacterMainControl> ModeEAliveEnemies { get { return getModeEAliveEnemies(); } }
        private Func<List<MonoBehaviour>> GetModeEBossRegenCache;
        private Func<bool> getmodeFActive;
        private bool modeFActive { get { return getmodeFActive(); } }
        private Func<HashSet<CharacterMainControl>> getmodeFActiveBossSet;
        private HashSet<CharacterMainControl> modeFActiveBossSet { get { return getmodeFActiveBossSet(); } }
        private Func<List<MonoBehaviour>> GetModeFBossRegenCache;

        internal void BindArenaQueries(Func<bool> IsActive, Func<int> bossesPerWave, Func<MonoBehaviour> currentBoss, Func<List<MonoBehaviour>> currentWaveBosses)
        {
            getIsActive = IsActive;
            getbossesPerWave = bossesPerWave;
            getcurrentBoss = currentBoss;
            getcurrentWaveBosses = currentWaveBosses;
        }

        internal void BindModeDQueries(Func<bool> modeDActive, Func<List<CharacterMainControl>> modeDCurrentWaveEnemies)
        {
            getmodeDActive = modeDActive;
            getmodeDCurrentWaveEnemies = modeDCurrentWaveEnemies;
        }

        internal void BindModeEQueries(Func<bool> modeEActive, Func<List<CharacterMainControl>> ModeEAliveEnemies, Func<List<MonoBehaviour>> GetModeEBossRegenCache)
        {
            getmodeEActive = modeEActive;
            getModeEAliveEnemies = ModeEAliveEnemies;
            this.GetModeEBossRegenCache = GetModeEBossRegenCache;
        }

        internal void BindModeFQueries(Func<bool> modeFActive, Func<HashSet<CharacterMainControl>> modeFActiveBossSet, Func<List<MonoBehaviour>> GetModeFBossRegenCache)
        {
            getmodeFActive = modeFActive;
            getmodeFActiveBossSet = modeFActiveBossSet;
            this.GetModeFBossRegenCache = GetModeFBossRegenCache;
        }

        internal void Tick()
        {
            if (MutatorManager.BossRegenEnabled)
            {
                if (IsActive)
                {
                    if (bossesPerWave > 1)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, currentWaveBosses);
                    }
                    else if (currentBoss != null)
                    {
                        // 复用静态临时列表避免每帧分配
                        _singleBossRegenList.Clear();
                        _singleBossRegenList.Add(currentBoss);
                        MutatorManager.TickBossRegen(Time.deltaTime, _singleBossRegenList);
                    }
                }
                if (modeDActive && modeDCurrentWaveEnemies.Count > 0)
                {
                    // Mode D：把当前波次所有存活敌人都喂给 BossRegen
                    // （Mode D 的"敌人"逻辑上都是 Boss 池里的角色，回血一致处理）
                    _singleBossRegenList.Clear();
                    for (int i = 0; i < modeDCurrentWaveEnemies.Count; i++)
                    {
                        CharacterMainControl boss = modeDCurrentWaveEnemies[i];
                        if (boss != null) _singleBossRegenList.Add(boss);
                    }
                    if (_singleBossRegenList.Count > 0)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, _singleBossRegenList);
                    }
                }
                else if (modeEActive && ModeEAliveEnemies != null && ModeEAliveEnemies.Count > 0)
                {
                    // Mode E：所有阵营的 Boss 都回血（设计上对称，所有阵营吃同一条规则）
                    List<MonoBehaviour> modeERegenCache = GetModeEBossRegenCache();
                    if (modeERegenCache.Count > 0)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, modeERegenCache);
                    }
                }
                else if (modeFActive && modeFActiveBossSet.Count > 0)
                {
                    // Mode F：HashSet 转列表喂入。Mode F 已经在用 BleedRateMultiplier，
                    // 这里再补 BossRegen 让两个环境规则词条都能在血猎模式生效
                    List<MonoBehaviour> modeFRegenCache = GetModeFBossRegenCache();
                    if (modeFRegenCache.Count > 0)
                    {
                        MutatorManager.TickBossRegen(Time.deltaTime, modeFRegenCache);
                    }
                }
            }
        }
    }
}
