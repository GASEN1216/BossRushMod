// ============================================================================
// DuckNpcRegistry.cs - 捏脸 NPC 蓝图注册表
// ============================================================================
// 模块说明：
//   按 AGENTS.md 4.8 Config 三层归位的**第 3 层**实现：
//     Assets/Data/DuckNpcs.json  +  Registry  +  硬编码 fallback
//
//   读取走全仓唯一入口 JsonDataRegistry.TryReadDataFile()，不另建第二个 parser。
//   解析走 ModeHJsonParser（Campaign / Audio 两张生产表用的同一个 token parser），
//   **不用 Unity JsonUtility** —— 实机 Unity 2022.3 在「int version + 对象数组」
//   这种 internal DTO 上会只填 version、静默把数组留成 null，
//   实测记录见 Campaign/CampaignContentCatalog.cs:136。本表与它完全同形。
//
//   JSON 读不到或解析不出任何一条蓝图时回落硬编码兜底，并走 CriticalLog
//   （数据表没生效属玩家可见故障，DevLog 在正式构建里会被整句编译掉）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 捏脸 NPC 蓝图注册表。
    /// </summary>
    internal static class DuckNpcRegistry
    {
        private const string DataFileName = "DuckNpcs.json";
        private const string LogPrefix = "[DuckNpcRegistry]";

        private static readonly Dictionary<string, DuckNpcBlueprint> _blueprints =
            new Dictionary<string, DuckNpcBlueprint>(StringComparer.Ordinal);
        private static readonly List<DuckNpcBlueprint> _ordered = new List<DuckNpcBlueprint>();
        private static bool _initialized;

        // ====================================================================
        // 查询
        // ====================================================================

        /// <summary>按 id 取蓝图。</summary>
        internal static bool TryGet(string id, out DuckNpcBlueprint blueprint)
        {
            EnsureInitialized();
            blueprint = null;
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }
            return _blueprints.TryGetValue(id, out blueprint);
        }

        /// <summary>全部蓝图，按 JSON 里的顺序。</summary>
        internal static IList<DuckNpcBlueprint> All
        {
            get
            {
                EnsureInitialized();
                return _ordered;
            }
        }

        internal static int Count
        {
            get
            {
                EnsureInitialized();
                return _ordered.Count;
            }
        }

        // ====================================================================
        // 初始化
        // ====================================================================

        internal static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            // 先置位再加载：加载过程里任何一处回调再进来都不会递归重入。
            _initialized = true;

            DuckNpcBlueprint[] loaded = LoadFromJson();
            if (loaded == null || loaded.Length == 0)
            {
                loaded = CreateFallbackBlueprints();
                ModBehaviour.CriticalLog(
                    "duck-npc-blueprint-fallback",
                    LogPrefix + " [WARNING] " + DataFileName + " 无有效蓝图，使用硬编码兜底");
            }

            Register(loaded);
            ModBehaviour.DevLog(LogPrefix + " 已装载蓝图 " + _ordered.Count + " 条");
        }

        private static void Register(DuckNpcBlueprint[] blueprints)
        {
            for (int i = 0; i < blueprints.Length; i++)
            {
                DuckNpcBlueprint blueprint = blueprints[i];
                if (blueprint == null)
                {
                    continue;
                }

                // 字段校验与缺省值已在 DuckNpcBlueprintTable.ParseRow 里做完，
                // 硬编码兜底也保证给全字段，所以这里只需要查 id 重复。
                if (string.IsNullOrEmpty(blueprint.id))
                {
                    continue;
                }

                if (_blueprints.ContainsKey(blueprint.id))
                {
                    // id 重复属配置错误，保留先出现的那条并明确报出来，
                    // 否则后写的会静默覆盖前一条，排查时完全看不出。
                    ModBehaviour.CriticalLog(
                        "duck-npc-blueprint-dup-" + blueprint.id,
                        LogPrefix + " [WARNING] 蓝图 id 重复，已忽略后一条: " + blueprint.id);
                    continue;
                }

                _blueprints.Add(blueprint.id, blueprint);
                _ordered.Add(blueprint);
            }
        }

        private static DuckNpcBlueprint[] LoadFromJson()
        {
            string json;
            if (!JsonDataRegistry.TryReadDataFile(DataFileName, out json))
            {
                return null;
            }

            DuckNpcBlueprintTable table;
            string parseError;
            if (!DuckNpcBlueprintTable.TryParse(json, out table, out parseError))
            {
                ModBehaviour.CriticalLog(
                    "duck-npc-blueprint-parse",
                    LogPrefix + " [ERROR] " + DataFileName + " 解析失败: " + (parseError ?? "unknown"));
                return null;
            }

            if (table == null || table.npcs == null || table.npcs.Length == 0)
            {
                return null;
            }

            return table.npcs;
        }

        /// <summary>
        /// 硬编码兜底。只保留一条最小可用蓝图，作用是让 JSON 缺失时
        /// 整条链路仍然可跑、可被 F3 观测到，而不是静默变成"什么都没有"。
        /// </summary>
        private static DuckNpcBlueprint[] CreateFallbackBlueprints()
        {
            // DTO 已禁用字段初始化器，兜底这里必须逐项写全，
            // 否则 bool 会是 false、float 会是 0，NPC 会变成一只可被打死的零缩放鸭子。
            DuckNpcBlueprint sample = new DuckNpcBlueprint();
            sample.id = "duck_npc_sample";
            sample.displayNameKey = string.Empty;
            sample.faceMode = DuckNpcFaceModes.RandomVaried;
            // 固定种子：兜底 NPC 也应该每次长得一样，否则没法判断"是不是同一只"。
            sample.faceSeed = 20260904;
            sample.faceJson = string.Empty;
            sample.baseModel = string.Empty;
            sample.modelScale = 1f;
            sample.team = "player";
            sample.invincible = true;
            sample.showHealthBar = false;
            sample.pushCharacter = true;
            sample.randomEquipment = true;
            sample.equipmentSeed = 20260904;
            sample.equipmentTypeIds = null;
            sample.equipmentSlots = null;
            sample.canWander = false;
            sample.wanderRadius = 8f;
            // 兜底蓝图不给 scenes：JSON 都读不到了，不该再自动往场景里塞 NPC，
            // 只保留"能被代码显式召唤"的能力，避免故障状态下污染玩家存档场景。
            sample.scenes = null;

            return new DuckNpcBlueprint[] { sample };
        }

        // ====================================================================
        // 脸解析
        // ====================================================================

        /// <summary>
        /// 按蓝图解析出实际要用的捏脸数据。
        /// 返回 false 表示这条蓝图不带脸，调用方应沿用底模自带的脸。
        /// </summary>
        internal static bool TryResolveFace(DuckNpcBlueprint blueprint, out CustomFaceSettingData face)
        {
            face = default(CustomFaceSettingData);
            if (blueprint == null)
            {
                return false;
            }

            switch (blueprint.faceMode)
            {
                case DuckNpcFaceModes.Json:
                    if (DuckNpcFaceCodec.TryFromJsonNormalized(blueprint.faceJson, out face))
                    {
                        return true;
                    }
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 蓝图 " + blueprint.id + " 的 faceJson 解析失败");
                    return false;

                case DuckNpcFaceModes.RandomVaried:
                    return DuckNpcFaceRandomizer.TryCreateSeeded(
                        DuckNpcFaceWildness.Varied, blueprint.faceSeed, out face);

                case DuckNpcFaceModes.RandomExaggerated:
                    return DuckNpcFaceRandomizer.TryCreateSeeded(
                        DuckNpcFaceWildness.Exaggerated, blueprint.faceSeed, out face);

                case DuckNpcFaceModes.Baseline:
                default:
                    return DuckNpcFaceCatalog.TryGetDefaultFace(out face);
            }
        }

        // ====================================================================
        // 缓存
        // ====================================================================

        /// <summary>
        /// 清空注册表。由 AlwaysOnRuntimeHooks 在 Mod 卸载时调用 ——
        /// 蓝图本身是纯数据不握 Unity 引用，但重载后 JSON 可能已改，必须重读。
        /// </summary>
        internal static void ResetStaticCaches()
        {
            _blueprints.Clear();
            _ordered.Clear();
            _initialized = false;
        }
    }
}
