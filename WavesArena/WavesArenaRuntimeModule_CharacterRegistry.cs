using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov;

namespace BossRush
{
    internal sealed partial class WavesArenaRuntimeModule
    {
        private readonly HashSet<CharacterMainControl> bossRushOwnedDaXingXing = new HashSet<CharacterMainControl>();
        private static List<CharacterMainControl> _cachedCharacters = new List<CharacterMainControl>();
        private static bool _characterCacheNeedsRefresh = true;
        private static float _characterCacheRefreshTimer = 0f;
        private const float CharacterCacheRefreshInterval = 10f; // 每 10 秒强制刷新一次缓存（从 5f 优化为 10f）
        private static readonly List<GameObject> _reusableDestroyList = new List<GameObject>(32);
        private static Vector3 _arenaCenter = Vector3.zero; // 竞技场中心位置（路牌位置）
        private static bool _arenaCenterSet = false; // 是否已设置竞技场中心

        private const float ARENA_RADIUS = ModBehaviour.ARENA_RADIUS;
        private const float DaXingXingCleanInterval = ModBehaviour.DaXingXingCleanInterval;
        internal HashSet<CharacterMainControl> OwnedDaXingXing { get { return bossRushOwnedDaXingXing; } }
        internal static List<CharacterMainControl> CharacterCache { get { return _cachedCharacters; } }
        internal static bool CharacterCacheNeedsRefresh { get { return _characterCacheNeedsRefresh; } set { _characterCacheNeedsRefresh = value; } }
        internal static List<GameObject> ReusableDestroyList { get { return _reusableDestroyList; } }
        internal static bool ArenaCenterSet { get { return _arenaCenterSet; } }
        internal static Vector3 ArenaCenter { get { return _arenaCenter; } }

        internal static void PrepareSceneCharacterCacheForLoad()
        {
            _characterCacheNeedsRefresh = true;
            _characterCacheRefreshTimer = 0f;
            _arenaCenterSet = false;
        }

        internal static void SetArenaCenterFromSign(Vector3 position)
        {
            _arenaCenter = position;
            _arenaCenterSet = true;
        }

