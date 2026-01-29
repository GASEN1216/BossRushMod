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
        /// 播放龙王BGM
        /// </summary>
        public void PlayDragonKingBGM()
        {
            string modPath = ModBehaviour.GetModPath();
            if (string.IsNullOrEmpty(modPath)) return;

            string path = Path.Combine(modPath, "Assets", "Sounds", "DragonKing", "dragonking.mp3");
            if (File.Exists(path))
            {
                ModBehaviour.Instance?.PlaySoundEffect(path);
            }
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
