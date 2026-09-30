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
    /// COMPAT：普通巡逻专用内容表。坐标取 collision_navigation.json 岛区三角形中心，运行时不随机选点。
    /// 每个巡逻 owner 入图时 Load 一次并持有结果；本类型不持有缓存、场景、活体或存档，不需要静态清理。
    /// JSON 缺失或结构/身份/数值无效时整表回退；离线几何由 SkyIslandPatrolPlacementPropertyTest 独立复算。
    /// </summary>
    internal static class SkyIslandPatrolRules
    {
        internal const string RelativePath = "Assets/Data/SkyIsland/Patrols.json";
        internal const int Version = 1;
        // 固定坐标的采样来源；与 JSON 一起由定向几何验收核对当前 NAV。
        internal const string SourceNavigationSha256 = "24e7cd204471e41ca7edc4f7da1d4764f0c110e6a8a71a7c1c54fe3f60fb269c";
        // 仅属于普通巡逻。既有 SkyIslandEncounters 的自动组上限和剧情预留保持独立。
        internal const int ActiveLimit = 24;
        internal const float ActivationRadius = 70f;
        internal const float SuspensionRadius = 110f;
        internal const float SpawnInterval = 0.12f;
        internal const float TickInterval = 0.25f;
        internal const float SpawnClearance = 14f, ResidentClearance = 8f, InteractionClearance = 4f, SlotSpacing = 5f;

        internal static int TargetCount(string regionId)
        {
            switch (regionId)
            {
                case "A": return 6;
                case "B": return 8;
                case "C": return 10;
                case "D": case "E": case "F": return 12;
                case "G": case "H": return 14;
                case "S1": case "S2": case "S3": case "S4": return 6;
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
                new SkyIslandPatrolSlot("Patrol_A_01", "A", -50.272553f, 0.0f, -184.370416f),
                new SkyIslandPatrolSlot("Patrol_A_02", "A", 51.480846f, 0.0f, -254.875362f),
                new SkyIslandPatrolSlot("Patrol_A_03", "A", 26.402569f, 0.0f, -182.460127f),
                new SkyIslandPatrolSlot("Patrol_A_04", "A", -24.910386f, 0.0f, -257.920574f),
                new SkyIslandPatrolSlot("Patrol_A_05", "A", -7.786446f, 0.0f, -213.01581f),
                new SkyIslandPatrolSlot("Patrol_A_06", "A", -52.004106f, 0.0f, -230.954734f),
                new SkyIslandPatrolSlot("Patrol_B_01", "B", -60.772565f, 5.0f, -139.170516f),
                new SkyIslandPatrolSlot("Patrol_B_02", "B", 60.635617f, 5.0f, -50.20749f),
                new SkyIslandPatrolSlot("Patrol_B_03", "B", -42.685915f, 5.0f, -44.511769f),
                new SkyIslandPatrolSlot("Patrol_B_04", "B", 40.13661f, 5.0f, -144.233852f),
                new SkyIslandPatrolSlot("Patrol_B_05", "B", -4.521071f, 5.0f, -95.829714f),
                new SkyIslandPatrolSlot("Patrol_B_06", "B", 10.011425f, 5.0f, -45.154996f),
                new SkyIslandPatrolSlot("Patrol_B_07", "B", -58.165418f, 5.0f, -91.137011f),
                new SkyIslandPatrolSlot("Patrol_B_08", "B", -14.561067f, 5.0f, -144.918084f),
                new SkyIslandPatrolSlot("Patrol_C_01", "C", -108.616805f, 10.0f, -133.976071f),
                new SkyIslandPatrolSlot("Patrol_C_02", "C", -225.125429f, 10.0f, -31.385518f),
                new SkyIslandPatrolSlot("Patrol_C_03", "C", -112.88304f, 10.0f, -25.354842f),
                new SkyIslandPatrolSlot("Patrol_C_04", "C", -215.588265f, 10.0f, -134.261449f),
                new SkyIslandPatrolSlot("Patrol_C_05", "C", -163.92304f, 10.0f, -78.267285f),
                new SkyIslandPatrolSlot("Patrol_C_06", "C", -102.945194f, 10.0f, -79.074975f),
                new SkyIslandPatrolSlot("Patrol_C_07", "C", -168.941085f, 10.0f, -24.597312f),
                new SkyIslandPatrolSlot("Patrol_C_08", "C", -227.356073f, 10.0f, -84.332092f),
                new SkyIslandPatrolSlot("Patrol_C_09", "C", -159.111176f, 10.0f, -132.982059f),
                new SkyIslandPatrolSlot("Patrol_C_10", "C", -196.465861f, 10.0f, -55.356779f),
                new SkyIslandPatrolSlot("Patrol_D_01", "D", -210.407797f, 16.0f, 130.753132f),
                new SkyIslandPatrolSlot("Patrol_D_02", "D", -103.686718f, 16.0f, 18.804355f),
                new SkyIslandPatrolSlot("Patrol_D_03", "D", -216.095229f, 16.0f, 19.992537f),
                new SkyIslandPatrolSlot("Patrol_D_04", "D", -102.213823f, 16.0f, 126.096098f),
                new SkyIslandPatrolSlot("Patrol_D_05", "D", -160.934265f, 16.0f, 78.428456f),
                new SkyIslandPatrolSlot("Patrol_D_06", "D", -158.818279f, 16.0f, 16.713751f),
                new SkyIslandPatrolSlot("Patrol_D_07", "D", -155.188624f, 16.0f, 129.016223f),
                new SkyIslandPatrolSlot("Patrol_D_08", "D", -211.848854f, 16.0f, 76.240485f),
                new SkyIslandPatrolSlot("Patrol_D_09", "D", -105.47057f, 16.0f, 75.589448f),
                new SkyIslandPatrolSlot("Patrol_D_10", "D", -186.253509f, 16.0f, 47.777264f),
                new SkyIslandPatrolSlot("Patrol_D_11", "D", -125.718817f, 16.0f, 96.730695f),
                new SkyIslandPatrolSlot("Patrol_D_12", "D", -193.472896f, 16.0f, 103.771339f),
                new SkyIslandPatrolSlot("Patrol_E_01", "E", -37.191723f, 18.0f, 127.327868f),
                new SkyIslandPatrolSlot("Patrol_E_02", "E", 71.089106f, 18.0f, 34.082689f),
                new SkyIslandPatrolSlot("Patrol_E_03", "E", -32.516733f, 18.0f, 30.20052f),
                new SkyIslandPatrolSlot("Patrol_E_04", "E", 61.527176f, 18.0f, 129.927788f),
                new SkyIslandPatrolSlot("Patrol_E_05", "E", 23.499791f, 18.0f, 66.784807f),
                new SkyIslandPatrolSlot("Patrol_E_06", "E", 12.365288f, 18.0f, 127.337077f),
                new SkyIslandPatrolSlot("Patrol_E_07", "E", -40.441524f, 18.0f, 79.24887f),
                new SkyIslandPatrolSlot("Patrol_E_08", "E", 72.533338f, 18.0f, 82.413919f),
                new SkyIslandPatrolSlot("Patrol_E_09", "E", 11.656911f, 18.0f, 29.449964f),
                new SkyIslandPatrolSlot("Patrol_E_10", "E", -16.751088f, 18.0f, 100.676434f),
                new SkyIslandPatrolSlot("Patrol_E_11", "E", 36.852746f, 18.0f, 96.905396f),
                new SkyIslandPatrolSlot("Patrol_E_12", "E", -9.996833f, 18.0f, 65.301999f),
                new SkyIslandPatrolSlot("Patrol_F_01", "F", 174.099868f, 15.0f, -10.373892f),
                new SkyIslandPatrolSlot("Patrol_F_02", "F", 231.047067f, 15.0f, -121.167097f),
                new SkyIslandPatrolSlot("Patrol_F_03", "F", 114.588664f, 15.0f, -111.67994f),
                new SkyIslandPatrolSlot("Patrol_F_04", "F", 237.288863f, 15.0f, -46.470607f),
                new SkyIslandPatrolSlot("Patrol_F_05", "F", 113.756875f, 15.0f, -39.801233f),
                new SkyIslandPatrolSlot("Patrol_F_06", "F", 181.987714f, 15.0f, -85.201284f),
                new SkyIslandPatrolSlot("Patrol_F_07", "F", 220.425656f, 15.0f, -9.167937f),
                new SkyIslandPatrolSlot("Patrol_F_08", "F", 157.280782f, 15.0f, -114.302194f),
                new SkyIslandPatrolSlot("Patrol_F_09", "F", 197.763938f, 15.0f, -44.395666f),
                new SkyIslandPatrolSlot("Patrol_F_10", "F", 131.005943f, 15.0f, -8.550476f),
                new SkyIslandPatrolSlot("Patrol_F_11", "F", 221.202764f, 15.0f, -78.799249f),
                new SkyIslandPatrolSlot("Patrol_F_12", "F", 151.850556f, 15.0f, -40.428577f),
                new SkyIslandPatrolSlot("Patrol_G_01", "G", 219.606251f, 21.0f, 30.273528f),
                new SkyIslandPatrolSlot("Patrol_G_02", "G", 121.345773f, 21.0f, 119.321176f),
                new SkyIslandPatrolSlot("Patrol_G_03", "G", 221.285893f, 21.0f, 123.940281f),
                new SkyIslandPatrolSlot("Patrol_G_04", "G", 129.590123f, 21.0f, 27.607399f),
                new SkyIslandPatrolSlot("Patrol_G_05", "G", 177.854674f, 21.0f, 66.025076f),
                new SkyIslandPatrolSlot("Patrol_G_06", "G", 227.225638f, 21.0f, 75.854039f),
                new SkyIslandPatrolSlot("Patrol_G_07", "G", 170.582411f, 21.0f, 118.711387f),
                new SkyIslandPatrolSlot("Patrol_G_08", "G", 119.232152f, 21.0f, 71.915643f),
                new SkyIslandPatrolSlot("Patrol_G_09", "G", 181.010424f, 21.0f, 24.016356f),
                new SkyIslandPatrolSlot("Patrol_G_10", "G", 146.437559f, 21.0f, 88.587698f),
                new SkyIslandPatrolSlot("Patrol_G_11", "G", 201.341445f, 21.0f, 49.593811f),
                new SkyIslandPatrolSlot("Patrol_G_12", "G", 195.449333f, 21.0f, 105.866667f),
                new SkyIslandPatrolSlot("Patrol_G_13", "G", 230.602871f, 21.0f, 101.571855f),
                new SkyIslandPatrolSlot("Patrol_G_14", "G", 150.452328f, 21.0f, 41.449911f),
                new SkyIslandPatrolSlot("Patrol_H_01", "H", -48.629246f, 26.0f, 272.871545f),
                new SkyIslandPatrolSlot("Patrol_H_02", "H", 63.442396f, 26.0f, 180.403409f),
                new SkyIslandPatrolSlot("Patrol_H_03", "H", -37.321385f, 26.0f, 174.77648f),
                new SkyIslandPatrolSlot("Patrol_H_04", "H", 55.082336f, 26.0f, 275.079794f),
                new SkyIslandPatrolSlot("Patrol_H_05", "H", 15.326958f, 26.0f, 228.190592f),
                new SkyIslandPatrolSlot("Patrol_H_06", "H", 12.933648f, 26.0f, 173.759203f),
                new SkyIslandPatrolSlot("Patrol_H_07", "H", -41.729555f, 26.0f, 222.42304f),
                new SkyIslandPatrolSlot("Patrol_H_08", "H", 53.771531f, 26.0f, 229.146641f),
                new SkyIslandPatrolSlot("Patrol_H_09", "H", 10.912434f, 26.0f, 271.224989f),
                new SkyIslandPatrolSlot("Patrol_H_10", "H", -20.214901f, 26.0f, 249.701156f),
                new SkyIslandPatrolSlot("Patrol_H_11", "H", -11.123299f, 26.0f, 197.268702f),
                new SkyIslandPatrolSlot("Patrol_H_12", "H", 34.863677f, 26.0f, 204.299352f),
                new SkyIslandPatrolSlot("Patrol_H_13", "H", -52.423537f, 26.0f, 247.032021f),
                new SkyIslandPatrolSlot("Patrol_H_14", "H", 25.796635f, 26.0f, 242.6548f),
                new SkyIslandPatrolSlot("Patrol_S1_01", "S1", -263.841712f, 7.0f, -84.927354f),
                new SkyIslandPatrolSlot("Patrol_S1_02", "S1", -316.16176f, 7.0f, -111.018335f),
                new SkyIslandPatrolSlot("Patrol_S1_03", "S1", -280.815404f, 7.0f, -122.687251f),
                new SkyIslandPatrolSlot("Patrol_S1_04", "S1", -316.171187f, 7.0f, -78.271944f),
                new SkyIslandPatrolSlot("Patrol_S1_05", "S1", -290.420568f, 7.0f, -93.644485f),
                new SkyIslandPatrolSlot("Patrol_S1_06", "S1", -263.065607f, 7.0f, -111.515304f),
                new SkyIslandPatrolSlot("Patrol_S2_01", "S2", -256.785371f, 13.0f, 112.855237f),
                new SkyIslandPatrolSlot("Patrol_S2_02", "S2", -300.000563f, 13.0f, 78.072602f),
                new SkyIslandPatrolSlot("Patrol_S2_03", "S2", -298.52853f, 13.0f, 116.821059f),
                new SkyIslandPatrolSlot("Patrol_S2_04", "S2", -259.82331f, 13.0f, 80.300231f),
                new SkyIslandPatrolSlot("Patrol_S2_05", "S2", -280.979168f, 13.0f, 97.394487f),
                new SkyIslandPatrolSlot("Patrol_S2_06", "S2", -301.125971f, 13.0f, 100.606802f),
                new SkyIslandPatrolSlot("Patrol_S3_01", "S3", 332.328577f, 10.0f, -37.363011f),
                new SkyIslandPatrolSlot("Patrol_S3_02", "S3", 275.704542f, 10.0f, -93.886346f),
                new SkyIslandPatrolSlot("Patrol_S3_03", "S3", 274.71219f, 10.0f, -36.13269f),
                new SkyIslandPatrolSlot("Patrol_S3_04", "S3", 329.617475f, 10.0f, -87.898156f),
                new SkyIslandPatrolSlot("Patrol_S3_05", "S3", 306.349285f, 10.0f, -62.059287f),
                new SkyIslandPatrolSlot("Patrol_S3_06", "S3", 272.081602f, 10.0f, -64.440573f),
                new SkyIslandPatrolSlot("Patrol_S4_01", "S4", 323.057333f, 26.0f, 104.778706f),
                new SkyIslandPatrolSlot("Patrol_S4_02", "S4", 267.054462f, 26.0f, 47.432423f),
                new SkyIslandPatrolSlot("Patrol_S4_03", "S4", 267.923741f, 26.0f, 103.616842f),
                new SkyIslandPatrolSlot("Patrol_S4_04", "S4", 321.054891f, 26.0f, 48.28255f),
                new SkyIslandPatrolSlot("Patrol_S4_05", "S4", 296.63791f, 26.0f, 69.500729f),
                new SkyIslandPatrolSlot("Patrol_S4_06", "S4", 264.764725f, 26.0f, 75.566868f),
            }, "Fallback");
        }
    }
}