        internal void SetArenaCenterFromMapConfig(string sceneName)
        {
            try
            {
                BossRushMapConfig mapConfig = ModBehaviour.GetMapConfigBySceneName(sceneName);
                if (mapConfig != null)
                {
                    // 优先使用默认路牌位置，其次使用自定义传送位置
                    if (mapConfig.defaultSignPos.HasValue)
                    {
                        _arenaCenter = mapConfig.defaultSignPos.Value;
                    }
                    else if (mapConfig.customSpawnPos.HasValue)
                    {
                        _arenaCenter = mapConfig.customSpawnPos.Value;
                    }
                    else if (mapConfig.spawnPoints != null && mapConfig.spawnPoints.Length > 0)
                    {
                        // 兜底：使用刷新点的中心位置
                        Vector3 sum = Vector3.zero;
                        for (int i = 0; i < mapConfig.spawnPoints.Length; i++)
                        {
                            sum += mapConfig.spawnPoints[i];
                        }
                        _arenaCenter = sum / mapConfig.spawnPoints.Length;
                    }
                    _arenaCenterSet = true;
                    ModBehaviour.DevLog("[BossRush] 已设置竞技场中心: " + _arenaCenter + " (场景=" + sceneName + ", 半径=" + ARENA_RADIUS + "m)");
                }
                else
                {
                    ModBehaviour.DevLog("[BossRush] 未找到场景 " + sceneName + " 的地图配置，范围限制未启用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] SetArenaCenterFromMapConfig 出错: " + e.Message);
            }
        }

        internal bool IsDaXingXingPreset(EnemyPresetInfo preset)
        {
            if (preset == null)
            {
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(preset.displayName) && (preset.displayName.Contains("大兴兴") || preset.displayName.Contains("小兴兴")))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(preset.name))
                {
                    if (preset.name.IndexOf("daxing", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch {}

            return false;
        }

        internal void TryCleanNonBossRushDaXingXing()
        {
            try
            {
                // [性能优化] 使用缓存的角色列表，避免每次都调用 FindObjectsOfType
                // 只有 DEMO 地图才需要定时刷新缓存（因为只有 DEMO 地图有原生大兴兴 spawner）
                // 其他地图只在场景加载时刷新一次即可
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                bool isDemoMap = (currentScene == "Level_DemoChallenge_1");

                // [性能优化] 范围限制参数
                bool useRangeLimit = _arenaCenterSet;
                float radiusSq = ARENA_RADIUS * ARENA_RADIUS;

                _characterCacheRefreshTimer += DaXingXingCleanInterval;
                if (_characterCacheNeedsRefresh || (isDemoMap && _characterCacheRefreshTimer >= CharacterCacheRefreshInterval))
                {
                    RefreshCharacterCache();
                    _characterCacheRefreshTimer = 0f;
                }

                // 清理缓存中已销毁的引用
                _cachedCharacters.RemoveAll(c => c == null);

                if (_cachedCharacters.Count == 0)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                // 清理 bossRushOwnedDaXingXing 中已经被销毁的引用
                // [性能优化] 使用 RemoveWhere 替代创建临时 List，减少 GC 压力
                try
                {
                    if (bossRushOwnedDaXingXing != null && bossRushOwnedDaXingXing.Count > 0)
                    {
                        bossRushOwnedDaXingXing.RemoveWhere(owned => owned == null);
                    }
                }
                catch {}

                foreach (var c in _cachedCharacters)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    // 跳过遗种巢随从：它是玩家方单位，只是沿用了官方 preset 的身份
                    // （clone 不改 nameKey，DisplayName 仍是「大兴兴」），会命中下面的
                    // 名字匹配。全仓四条 Destroy 扫描里另外三条都做了这个豁免，
                    // 漏掉这一条会让该血脉的崽入场即被销毁并循环重生。
                    if (PetNestCompanionAgent.IsCompanionCharacter(c))
                    {
                        continue;
                    }

                    // 跳过玩家角色
                    bool isMain = false;
                    try
                    {
                        if (main != null && c == main)
                        {
                            isMain = true;
                        }
                        else
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(c);
                        }
                    }
                    catch {}

                    if (isMain)
                    {
                        continue;
                    }

                    bool isDaXing = false;
                    try
                    {
                        CharacterRandomPreset preset = c.characterPreset;
                        if (preset != null)
                        {
                            string displayName = null;
                            try
                            {
                                displayName = preset.DisplayName;
                            }
                            catch {}

                            if (!string.IsNullOrEmpty(displayName) && (displayName.Contains("大兴兴") || displayName.Contains("小兴兴")))
                            {
                                isDaXing = true;
                            }
                            else
                            {
                                string key = null;
                                try
                                {
                                    key = preset.nameKey;
                                }
                                catch {}

                                if (!string.IsNullOrEmpty(key))
                                {
                                    if (key.IndexOf("daxing", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        isDaXing = true;
                                    }
                                }
                            }
                        }
                    }
                    catch {}

                    if (!isDaXing)
                    {
                        continue;
                    }

                    // BossRush 自己生成的大兴兴：保留
                    bool isOwnedByBossRush = false;
                    try
                    {
                        if (bossRushOwnedDaXingXing != null && bossRushOwnedDaXingXing.Contains(c))
                        {
                            isOwnedByBossRush = true;
                        }
                    }
                    catch {}

                    if (isOwnedByBossRush)
                    {
                        continue;
                    }

                    // Mode G owner 查询（加法分支）：Mode G 通过 staging preset/exact Character/
                    // committed handle 登记的本局角色必须保留，只删除真正外来实例。
                    // 查询默认 false、no-throw、O(1)；未启用 Mode G 时注册表为空且恒 false。
                    bool isOwnedByModeG = false;
                    try
                    {
                        isOwnedByModeG = ModeGRuntimeGates.IsDaXingXingOwnedByModeG(c);
                    }
                    catch
                    {
                        isOwnedByModeG = false;
                    }

                    if (isOwnedByModeG)
                    {
                        continue;
                    }

                    // [性能优化] 范围检查：只清理竞技场范围内的大兴兴
                    if (useRangeLimit && c.transform != null)
                    {
                        float distSq = (c.transform.position - _arenaCenter).sqrMagnitude;
                        if (distSq > radiusSq)
                        {
                            continue; // 超出范围，跳过
                        }
                    }

                    // 其余所有大兴兴都视为 DEMO 地图原生刷出的 BossRush 外来 Boss，直接清除
                    try
                    {
                        ModBehaviour.DevLog("[BossRush] 清理非 BossRush 源的大兴兴: goName=" + c.gameObject.name +
                               ", presetKey=" + (c.characterPreset != null ? c.characterPreset.nameKey : "<null>") +
                               ", scene=" + c.gameObject.scene.name +
                               ", pos=" + c.transform.position);
                    }
                    catch {}

                    try
                    {
                        UnityEngine.Object.Destroy(c.gameObject);
                    }
                    catch {}
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] TryCleanNonBossRushDaXingXing 出错: " + e.Message);
            }
        }

        internal static void RefreshCharacterCache()
        {
            try
            {
                var characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                _cachedCharacters.Clear();
                if (characters != null)
                {
                    _cachedCharacters.AddRange(characters);
                }
                _characterCacheNeedsRefresh = false;
                ModBehaviour.DevLog("[BossRush] 角色缓存已刷新，共 " + _cachedCharacters.Count + " 个角色");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] RefreshCharacterCache 出错: " + e.Message);
            }
        }
    }
}
