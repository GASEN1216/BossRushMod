using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BossRush
{
    /// <summary>普通巡逻的岛区数值；只应用到巡逻 owner 的克隆 preset，不写既有头目与剧情组。</summary>
    internal sealed class SkyIslandPatrolProfile
    {
        internal readonly string RegionId;
        internal readonly int Rank;
        internal readonly float HealthFactor, DamageFactor, SightDistance, ReactionTime;

        internal SkyIslandPatrolProfile(string regionId, int rank, float healthFactor, float damageFactor,
            float sightDistance, float reactionTime)
        {
            RegionId = regionId; Rank = rank; HealthFactor = healthFactor; DamageFactor = damageFactor;
            SightDistance = sightDistance; ReactionTime = reactionTime;
        }
    }

    /// <summary>作者地图局部 Unity XYZ 固定落点；Y 是导航面高度，生成时的抬脚由巡逻 owner 统一处理。</summary>
    internal sealed class SkyIslandPatrolSlot
    {
        internal readonly string Id, RegionId;
        internal readonly float X, Y, Z;
        internal SkyIslandPatrolSlot(string id, string regionId, float x, float y, float z)
        { Id = id; RegionId = regionId; X = x; Y = y; Z = z; }
    }

    internal sealed class SkyIslandPatrolData
    {
        internal readonly SkyIslandPatrolProfile[] Profiles;
        internal readonly SkyIslandPatrolSlot[] Slots;
        internal readonly string Source;
        internal SkyIslandPatrolData(SkyIslandPatrolProfile[] profiles, SkyIslandPatrolSlot[] slots, string source)
        { Profiles = profiles; Slots = slots; Source = source; }

        internal SkyIslandPatrolProfile FindProfile(string regionId)
        {
            foreach (SkyIslandPatrolProfile profile in Profiles)
                if (string.Equals(profile.RegionId, regionId, StringComparison.Ordinal)) return profile;
            return null;
        }
    }

    /// <summary>
    /// COMPAT：普通巡逻专用内容表。坐标取 collision_navigation.json 岛区三角形中心，运行时不随机选点；
    /// 选点（tools/sky_island_patrol_slots.py）只取离岛缘够远、靠路网 / 桥头 / 地标、不贴墙的中心，判据在属性测试里。
    /// 每个巡逻 owner 入图时 Load 一次并持有结果；本类型不持有缓存、场景、活体或存档，不需要静态清理。
    /// JSON 缺失或结构/身份/数值无效时整表回退；离线几何由 SkyIslandPatrolPlacementPropertyTest 独立复算。
    /// </summary>
    internal static class SkyIslandPatrolRules
    {
        internal const string RelativePath = "Assets/Data/SkyIsland/Patrols.json";
        internal const int Version = 1;
        // 固定坐标的采样来源；与 JSON 一起由定向几何验收核对当前 NAV。
        internal const string SourceNavigationSha256 = "dc446f697e2eb6846b7b73dad499ab01e14877c10908abe9382767dc97605d4f";
        // 仅属于普通巡逻。既有 SkyIslandEncounters 的自动组上限和剧情预留保持独立。
        // 2026-10-01 加密后：全图任一点 70 m 内最多 26 个槽位（残星工坊腹地），32 = 26 + 6 个近远滞回余量；
        // 挂起半径从 110 m 收到 95 m，走过桥后上一座岛的活体早点让出名额（离线路线复算：56 m 内从不因预算缺人）。
        internal const int ActiveLimit = 32;
        internal const float ActivationRadius = 70f;
        internal const float SuspensionRadius = 95f;
        internal const float SpawnInterval = 0.12f;
        internal const float TickInterval = 0.25f;
        // 居民 35 m（码头 45 m）：与会话的剧情面板 / 写盘安静半径同口径，离线选点由 SkyIslandPatrolPlacementPropertyTest 复算（发布前审查 P2）。
        internal const float SpawnClearance = 14f, ResidentClearance = 35f, DockResidentClearance = 45f, InteractionClearance = 4f, SlotSpacing = 5f;

        // owner 2026-10-01 实机反馈「密度不够」：主岛约翻倍；钟庭与小岛按合格候选的面积加（钟庭只有一条路、听雨洞最窄）。
        internal static int TargetCount(string regionId)
        {
            switch (regionId)
            {
                case "A": return 10;
                case "B": return 14;
                case "C": return 18;
                case "D": case "E": case "F": return 22;
                case "G": return 26;
                case "H": return 9;
                case "S1": case "S2": return 8;
                case "S3": return 7;
                case "S4": return 10;
                default: return 0;
            }
        }

        /// <summary>
        /// 调用方明确传入 Mod 根目录。只做文件读取，继续复用共享 JSON token/parser；不依赖 Unity 或宿主。
        /// 缺失/无效时返回 Source=Fallback，由 Runtime 决定如何报告。岛区门控也由 Runtime 持有：
        /// 只有 H 需要 content.IsGateOpen("BellCourt", story)，不把场景/剧情状态引入纯规则。
        /// </summary>
        internal static SkyIslandPatrolData Load(string modPath)
        {
            if (string.IsNullOrEmpty(modPath)) return CreateFallback();
            try
            {
                string path = Path.Combine(modPath, "Assets", "Data", "SkyIsland", "Patrols.json");
                if (!File.Exists(path)) return CreateFallback();
                string raw = File.ReadAllText(path, Encoding.UTF8);
                SkyIslandPatrolData result;
                string error;
                if (TryParse(raw, out result, out error)) return result;
            }
            catch (Exception)
            {
                // 路径、权限与部署错误均回退；不在纯数据层引用游戏日志或吞掉整份巡逻内容。
            }
            return CreateFallback();
        }

        internal static bool TryParse(string raw, out SkyIslandPatrolData result, out string error)
        {
            result = null;
            error = "invalid_root";
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            string navigationSha256;
            List<BossRushJsonValue> profileItems, slotItems;
            if (!Fields(root, "version", "sourceNavigationSha256", "profiles", "slots") || !root.TryGetInt("version", out version) || version != Version ||
                !root.TryGetArray("profiles", out profileItems) || !root.TryGetArray("slots", out slotItems)) return false;
            if (!root.TryGetString("sourceNavigationSha256", out navigationSha256) || navigationSha256 != SourceNavigationSha256)
            { error = "source_navigation_sha256"; return false; }
            SkyIslandPatrolData expected = CreateFallback();
            if (profileItems.Count != expected.Profiles.Length || slotItems.Count != expected.Slots.Length)
            { error = "incomplete_content"; return false; }
            var profiles = new List<SkyIslandPatrolProfile>();
            var regions = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushJsonValue item in profileItems)
            {
                string region; int rank; float hp, damage, sight, reaction;
                if (!Fields(item, "regionId", "rank", "healthFactor", "damageFactor", "sightDistance", "reactionTime") ||
                    !item.TryGetString("regionId", out region) || !item.TryGetInt("rank", out rank) || !regions.Add(region) ||
                    !item.TryGetFloat("healthFactor", out hp) || !item.TryGetFloat("damageFactor", out damage) ||
                    !item.TryGetFloat("sightDistance", out sight) || !item.TryGetFloat("reactionTime", out reaction) ||
                    !InRange(hp, 0.1f, 10f) || !InRange(damage, 0.1f, 5f) ||
                    !InRange(sight, 1f, 80f) || !InRange(reaction, 0.1f, 10f))
                { error = "invalid_profile"; return false; }
                SkyIslandPatrolProfile known = expected.FindProfile(region);
                if (known == null || rank != known.Rank) { error = "region_rank:" + region; return false; }
                profiles.Add(new SkyIslandPatrolProfile(region, rank, hp, damage, sight, reaction));
            }
            foreach (SkyIslandPatrolProfile a in profiles)
                foreach (SkyIslandPatrolProfile b in profiles)
                {
                    if ((a.Rank < b.Rank && (a.HealthFactor >= b.HealthFactor || a.DamageFactor >= b.DamageFactor ||
                            a.SightDistance > b.SightDistance || a.ReactionTime < b.ReactionTime)) ||
                        (a.Rank == b.Rank && (a.HealthFactor != b.HealthFactor || a.DamageFactor != b.DamageFactor ||
                            a.SightDistance != b.SightDistance || a.ReactionTime != b.ReactionTime)) ||
                        (a.Rank <= 3 && (a.SightDistance < 8f || a.SightDistance > 12f)))
                    { error = "difficulty_order"; return false; }
                }
            var slots = new List<SkyIslandPatrolSlot>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BossRushJsonValue item in slotItems)
            {
                string id, region; float x, y, z;
                if (!Fields(item, "id", "regionId", "x", "y", "z") || !item.TryGetString("id", out id) ||
                    !item.TryGetString("regionId", out region) || !regions.Contains(region) || !ids.Add(id) ||
                    !item.TryGetFloat("x", out x) || !item.TryGetFloat("y", out y) || !item.TryGetFloat("z", out z) ||
                    !InRange(x, -475f, 475f) || !InRange(y, -8f, 100f) || !InRange(z, -425f, 425f))
                { error = "invalid_slot"; return false; }
                bool known = false;
                foreach (SkyIslandPatrolSlot stable in expected.Slots)
                    if (stable.Id == id && stable.RegionId == region) { known = true; break; }
                if (!known) { error = "slot_binding:" + id; return false; }
                foreach (SkyIslandPatrolSlot other in slots)
                {
                    if (other.RegionId != region) continue;
                    double dx = (double)x - other.X, dz = (double)z - other.Z;
                    if (dx * dx + dz * dz < SlotSpacing * SlotSpacing)
                    { error = "slot_spacing:" + id; return false; }
                }
                int count;
                counts.TryGetValue(region, out count); counts[region] = count + 1;
                slots.Add(new SkyIslandPatrolSlot(id, region, x, y, z));
            }
            foreach (string region in regions)
                if (!counts.ContainsKey(region) || counts[region] != TargetCount(region))
                { error = "slot_count:" + region; return false; }
            result = new SkyIslandPatrolData(profiles.ToArray(), slots.ToArray(), "JSON");
            error = null;
            return true;
        }

        private static bool InRange(float value, float min, float max)
        { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max; }

        private static bool Fields(BossRushJsonValue value, params string[] names)
        {
            if (value == null || value.Kind != BossRushJsonKind.Object || value.Properties == null || value.Properties.Count != names.Length)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushJsonProperty property in value.Properties)
                if (property == null || Array.IndexOf(names, property.Name) < 0 || !seen.Add(property.Name)) return false;
            return true;
        }

        internal static SkyIslandPatrolData CreateFallback()
        {
            return new SkyIslandPatrolData(new[]
            {
                new SkyIslandPatrolProfile("A", 1, 0.8f, 0.7f, 8.0f, 1.2f),
                new SkyIslandPatrolProfile("B", 2, 1.0f, 0.8f, 10.0f, 1.1f),
                new SkyIslandPatrolProfile("C", 3, 1.2f, 0.9f, 12.0f, 1.0f),
                new SkyIslandPatrolProfile("D", 4, 1.45f, 1.0f, 14.0f, 0.9f),
                new SkyIslandPatrolProfile("E", 5, 1.7f, 1.1f, 16.0f, 0.8f),
                new SkyIslandPatrolProfile("F", 6, 2.0f, 1.2f, 18.0f, 0.7f),
                new SkyIslandPatrolProfile("G", 7, 2.3f, 1.3f, 20.0f, 0.6f),
                new SkyIslandPatrolProfile("H", 8, 2.6f, 1.35f, 22.0f, 0.5f),
                new SkyIslandPatrolProfile("S1", 3, 1.2f, 0.9f, 12.0f, 1.0f),
                new SkyIslandPatrolProfile("S2", 4, 1.45f, 1.0f, 14.0f, 0.9f),
                new SkyIslandPatrolProfile("S3", 6, 2.0f, 1.2f, 18.0f, 0.7f),
                new SkyIslandPatrolProfile("S4", 7, 2.3f, 1.3f, 20.0f, 0.6f),
            }, new[]
            {
                new SkyIslandPatrolSlot("Patrol_A_01", "A", -39.109582f, 0.0f, -242.361088f),
                new SkyIslandPatrolSlot("Patrol_A_02", "A", 32.924632f, 0.0f, -195.426371f),
                new SkyIslandPatrolSlot("Patrol_A_03", "A", 15.057139f, 0.0f, -243.086236f),
                new SkyIslandPatrolSlot("Patrol_A_04", "A", 38.210101f, 0.0f, -223.220314f),
                new SkyIslandPatrolSlot("Patrol_A_05", "A", 41.494667f, 0.0f, -208.642863f),
                new SkyIslandPatrolSlot("Patrol_A_06", "A", -26.399556f, 0.0f, -248.505666f),
                new SkyIslandPatrolSlot("Patrol_A_07", "A", 29.066782f, 0.0f, -227.250534f),
                new SkyIslandPatrolSlot("Patrol_A_08", "A", 39.511126f, 0.0f, -191.906846f),
                new SkyIslandPatrolSlot("Patrol_A_09", "A", -32.348636f, 0.0f, -245.50016f),
                new SkyIslandPatrolSlot("Patrol_A_10", "A", -39.336899f, 0.0f, -248.795605f),
                new SkyIslandPatrolSlot("Patrol_B_01", "B", 48.41578f, 5.0f, -83.679694f),
                new SkyIslandPatrolSlot("Patrol_B_02", "B", -48.560817f, 5.0f, -88.672603f),
                new SkyIslandPatrolSlot("Patrol_B_03", "B", -38.435985f, 5.0f, -131.030889f),
                new SkyIslandPatrolSlot("Patrol_B_04", "B", 24.685151f, 5.0f, -61.36804f),
                new SkyIslandPatrolSlot("Patrol_B_05", "B", -40.505238f, 5.0f, -54.972165f),
                new SkyIslandPatrolSlot("Patrol_B_06", "B", 52.700948f, 5.0f, -126.273192f),
                new SkyIslandPatrolSlot("Patrol_B_07", "B", -21.301525f, 5.0f, -112.279813f),
                new SkyIslandPatrolSlot("Patrol_B_08", "B", -15.157441f, 5.0f, -133.861965f),
                new SkyIslandPatrolSlot("Patrol_B_09", "B", 47.045293f, 5.0f, -59.378082f),
                new SkyIslandPatrolSlot("Patrol_B_10", "B", -51.425284f, 5.0f, -115.083751f),
                new SkyIslandPatrolSlot("Patrol_B_11", "B", -52.95003f, 5.0f, -71.138921f),
                new SkyIslandPatrolSlot("Patrol_B_12", "B", -39.313318f, 5.0f, -103.607446f),
                new SkyIslandPatrolSlot("Patrol_B_13", "B", 43.626816f, 5.0f, -72.081141f),
                new SkyIslandPatrolSlot("Patrol_B_14", "B", -48.562523f, 5.0f, -128.712991f),
                new SkyIslandPatrolSlot("Patrol_C_01", "C", -144.543628f, 10.0f, -83.752577f),
                new SkyIslandPatrolSlot("Patrol_C_02", "C", -173.275591f, 10.0f, -48.554148f),
                new SkyIslandPatrolSlot("Patrol_C_03", "C", -215.701593f, 10.0f, -100.916672f),
                new SkyIslandPatrolSlot("Patrol_C_04", "C", -184.827607f, 10.0f, -87.760647f),
                new SkyIslandPatrolSlot("Patrol_C_05", "C", -115.617124f, 10.0f, -92.960861f),
                new SkyIslandPatrolSlot("Patrol_C_06", "C", -142.85323f, 10.0f, -53.354706f),
                new SkyIslandPatrolSlot("Patrol_C_07", "C", -191.264716f, 10.0f, -116.599106f),
                new SkyIslandPatrolSlot("Patrol_C_08", "C", -116.836042f, 10.0f, -41.154593f),
                new SkyIslandPatrolSlot("Patrol_C_09", "C", -199.812364f, 10.0f, -56.211231f),
                new SkyIslandPatrolSlot("Patrol_C_10", "C", -155.187871f, 10.0f, -108.566192f),
                new SkyIslandPatrolSlot("Patrol_C_11", "C", -164.131673f, 10.0f, -69.773551f),
                new SkyIslandPatrolSlot("Patrol_C_12", "C", -112.938632f, 10.0f, -116.218563f),
                new SkyIslandPatrolSlot("Patrol_C_13", "C", -117.104208f, 10.0f, -70.034599f),
                new SkyIslandPatrolSlot("Patrol_C_14", "C", -154.527518f, 10.0f, -34.538971f),
                new SkyIslandPatrolSlot("Patrol_C_15", "C", -204.883579f, 10.0f, -79.8768f),
                new SkyIslandPatrolSlot("Patrol_C_16", "C", -137.695951f, 10.0f, -100.568461f),
                new SkyIslandPatrolSlot("Patrol_C_17", "C", -181.703824f, 10.0f, -67.545735f),
                new SkyIslandPatrolSlot("Patrol_C_18", "C", -188.49151f, 10.0f, -40.261869f),
                new SkyIslandPatrolSlot("Patrol_D_01", "D", -111.235959f, 16.0f, 50.703418f),
                new SkyIslandPatrolSlot("Patrol_D_02", "D", -209.934265f, 16.0f, 100.444349f),
                new SkyIslandPatrolSlot("Patrol_D_03", "D", -187.273847f, 16.0f, 70.638931f),
                new SkyIslandPatrolSlot("Patrol_D_04", "D", -107.892057f, 16.0f, 85.818327f),
                new SkyIslandPatrolSlot("Patrol_D_05", "D", -154.947391f, 16.0f, 40.126959f),
                new SkyIslandPatrolSlot("Patrol_D_06", "D", -190.703858f, 16.0f, 40.482493f),
                new SkyIslandPatrolSlot("Patrol_D_07", "D", -168.041265f, 16.0f, 59.350195f),
                new SkyIslandPatrolSlot("Patrol_D_08", "D", -206.823825f, 16.0f, 76.390147f),
                new SkyIslandPatrolSlot("Patrol_D_09", "D", -119.773966f, 16.0f, 69.04365f),
                new SkyIslandPatrolSlot("Patrol_D_10", "D", -113.678457f, 16.0f, 104.584632f),
                new SkyIslandPatrolSlot("Patrol_D_11", "D", -200.916478f, 16.0f, 55.747435f),
                new SkyIslandPatrolSlot("Patrol_D_12", "D", -138.058969f, 16.0f, 37.840994f),
                new SkyIslandPatrolSlot("Patrol_D_13", "D", -176.157339f, 16.0f, 31.81114f),
                new SkyIslandPatrolSlot("Patrol_D_14", "D", -202.904776f, 16.0f, 115.76118f),
                new SkyIslandPatrolSlot("Patrol_D_15", "D", -110.769213f, 16.0f, 34.728109f),
                new SkyIslandPatrolSlot("Patrol_D_16", "D", -178.214476f, 16.0f, 48.935331f),
                new SkyIslandPatrolSlot("Patrol_D_17", "D", -156.552235f, 16.0f, 55.489714f),
                new SkyIslandPatrolSlot("Patrol_D_18", "D", -198.497335f, 16.0f, 98.05294f),
                new SkyIslandPatrolSlot("Patrol_D_19", "D", -108.994699f, 16.0f, 61.571161f),
                new SkyIslandPatrolSlot("Patrol_D_20", "D", -165.928726f, 16.0f, 38.232682f),
                new SkyIslandPatrolSlot("Patrol_D_21", "D", -118.99488f, 16.0f, 58.787878f),
                new SkyIslandPatrolSlot("Patrol_D_22", "D", -152.929432f, 16.0f, 30.178055f),
                new SkyIslandPatrolSlot("Patrol_E_01", "E", 25.712676f, 18.0f, 80.185135f),
                new SkyIslandPatrolSlot("Patrol_E_02", "E", -11.500686f, 18.0f, 81.455816f),
                new SkyIslandPatrolSlot("Patrol_E_03", "E", 4.061993f, 18.0f, 115.754411f),
                new SkyIslandPatrolSlot("Patrol_E_04", "E", -2.8496f, 18.0f, 52.554104f),
                new SkyIslandPatrolSlot("Patrol_E_05", "E", 57.034607f, 18.0f, 86.717535f),
                new SkyIslandPatrolSlot("Patrol_E_06", "E", 44.939006f, 18.0f, 116.906654f),
                new SkyIslandPatrolSlot("Patrol_E_07", "E", 43.254226f, 18.0f, 59.939343f),
                new SkyIslandPatrolSlot("Patrol_E_08", "E", 25.810687f, 18.0f, 104.645784f),
                new SkyIslandPatrolSlot("Patrol_E_09", "E", 20.218393f, 18.0f, 58.400396f),
                new SkyIslandPatrolSlot("Patrol_E_10", "E", -24.748399f, 18.0f, 54.377564f),
                new SkyIslandPatrolSlot("Patrol_E_11", "E", -28.733333f, 18.0f, 73.1f),
                new SkyIslandPatrolSlot("Patrol_E_12", "E", -14.941206f, 18.0f, 118.323288f),
                new SkyIslandPatrolSlot("Patrol_E_13", "E", -25.276604f, 18.0f, 93.421501f),
                new SkyIslandPatrolSlot("Patrol_E_14", "E", 4.559026f, 18.0f, 89.95833f),
                new SkyIslandPatrolSlot("Patrol_E_15", "E", 7.146495f, 18.0f, 70.566434f),
                new SkyIslandPatrolSlot("Patrol_E_16", "E", 43.97757f, 18.0f, 99.262586f),
                new SkyIslandPatrolSlot("Patrol_E_17", "E", 10.508756f, 18.0f, 42.216209f),
                new SkyIslandPatrolSlot("Patrol_E_18", "E", 41.12277f, 18.0f, 79.455485f),
                new SkyIslandPatrolSlot("Patrol_E_19", "E", 54.031832f, 18.0f, 71.964125f),
                new SkyIslandPatrolSlot("Patrol_E_20", "E", 11.83403f, 18.0f, 103.913306f),
                new SkyIslandPatrolSlot("Patrol_E_21", "E", -4.562545f, 18.0f, 100.484919f),
                new SkyIslandPatrolSlot("Patrol_E_22", "E", 57.972484f, 18.0f, 117.324645f),
                new SkyIslandPatrolSlot("Patrol_F_01", "F", 130.274408f, 15.0f, -91.368957f),
                new SkyIslandPatrolSlot("Patrol_F_02", "F", 145.291287f, 15.0f, -53.184794f),
                new SkyIslandPatrolSlot("Patrol_F_03", "F", 213.218675f, 15.0f, -46.727043f),
                new SkyIslandPatrolSlot("Patrol_F_04", "F", 179.413759f, 15.0f, -42.903009f),
                new SkyIslandPatrolSlot("Patrol_F_05", "F", 146.772776f, 15.0f, -22.587081f),
                new SkyIslandPatrolSlot("Patrol_F_06", "F", 168.446826f, 15.0f, -106.820943f),
                new SkyIslandPatrolSlot("Patrol_F_07", "F", 227.986976f, 15.0f, -98.53656f),
                new SkyIslandPatrolSlot("Patrol_F_08", "F", 220.68227f, 15.0f, -20.700381f),
                new SkyIslandPatrolSlot("Patrol_F_09", "F", 153.33493f, 15.0f, -82.826591f),
                new SkyIslandPatrolSlot("Patrol_F_10", "F", 123.120072f, 15.0f, -67.593725f),
                new SkyIslandPatrolSlot("Patrol_F_11", "F", 124.156662f, 15.0f, -21.680787f),
                new SkyIslandPatrolSlot("Patrol_F_12", "F", 148.43261f, 15.0f, -103.953764f),
                new SkyIslandPatrolSlot("Patrol_F_13", "F", 127.094021f, 15.0f, -41.033284f),
                new SkyIslandPatrolSlot("Patrol_F_14", "F", 217.305463f, 15.0f, -112.348057f),
                new SkyIslandPatrolSlot("Patrol_F_15", "F", 158.465105f, 15.0f, -34.599455f),
                new SkyIslandPatrolSlot("Patrol_F_16", "F", 129.434394f, 15.0f, -107.224902f),
                new SkyIslandPatrolSlot("Patrol_F_17", "F", 142.625626f, 15.0f, -68.151492f),
                new SkyIslandPatrolSlot("Patrol_F_18", "F", 227.839916f, 15.0f, -33.610433f),
                new SkyIslandPatrolSlot("Patrol_F_19", "F", 213.732819f, 15.0f, -32.760739f),
                new SkyIslandPatrolSlot("Patrol_F_20", "F", 132.540725f, 15.0f, -56.137804f),
                new SkyIslandPatrolSlot("Patrol_F_21", "F", 138.598699f, 15.0f, -35.532218f),
                new SkyIslandPatrolSlot("Patrol_F_22", "F", 142.643366f, 15.0f, -89.581699f),
                new SkyIslandPatrolSlot("Patrol_G_01", "G", 175.266965f, 21.0f, 54.608029f),
                new SkyIslandPatrolSlot("Patrol_G_02", "G", 216.748931f, 21.0f, 74.72286f),
                new SkyIslandPatrolSlot("Patrol_G_03", "G", 145.965702f, 21.0f, 81.21242f),
                new SkyIslandPatrolSlot("Patrol_G_04", "G", 136.097873f, 21.0f, 49.734169f),
                new SkyIslandPatrolSlot("Patrol_G_05", "G", 205.261805f, 21.0f, 111.06186f),
                new SkyIslandPatrolSlot("Patrol_G_06", "G", 203.821331f, 21.0f, 36.377534f),
                new SkyIslandPatrolSlot("Patrol_G_07", "G", 163.656184f, 21.0f, 107.459884f),
                new SkyIslandPatrolSlot("Patrol_G_08", "G", 130.452334f, 21.0f, 108.938648f),
                new SkyIslandPatrolSlot("Patrol_G_09", "G", 156.662568f, 21.0f, 62.550132f),
                new SkyIslandPatrolSlot("Patrol_G_10", "G", 189.12f, 21.0f, 69.533333f),
                new SkyIslandPatrolSlot("Patrol_G_11", "G", 213.741203f, 21.0f, 53.756545f),
                new SkyIslandPatrolSlot("Patrol_G_12", "G", 170.143377f, 21.0f, 35.624038f),
                new SkyIslandPatrolSlot("Patrol_G_13", "G", 166.531667f, 21.0f, 89.7f),
                new SkyIslandPatrolSlot("Patrol_G_14", "G", 153.479075f, 21.0f, 41.987181f),
                new SkyIslandPatrolSlot("Patrol_G_15", "G", 214.465788f, 21.0f, 96.852068f),
                new SkyIslandPatrolSlot("Patrol_G_16", "G", 187.28623f, 21.0f, 43.827174f),
                new SkyIslandPatrolSlot("Patrol_G_17", "G", 137.458423f, 21.0f, 65.512275f),
                new SkyIslandPatrolSlot("Patrol_G_18", "G", 135.990868f, 21.0f, 94.409614f),
                new SkyIslandPatrolSlot("Patrol_G_19", "G", 130.913766f, 21.0f, 80.044428f),
                new SkyIslandPatrolSlot("Patrol_G_20", "G", 132.216289f, 21.0f, 35.334153f),
                new SkyIslandPatrolSlot("Patrol_G_21", "G", 203.116f, 21.0f, 66.866667f),
                new SkyIslandPatrolSlot("Patrol_G_22", "G", 177.121783f, 21.0f, 105.195298f),
                new SkyIslandPatrolSlot("Patrol_G_23", "G", 193.326101f, 21.0f, 55.349177f),
                new SkyIslandPatrolSlot("Patrol_G_24", "G", 207.693348f, 21.0f, 85.350873f),
                new SkyIslandPatrolSlot("Patrol_G_25", "G", 163.503262f, 21.0f, 50.106821f),
                new SkyIslandPatrolSlot("Patrol_G_26", "G", 168.265292f, 21.0f, 65.653886f),
                new SkyIslandPatrolSlot("Patrol_H_01", "H", 37.704341f, 26.0f, 260.609826f),
                new SkyIslandPatrolSlot("Patrol_H_02", "H", -33.139477f, 26.0f, 197.144262f),
                new SkyIslandPatrolSlot("Patrol_H_03", "H", -31.448425f, 26.0f, 223.189706f),
                new SkyIslandPatrolSlot("Patrol_H_04", "H", 39.419165f, 26.0f, 242.2548f),
                new SkyIslandPatrolSlot("Patrol_H_05", "H", 50.48693f, 26.0f, 259.003168f),
                new SkyIslandPatrolSlot("Patrol_H_06", "H", -40.297247f, 26.0f, 205.353347f),
                new SkyIslandPatrolSlot("Patrol_H_07", "H", 47.578339f, 26.0f, 248.677819f),
                new SkyIslandPatrolSlot("Patrol_H_08", "H", -42.658047f, 26.0f, 198.300167f),
                new SkyIslandPatrolSlot("Patrol_H_09", "H", -36.061195f, 26.0f, 219.079413f),
                new SkyIslandPatrolSlot("Patrol_S1_01", "S1", -272.10187f, 7.0f, -99.423765f),
                new SkyIslandPatrolSlot("Patrol_S1_02", "S1", -307.577195f, 7.0f, -110.949377f),
                new SkyIslandPatrolSlot("Patrol_S1_03", "S1", -298.419086f, 7.0f, -87.138165f),
                new SkyIslandPatrolSlot("Patrol_S1_04", "S1", -275.701229f, 7.0f, -113.520557f),
                new SkyIslandPatrolSlot("Patrol_S1_05", "S1", -288.939834f, 7.0f, -95.865356f),
                new SkyIslandPatrolSlot("Patrol_S1_06", "S1", -308.411568f, 7.0f, -95.386761f),
                new SkyIslandPatrolSlot("Patrol_S1_07", "S1", -271.116209f, 7.0f, -89.637925f),
                new SkyIslandPatrolSlot("Patrol_S1_08", "S1", -288.810095f, 7.0f, -87.884262f),
                new SkyIslandPatrolSlot("Patrol_S2_01", "S2", -281.020039f, 13.0f, 97.63886f),
                new SkyIslandPatrolSlot("Patrol_S2_02", "S2", -299.801625f, 13.0f, 116.953968f),
                new SkyIslandPatrolSlot("Patrol_S2_03", "S2", -267.51414f, 13.0f, 114.966848f),
                new SkyIslandPatrolSlot("Patrol_S2_04", "S2", -267.066782f, 13.0f, 83.112149f),
                new SkyIslandPatrolSlot("Patrol_S2_05", "S2", -265.721548f, 13.0f, 101.210308f),
                new SkyIslandPatrolSlot("Patrol_S2_06", "S2", -281.586706f, 13.0f, 89.066365f),
                new SkyIslandPatrolSlot("Patrol_S2_07", "S2", -277.26693f, 13.0f, 103.703699f),
                new SkyIslandPatrolSlot("Patrol_S2_08", "S2", -274.583261f, 13.0f, 96.81093f),
                new SkyIslandPatrolSlot("Patrol_S3_01", "S3", 288.972929f, 10.0f, -68.04983f),
                new SkyIslandPatrolSlot("Patrol_S3_02", "S3", 323.346463f, 10.0f, -85.714561f),
                new SkyIslandPatrolSlot("Patrol_S3_03", "S3", 280.305194f, 10.0f, -47.3621f),
                new SkyIslandPatrolSlot("Patrol_S3_04", "S3", 280.94565f, 10.0f, -86.720153f),
                new SkyIslandPatrolSlot("Patrol_S3_05", "S3", 306.891807f, 10.0f, -61.888869f),
                new SkyIslandPatrolSlot("Patrol_S3_06", "S3", 322.681606f, 10.0f, -70.05806f),
                new SkyIslandPatrolSlot("Patrol_S3_07", "S3", 297.87317f, 10.0f, -53.721481f),
                new SkyIslandPatrolSlot("Patrol_S4_01", "S4", 281.472925f, 26.0f, 78.3284f),
                new SkyIslandPatrolSlot("Patrol_S4_02", "S4", 311.579689f, 26.0f, 54.888873f),
                new SkyIslandPatrolSlot("Patrol_S4_03", "S4", 315.288187f, 26.0f, 93.864585f),
                new SkyIslandPatrolSlot("Patrol_S4_04", "S4", 275.108525f, 26.0f, 55.551036f),
                new SkyIslandPatrolSlot("Patrol_S4_05", "S4", 272.835532f, 26.0f, 95.222818f),
                new SkyIslandPatrolSlot("Patrol_S4_06", "S4", 310.762518f, 26.0f, 76.431881f),
                new SkyIslandPatrolSlot("Patrol_S4_07", "S4", 292.549088f, 26.0f, 59.99954f),
                new SkyIslandPatrolSlot("Patrol_S4_08", "S4", 306.252835f, 26.0f, 64.979435f),
                new SkyIslandPatrolSlot("Patrol_S4_09", "S4", 296.644546f, 26.0f, 69.466234f),
                new SkyIslandPatrolSlot("Patrol_S4_10", "S4", 281.357491f, 26.0f, 87.597204f),
            }, "Fallback");
        }
    }
}
