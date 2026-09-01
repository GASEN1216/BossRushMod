// ============================================================================
// BossRushAudioManager.cs - Boss Rush 音效管理器
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    public class BossRushAudioManager : MonoBehaviour
    {
        private static BossRushAudioManager _instance;

        public static BossRushAudioManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    if (ModBehaviour.Instance != null)
                    {
                        _instance = ModBehaviour.Instance.gameObject.GetComponent<BossRushAudioManager>();
                        if (_instance == null)
                        {
                            _instance = ModBehaviour.Instance.gameObject.AddComponent<BossRushAudioManager>();
                        }
                    }
                    else
                    {
                        GameObject go = new GameObject("BossRushAudioManager");
                        _instance = go.AddComponent<BossRushAudioManager>();
                        DontDestroyOnLoad(go);
                    }
                }

                return _instance;
            }
        }

        private bool isDragonKingBGMPlaying = false;
        private string cachedNurseInteractSfxPath = null;
        private bool nurseInteractSfxChecked = false;
        private bool nurseInteractSfxExists = false;
        private string cachedGoblinInteractSfxPath = null;
        private bool goblinInteractSfxChecked = false;
        private bool goblinInteractSfxExists = false;
        private readonly Dictionary<string, Action> npcInteractSfxHandlers = new Dictionary<string, Action>(StringComparer.Ordinal);
        private bool npcInteractHandlersInitialized = false;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            EnsureNPCInteractSfxHandlers();
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 龙王 BGM。曲目表里配了 DragonKing 条目就走 BossBgmCoordinator 的循环 BGM，
        /// 否则维持旧的 dragonking.mp3 一次性音效——零素材时行为与从前完全一致。
        /// 三个既有调用点（生成、死亡、场景清理）因此不需要任何改动。
        /// </summary>
        public void PlayDragonKingBGM()
        {
            if (isDragonKingBGMPlaying)
            {
                return;
            }

            if (BossBgmCoordinator.PlayBossBgm(BossBgmKeys.DragonKing))
            {
                isDragonKingBGMPlaying = true;
                return;
            }

            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                return;
            }

            string path = Path.Combine(modPath, "Assets", "Sounds", "DragonKing", "dragonking.mp3");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
                isDragonKingBGMPlaying = true;
            }
        }

        public void ResetDragonKingBGMState()
        {
            isDragonKingBGMPlaying = false;
            // 只停由本 Boss 起播的循环 BGM：多 Boss 波次里别掐掉别人的曲子
            BossBgmCoordinator.StopBossBgm(BossBgmKeys.DragonKing);
        }

        /// <summary>
        /// 通用 Boss 战 BGM 起播。曲目表没条目或文件缺失时静默无操作。
        /// 幂等：同一 Boss 重复调用不会把曲子掐回开头。
        /// </summary>
        public void PlayBossBGM(string bossKey)
        {
            BossBgmCoordinator.PlayBossBgm(bossKey);
        }

        public bool PlayBossBGM(string bossKey, UnityEngine.Object owner)
        {
            return BossBgmCoordinator.AcquireBossBgm(bossKey, owner);
        }

        /// <summary>
        /// 通用 Boss 战 BGM 停止。只在当前正播的就是该 Boss 时才真正停止。
        /// </summary>
        public void StopBossBGM(string bossKey)
        {
            BossBgmCoordinator.StopBossBgm(bossKey);
        }

        public void StopBossBGM(string bossKey, UnityEngine.Object owner)
        {
            BossBgmCoordinator.ReleaseBossBgm(bossKey, owner);
        }

        /// <summary>一次性 stinger（不占 BGM 通道，不打断正在放的循环曲）。</summary>
        public void PlayStinger(string eventKey)
        {
            BossBgmCoordinator.PlayStinger(eventKey);
        }

        public void PlayNPCInteractSFX(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return;
            }

            EnsureNPCInteractSfxHandlers();
            if (npcInteractSfxHandlers.TryGetValue(npcId, out Action handler))
            {
                handler?.Invoke();
            }
        }

        public void RegisterNPCInteractSFXHandler(string npcId, Action handler)
        {
            if (string.IsNullOrEmpty(npcId) || handler == null)
            {
                return;
            }

            EnsureNPCInteractSfxHandlers();
            npcInteractSfxHandlers[npcId] = handler;
        }

        public void UnregisterNPCInteractSFXHandler(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return;
            }

            npcInteractSfxHandlers.Remove(npcId);
        }

        private void EnsureNPCInteractSfxHandlers()
        {
            if (npcInteractHandlersInitialized)
            {
                return;
            }

            npcInteractHandlersInitialized = true;
            npcInteractSfxHandlers[GoblinAffinityConfig.NPC_ID] = PlayGoblinInteractSFX;
            npcInteractSfxHandlers[NurseAffinityConfig.NPC_ID] = PlayNurseInteractSFX;
        }

        private void PlayNurseInteractSFX()
        {
            if (TryGetNurseInteractSfxPath(out string path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
        }

        private bool TryGetNurseInteractSfxPath(out string path)
        {
            path = null;

            if (!nurseInteractSfxChecked)
            {
                nurseInteractSfxChecked = true;
                string modPath = ModBehaviour.GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    cachedNurseInteractSfxPath = Path.Combine(modPath, "Assets", "Sounds", "Nurse", "nurse.mp3");
                    nurseInteractSfxExists = File.Exists(cachedNurseInteractSfxPath);
                }
                else
                {
                    cachedNurseInteractSfxPath = null;
                    nurseInteractSfxExists = false;
                }
            }

            if (!nurseInteractSfxExists || string.IsNullOrEmpty(cachedNurseInteractSfxPath))
            {
                return false;
            }

            path = cachedNurseInteractSfxPath;
            return true;
        }

        public void PlayGoblinInteractSFX()
        {
            if (TryGetGoblinInteractSfxPath(out string path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
        }

        private bool TryGetGoblinInteractSfxPath(out string path)
        {
            path = null;

            if (!goblinInteractSfxChecked)
            {
                goblinInteractSfxChecked = true;
                string modPath = ModBehaviour.GetModPath();
                if (!string.IsNullOrEmpty(modPath))
                {
                    cachedGoblinInteractSfxPath = Path.Combine(modPath, "Assets", "Sounds", "Goblin", "goblin.wav");
                    goblinInteractSfxExists = File.Exists(cachedGoblinInteractSfxPath);
                }
                else
                {
                    cachedGoblinInteractSfxPath = null;
                    goblinInteractSfxExists = false;
                }
            }

            if (!goblinInteractSfxExists || string.IsNullOrEmpty(cachedGoblinInteractSfxPath))
            {
                return false;
            }

            path = cachedGoblinInteractSfxPath;
            return true;
        }

        public void PlayReforgeSFX()
        {
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                return;
            }

            string path = Path.Combine(modPath, "Assets", "Sounds", "Goblin", "reforge.wav");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
        }

        public void PlayHalberdZadiSFX()
        {
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                return;
            }

            string path = Path.Combine(modPath, "Assets", "Sounds", "items", "zadi.mp3");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
        }
    }
}
