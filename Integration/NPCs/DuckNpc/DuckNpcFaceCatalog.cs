// ============================================================================
// DuckNpcFaceCatalog.cs - 官方捏脸资产清点
// ============================================================================
// 模块说明：
//   清点官方捏脸系统（CustomFaceData / CustomFacePartCollection）的可用家底，
//   为「用原版捏脸造新 NPC」提供两样东西：
//     1. 每类部件的合法 ID 列表 —— 手写 NPC 脸 JSON 时不能瞎猜 ID。
//     2. 可用底模池 —— 哪些 CharacterModel 预制体真的挂了 CustomFaceInstance。
//
//   全程只用官方 public API，不反射：
//     - CustomFacePartCollection.totalCount / GetPartPrefab / GetNextOrPrevPrefab
//     - CustomFacePart.id（public 字段）
//     - CharacterRandomPreset.CharacterModel / FacePreset（public getter）
//   官方把 parts 列表设为 private，但 GetPartPrefab(不存在的 ID) 会回落到 parts[0]，
//   再用 GetNextOrPrevPrefab 逐步前进即可走完整表，不需要动 private 字段。
//
//   本文件只做**读取和清点**，不生成角色。生成走 DuckNpcFactory。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 官方捏脸资产清点器。只读，不改任何官方资产。
    /// </summary>
    internal static class DuckNpcFaceCatalog
    {
        /// <summary>
        /// 用来让 GetPartPrefab 走 "FindIndex 失败 → 回落 parts[0]" 分支的哨兵 ID。
        /// 官方部件 ID 是小整数，取 int.MinValue 不可能撞上。
        /// </summary>
        private const int SentinelMissingPartId = int.MinValue;

        private static readonly int[] EmptyIds = new int[0];

        /// <summary>底模名 → 底模预制体。惰性构建，卸载时清。</summary>
        private static Dictionary<string, CharacterModel> _modelsByName;

        /// <summary>捏脸部件的七个类别，与 CustomFacePartTypes 一一对应。</summary>
        internal static readonly CustomFacePartTypes[] AllPartTypes = new CustomFacePartTypes[]
        {
            CustomFacePartTypes.hair,
            CustomFacePartTypes.eye,
            CustomFacePartTypes.eyebrow,
            CustomFacePartTypes.mouth,
            CustomFacePartTypes.tail,
            CustomFacePartTypes.foot,
            CustomFacePartTypes.wing
        };

        // ====================================================================
        // 部件清点
        // ====================================================================

        /// <summary>
        /// 取官方捏脸数据表。取不到返回 null（早于 GameplayDataSettings 初始化时会发生）。
        /// </summary>
        internal static CustomFaceData ResolveFaceData()
        {
            try
            {
                return GameplayDataSettings.CustomFaceData;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取 CustomFaceData 失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 按部件类别取对应的官方部件集合。
        /// </summary>
        /// <remarks>
        /// 注意 CustomFaceData 里还有一个 Decorations 集合，但官方 CustomFacePartTypes
        /// 枚举里没有 decoration、CustomFaceSettingData 里也没有对应字段，
        /// CustomFaceInstance 从不消费它 —— 那是条死路，别指望用它做配饰。
        /// </remarks>
        internal static CustomFacePartCollection ResolveCollection(CustomFacePartTypes type)
        {
            CustomFaceData data = ResolveFaceData();
            if (data == null)
            {
                return null;
            }

            try
            {
                switch (type)
                {
                    case CustomFacePartTypes.hair: return data.Hairs;
                    case CustomFacePartTypes.eye: return data.Eyes;
                    case CustomFacePartTypes.eyebrow: return data.Eyebrows;
                    case CustomFacePartTypes.mouth: return data.Mouths;
                    case CustomFacePartTypes.tail: return data.Tails;
                    case CustomFacePartTypes.foot: return data.Foots;
                    case CustomFacePartTypes.wing: return data.Wings;
                    default: return null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取部件集合失败 type=" + type + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 枚举某类部件的全部合法 ID。失败或集合为空时返回空数组。
        /// </summary>
        internal static int[] EnumeratePartIds(CustomFacePartTypes type)
        {
            CustomFacePartCollection collection = ResolveCollection(type);
            if (collection == null)
            {
                return EmptyIds;
            }

            int total;
            try
            {
                total = collection.totalCount;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 读 totalCount 失败 type=" + type + ": " + e.Message);
                return EmptyIds;
            }

            if (total <= 0)
            {
                return EmptyIds;
            }

            int firstId;
            try
            {
                CustomFacePart first = collection.GetPartPrefab(SentinelMissingPartId);
                if (first == null)
                {
                    return EmptyIds;
                }
                firstId = first.id;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取首个部件失败 type=" + type + ": " + e.Message);
                return EmptyIds;
            }

            List<int> ids = new List<int>(total);
            ids.Add(firstId);

            int cursor = firstId;
            // 上限用 total 卡死，防止 ID 重复导致 GetNextOrPrevPrefab 原地打转。
            for (int step = 1; step < total; step++)
            {
                CustomFacePart next;
                try
                {
                    next = collection.GetNextOrPrevPrefab(cursor, 1);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 步进部件失败 type=" + type
                        + ", cursor=" + cursor + ": " + e.Message);
                    break;
                }

                if (next == null)
                {
                    break;
                }

                // 提前回到起点说明表比 totalCount 短或 ID 有重复，停下来而不是重复登记。
                if (next.id == firstId)
                {
                    break;
                }

                ids.Add(next.id);
                cursor = next.id;
            }

            return ids.ToArray();
        }

        // ====================================================================
        // 捏脸基线
        // ====================================================================

        /// <summary>
        /// 取官方默认捏脸基线。
        /// </summary>
        /// <remarks>
        /// 这是造新 NPC 脸的**唯一正确起点**。不要从 default(CustomFaceSettingData) 起手：
        /// CustomFacePartInfo 里的 radius / heightOffset 不在捏脸 UI 里，
        /// 只存在于 preset 资产中；全零会把五官糊在头部中心，得到一团畸形。
        /// </remarks>
        internal static bool TryGetDefaultFace(out CustomFaceSettingData face)
        {
            face = default(CustomFaceSettingData);

            CustomFaceData data = ResolveFaceData();
            if (data == null)
            {
                return false;
            }

            try
            {
                CustomFacePreset preset = data.DefaultPreset;
                if (preset == null)
                {
                    return false;
                }
                face = preset.settings;
                face.savedSetting = true;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取默认捏脸基线失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 从官方角色 preset 里取一份捏脸模板（比默认基线更"像个 NPC"的起点）。
        /// nameKeyFilter 为空时取第一个带 FacePreset 的 preset。
        /// </summary>
        internal static bool TryGetPresetFace(string nameKeyFilter, out CustomFaceSettingData face, out string matchedNameKey)
        {
            face = default(CustomFaceSettingData);
            matchedNameKey = null;

            CharacterRandomPreset[] presets;
            try
            {
                presets = ObjectCache.GetCharacterPresets();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 枚举角色 preset 失败: " + e.Message);
                return false;
            }

            if (presets == null || presets.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < presets.Length; i++)
            {
                CharacterRandomPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                try
                {
                    CustomFacePreset facePreset = preset.FacePreset;
                    if (facePreset == null)
                    {
                        continue;
                    }

                    string nameKey = preset.nameKey;
                    if (!string.IsNullOrEmpty(nameKeyFilter))
                    {
                        if (string.IsNullOrEmpty(nameKey) ||
                            nameKey.IndexOf(nameKeyFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    face = facePreset.settings;
                    face.savedSetting = true;
                    matchedNameKey = nameKey;
                    return true;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 取 preset 捏脸模板时跳过一个异常项: " + e.Message);
                }
            }

            return false;
        }

        // ====================================================================
        // 底模池清点
        // ====================================================================

        /// <summary>一条底模候选记录。</summary>
        internal sealed class ModelCandidate
        {
            public string PresetNameKey;
            public string ModelPrefabName;
            public bool HasCustomFace;
            public bool HasFacePreset;
            public bool IsBoss;
            public Teams Team;
        }

        /// <summary>
        /// 清点全部角色 preset 引用到的 CharacterModel 预制体，并标注哪些真的挂了
        /// CustomFaceInstance —— 只有挂了的才能当捏脸 NPC 的底模。
        /// </summary>
        internal static List<ModelCandidate> EnumerateModelCandidates()
        {
            List<ModelCandidate> result = new List<ModelCandidate>();

            CharacterRandomPreset[] presets;
            try
            {
                presets = ObjectCache.GetCharacterPresets();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 枚举角色 preset 失败: " + e.Message);
                return result;
            }

            if (presets == null)
            {
                return result;
            }

            HashSet<string> seenModels = new HashSet<string>();
            for (int i = 0; i < presets.Length; i++)
            {
                CharacterRandomPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                try
                {
                    CharacterModel model = preset.CharacterModel;
                    if (model == null)
                    {
                        continue;
                    }

                    string modelName = model.name;
                    // 同一个底模会被多个 preset 复用，按底模名去重，报告才读得动。
                    if (!seenModels.Add(modelName))
                    {
                        continue;
                    }

                    ModelCandidate candidate = new ModelCandidate();
                    candidate.PresetNameKey = preset.nameKey;
                    candidate.ModelPrefabName = modelName;
                    candidate.HasCustomFace = model.CustomFace != null;
                    candidate.HasFacePreset = preset.FacePreset != null;
                    candidate.IsBoss = preset.isBoss;
                    candidate.Team = preset.team;
                    result.Add(candidate);
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 清点底模时跳过一个异常 preset: " + e.Message);
                }
            }

            return result;
        }

        /// <summary>
        /// 按预制体名解析底模。名字来自 F3 家底报告第 2 节的底模池。
        /// 找不到、或找到的底模没挂 CustomFaceInstance 时返回 null 并记一行。
        /// </summary>
        /// <remarks>
        /// 底模引用只能从 CharacterRandomPreset.CharacterModel 拿 ——
        /// 官方没有"按名字取 CharacterModel"的资产表，预制体也不在 Resources 里。
        /// 结果按名字缓存：这是一次全 preset 扫描，蓝图层每次生成都调它。
        /// </remarks>
        internal static CharacterModel ResolveModelByName(string modelName)
        {
            if (string.IsNullOrEmpty(modelName))
            {
                return null;
            }

            if (_modelsByName == null)
            {
                _modelsByName = new Dictionary<string, CharacterModel>(StringComparer.Ordinal);
                BuildModelNameIndex();
            }

            CharacterModel model;
            if (!_modelsByName.TryGetValue(modelName, out model) || model == null)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 找不到底模: " + modelName + "，将回落默认底模");
                return null;
            }

            try
            {
                if (model.CustomFace == null)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 底模 " + modelName
                        + " 没有 CustomFaceInstance，捏脸会被静默忽略，将回落默认底模");
                    return null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 读底模 CustomFace 失败 " + modelName + ": " + e.Message);
                return null;
            }

            return model;
        }

        private static void BuildModelNameIndex()
        {
            CharacterRandomPreset[] presets;
            try
            {
                presets = ObjectCache.GetCharacterPresets();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 建底模索引失败: " + e.Message);
                return;
            }

            if (presets == null)
            {
                return;
            }

            for (int i = 0; i < presets.Length; i++)
            {
                CharacterRandomPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                try
                {
                    CharacterModel model = preset.CharacterModel;
                    if (model == null)
                    {
                        continue;
                    }
                    if (!_modelsByName.ContainsKey(model.name))
                    {
                        _modelsByName.Add(model.name, model);
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 建底模索引时跳过一个异常 preset: " + e.Message);
                }
            }

            // 默认底模不一定被任何 preset 引用，单独补进索引。
            try
            {
                CharacterModel fallback = ResolveDefaultCharacterModel();
                if (fallback != null && !_modelsByName.ContainsKey(fallback.name))
                {
                    _modelsByName.Add(fallback.name, fallback);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 补默认底模进索引失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取官方默认角色底模（玩家鸭模）。这是 public API，
        /// 不需要像 DeathWraith 那样反射 LevelManager.characterModel。
        /// </summary>
        internal static CharacterModel ResolveDefaultCharacterModel()
        {
            try
            {
                return GameplayDataSettings.Prefabs.DefaultCharacterModel;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 取 DefaultCharacterModel 失败: " + e.Message);
                return null;
            }
        }

        // ====================================================================
        // 报告渲染
        // ====================================================================

        /// <summary>
        /// 渲染一份家底清点报告（Markdown）。供 F3 落盘，用于把实机数据交回设计侧。
        /// </summary>
        internal static string BuildInventoryReport()
        {
            StringBuilder sb = new StringBuilder(8192);

            sb.AppendLine("# 捏脸 NPC 家底清点报告");
            sb.AppendLine();
            sb.AppendLine("- 生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try
            {
                sb.AppendLine("- 场景: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            catch (Exception e)
            {
                sb.AppendLine("- 场景: (读取失败: " + e.GetType().Name + ")");
            }
            sb.AppendLine();

            AppendPartInventory(sb);
            AppendModelCandidates(sb);
            AppendBaselineFaces(sb);

            return sb.ToString();
        }

        private static void AppendPartInventory(StringBuilder sb)
        {
            sb.AppendLine("## 1. 部件清单（可用 ID）");
            sb.AppendLine();

            CustomFaceData data = ResolveFaceData();
            if (data == null)
            {
                sb.AppendLine("> 取不到 GameplayDataSettings.CustomFaceData，清点失败。");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("| 部件 | totalCount | 实际枚举到 | ID 列表 |");
            sb.AppendLine("| --- | --- | --- | --- |");

            for (int i = 0; i < AllPartTypes.Length; i++)
            {
                CustomFacePartTypes type = AllPartTypes[i];
                CustomFacePartCollection collection = ResolveCollection(type);

                int total = -1;
                try
                {
                    if (collection != null)
                    {
                        total = collection.totalCount;
                    }
                }
                catch (Exception e)
                {
                    // EnumeratePartIds 内部也会记一次；这里保留是为了让 total 停在 -1 并说明原因。
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 报告读 totalCount 失败 type=" + type + ": " + e.Message);
                }

                int[] ids = EnumeratePartIds(type);
                sb.AppendLine("| " + type
                    + " | " + total.ToString(CultureInfo.InvariantCulture)
                    + " | " + ids.Length.ToString(CultureInfo.InvariantCulture)
                    + " | " + JoinIds(ids) + " |");
            }

            sb.AppendLine();
            sb.AppendLine("> `totalCount` 与「实际枚举到」不一致说明部件 ID 有重复，");
            sb.AppendLine("> 此时 `GetPartPrefab(id)` 会命中第一个同 ID 项，需要人工核对。");
            sb.AppendLine();
        }

        private static void AppendModelCandidates(StringBuilder sb)
        {
            sb.AppendLine("## 2. 底模池（哪些 CharacterModel 能当捏脸 NPC 底模）");
            sb.AppendLine();

            CharacterModel defaultModel = ResolveDefaultCharacterModel();
            if (defaultModel == null)
            {
                sb.AppendLine("- `Prefabs.DefaultCharacterModel`: **取不到**");
            }
            else
            {
                bool hasFace = false;
                try
                {
                    hasFace = defaultModel.CustomFace != null;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 读默认底模 CustomFace 失败: " + e.Message);
                }
                sb.AppendLine("- `Prefabs.DefaultCharacterModel`: " + defaultModel.name
                    + "，CustomFace=" + (hasFace ? "有" : "**无**"));
            }
            sb.AppendLine();

            List<ModelCandidate> candidates = EnumerateModelCandidates();
            if (candidates.Count == 0)
            {
                sb.AppendLine("> 未枚举到任何 preset 底模（可能不在关卡场景内运行）。");
                sb.AppendLine();
                return;
            }

            int withFace = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].HasCustomFace)
                {
                    withFace++;
                }
            }

            sb.AppendLine("共 " + candidates.Count.ToString(CultureInfo.InvariantCulture)
                + " 个去重底模，其中 " + withFace.ToString(CultureInfo.InvariantCulture) + " 个挂了 CustomFaceInstance。");
            sb.AppendLine();
            sb.AppendLine("| 底模预制体 | CustomFace | 样例 preset nameKey | 自带 FacePreset | isBoss | team |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- |");

            for (int i = 0; i < candidates.Count; i++)
            {
                ModelCandidate c = candidates[i];
                sb.AppendLine("| " + Safe(c.ModelPrefabName)
                    + " | " + (c.HasCustomFace ? "有" : "-")
                    + " | " + Safe(c.PresetNameKey)
                    + " | " + (c.HasFacePreset ? "有" : "-")
                    + " | " + (c.IsBoss ? "是" : "-")
                    + " | " + c.Team + " |");
            }

            sb.AppendLine();
        }

        private static void AppendBaselineFaces(StringBuilder sb)
        {
            sb.AppendLine("## 3. 捏脸基线 JSON（造新 NPC 脸的起点）");
            sb.AppendLine();

            CustomFaceSettingData defaultFace;
            if (TryGetDefaultFace(out defaultFace))
            {
                sb.AppendLine("### 官方默认基线 `CustomFaceData.DefaultPreset`");
                sb.AppendLine();
                sb.AppendLine("```json");
                sb.AppendLine(DuckNpcFaceCodec.ToJson(defaultFace));
                sb.AppendLine("```");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("> 取不到官方默认基线。");
                sb.AppendLine();
            }

            AppendPresetFaceSamples(sb);
        }

        private static void AppendPresetFaceSamples(StringBuilder sb)
        {
            sb.AppendLine("### 官方 preset 捏脸模板样例");
            sb.AppendLine();

            CharacterRandomPreset[] presets;
            try
            {
                presets = ObjectCache.GetCharacterPresets();
            }
            catch
            {
                presets = null;
            }

            if (presets == null || presets.Length == 0)
            {
                sb.AppendLine("> 未枚举到角色 preset。");
                sb.AppendLine();
                return;
            }

            // 采样上限：报告是给人读的，全量几十条 JSON 没人看得完。
            const int MaxSamples = 12;
            int emitted = 0;

            for (int i = 0; i < presets.Length && emitted < MaxSamples; i++)
            {
                CharacterRandomPreset preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                try
                {
                    CustomFacePreset facePreset = preset.FacePreset;
                    if (facePreset == null)
                    {
                        continue;
                    }

                    CustomFaceSettingData face = facePreset.settings;
                    sb.AppendLine("#### " + Safe(preset.nameKey) + "  (asset: " + Safe(facePreset.name) + ")");
                    sb.AppendLine();
                    sb.AppendLine("```json");
                    sb.AppendLine(DuckNpcFaceCodec.ToJson(face));
                    sb.AppendLine("```");
                    sb.AppendLine();
                    emitted++;
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 渲染 preset 捏脸样例失败: " + e.Message);
                }
            }

            if (emitted == 0)
            {
                sb.AppendLine("> 所有 preset 都没有 FacePreset。");
                sb.AppendLine();
            }
        }

        private static string JoinIds(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return "(空)";
            }

            StringBuilder sb = new StringBuilder(ids.Length * 4);
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(ids[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "(空)" : value;
        }

        /// <summary>
        /// 清空底模索引。由 AlwaysOnRuntimeHooks 在 Mod 卸载时调用 ——
        /// 索引握着官方 CharacterModel 预制体引用，不清会把旧程序集的引用留到下次加载。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            if (_modelsByName != null)
            {
                _modelsByName.Clear();
                _modelsByName = null;
            }
        }
    }
}
