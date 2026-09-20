// ============================================================================
// CodexOfficialBossRegistry.cs - 官方 Boss 名单（图鉴分类的唯一判据）
// ============================================================================
// 为什么需要它：
//   图鉴目录的来源是 BossFilter 的过滤池，而那张池子的口径是
//   「showName && baseHealth > 100 && 非玩家/中立阵营」——它会把官方明确**不是 Boss**
//   的精英怪（Cname_Boss_Red / Cname_Boss_Blue / Cname_UltraMan / Cname_Ghost …）一起收进来。
//   旧的 FormatCategory 把整张池一律标成「官方 Boss」，玩家在详情页看到的分类是错的
//   （owner 2026-09-20：「原版官方 boss 分类感觉不是很好」）。
//
// 判据来源：
//   官方生物数据库 https://escapefromduckov.net/zh/wiki/creatures 的 `isBoss` 标记，
//   2026-09-20 快照。数据落在 Assets/Data/CodexOfficialBosses.json（AGENTS 4.8 第 3 层：
//   大型数据表 = JSON + Registry + guard + 硬编码 fallback）。
//
// 两个消费者（一份名单同时管分类和目录，避免两头打架）：
//   - CodexView_Grid.FormatCategory：详情页的「分类」一行；
//   - CodexBossCatalog.AddOfficialRosterEntries：过滤池没给出的官方 Boss 补成锁定卡。
//
// 分类口径（FormatCategory 消费）：
//   - officialBossNameKeys  里的  -> 官方 Boss
//   - officialNonBossNameKeys 里的 -> 官方精英（是官方生物，但官方没给 Boss 标）
//   - 两张表都没有            -> 本 Mod 自己加的（自定义 Boss / 丧尸 Boss / 历史条目）
//
// 硬约束：
//   - 纯查询，无 Unity 依赖、无副作用，可被 guard 与离线夹具单独推理；
//   - 惰性初始化 + 一次性读表；读表失败静默回落硬编码名单（fail-open，分类退化但面板照开）；
//   - 名单是**展示判据**，不参与掉落、刷怪或存档，改它不会破坏任何兼容面。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>官方 Boss / 官方生物名单。惰性建表，fail-open。</summary>
    internal static class CodexOfficialBossRegistry
    {
        private const string DataFileName = "CodexOfficialBosses.json";

        private static readonly object _lock = new object();
        private static HashSet<string> _bosses;
        private static HashSet<string> _others;
        /// <summary>
        /// 官方 Boss 名单的**有序**副本。HashSet 的枚举顺序不保证稳定，
        /// 而目录是玩家一眼扫过去的列表：顺序一变，卡片就会莫名其妙换位置。
        /// 因此登记时同时留一份 List，补目录时按它走。
        /// </summary>
        private static List<string> _bossOrder;
        private static bool _loadedFromJson;

        /// <summary>诊断：名单是否来自 JSON（false = 走了硬编码兜底）。</summary>
        internal static bool LoadedFromJson
        {
            get { EnsureBuilt(); return _loadedFromJson; }
        }

        /// <summary>官方 Boss 名单条目数。</summary>
        internal static int OfficialBossCount
        {
            get { EnsureBuilt(); return _bosses.Count; }
        }

        /// <summary>
        /// 官方 Boss 名单的只读有序快照。CodexBossCatalog 用它把过滤池没给出的
        /// 官方 Boss 补成锁定卡；调用方不得修改返回的列表（这里直接返回内部副本，
        /// 名单是进程内只读数据，构建后不再变动）。
        /// </summary>
        internal static List<string> OfficialBossKeys()
        {
            EnsureBuilt();
            return _bossOrder;
        }

        /// <summary>该 nameKey 是否在官方 Boss 名单里。</summary>
        internal static bool IsOfficialBoss(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey)) return false;
            EnsureBuilt();
            return _bosses.Contains(nameKey);
        }

        /// <summary>该 nameKey 是否是官方生物（含 Boss 与非 Boss）。</summary>
        internal static bool IsOfficialCreature(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey)) return false;
            EnsureBuilt();
            return _bosses.Contains(nameKey) || _others.Contains(nameKey);
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _bosses = null;
                _others = null;
                _bossOrder = null;
                _loadedFromJson = false;
            }
        }

        private static void EnsureBuilt()
        {
            if (_bosses != null && _others != null && _bossOrder != null) return;
            lock (_lock)
            {
                if (_bosses != null && _others != null && _bossOrder != null) return;

                HashSet<string> bosses = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> others = new HashSet<string>(StringComparer.Ordinal);
                List<string> bossOrder = new List<string>(48);
                bool fromJson = false;

                try
                {
                    string json;
                    if (JsonDataRegistry.TryReadDataFile(DataFileName, out json))
                    {
                        BossRushJsonValue root = BossRushJsonParser.ParseOrNull(json);
                        if (root != null && root.Kind == BossRushJsonKind.Object)
                        {
                            int loaded = ReadKeys(root, "officialBossNameKeys", bosses, bossOrder)
                                + ReadKeys(root, "officialNonBossNameKeys", others, null);
                            fromJson = loaded > 0;
                        }
                    }
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog(CodexTuning.LogPrefix
                        + "[WARNING] 官方 Boss 名单读取失败，使用硬编码兜底: " + e.Message);
                    fromJson = false;
                }

                if (!fromJson)
                {
                    // 回退硬编码意味着 JSON 没生效，分类会退化但不阻塞面板
                    ModBehaviour.CriticalLog(
                        "codex-official-boss-fallback",
                        "[Codex] [WARNING] " + DataFileName + " 无有效名单，使用硬编码兜底");
                    bosses.Clear();
                    others.Clear();
                    bossOrder.Clear();
                    for (int i = 0; i < FallbackBosses.Length; i++)
                    {
                        if (bosses.Add(FallbackBosses[i])) bossOrder.Add(FallbackBosses[i]);
                    }
                    for (int i = 0; i < FallbackOthers.Length; i++) others.Add(FallbackOthers[i]);
                }

                _bosses = bosses;
                _others = others;
                _bossOrder = bossOrder;
                _loadedFromJson = fromJson;
            }
        }

        private static int ReadKeys(BossRushJsonValue root, string field, HashSet<string> sink, List<string> order)
        {
            List<string> keys;
            if (!root.TryGetStringList(field, out keys) || keys == null) return 0;

            int added = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key)) continue;
                if (!sink.Add(key)) continue;
                added++;
                if (order != null) order.Add(key);
            }
            return added;
        }

        #region 硬编码兜底（与 Assets/Data/CodexOfficialBosses.json 同源，CodexOfficialBossRegistryGuard 交叉核对）

        private static readonly string[] FallbackBosses =
        {
            "Character_Alex", "Character_Fo", "Character_Jeff",
            "Character_Ming", "Character_Orange", "Character_Xavier",
            "Cname_BALeader", "Cname_Boss_3Shot", "Cname_Boss_Arcade",
            "Cname_Boss_Fly", "Cname_Boss_Shot", "Cname_Boss_Sniper",
            "Cname_Grenade", "Cname_Hunter", "Cname_IslandBoss",
            "Cname_Killa", "Cname_PMCLeader", "Cname_Prison_Boss",
            "Cname_RPG", "Cname_RaiderIce", "Cname_Roadblock",
            "Cname_SchoolBully", "Cname_SenorEngineer", "Cname_ServerGuardian",
            "Cname_ShortEagle", "Cname_SnowMan", "Cname_SnowMan_Island",
            "Cname_Snow_BigIce", "Cname_Snow_Fleeze", "Cname_Snow_Igny",
            "Cname_Speedy", "Cname_Speedy_Ice", "Cname_StormBoss1",
            "Cname_StormBoss2", "Cname_StormBoss3", "Cname_StormBoss4",
            "Cname_StormBoss5", "Cname_Tagilla", "Cname_Vida",
            "Cname_XING",
        };

        private static readonly string[] FallbackOthers =
        {
            "Character_Mud", "Character_SnowPMC", "Cname_3Shot_Child",
            "Cname_BALeader_Child", "Cname_Bear", "Cname_BoomCar",
            "Cname_Boss_Blue", "Cname_Boss_Fly_Alone", "Cname_Boss_Fly_Child",
            "Cname_Boss_LionHead", "Cname_Boss_Red", "Cname_CrazyRob",
            "Cname_DengWolf", "Cname_Football_1", "Cname_Football_2",
            "Cname_Ghost", "Cname_GunTurret", "Cname_Horse",
            "Cname_LabTestObjective", "Cname_MonsterClimb", "Cname_Moto",
            "Cname_Mushroom", "Cname_Prison", "Cname_Raider",
            "Cname_RobSpider", "Cname_Scav", "Cname_ScavRage",
            "Cname_SchoolBully_Child", "Cname_SpeedyChild", "Cname_SpeedyChild_Ice",
            "Cname_StormBoss1_Child", "Cname_StormCreature", "Cname_StormRobot",
            "Cname_StormVirus", "Cname_SuperRob", "Cname_UltraMan",
            "Cname_Usec", "Cname_Wolf", "Cname_WolfKing_Ice",
            "Cname_XINGS", "Cname_Zombie", "MerchantName_Myst",
        };

        #endregion
    }
}
