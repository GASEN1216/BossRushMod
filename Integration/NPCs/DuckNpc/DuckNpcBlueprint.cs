// ============================================================================
// DuckNpcBlueprint.cs - 捏脸 NPC 蓝图（一个新 NPC 的全部定义）
// ============================================================================
// 模块说明：
//   一个捏脸 NPC 长什么样、站哪个阵营、穿什么装备，全部由一条蓝图描述。
//   蓝图落在 Assets/Data/DuckNpcs.json
//   （AGENTS.md 4.8 Config 三层归位的第 3 层：大型数据表 + Registry + 硬编码 fallback）。
//
//   **新增一个 NPC 的成本 = 往 JSON 里加一条**，不需要建模、不需要打 AssetBundle、
//   不需要写 C#。这是整条路线的目的。
//
//   两条 DTO 纪律（都是本仓库踩过坑换来的，别退回去）：
//
//   1. **不用 Unity JsonUtility**。实机 Unity 2022.3 在「int version + 对象数组」
//      这种 internal DTO 上会只填 version、**静默把数组留成 null**。
//      Campaign 的章节表就是因此改用 token parser 的
//      （见 Campaign/CampaignContentCatalog.cs:136 的实测记录），
//      Audio 的曲目表同理（Audio/BossBgmTrackTable.cs:33）。
//      本表结构与它们完全同形，因此同样复用 ModeHJsonParser。
//
//   2. **字段禁用初始化器**（同 Campaign/CampaignPersistence.cs:15 的 ModeG 纪律）。
//      默认值一律在 TryParse 里显式写，读代码时"JSON 没写会变成什么"一目了然，
//      也不依赖任何序列化器是否走默认构造。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>捏脸 NPC 的脸从哪来。</summary>
    internal static class DuckNpcFaceModes
    {
        /// <summary>官方默认基线，最朴素的一张鸭脸。</summary>
        public const string Baseline = "baseline";

        /// <summary>用蓝图里的 faceJson（游戏内理发镜捏好后导出的那段）。</summary>
        public const string Json = "json";

        /// <summary>随机脸，正常审美档。</summary>
        public const string RandomVaried = "randomVaried";

        /// <summary>随机脸，夸张档。</summary>
        public const string RandomExaggerated = "randomExaggerated";
    }

    /// <summary>
    /// 一个捏脸 NPC 的完整定义。字段无初始化器，默认值见 DuckNpcBlueprintTable.TryParse。
    /// </summary>
    [Serializable]
    internal sealed class DuckNpcBlueprint
    {
        // —— 身份 ——
        /// <summary>唯一标识。会写进 DuckNpcRuntimeMarker，供交互和好感度反查。</summary>
        public string id;

        /// <summary>本地化 key（可空）。</summary>
        public string displayNameKey;

        // —— 外观 ——
        /// <summary>脸的来源，取值见 DuckNpcFaceModes。</summary>
        public string faceMode;

        /// <summary>faceMode = json 时使用。内容是 CustomFaceSettingData 的 JSON。</summary>
        public string faceJson;

        /// <summary>随机脸种子。非 0 → 每次生成都长得一样；0 → 每次都不同。</summary>
        public int faceSeed;

        /// <summary>
        /// 底模预制体名（可空 → 用 Prefabs.DefaultCharacterModel）。
        /// 必须是挂了 CustomFaceInstance 的底模，可用清单见 F3 家底报告第 2 节。
        /// </summary>
        public string baseModel;

        /// <summary>模型缩放，1 为原尺寸。</summary>
        public float modelScale;

        // —— 本体 ——
        /// <summary>阵营名，对应 Teams 枚举。</summary>
        public string team;

        /// <summary>是否无敌。</summary>
        public bool invincible;

        /// <summary>是否显示官方血条。</summary>
        public bool showHealthBar;

        /// <summary>是否挤压其他角色（官方 pushCharacter 等价项）。</summary>
        public bool pushCharacter;

        // —— 装备 ——
        /// <summary>是否随机穿一套装备。equipmentTypeIds 非空时忽略本项。</summary>
        public bool randomEquipment;

        /// <summary>随机装备种子。非 0 → 每次穿的都一样。</summary>
        public int equipmentSeed;

        /// <summary>固定装备 TypeID 列表（可空）。</summary>
        public int[] equipmentTypeIds;

        /// <summary>随机装备限定槽位（可空 → DuckNpcOutfitter.DefaultVisualSlotKeys）。</summary>
        public string[] equipmentSlots;

        // —— 移动 ——
        /// <summary>是否会自己走动（接官方 A* 寻路）。false = 站桩。</summary>
        public bool canWander;

        /// <summary>漫步半径（米）。canWander = false 时无意义。</summary>
        public float wanderRadius;

        // —— 场景 ——
        /// <summary>允许生成的场景名（可空 → 任何场景都不自动生成，只能被代码显式召唤）。</summary>
        public string[] scenes;

        // —— 永久 NPC ——
        /// <summary>
        /// 是否是常驻永久 NPC（接好感度/对话/送礼/婚姻），而不是一次性随机 NPC。
        /// </summary>
        public bool isPermanent;

        /// <summary>永久 NPC 的扩展数据。isPermanent 为 true 时必须非 null。</summary>
        public PermanentDuckNpcData permanent;

        // ====================================================================
        // 派生
        // ====================================================================

        /// <summary>阵营名 → Teams 枚举。解析失败回落 player 并记一行。</summary>
        internal Teams ResolveTeam()
        {
            try
            {
                return (Teams)Enum.Parse(typeof(Teams), team, true);
            }
            catch (Exception)
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + id + " 阵营名非法: " + team + "，回落 player");
                return Teams.player;
            }
        }

        /// <summary>是否配置了任何装备。</summary>
        internal bool HasEquipment
        {
            get { return randomEquipment || (equipmentTypeIds != null && equipmentTypeIds.Length > 0); }
        }

        /// <summary>该蓝图是否允许在指定场景自动生成。</summary>
        internal bool AllowsScene(string sceneName)
        {
            if (scenes == null || scenes.Length == 0 || string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            for (int i = 0; i < scenes.Length; i++)
            {
                if (string.Equals(scenes[i], sceneName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Assets/Data/DuckNpcs.json 的顶层结构 + 解析器。
    /// </summary>
    internal sealed class DuckNpcBlueprintTable
    {
        public int version;
        public DuckNpcBlueprint[] npcs;

        /// <summary>
        /// 解析整张表。任何一行结构性非法都只丢那一行并记原因，
        /// 但**整表解析失败**（根不是对象、npcs 不是数组）返回 false，
        /// 由调用方整表回退硬编码兜底 —— 半张表比没有表更难排查。
        /// </summary>
        internal static bool TryParse(string json, out DuckNpcBlueprintTable table, out string error)
        {
            table = null;

            ModeHJsonValue root;
            if (!ModeHJsonParser.TryParse(json, out root, out error))
            {
                return false;
            }

            if (root == null || root.Kind != ModeHJsonKind.Object)
            {
                error = "root_not_object";
                return false;
            }

            int version;
            if (!root.TryGetInt("version", out version) || version != 1)
            {
                error = "unsupported_version";
                return false;
            }

            List<ModeHJsonValue> rows;
            if (!root.TryGetArray("npcs", out rows))
            {
                error = "npcs_not_array";
                return false;
            }

            List<DuckNpcBlueprint> parsed = new List<DuckNpcBlueprint>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                ModeHJsonValue row = rows[i];
                if (row == null || row.Kind != ModeHJsonKind.Object)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图第 " + i + " 行不是对象，已丢弃");
                    continue;
                }

                DuckNpcBlueprint blueprint = ParseRow(row, i);
                if (blueprint != null)
                {
                    parsed.Add(blueprint);
                }
            }

            table = new DuckNpcBlueprintTable();
            table.version = version;
            table.npcs = parsed.ToArray();
            error = null;
            return true;
        }

        /// <summary>
        /// 解析一行。缺省值全部在这里显式给出 —— 这就是「JSON 没写会变成什么」的唯一答案。
        /// </summary>
        private static DuckNpcBlueprint ParseRow(ModeHJsonValue row, int index)
        {
            DuckNpcBlueprint b = new DuckNpcBlueprint();

            if (!row.TryGetString("id", out b.id) || string.IsNullOrEmpty(b.id))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图第 " + index + " 行缺少 id，已丢弃");
                return null;
            }

            if (!row.TryGetString("displayNameKey", out b.displayNameKey))
            {
                b.displayNameKey = string.Empty;
            }

            if (!row.TryGetString("faceMode", out b.faceMode) || string.IsNullOrEmpty(b.faceMode))
            {
                b.faceMode = DuckNpcFaceModes.Baseline;
            }

            if (!row.TryGetString("faceJson", out b.faceJson))
            {
                b.faceJson = string.Empty;
            }

            if (b.faceMode == DuckNpcFaceModes.Json && string.IsNullOrEmpty(b.faceJson))
            {
                // 声明了用 JSON 却没给 JSON：降级到基线而不是丢弃整条蓝图，
                // 让 NPC 还能出场，问题在日志里可见。
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + b.id
                    + " faceMode=json 但 faceJson 为空，降级为 baseline");
                b.faceMode = DuckNpcFaceModes.Baseline;
            }

            if (!row.TryGetInt("faceSeed", out b.faceSeed))
            {
                b.faceSeed = 0;
            }

            if (!row.TryGetString("baseModel", out b.baseModel))
            {
                b.baseModel = string.Empty;
            }

            if (!row.TryGetFloat("modelScale", out b.modelScale) || b.modelScale <= 0f)
            {
                b.modelScale = 1f;
            }
            // 缩放上下限：太小看不见，太大挡视野且会卡进地形。
            b.modelScale = Mathf.Clamp(b.modelScale, 0.2f, 3f);

            if (!row.TryGetString("team", out b.team) || string.IsNullOrEmpty(b.team))
            {
                // 默认玩家方：这是清场豁免与不被攻击的根基。
                b.team = "player";
            }

            if (!row.TryGetBool("invincible", out b.invincible))
            {
                b.invincible = true;
            }

            if (!row.TryGetBool("showHealthBar", out b.showHealthBar))
            {
                b.showHealthBar = false;
            }

            if (!row.TryGetBool("pushCharacter", out b.pushCharacter))
            {
                b.pushCharacter = true;
            }

            if (!row.TryGetBool("randomEquipment", out b.randomEquipment))
            {
                b.randomEquipment = false;
            }

            if (!row.TryGetInt("equipmentSeed", out b.equipmentSeed))
            {
                b.equipmentSeed = 0;
            }

            b.equipmentTypeIds = ReadIntArray(row, "equipmentTypeIds", b.id);
            b.equipmentSlots = ReadStringArray(row, "equipmentSlots", b.id);
            b.scenes = ReadStringArray(row, "scenes", b.id);

            if (!row.TryGetBool("canWander", out b.canWander))
            {
                b.canWander = false;
            }

            if (!row.TryGetFloat("wanderRadius", out b.wanderRadius) || b.wanderRadius <= 0f)
            {
                b.wanderRadius = 8f;
            }

            if (!row.TryGetBool("isPermanent", out b.isPermanent))
            {
                b.isPermanent = false;
            }

            if (b.isPermanent)
            {
                b.permanent = PermanentDuckNpcData.Parse(row, b.id);
                if (b.permanent == null)
                {
                    // 声明了永久却没给 permanent 子对象：降级成一次性 NPC 而不是丢弃整条，
                    // 让它还能出场，问题在日志里可见。
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + b.id
                        + " isPermanent=true 但缺少 permanent 子对象，已降级为一次性 NPC");
                    b.isPermanent = false;
                }
                else if (b.faceMode != DuckNpcFaceModes.Json)
                {
                    // 永久 NPC 必须用字面脸数据。种子只在随机算法参数顺序不变时有效，
                    // 一旦调整 DuckNpcFaceRandomizer，同一颗种子会变出另一张脸 ——
                    // 而永久 NPC 的长相必须跨版本一致。
                    ModBehaviour.CriticalLog(
                        "duck-npc-permanent-face-mode-" + b.id,
                        "[DuckNpc] [WARNING] 永久 NPC " + b.id + " 的 faceMode 不是 json（当前 "
                        + b.faceMode + "）。随机种子跨版本会漂，长相不保证稳定。"
                        + "请用 F3「保存为永久 NPC 数据」导出字面 faceJson。");
                }
            }

            return b;
        }

        private static int[] ReadIntArray(ModeHJsonValue row, string key, string blueprintId)
        {
            if (row.GetProperty(key) == null)
            {
                return null;
            }

            List<ModeHJsonValue> items;
            if (!row.TryGetArray(key, out items))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + key + " 不是数组，已忽略");
                return null;
            }

            List<int> result = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ModeHJsonValue item = items[i];
                if (item == null || item.Kind != ModeHJsonKind.Integer)
                {
                    ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + key + " 含非整数项，已忽略整个数组");
                    return null;
                }
                result.Add((int)item.IntegerValue);
            }
            return result.ToArray();
        }

        private static string[] ReadStringArray(ModeHJsonValue row, string key, string blueprintId)
        {
            if (row.GetProperty(key) == null)
            {
                return null;
            }

            List<string> values;
            if (!row.TryGetStringList(key, out values))
            {
                ModBehaviour.DevLog("[DuckNpc] [WARNING] 蓝图 " + blueprintId + " 的 " + key + " 不是字符串数组，已忽略");
                return null;
            }
            return values.ToArray();
        }
    }
}
