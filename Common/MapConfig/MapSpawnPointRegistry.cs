// ============================================================================
// MapSpawnPointRegistry.cs - 地图刷新点注册表
// ============================================================================
// 模块说明：
//   OnAwake 时扫描 Assets/SpawnPoints/*.json，缓存为字典。
//   TryGet 返回 null 表示该地图无 JSON 或解析失败。
//   ExportFromHardcoded 用于初次迁移时从硬编码表反向导出 JSON 文件。
//
// 设计约束：
//   - Initialize 在 OnAwake 阶段同步完成，不使用异步
//   - 解析失败时记录警告日志并跳过，不影响其他地图
//   - 使用 System.IO 进行文件操作
//   - 使用 ModBehaviour.DevLog 进行日志输出
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
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
                ModBehaviour.DevLog("[MapSpawnPointRegistry] [WARNING] 目录不存在: " + _jsonDirectory);
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
        /// 反向导出器：把硬编码表导出为 JSON 文件
        /// 用于初次迁移时生成 Assets/SpawnPoints/*.json
        /// </summary>
        public void ExportFromHardcoded(IEnumerable<BossRushMapConfig> hardcodedConfigs)
        {
            if (string.IsNullOrEmpty(_jsonDirectory))
            {
                ModBehaviour.DevLog("[MapSpawnPointRegistry] [WARNING] ExportFromHardcoded: _jsonDirectory 未初始化");
                return;
            }

            if (!Directory.Exists(_jsonDirectory))
            {
                Directory.CreateDirectory(_jsonDirectory);
            }

            int count = 0;
            foreach (var config in hardcodedConfigs)
            {
                if (config == null || string.IsNullOrEmpty(config.sceneName))
                {
                    continue;
                }

                try
                {
                    string json = SerializeToJson(config);
                    string filePath = Path.Combine(_jsonDirectory, config.sceneName + ".json");
                    File.WriteAllText(filePath, json, Encoding.UTF8);
                    count++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[MapSpawnPointRegistry] [WARNING] 导出失败: "
                        + config.sceneName + " - " + e.Message);
                }
            }

            ModBehaviour.DevLog("[MapSpawnPointRegistry] 已导出 " + count + " 张地图配置到 " + _jsonDirectory);
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
                    ModBehaviour.DevLog("[MapSpawnPointRegistry] [WARNING] JSON 解析结果无效: " + filePath);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[MapSpawnPointRegistry] [WARNING] JSON 解析失败: "
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

        #region JSON 序列化

        /// <summary>
        /// 将 BossRushMapConfig 序列化为 JSON 字符串
        /// 使用 float.ToString("R") 保证往返精度
        /// </summary>
        internal static string SerializeToJson(BossRushMapConfig config)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\n");

            // sceneName
            sb.Append("  \"sceneName\": ");
            AppendJsonString(sb, config.sceneName);
            sb.Append(",\n");

            // sceneID
            sb.Append("  \"sceneID\": ");
            AppendJsonString(sb, config.sceneID);
            sb.Append(",\n");

            // displayNameCN
            sb.Append("  \"displayNameCN\": ");
            AppendJsonString(sb, config.displayNameCN);
            sb.Append(",\n");

            // displayNameEN
            sb.Append("  \"displayNameEN\": ");
            AppendJsonString(sb, config.displayNameEN);
            sb.Append(",\n");

            // sortOrder
            sb.Append("  \"sortOrder\": ");
            sb.Append(config.sortOrder);
            sb.Append(",\n");

            // spawnPoints
            sb.Append("  \"spawnPoints\": ");
            AppendVector3Array(sb, config.spawnPoints);
            sb.Append(",\n");

            // customSpawnPos
            sb.Append("  \"customSpawnPos\": ");
            AppendNullableVector3(sb, config.customSpawnPos);
            sb.Append(",\n");

            // defaultSignPos
            sb.Append("  \"defaultSignPos\": ");
            AppendNullableVector3(sb, config.defaultSignPos);
            sb.Append(",\n");

            // beaconIndex
            sb.Append("  \"beaconIndex\": ");
            sb.Append(config.beaconIndex);
            sb.Append(",\n");

            // previewImageName
            sb.Append("  \"previewImageName\": ");
            if (config.previewImageName == null)
            {
                sb.Append("null");
            }
            else
            {
                AppendJsonString(sb, config.previewImageName);
            }
            sb.Append(",\n");

            // mapNorth
            sb.Append("  \"mapNorth\": ");
            AppendVector3(sb, config.mapNorth);
            sb.Append(",\n");

            // modeESpawnPoints
            sb.Append("  \"modeESpawnPoints\": ");
            AppendVector3Array(sb, config.modeESpawnPoints);
            sb.Append(",\n");

            // modeEPlayerSpawnPos
            sb.Append("  \"modeEPlayerSpawnPos\": ");
            AppendNullableVector3(sb, config.modeEPlayerSpawnPos);
            sb.Append("\n");

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// 从 JSON 字符串反序列化为 BossRushMapConfig
        /// </summary>
        internal static BossRushMapConfig DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            // 提取基本字符串字段
            string sceneName = ExtractStringValue(json, "sceneName");
            string sceneID = ExtractStringValue(json, "sceneID");
            string displayNameCN = ExtractStringValue(json, "displayNameCN");
            string displayNameEN = ExtractStringValue(json, "displayNameEN");
            string previewImageName = ExtractNullableStringValue(json, "previewImageName");
            int sortOrder = ExtractIntValueOrDefault(json, "sortOrder", int.MaxValue);

            // 提取整数字段
            int beaconIndex = ExtractIntValue(json, "beaconIndex");

            // 提取 Vector3 数组字段
            Vector3[] spawnPoints = ExtractVector3Array(json, "spawnPoints");
            Vector3[] modeESpawnPoints = ExtractVector3Array(json, "modeESpawnPoints");

            // 提取可空 Vector3 字段
            Vector3? customSpawnPos = ExtractNullableVector3(json, "customSpawnPos");
            Vector3? defaultSignPos = ExtractNullableVector3(json, "defaultSignPos");
            Vector3? modeEPlayerSpawnPos = ExtractNullableVector3(json, "modeEPlayerSpawnPos");

            // 提取 mapNorth（必填，默认使用 DEMO 竞技场北方向量）
            Vector3? mapNorthNullable = ExtractNullableVector3(json, "mapNorth");
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
                sortOrder
            );
        }

        #endregion

        #region JSON 序列化辅助方法

        /// <summary>
        /// 追加 JSON 转义字符串
        /// </summary>
        private static void AppendJsonString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
        }

        /// <summary>
        /// 追加 Vector3 为 [x, y, z] 数组格式
        /// 使用 "R" 格式化保证浮点数往返精度
        /// </summary>
        private static void AppendVector3(StringBuilder sb, Vector3 v)
        {
            sb.Append('[');
            sb.Append(v.x.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(", ");
            sb.Append(v.y.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(", ");
            sb.Append(v.z.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(']');
        }

        /// <summary>
        /// 追加可空 Vector3（null 时输出 "null"）
        /// </summary>
        private static void AppendNullableVector3(StringBuilder sb, Vector3? v)
        {
            if (!v.HasValue)
            {
                sb.Append("null");
                return;
            }
            AppendVector3(sb, v.Value);
        }

        /// <summary>
        /// 追加 Vector3 数组为 [[x,y,z], ...] 格式
        /// </summary>
        private static void AppendVector3Array(StringBuilder sb, Vector3[] arr)
        {
            if (arr == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('[');
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                AppendVector3(sb, arr[i]);
            }
            sb.Append(']');
        }

        #endregion

        #region JSON 反序列化辅助方法

        /// <summary>
        /// 从 JSON 中提取字符串值
        /// </summary>
        private static string ExtractStringValue(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyPos < 0) return "";

            // 跳过 key 和冒号
            int colonPos = json.IndexOf(':', keyPos + pattern.Length);
            if (colonPos < 0) return "";

            int pos = colonPos + 1;
            // 跳过空白
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;

            if (pos >= json.Length) return "";

            // 检查 null
            if (pos + 4 <= json.Length && json.Substring(pos, 4) == "null")
            {
                return "";
            }

            // 期望引号开始
            if (json[pos] != '"') return "";
            pos++;

            // 读取到结束引号（处理转义）
            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '\\' && pos + 1 < json.Length)
                {
                    char next = json[pos + 1];
                    switch (next)
                    {
                        case '\\': sb.Append('\\'); pos += 2; break;
                        case '"': sb.Append('"'); pos += 2; break;
                        case 'n': sb.Append('\n'); pos += 2; break;
                        case 'r': sb.Append('\r'); pos += 2; break;
                        case 't': sb.Append('\t'); pos += 2; break;
                        default: sb.Append(c); pos++; break;
                    }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从 JSON 中提取可空字符串值（区分 null 和空字符串）
        /// </summary>
        private static string ExtractNullableStringValue(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyPos < 0) return null;

            int colonPos = json.IndexOf(':', keyPos + pattern.Length);
            if (colonPos < 0) return null;

            int pos = colonPos + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;

            if (pos >= json.Length) return null;

            // 检查 null
            if (pos + 4 <= json.Length && json.Substring(pos, 4) == "null")
            {
                return null;
            }

            // 期望引号开始
            if (json[pos] != '"') return null;
            pos++;

            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '\\' && pos + 1 < json.Length)
                {
                    char next = json[pos + 1];
                    switch (next)
                    {
                        case '\\': sb.Append('\\'); pos += 2; break;
                        case '"': sb.Append('"'); pos += 2; break;
                        case 'n': sb.Append('\n'); pos += 2; break;
                        case 'r': sb.Append('\r'); pos += 2; break;
                        case 't': sb.Append('\t'); pos += 2; break;
                        default: sb.Append(c); pos++; break;
                    }
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从 JSON 中提取整数值
        /// </summary>
        private static int ExtractIntValue(string json, string key)
        {
            return ExtractIntValueOrDefault(json, key, 0);
        }

        /// <summary>
        /// 从 JSON 中提取整数值，字段缺失或解析失败时返回默认值
        /// </summary>
        private static int ExtractIntValueOrDefault(string json, string key, int defaultValue)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyPos < 0) return defaultValue;

            int colonPos = json.IndexOf(':', keyPos + pattern.Length);
            if (colonPos < 0) return defaultValue;

            int pos = colonPos + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;

            // 读取数字
            int start = pos;
            if (pos < json.Length && json[pos] == '-') pos++;
            while (pos < json.Length && char.IsDigit(json[pos])) pos++;

            if (pos == start) return defaultValue;

            int result;
            if (int.TryParse(json.Substring(start, pos - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return result;
            }
            return defaultValue;
        }

        /// <summary>
        /// 从 JSON 中提取可空 Vector3 值
        /// 格式: [x, y, z] 或 null
        /// </summary>
        private static Vector3? ExtractNullableVector3(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyPos < 0) return null;

            int colonPos = json.IndexOf(':', keyPos + pattern.Length);
            if (colonPos < 0) return null;

            int pos = colonPos + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;

            if (pos >= json.Length) return null;

            // 检查 null
            if (pos + 4 <= json.Length && json.Substring(pos, 4) == "null")
            {
                return null;
            }

            // 期望 '['
            if (json[pos] != '[') return null;
            pos++;

            // 解析三个浮点数
            float x = ParseNextFloat(json, ref pos);
            SkipToNextValue(json, ref pos);
            float y = ParseNextFloat(json, ref pos);
            SkipToNextValue(json, ref pos);
            float z = ParseNextFloat(json, ref pos);

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 从 JSON 中提取 Vector3 数组
        /// 格式: [[x,y,z], [x,y,z], ...] 或 null
        /// </summary>
        private static Vector3[] ExtractVector3Array(string json, string key)
        {
            string pattern = "\"" + key + "\"";
            int keyPos = json.IndexOf(pattern, StringComparison.Ordinal);
            if (keyPos < 0) return null;

            int colonPos = json.IndexOf(':', keyPos + pattern.Length);
            if (colonPos < 0) return null;

            int pos = colonPos + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;

            if (pos >= json.Length) return null;

            // 检查 null
            if (pos + 4 <= json.Length && json.Substring(pos, 4) == "null")
            {
                return null;
            }

            // 期望外层 '['
            if (json[pos] != '[') return null;
            pos++;

            var result = new List<Vector3>();

            while (pos < json.Length)
            {
                // 跳过空白和逗号
                while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;

                if (pos >= json.Length) break;

                // 检查外层数组结束
                if (json[pos] == ']') break;

                // 期望内层 '['
                if (json[pos] != '[')
                {
                    // 跳过非法字符
                    pos++;
                    continue;
                }
                pos++;

                // 解析三个浮点数
                float x = ParseNextFloat(json, ref pos);
                SkipToNextValue(json, ref pos);
                float y = ParseNextFloat(json, ref pos);
                SkipToNextValue(json, ref pos);
                float z = ParseNextFloat(json, ref pos);

                result.Add(new Vector3(x, y, z));

                // 跳到内层 ']'
                while (pos < json.Length && json[pos] != ']') pos++;
                if (pos < json.Length) pos++; // 跳过 ']'
            }

            return result.Count > 0 ? result.ToArray() : null;
        }

        /// <summary>
        /// 从当前位置解析下一个浮点数
        /// </summary>
        private static float ParseNextFloat(string json, ref int pos)
        {
            // 跳过空白
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;

            int start = pos;

            // 负号
            if (pos < json.Length && json[pos] == '-') pos++;

            // 整数部分
            while (pos < json.Length && char.IsDigit(json[pos])) pos++;

            // 小数部分
            if (pos < json.Length && json[pos] == '.')
            {
                pos++;
                while (pos < json.Length && char.IsDigit(json[pos])) pos++;
            }

            // 科学计数法
            if (pos < json.Length && (json[pos] == 'e' || json[pos] == 'E'))
            {
                pos++;
                if (pos < json.Length && (json[pos] == '+' || json[pos] == '-')) pos++;
                while (pos < json.Length && char.IsDigit(json[pos])) pos++;
            }

            if (pos == start) return 0f;

            float result;
            if (float.TryParse(
                json.Substring(start, pos - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result))
            {
                return result;
            }
            return 0f;
        }

        /// <summary>
        /// 跳过逗号和空白到下一个值
        /// </summary>
        private static void SkipToNextValue(string json, ref int pos)
        {
            while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;
        }

        #endregion
    }
}
