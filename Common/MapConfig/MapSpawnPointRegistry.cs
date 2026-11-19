// ============================================================================
// MapSpawnPointRegistry.cs - 地图刷新点注册表
// ============================================================================
// 模块说明：
//   OnAwake 时扫描 Assets/SpawnPoints/*.json，缓存为字典。
//   TryGet 返回 null 表示该地图无 JSON 或解析失败。
//   整份 JSON 经共享 BossRushJsonParser 解析成树，全部字段都从树上取（2026-11-19 起标量也不再在原文本上搜）。
//
// 设计约束：
//   - Initialize 在 OnAwake 阶段同步完成，不使用异步
//   - 解析失败时记录警告日志并跳过，不影响其他地图
//   - 使用 System.IO 进行文件操作
//   - 使用 ModBehaviour.DevLog 进行日志输出
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 地图刷新点注册表
    /// OnAwake 时扫描 Assets/SpawnPoints/*.json，缓存为字典
    /// TryGet 返回 null 表示该地图无 JSON 或解析失败
    /// </summary>
    internal sealed class MapSpawnPointRegistry
    {
        // 地图配置缓存（大小写不敏感）
        private readonly Dictionary<string, BossRushMapConfig> _configs
            = new Dictionary<string, BossRushMapConfig>(StringComparer.OrdinalIgnoreCase);
        private readonly List<BossRushMapConfig> _orderedConfigs = new List<BossRushMapConfig>();

        // JSON 文件目录路径
        private string _jsonDirectory;

        /// <summary>
        /// 初始化：扫描 JSON 目录并缓存
        /// 在 IBossRushRuntimeModule.OnAwake 阶段调用，同步完成
        /// </summary>
        public void Initialize(string modPath)
        {
            _jsonDirectory = Path.Combine(modPath, "Assets", "SpawnPoints");
            _configs.Clear();
            _orderedConfigs.Clear();

            if (!Directory.Exists(_jsonDirectory))
            {
                // 刷新点目录整个缺失 = 所有地图都拿不到配置，玩家侧表现为地图列表异常，必须有声
                ModBehaviour.CriticalLog(
                    "map-spawn-dir-missing",
                    "[MapSpawnPointRegistry] [ERROR] 目录不存在: " + _jsonDirectory);
                return;
            }

            string[] files = Directory.GetFiles(_jsonDirectory, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                TryLoadJson(file);
            }
            SortOrderedConfigs();

            ModBehaviour.DevLog("[MapSpawnPointRegistry] 已加载 " + _configs.Count + " 张地图配置");
        }

        /// <summary>
        /// 按 sceneName 查询，返回 null 表示未找到或解析失败
        /// </summary>
        public BossRushMapConfig TryGet(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return null;
            }

            BossRushMapConfig config;
            _configs.TryGetValue(sceneName, out config);
            return config;
        }

        /// <summary>
        /// 获取所有已加载的地图配置
        /// </summary>
        public IEnumerable<BossRushMapConfig> All()
        {
            return _orderedConfigs;
        }

        /// <summary>
        /// 尝试加载单个 JSON 文件并缓存
        /// 解析失败时记录警告日志并跳过
        /// </summary>
        private void TryLoadJson(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                BossRushMapConfig config = DeserializeFromJson(json);
                if (config != null && !string.IsNullOrEmpty(config.sceneName))
                {
                    if (_configs.ContainsKey(config.sceneName))
                    {
                        _orderedConfigs.RemoveAll(existing =>
                            existing != null &&
                            string.Equals(existing.sceneName, config.sceneName, StringComparison.OrdinalIgnoreCase));
                    }

                    _configs[config.sceneName] = config;
                    _orderedConfigs.Add(config);
                }
                else
                {
                    ModBehaviour.CriticalLog(
                        "map-spawn-invalid-" + filePath,
                        "[MapSpawnPointRegistry] [ERROR] JSON 解析结果无效: " + filePath);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.CriticalLog(
                    "map-spawn-parse-" + filePath,
                    "[MapSpawnPointRegistry] [ERROR] JSON 解析失败: "
                    + filePath + " - " + e.Message);
            }
        }

        private void SortOrderedConfigs()
        {
            _orderedConfigs.Sort((left, right) =>
            {
                int orderCompare = left.sortOrder.CompareTo(right.sortOrder);
                if (orderCompare != 0)
                {
                    return orderCompare;
                }

                return string.Compare(left.sceneName, right.sceneName, StringComparison.OrdinalIgnoreCase);
            });
        }

        #region JSON 反序列化

        /// <summary>
        /// 从 JSON 字符串反序列化为 BossRushMapConfig
        /// </summary>
        internal static BossRushMapConfig DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            BossRushJsonValue jsonRoot;
            string parseError;
            if (!BossRushJsonParser.TryParse(json, out jsonRoot, out parseError) || jsonRoot.Kind != BossRushJsonKind.Object) return null;

            // 字符串字段：缺失、null 或不是字符串时为 ""（previewImageName 为 null，区分「没配预览图」）
            string sceneName = jsonRoot.GetString("sceneName", "");
            string sceneID = jsonRoot.GetString("sceneID", "");
            string displayNameCN = jsonRoot.GetString("displayNameCN", "");
            string displayNameEN = jsonRoot.GetString("displayNameEN", "");
            string previewImageName = jsonRoot.GetString("previewImageName", null);

            // 整数字段：缺失或不是数字时取默认（sortOrder 未配置的新地图排到末尾）
            int sortOrder = jsonRoot.GetInt("sortOrder", int.MaxValue);
            int beaconIndex = jsonRoot.GetInt("beaconIndex", 0);

            // 提取 Vector3 数组字段
            Vector3[] spawnPoints = ExtractVector3Array(jsonRoot, "spawnPoints");
            Vector3[] modeESpawnPoints = ExtractVector3Array(jsonRoot, "modeESpawnPoints");
            Vector3[] modeHSpawnPoints = ExtractVector3Array(jsonRoot, "modeHSpawnPoints");

            // 提取可空 Vector3 字段
            Vector3? customSpawnPos = ExtractNullableVector3(jsonRoot, "customSpawnPos");
            Vector3? defaultSignPos = ExtractNullableVector3(jsonRoot, "defaultSignPos");
            Vector3? modeEPlayerSpawnPos = ExtractNullableVector3(jsonRoot, "modeEPlayerSpawnPos");
            Vector3? modeHStagingPos = ExtractNullableVector3(jsonRoot, "modeHStagingPos");
            Vector3? modeHSpectatorPos = ExtractNullableVector3(jsonRoot, "modeHSpectatorPos");
            Vector3? modeHPlayerSpawnPos = ExtractNullableVector3(jsonRoot, "modeHPlayerSpawnPos");
            Vector3? modeHExitPos = ExtractNullableVector3(jsonRoot, "modeHExitPos");

            // 提取 mapNorth（必填，默认使用 DEMO 竞技场北方向量）
            Vector3? mapNorthNullable = ExtractNullableVector3(jsonRoot, "mapNorth");
            Vector3 mapNorth = mapNorthNullable.HasValue
                ? mapNorthNullable.Value
                : new Vector3(-0.959f, 0f, 0.284f);

            if (string.IsNullOrEmpty(sceneName))
            {
                return null;
            }

            if (string.IsNullOrEmpty(sceneID)
                || string.IsNullOrEmpty(displayNameCN)
                || string.IsNullOrEmpty(displayNameEN)
                || spawnPoints == null || spawnPoints.Length == 0)
            {
                return null;
            }

            return new BossRushMapConfig(
                sceneName,
                sceneID,
                displayNameCN,
                displayNameEN,
                spawnPoints,
                customSpawnPos,
                defaultSignPos,
                beaconIndex,
                previewImageName,
                mapNorth,
                modeESpawnPoints,
                modeEPlayerSpawnPos,
                sortOrder,
                modeHSpawnPoints,
                modeHStagingPos,
                modeHSpectatorPos,
                modeHPlayerSpawnPos,
                modeHExitPos
            );
        }

        #endregion

        #region JSON 反序列化辅助方法

        /// <summary>
        /// 从 JSON 中提取可空 Vector3 值
        /// 格式: [x, y, z] 或 null
        /// </summary>
        private static Vector3? ExtractNullableVector3(BossRushJsonValue root, string key)
        {
            BossRushJsonValue point = root.GetProperty(key);
            Vector3? value = ReadVector3(point);
            if (!value.HasValue && point != null && point.Kind != BossRushJsonKind.Null)
                ModBehaviour.DevLog("[MapSpawnPointRegistry] [WARNING] 无效坐标，使用默认落点: " + key);
            return value;
        }

        private static Vector3[] ExtractVector3Array(BossRushJsonValue root, string key)
        {
            List<BossRushJsonValue> points;
            if (!root.TryGetArray(key, out points) || points.Count == 0) return null;
            var result = new List<Vector3>();
            foreach (BossRushJsonValue point in points)
            {
                Vector3? value = ReadVector3(point);
                // 不把坏分量默认为世界原点；整组无效会走现有地图校验/后备。
                if (!value.HasValue) return null;
                result.Add(value.Value);
            }
            return result.ToArray();
        }

        private static Vector3? ReadVector3(BossRushJsonValue point)
        {
            if (point == null || point.Kind != BossRushJsonKind.Array ||
                point.Items == null || point.Items.Count != 3) return null;
            float x = point.Items[0] == null ? float.NaN : point.Items[0].AsFloat(float.NaN);
            float y = point.Items[1] == null ? float.NaN : point.Items[1].AsFloat(float.NaN);
            float z = point.Items[2] == null ? float.NaN : point.Items[2].AsFloat(float.NaN);
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) return null;
            return new Vector3(x, y, z);
        }

        #endregion
    }
}
