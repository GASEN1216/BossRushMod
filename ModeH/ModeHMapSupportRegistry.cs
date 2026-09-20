using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一张通过审计的 Mode H 地图记录（不可变）。
    /// </summary>
    public sealed class ModeHSupportedMap
    {
        /// <summary>运行时场景名。</summary>
        public string SceneName;
        /// <summary>加载用场景 ID。</summary>
        public string SceneId;
        /// <summary>显示名。</summary>
        public string DisplayName;
        /// <summary>擂台刷怪点。</summary>
        public Vector3[] ArenaSpawnPoints;
        /// <summary>隔离生成点。</summary>
        public Vector3 StagingPos;
        /// <summary>看台点。</summary>
        public Vector3 SpectatorPos;
        /// <summary>玩家落点。</summary>
        public Vector3 PlayerSpawnPos;
        /// <summary>安全离场点。</summary>
        public Vector3 ExitPos;
        /// <summary>擂台中心（由刷怪点求均值，供 center 口令点火使用）。</summary>
        public Vector3 ArenaCenter;

        /// <summary>
        /// 点位是**派生**的还是地图 JSON 显式给的。
        /// 派生 = 从该图已验证的 Boss 刷新点选出来的（见 ModeHMapSupportRegistry.TryDeriveMap），
        /// 玩法上等价，但没有经过实机逐点核对，诊断与验收报告需要分得清。
        /// </summary>
        public bool Derived;

        /// <summary>
        /// 擂台半径：由中心到最远刷怪点的距离再留一倍余量。
        /// 战场快照重建的位置可用性判定使用同一个定义，避免出现第二套边界。
        /// </summary>
        public float ArenaRadius
        {
            get
            {
                if (ArenaSpawnPoints == null || ArenaSpawnPoints.Length == 0) return 0f;
                float max = 0f;
                for (int i = 0; i < ArenaSpawnPoints.Length; i++)
                {
                    float distance = Vector3.Distance(ArenaCenter, ArenaSpawnPoints[i]);
                    if (distance > max) max = distance;
                }
                return max * 2f;
            }
        }

        /// <summary>位置是否落在擂台边界内（快照重建的位置可用性判据）。</summary>
        public bool IsInsideArena(Vector3 position)
        {
            float radius = ArenaRadius;
            if (radius <= 0f) return false;
            return Vector3.Distance(ArenaCenter, position) <= radius;
        }
    }

    /// <summary>
    /// Mode H 地图支持注册表（设计提案 §19.1、§25.1）。
    ///
    /// 与 Mode G 的地图注册表同形：不维护第二份地图清单，只从
    /// ModBehaviour.GetAllMapConfigs() 构建，并要求地图 JSON 提供 Mode H 可选点位：
    /// modeHSpawnPoints / modeHStagingPos / modeHSpectatorPos / modeHPlayerSpawnPos / modeHExitPos。
    ///
    /// 没有有效擂台、staging 或看台点位就拒绝进入，绝不把普通 BossRush 刷新点猜作替代。
    /// </summary>
    public static class ModeHMapSupportRegistry
    {
        #region 状态

        private static readonly object _lock = new object();
        private static Dictionary<string, ModeHSupportedMap> _mapsByScene;
        private static string _lastError;

        /// <summary>staging 点与擂台/看台的最小隔离距离（米），低于该值判定点位无效。</summary>
        public const float MinStagingIsolationDistance = 30f;

        #endregion

        #region 只读

        /// <summary>最后一次审计失败原因。</summary>
        public static string LastError { get { return _lastError; } }

        #endregion

        #region 构建

        /// <summary>清空缓存（地图配置变化或 Mod 重载时调用）。</summary>
        public static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _mapsByScene = null;
                _lastError = null;
            }
        }

        private static Dictionary<string, ModeHSupportedMap> GetOrBuild()
        {
            lock (_lock)
            {
                if (_mapsByScene != null) return _mapsByScene;
            }

            Dictionary<string, ModeHSupportedMap> maps =
                new Dictionary<string, ModeHSupportedMap>(StringComparer.Ordinal);
            try
            {
                BossRushMapConfig[] configs = ModBehaviour.GetAllMapConfigs();
                if (configs == null || configs.Length == 0)
                {
                    _lastError = "map_configs_empty";
                    return maps; // 空结果不缓存，允许 OnAwake 之后重试
                }

                for (int i = 0; i < configs.Length; i++)
                {
                    BossRushMapConfig config = configs[i];
                    ModeHSupportedMap map = TryBuildMap(config);
                    if (map == null) continue;
                    maps[map.SceneName] = map;
                }
            }
            catch (Exception e)
            {
                _lastError = "map_scan_exception:" + e.GetType().Name;
                return maps;
            }

            if (maps.Count == 0)
            {
                _lastError = "map_no_modeh_points";
                return maps; // 同样不缓存空结果
            }

            lock (_lock)
            {
                _mapsByScene = maps;
            }
            return maps;
        }

        private static ModeHSupportedMap TryBuildMap(BossRushMapConfig config)
        {
            if (config == null) return null;
            if (string.IsNullOrEmpty(config.sceneName) || string.IsNullOrEmpty(config.sceneID)) return null;

            // 地图 JSON 显式给了 Mode H 点位（owner 实机调过的那一套）时一律以它为准；
            // 没给的图走派生（2026-09-20 owner：「地图选择器里已有的所有图都接入新模式」）。
            if (config.modeHSpawnPoints == null || config.modeHSpawnPoints.Length == 0
                || !config.modeHStagingPos.HasValue || !config.modeHSpectatorPos.HasValue
                || !config.modeHPlayerSpawnPos.HasValue || !config.modeHExitPos.HasValue)
            {
                return TryDeriveMap(config);
            }

            Vector3 staging = config.modeHStagingPos.Value;
            Vector3 spectator = config.modeHSpectatorPos.Value;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < config.modeHSpawnPoints.Length; i++)
            {
                center += config.modeHSpawnPoints[i];
            }
            center /= config.modeHSpawnPoints.Length;

            // staging 必须与擂台和看台保持实机审计后的隔离距离（§19.1）
            if (Vector3.Distance(staging, center) < MinStagingIsolationDistance) return null;
            if (Vector3.Distance(staging, spectator) < MinStagingIsolationDistance) return null;

            ModeHSupportedMap map = new ModeHSupportedMap();
            map.SceneName = config.sceneName;
            map.SceneId = config.sceneID;
            map.DisplayName = config.displayName;
            map.ArenaSpawnPoints = config.modeHSpawnPoints;
            map.StagingPos = staging;
            map.SpectatorPos = spectator;
            map.PlayerSpawnPos = config.modeHPlayerSpawnPos.Value;
            map.ExitPos = config.modeHExitPos.Value;
            map.ArenaCenter = center;
            map.Derived = false;
            return map;
        }

        #region 从已验证刷新点派生擂台

        /// <summary>擂台半径的可接受区间（米）：太小塞不下四个角色，太大就不是擂台了。</summary>
        internal const float MinDerivedArenaSpread = 4f;
        internal const float MaxDerivedArenaSpread = 26f;

        /// <summary>看台与擂台边缘之间的可接受距离区间（米）。</summary>
        internal const float MinSpectatorGap = 2f;
        internal const float MaxSpectatorGap = 30f;

        /// <summary>擂台点数（与 DemoChallenge 的人工点位一致）。</summary>
        internal const int DerivedArenaPointCount = 4;

        /// <summary>
        /// 派生隔离点的地下深度（米）。沿用遗种巢 staging 的成熟做法
        /// （PetNestCompanionSpawner.StagingOffset 也是 -240）：角色在 staging 点是
        /// **inactive + invincible** 创建的，不需要地面，也不会被任何人看到，
        /// 因此地下深处比「地图另一头的某个刷新点」更干净，小地图也一定满足隔离距离。
        /// </summary>
        internal const float DerivedStagingDepth = 240f;

        /// <summary>
        /// 从该图已有 Boss 刷新点与玩家落点派生一套 Mode H 点位。
        ///
        /// `spawnPoints` 沿用 BossRush 的刷怪坐标，`customSpawnPos` 沿用玩家入场点。
        /// 角色落点只选这些实点，均值仅用于范围计算；点位之间的连通性与看台视野仍需实机验收。
        /// 任何一步找不到合格点就返回 null（fail-closed），不凭空补角色落点。
        ///
        /// 选点规则：
        ///   候选  = 展开度落在合理区间内且最紧凑的 5 个实点；
        ///   中心  = 这 5 点的均值，只用于范围与口令；
        ///   落点  = 离中心最近的候选点给斗士，其余 4 点给对手；
        ///   看台  = 五点之外、离中心最近且仍在合理间距内的实点；
        ///   隔离  = 擂台中心正下方 DerivedStagingDepth 米（角色在那儿是 inactive 创建的，
        ///           不需要地面；小地图也不会因为「找不到足够远的刷新点」被拒）。
        /// </summary>
        internal static ModeHSupportedMap TryDeriveMap(BossRushMapConfig config)
        {
            if (config == null) return null;
            Vector3[] points = config.spawnPoints;
            if (points == null || points.Length < DerivedArenaPointCount + 2) return null;

            // 已配置的玩家落点同样是可用实点；37 号实验区的中心落点能补齐紧凑五席。
            if (config.customSpawnPos.HasValue)
            {
                Vector3 playerPoint = config.customSpawnPos.Value;
                bool duplicate = false;
                for (int i = 0; i < points.Length; i++)
                    if (Vector3.Distance(points[i], playerPoint) < 0.1f) duplicate = true;
                if (!duplicate)
                {
                    Vector3[] expanded = new Vector3[points.Length + 1];
                    Array.Copy(points, expanded, points.Length);
                    expanded[points.Length] = playerPoint;
                    points = expanded;
                }
            }

            float spread;
            int[] arenaIndices = PickArenaCluster(points, DerivedArenaPointCount + 1, out spread);
            if (arenaIndices == null) return null;

            Vector3[] arena = new Vector3[DerivedArenaPointCount];
            Vector3 center = Vector3.zero;
            for (int i = 0; i < arenaIndices.Length; i++)
                center += points[arenaIndices[i]];
            center /= arenaIndices.Length;

            // 均值只计算范围，不能作为落点：两个地面点之间可能是墙、坑或楼层间隙。
            // 斗士占离中心最近的真实刷新点，其余四点给对手，避免重叠生成。
            int fighterIndex = 0;
            for (int i = 1; i < arenaIndices.Length; i++)
                if (Vector3.Distance(points[arenaIndices[i]], center)
                    < Vector3.Distance(points[arenaIndices[fighterIndex]], center)) fighterIndex = i;
            int arenaSlot = 0;
            for (int i = 0; i < arenaIndices.Length; i++)
                if (i != fighterIndex) arena[arenaSlot++] = points[arenaIndices[i]];

            // 看台：擂台之外、离中心最近的那个已验证点
            int spectatorIndex = -1;
            float spectatorDistance = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                if (Contains(arenaIndices, i)) continue;
                float distance = Vector3.Distance(center, points[i]);
                if (distance >= spread + MinSpectatorGap && distance <= spread + MaxSpectatorGap
                    && distance < spectatorDistance)
                {
                    spectatorDistance = distance;
                    spectatorIndex = i;
                }
            }
            if (spectatorIndex < 0) return null;

            Vector3 spectator = points[spectatorIndex];
            Vector3 staging = center + new Vector3(0f, -DerivedStagingDepth, 0f);

            // 与人工点位同一条隔离判据，派生不得放宽
            if (Vector3.Distance(staging, center) < MinStagingIsolationDistance) return null;
            if (Vector3.Distance(staging, spectator) < MinStagingIsolationDistance) return null;

            // 离场点优先用玩家自定义传送点（已验证的落地位置），否则回落隔离点
            Vector3 exit = config.customSpawnPos.HasValue
                ? config.customSpawnPos.Value
                : (config.modeEPlayerSpawnPos.HasValue ? config.modeEPlayerSpawnPos.Value : spectator);

            ModeHSupportedMap map = new ModeHSupportedMap();
            map.SceneName = config.sceneName;
            map.SceneId = config.sceneID;
            map.DisplayName = config.displayName;
            map.ArenaSpawnPoints = arena;
            map.StagingPos = staging;
            map.SpectatorPos = spectator;
            map.PlayerSpawnPos = points[arenaIndices[fighterIndex]];
            map.ExitPos = exit;
            map.ArenaCenter = center;
            map.Derived = true;
            return map;
        }

        /// <summary>
        /// 挑 count 个点组成擂台：对每个点取它的 count-1 个最近邻，
        /// 在**展开度落在 [MinDerivedArenaSpread, MaxDerivedArenaSpread] 内**的候选里
        /// 选最紧凑的一组。只取全局最紧凑会被几个挤在一起的刷新点带偏
        /// （零度挑战图的最紧四点展开度只有 3.9 米，塞不下四个角色）。
        /// O(n²·count)，n 是刷新点数（最多几十），只在注册表首次构建时跑一次。
        /// </summary>
        private static int[] PickArenaCluster(Vector3[] points, int count, out float bestSpread)
        {
            bestSpread = 0f;
            if (points == null || points.Length < count || count <= 0) return null;

            int[] best = null;
            float bestScore = float.MaxValue;
            int[] candidate = new int[count];
            float[] candidateDistance = new float[count];

            for (int seed = 0; seed < points.Length; seed++)
            {
                candidate[0] = seed;
                candidateDistance[0] = 0f;
                int filled = 1;

                for (int i = 0; i < points.Length; i++)
                {
                    if (i == seed) continue;
                    float distance = Vector3.Distance(points[seed], points[i]);

                    if (filled < count)
                    {
                        candidate[filled] = i;
                        candidateDistance[filled] = distance;
                        filled++;
                        SortCandidate(candidate, candidateDistance, filled);
                        continue;
                    }
                    if (distance >= candidateDistance[count - 1]) continue;
                    candidate[count - 1] = i;
                    candidateDistance[count - 1] = distance;
                    SortCandidate(candidate, candidateDistance, count);
                }
                if (filled < count) continue;

                // 展开度按「到这组均值的最大距离」算，与 TryDeriveMap 里的口径一致
                Vector3 clusterCenter = Vector3.zero;
                for (int i = 0; i < count; i++) clusterCenter += points[candidate[i]];
                clusterCenter /= count;
                float spread = 0f;
                for (int i = 0; i < count; i++)
                {
                    float distance = Vector3.Distance(clusterCenter, points[candidate[i]]);
                    if (distance > spread) spread = distance;
                }
                if (spread < MinDerivedArenaSpread || spread > MaxDerivedArenaSpread) continue;

                float score = 0f;
                for (int i = 1; i < count; i++) score += candidateDistance[i];
                if (score >= bestScore) continue;

                bestScore = score;
                bestSpread = spread;
                if (best == null) best = new int[count];
                Array.Copy(candidate, best, count);
            }
            return best;
        }

        /// <summary>按距离升序整理候选（count 最多 4，插入排序足够）。</summary>
        private static void SortCandidate(int[] indices, float[] distances, int length)
        {
            for (int i = 1; i < length; i++)
            {
                int index = indices[i];
                float distance = distances[i];
                int j = i - 1;
                while (j >= 1 && distances[j] > distance)
                {
                    indices[j + 1] = indices[j];
                    distances[j + 1] = distances[j];
                    j--;
                }
                indices[j + 1] = index;
                distances[j + 1] = distance;
            }
        }

        private static bool Contains(int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value) return true;
            }
            return false;
        }

        #endregion

        #endregion

        #region 查询

        /// <summary>该场景是否支持 Mode H。</summary>
        public static bool IsSupportedScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return GetOrBuild().ContainsKey(sceneName);
        }

        /// <summary>该 scene pair 是否支持 Mode H。</summary>
        public static bool IsSupportedPair(string sceneName, string sceneId)
        {
            ModeHSupportedMap map;
            if (!TryGetMap(sceneName, out map)) return false;
            if (string.IsNullOrEmpty(sceneId)) return true;
            return string.Equals(map.SceneId, sceneId, StringComparison.Ordinal);
        }

        /// <summary>取地图记录。</summary>
        public static bool TryGetMap(string sceneName, out ModeHSupportedMap map)
        {
            map = null;
            if (string.IsNullOrEmpty(sceneName)) return false;
            Dictionary<string, ModeHSupportedMap> maps = GetOrBuild();
            return maps.TryGetValue(sceneName, out map);
        }

        /// <summary>
        /// 入口页的默认目标地图。**优先取地图 JSON 显式给了点位的那一张**
        /// （owner 实机调过的擂台），都没有时才退到派生地图的 ordinal 第一张。
        /// 不这么排的话，2026-09-20 接入全部地图之后默认目标会从人工调过的
        /// DemoChallenge 静默漂到 ordinal 最靠前的派生地图。
        /// 玩家随后仍可在地图选择界面里改选（SetPendingMapEntryIndex 会重绑 intent）。
        /// </summary>
        public static bool TryGetPrimaryMap(out ModeHSupportedMap map)
        {
            map = null;
            Dictionary<string, ModeHSupportedMap> maps = GetOrBuild();
            if (maps.Count == 0) return false;

            List<string> names = new List<string>(maps.Keys);
            names.Sort(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                ModeHSupportedMap candidate = maps[names[i]];
                if (candidate != null && !candidate.Derived)
                {
                    map = candidate;
                    return true;
                }
            }
            map = maps[names[0]];
            return true;
        }

        /// <summary>受支持地图数量。</summary>
        public static int SupportedMapCount
        {
            get { return GetOrBuild().Count; }
        }

        /// <summary>全部受支持场景名（ordinal 升序）。</summary>
        public static List<string> GetSupportedSceneNames()
        {
            List<string> names = new List<string>(GetOrBuild().Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        #endregion
    }
}
