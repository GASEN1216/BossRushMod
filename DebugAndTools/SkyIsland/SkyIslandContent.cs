using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class SkyIslandEncounterDefinition
    {
        internal string Id, Marker;
        internal int Count;
        internal bool Manual;
        /// <summary>随从档次：组内第 1 名之外的所有人。</summary>
        internal SkyIslandEnemyTier Tier;
        /// <summary>带队档次：组内第 1 名。普通组与随从同档，守卫组与 Boss 组由它区分。</summary>
        internal SkyIslandEnemyTier Lead;
        internal SkyIslandEnemyTier TierFor(int index) { return index == 0 ? Lead : Tier; }
    }
    internal sealed class SkyIslandGateDefinition
    {
        internal string Id;
        internal SkyIslandStoryFlag Required;
        internal bool Any;
        internal bool IsOpen(SkyIslandStoryData story)
        {
            return story != null && (Any ? (story.flags & (int)Required) != 0 : story.Has(Required));
        }
    }
    internal sealed class SkyIslandContentData
    {
        internal SkyIslandEncounterDefinition[] Encounters;
        internal SkyIslandGateDefinition[] Gates;
        internal string Source;
        internal bool IsGateOpen(string id, SkyIslandStoryData story)
        {
            foreach (SkyIslandGateDefinition gate in Gates) if (gate.Id == id) return gate.IsOpen(story);
            return false;
        }
    }

    /// <summary>COMPAT：正式遭遇与进度门内容表；严格校验稳定身份、作者标记与完整集合，错误整表回退。</summary>
    internal static class SkyIslandContent
    {
        internal const string RelativePath = "Assets/Data/SkyIsland/World.json";
        internal const int Version = 1;

        internal static SkyIslandContentData Load()
        {
            try
            {
                string raw;
                if (!JsonDataRegistry.TryReadDataFile("SkyIsland", "World.json", out raw)) return CreateFallback();
                SkyIslandContentData parsed;
                string error;
                if (TryParse(raw, out parsed, out error)) return parsed;
                ModBehaviour.CriticalLog("sky-island-content-invalid", "[SkyIsland] [WARNING] 内容表校验失败，使用完整内置表：" + error);
            }
            catch (Exception e) { ModBehaviour.CriticalLog("sky-island-content-read", "[SkyIsland] [WARNING] 内容表不可读，使用完整内置表：" + e.Message); }
            return CreateFallback();
        }

        internal static bool TryParse(string raw, out SkyIslandContentData result, out string error)
        {
            result = null;
            error = "invalid_root";
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            List<BossRushJsonValue> encounters, gates;
            if (!Fields(root, "version", "encounters", "gates") || !root.TryGetInt("version", out version) || version != Version ||
                !root.TryGetArray("encounters", out encounters) || !root.TryGetArray("gates", out gates)) return false;
            SkyIslandContentData expected = CreateFallback();
            if (encounters.Count != expected.Encounters.Length || gates.Count != expected.Gates.Length)
            { error = "incomplete_content"; return false; }
            var parsedEncounters = new List<SkyIslandEncounterDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushJsonValue item in encounters)
            {
                string id, marker, tier, lead; int count; bool manual;
                SkyIslandEnemyTier parsedTier, parsedLead;
                if (!Fields(item, "id", "marker", "count", "manual", "tier", "lead") || !item.TryGetString("id", out id) ||
                    !item.TryGetString("marker", out marker) || !item.TryGetInt("count", out count) ||
                    !item.TryGetBool("manual", out manual) || !item.TryGetString("tier", out tier) ||
                    !item.TryGetString("lead", out lead) || !TryParseTier(tier, out parsedTier) ||
                    !TryParseTier(lead, out parsedLead) || count < 1 || count > 4 || !ids.Add(id))
                { error = "invalid_encounter"; return false; }
                SkyIslandEncounterDefinition known = Array.Find(expected.Encounters, e => e.Id == id);
                if (known == null || known.Marker != marker || known.Manual != manual ||
                    known.Tier != parsedTier || known.Lead != parsedLead)
                { error = "encounter_binding:" + id; return false; }
                parsedEncounters.Add(new SkyIslandEncounterDefinition { Id = id, Marker = marker, Count = count,
                    Manual = manual, Tier = parsedTier, Lead = parsedLead });
            }
            ids.Clear();
            var parsedGates = new List<SkyIslandGateDefinition>();
            foreach (BossRushJsonValue item in gates)
            {
                string id; int required; bool any;
                if (!Fields(item, "id", "requiredFlags", "any") || !item.TryGetString("id", out id) ||
                    !item.TryGetInt("requiredFlags", out required) || !item.TryGetBool("any", out any) || !ids.Add(id))
                { error = "invalid_gate"; return false; }
                SkyIslandGateDefinition known = Array.Find(expected.Gates, e => e.Id == id);
                if (known == null || (int)known.Required != required || known.Any != any)
                { error = "gate_contract:" + id; return false; }
                parsedGates.Add(new SkyIslandGateDefinition { Id = id, Required = (SkyIslandStoryFlag)required, Any = any });
            }
            result = new SkyIslandContentData { Encounters = parsedEncounters.ToArray(), Gates = parsedGates.ToArray(), Source = "Json" };
            error = null;
            return true;
        }

        /// <summary>档次只认稳定字符串名，不认数字：枚举值将来插入新档不会让旧表悄悄改语义。</summary>
        internal static bool TryParseTier(string value, out SkyIslandEnemyTier tier)
        {
            tier = SkyIslandEnemyTier.Scav;
            if (value == "Scav") return true;
            if (value == "Elite") { tier = SkyIslandEnemyTier.Elite; return true; }
            if (value == "Champion") { tier = SkyIslandEnemyTier.Champion; return true; }
            if (value == "Storm") { tier = SkyIslandEnemyTier.Storm; return true; }
            return false;
        }

        private static bool Fields(BossRushJsonValue value, params string[] names)
        {
            if (value == null || value.Kind != BossRushJsonKind.Object || value.Properties == null || value.Properties.Count != names.Length) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushJsonProperty property in value.Properties)
                if (property == null || Array.IndexOf(names, property.Name) < 0 || !seen.Add(property.Name)) return false;
            return true;
        }

        internal static SkyIslandContentData CreateFallback()
        {
            return new SkyIslandContentData
            {
                Source = "Fallback",
                Encounters = new[]
                {
                    Encounter("C", "EnemySpawn_C", 2), Encounter("C_02", "Search_C_02", 2),
                    // 两处航标守卫各带一名断风游猎：主线目标应当比路上的普通遭遇更有分量。
                    Encounter("D", "EnemySpawn_D", 2, false, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Elite),
                    Encounter("D_02", "Search_D_02", 2),
                    Encounter("E", "EnemySpawn_E", 2), Encounter("E_02", "Search_E_02", 2),
                    Encounter("G", "EnemySpawn_G", 2, false, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Elite),
                    Encounter("G_02", "Search_G_02", 2),
                    Encounter("S1", "EnemySpawn_S1", 2), Encounter("S2", "EnemySpawn_S2", 2),
                    Encounter("S3", "EnemySpawn_S3", 2),
                    Encounter("S4", "EnemySpawn_S4", 3, false, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Elite),
                    Encounter("F", "Search_F_02", 2),
                    // 具名剧情对手保留自己的脸与名字，只吃数值与 AI。
                    Encounter("Zheling", "EnemySpawn_F", 1, true, SkyIslandEnemyTier.Champion, SkyIslandEnemyTier.Champion),
                    Encounter("BellKeeper", "EnemySpawn_H", 3, true, SkyIslandEnemyTier.Scav, SkyIslandEnemyTier.Champion),
                    // 噬风：全图唯一 Boss，双航标点亮后在鸣风栈道可挑战；一次生成 Boss + 两名精英。
                    Encounter("Storm", "POI_E", 3, true, SkyIslandEnemyTier.Elite, SkyIslandEnemyTier.Storm)
                },
                Gates = new[]
                {
                    Gate("K1", SkyIslandStoryFlag.ShortcutK1), Gate("K2", SkyIslandStoryFlag.ShortcutK2),
                    Gate("K3", SkyIslandStoryFlag.ShortcutK3),
                    Gate("BellCourt", SkyIslandStoryFlag.WindBeacon | SkyIslandStoryFlag.StarLamp),
                    Gate("ZhelingPass", SkyIslandStoryFlag.ZhelingReconciled | SkyIslandStoryFlag.ZhelingDefeated, true)
                }
            };
        }
        private static SkyIslandEncounterDefinition Encounter(string id, string marker, int count,
            bool manual = false, SkyIslandEnemyTier tier = SkyIslandEnemyTier.Scav,
            SkyIslandEnemyTier? lead = null)
        {
            return new SkyIslandEncounterDefinition
            {
                Id = id, Marker = marker, Count = count, Manual = manual,
                Tier = tier, Lead = lead.HasValue ? lead.Value : tier
            };
        }
        private static SkyIslandGateDefinition Gate(string id, SkyIslandStoryFlag required, bool any = false)
        {
            return new SkyIslandGateDefinition { Id = id, Required = required, Any = any };
        }
    }
}
