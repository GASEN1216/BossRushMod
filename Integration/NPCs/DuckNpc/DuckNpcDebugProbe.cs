// ============================================================================
// DuckNpcDebugProbe.cs - 捏脸 NPC 的 F3 数据采集与探针
// ============================================================================
// 模块说明：
//   「用原版捏脸造新 NPC」这条路上有两类问题只能实机回答：
//     A. 家底有多少 —— 每类部件几款、哪些底模挂了 CustomFaceInstance。
//     B. 无 preset 裸造的角色到底是什么行为 —— 站不站得住、有没有待机动画、
//        会不会被清场、能不能交互、过图会不会残留。
//
//   本文件提供四个 F3 入口来采集这两类数据：
//     1. 输出家底报告   → 落盘 Markdown，回答 A
//     2. 导出玩家捏脸   → 落盘 + 剪贴板 JSON，这是新 NPC 脸的**作者工作流**
//     3. 生成/回收探针  → 回答 B
//     4. 输出探针状态   → 把探针的运行时观测项一次性打出来
//
//   落盘路径与 F3 验收报告同目录（Application.persistentDataPath/BossRushTestReports），
//   日志用 LogInfo 而不是 DevLog —— DevLog 带 [Conditional("BOSSRUSH_DEV")]，
//   在正式构建里整句被编译掉，玩家跑一遍会什么都看不到。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    /// <summary>探针脸的来源。</summary>
    internal enum DuckNpcProbeFaceSource
    {
        /// <summary>玩家当前的脸（验证「抓脸→贴脸」链路）。</summary>
        PlayerFace,

        /// <summary>官方默认基线（验证手写蓝图的几何是否正确）。</summary>
        OfficialBaseline,

        /// <summary>随机夸张脸（用于肉眼确认"生成的脸确实不一样"）。</summary>
        RandomExaggerated
    }

    /// <summary>
    /// 捏脸 NPC 调试探针。仅供 F3 调用，不参与正常玩法路径。
    /// </summary>
    internal static class DuckNpcDebugProbe
    {
        private const string LogPrefix = "[DuckNpcProbe]";
        private const string ProbeNpcId = "duck_npc_probe";

        /// <summary>探针相对玩家的落点偏移。</summary>
        private static readonly Vector3 ProbeSpawnOffset = new Vector3(0f, 0f, 2f);

        private static CharacterMainControl _probe;
        private static bool _spawnInFlight;
        private static bool _equipInFlight;
        private static string _lastStatus = "尚未采集捏脸 NPC 数据";
        private static string _lastReportPath;
        private static DuckNpcOutfitResult _lastOutfit;
        private static string _lastFaceSource;

        /// <summary>供 F3 按钮回调读取的最近一次状态。</summary>
        internal static string LastStatus
        {
            get { return _lastStatus; }
        }

        // ====================================================================
        // 1. 家底报告
        // ====================================================================

        /// <summary>
        /// 清点官方捏脸家底并落盘。
        /// </summary>
        internal static void DumpInventoryReport()
        {
            try
            {
                string report = DuckNpcFaceCatalog.BuildInventoryReport();
                string path = WriteReportFile("DuckNpcInventory", "md", report);
                if (path == null)
                {
                    _lastStatus = "家底报告落盘失败，详见日志";
                    return;
                }

                _lastReportPath = path;
                _lastStatus = "已输出家底报告: " + path;
                ModBehaviour.LogInfo(LogPrefix + " 家底报告已落盘: " + path);
            }
            catch (Exception e)
            {
                _lastStatus = "家底报告生成异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 家底报告生成异常: " + e);
            }
        }

        // ====================================================================
        // 2. 导出玩家捏脸（作者工作流）
        // ====================================================================

        /// <summary>
        /// 把玩家当前的捏脸导出成 JSON：落盘 + 进剪贴板。
        /// </summary>
        /// <remarks>
        /// 这是新 NPC 长相的推荐生产方式：在游戏内理发镜把脸捏好 → 点这个按钮 →
        /// 粘贴到 NPC 蓝图 JSON 里。比手写一堆浮点数字靠谱得多，
        /// 而且导出的几何字段（radius / heightOffset）天然是对的。
        /// </remarks>
        internal static void ExportPlayerFace()
        {
            try
            {
                CustomFaceSettingData face;
                string source;
                if (!TryCapturePlayerFace(out face, out source))
                {
                    _lastStatus = "取不到玩家捏脸数据（需要在关卡内、主角已生成）";
                    return;
                }

                string json = DuckNpcFaceCodec.ToJson(face);
                if (string.IsNullOrEmpty(json))
                {
                    _lastStatus = "玩家捏脸序列化失败";
                    return;
                }

                StringBuilder sb = new StringBuilder(json.Length + 512);
                sb.AppendLine("// 玩家当前捏脸导出");
                sb.AppendLine("// 来源: " + source);
                sb.AppendLine("// 时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                sb.AppendLine("// 用法: 整段 JSON 贴进 NPC 蓝图的 face 字段；");
                sb.AppendLine("//       radius / heightOffset 这两项捏脸 UI 不暴露，靠本导出带出来，别手删。");
                sb.AppendLine(json);

                string path = WriteReportFile("DuckNpcFace", "json.txt", sb.ToString());

                bool clipboardOk = TryCopyToClipboard(json);

                _lastStatus = "已导出玩家捏脸"
                    + (clipboardOk ? "（已进剪贴板）" : "（剪贴板写入失败）")
                    + (path != null ? "，文件: " + path : "，落盘失败");

                ModBehaviour.LogInfo(LogPrefix + " 玩家捏脸导出 source=" + source
                    + ", clipboard=" + clipboardOk
                    + ", file=" + (path ?? "(失败)"));
                ModBehaviour.LogInfo(LogPrefix + " FACE_JSON " + json);
            }
            catch (Exception e)
            {
                _lastStatus = "导出玩家捏脸异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 导出玩家捏脸异常: " + e);
            }
        }

        /// <summary>
        /// 抓玩家当前捏脸。优先抓可见模型上的实例，失败回落主角捏脸存档。
        /// 与 DeathWraith 的抓取策略一致。
        /// </summary>
        private static bool TryCapturePlayerFace(out CustomFaceSettingData face, out string source)
        {
            face = default(CustomFaceSettingData);
            source = null;

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null && main.characterModel != null && main.characterModel.CustomFace != null)
                {
                    face = main.characterModel.CustomFace.ConvertToSaveData();
                    face.savedSetting = true;
                    source = "主角当前模型 CustomFaceInstance";
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 从主角模型抓脸失败: " + e.Message);
            }

            try
            {
                LevelManager level = LevelManager.Instance;
                if (level != null && level.CustomFaceManager != null)
                {
                    face = level.CustomFaceManager.LoadMainCharacterSetting();
                    face.savedSetting = true;
                    source = "主角捏脸存档 CustomFace_MainCharacter";
                    return true;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 读主角捏脸存档失败: " + e.Message);
            }

            return false;
        }

        // ====================================================================
        // 3. 探针生成 / 回收
        // ====================================================================

        /// <summary>
        /// 在玩家前方生成一个探针 NPC。
        /// </summary>
        /// <param name="faceSource">脸的来源，见 DuckNpcProbeFaceSource。</param>
        /// <param name="withEquipment">是否随机穿一套官方装备（头盔/护甲/耳机/面罩/背包/主武器）。</param>
        internal static void SpawnProbe(DuckNpcProbeFaceSource faceSource, bool withEquipment)
        {
            if (_spawnInFlight)
            {
                _lastStatus = "探针正在生成中，请稍候";
                return;
            }

            if (_probe != null)
            {
                _lastStatus = "探针已存在，请先回收";
                return;
            }

            // 下面是 fire-and-forget，F3 回调紧接着就会读 LastStatus，
            // 这里必须先同步写一句，否则按钮看起来像没反应。
            _lastStatus = "正在生成探针（" + DescribeFaceSource(faceSource)
                + (withEquipment ? " + 随机装备" : "") + "）……";
            _spawnInFlight = true;
            SpawnProbeAsync(faceSource, withEquipment).Forget();
        }

        private static string DescribeFaceSource(DuckNpcProbeFaceSource source)
        {
            switch (source)
            {
                case DuckNpcProbeFaceSource.PlayerFace: return "玩家脸";
                case DuckNpcProbeFaceSource.RandomExaggerated: return "随机夸张脸";
                default: return "官方基线";
            }
        }

        private static async UniTaskVoid SpawnProbeAsync(DuckNpcProbeFaceSource faceSource, bool withEquipment)
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    _lastStatus = "找不到玩家，无法生成探针";
                    return;
                }

                Vector3 forward = main.transform.forward;
                Vector3 spawnPos = main.transform.position
                    + forward * ProbeSpawnOffset.z
                    + Vector3.up * ProbeSpawnOffset.y;

                DuckNpcSpawnRequest request = new DuckNpcSpawnRequest();
                request.NpcId = ProbeNpcId;
                request.Position = spawnPos;
                request.Facing = -forward;   // 面向玩家

                string faceSourceLabel;
                CustomFaceSettingData face;
                if (faceSource == DuckNpcProbeFaceSource.PlayerFace)
                {
                    if (TryCapturePlayerFace(out face, out faceSourceLabel))
                    {
                        request.Face = face;
                        request.HasFace = true;
                    }
                    else
                    {
                        faceSourceLabel = "抓取失败，沿用底模自带脸";
                    }
                }
                else if (faceSource == DuckNpcProbeFaceSource.RandomExaggerated)
                {
                    if (DuckNpcFaceRandomizer.TryCreate(DuckNpcFaceWildness.Exaggerated, out face))
                    {
                        request.Face = face;
                        request.HasFace = true;
                        faceSourceLabel = "随机夸张脸";
                    }
                    else
                    {
                        faceSourceLabel = "随机脸生成失败，沿用底模自带脸";
                    }
                }
                else
                {
                    if (DuckNpcFaceCatalog.TryGetDefaultFace(out face))
                    {
                        // 走一遍归一化，等于顺手验证 Normalize 不会把官方基线改坏。
                        DuckNpcFaceCodec.Normalize(ref face, face);
                        request.Face = face;
                        request.HasFace = true;
                        faceSourceLabel = "官方 DefaultPreset 基线";
                    }
                    else
                    {
                        faceSourceLabel = "基线取不到，沿用底模自带脸";
                    }
                }

                CharacterMainControl npc = await DuckNpcFactory.SpawnAsync(request);
                if (npc == null)
                {
                    _lastStatus = "探针生成失败，详见日志";
                    ModBehaviour.LogWarning(LogPrefix + " 探针生成失败");
                    return;
                }

                _probe = npc;
                try
                {
                    npc.gameObject.name = "DuckNpcProbe_BossRush";
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 探针改名失败: " + e.Message);
                }

                _lastFaceSource = faceSourceLabel;
                _lastOutfit = null;

                if (withEquipment)
                {
                    // 穿装备必须在角色已完全建立之后：TryPlug 走的是角色物品树，
                    // 官方 CharacterModel 监听槽位变化再把模型挂到对应 socket 上。
                    _lastOutfit = await DuckNpcOutfitter.EquipRandomAsync(npc, null);
                    ModBehaviour.LogInfo(LogPrefix + " 探针装备: 成功 " + _lastOutfit.EquippedCount
                        + " 件, 跳过 " + _lastOutfit.Skipped.Count + " 项");
                    for (int i = 0; i < _lastOutfit.Equipped.Count; i++)
                    {
                        ModBehaviour.LogInfo(LogPrefix + " 　装备 → " + _lastOutfit.Equipped[i]);
                    }
                    for (int i = 0; i < _lastOutfit.Skipped.Count; i++)
                    {
                        ModBehaviour.LogInfo(LogPrefix + " 　跳过 → " + _lastOutfit.Skipped[i]);
                    }
                }

                _lastStatus = "探针已生成（脸: " + faceSourceLabel
                    + (_lastOutfit != null ? "，装备 " + _lastOutfit.EquippedCount + " 件" : "")
                    + "），按「输出探针状态」看观测项";
                ModBehaviour.LogInfo(LogPrefix + " 探针已生成 faceSource=" + faceSourceLabel + ", pos=" + spawnPos);
                LogProbeFaceJson(npc);
            }
            catch (Exception e)
            {
                _lastStatus = "探针生成异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 探针生成异常: " + e);
            }
            finally
            {
                _spawnInFlight = false;
            }
        }

        /// <summary>
        /// 回收探针。幂等。
        /// </summary>
        internal static void DespawnProbe()
        {
            if (_probe == null)
            {
                _lastStatus = "当前没有探针";
                return;
            }

            DuckNpcFactory.Despawn(_probe);
            _probe = null;
            _lastOutfit = null;
            _lastFaceSource = null;
            _lastStatus = "探针已回收";
            ModBehaviour.LogInfo(LogPrefix + " 探针已回收");
        }

        /// <summary>
        /// 就地给当前探针换一张新的随机夸张脸，不重新生成角色。
        /// </summary>
        /// <remarks>
        /// 这是验证"生成的脸确实不一样"最快的方式：连点几次，
        /// 同一只鸭子每次都该变个样。同时也顺带证明 SetFaceFromData
        /// 可以在角色存活期间反复调用（官方 LoadFromData 每次会重新
        /// Instantiate 七个部件并销毁旧的，所以**只能按需调用，不能每帧调**）。
        /// </remarks>
        internal static void RerollProbeFace()
        {
            if (_probe == null)
            {
                _lastStatus = "当前没有探针，先生成一个";
                return;
            }

            try
            {
                if (_probe.characterModel == null || _probe.characterModel.CustomFace == null)
                {
                    _lastStatus = "探针底模没有 CustomFace，换不了脸";
                    return;
                }

                CustomFaceSettingData face;
                if (!DuckNpcFaceRandomizer.TryCreate(DuckNpcFaceWildness.Exaggerated, out face))
                {
                    _lastStatus = "随机脸生成失败（取不到官方基线）";
                    return;
                }

                _probe.characterModel.SetFaceFromData(face);
                _lastFaceSource = "随机夸张脸（原地重掷）";
                _lastStatus = "已换脸：hair=" + face.hairID
                    + " eye=" + face.eyeID
                    + " mouth=" + face.mouthID
                    + " 体色=" + ColorToHex(face.headSetting.mainColor);
                ModBehaviour.LogInfo(LogPrefix + " 探针换脸 " + _lastStatus);
                LogProbeFaceJson(_probe);
            }
            catch (Exception e)
            {
                _lastStatus = "换脸异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 换脸异常: " + e);
            }
        }

        /// <summary>
        /// 就地给当前探针重掷一套随机装备，不重新生成角色。
        /// </summary>
        /// <remarks>
        /// 必须先卸干净再穿：槽位已占时 TryPlug 会全部失败，
        /// 表现成「点了重掷但一点没变」。
        /// </remarks>
        internal static void RerollProbeEquipment()
        {
            if (_probe == null)
            {
                _lastStatus = "当前没有探针，先生成一个";
                return;
            }

            if (_equipInFlight)
            {
                _lastStatus = "装备正在重掷中，请稍候";
                return;
            }

            _equipInFlight = true;
            _lastStatus = "正在重掷装备……";
            RerollProbeEquipmentAsync().Forget();
        }

        private static async UniTaskVoid RerollProbeEquipmentAsync()
        {
            try
            {
                CharacterMainControl npc = _probe;
                if (npc == null)
                {
                    _lastStatus = "探针已消失，重掷取消";
                    return;
                }

                int stripped = DuckNpcOutfitter.StripEquipment(npc);
                _lastOutfit = await DuckNpcOutfitter.EquipRandomAsync(npc, null);

                _lastStatus = "已重掷装备：卸下 " + stripped + " 件，穿上 " + _lastOutfit.EquippedCount + " 件";
                ModBehaviour.LogInfo(LogPrefix + " " + _lastStatus);
                for (int i = 0; i < _lastOutfit.Equipped.Count; i++)
                {
                    ModBehaviour.LogInfo(LogPrefix + " 　装备 → " + _lastOutfit.Equipped[i]);
                }
            }
            catch (Exception e)
            {
                _lastStatus = "重掷装备异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 重掷装备异常: " + e);
            }
            finally
            {
                _equipInFlight = false;
            }
        }

        // ====================================================================
        // 保存为永久 NPC 数据
        // ====================================================================

        /// <summary>
        /// 把当前探针的脸 + 装备固化成一段可直接粘进 Assets/Data/DuckNpcs.json 的蓝图。
        /// </summary>
        /// <remarks>
        /// **存字面数据，不存种子。**
        /// 随机脸可以用 faceSeed 复现，但种子只在随机算法参数顺序不变时有效 ——
        /// 以后改一次 DuckNpcFaceRandomizer 的参数顺序，同一颗种子就会变出另一张脸。
        /// 永久 NPC 必须跨版本长相一致，所以这里写的是完整 faceJson +
        /// 明确的 equipmentTypeIds 数组。
        ///
        /// 装备也从角色物品树**实况读回**，而不是用上一次穿戴的返回值 ——
        /// 探针可能被重掷过。
        /// </remarks>
        internal static void SaveProbeAsPermanentNpc()
        {
            if (_probe == null)
            {
                _lastStatus = "当前没有探针，先生成一个再保存";
                return;
            }

            try
            {
                if (_probe.characterModel == null || _probe.characterModel.CustomFace == null)
                {
                    _lastStatus = "探针没有 CustomFace 实例，无法保存";
                    return;
                }

                CustomFaceSettingData face = _probe.characterModel.CustomFace.ConvertToSaveData();
                string faceJson = DuckNpcFaceCodec.ToJson(face);
                if (string.IsNullOrEmpty(faceJson))
                {
                    _lastStatus = "捏脸数据序列化失败";
                    return;
                }

                List<DuckNpcEquippedItem> equipped = DuckNpcOutfitter.ReadEquippedItems(_probe);
                string blueprint = BuildPermanentBlueprintJson(faceJson, equipped);

                string path = WriteReportFile("DuckNpcPermanent", ".json.txt", blueprint);
                bool clipboard = TryCopyToClipboard(blueprint);

                _lastStatus = "已保存永久 NPC 数据（装备 " + equipped.Count + " 件"
                    + (clipboard ? "，已进剪贴板" : "") + "）: " + (path ?? "落盘失败");
                ModBehaviour.LogInfo(LogPrefix + " 永久 NPC 数据已保存 clipboard=" + clipboard
                    + ", equipped=" + equipped.Count + ", file=" + (path ?? "(失败)"));
                ModBehaviour.LogInfo(LogPrefix + " PERMANENT_BLUEPRINT " + blueprint);
            }
            catch (Exception e)
            {
                _lastStatus = "保存永久 NPC 数据异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 保存永久 NPC 数据异常: " + e);
            }
        }

        /// <summary>
        /// 渲染一段可直接粘进 DuckNpcs.json 的蓝图对象。
        /// </summary>
        private static string BuildPermanentBlueprintJson(
            string faceJson,
            List<DuckNpcEquippedItem> equipped)
        {
            StringBuilder sb = new StringBuilder(4096);

            sb.AppendLine("// 直接把下面这个对象加进 Assets/Data/DuckNpcs.json 的 npcs 数组；");
            sb.AppendLine("// id / displayNameKey / scenes / 对话表请按需改。");
            sb.AppendLine("// 注意：JSON 不支持注释，粘贴时**不要**带上这三行。");
            sb.AppendLine("{");
            sb.AppendLine("  \"id\": \"duck_npc_TODO_RENAME\",");
            sb.AppendLine("  \"isPermanent\": true,");
            sb.AppendLine("  \"displayNameKey\": \"\",");
            sb.AppendLine("  \"faceMode\": \"json\",");
            sb.AppendLine("  \"faceJson\": " + EncodeJsonString(faceJson) + ",");

            string baseModel = string.Empty;
            try
            {
                if (_probe.characterModel != null)
                {
                    // 实例名带 "(Clone)" 后缀，蓝图里要的是预制体名
                    baseModel = _probe.characterModel.name.Replace("(Clone)", string.Empty).Trim();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 读底模名失败: " + e.Message);
            }
            sb.AppendLine("  \"baseModel\": \"" + baseModel + "\",");

            sb.AppendLine("  \"modelScale\": 1.0,");
            sb.AppendLine("  \"team\": \"player\",");
            sb.AppendLine("  \"invincible\": true,");
            sb.AppendLine("  \"showHealthBar\": false,");
            sb.AppendLine("  \"pushCharacter\": true,");
            sb.AppendLine("  \"randomEquipment\": false,");

            sb.Append("  \"equipmentTypeIds\": [");
            for (int i = 0; i < equipped.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(equipped[i].TypeId.ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine("],");

            sb.AppendLine("  \"canWander\": true,");
            sb.AppendLine("  \"wanderRadius\": 8.0,");
            sb.AppendLine("  \"scenes\": []");
            sb.AppendLine("}");

            sb.AppendLine();
            sb.AppendLine("// —— 装备明细（仅供核对，不要粘进 JSON）——");
            for (int i = 0; i < equipped.Count; i++)
            {
                DuckNpcEquippedItem item = equipped[i];
                sb.AppendLine("//   " + item.SlotKey + " = " + item.DisplayName + " (TypeID " + item.TypeId + ")");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 把一段字符串编码成合法的 JSON 字符串字面量（含引号）。
        /// </summary>
        /// <remarks>
        /// faceJson 本身就是一段 JSON，要作为**字符串值**嵌进外层 JSON，
        /// 必须转义引号和反斜杠，否则粘进去就是坏 JSON。
        /// </remarks>
        private static string EncodeJsonString(string raw)
        {
            if (raw == null)
            {
                return "\"\"";
            }

            StringBuilder sb = new StringBuilder(raw.Length + 32);
            sb.Append('"');
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string ColorToHex(Color color)
        {
            try
            {
                return "#" + ColorUtility.ToHtmlStringRGB(color);
            }
            catch
            {
                return "(色值读取失败)";
            }
        }

        // ====================================================================
        // 4. 探针状态观测
        // ====================================================================

        /// <summary>
        /// 把探针的运行时观测项打成一份报告并落盘。
        /// 这是「无 preset 裸造角色到底是什么行为」的答案来源。
        /// </summary>
        internal static void DumpProbeState()
        {
            try
            {
                string report = BuildProbeStateReport();
                string path = WriteReportFile("DuckNpcProbeState", "md", report);
                _lastReportPath = path ?? _lastReportPath;
                _lastStatus = path != null
                    ? "已输出探针状态: " + path
                    : "探针状态落盘失败，详见日志";
                ModBehaviour.LogInfo(LogPrefix + " 探针状态报告:\n" + report);
            }
            catch (Exception e)
            {
                _lastStatus = "探针状态输出异常: " + e.Message;
                ModBehaviour.LogWarning(LogPrefix + " 探针状态输出异常: " + e);
            }
        }

        private static string BuildProbeStateReport()
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("# 捏脸 NPC 探针状态");
            sb.AppendLine();
            sb.AppendLine("- 时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            try
            {
                sb.AppendLine("- 场景: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            catch (Exception e)
            {
                sb.AppendLine("- 场景: (读取失败: " + e.GetType().Name + ")");
            }
            sb.AppendLine("- 生成中: " + _spawnInFlight);
            sb.AppendLine();

            if (_probe == null)
            {
                sb.AppendLine("> 当前没有探针实例（未生成，或已被回收/销毁）。");
                return sb.ToString();
            }

            sb.AppendLine("| 观测项 | 值 |");
            sb.AppendLine("| --- | --- |");
            AppendProbeRow(sb, "GameObject", () => _probe.gameObject.name);
            AppendProbeRow(sb, "activeInHierarchy", () => _probe.gameObject.activeInHierarchy.ToString());
            AppendProbeRow(sb, "所属场景", () => _probe.gameObject.scene.name);
            AppendProbeRow(sb, "位置", () => _probe.transform.position.ToString());
            AppendProbeRow(sb, "Team", () => _probe.Team.ToString());
            AppendProbeRow(sb, "IsMainCharacter", () => _probe.IsMainCharacter.ToString());
            AppendProbeRow(sb, "characterPreset", () => _probe.characterPreset == null ? "null（预期如此）" : _probe.characterPreset.nameKey);
            AppendProbeRow(sb, "characterModel", () => _probe.characterModel == null ? "null" : _probe.characterModel.name);
            AppendProbeRow(sb, "CustomFace 实例", () => (_probe.characterModel != null && _probe.characterModel.CustomFace != null) ? "有" : "无");
            AppendProbeRow(sb, "aiCharacterController", () => _probe.aiCharacterController == null ? "null（预期如此，站桩）" : _probe.aiCharacterController.name);
            AppendProbeRow(sb, "Health.IsDead", () => _probe.Health == null ? "Health 为 null" : _probe.Health.IsDead.ToString());
            AppendProbeRow(sb, "Health.MaxHealth", () => _probe.Health == null ? "-" : _probe.Health.MaxHealth.ToString("F0", CultureInfo.InvariantCulture));
            AppendProbeRow(sb, "DuckNpcRuntimeMarker", () => DuckNpcRuntimeMarker.IsDuckNpc(_probe) ? "有（清场豁免生效）" : "**无（会被清场销毁）**");
            AppendProbeRow(sb, "Animator", () => DescribeAnimator(_probe));
            AppendProbeRow(sb, "当前捏脸 JSON", () => DescribeProbeFace(_probe));
            AppendProbeRow(sb, "脸来源", () => string.IsNullOrEmpty(_lastFaceSource) ? "(未记录)" : _lastFaceSource);

            AppendOutfitSection(sb);
            AppendPhysicsSection(sb);

            sb.AppendLine();
            sb.AppendLine("## 需要人工目视确认的项");
            sb.AppendLine();
            sb.AppendLine("- [x] 探针**站得住** —— 2026-09-04 已确认");
            sb.AppendLine("- [x] **无敌生效**、走远 100m 返回仍在 —— 2026-09-04 已确认");
            sb.AppendLine("- [x] 切图不残留、无报错 —— 2026-09-04 已确认（4 次过图零异常）");
            sb.AppendLine("- [ ] 探针有**待机动画**（呼吸/摇晃），不是一个静止雕像");
            sb.AppendLine("- [ ] **随机夸张脸**与周围鸭子/上一只探针明显不同（换脸按钮连点几次看）");
            sb.AppendLine("- [ ] **装备**（头盔/护甲/背包/武器）挂在身上且位置正确");
            sb.AppendLine("- [ ] **能不能穿过去** —— 若能穿过，对照上面「物理/碰撞诊断」两张表找差异");

            return sb.ToString();
        }

        /// <summary>
        /// 把探针当前的完整捏脸 JSON 打进日志。报告是 Markdown 表格，
        /// JSON 里的竖线会破表，所以完整内容只走日志。
        /// </summary>
        private static void LogProbeFaceJson(CharacterMainControl npc)
        {
            try
            {
                if (npc == null || npc.characterModel == null || npc.characterModel.CustomFace == null)
                {
                    return;
                }
                string json = DuckNpcFaceCodec.ToJson(npc.characterModel.CustomFace.ConvertToSaveData());
                if (!string.IsNullOrEmpty(json))
                {
                    ModBehaviour.LogInfo(LogPrefix + " PROBE_FACE_JSON " + json);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 输出探针脸 JSON 失败: " + e.Message);
            }
        }

        // ====================================================================
        // 装备
        // ====================================================================

        private static void AppendOutfitSection(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 装备");
            sb.AppendLine();

            if (_lastOutfit == null)
            {
                sb.AppendLine("> 本次生成没有请求装备。");
            }
            else
            {
                sb.AppendLine("穿戴成功 " + _lastOutfit.EquippedCount + " 件，跳过 " + _lastOutfit.Skipped.Count + " 项。");
                sb.AppendLine();
                for (int i = 0; i < _lastOutfit.Equipped.Count; i++)
                {
                    sb.AppendLine("- 穿上: " + _lastOutfit.Equipped[i]);
                }
                for (int i = 0; i < _lastOutfit.Skipped.Count; i++)
                {
                    sb.AppendLine("- 跳过: " + _lastOutfit.Skipped[i]);
                }
            }

            sb.AppendLine();
            sb.AppendLine("### 角色全部槽位");
            sb.AppendLine();
            try
            {
                List<string> keys = DuckNpcOutfitter.ListSlotKeys(_probe);
                if (keys.Count == 0)
                {
                    sb.AppendLine("> 未枚举到槽位。");
                }
                else
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        sb.AppendLine("- " + keys[i]);
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("> 槽位枚举异常: " + e.GetType().Name);
            }
        }

        // ====================================================================
        // 物理诊断（用于定位"无实体、能穿过去"）
        // ====================================================================

        /// <summary>
        /// 把探针和一只**官方角色**的物理组件并排列出来。
        /// 直接对比两边的组件清单和碰撞体状态就能看出缺了什么，
        /// 不用靠猜 ECM2 内部实现。
        /// </summary>
        private static void AppendPhysicsSection(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## 物理/碰撞诊断");
            sb.AppendLine();

            // 层碰撞矩阵是"能不能互相挡"的总闸：如果 Character↔Character 被忽略，
            // 那么任何角色都挡不住任何角色，"能穿过去"就是这游戏的正常行为，不是我们的 bug。
            try
            {
                int characterLayer = LayerMask.NameToLayer("Character");
                if (characterLayer >= 0)
                {
                    bool ignored = Physics.GetIgnoreLayerCollision(characterLayer, characterLayer);
                    sb.AppendLine("- 层碰撞矩阵 Character↔Character: "
                        + (ignored ? "**已忽略（角色之间本就不互相阻挡）**" : "生效（角色之间应当互相阻挡）"));
                    sb.AppendLine();
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("- 层碰撞矩阵读取异常: " + e.GetType().Name);
                sb.AppendLine();
            }

            sb.AppendLine("### 探针");
            sb.AppendLine();
            AppendPhysicsDump(sb, _probe);

            CharacterMainControl reference = FindReferenceOfficialCharacter();
            sb.AppendLine();
            sb.AppendLine("### 对照组：场景里的官方角色");
            sb.AppendLine();
            if (reference == null)
            {
                sb.AppendLine("> 当前场景没找到可用作对照的官方角色。");
                sb.AppendLine("> 到有敌人的图里再点一次，这一节才有意义。");
            }
            else
            {
                sb.AppendLine("对照角色: " + SafeGoName(reference)
                    + "，preset=" + (reference.characterPreset != null ? reference.characterPreset.nameKey : "null"));
                sb.AppendLine();
                AppendPhysicsDump(sb, reference);
            }
        }

        private static void AppendPhysicsDump(StringBuilder sb, CharacterMainControl character)
        {
            if (character == null)
            {
                sb.AppendLine("> 角色为空。");
                return;
            }

            GameObject go = null;
            try
            {
                go = character.gameObject;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 取角色 GameObject 失败: " + e.Message);
            }

            if (go == null)
            {
                sb.AppendLine("> GameObject 已销毁。");
                return;
            }

            GameObject target = go;
            CharacterMainControl owner = character;
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("| --- | --- |");
            AppendProbeRow(sb, "layer", () => target.layer + " (" + LayerMask.LayerToName(target.layer) + ")");
            AppendProbeRow(sb, "movementControl", () => owner.movementControl == null
                ? "**null**"
                : "有, MovementEnabled=" + owner.movementControl.MovementEnabled);
            AppendProbeRow(sb, "Rigidbody", () => DescribeRigidbodies(target));
            AppendProbeRow(sb, "根组件清单", () => DescribeRootComponents(target));
            AppendProbeRow(sb, "碰撞体", () => DescribeColliders(target));
            AppendProbeRow(sb, "AllowPushCharacters", () => DescribePushCharacters(target));
        }

        /// <summary>
        /// 读 ECM2 CharacterMovement.AllowPushCharacters。ECM2 不在反编译源码里，
        /// 只能反射拿；这是探针与官方角色之间最后一处可能的差异。
        /// </summary>
        private static string DescribePushCharacters(GameObject go)
        {
            try
            {
                Component[] components = go.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component c = components[i];
                    if (c == null) continue;
                    Type t = c.GetType();
                    if (t.Name != "CharacterMovement") continue;

                    System.Reflection.PropertyInfo prop = t.GetProperty("AllowPushCharacters");
                    if (prop == null)
                    {
                        return "CharacterMovement 上没有 AllowPushCharacters 属性";
                    }
                    return prop.GetValue(c, null).ToString();
                }
                return "未找到 CharacterMovement 组件";
            }
            catch (Exception e)
            {
                return "读取异常: " + e.GetType().Name;
            }
        }

        private static string DescribeRigidbodies(GameObject go)
        {
            Rigidbody[] bodies = go.GetComponentsInChildren<Rigidbody>(true);
            if (bodies == null || bodies.Length == 0)
            {
                return "无";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null) continue;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(rb.gameObject.name)
                  .Append(" kinematic=").Append(rb.isKinematic)
                  .Append(" detectCollisions=").Append(rb.detectCollisions);
            }
            return sb.Length == 0 ? "无" : sb.ToString();
        }

        private static string DescribeRootComponents(GameObject go)
        {
            Component[] components = go.GetComponents<Component>();
            if (components == null || components.Length == 0)
            {
                return "(空)";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(c == null ? "(missing script)" : c.GetType().Name);
            }
            return sb.ToString();
        }

        private static string DescribeColliders(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
            {
                return "**一个都没有**";
            }

            StringBuilder sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < colliders.Length && shown < 12; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(c.GetType().Name)
                  .Append(" on ").Append(c.gameObject.name)
                  .Append(" enabled=").Append(c.enabled)
                  .Append(" trigger=").Append(c.isTrigger)
                  .Append(" layer=").Append(LayerMask.LayerToName(c.gameObject.layer));
                shown++;
            }
            if (colliders.Length > shown)
            {
                sb.Append("; ...共 ").Append(colliders.Length).Append(" 个");
            }
            return sb.Length == 0 ? "**一个都没有**" : sb.ToString();
        }

        /// <summary>
        /// 找一只官方角色当对照：不是主角、不是探针、有 characterPreset。
        /// </summary>
        private static CharacterMainControl FindReferenceOfficialCharacter()
        {
            try
            {
                CharacterMainControl[] all = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (all == null)
                {
                    return null;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    CharacterMainControl c = all[i];
                    if (c == null) continue;
                    if (c == _probe) continue;
                    if (c.IsMainCharacter) continue;
                    if (c.characterPreset == null) continue;
                    return c;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 查找对照角色失败: " + e.Message);
            }
            return null;
        }

        private static string SafeGoName(CharacterMainControl character)
        {
            try
            {
                return character.gameObject.name;
            }
            catch
            {
                return "(已销毁)";
            }
        }

        private static void AppendProbeRow(StringBuilder sb, string label, Func<string> valueGetter)
        {
            string value;
            try
            {
                value = valueGetter();
            }
            catch (Exception e)
            {
                value = "读取异常: " + e.GetType().Name;
            }
            sb.AppendLine("| " + label + " | " + (string.IsNullOrEmpty(value) ? "(空)" : value) + " |");
        }

        private static string DescribeAnimator(CharacterMainControl npc)
        {
            if (npc.characterModel == null)
            {
                return "characterModel 为 null";
            }

            Animator animator = npc.characterModel.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                return "**无 Animator**";
            }

            string controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "**无 Controller**";
            return "有，controller=" + controllerName + ", enabled=" + animator.enabled;
        }

        private static string DescribeProbeFace(CharacterMainControl npc)
        {
            if (npc.characterModel == null || npc.characterModel.CustomFace == null)
            {
                return "无 CustomFace 实例";
            }

            string json = DuckNpcFaceCodec.ToJson(npc.characterModel.CustomFace.ConvertToSaveData());
            // 报告是 Markdown 表格，JSON 里的竖线会破表；这里只留摘要，完整 JSON 走日志。
            return string.IsNullOrEmpty(json)
                ? "序列化失败"
                : "长度 " + json.Length.ToString(CultureInfo.InvariantCulture) + " 字符（完整内容见日志 FACE_JSON 行）";
        }

        // ====================================================================
        // 落盘与剪贴板
        // ====================================================================

        /// <summary>
        /// 供同一套调试工具（如 PermanentDuckNpcDebug）复用的落盘入口。
        /// 报告目录与 F3 验收报告一致，不另建第二处。
        /// </summary>
        internal static string WriteDebugReport(string prefix, string extension, string content)
        {
            return WriteReportFile(prefix, extension, content);
        }

        private static string WriteReportFile(string prefix, string extension, string content)
        {
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string dir = Path.Combine(Application.persistentDataPath, "BossRushTestReports");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, prefix + "_" + stamp + "." + extension);
                File.WriteAllText(path, content, Encoding.UTF8);
                return path;
            }
            catch (Exception e)
            {
                ModBehaviour.LogWarning(LogPrefix + " 报告落盘失败: " + e.Message);
                return null;
            }
        }

        private static bool TryCopyToClipboard(string text)
        {
            try
            {
                GUIUtility.systemCopyBuffer = text;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 写剪贴板失败: " + e.Message);
                return false;
            }
        }

        // ====================================================================
        // 静态缓存复位
        // ====================================================================

        /// <summary>
        /// Mod 卸载 / 强制刷新时复位。不销毁探针 —— 卸载路径下场景对象另有清理，
        /// 这里只丢引用，避免把旧程序集的角色引用留在静态字段里。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            _probe = null;
            _spawnInFlight = false;
            _lastStatus = "尚未采集捏脸 NPC 数据";
            _lastReportPath = null;
            _lastOutfit = null;
            _lastFaceSource = null;
            _equipInFlight = false;
        }
    }
}
