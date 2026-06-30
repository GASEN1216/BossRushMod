using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Duckov.ItemUsage;

namespace BossRush
{
    public partial class ModBehaviour
    {
        public void TrySpawnEggForPlayer()
        {
            try
            {
                try
                {
                    TryPlayNgmSound();
                }
                catch {}

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null && playerCharacter != null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 无法找到玩家角色");
                    return;
                }

                SpawnEgg behavior = null;
                try
                {
                    behavior = cachedSpawnEggBehavior;
                }
                catch {}

                if (behavior == null)
                {
                    try
                    {
                        var all = Resources.FindObjectsOfTypeAll<SpawnEgg>();
                        if (all != null && all.Length > 0)
                        {
                            behavior = all[0];
                            cachedSpawnEggBehavior = behavior;
                        }
                    }
                    catch {}
                }

                if (behavior == null || behavior.eggPrefab == null)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 未找到 SpawnEgg 配置或 eggPrefab，跳过下蛋");
                    return;
                }

                try
                {
                    if (behavior.spawnCharacter != null)
                    {
                        eggSpawnPreset = behavior.spawnCharacter;
                    }
                }
                catch {}

                Egg egg = null;
                try
                {
                    egg = UnityEngine.Object.Instantiate<Egg>(behavior.eggPrefab, main.transform.position, Quaternion.identity);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 实例化蛋失败: " + e.Message);
                    return;
                }

                try
                {
                    Collider eggCol = null;
                    try { eggCol = egg.GetComponent<Collider>(); } catch {}
                    Collider playerCol = null;
                    try { playerCol = main.GetComponent<Collider>(); } catch {}
                    if (eggCol != null && playerCol != null)
                    {
                        Physics.IgnoreCollision(eggCol, playerCol, true);
                    }
                }
                catch {}

                try
                {
                    egg.Init(main.transform.position, main.CurrentAimDirection * 1f, main, behavior.spawnCharacter, behavior.eggSpawnDelay);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] TrySpawnEggForPlayer: 初始化蛋失败: " + e.Message);
                }
            }
            catch {}
        }

        private void TryPlayNgmSound()
        {
            try
            {
                string baseDir = null;
                try
                {
                    baseDir = info.path;
                }
                catch {}

                if (string.IsNullOrEmpty(baseDir))
                {
                    return;
                }

                string filePath = null;
                try
                {
                    string candidate1 = null;
                    string candidate2 = null;
                    try
                    {
                        string assetsDir = Path.Combine(baseDir, "Assets");
                        candidate1 = Path.Combine(assetsDir, "ngm.mp3");
                    }
                    catch {}
                    try
                    {
                        candidate2 = Path.Combine(baseDir, "ngm.mp3");
                    }
                    catch {}

                    try
                    {
                        if (!string.IsNullOrEmpty(candidate1) && File.Exists(candidate1))
                        {
                            filePath = candidate1;
                        }
                        else if (!string.IsNullOrEmpty(candidate2) && File.Exists(candidate2))
                        {
                            filePath = candidate2;
                        }
                    }
                    catch {}
                }
                catch {}

                if (string.IsNullOrEmpty(filePath))
                {
                    return;
                }

                GameObject target = null;
                try
                {
                    CharacterMainControl main = null;
                    try
                    {
                        main = CharacterMainControl.Main;
                    }
                    catch {}
                    if (main != null)
                    {
                        target = main.gameObject;
                    }
                }
                catch {}

                try
                {
                    PostCustomSfxDelegate postCustomSfx;
                    if (TryGetPostCustomSfxDelegate(out postCustomSfx))
                    {
                        postCustomSfx(filePath, target, false);
                        return;
                    }

                    System.Type audioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                    if (audioManagerType == null)
                    {
                        return;
                    }
                    var method = audioManagerType.GetMethod("PostCustomSFX", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method == null)
                    {
                        return;
                    }
                    object[] args = new object[] { filePath, target, false };
                    try
                    {
                        method.Invoke(null, args);
                    }
                    catch {}
                }
                catch {}
            }
            catch {}
        }

        private delegate void PostCustomSfxDelegate(string filePath, GameObject target, bool loop);

        private static readonly Dictionary<string, bool> _cachedSoundFileExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static PostCustomSfxDelegate _cachedPostCustomSfx;
        private static bool _postCustomSfxResolved = false;

        private static void ResetBossRushAudioHooksStaticCaches()
        {
            cachedSpawnEggBehavior = null;
            eggSpawnPreset = null;
            _cachedSoundFileExists.Clear();
            _cachedPostCustomSfx = null;
            _postCustomSfxResolved = false;
        }

        private static bool SoundFileExistsCached(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            bool exists;
            if (_cachedSoundFileExists.TryGetValue(filePath, out exists))
            {
                return exists;
            }

            exists = File.Exists(filePath);
            _cachedSoundFileExists[filePath] = exists;
            return exists;
        }

        private static bool TryGetPostCustomSfxDelegate(out PostCustomSfxDelegate postCustomSfx)
        {
            if (!_postCustomSfxResolved)
            {
                _postCustomSfxResolved = true;

                try
                {
                    Type audioManagerType = Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                    if (audioManagerType != null)
                    {
                        MethodInfo method = audioManagerType.GetMethod(
                            "PostCustomSFX",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[] { typeof(string), typeof(GameObject), typeof(bool) },
                            null);

                        if (method != null)
                        {
                            _cachedPostCustomSfx = Delegate.CreateDelegate(typeof(PostCustomSfxDelegate), method, false) as PostCustomSfxDelegate;
                        }
                    }
                }
                catch
                {
                    _cachedPostCustomSfx = null;
                }
            }

            postCustomSfx = _cachedPostCustomSfx;
            return postCustomSfx != null;
        }

        /// <summary>
        /// 播放自定义音效文件
        /// </summary>
        public void PlaySoundEffect(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !SoundFileExistsCached(filePath))
                {
                    DevLog("[BossRush] PlaySoundEffect: 音效文件不存在: " + filePath);
                    return;
                }

                GameObject target = null;
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null && main.gameObject.activeInHierarchy)
                    {
                        target = main.gameObject;
                    }
                }
                catch {}

                PostCustomSfxDelegate postCustomSfx;
                if (TryGetPostCustomSfxDelegate(out postCustomSfx))
                {
                    postCustomSfx(filePath, target, false);
                    return;
                }

                System.Type audioManagerType = System.Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                if (audioManagerType == null)
                {
                    DevLog("[BossRush] PlaySoundEffect: AudioManager类型未找到");
                    return;
                }

                var method = audioManagerType.GetMethod("PostCustomSFX", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    DevLog("[BossRush] PlaySoundEffect: PostCustomSFX方法未找到");
                    return;
                }

                object[] args = new object[] { filePath, target, false };
                method.Invoke(null, args);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] PlaySoundEffect异常: " + e.Message);
            }
        }
    }
}
