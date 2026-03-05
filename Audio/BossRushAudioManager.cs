// ============================================================================
// BossRushAudioManager.cs - Boss Rush 音效管理器
// ============================================================================
// 模块说明：
//   集中管理音效播放
//   使用 ModBehaviour.PlaySoundEffect 播放音效
// ============================================================================

using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Boss Rush 音效管理器
    /// </summary>
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

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// 龙王BGM是否正在播放（防止多Boss时重复播放）
        /// </summary>
        private bool isDragonKingBGMPlaying = false;
        private string cachedNurseInteractSfxPath = null;
        private bool nurseInteractSfxChecked = false;
        private bool nurseInteractSfxExists = false;

        /// <summary>
        /// 播放龙王BGM（防止重复播放）
        /// </summary>
        public void PlayDragonKingBGM()
        {
            if (isDragonKingBGMPlaying) return;

            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return;

            string path = Path.Combine(modPath, "Assets", "Sounds", "DragonKing", "dragonking.mp3");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
                isDragonKingBGMPlaying = true;
            }
        }

        /// <summary>
        /// 重置龙王BGM播放状态（龙王死亡或场景切换时调用）
        /// </summary>
        public void ResetDragonKingBGMState()
        {
            isDragonKingBGMPlaying = false;
        }

        /// <summary>
        /// 按 NPC ID 播放交互音效
        /// </summary>
        public void PlayNPCInteractSFX(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;

            // 哥布林：保留现有语音音效
            if (npcId == GoblinAffinityConfig.NPC_ID)
            {
                PlayGoblinInteractSFX();
                return;
            }

            // 护士：仅在存在专属音效文件时播放，避免误播哥布林音效
            if (npcId == NurseAffinityConfig.NPC_ID)
            {
                if (TryGetNurseInteractSfxPath(out string path))
                {
                    ModBehaviour.Instance?.PlaySoundEffect(path);
                }
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

        /// <summary>
        /// 播放哥布林交互音效
        /// </summary>
        public void PlayGoblinInteractSFX()
        {
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return;

            string path = Path.Combine(modPath, "Assets", "Sounds", "Goblin", "goblin.wav");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
        }

        /// <summary>
        /// 播放重铸音效
        /// </summary>
        public void PlayReforgeSFX()
        {
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return;

            string path = Path.Combine(modPath, "Assets", "Sounds", "Goblin", "reforge.wav");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
        }
    }
}
